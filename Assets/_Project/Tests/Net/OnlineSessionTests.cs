using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Net.Tests
{
    /// <summary>
    /// OnlineSession を、同じプロセスの中で、本当の通信（127.0.0.1）を通して複数動かす（docs/10_online_plan.md T2-7）。
    /// 全員が入力を送り合い、同じ状態になること、ホストがチェックサムを比べて不一致がないことを確かめる。
    /// </summary>
    public sealed class OnlineSessionTests
    {
        private const int TimeoutMs = 20000;
        private const float FrameSeconds = 1f / 60f;
        private static readonly Vector3 SpawnBase = new Vector3(0f, 1f, 0f);

        private readonly List<OnlineSession> _sessions = new List<OnlineSession>();
        private BoxTerrain _terrain;
        private OnlineStage _stage;
        private Func<int, int, PlayerInput> _input;

        [SetUp]
        public void SetUp()
        {
            _terrain = TestArenas.Playground();
            _stage = null;
            _input = Scripted;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (OnlineSession session in _sessions)
            {
                session.Dispose();
            }
            _sessions.Clear();
        }

        [Test]
        public void ThreePlayers_StayInSyncOverLoopback()
        {
            const int ticks = 360;
            OnlineSession host = Host();
            OnlineSession client1 = Join(host);
            OnlineSession client2 = Join(host);
            PumpUntil(() => client1.LocalPlayer >= 0 && client2.LocalPlayer >= 0);
            Assert.That(host.LobbyPlayerCount, Is.EqualTo(3));
            Assert.That(new[] { client1.LocalPlayer, client2.LocalPlayer }, Is.EquivalentTo(new[] { 1, 2 }));

            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running));
            foreach (OnlineSession session in _sessions)
            {
                session.TickLimit = ticks;
            }

            // 全員が最後のティックまで進み、全員の入力が届いて確定するまで回す。最後にもう一度回して、届いた入力で計算し直させる。
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);

            ulong expected = host.World.Checksum();
            foreach (OnlineSession session in _sessions)
            {
                Assert.That(session.PlayerCount, Is.EqualTo(3));
                Assert.That(session.World.Tick, Is.EqualTo(ticks));
                Assert.That(session.World.Checksum(), Is.EqualTo(expected), $"プレイヤー {session.LocalPlayer} の状態が違う");
            }
            Assert.That(host.ComparedChecksums, Is.GreaterThan(0), "チェックサムを比べていない");
            Assert.That(host.Desyncs, Is.Empty);

            // 入力どおりに動いている（全員が出現位置から離れている）。
            for (int p = 0; p < 3; p++)
            {
                Vector3 spawn = OnlineSession.SpawnPosition(SpawnBase, p, 3);
                Assert.That(Vector3.Distance(host.World.GetState(p).Position, spawn), Is.GreaterThan(1f), $"プレイヤー {p} が動いていない");
            }
        }

        [Test]
        public void TwoPlayers_WithSimulatedDelayAndLoss_StayInSync_AndReportCorrections()
        {
            // 片道 40ms（往復 80ms）、揺らぎ ±5ms、欠落 5%。入力が遅れて届くので、予測が外れて巻き戻し、表示中の位置が変わる。
            const int ticks = 240;
            OnlineSession host = Host();
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);
            host.Link.SetSimulatedConditions(40, 5, 5);
            client.Link.SetSimulatedConditions(40, 5, 5);
            host.StartMatch();
            PumpUntil(() => client.State == OnlineSession.SessionState.Running);
            host.TickLimit = ticks;
            client.TickLimit = ticks;

            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);

            Assert.That(client.World.Checksum(), Is.EqualTo(host.World.Checksum()));
            Assert.That(host.Desyncs, Is.Empty);
            int corrections = host.CorrectionCount + client.CorrectionCount;
            Assert.That(corrections, Is.GreaterThan(0), "前提: 予測が外れて位置が変わっている");
            Assert.That(Math.Max(host.LargestCorrection, client.LargestCorrection), Is.GreaterThan(0f));

            // 位置が変わった量は、取り出すと 0 に戻る。
            host.ConsumeCorrection(1);
            Assert.That(host.ConsumeCorrection(1), Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void TwoPlayers_OnAStageWithGimmicksAndKnockouts_StayInSync()
        {
            // 床の上のジャンプ台、上昇気流、加速リング、低い天井の撃破（ジャンプ台で打ち上がると上の外に出る）、コイン。
            // 攻撃も押し、遅延と欠落で巻き戻しを起こす（docs/11_combat_plan.md T3-8）。
            const int ticks = 360;
            BoxTerrain floor = new BoxTerrain().AddBox(-12f, -1f, 12f, 0f, true);
            StageZone[] zones =
            {
                new StageZone(StageZoneKind.JumpPad, -6f, 0f, -2f, 0.4f, System.Numerics.Vector2.UnitY, 24f),
                new StageZone(StageZoneKind.Updraft, 2f, 0f, 5f, 6f, System.Numerics.Vector2.Zero, 45f, 8f),
                new StageZone(StageZoneKind.BoostRing, -1f, 2f, 1f, 5f, System.Numerics.Vector2.UnitX, 18f),
            };
            CoinSpawnPoint[] coins =
            {
                new CoinSpawnPoint(new System.Numerics.Vector2(-4f, 1f), 5),
                new CoinSpawnPoint(new System.Numerics.Vector2(4f, 1f), 5),
                new CoinSpawnPoint(new System.Numerics.Vector2(0f, 3f), 10),
                new CoinSpawnPoint(new System.Numerics.Vector2(8f, 2f), 10),
            };
            _stage = new OnlineStage(floor, new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f) }, new StageBounds(-20f, -10f, 20f, 8f), zones, coins);
            _input = (player, tick) =>
            {
                PlayerInput input = Scripted(player, tick);
                return (tick + player * 11) % 60 < 2 ? PlayerInput.FromAxes(input.Move.X, 0f, input.Buttons | InputButtons.Attack) : input;
            };
            OnlineSession host = Host();
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);
            host.Link.SetSimulatedConditions(40, 5, 5);
            client.Link.SetSimulatedConditions(40, 5, 5);
            host.StartMatch();
            PumpUntil(() => client.State == OnlineSession.SessionState.Running);
            host.TickLimit = ticks;
            client.TickLimit = ticks;

            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);

            Assert.That(client.World.Checksum(), Is.EqualTo(host.World.Checksum()));
            Assert.That(host.Desyncs, Is.Empty);
            Assert.That(host.Session.RollbackCount + client.Session.RollbackCount, Is.GreaterThan(0), "前提: 巻き戻した");
            Assert.That(host.World.GetState(0).JumpPadCount, Is.GreaterThan(0), "前提: ジャンプ台が効いた");
            Assert.That(host.World.GetState(0).Combat.Falls + host.World.GetState(1).Combat.Falls, Is.GreaterThan(0), "前提: 撃破が起きた");
            Assert.That(host.World.GetState(0).Coins.Held + host.World.GetState(1).Coins.Held, Is.GreaterThan(0), "前提: コインを拾った");
        }

        [Test]
        public void FourPlayers_PlayAWholeMatch_WithCoinsBanksAndTheJackpot_AndRestart()
        {
            // T4-9: 4 人の本当の通信（遅延と欠落あり）で、短い試合を最後まで（カウントダウン → コイン・拠点の移動・100 枚のコイン・
            // 攻撃と撃破 → 終盤 → 時間切れ（アディショナルタイムを含む）→ 終わり）進め、全員が同じ状態で終わる。その後に再戦する。
            BoxTerrain floor = new BoxTerrain().AddBox(-16f, -1f, 16f, 0f, true).AddBox(-16f, 9f, 16f, 10f, true);
            StageZone[] zones =
            {
                new StageZone(StageZoneKind.JumpPad, -2f, 0f, 2f, 0.4f, System.Numerics.Vector2.UnitY, 22f),
                new StageZone(StageZoneKind.Updraft, 9f, 0f, 12f, 6f, System.Numerics.Vector2.Zero, 60f, 13f),
            };
            CoinSpawnPoint[] coins =
            {
                new CoinSpawnPoint(new System.Numerics.Vector2(-9f, 1f), 1),
                new CoinSpawnPoint(new System.Numerics.Vector2(-3f, 1f), 3),
                new CoinSpawnPoint(new System.Numerics.Vector2(3f, 1f), 1),
                new CoinSpawnPoint(new System.Numerics.Vector2(9f, 1f), 20),
                new CoinSpawnPoint(new System.Numerics.Vector2(0f, 4f), 100, CoinSpawnKind.Jackpot),
                new CoinSpawnPoint(new System.Numerics.Vector2(-6f, 4f), 100, CoinSpawnKind.Jackpot),
            };
            BankSpot[] banks =
            {
                new BankSpot(new System.Numerics.Vector2(0f, 1.5f), true),
                new BankSpot(new System.Numerics.Vector2(-8f, 1.5f), false),
                new BankSpot(new System.Numerics.Vector2(8f, 1.5f), false),
            };
            Vector3[] spawns = { new Vector3(-6f, 1f, 0f), new Vector3(6f, 1f, 0f), new Vector3(-2f, 1f, 0f), new Vector3(2f, 1f, 0f) };
            _stage = new OnlineStage(floor, spawns, new StageBounds(-20f, -10f, 20f, 14f), zones, coins, banks);
            _input = (player, tick) =>
            {
                PlayerInput input = Scripted(player, tick);
                return (tick + player * 11) % 40 < 2 ? PlayerInput.FromAxes(input.Move.X, 0f, input.Buttons | InputButtons.Attack) : input;
            };
            var rules = new MatchRules
            {
                CountdownTicks = 30,
                DurationTicks = 900,
                OvertimeTicks = 60,
                CoinSpawnIntervalTicks = 120,
                LateGameTicks = 300,
                LateGameSpawnIntervalTicks = 60,
                JackpotTicksBeforeEnd = 600,
                JackpotNoticeTicks = 120,
                BankMoveIntervalTicks = 240,
                BankNoticeTicks = 60,
                Seed = 7,
            };
            int end = rules.CountdownTicks + rules.DurationTicks + rules.OvertimeTicks + 30;

            OnlineSession host = Host();
            var clients = new List<OnlineSession> { Join(host), Join(host), Join(host) };
            PumpUntil(() => clients.TrueForAll(c => c.LocalPlayer >= 0));
            foreach (OnlineSession session in _sessions)
            {
                session.Link.SetSimulatedConditions(25, 3, 2);
            }
            host.StartMatch(rules);
            PumpUntil(() => clients.TrueForAll(c => c.State == OnlineSession.SessionState.Running));
            foreach (OnlineSession session in _sessions)
            {
                session.TickLimit = end;
            }
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == end && s.Session.ConfirmedTick >= end - 1));
            PumpFrames(2);

            ulong expected = host.World.Checksum();
            foreach (OnlineSession session in _sessions)
            {
                Assert.That(session.PlayerCount, Is.EqualTo(4));
                Assert.That(session.World.Phase, Is.EqualTo(MatchPhase.Finished), $"プレイヤー {session.LocalPlayer}: 試合が終わっていない");
                Assert.That(session.World.Checksum(), Is.EqualTo(expected), $"プレイヤー {session.LocalPlayer} の状態が違う");
            }
            Assert.That(host.Desyncs, Is.Empty);
            Assert.That(host.ComparedChecksums, Is.GreaterThan(0));

            var states = new List<CharacterState>();
            int picked = 0;
            int falls = 0;
            for (int p = 0; p < 4; p++)
            {
                CharacterState state = host.World.GetState(p);
                states.Add(state);
                picked += state.Coins.Held + state.Coins.Banked;
                falls += state.Combat.Falls;
            }
            CoinTotals totals = host.World.Coins.Totals(states);
            Assert.That(totals.IsConserved, Is.True, totals.ToString());
            Assert.That(totals.Generated, Is.GreaterThanOrEqualTo(100), "前提: 100 枚のコインが出た");
            Assert.That(picked, Is.GreaterThan(0), "前提: だれかがコインを拾った");
            Assert.That(BankRules.MovesAt(rules, end), Is.GreaterThanOrEqualTo(3), "前提: 拠点が移った");
            Assert.That(MatchResults.Rank(states, host.World.Match.Winner), Has.Length.EqualTo(4));

            // 再戦: 同じ 4 人で、新しい試合を同じ状態で進める。
            host.RestartMatch(rules);
            PumpUntil(() => clients.TrueForAll(c => c.MatchNumber == host.MatchNumber));
            foreach (OnlineSession session in _sessions)
            {
                session.TickLimit = 180;
            }
            PumpUntil(() => _sessions.TrueForAll(s => s.MatchNumber == host.MatchNumber && s.Session.CurrentTick == 180 && s.Session.ConfirmedTick >= 179));
            PumpFrames(2);
            foreach (OnlineSession session in _sessions)
            {
                Assert.That(session.World.Checksum(), Is.EqualTo(host.World.Checksum()), $"再戦の後に、プレイヤー {session.LocalPlayer} の状態が違う");
            }
            Assert.That(host.Desyncs, Is.Empty, "再戦の後に不一致");
        }

        [Test]
        public void ClientsReceiveHostConfig()
        {
            var config = new CharacterConfig();
            config.Wire.MaxLength = 9.5f;
            OnlineSession host = Host(config);
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);

            host.StartMatch();
            PumpUntil(() => client.State == OnlineSession.SessionState.Running);
            Assert.That(client.World.GetConfig(0).Wire.MaxLength, Is.EqualTo(9.5f));
            Assert.That(client.World.GetConfig(1).Wire.MaxLength, Is.EqualTo(9.5f));

            // ホストの元の設定を後から変えても、試合の値は変わらない（全員の値がずれない）。
            config.Wire.MaxLength = 3f;
            Assert.That(host.World.GetConfig(0).Wire.MaxLength, Is.EqualTo(9.5f));
        }

        [Test]
        public void PlayerNames_AreSharedWithEveryone_AndCanChangeInTheLobby()
        {
            OnlineSession host = Host();
            host.LocalName = "  Bob\n ";
            OnlineSession first = Join(host);
            first.LocalName = "Alice";
            OnlineSession second = Join(host);
            PumpUntil(() => first.LocalPlayer >= 0 && second.LocalPlayer >= 0);
            first.LocalName = "Alice the very long name";
            PumpFrames(5);

            host.StartMatch();
            PumpUntil(() => first.State == OnlineSession.SessionState.Running && second.State == OnlineSession.SessionState.Running);
            var expected = new[] { "Bob", "Alice the ve", string.Empty };
            Assert.That(host.PlayerNames, Is.EqualTo(expected), "ホストの名前の表");
            Assert.That(first.PlayerNames, Is.EqualTo(expected), "クライアントに届いた名前");
            Assert.That(second.PlayerNames, Is.EqualTo(expected), "名前のない人は空");
            Assert.That(OnlineSession.SanitizeName(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void EachPlayer_UsesTheCharacterTheyPicked_AndStaysInSync()
        {
            // キャラクターの選択（docs/13 T5-5）: ロビーで選んだ番号をホストへ送り、ホストが開始のメッセージで各自の調整値を全員へ送る。
            const int ticks = 240;
            var standard = new CharacterConfig();
            var fast = new CharacterConfig();
            fast.Movement.RunSpeed = 11f;
            fast.Combat.SuperGaugeCost = 500;
            OnlineSession host = OnlineSession.Host(0, new OnlineStage(_terrain, new[] { SpawnBase }), new[] { standard, fast },
                new RollbackSettings());
            _sessions.Add(host);
            host.LocalCharacter = 1;
            OnlineSession first = Join(host);
            first.LocalCharacter = 1;
            OnlineSession second = Join(host);
            second.LocalCharacter = 5;
            PumpUntil(() => first.LocalPlayer >= 0 && second.LocalPlayer >= 0);
            PumpUntil(() => first.Roster[1].Character == 1 && first.Roster[0].Character == 1);
            Assert.That(host.Roster[2].Character, Is.Zero, "選べない番号は 0 になる");

            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running));
            foreach (OnlineSession session in _sessions)
            {
                Assert.That(session.PlayerCharacters, Is.EqualTo(new[] { 1, 1, 0 }), $"プレイヤー {session.LocalPlayer} のキャラクターの表");
                Assert.That(session.World.GetConfig(0).Movement.RunSpeed, Is.EqualTo(11f));
                Assert.That(session.World.GetConfig(1).Movement.RunSpeed, Is.EqualTo(11f));
                Assert.That(session.World.GetConfig(2).Movement.RunSpeed, Is.EqualTo(8f));
                Assert.That(session.World.GetConfig(1).Combat.SuperGaugeCost, Is.EqualTo(500));
                session.TickLimit = ticks;
            }

            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);
            ulong expected = host.World.Checksum();
            foreach (OnlineSession session in _sessions)
            {
                Assert.That(session.World.Checksum(), Is.EqualTo(expected), $"プレイヤー {session.LocalPlayer} の状態が違う");
            }
            Assert.That(host.Desyncs, Is.Empty);
        }

        [Test]
        public void ClientsReceiveTheMatchRules()
        {
            OnlineSession host = Host();
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);

            host.StartMatch(new MatchRules { CountdownTicks = 30, DurationTicks = 90, RespawnDelayTicks = 20, InvincibleTicks = 10 });
            PumpUntil(() => client.State == OnlineSession.SessionState.Running);
            MatchRules rules = client.World.Rules;
            Assert.That(rules.CountdownTicks, Is.EqualTo(30));
            Assert.That(rules.DurationTicks, Is.EqualTo(90));
            Assert.That(rules.RespawnDelayTicks, Is.EqualTo(20));
            Assert.That(rules.InvincibleTicks, Is.EqualTo(10));

            // 全員が終わりまで進み、同じ状態で止まる。
            PumpUntil(() => _sessions.TrueForAll(s => s.World.Phase == MatchPhase.Finished && s.Session.ConfirmedTick >= 125));
            PumpFrames(2);
            Assert.That(client.World.Checksum(), Is.EqualTo(host.World.Checksum()));
            Assert.That(host.Desyncs, Is.Empty);
        }

        [Test]
        public void Restart_StartsANewMatch_ForEveryone_AndStaysInSync()
        {
            // 遅延と欠落があっても、前の試合の入力やチェックサムが新しい試合に混ざらない（試合の番号で分ける）。
            OnlineSession host = Host();
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);
            host.Link.SetSimulatedConditions(40, 5, 5);
            client.Link.SetSimulatedConditions(40, 5, 5);
            // 2 人とも確定 0 で同点なので、時間切れでアディショナルタイム（30 ティック）に入ってから終わる（T4-8）。
            var rules = new MatchRules { CountdownTicks = 20, DurationTicks = 100, OvertimeTicks = 30 };
            host.StartMatch(rules);
            PumpUntil(() => _sessions.TrueForAll(s => s.World != null && s.World.Phase == MatchPhase.Finished));
            int firstMatch = host.MatchNumber;

            host.RestartMatch(rules);
            PumpUntil(() => client.MatchNumber == host.MatchNumber);
            Assert.That(host.MatchNumber, Is.EqualTo(firstMatch + 1));
            foreach (OnlineSession session in _sessions)
            {
                session.TickLimit = 200;
            }
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == 200 && s.Session.ConfirmedTick >= 199));
            PumpFrames(2);

            Assert.That(client.World.Checksum(), Is.EqualTo(host.World.Checksum()), "始め直した後に状態が違う");
            Assert.That(host.Desyncs, Is.Empty, "前の試合のチェックサムが混ざった");
            Assert.That(client.World.Phase, Is.EqualTo(MatchPhase.Finished));
        }

        [Test]
        public void Restart_ResendsTheStart_UntilTheClientHasStarted_AndDuplicatesAreIgnored()
        {
            // 配布したビルドの再戦で、クライアントが始まらずに止まった（2026-10-14）。開始のメッセージが届いたと分かるまで送り直す。
            OnlineSession host = Host();
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);
            var rules = new MatchRules { CountdownTicks = 10, DurationTicks = 60, OvertimeTicks = 0 };
            host.StartMatch(rules);
            PumpUntil(() => _sessions.TrueForAll(s => s.World != null && s.World.Phase == MatchPhase.Finished));

            // クライアントからホストへの送信を全部落とす（ホストは、クライアントが新しい試合を始めたことを知れない）。
            client.Link.SetSimulatedConditions(0, 0, 100);
            host.RestartMatch(rules);
            int match = host.MatchNumber;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 1500)
            {
                PumpFrames(1);
                Thread.Sleep(1);
            }
            Assert.That(client.MatchNumber, Is.EqualTo(match), "クライアントに開始のメッセージが届かない");
            Assert.That(host.StartResends, Is.GreaterThan(0), "始めたと分からないのに、送り直さない");
            int startedTick = client.Session.CurrentTick;
            Assert.That(startedTick, Is.GreaterThan(0), "前提: クライアントが新しい試合を進めている");

            // 送信を戻すと、そろって進み、送り直しも止まる。送り直された開始のメッセージで、クライアントが始め直さない。
            client.Link.SetSimulatedConditions(0, 0, 0);
            foreach (OnlineSession session in _sessions)
            {
                session.TickLimit = 150;
            }
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == 150 && s.Session.ConfirmedTick >= 149));
            int resends = host.StartResends;
            PumpFrames(60);
            Assert.That(host.StartResends, Is.EqualTo(resends), "クライアントが始めた後も送り直している");
            Assert.That(client.MatchNumber, Is.EqualTo(match));
            Assert.That(client.World.Checksum(), Is.EqualTo(host.World.Checksum()), "送り直しのあとに状態が違う");
            Assert.That(host.Desyncs, Is.Empty);
        }

        [Test]
        public void RestartMatch_OnlyForTheHostDuringAMatch()
        {
            OnlineSession host = Host();
            Assert.Throws<InvalidOperationException>(() => host.RestartMatch(), "ロビーで始め直せた");
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);
            host.StartMatch();
            PumpUntil(() => client.State == OnlineSession.SessionState.Running);
            Assert.Throws<InvalidOperationException>(() => client.RestartMatch(), "クライアントが始め直せた");
        }

        [Test]
        public void StartMatch_WithoutRules_HasNoTimeLimit()
        {
            OnlineSession host = Host();
            host.StartMatch();
            Assert.That(host.World.Rules.HasTimeLimit, Is.False);
            Assert.Throws<Airerage.Core.Common.InvalidTuningException>(() => Host().StartMatch(new MatchRules { CountdownTicks = -1 }));
        }

        [Test]
        public void JoinAfterStart_IsRefused()
        {
            OnlineSession host = Host();
            OnlineSession early = Join(host);
            PumpUntil(() => early.LocalPlayer >= 0);
            host.StartMatch();

            OnlineSession late = Join(host);
            PumpUntil(() => late.State == OnlineSession.SessionState.Ended);
            Assert.That(late.LocalPlayer, Is.EqualTo(-1));
            Assert.That(host.LobbyPlayerCount, Is.EqualTo(2));
        }

        [Test]
        public void PlayerLeavingDuringMatch_IsRemoved_AndTheOthersGoOnInSync()
        {
            // 試合中に抜けた人を外して、残りの人で続ける（docs/15 T6-10）。
            OnlineSession host = Host();
            OnlineSession stays = Join(host);
            OnlineSession leaves = Join(host);
            PumpUntil(() => stays.LocalPlayer >= 0 && leaves.LocalPlayer >= 0);
            int leaver = leaves.LocalPlayer;
            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running && s.Session.CurrentTick > 30));

            leaves.Dispose();
            _sessions.Remove(leaves);
            PumpUntil(() => host.HasDeparted(leaver) && stays.HasDeparted(leaver));
            int ticks = Math.Max(host.Session.CurrentTick, stays.Session.CurrentTick) + 180;
            host.TickLimit = ticks;
            stays.TickLimit = ticks;
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);

            Assert.That(host.State, Is.EqualTo(OnlineSession.SessionState.Running), "残りの人の試合が終わった");
            Assert.That(stays.State, Is.EqualTo(OnlineSession.SessionState.Running));
            Assert.That(stays.World.Checksum(), Is.EqualTo(host.World.Checksum()), "残りの人の状態がそろわない");
            Assert.That(host.Desyncs, Is.Empty);
            Assert.That(host.World.GetState(leaver).Combat.HasLeft, Is.True, "抜けた人のキャラクターが外れていない");
            Assert.That(host.Status, Does.Contain("left"));
            Assert.That(stays.Status, Does.Contain("left"));
        }

        [Test]
        public void PlayerLeaving_WithDelayAndLoss_StillLeavesTheOthersInSync()
        {
            // 片道 40ms・欠落 5% で、入力や確認応答が遅れて届いたり、届かなかったりしても、残りの人はそろう（抜ける人の送信は欠落させない。
            // 切断の知らせが欠落すると、気づくのが接続の時間切れまで遅れるため）。
            OnlineSession host = Host();
            OnlineSession stays = Join(host);
            OnlineSession leaves = Join(host);
            PumpUntil(() => stays.LocalPlayer >= 0 && leaves.LocalPlayer >= 0);
            host.Link.SetSimulatedConditions(40, 5, 5);
            stays.Link.SetSimulatedConditions(40, 5, 5);
            leaves.Link.SetSimulatedConditions(40, 5, 0);
            int leaver = leaves.LocalPlayer;
            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running && s.Session.CurrentTick > 60));

            leaves.Dispose();
            _sessions.Remove(leaves);
            PumpUntil(() => host.HasDeparted(leaver) && stays.HasDeparted(leaver));
            int ticks = Math.Max(host.Session.CurrentTick, stays.Session.CurrentTick) + 180;
            host.TickLimit = ticks;
            stays.TickLimit = ticks;
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);

            Assert.That(stays.World.Checksum(), Is.EqualTo(host.World.Checksum()), "残りの人の状態がそろわない");
            Assert.That(host.Desyncs, Is.Empty);
        }

        [Test]
        public void LastClientLeavingDuringMatch_EndsTheMatch()
        {
            OnlineSession host = Host();
            OnlineSession leaves = Join(host);
            PumpUntil(() => leaves.LocalPlayer >= 0);
            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running && s.Session.CurrentTick > 30));

            leaves.Dispose();
            _sessions.Remove(leaves);
            PumpUntil(() => host.State == OnlineSession.SessionState.Ended);
            Assert.That(host.Status, Does.Contain("disconnected"));
        }

        [Test]
        public void Rematch_AfterSomeoneLeft_StartsWithThePeopleWhoStayed()
        {
            OnlineSession host = Host();
            OnlineSession leaves = Join(host);
            PumpUntil(() => leaves.LocalPlayer == 1);
            OnlineSession stays = Join(host);
            PumpUntil(() => stays.LocalPlayer == 2);
            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running && s.Session.CurrentTick > 30));
            leaves.Dispose();
            _sessions.Remove(leaves);
            PumpUntil(() => host.HasDeparted(1) && stays.HasDeparted(1));

            host.RestartMatch();
            PumpUntil(() => stays.State == OnlineSession.SessionState.Running && stays.MatchNumber == host.MatchNumber && stays.Session.CurrentTick > 30
                && host.Session.CurrentTick > 30);

            Assert.That(host.PlayerCount, Is.EqualTo(2), "抜けた人を入れて始め直した");
            Assert.That(stays.PlayerCount, Is.EqualTo(2));
            Assert.That(stays.LocalPlayer, Is.EqualTo(1), "番号を詰めていない");
            Assert.That(stays.HasDeparted(1), Is.False);
        }

        [Test]
        public void Lobby_ShowsWhoIsConnected_OnTheHostAndEveryClient()
        {
            OnlineSession host = Host();
            host.LocalName = "Bob";
            OnlineSession first = Join(host);
            first.LocalName = "Alice";
            OnlineSession second = Join(host);
            PumpUntil(() => first.LocalPlayer >= 0 && second.LocalPlayer >= 0);
            PumpUntil(() => second.Roster[1].Name == "Alice");

            var expected = new[]
            {
                new LobbySlot(true, "Bob"), new LobbySlot(true, "Alice"), new LobbySlot(true, string.Empty), new LobbySlot(false, string.Empty),
            };
            Assert.That(host.Roster, Is.EqualTo(expected), "ホストの待機所");
            Assert.That(first.Roster, Is.EqualTo(expected), "クライアントの待機所");
            Assert.That(second.Roster, Is.EqualTo(expected), "あとから来たクライアントの待機所");

            // 名前を変えると、全員の待機所に届く。
            second.LocalName = "Carol";
            host.LocalName = "Bobby";
            PumpUntil(() => first.Roster[2].Name == "Carol" && first.Roster[0].Name == "Bobby");
            Assert.That(second.Roster[0].Name, Is.EqualTo("Bobby"));
        }

        [Test]
        public void Lobby_ALeavingPlayer_IsShownAsWaiting_AndTheNumbersCloseUpAtTheStart()
        {
            OnlineSession host = Host();
            OnlineSession leaves = Join(host);
            PumpUntil(() => leaves.LocalPlayer == 1);
            OnlineSession first = Join(host);
            PumpUntil(() => first.LocalPlayer == 2);
            first.LocalName = "Alice";
            OnlineSession second = Join(host);
            PumpUntil(() => second.LocalPlayer == 3);
            second.LocalName = "Carol";
            PumpFrames(5);

            leaves.Dispose();
            _sessions.Remove(leaves);
            PumpUntil(() => !host.Roster[1].Connected && !first.Roster[1].Connected && !second.Roster[1].Connected);
            Assert.That(first.Roster[2], Is.EqualTo(new LobbySlot(true, "Alice")), "残った人の表示が変わった");
            Assert.That(host.LobbyPlayerCount, Is.EqualTo(3));

            // 空いた番号を詰めて始める（3 人で、番号は 0〜2）。
            host.StartMatch();
            PumpUntil(() => first.State == OnlineSession.SessionState.Running && second.State == OnlineSession.SessionState.Running);
            Assert.That(host.PlayerCount, Is.EqualTo(3));
            Assert.That(first.LocalPlayer, Is.EqualTo(1), "番号が詰まらない");
            Assert.That(second.LocalPlayer, Is.EqualTo(2));
            Assert.That(first.PlayerNames, Is.EqualTo(new[] { string.Empty, "Alice", "Carol" }));

            const int ticks = 120;
            foreach (OnlineSession session in _sessions)
            {
                session.TickLimit = ticks;
            }
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);
            Assert.That(first.World.Checksum(), Is.EqualTo(host.World.Checksum()));
            Assert.That(second.World.Checksum(), Is.EqualTo(host.World.Checksum()));
            Assert.That(host.Desyncs, Is.Empty);
        }

        [Test]
        public void Lobby_EveryoneReady_MovesToCharacterSelect_ThenStageSelect_AndTheMatchStartsOnTheChosenStage()
        {
            // ネットワーク対戦の流れ（docs/15 T6-7）: 待機所で全員が準備完了 → キャラクター選択 → 全員が決定 → ステージ選択（ホスト）→
            // 開始のメッセージのステージを全員が読み込んで（BeginPending）始める。
            var characters = new[] { new CharacterConfig(), CharacterPresets.RiskTaker() };
            OnlineSession host = OnlineSession.Host(0, characters, new RollbackSettings());
            _sessions.Add(host);
            OnlineSession client = OnlineSession.Join("127.0.0.1", host.Port, new RollbackSettings());
            _sessions.Add(client);
            PumpUntil(() => client.LocalPlayer == 1 && client.Roster[1].Connected);
            Assert.That(host.Phase, Is.EqualTo(LobbyPhase.Waiting));

            host.LocalReady = true;
            PumpUntil(() => client.Roster[0].Ready);
            Assert.That(client.Phase, Is.EqualTo(LobbyPhase.Waiting), "1 人だけの準備完了で進んだ");
            client.LocalReady = true;
            PumpUntil(() => client.Phase == LobbyPhase.CharacterSelect);
            Assert.That(host.Phase, Is.EqualTo(LobbyPhase.CharacterSelect));
            Assert.That(host.LocalReady || client.LocalReady, Is.False, "段階が変わっても準備完了が残っている");

            // キャラクター選択の途中からは参加できない。
            OnlineSession late = OnlineSession.Join("127.0.0.1", host.Port, new RollbackSettings());
            _sessions.Add(late);
            PumpUntil(() => late.State == OnlineSession.SessionState.Ended);
            Assert.That(host.LobbyPlayerCount, Is.EqualTo(2));
            late.Dispose();
            _sessions.Remove(late);

            client.LocalCharacter = 1;
            PumpUntil(() => host.Roster[1].Character == 1);
            host.LocalReady = true;
            client.LocalReady = true;
            PumpUntil(() => client.Phase == LobbyPhase.StageSelect);
            host.StageCursor = 3;
            PumpUntil(() => client.StageCursor == 3);

            // ステージ選択からキャラクター選択へ戻せる（決定は外れる）。
            host.BackToCharacterSelect();
            PumpUntil(() => client.Phase == LobbyPhase.CharacterSelect);
            Assert.That(client.Roster[1].Ready, Is.False);
            host.LocalReady = true;
            client.LocalReady = true;
            PumpUntil(() => client.Phase == LobbyPhase.StageSelect);

            host.StartMatch(MatchRules.FreePlay(), 3);
            PumpUntil(() => client.State == OnlineSession.SessionState.Loading);
            Assert.That(host.State, Is.EqualTo(OnlineSession.SessionState.Loading), "ステージを読み込む前に始めた");
            Assert.That(host.PendingStage, Is.EqualTo(3));
            Assert.That(client.PendingStage, Is.EqualTo(3), "開始のメッセージにステージの番号がない");
            PumpFrames(10);
            Assert.That(client.State, Is.EqualTo(OnlineSession.SessionState.Loading), "送り直しの開始のメッセージで状態が変わった");

            var stage = new OnlineStage(_terrain, new[] { SpawnBase });
            host.BeginPending(stage, 3);
            PumpFrames(5);
            client.BeginPending(stage, 3);
            PumpUntil(() => client.State == OnlineSession.SessionState.Running && host.State == OnlineSession.SessionState.Running);
            Assert.That(client.StageIndex, Is.EqualTo(3));
            Assert.That(client.PlayerCharacters, Is.EqualTo(new[] { 0, 1 }), "選んだキャラクターで始まらない");
            RunTicksAndCompare(host, client, 120);

            // 再戦は同じステージなので、すぐ始まる。
            host.RestartMatch(MatchRules.FreePlay());
            PumpUntil(() => client.State == OnlineSession.SessionState.Running && client.MatchNumber == host.MatchNumber);
            Assert.That(host.State, Is.EqualTo(OnlineSession.SessionState.Running));

            // 試合の後に、全員でキャラクター選択へ戻る（T6-8）。
            host.ReturnToLobby(LobbyPhase.CharacterSelect);
            PumpUntil(() => client.State == OnlineSession.SessionState.Lobby);
            Assert.That(client.Phase, Is.EqualTo(LobbyPhase.CharacterSelect));
            Assert.That(client.World, Is.Null, "前の試合のワールドが残っている");
            Assert.That(host.Roster[1].Character, Is.EqualTo(1), "戻ったら選んだキャラクターが消えた");
        }

        [Test]
        public void Lobby_FallingBelowTwoPlayers_GoesBackToTheWaitingRoom()
        {
            OnlineSession host = OnlineSession.Host(0, new[] { new CharacterConfig() }, new RollbackSettings());
            _sessions.Add(host);
            OnlineSession client = OnlineSession.Join("127.0.0.1", host.Port, new RollbackSettings());
            _sessions.Add(client);
            PumpUntil(() => client.LocalPlayer == 1 && host.LobbyPlayerCount == 2);
            host.LocalReady = true;
            client.LocalReady = true;
            PumpUntil(() => host.Phase == LobbyPhase.CharacterSelect);
            client.Dispose();
            _sessions.Remove(client);
            PumpUntil(() => host.LobbyPlayerCount == 1);
            Assert.That(host.Phase, Is.EqualTo(LobbyPhase.Waiting), "1 人になってもキャラクター選択のまま");
        }

        // 全員を ticks まで進め、入力が確定するまで回して、状態とチェックサムがそろっていることを確かめる。
        private void RunTicksAndCompare(OnlineSession host, OnlineSession client, int ticks)
        {
            host.TickLimit = ticks;
            client.TickLimit = ticks;
            PumpUntil(() => _sessions.TrueForAll(s => s.Session.CurrentTick == ticks && s.Session.ConfirmedTick >= ticks - 1));
            PumpFrames(2);
            Assert.That(client.World.Checksum(), Is.EqualTo(host.World.Checksum()));
            Assert.That(host.Desyncs, Is.Empty);
            host.TickLimit = int.MaxValue;
            client.TickLimit = int.MaxValue;
        }

        [Test]
        public void LobbyMessage_RoundTrips_AndRejectsBrokenData()
        {
            var slots = new[] { new LobbySlot(true, "ボブ", 2, true), new LobbySlot(false, string.Empty) };
            byte[] data = NetMessages.Lobby(slots, LobbyPhase.StageSelect, 4);
            Assert.That(NetMessages.ReadLobby(data, out LobbyPhase phase, out int stage), Is.EqualTo(slots));
            Assert.That(phase, Is.EqualTo(LobbyPhase.StageSelect));
            Assert.That(stage, Is.EqualTo(4));
            Assert.That(NetMessages.ReadReady(NetMessages.Ready(true)), Is.True);
            Assert.Throws<FormatException>(() => NetMessages.ReadLobby(new[] { (byte)NetKind.Lobby, (byte)2, (byte)1, (byte)9 }));
        }

        [Test]
        public void StartMatch_OnClient_Throws()
        {
            OnlineSession host = Host();
            OnlineSession client = Join(host);
            PumpUntil(() => client.LocalPlayer >= 0);
            Assert.Throws<InvalidOperationException>(() => client.StartMatch());
        }

        // 各プレイヤーの決まった入力（ティックとプレイヤーの番号だけで決まる）。左右の移動、ジャンプ、ワイヤーを含む。
        private static PlayerInput Scripted(int player, int tick)
        {
            int phase = (tick / 45 + player) % 4;
            float x = phase == 0 ? 1f : phase == 1 ? 0f : phase == 2 ? -1f : 0.5f;
            InputButtons buttons = InputButtons.None;
            if ((tick + player * 7) % 50 < 3)
            {
                buttons |= InputButtons.Jump;
            }
            if ((tick + player * 13) % 90 < 30)
            {
                buttons |= InputButtons.Wire;
            }
            return PlayerInput.FromAxes(x, 0f, buttons);
        }

        private OnlineSession Host(CharacterConfig config = null)
        {
            OnlineSession host = _stage != null
                ? OnlineSession.Host(0, _stage, config ?? new CharacterConfig(), new RollbackSettings())
                : OnlineSession.Host(0, _terrain, SpawnBase, config ?? new CharacterConfig(), new RollbackSettings());
            _sessions.Add(host);
            return host;
        }

        private OnlineSession Join(OnlineSession host)
        {
            OnlineSession client = _stage != null
                ? OnlineSession.Join("127.0.0.1", host.Port, _stage, new RollbackSettings())
                : OnlineSession.Join("127.0.0.1", host.Port, _terrain, SpawnBase, new RollbackSettings());
            _sessions.Add(client);
            return client;
        }

        // 全員を1フレームずつ（1/60 秒として）進めることを、条件を満たすまで繰り返す。時間内に満たさなければ失敗。
        private void PumpUntil(Func<bool> done)
        {
            var watch = Stopwatch.StartNew();
            while (!done())
            {
                if (watch.ElapsedMilliseconds > TimeoutMs)
                {
                    Assert.Fail("時間内に終わらなかった: " + string.Join(" / ", _sessions.ConvertAll(s => s.Status)));
                }
                PumpFrames(1);
                Thread.Sleep(1);
            }
        }

        private void PumpFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                foreach (OnlineSession session in _sessions)
                {
                    OnlineSession current = session;
                    current.Update(FrameSeconds, tick => _input(current.LocalPlayer, tick));
                }
            }
        }
    }
}
