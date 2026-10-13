using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Combat
{
    /// <summary>命中、ヒットストップ、のけぞり、吹き飛び（docs/11_combat_plan.md T3-2）。</summary>
    public class HitResolverTests
    {
        private const float GroundY = 0.91f;
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);
        private static readonly CombatTuning Combat = new CombatTuning();

        // 命中の仕組みを確かめるテストでは、地上技を1段目だけにする（二段技は GroundAttack_HitsTwice で確かめる）。
        private static readonly AttackHit Ground1 = Combat.Ground.Hits[0];

        private static CharacterConfig SingleHitConfig()
        {
            var config = new CharacterConfig();
            config.Combat.Ground.Hits = new[] { config.Combat.Ground.Hits[0] };
            return config;
        }

        // 地面の上に、指定した位置と向きでキャラクターを並べる（地上技は1段目だけ）。
        private static SimWorld World(params (float X, int Facing)[] characters)
        {
            var world = new SimWorld(TestArenas.Ground());
            foreach (var c in characters)
            {
                world.AddCharacter(SingleHitConfig(), new Vector3(c.X, GroundY, 0f), c.Facing);
            }
            world.Step(new PlayerInput[characters.Length]);
            return world;
        }

        private static void Step(SimWorld world, int ticks, params PlayerInput[] inputs)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
        }

        // victim がのけぞるまで進める（最大 maxTicks）。進めたティック数を返す。
        private static int StepUntilHit(SimWorld world, int victim, int maxTicks, params PlayerInput[] inputs)
        {
            for (int t = 1; t <= maxTicks; t++)
            {
                world.Step(inputs);
                if (world.GetState(victim).Action.Kind == ActionKind.Hitstun)
                {
                    return t;
                }
            }
            Assert.Fail($"{maxTicks} ティック以内に当たらなかった。攻撃した側: {world.GetState(0)}、当たる側: {world.GetState(victim)}");
            return -1;
        }

        [Test]
        public void GroundAttack_HitsTheDummy_AndRecordsTheAttacker()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });

            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            CharacterState attacker = world.GetState(0);
            CharacterState victim = world.GetState(1);
            Assert.That(attacker.Action.HitMask, Is.EqualTo(HitResolver.HitBit(0, 1)), "当てた判定と相手を覚える");
            Assert.That(victim.Action.Duration, Is.EqualTo(Ground1.HitstunTicks));
            Assert.That(victim.Combat.HasLastAttacker, Is.True);
            Assert.That(victim.Combat.LastAttacker, Is.EqualTo(0));
            Assert.That(victim.Combat.LastDamagedTick, Is.EqualTo(world.Tick));
            Assert.That(victim.Combat.ComboCount, Is.EqualTo(1));
            Assert.That(victim.Combat.ComboDamage, Is.EqualTo(Ground1.Damage));
            Assert.That(victim.Velocity, Is.EqualTo(new Vector3(Ground1.KnockbackX, Ground1.KnockbackY, 0f)),
                "攻撃した側の向きに吹き飛ぶ");
        }

        [Test]
        public void Hitstop_FreezesBoth_ThenTheyMoveAgain()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            CharacterState attacker = world.GetState(0);
            CharacterState victim = world.GetState(1);
            Assert.That(attacker.Combat.HitstopTicksLeft, Is.EqualTo(Ground1.HitstopTicks));
            Assert.That(victim.Combat.HitstopTicksLeft, Is.EqualTo(Ground1.HitstopTicks));

            Step(world, Ground1.HitstopTicks, default, default);
            Assert.That(world.GetState(0).Position, Is.EqualTo(attacker.Position), "当てた側はヒットストップの間止まる");
            Assert.That(world.GetState(1).Position, Is.EqualTo(victim.Position), "当たった側もヒットストップの間止まる");
            Assert.That(world.GetState(0).Action.Ticks, Is.EqualTo(attacker.Action.Ticks), "止まっている間は攻撃も進まない");

            world.Step(new PlayerInput[2]);
            Assert.That(world.GetState(1).Position.X, Is.GreaterThan(victim.Position.X), "ヒットストップが終わると吹き飛ぶ");
            Assert.That(world.GetState(0).Action.Ticks, Is.EqualTo(attacker.Action.Ticks + 1));
        }

        [Test]
        public void SameAttack_HitsTheSameTargetOnlyOnce()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            Step(world, Combat.Ground.TotalTicks, default, default);

            Assert.That(world.GetState(1).Combat.ComboCount, Is.EqualTo(1));
        }

        [Test]
        public void Hitstun_IgnoresInput_ThenReturnsToFree()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);
            CharacterState hit = world.GetState(1);

            var mash = PlayerInput.FromAxes(-1f, 0f, InputButtons.Jump | InputButtons.Wire | InputButtons.Attack);
            int ticks = Ground1.HitstopTicks + Ground1.HitstunTicks;
            for (int t = 0; t < ticks; t++)
            {
                world.Step(new[] { default(PlayerInput), mash });
                CharacterState s = world.GetState(1);
                Assert.That(s.Action.Kind, Is.EqualTo(ActionKind.Hitstun), $"{t} ティック目: のけぞりが早く終わった");
                Assert.That(s.Wire.Phase, Is.EqualTo(WirePhase.Ready), "のけぞり中はワイヤーを撃てない");
                Assert.That(s.Facing, Is.EqualTo(hit.Facing), "のけぞり中は向きが変わらない");
            }
            Assert.That(world.GetState(1).AirJumpsUsed, Is.EqualTo(0), "のけぞり中は空中ジャンプできない");

            world.Step(new PlayerInput[2]);
            Assert.That(world.GetState(1).Action.IsFree, Is.True, "のけぞりが終わると動ける");
        }

        [Test]
        public void Knockback_FollowsTheAttackersFacing()
        {
            SimWorld world = World((0f, -1), (-1.6f, 1));
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            Assert.That(world.GetState(1).Velocity.X, Is.EqualTo(-Ground1.KnockbackX), "左を向いた攻撃は左へ飛ばす");
        }

        // 空中で、攻撃する側（横の速さ vx）と、その斜め下にいる相手を置き、空中・下（撃破技）を当てる。相手の吹き飛びの速さを返す。
        private static Vector3 KoHit(float vx, out CharacterState attacker)
        {
            SimWorld world = KoWorld(vx);
            attacker = world.GetState(0);
            return world.GetState(1).Velocity;
        }

        // 横の速さ vx で空中を動いている 0 が撃破技（空中・下）を出し、下にいる 1 に当てる。当たったティックの直後のワールド。
        private static SimWorld KoWorld(float vx)
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 12f, 0f), 1);
            float lead = vx * Combat.AirDown.FirstHitTick / 60f;
            world.AddCharacter(new CharacterConfig(), new Vector3(lead + 0.5f, 10.4f, 0f), 1);
            CharacterState start = world.GetState(0);
            start.Velocity = new Vector3(vx, 0f, 0f);
            world.SetState(0, start);
            world.Step(new[] { PlayerInput.FromAxes(0f, -1f, InputButtons.Attack), default });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirDown), "前提: 空中・下（撃破技）");

            StepUntilHit(world, 1, Combat.AirDown.TotalTicks, default, default);
            return world;
        }

        [Test]
        public void KoMove_PowerIsScaledByTheStrikeSpeed_LikeTheKnockback()
        {
            // 撃破技の威力（ダメージ）にも、吹き飛びと同じ倍率をかける（ユーザーの要望、2026-10-13）。
            SimWorld world = KoWorld(8f);

            float scale = Combat.KoKnockbackScale(world.GetState(0).Action.StrikeSpeed);
            int expected = (int)System.MathF.Round(Combat.AirDown.Hits[0].Damage * scale);
            Assert.That(expected, Is.GreaterThan(Combat.AirDown.Hits[0].Damage), "前提: 倍率が 1 より大きい");
            Assert.That(world.LastHits[0].Damage, Is.EqualTo(expected));
            Assert.That(world.GetState(1).Combat.ComboDamage, Is.EqualTo(expected));
        }

        [Test]
        public void FastKoMove_IsFatal_WithTripleHitstop()
        {
            SimWorld world = KoWorld(14f);

            Assert.That(world.GetState(0).Action.StrikeSpeed, Is.GreaterThanOrEqualTo(Combat.FatalStrikeSpeed), "前提: 速く打った");
            Assert.That(world.LastHits[0].IsFatalHit, Is.True);
            int hitstop = Combat.AirDown.Hits[0].HitstopTicks * Combat.FatalHitstopMultiplier;
            Assert.That(world.GetState(0).Combat.HitstopTicksLeft, Is.EqualTo(hitstop), "攻撃した側のヒットストップが 3 倍でない");
            Assert.That(world.GetState(1).Combat.HitstopTicksLeft, Is.EqualTo(hitstop), "当たった側のヒットストップが 3 倍でない");
            Assert.That(world.GetState(1).Combat.FatalHitCount, Is.EqualTo(1));
        }

        [Test]
        public void SlowKoMove_IsNotFatal()
        {
            SimWorld world = KoWorld(0f);

            Assert.That(world.GetState(0).Action.StrikeSpeed, Is.LessThan(Combat.FatalStrikeSpeed), "前提: 遅く打った");
            Assert.That(world.LastHits[0].IsFatalHit, Is.False);
            Assert.That(world.GetState(1).Combat.HitstopTicksLeft, Is.EqualTo(Combat.AirDown.Hits[0].HitstopTicks));
            Assert.That(world.GetState(1).Combat.FatalHitCount, Is.EqualTo(0));
        }

        [Test]
        public void KoMove_SpikesDownward_ScaledByTheStrikeSpeed()
        {
            AttackHit hit = Combat.AirDown.Hits[0];
            Vector3 velocity = KoHit(8f, out CharacterState attacker);

            float scale = Combat.KoKnockbackScale(attacker.Action.StrikeSpeed);
            Assert.That(attacker.Action.StrikeSpeed, Is.GreaterThan(7f), "前提: 打つ直前の速さを覚えている");
            Assert.That(velocity.X, Is.EqualTo(hit.KnockbackX * scale).Within(1e-3f));
            Assert.That(velocity.Y, Is.EqualTo(hit.KnockbackY * scale).Within(1e-3f), "斜め下へたたき落とす");
            Assert.That(velocity.Y, Is.LessThan(0f));
        }

        [Test]
        public void LastHits_ReportsTheHitOfTheLastTickOnly()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            Assert.That(world.LastHits.Count, Is.EqualTo(1));
            HitRecord hit = world.LastHits[0];
            Assert.That(hit.Attacker, Is.EqualTo(0));
            Assert.That(hit.Victim, Is.EqualTo(1));
            Assert.That(hit.Attack, Is.EqualTo(AttackSlot.Ground));
            Assert.That(hit.HitIndex, Is.EqualTo(0));
            Assert.That(hit.KnockbackScale, Is.EqualTo(1f));
            Assert.That(hit.Knockback, Is.EqualTo(new Vector2(Ground1.KnockbackX, Ground1.KnockbackY)));
            Assert.That(world.GetState(1).Velocity, Is.EqualTo(new Vector3(hit.Knockback.X, hit.Knockback.Y, 0f)));

            world.Step(new PlayerInput[2]);
            Assert.That(world.LastHits.Count, Is.EqualTo(0), "次のティックには残らない");
        }

        [Test]
        public void Hitstun_KeepsKnockbackFasterThanTheMaxFallSpeed()
        {
            // のけぞり中は、落下の上限速度より速い下向きの吹き飛びを切らない（重力では加速しない）。のけぞりが終わると上限に戻る（D-43）。
            var config = new CharacterConfig();
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(config, new Vector3(0f, 200f, 0f));
            CharacterState state = world.GetState(0);
            state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = 10 };
            float knockback = -(config.Movement.MaxFallSpeed + 10f);
            state.Velocity = new Vector3(0f, knockback, 0f);
            world.SetState(0, state);

            Step(world, 5, default(PlayerInput));
            Assert.That(world.GetState(0).Velocity.Y, Is.EqualTo(knockback), "のけぞり中に上限で切られた");

            for (int i = 0; i < 20 && !world.GetState(0).Action.IsFree; i++)
            {
                world.Step(new PlayerInput[1]);
            }
            world.Step(new PlayerInput[1]);
            Assert.That(world.GetState(0).Velocity.Y, Is.EqualTo(-config.Movement.MaxFallSpeed), "のけぞりが終わっても上限に戻らない");
        }

        [Test]
        public void KoMove_KnocksFarther_WhenStrikingFaster()
        {
            Vector3 slow = KoHit(0f, out _);
            Vector3 fast = KoHit(14f, out _);

            Assert.That(fast.Length(), Is.GreaterThan(slow.Length() * 1.5f), "速く打つほど遠くへ飛ばす（D-38）");
        }

        [TestCase(0.4f)]
        [TestCase(1.0f)]
        [TestCase(1.6f)]
        [TestCase(2.6f)]
        public void GroundAttack_HitsTwice(float distance)
        {
            // デフォルトの地上技は二段技: 踏み込みの1段目と、斜め上へ飛び出す2段目が、同じ相手に1回ずつ当たる。
            // 近くから出しても、1段目で飛ばした相手を踏み込みで追い越さない（ユーザーのプレイテスト、2026-10-13）。
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(distance, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Attack, default });

            int maxCombo = 0;
            for (int t = 0; t < Combat.Ground.TotalTicks + 20; t++)
            {
                world.Step(new PlayerInput[2]);
                maxCombo = System.Math.Max(maxCombo, world.GetState(1).Combat.ComboCount);
            }

            Assert.That(maxCombo, Is.EqualTo(2), "2段とも当たる");
            Assert.That(world.GetState(1).Combat.ComboDamage, Is.EqualTo(Combat.Ground.Hits[0].Damage + Combat.Ground.Hits[1].Damage));
        }

        [TestCase(0.4f, 0f)]
        [TestCase(1.0f, 0f)]
        [TestCase(1.6f, 0f)]
        [TestCase(2.6f, 0f)]
        [TestCase(1.0f, 1f)]
        [TestCase(1.6f, 1f)]
        public void GroundAttack_ThenAnAirAttack_Connects(float distance, float up)
        {
            // 地上技（二段）の後、すぐに空中技（ニュートラル、または上）を出すと、のけぞっている相手にもう一度当たる
            // （キャンセルなしでつながる。ユーザーのプレイテスト、2026-10-13）。
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(distance, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Attack, default });
            for (int t = 0; t < 60 && !world.GetState(0).Action.IsFree; t++)
            {
                world.Step(new PlayerInput[2]);
            }
            Assert.That(world.GetState(1).Combat.ComboCount, Is.EqualTo(2), "前提: 2段とも当たる");

            world.Step(new[] { PlayerInput.FromAxes(0f, up, InputButtons.Attack), default });
            for (int t = 0; t < 30; t++)
            {
                world.Step(new PlayerInput[2]);
            }

            Assert.That(world.GetState(1).Combat.ComboCount, Is.EqualTo(3), "空中技がつながらない");
        }

        [Test]
        public void Hit_KnocksTheTargetOffItsWire()
        {
            // ぶら下がる側は巻き取らない設定にして、接続点の真下で止まったままにする。
            var hangingConfig = new CharacterConfig();
            hangingConfig.Wire.Inertial.ReelSpeed = 0f;
            hangingConfig.Wire.Inertial.StartPullSpeed = 0f;
            var world = new SimWorld(TestArenas.Playground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(hangingConfig, new Vector3(1.6f, 1.5f, 0f), -1);
            CharacterState hanging = world.GetState(1);
            hanging.Grounded = false;
            hanging.PreviousButtons = InputButtons.Wire;
            hanging.Wire = new WireState
            {
                Phase = WirePhase.Attached,
                Anchor = new Vector3(1.6f, 9f, 0f),
                RopeLength = 7.5f,
            };
            world.SetState(1, hanging);
            var holdWire = new PlayerInput(0, 0, InputButtons.Wire);
            world.Step(new[] { default, holdWire });
            Assert.That(world.GetState(0).Grounded, Is.True, "前提: 攻撃する側は地面にいる（地上技）");

            world.Step(new[] { Attack, holdWire });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, holdWire);

            CharacterState victim = world.GetState(1);
            Assert.That(victim.Wire.Phase, Is.Not.EqualTo(WirePhase.Attached), "攻撃を受けるとワイヤーが外れる【仮】");
            Assert.That(victim.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.Hit));
        }

        [Test]
        public void Trade_BothGetHit()
        {
            SimWorld world = World((0f, 1), (3.2f, -1));
            world.Step(new[] { Attack, Attack });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            CharacterState a = world.GetState(0);
            CharacterState b = world.GetState(1);
            Assert.That(a.Action.Kind, Is.EqualTo(ActionKind.Hitstun), "同じティックに当て合ったら、両方がのけぞる");
            Assert.That(b.Action.Kind, Is.EqualTo(ActionKind.Hitstun));
            Assert.That(a.Combat.LastAttacker, Is.EqualTo(1));
            Assert.That(b.Combat.LastAttacker, Is.EqualTo(0));
            Assert.That(a.Velocity.X, Is.LessThan(0f), "左へ飛ぶ");
            Assert.That(b.Velocity.X, Is.GreaterThan(0f), "右へ飛ぶ");
        }

        [Test]
        public void HitByAnotherAttackerDuringHitstun_StartsTheirOwnCombo()
        {
            // 0 と 2 で 1 を挟み、2 は少し遅れて攻撃する。1 がのけぞっている間に 2 が当てると、2 の新しいコンボになる
            // （コンボは当てた人ごと。ユーザーの指示、2026-10-13）。同じ人が当て続けるとつながる（GroundAttack_HitsTwice）。
            SimWorld world = World((-1.6f, 1), (0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default, default });
            Step(world, 3, default, default, default);
            world.Step(new[] { default, default, Attack });

            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default, default);
            for (int t = 0; t < Combat.Ground.TotalTicks && world.GetState(1).Combat.LastAttacker != 2; t++)
            {
                world.Step(new PlayerInput[3]);
            }

            CharacterState victim = world.GetState(1);
            Assert.That(victim.Combat.LastAttacker, Is.EqualTo(2), "前提: 2 がのけぞっている 1 に当てた");
            Assert.That(victim.Combat.ComboCount, Is.EqualTo(1), "別の人のコンボはつながらない");
            Assert.That(victim.Combat.ComboDamage, Is.EqualTo(Ground1.Damage));
        }

        [Test]
        public void FreshHit_AfterHitstun_StartsANewCombo()
        {
            SimWorld world = World((0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);
            Step(world, 120, default, default);
            Assert.That(world.GetState(1).Action.IsFree, Is.True, "前提: のけぞりが終わった");

            // 位置を戻して、もう一度当てる。
            CharacterState attacker = world.GetState(0);
            CharacterState victim = world.GetState(1);
            attacker.Position = new Vector3(0f, GroundY, 0f);
            attacker.Facing = 1;
            victim.Position = new Vector3(1.6f, GroundY, 0f);
            world.SetState(0, attacker);
            world.SetState(1, victim);
            world.Step(new[] { Attack, default });
            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default);

            Assert.That(world.GetState(1).Combat.ComboCount, Is.EqualTo(1), "のけぞりが切れた後の命中は、新しいコンボ");
            Assert.That(world.GetState(1).Combat.ComboDamage, Is.EqualTo(Ground1.Damage));
        }

        [Test]
        public void Rollback_ReproducesHits()
        {
            // 2人が向かい合って走り、攻撃し合う入力で、巻き戻しても全員が本当の入力どおりの状態になる。
            var scripts = new PlayerInput[2][];
            for (int p = 0; p < 2; p++)
            {
                float toward = p == 0 ? 1f : -1f;
                scripts[p] = new PlayerInput[900];
                for (int t = 0; t < scripts[p].Length; t++)
                {
                    int phase = (t + p * 11) % 40;
                    InputButtons buttons = phase == 0 || phase == 23 ? InputButtons.Attack : phase == 31 ? InputButtons.Jump : InputButtons.None;
                    scripts[p][t] = PlayerInput.FromAxes(phase < 30 ? toward : 0f, phase == 23 ? -1f : 0f, buttons);
                }
            }
            var network = new PseudoNetwork(scripts, new RollbackSettings(), 5, 2, 0.05, 9);

            network.RunUntil(700, maxFrames: 4000);

            SimWorld reference = network.Reference(700);
            Assert.That(reference.GetState(0).Combat.HasLastAttacker || reference.GetState(1).Combat.HasLastAttacker, Is.True,
                "前提: 攻撃が当たっている");
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Worlds[1].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 巻き戻している");
        }
    }
}
