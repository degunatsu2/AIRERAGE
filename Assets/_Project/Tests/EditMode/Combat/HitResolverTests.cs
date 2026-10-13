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

        // 地面の上に、指定した位置と向きでキャラクターを並べる。
        private static SimWorld World(params (float X, int Facing)[] characters)
        {
            var world = new SimWorld(TestArenas.Ground());
            foreach (var c in characters)
            {
                world.AddCharacter(new CharacterConfig(), new Vector3(c.X, GroundY, 0f), c.Facing);
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
            Assert.That(attacker.Action.HitMask, Is.EqualTo(1 << 1), "当てた相手を覚える");
            Assert.That(victim.Action.Duration, Is.EqualTo(Combat.Ground.HitstunTicks));
            Assert.That(victim.Combat.HasLastAttacker, Is.True);
            Assert.That(victim.Combat.LastAttacker, Is.EqualTo(0));
            Assert.That(victim.Combat.LastDamagedTick, Is.EqualTo(world.Tick));
            Assert.That(victim.Combat.ComboCount, Is.EqualTo(1));
            Assert.That(victim.Combat.ComboDamage, Is.EqualTo(Combat.Ground.Damage));
            Assert.That(victim.Velocity, Is.EqualTo(new Vector3(Combat.Ground.KnockbackX, Combat.Ground.KnockbackY, 0f)),
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
            Assert.That(attacker.Combat.HitstopTicksLeft, Is.EqualTo(Combat.Ground.HitstopTicks));
            Assert.That(victim.Combat.HitstopTicksLeft, Is.EqualTo(Combat.Ground.HitstopTicks));

            Step(world, Combat.Ground.HitstopTicks, default, default);
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
            int ticks = Combat.Ground.HitstopTicks + Combat.Ground.HitstunTicks;
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

            Assert.That(world.GetState(1).Velocity.X, Is.EqualTo(-Combat.Ground.KnockbackX), "左を向いた攻撃は左へ飛ばす");
        }

        [Test]
        public void AirDown_SpikesTheTargetDownward()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 8f, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(0.1f, 6.2f, 0f), 1);
            world.Step(new[] { PlayerInput.FromAxes(0f, -1f, InputButtons.Attack), default });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirDown), "前提: 空中の下攻撃");

            StepUntilHit(world, 1, Combat.AirDown.TotalTicks, default, default);

            Assert.That(world.GetState(1).Velocity.Y, Is.EqualTo(Combat.AirDown.KnockbackY), "真下へたたき落とす");
            Assert.That(Combat.AirDown.KnockbackY, Is.LessThan(0f));
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
        public void HitDuringHitstun_ContinuesTheCombo()
        {
            // 0 と 2 で 1 を挟み、2 は少し遅れて攻撃する。1 がのけぞっている間に当たれば、コンボがつながる。
            SimWorld world = World((-1.6f, 1), (0f, 1), (1.6f, -1));
            world.Step(new[] { Attack, default, default });
            Step(world, 3, default, default, default);
            world.Step(new[] { default, default, Attack });

            StepUntilHit(world, 1, Combat.Ground.TotalTicks, default, default, default);
            for (int t = 0; t < Combat.Ground.TotalTicks && world.GetState(1).Combat.ComboCount < 2; t++)
            {
                world.Step(new PlayerInput[3]);
            }

            CharacterState victim = world.GetState(1);
            Assert.That(victim.Combat.ComboCount, Is.EqualTo(2));
            Assert.That(victim.Combat.ComboDamage, Is.EqualTo(2 * Combat.Ground.Damage));
            Assert.That(victim.Combat.LastAttacker, Is.EqualTo(2), "最後にダメージを与えた人");
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
            Assert.That(world.GetState(1).Combat.ComboDamage, Is.EqualTo(Combat.Ground.Damage));
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
