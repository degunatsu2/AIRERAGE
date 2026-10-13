using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using NUnit.Framework;

namespace Degunatsu.Net.Tests
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
            // 床の上のジャンプ台、上昇気流、加速リング、低い天井の撃破（ジャンプ台で打ち上がると上の外に出る）。
            // 攻撃も押し、遅延と欠落で巻き戻しを起こす（docs/11_combat_plan.md T3-8）。
            const int ticks = 360;
            BoxTerrain floor = new BoxTerrain().AddBox(-12f, -1f, 12f, 0f, true);
            StageZone[] zones =
            {
                new StageZone(StageZoneKind.JumpPad, -6f, 0f, -2f, 0.4f, System.Numerics.Vector2.UnitY, 24f),
                new StageZone(StageZoneKind.Updraft, 2f, 0f, 5f, 6f, System.Numerics.Vector2.Zero, 45f, 8f),
                new StageZone(StageZoneKind.BoostRing, -1f, 2f, 1f, 5f, System.Numerics.Vector2.UnitX, 18f),
            };
            _stage = new OnlineStage(floor, new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f) }, new StageBounds(-20f, -10f, 20f, 8f), zones);
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
        public void PlayerLeavingDuringMatch_EndsTheMatchForEveryone()
        {
            OnlineSession host = Host();
            OnlineSession stays = Join(host);
            OnlineSession leaves = Join(host);
            PumpUntil(() => stays.LocalPlayer >= 0 && leaves.LocalPlayer >= 0);
            host.StartMatch();
            PumpUntil(() => _sessions.TrueForAll(s => s.State == OnlineSession.SessionState.Running && s.Session.CurrentTick > 30));

            leaves.Dispose();
            _sessions.Remove(leaves);
            PumpUntil(() => host.State == OnlineSession.SessionState.Ended && stays.State == OnlineSession.SessionState.Ended);
            Assert.That(host.Status, Does.Contain("disconnected"));
            Assert.That(stays.Status, Does.Contain("Disconnected"));
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
