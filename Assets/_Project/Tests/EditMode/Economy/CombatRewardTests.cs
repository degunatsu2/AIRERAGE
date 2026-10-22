using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Economy
{
    /// <summary>
    /// 戦闘の報酬（docs/12_match_plan.md T4-5。ユーザーの決定、2026-10-14）: コンボが終わったらダメージの 30%（少なくとも 1 枚）、
    /// 撃破したら 20 枚＋順位の差 × 10 枚（上乗せは 30 枚まで）。同じ相手からの上限と減衰はない。
    /// </summary>
    public class CombatRewardTests
    {
        private const float GroundY = 0.91f;
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);
        private static readonly PlayerInput Guard = new PlayerInput(0, 0, InputButtons.Guard);
        private static readonly StageBounds Bounds = new StageBounds(-20f, -10f, 20f, 30f);

        // 地上技を1段目だけにした設定（コンボのダメージを数えやすくする）。
        private static CharacterConfig SingleHitConfig()
        {
            var config = new CharacterConfig();
            config.Combat.Ground.Hits = new[] { config.Combat.Ground.Hits[0] };
            return config;
        }

        private static SimWorld World(params (float X, int Facing)[] characters)
        {
            var world = new SimWorld(TestArenas.Ground(), Bounds, null, MatchRules.FreePlay());
            foreach (var c in characters)
            {
                world.AddCharacter(SingleHitConfig(), new Vector3(c.X, GroundY, 0f), c.Facing);
            }
            world.Step(new PlayerInput[characters.Length]);
            return world;
        }

        private static int Damage => new CombatTuning().Ground.Hits[0].Damage;

        private static void StepUntil(SimWorld world, System.Func<SimWorld, bool> condition, int maxTicks, params PlayerInput[] inputs)
        {
            for (int t = 0; t < maxTicks; t++)
            {
                if (condition(world))
                {
                    return;
                }
                world.Step(inputs);
            }
            Assert.That(condition(world), Is.True, $"{maxTicks} ティック以内に条件を満たさなかった");
        }

        private static List<CharacterState> States(SimWorld world)
        {
            var states = new List<CharacterState>();
            for (int i = 0; i < world.CharacterCount; i++)
            {
                states.Add(world.GetState(i));
            }
            return states;
        }

        private static CharacterState WithBanked(int banked)
        {
            return new CharacterState { Coins = new CoinPurse { Banked = banked } };
        }

        [Test]
        public void DamageReward_EqualsTheDamage()
        {
            // 威力とコインは等価（2026-10-22。前は 30%）。割合を変えれば端数は切り捨て、少なくとも 1 枚。
            var rules = new MatchRules();
            Assert.That(CombatRewardRules.DamageRewardAmount(rules, 10), Is.EqualTo(10));
            Assert.That(CombatRewardRules.DamageRewardAmount(rules, 1), Is.EqualTo(1));
            Assert.That(CombatRewardRules.DamageRewardAmount(rules, 0), Is.EqualTo(0));
            var thirty = new MatchRules { DamageRewardPercent = 30 };
            Assert.That(CombatRewardRules.DamageRewardAmount(thirty, 8), Is.EqualTo(2), "端数は切り捨て");
            Assert.That(CombatRewardRules.DamageRewardAmount(thirty, 1), Is.EqualTo(1), "少なくとも 1 枚");
            Assert.That(CombatRewardRules.DamageRewardAmount(new MatchRules { DamageRewardPercent = 0 }, 10), Is.EqualTo(0), "0% なら払わない");
        }

        [Test]
        public void AComboEnding_PaysTheAttacker_WhenTheHitstunEnds()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntil(world, w => w.GetState(1).Action.Kind == ActionKind.Hitstun, 40, default, default);

            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0), "コンボの途中で払った");
            Assert.That(world.GetState(1).Combat.RewardDamage, Is.EqualTo(Damage), "前提: 報酬の元を積む");

            StepUntil(world, w => w.GetState(1).Action.Kind != ActionKind.Hitstun, 200, default, default);
            int expected = CombatRewardRules.DamageRewardAmount(world.Rules, Damage);
            Assert.That(expected, Is.GreaterThan(0), "前提");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(expected), "コンボが終わったのに払わない");
            Assert.That(world.GetState(1).Combat.RewardDamage, Is.EqualTo(0), "払った後も報酬の元が残る");
            Assert.That(world.GetState(1).Coins.Held, Is.EqualTo(0), "当たった人のコインが増えた");
            Assert.That(world.Coins.Totals(States(world)).IsConserved, Is.True);

            // その後は払わない（二重に払わない）。
            world.Step(new PlayerInput[2]);
            world.Step(new PlayerInput[2]);
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(expected), "二重に払った");
        }

        [Test]
        public void AnotherAttackersHit_EndsTheCombo_AndPaysTheFirstAttackerAtOnce()
        {
            // 0 と 2 で 1 を挟み、2 は少し遅れて攻撃する（HitResolverTests.HitByAnotherAttackerDuringHitstun_StartsTheirOwnCombo）。
            SimWorld world = World((-2.4f, 1), (0f, 1), (2.4f, -1));
            world.Step(new[] { Attack, default, default });
            for (int i = 0; i < 3; i++)
            {
                world.Step(new PlayerInput[3]);
            }
            world.Step(new[] { default, default, Attack });
            StepUntil(world, w => w.GetState(1).Combat.HasLastAttacker && w.GetState(1).Combat.LastAttacker == 2, 60, default, default, default);

            int reward = CombatRewardRules.DamageRewardAmount(world.Rules, Damage);
            Assert.That(world.GetState(1).Action.Kind, Is.EqualTo(ActionKind.Hitstun), "前提: まだのけぞっている");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(reward), "別の人の新しいコンボになったのに、前のコンボを払わない");
            Assert.That(world.GetState(2).Coins.Held, Is.EqualTo(0), "2 のコンボはまだ終わっていない");
            Assert.That(world.GetState(1).Combat.RewardAttacker, Is.EqualTo(2));

            StepUntil(world, w => w.GetState(1).Action.Kind != ActionKind.Hitstun, 200, default, default, default);
            Assert.That(world.GetState(2).Coins.Held, Is.EqualTo(reward), "2 のコンボが終わったのに払わない");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(reward), "0 に二重に払った");
        }

        [Test]
        public void AKnockout_PaysTheComboAndTheKoReward()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntil(world, w => w.GetState(1).Action.Kind == ActionKind.Hitstun, 40, default, default);

            // のけぞっている間に、ステージの外へ出す。
            CharacterState victim = world.GetState(1);
            victim.Position = new Vector3(30f, 5f, 0f);
            world.SetState(1, victim);
            world.Step(new PlayerInput[2]);

            Assert.That(world.LastKnockouts.Count, Is.EqualTo(1), "前提: 撃破した");
            int expected = CombatRewardRules.DamageRewardAmount(world.Rules, Damage) + world.Rules.KoRewardBase;
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(expected), "コンボの分と撃破報酬（順位が同じなら基本の 20 枚）を払わない");
            Assert.That(world.GetState(1).Combat.RewardDamage, Is.EqualTo(0));
            Assert.That(world.Coins.Totals(States(world)).IsConserved, Is.True);
        }

        [Test]
        public void KoReward_GrowsWithTheRankDifference_UpToTheMax()
        {
            var rules = new MatchRules();
            // 確定コイン: 0 番 30（1 位）、1 番 20（2 位）、2 番 10（3 位）、3 番 0（4 位）。
            var states = new[] { WithBanked(30), WithBanked(20), WithBanked(10), WithBanked(0) };

            Assert.That(CombatRewardRules.KoRewardAmount(rules, states, 3, 0), Is.EqualTo(50), "4 位が首位を撃破: 20 + 30");
            Assert.That(CombatRewardRules.KoRewardAmount(rules, states, 2, 0), Is.EqualTo(40), "3 位が首位: 20 + 20");
            Assert.That(CombatRewardRules.KoRewardAmount(rules, states, 1, 0), Is.EqualTo(30), "2 位が首位: 20 + 10");
            Assert.That(CombatRewardRules.KoRewardAmount(rules, states, 3, 2), Is.EqualTo(30), "4 位が 3 位: 20 + 10");
            Assert.That(CombatRewardRules.KoRewardAmount(rules, states, 0, 3), Is.EqualTo(20), "上の順位の人が下を撃破: 基本だけ");
            Assert.That(CombatRewardRules.KoRewardAmount(rules, states, -1, 0), Is.EqualTo(0), "自滅は払わない");

            var bigSteps = new MatchRules { KoRewardPerRank = 25 };
            Assert.That(CombatRewardRules.KoRewardAmount(bigSteps, states, 3, 0), Is.EqualTo(50), "上乗せは上限まで");

            var tied = new[] { WithBanked(10), WithBanked(10), WithBanked(0) };
            Assert.That(CombatRewardRules.RankOf(tied, 0), Is.EqualTo(1));
            Assert.That(CombatRewardRules.RankOf(tied, 1), Is.EqualTo(1), "同じ数なら同じ順位");
            Assert.That(CombatRewardRules.RankOf(tied, 2), Is.EqualTo(3));
            Assert.That(CombatRewardRules.KoRewardAmount(rules, tied, 1, 0), Is.EqualTo(20), "同じ順位なら上乗せなし");
            Assert.That(CombatRewardRules.KoRewardAmount(rules, tied, 2, 0), Is.EqualTo(40), "3 位が同点の首位: 20 + 20");
        }

        [Test]
        public void TheSameTarget_PaysEveryTime_WithoutDecay()
        {
            // 同じ相手からの上限と減衰はない（ユーザーの決定）。
            SimWorld world = World((0f, 1), (1.6f, -1));
            int paid = 0;
            for (int round = 0; round < 3; round++)
            {
                CharacterState attacker = world.GetState(0);
                CharacterState victim = world.GetState(1);
                attacker.Position = new Vector3(0f, GroundY, 0f);
                attacker.Facing = 1;
                victim.Position = new Vector3(1.6f, GroundY, 0f);
                world.SetState(0, attacker);
                world.SetState(1, victim);
                world.Step(new[] { Attack, default });
                StepUntil(world, w => w.GetState(1).Action.Kind == ActionKind.Hitstun, 40, default, default);
                StepUntil(world, w => w.GetState(1).Action.IsFree && w.GetState(0).Action.IsFree, 200, default, default);
                paid += CombatRewardRules.DamageRewardAmount(world.Rules, Damage);
                Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(paid), $"{round + 1} 回目で減った");
            }
        }

        [Test]
        public void AGuardedHit_PaysNothing()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            for (int i = 0; i < 20; i++)
            {
                world.Step(new[] { default, Guard });
            }
            world.Step(new[] { Attack, Guard });
            for (int i = 0; i < 80; i++)
            {
                world.Step(new[] { default, Guard });
            }
            Assert.That(world.GetState(1).Combat.GuardedCount, Is.GreaterThan(0), "前提: ガードした");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0), "ガードされた攻撃で払った");
        }

        [Test]
        public void ASelfKnockout_PaysNoKoReward()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            CharacterState victim = world.GetState(1);
            victim.Position = new Vector3(30f, 5f, 0f);
            world.SetState(1, victim);
            world.Step(new PlayerInput[2]);

            Assert.That(world.LastKnockouts.Count, Is.EqualTo(1), "前提: 外に出た");
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(0));
            Assert.That(world.Coins.Field.Generated, Is.EqualTo(0), "だれかに報酬を払った");
        }

        [Test]
        public void RollingBack_PaysTheSameRewards()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            world.Step(new[] { Attack, default });
            for (int i = 0; i < 90; i++)
            {
                world.Step(new PlayerInput[2]);
            }
            ulong expected = world.Checksum();
            int held = world.GetState(0).Coins.Held;
            Assert.That(held, Is.GreaterThan(0), "前提: 払った");

            world.LoadFrom(snapshot);
            world.Step(new[] { Attack, default });
            for (int i = 0; i < 90; i++)
            {
                world.Step(new PlayerInput[2]);
            }
            Assert.That(world.GetState(0).Coins.Held, Is.EqualTo(held));
            Assert.That(world.Checksum(), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidRewardRules_AreRejected()
        {
            Assert.Throws<Airerage.Core.Common.InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { DamageRewardPercent = -1 }));
            Assert.Throws<Airerage.Core.Common.InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { KoRewardBase = -1 }));
            Assert.Throws<Airerage.Core.Common.InvalidTuningException>(() => new SimWorld(TestArenas.Ground(), null, null, new MatchRules { KoRewardMaxBonus = -5 }));
        }
    }
}
