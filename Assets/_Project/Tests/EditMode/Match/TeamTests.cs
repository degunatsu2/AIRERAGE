using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Match
{
    /// <summary>
    /// チーム対戦の Core（docs/19_team_plan.md T9-1〜T9-3。D-78〜D-86 は推奨どおりに決定、2026-11-02）: 味方に当たらない、確定コインは
    /// チームの合計で順位・勝敗・アディショナルタイム・撃破報酬・逆転の恩恵を決める、均等に分けるボタンの割り当て、8 人の処理の重さ。
    /// </summary>
    public class TeamTests
    {
        private const float GroundY = 0.91f;
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);

        private static CharacterConfig SingleHitConfig()
        {
            var config = new CharacterConfig();
            config.Combat.Ground.Hits = new[] { config.Combat.Ground.Hits[0] };
            return config;
        }

        private static List<CharacterState> States(params (byte Team, int Banked, int Held)[] people)
        {
            var states = new List<CharacterState>();
            foreach (var p in people)
            {
                CharacterState state = CharacterState.Spawn(Vector3.Zero, 1);
                state.Combat.Team = p.Team;
                state.Coins.Banked = p.Banked;
                state.Coins.Held = p.Held;
                states.Add(state);
            }
            return states;
        }

        [Test]
        public void Allies_AreNotHitByMeleeAttacks_ButEnemiesAre()
        {
            foreach (bool sameTeam in new[] { true, false })
            {
                var world = new SimWorld(TestArenas.Ground());
                world.AddCharacter(SingleHitConfig(), new Vector3(0f, GroundY, 0f), 1);
                world.AddCharacter(SingleHitConfig(), new Vector3(1.6f, GroundY, 0f), -1);
                world.SetTeam(0, 1);
                world.SetTeam(1, sameTeam ? (byte)1 : (byte)2);
                world.Step(new PlayerInput[2]);
                world.Step(new[] { Attack, default });
                bool hit = false;
                for (int t = 0; t < 60 && !hit; t++)
                {
                    world.Step(new PlayerInput[2]);
                    hit = world.GetState(1).Action.Kind == ActionKind.Hitstun;
                }
                Assert.That(hit, Is.EqualTo(!sameTeam), sameTeam ? "味方に当たった" : "敵に当たらない");
            }
        }

        [Test]
        public void ProjectileHits_OnAllies_AreDropped()
        {
            List<CharacterState> states = States((1, 0, 0), (1, 0, 0), (2, 0, 0));
            var configs = new List<CharacterConfig> { new CharacterConfig(), new CharacterConfig(), new CharacterConfig() };
            var combat = new CombatTuning();
            AttackHit data = combat.Ground.Hits[0];
            var extra = new List<HitRecord>
            {
                new HitRecord(0, 1, AttackSlot.Ground, 0, data, 1, AttackPower.Normal, fromObject: true),
                new HitRecord(0, 2, AttackSlot.Ground, 0, data, 1, AttackPower.Normal, fromObject: true),
            };
            var resolver = new HitResolver();
            resolver.Resolve(states, configs, 1, null, 0, extra);
            Assert.That(resolver.LastHits.Count, Is.EqualTo(1), "味方への投射物の命中が残った");
            Assert.That(resolver.LastHits[0].Victim, Is.EqualTo(2));
        }

        [Test]
        public void BankedCoins_AreSummedPerTeam_ForScoreAndRank()
        {
            // チーム 1: 10 + 15 = 25、チーム 2: 30、チーム 3: 25。
            List<CharacterState> states = States((1, 10, 0), (2, 30, 0), (1, 15, 0), (3, 25, 0));
            Assert.That(TeamRules.ScoreOf(states, 0), Is.EqualTo(25));
            Assert.That(TeamRules.ScoreOf(states, 2), Is.EqualTo(25));
            Assert.That(TeamRules.ScoreOf(states, 1), Is.EqualTo(30));
            Assert.That(TeamRules.RankOf(states, 1, false), Is.EqualTo(1));
            Assert.That(TeamRules.RankOf(states, 0, false), Is.EqualTo(2), "チーム 1 は 2 位");
            Assert.That(TeamRules.RankOf(states, 2, false), Is.EqualTo(2), "同じチームは同じ順位");
            Assert.That(TeamRules.RankOf(states, 3, false), Is.EqualTo(2), "同じ合計のチームは同じ順位");
            Assert.That(CatchUpRules.RankOf(states, 0), Is.EqualTo(2), "逆転の恩恵もチームの順位");
            Assert.That(CombatRewardRules.RankOf(states, 3), Is.EqualTo(2), "撃破報酬もチームの順位");

            // 結果の並び: チーム 2 → 合計が同じチーム 1・3 は番号の順（チーム 1 の 2 人は並ぶ。中は確定コインの多い順）。
            int[] order = MatchResults.Rank(states);
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 0, 3 }));
            // アディショナルタイムでチーム 3 の人が勝てば、チーム 3 がいちばん上。
            Assert.That(MatchResults.Rank(states, winner: 3)[0], Is.EqualTo(3));
        }

        [Test]
        public void WithoutTeams_RanksAndResults_StayAsBefore()
        {
            List<CharacterState> states = States((0, 10, 0), (0, 30, 0), (0, 10, 0));
            Assert.That(TeamRules.IsTeamMatch(states), Is.False);
            Assert.That(TeamRules.ScoreOf(states, 0), Is.EqualTo(10));
            Assert.That(TeamRules.RankOf(states, 0, false), Is.EqualTo(2));
            Assert.That(TeamRules.RankOf(states, 2, false), Is.EqualTo(2));
            Assert.That(MatchResults.Rank(states), Is.EqualTo(new[] { 1, 0, 2 }));
            Assert.That(TeamRules.Allies(states, 0, 2), Is.False, "チームのない人どうしは味方ではない");
        }

        [Test]
        public void Overtime_ComparesTeamTotals()
        {
            // チーム 1 の確定は 20、チーム 2 は 25。チーム 1 の 1 人が所持 10 なら、拠点に着けば 30 で逆転できる。
            List<CharacterState> states = States((1, 10, 0), (1, 10, 10), (2, 25, 0));
            Assert.That(MatchFlow.CanWinAtTheBank(states, 0), Is.True, "味方の所持も足して逆転できる");
            Assert.That(MatchFlow.IsSoleLeader(states, 2), Is.True);
            Assert.That(MatchFlow.AnyChallenger(states), Is.True);
            CharacterState poorer = states[1];
            poorer.Coins.Held = 2;
            states[1] = poorer;
            Assert.That(MatchFlow.CanWinAtTheBank(states, 0), Is.False, "合計 22 では 25 に届かない");
            Assert.That(MatchFlow.AnyChallenger(states), Is.False);
        }

        [Test]
        public void TheTeam_StaysAfterAKnockout()
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 0, RespawnDelayTicks = 10 };
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-20f, -10f, 20f, 30f), null, rules);
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            world.SetTeam(0, 3);
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(0f, -50f, 0f);
            world.SetState(0, state);
            for (int t = 0; t < 30; t++)
            {
                world.Step(new PlayerInput[1]);
            }
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).Combat.Team, Is.EqualTo(3), "撃破されて戻ったらチームがなくなった");
        }

        [Test]
        public void BalancedSplit_FollowsTheSizes_AndTheSeed()
        {
            var sizes = new[] { 3, 3, 2 };
            byte[] teams = TeamRules.Distribute(8, sizes, 42);
            var counts = new int[4];
            foreach (byte team in teams)
            {
                Assert.That(team, Is.InRange(1, 3));
                counts[team]++;
            }
            Assert.That(counts[1], Is.EqualTo(3));
            Assert.That(counts[2], Is.EqualTo(3));
            Assert.That(counts[3], Is.EqualTo(2));
            Assert.That(TeamRules.Distribute(8, sizes, 42), Is.EqualTo(teams), "同じ種で違う割り当て");
            var different = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                different.Add(string.Join(",", TeamRules.Distribute(8, sizes, seed)));
            }
            Assert.That(different.Count, Is.GreaterThan(5), "種を変えても割り当てがほとんど変わらない（ランダムでない）");
            Assert.Throws<System.ArgumentException>(() => TeamRules.Distribute(8, new[] { 4, 3 }, 0), "合計が人数と違う");
            Assert.Throws<System.ArgumentException>(() => TeamRules.Distribute(5, new[] { 5 }, 0), "チームが 1 つ");
        }

        [Test, Explicit("8 人の重さの計測（T9-1）。数値を docs/19 に書く。dotnet test --filter Name~EightPlayers で実行する")]
        public void EightPlayers_StepAndRollbackCost()
        {
            var rules = new MatchRules { CountdownTicks = 0 };
            var world = new SimWorld(TestArenas.Playground(), new StageBounds(-60f, -30f, 60f, 60f), null, rules);
            for (int i = 0; i < 8; i++)
            {
                world.AddCharacter(new CharacterConfig(), new Vector3(-21f + i * 6f, GroundY, 0f), i % 2 == 0 ? 1 : -1);
                world.SetTeam(i, (byte)(i % 4 + 1));
            }
            var inputs = new PlayerInput[8];
            var snapshot = new WorldSnapshot();
            // 温める（JIT）。
            for (int t = 0; t < 120; t++)
            {
                Fill(inputs, t);
                world.Step(inputs);
            }
            const int frames = 600;
            const int rollback = 8;
            var watch = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                // 1 フレームに、8 ティックを巻き戻して進め直す（予測の上限 8F の最悪の場合。D-34）＋1 ティック進める。
                world.SaveTo(snapshot);
                for (int r = 0; r < rollback; r++)
                {
                    Fill(inputs, f + r);
                    world.Step(inputs);
                }
                world.LoadFrom(snapshot);
                Fill(inputs, f);
                world.Step(inputs);
            }
            watch.Stop();
            double perFrame = watch.Elapsed.TotalMilliseconds / frames;
            TestContext.WriteLine($"8 人: 1 フレーム（8 ティックの巻き戻し＋1 ティック）あたり {perFrame:0.000} ms、1 ティックあたり {perFrame / (rollback + 1):0.000} ms");
            Assert.That(perFrame, Is.LessThan(16.6), "1 フレームの予算（60fps）に収まらない");
        }

        // 動き回る入力（走る・跳ぶ・攻撃・ワイヤー）。
        private static void Fill(PlayerInput[] inputs, int tick)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                int phase = (tick + i * 13) % 90;
                InputButtons buttons = InputButtons.None;
                if (phase % 30 < 2)
                {
                    buttons |= InputButtons.Jump;
                }
                if (phase % 17 < 2)
                {
                    buttons |= InputButtons.Attack;
                }
                if (phase > 60 && phase < 75)
                {
                    buttons |= InputButtons.Wire;
                }
                inputs[i] = PlayerInput.FromAxes(phase < 45 ? 1f : -1f, phase > 70 ? 1f : 0f, buttons);
            }
        }
    }
}
