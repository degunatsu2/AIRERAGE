using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Combat
{
    /// <summary>
    /// 特殊攻撃 2 つと必殺技（docs/13_character_plan.md T5-2、T5-3。D-53、D-55）: 出し方、特殊攻撃のクールタイム、必殺技のゲージ、
    /// キャンセル規則（通常攻撃と同じ扱い）、仮の技の動き。
    /// </summary>
    public class SpecialMoveTests
    {
        private const float GroundY = 0.91f;
        private static readonly CombatTuning Combat = new CombatTuning();

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        // 地形のない空中に1人だけ置く（しばらく落ち続ける）。
        private static SimWorld AirWorld()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 100f, 0f), 1);
            return world;
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input = default)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step(new[] { input });
            }
        }

        private static void SetGauge(SimWorld world, int gauge)
        {
            CharacterState state = world.GetState(0);
            state.Combat.Gauge = gauge;
            world.SetState(0, state);
        }

        // 今の攻撃が相手に当たったことにする（命中の仕組みは HitResolverTests で確かめる）。
        private static void MarkHit(SimWorld world)
        {
            CharacterState state = world.GetState(0);
            state.Action.HitMask = HitResolver.HitBit(0, 1);
            state.Action.Connected = true;
            world.SetState(0, state);
        }

        [Test]
        public void DefaultTuning_IsValid_AndHasTheSpecialMoves()
        {
            Assert.DoesNotThrow(() => new CharacterConfig().Validate());
            Assert.That(Combat.Get(AttackSlot.Special1), Is.SameAs(Combat.Special1));
            Assert.That(Combat.Get(AttackSlot.Special2), Is.SameAs(Combat.Special2));
            Assert.That(Combat.Get(AttackSlot.Super), Is.SameAs(Combat.Super));
            Assert.That(Combat.SuperGaugeCost, Is.EqualTo(100 * CombatTuning.GaugePerPercent), "D-55: 必殺技はゲージ 100%");
            Assert.That(CombatTuning.IsSpecial(AttackSlot.AirDown), Is.False);
            Assert.That(CombatTuning.IsSpecial(AttackSlot.Super), Is.True);
        }

        [Test]
        public void SuperGaugeCost_AboveTheMaximum_IsInvalid()
        {
            var config = new CharacterConfig();
            config.Combat.SuperGaugeCost = CombatTuning.MaxGauge + 1;

            Assert.Throws<InvalidTuningException>(() => config.Validate());
        }

        [TestCase(InputButtons.Special1, AttackSlot.Special1)]
        [TestCase(InputButtons.Special2, AttackSlot.Special2)]
        public void Special_StartsInTheAir_AndOnTheGround_AsTheSameMove(InputButtons button, AttackSlot slot)
        {
            SimWorld air = AirWorld();
            air.Step(new[] { Press(button, 0f, -1f) });
            Assert.That(air.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack));
            Assert.That(air.GetState(0).Action.Attack, Is.EqualTo(slot), "空中でスティックの向きに関係なく同じ技");

            var ground = new SimWorld(TestArenas.Ground());
            ground.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            ground.Step(new PlayerInput[1]);
            Assert.That(ground.GetState(0).Grounded, Is.True, "前提: 地上にいる");
            ground.Step(new[] { Press(button) });
            Assert.That(ground.GetState(0).Action.Attack, Is.EqualTo(slot), "地上でも地上技にならず同じ技");
        }

        [Test]
        public void Special1_StartsItsCooldown_AndCannotBeUsedAgainUntilItEnds()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Special1) });
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.EqualTo(Combat.Special1CooldownTicks));

            // 技が終わってから押しても、クールタイムの間は出ない（特殊攻撃 2 のクールタイムは別）。
            Step(world, Combat.Special1.TotalTicks);
            Assert.That(world.GetState(0).Action.IsFree, Is.True, "前提: 技が終わった");
            world.Step(new[] { Press(InputButtons.Special1) });
            Assert.That(world.GetState(0).Action.IsFree, Is.True, "クールタイム中に出た");
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.Zero);
            world.Step(new[] { Press(InputButtons.Special2) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special2), "もう一方の特殊攻撃は使える");

            // クールタイムが終わる少し前に押すと、先行入力で終わったときに出る。
            Step(world, Combat.Special2.TotalTicks);
            int left = world.GetState(0).Combat.Special1CooldownTicksLeft;
            Assert.That(left, Is.GreaterThan(Combat.AttackBufferTicks), "前提: まだクールタイムが残っている");
            Step(world, left - 3);
            world.Step(new[] { Press(InputButtons.Special1) });
            Assert.That(world.GetState(0).Action.IsFree, Is.True);
            Step(world, 2);
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special1), "クールタイムが終わったときに先行入力で出ない");
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.EqualTo(Combat.Special1CooldownTicks));
        }

        [Test]
        public void Super_NeedsTheGauge_AndUsesIt()
        {
            SimWorld world = AirWorld();
            SetGauge(world, Combat.SuperGaugeCost - 1);
            world.Step(new[] { Press(InputButtons.Super) });
            Assert.That(world.GetState(0).Action.IsFree, Is.True, "ゲージが足りないのに出た");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(Combat.SuperGaugeCost - 1), "出ないのにゲージが減った");

            Step(world, Combat.AttackBufferTicks + 1);
            SetGauge(world, CombatTuning.MaxGauge);
            world.Step(new[] { Press(InputButtons.Super) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Super));
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge - Combat.SuperGaugeCost));
        }

        [Test]
        public void PressingSeveralAtOnce_PrefersTheSuper_ThenTheSpecials_ThenTheAttack()
        {
            SimWorld world = AirWorld();
            SetGauge(world, CombatTuning.MaxGauge);
            world.Step(new[] { Press(InputButtons.Attack | InputButtons.Special1 | InputButtons.Special2 | InputButtons.Super) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Super));

            SimWorld second = AirWorld();
            second.Step(new[] { Press(InputButtons.Attack | InputButtons.Special1 | InputButtons.Special2) });
            Assert.That(second.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special2));

            SimWorld third = AirWorld();
            third.Step(new[] { Press(InputButtons.Attack | InputButtons.Special1) });
            Assert.That(third.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special1));
        }

        [Test]
        public void SpecialPressedWhileOnCooldown_DoesNotReleaseTheWire()
        {
            var harness = new SimHarness(TestArenas.Playground(), WireTestSetup.LongAttachConfig(WireModelType.Inertial));
            WireTestSetup.AttachAt(harness, new Vector3(0f, 5f, 0f), new Vector3(3f, 7f, 0f), new Vector3(0f, 6f, 0f));
            harness.Step(Inputs.Wire(), 5);
            CharacterState state = harness.State;
            state.Combat.Special1CooldownTicksLeft = 100;
            harness.State = state;

            harness.Step(Press(InputButtons.Special1 | InputButtons.Wire));
            Assert.That(harness.State.Wire.Phase, Is.EqualTo(WirePhase.Attached), "使えない技でワイヤーが外れた");
            Assert.That(harness.State.Action.IsFree, Is.True);

            harness.Step(Inputs.Wire(), Combat.AttackBufferTicks + 1);
            harness.Step(Press(InputButtons.Special2 | InputButtons.Wire));
            Assert.That(harness.State.Wire.Phase, Is.Not.EqualTo(WirePhase.Attached), "ワイヤー→技でワイヤーを外さない（D-19）");
            Assert.That(harness.State.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.Attack));
            Assert.That(harness.State.Action.Attack, Is.EqualTo(AttackSlot.Special2));
        }

        [Test]
        public void CancelIntoASpecial_OnHit_IsSeparateFromTheAttackCancel()
        {
            // 攻撃→特殊攻撃・必殺技のキャンセルは、攻撃→攻撃の 1 回とは別枠（ユーザーの要望、2026-10-15）。
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);
            world.Step(new[] { Press(InputButtons.Special1) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirNeutral), "空振りなのに特殊攻撃でキャンセルした");

            // 攻撃→攻撃の 1 回を先に使う。
            MarkHit(world);
            world.Step(new[] { Press(InputButtons.Attack, 1f) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirSide));
            Assert.That(world.GetState(0).Combat.AttackCancelUsed, Is.True, "前提: 攻撃→攻撃を使った");

            // 使った後でも、当たれば特殊攻撃へキャンセルできる（数えない）。
            Step(world, 8);
            MarkHit(world);
            world.Step(new[] { Press(InputButtons.Special1) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special1), "攻撃→攻撃を使った後に特殊攻撃でキャンセルできない");
            Assert.That(world.GetState(0).Action.Ticks, Is.Zero);

            // 技→通常攻撃は、攻撃→攻撃の 1 回に数える（もう使っているのでできない）。
            Step(world, 8);
            MarkHit(world);
            world.Step(new[] { Press(InputButtons.Attack, 0f, 1f) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special1), "連携の 2 回目の攻撃→攻撃ができた");

            // 技→もう一方の技・必殺技は、別枠なのでできる。
            world.Step(new[] { Press(InputButtons.Special2) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special2), "技→技でキャンセルできない");
            Step(world, 6);
            MarkHit(world);
            SetGauge(world, CombatTuning.MaxGauge);
            world.Step(new[] { Press(InputButtons.Super) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Super), "技→必殺技でキャンセルできない");
        }

        [Test]
        public void GroundAttackToSpecial_DoesNotUseTheAttackCancel()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.Step(new PlayerInput[1]);
            world.Step(new[] { Press(InputButtons.Attack) });
            Step(world, 6);
            MarkHit(world);

            world.Step(new[] { Press(InputButtons.Special2) });
            CharacterState state = world.GetState(0);
            Assert.That(state.Action.Attack, Is.EqualTo(AttackSlot.Special2));
            Assert.That(state.Combat.AttackCancelUsed, Is.False);
        }

        [Test]
        public void GuardCanBeCancelledIntoASpecial()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Guard) });
            Step(world, 3);
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard), "前提: ガード中");

            world.Step(new[] { Press(InputButtons.Special1) });
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special1));
            Assert.That(world.GetState(0).Combat.GuardCooldownTicksLeft, Is.GreaterThan(0), "キャンセルしたらガードのクールタイムが始まる");
        }

        [Test]
        public void Special1_DashesForwardAndKeepsItsHeight()
        {
            SimWorld world = AirWorld();
            world.Step(new[] { Press(InputButtons.Special1, -1f) });
            Assert.That(world.GetState(0).Facing, Is.EqualTo(-1), "押したときの向きを向かない");
            Vector3 start = world.GetState(0).Position;
            AttackMotion dash = Combat.Special1.Motions[1];
            Step(world, dash.StartTick + dash.Ticks - 1);

            Vector3 end = world.GetState(0).Position;
            Assert.That(start.X - end.X, Is.GreaterThan(3.5f), "左へ突進しない");
            Assert.That(end.Y, Is.EqualTo(start.Y).Within(0.01f), "突進の間に高さが変わった");
        }

        [Test]
        public void Special2_RisesHigherThanAJump()
        {
            SimWorld world = AirWorld();
            float startY = world.GetState(0).Position.Y;
            world.Step(new[] { Press(InputButtons.Special2) });
            float highest = startY;
            for (int t = 0; t < Combat.Special2.TotalTicks; t++)
            {
                world.Step(new PlayerInput[1]);
                highest = System.Math.Max(highest, world.GetState(0).Position.Y);
            }
            var movement = new MovementTuning();
            float jumpHeight = movement.JumpSpeed * movement.JumpSpeed / (2f * movement.Gravity);
            Assert.That(highest - startY, Is.GreaterThan(jumpHeight), "ジャンプより高く上がらない");
        }

        [Test]
        public void Super_HitsAroundTheUser_AndTheUserGainsNoGauge()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY + 2f, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(-3f, GroundY, 0f), 1);
            SetGauge(world, CombatTuning.MaxGauge);
            world.Step(new[] { Press(InputButtons.Super), default });

            HitRecord? hit = null;
            for (int t = 0; t < Combat.Super.TotalTicks && hit == null; t++)
            {
                world.Step(new PlayerInput[2]);
                if (world.LastHits.Count > 0)
                {
                    hit = world.LastHits[0];
                }
            }
            Assert.That(hit.HasValue, Is.True, "後ろにいる相手に当たらない");
            Assert.That(hit.Value.Attack, Is.EqualTo(AttackSlot.Super));
            Assert.That(world.GetState(1).Action.Kind, Is.EqualTo(ActionKind.Hitstun));
            Assert.That(world.GetState(0).Combat.Gauge, Is.Zero, "必殺技を当てた側がゲージを得た");
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(Combat.GaugeOnDamaged), "受けた側はゲージを得る");
        }

        [Test]
        public void Rollback_ReproducesTheSpecialsAndTheSuper()
        {
            // 2 人が向かい合って走り、通常攻撃・特殊攻撃 2 つ・必殺技（最初のゲージ 100）を出し合う入力で、遅延と欠落のある
            // 疑似ネットワークで巻き戻しても、全員が本当の入力どおりの状態になる（クールタイムとゲージの消費を含む）。
            var scripts = new PlayerInput[2][];
            for (int p = 0; p < 2; p++)
            {
                float toward = p == 0 ? 1f : -1f;
                scripts[p] = new PlayerInput[900];
                for (int t = 0; t < scripts[p].Length; t++)
                {
                    int phase = (t + p * 7) % 48;
                    InputButtons buttons = t == 2 + p * 30 ? InputButtons.Super
                        : phase == 0 ? InputButtons.Attack
                        : phase == 12 ? InputButtons.Special1
                        : phase == 26 ? InputButtons.Special2
                        : phase == 38 ? InputButtons.Jump
                        : InputButtons.None;
                    scripts[p][t] = PlayerInput.FromAxes(phase < 36 ? toward : 0f, 0f, buttons);
                }
            }
            void FullGauge(SimWorld world)
            {
                for (int i = 0; i < world.CharacterCount; i++)
                {
                    CharacterState state = world.GetState(i);
                    state.Combat.Gauge = CombatTuning.MaxGauge;
                    world.SetState(i, state);
                }
            }
            var network = new PseudoNetwork(scripts, new RollbackSettings(), 5, 2, 0.05, 11, FullGauge);

            network.RunUntil(700, maxFrames: 4000);

            Assert.That(network.Reference(10).GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Super), "前提: 必殺技を出している");
            SimWorld reference = network.Reference(700);
            Assert.That(reference.GetState(0).Combat.HasLastAttacker || reference.GetState(1).Combat.HasLastAttacker, Is.True,
                "前提: 攻撃が当たっている");
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Worlds[1].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 巻き戻している");
        }

        [Test]
        public void SpecialCooldowns_AreClearedByAKnockout_ButTheGaugeIsKept()
        {
            var world = new SimWorld(new BoxTerrain(), new StageBounds(-50f, -50f, 50f, 50f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 0f, 0f), 1);
            SetGauge(world, 40);
            world.Step(new[] { Press(InputButtons.Special2) });
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.GreaterThan(0), "前提: クールタイム中");

            CharacterState state = world.GetState(0);
            state.Position = new Vector3(0f, -60f, 0f);
            world.SetState(0, state);
            world.Step(new PlayerInput[1]);

            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.Zero);
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(40));
        }
    }
}
