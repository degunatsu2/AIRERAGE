using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// SOLDERER（docs/14 1.5。ユーザーの案、2026-10-21）: 通常攻撃（前後の衝撃波で跳ぶ、指先のクリティカル、反動のストンプ）、
    /// Q イナズマステップ（連続 3 回・長押し・回数で変わるクールタイム）、E インバータ（射程 1/3、鞭、雷の波動と感電）、R ボールライトニング。
    /// </summary>
    public class SoldererTests
    {
        private const float GroundY = 0.91f;
        private static readonly CharacterConfig Sol = CharacterPresets.Solderer();

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        private static void Step(SimWorld world, int ticks, params PlayerInput[] inputs)
        {
            var all = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < all.Length && i < inputs.Length; i++)
            {
                all[i] = inputs[i];
            }
            for (int t = 0; t < ticks; t++)
            {
                world.Step(all);
            }
        }

        private static void Edit(SimWorld world, int index, System.Func<CharacterState, CharacterState> edit)
        {
            world.SetState(index, edit(world.GetState(index)));
        }

        private static SimWorld World(ICollisionQuery terrain, Vector3 at, params Vector3[] dummies)
        {
            var world = new SimWorld(terrain);
            world.AddCharacter(CharacterPresets.Solderer(), at);
            foreach (Vector3 dummy in dummies)
            {
                world.AddCharacter(new CharacterConfig(), dummy);
            }
            return world;
        }

        [Test]
        public void Preset_IsValid()
        {
            Assert.DoesNotThrow(() => CharacterPresets.Solderer().Validate());
            Assert.That(Sol.Combat.KoAttack, Is.EqualTo(AttackSlot.AirSide));
            Assert.That(Sol.Combat.Special1CooldownTicks, Is.EqualTo(360), "1 回あたりのクールタイムが 6 秒でない");
            Assert.That(Sol.Combat.Special2CooldownTicks, Is.EqualTo(30), "インバータのクールタイムが 0.5 秒でない");
            Assert.That(Sol.Combat.SuperGaugeCost, Is.EqualTo(90 * CombatTuning.GaugePerPercent), "ボールライトニングが 90% でない");
        }

        [Test]
        public void ThunderKick_HitsFrontAndBack_AndJumpsStraightUpHigh()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(1.6f, GroundY, 0f), new Vector3(-1.6f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            float top = 0f;
            for (int t = 0; t < 70; t++)
            {
                Step(world, 1);
                top = System.Math.Max(top, world.GetState(0).Position.Y);
            }
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "前に当たらない");
            Assert.That(world.GetState(2).Combat.HasLastAttacker, Is.True, "後ろに当たらない");
            Assert.That(top, Is.GreaterThan(GroundY + 6f), "高く跳ばない");
            Assert.That(System.Math.Abs(world.GetState(0).Position.X), Is.LessThan(0.5f), "真上に跳ばない");
        }

        [TestCase(2.3f, 4.9f, true, TestName = "指先が体の中心に重なると FATAL")]
        [TestCase(1.4f, 5f, false, TestName = "通常の判定だけなら FATAL ではない")]
        public void FingerSpark_IsFatalOnlyWithTheCriticalHit(float x, float y, bool fatal)
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 6f, 0f), new Vector3(x, y, 0f));
            Step(world, 1, Press(InputButtons.Attack, 1f));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirSide));
            Step(world, 30);
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "当たらない");
            Assert.That(world.GetState(1).Combat.ComboCount, Is.EqualTo(1), "2 回当たった（ExclusiveHits）");
            Assert.That(world.GetState(1).Combat.FatalHitCount, Is.EqualTo(fatal ? 1 : 0));
        }

        [Test]
        public void FingerSpark_HasNoSpeedBonus()
        {
            // 撃破技の速さの係数は STANDARD だけ。SOLDERER の撃破技は速さで強くならない（ユーザーの指示、2026-10-22）。
            var state = new CharacterState { Action = new ActionState { Kind = ActionKind.Attack, Attack = AttackSlot.AirSide, StrikeSpeed = 40f } };
            Assert.That(AttackPower.Of(state, 0, Sol, 0).Fatal, Is.False, "速さで FATAL になった");
            Assert.That(AttackPower.Of(state, 0, Sol, 0).Multiplier, Is.EqualTo(1f), "速さで強くなっている");
        }

        [Test]
        public void ThunderFlip_Repeated_DoesNotClimbForever()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 20f, 0f));
            float start = world.GetState(0).Position.Y;
            int length = Sol.Combat.AirUp.TotalTicks;
            for (int k = 0; k < 8; k++)
            {
                Step(world, 1, Press(InputButtons.Attack, 0f, 1f));
                Step(world, length);
            }
            Assert.That(world.GetState(0).Position.Y, Is.LessThan(start - 2f), "続けて出すと上がり続ける");
        }

        [Test]
        public void Stomp_BouncesStraightUpOnHit()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 4.5f, 0f), new Vector3(0f, GroundY, 0f));
            Step(world, 1, Press(InputButtons.Attack, 0f, -1f));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirDown));
            float rise = -100f;
            for (int t = 0; t < 40; t++)
            {
                Step(world, 1);
                rise = System.Math.Max(rise, world.GetState(0).Velocity.Y);
            }
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "踏みつけが当たらない");
            Assert.That(rise, Is.GreaterThan(10f), "反動で跳ばない");
        }

        [Test]
        public void Stomp_EndsTheAttackOnHit_SoTheBounceIsNotCutShort()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 4.5f, 0f), new Vector3(0f, GroundY, 0f));
            Step(world, 1, Press(InputButtons.Attack, 0f, -1f));
            int t = 0;
            while (!world.GetState(1).Combat.HasLastAttacker && t++ < 40)
            {
                Step(world, 1);
            }
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "前提: 踏みつけが当たる");
            // ヒットストップが終わったら、攻撃は終わって上へ跳んでいる（降下の動きが残っていない）。
            Step(world, Sol.Combat.AirDown.Hits[0].HitstopTicks + 2);
            CharacterState after = world.GetState(0);
            Assert.That(after.Action.Kind, Is.EqualTo(ActionKind.Free), "当たっても攻撃が終わらない");
            Assert.That(after.Velocity.Y, Is.GreaterThan(8f), "反動で跳んでいない");
            float top = after.Position.Y;
            for (int k = 0; k < 20; k++)
            {
                Step(world, 1);
                top = System.Math.Max(top, world.GetState(0).Position.Y);
            }
            Assert.That(top, Is.GreaterThan(after.Position.Y + 1.5f), "跳んだ後すぐに降下する");
        }

        [Test]
        public void KnockedOutDuringTheChain_StartsTheCooldown_InsteadOfStayingActive()
        {
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-50f, -20f, 50f, 40f));
            world.AddCharacter(CharacterPresets.Solderer(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            TapStep(world, 1f, 0f);
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.EqualTo(CombatState.CooldownWhileActive), "前提: 続きを出せる");
            Edit(world, 0, s => { s.Position = new Vector3(0f, -30f, 0f); return s; });
            Step(world, 1);
            CharacterState knocked = world.GetState(0);
            Assert.That(knocked.Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(knocked.Combat.ChainCount, Is.Zero);
            Assert.That(knocked.Combat.Special1CooldownTicksLeft, Is.EqualTo(360), "撃破されたら、出した回数（1 回）のクールタイムにならない");
            Step(world, world.Rules.RespawnDelayTicks + 360 + 5);
            Assert.That(ActionController.CanUse(world.GetState(0), AttackSlot.Special1, Sol.Combat), Is.True, "クールタイムの後も使えない");
        }

        // Q を短く押す（移動の入力 x、y と一緒に）。
        private static void TapStep(SimWorld world, float x, float y)
        {
            Step(world, 1, Press(InputButtons.Special1, x, y));
            Step(world, 1, Press(InputButtons.None, x, y));
            Step(world, 14);
        }

        [Test]
        public void InazumaStep_ChainsThreeTimes_ThenCoolsDownByTheCount()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            TapStep(world, 1f, 0f);
            CharacterState first = world.GetState(0);
            Assert.That(first.Position.X, Is.GreaterThan(2.5f), "移動の入力の方向へ突撃しない");
            Assert.That(first.Combat.ChainCount, Is.EqualTo(1));
            Assert.That(first.Combat.Special1CooldownTicksLeft, Is.EqualTo(CombatState.CooldownWhileActive), "続けて出せない");
            TapStep(world, -1f, 0f);
            Assert.That(world.GetState(0).Position.X, Is.LessThan(first.Position.X - 2f), "2 回目が逆へ進まない");
            TapStep(world, 1f, 0f);
            CharacterState third = world.GetState(0);
            Assert.That(third.Combat.ChainCount, Is.Zero, "3 回で終わらない");
            Assert.That(third.Combat.Special1CooldownTicksLeft, Is.InRange(3 * 360 - 20, 3 * 360), "3 回分（18 秒）のクールタイムでない");
        }

        [Test]
        public void InazumaStep_FiresTheStrongDash_AsSoonAsItIsCharged()
        {
            // 長押しの判定になった瞬間に、離さなくても強突撃を出す（ユーザーの要望、2026-10-22）。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, Sol.Combat.Special1.ChargedFromTicks + 2, Press(InputButtons.Special1, 1f, 0f).WithAim(Vector2.UnitX));
            CharacterState state = world.GetState(0);
            Assert.That(state.Action.Kind, Is.EqualTo(ActionKind.Attack), "押したままでは出ない");
            Assert.That(state.Action.Attack, Is.EqualTo(AttackSlot.Special1));
            Assert.That(Sol.Combat.Special1.IsCharged(state.Action.Charge), Is.True, "強突撃でない");
            Assert.That(state.Combat.Holding, Is.False);
        }

        [Test]
        public void InazumaStep_Dashes_ALittleFartherThanBlinkKick()
        {
            // 短押しの突撃は SPACE TRAVELER の空中横（BLINK KICK）より少しだけ長い。長押しも前（8m）より短い（ユーザーの要望、2026-10-22）。
            float blink = MotionDistance(CharacterPresets.SpaceTraveler().Combat.AirSide, 0);
            float tapped = MotionDistance(Sol.Combat.Special1, 0);
            float charged = MotionDistance(Sol.Combat.Special1, Sol.Combat.Special1.ChargedFromTicks);
            Assert.That(tapped, Is.GreaterThan(blink).And.LessThan(blink + 1f), $"短押し {tapped}m、BLINK KICK {blink}m");
            Assert.That(charged, Is.InRange(tapped + 1f, 7f), "長押し");

            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1).WithAim(Vector2.UnitX));
            Step(world, 1, Press(InputButtons.None).WithAim(Vector2.UnitX));
            Step(world, 6);
            Assert.That(world.GetState(0).Position.X, Is.EqualTo(tapped).Within(0.5f), "突撃の距離");
            Assert.That(world.GetState(0).Velocity.X, Is.GreaterThan(10f), "突撃の後の慣性が残らない");
        }

        // 攻撃の動き（当たったときだけの動きを除く）で進む距離（m。向いている方向・照準の方向へ）。
        private static float MotionDistance(AttackData attack, int charge)
        {
            float distance = 0f;
            foreach (AttackMotion motion in attack.Motions)
            {
                if (!motion.RequiresHit && attack.Uses(motion.Charge, charge))
                {
                    distance += motion.VelocityX * motion.Ticks / 60f;
                }
            }
            return distance;
        }

        [Test]
        public void InazumaStep_GoesTowardThePointer_NotTheMoveInput()
        {
            // マウスポインターの方向へ（移動のキーとは逆でも。ユーザーの要望、2026-10-22）。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1, 1f, 0f).WithAim(new Vector2(-1f, 0f)));
            Step(world, 1, Press(InputButtons.None, 1f, 0f).WithAim(new Vector2(-1f, 0f)));
            Step(world, 14);
            Assert.That(world.GetState(0).Position.X, Is.LessThan(-2.5f), "ポインターの方向へ突撃しない");
            Assert.That(world.GetState(0).Facing, Is.EqualTo(-1), "ポインターの方を向かない");
        }

        [Test]
        public void InazumaStep_WindowExpires_AndAHoldEndsTheChain()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            TapStep(world, 1f, 0f);
            Step(world, Sol.Combat.Special1.ChainWindowTicks);
            Assert.That(world.GetState(0).Combat.ChainCount, Is.Zero, "3 秒で終わらない");
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.InRange(360 - 30, 360), "1 回分（6 秒）のクールタイムでない");

            SimWorld held = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(held, 2);
            TapStep(held, 1f, 0f);
            float x = held.GetState(0).Position.X;
            Step(held, 15, Press(InputButtons.Special1, 1f, 0f));
            Step(held, 1, Press(InputButtons.None, 1f, 0f));
            Assert.That(held.GetState(0).Action.Charge, Is.GreaterThanOrEqualTo(Sol.Combat.Special1.ChargedFromTicks), "前提: 長押し");
            Step(held, 30);
            Assert.That(held.GetState(0).Position.X, Is.GreaterThan(x + 5f), "長押しで長く突撃しない");
            Assert.That(held.GetState(0).Combat.ChainCount, Is.Zero, "長押しで終わらない");
            Assert.That(held.GetState(0).Combat.Special1CooldownTicksLeft, Is.InRange(2 * 360 - 40, 2 * 360), "2 回分（12 秒）のクールタイムでない");
        }

        [Test]
        public void Inverter_TogglesAndShortensTheWire()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special2));
            Step(world, 2);
            Assert.That(world.GetState(0).Combat.InverterOn, Is.True, "ON にならない");
            CharacterConfig effective = SpeedBoosts.EffectiveConfig(world.GetState(0), Sol);
            Assert.That(effective.Wire.MaxLength, Is.EqualTo(Sol.Wire.MaxLength / 3f).Within(1e-3f), "射程が 1/3 にならない");
            Step(world, 40);
            Step(world, 1, Press(InputButtons.Special2));
            Step(world, 2);
            Assert.That(world.GetState(0).Combat.InverterOn, Is.False, "OFF にならない");
        }

        [Test]
        public void Whip_SwingsWithTheAim_AndStopsTheTarget()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(3f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.InverterOn = true; return s; });
            Step(world, 2);
            // 何もない真上へ撃ち、押したまま。
            for (int t = 0; t < 12; t++)
            {
                Step(world, 1, Press(InputButtons.Wire).WithAim(Vector2.UnitY));
            }
            Assert.That(world.GetState(0).Wire.Whip, Is.True, "外れたワイヤーが鞭にならない");
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.False, "前提: まだ当たっていない");
            // 右へ振る。
            for (int t = 0; t < 4; t++)
            {
                Step(world, 1, Press(InputButtons.Wire).WithAim(Vector2.UnitX));
            }
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "鞭が当たらない");
            Assert.That(world.GetState(1).Action.Kind, Is.EqualTo(ActionKind.Hitstun), "のけぞらない");
            Assert.That(world.GetState(1).Velocity.X, Is.EqualTo(0f).Within(0.5f), "吹き飛んだ（慣性を止めない）");
            Step(world, 1, Press(InputButtons.None));
            Assert.That(world.GetState(0).Wire.Whip, Is.False, "離しても鞭のまま");
        }

        [Test]
        public void Whip_StaysOut_WhileAttacking_AsLongAsTheButtonIsHeld()
        {
            // 鞭（つながっていない電気のワイヤー）は、押している間は攻撃しても回収しない（ユーザーの要望、2026-10-22）。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 3f, 0f));
            Edit(world, 0, s => { s.Combat.InverterOn = true; return s; });
            Step(world, 2);
            for (int t = 0; t < 12; t++)
            {
                Step(world, 1, Press(InputButtons.Wire).WithAim(Vector2.UnitY));
            }
            Assert.That(world.GetState(0).Wire.Whip, Is.True, "前提: 鞭になった");
            Step(world, 1, Press(InputButtons.Wire | InputButtons.Attack).WithAim(Vector2.UnitY));
            for (int t = 0; t < 10; t++)
            {
                Step(world, 1, Press(InputButtons.Wire).WithAim(Vector2.UnitX));
                Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "前提: 攻撃している");
                Assert.That(world.GetState(0).Wire.Whip, Is.True, "攻撃したら鞭が消えた");
            }
            Assert.That(world.GetState(0).Wire.HookDirection.X, Is.GreaterThan(0.9f), "攻撃の間は鞭を振れない");
            Step(world, 1, Press(InputButtons.None));
            Assert.That(world.GetState(0).Wire.Whip, Is.False, "離しても鞭のまま");
        }

        [Test]
        public void ShockWave_HasAThreeSecondCooldown()
        {
            var terrain = new BoxTerrain().AddBox(-30f, -1f, 30f, 0f).AddBox(2f, 3f, 12f, 4f, attachable: true);
            SimWorld world = World(terrain, new Vector3(0f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.InverterOn = true; return s; });
            Step(world, 2);
            int Attach()
            {
                // つないで、離す（離した後のワイヤーのクールタイムも待つ）。波動の数を返す。
                for (int t = 0; t < 8; t++)
                {
                    Step(world, 1, Press(InputButtons.Wire).WithAim(new Vector2(1f, 1.2f)));
                }
                Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: 足場につないだ");
                int waves = world.Objects.Items.Count(o => o.Kind == WorldObjectKind.ShockWave);
                Step(world, 1, Press(InputButtons.None));
                Step(world, Sol.Wire.CooldownTicks + 2);
                Edit(world, 0, s => { s.Position = new Vector3(0f, GroundY, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 2);
                return waves;
            }
            Assert.That(Attach(), Is.EqualTo(1), "雷の波動が出ない");
            Assert.That(world.GetState(0).Combat.WaveCooldownTicks, Is.GreaterThan(0), "クールタイムが始まらない");
            int spawned = world.Objects.Items.Count(o => o.Kind == WorldObjectKind.ShockWave);
            Attach();
            Assert.That(world.Objects.Items.Count(o => o.Kind == WorldObjectKind.ShockWave), Is.LessThanOrEqualTo(spawned),
                "3 秒たたずに、また波動が出た");
            Step(world, Sol.Skills.Inverter.WaveCooldownTicks);
            Assert.That(world.GetState(0).Combat.WaveCooldownTicks, Is.Zero);
            Assert.That(Attach(), Is.GreaterThanOrEqualTo(1), "3 秒たっても波動が出ない");
        }

        [Test]
        public void ShockWave_RunsAroundTheBox_AndStunsWhoItTouches()
        {
            var terrain = new BoxTerrain().AddBox(-30f, -1f, 30f, 0f).AddBox(2f, 3f, 12f, 4f, attachable: true);
            SimWorld world = World(terrain, new Vector3(0f, GroundY, 0f), new Vector3(10f, 4.91f, 0f));
            Edit(world, 0, s => { s.Combat.InverterOn = true; return s; });
            Step(world, 2);
            for (int t = 0; t < 8; t++)
            {
                Step(world, 1, Press(InputButtons.Wire).WithAim(new Vector2(1f, 1.2f)));
            }
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: 足場につないだ");
            Assert.That(world.Objects.Items.Count(o => o.Kind == WorldObjectKind.ShockWave), Is.EqualTo(1), "雷の波動が出ない");
            bool stunned = false;
            for (int t = 0; t < 30 && !stunned; t++)
            {
                Step(world, 1, Press(InputButtons.Wire));
                stunned = world.GetState(1).Combat.IsStunned;
            }
            Assert.That(stunned, Is.True, "波動に触れて感電しない");
            Assert.That(world.GetState(0).Combat.IsStunned, Is.False, "持ち主が感電した");
            Vector3 at = world.GetState(1).Position;
            Step(world, 30);
            Assert.That(world.GetState(1).Position.X, Is.EqualTo(at.X).Within(0.01f), "感電している間に動いた");
            Step(world, 40);
            Assert.That(world.Objects.Items.Count(o => o.Kind == WorldObjectKind.ShockWave), Is.Zero, "一周しても消えない");
        }

        [Test]
        public void BallLightning_ThreeSlowBalls_StopAsTraps()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            Step(world, 2);
            for (int t = 0; t < 45; t++)
            {
                Step(world, 1, Press(InputButtons.Super).WithAim(Vector2.UnitX));
            }
            List<WorldObject> balls = world.Objects.Items.Where(o => o.Kind == WorldObjectKind.BallLightning).ToList();
            Assert.That(balls.Count, Is.EqualTo(3), "球が 3 つでない");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge - 90 * CombatTuning.GaugePerPercent));
            Step(world, 150);
            Assert.That(world.Objects.Items.Where(o => o.Kind == WorldObjectKind.BallLightning).All(o => ElectricRules.IsResting(o)), Is.True,
                "進んだ後に止まらない");
            Step(world, Sol.Skills.BallLightning.TrapTicks + 10);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.BallLightning), Is.False, "止まってから消えない");
        }

        [Test]
        public void BallLightning_HitsForDamage_StunsFirst_ThenKnocksBack_AndVanishes()
        {
            // 球は攻撃（威力 10・吹き飛び 3/5）で、追加効果の感電があり、当たったら消える（ユーザーの要望、2026-10-22）。
            // 先に感電してその場に止まり、感電が終わってから吹き飛ぶ（ユーザーの要望、2026-10-26）。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(4f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            Step(world, 2);
            AttackHit ballHit = Sol.Skills.BallLightning.Hit;
            Assert.That(ballHit.Damage, Is.EqualTo(10));
            Assert.That(ballHit.KnockbackX, Is.EqualTo(3f));
            Assert.That(ballHit.KnockbackY, Is.EqualTo(5f));
            HitRecord? hit = null;
            for (int t = 0; t < 60 && hit == null; t++)
            {
                Step(world, 1, Press(InputButtons.Super).WithAim(Vector2.UnitX));
                if (world.LastHits.Any(h => h.FromObject && h.Victim == 1))
                {
                    hit = world.LastHits.First(h => h.FromObject && h.Victim == 1);
                }
            }
            Assert.That(hit.HasValue, Is.True, "球が当たらない");
            Assert.That(hit.Value.Result, Is.EqualTo(HitResult.Hit));
            Assert.That(hit.Value.Damage, Is.EqualTo(10), "威力が 10 でない");
            Assert.That(world.Objects.Items.Count(o => o.Kind == WorldObjectKind.BallLightning), Is.EqualTo(2), "当たった球が消えない");
            CharacterState dummy = world.GetState(1);
            Assert.That(dummy.Action.Kind, Is.EqualTo(ActionKind.Hitstun), "のけぞりにならない（コンボと手柄が続かない）");
            Assert.That(dummy.Combat.IsStunned, Is.True, "当たってすぐに感電しない");
            Assert.That(dummy.Combat.HasStunKnockback, Is.True, "感電の後の吹き飛びを覚えない");
            Assert.That(dummy.Combat.ComboDamage, Is.GreaterThanOrEqualTo(10));
            Vector3 stunnedAt = dummy.Position;

            // 感電の間はその場に止まる（重力も受けない）。
            Edit(world, 0, s => { s.Position = new Vector3(-20f, GroundY, 0f); return s; });
            int stunTicks = 0;
            while (world.GetState(1).Combat.IsStunned && stunTicks < 200)
            {
                Step(world, 1);
                stunTicks++;
                if (world.GetState(1).Combat.IsStunned)
                {
                    Assert.That(Vector3.Distance(world.GetState(1).Position, stunnedAt), Is.LessThan(1e-3f), "感電の間に動いた");
                    Assert.That(world.GetState(1).Action.Kind, Is.EqualTo(ActionKind.Hitstun), "感電の間にのけぞりが終わった");
                }
            }
            Assert.That(stunTicks, Is.InRange(ballHit.StunTicks - 2, ballHit.StunTicks + ballHit.HitstopTicks + 2), "感電の長さが違う");
            CharacterState after = world.GetState(1);
            Assert.That(after.Action.Kind, Is.EqualTo(ActionKind.Hitstun), "感電の後にのけぞらない");
            Assert.That(after.Combat.HasStunKnockback, Is.False);
            Assert.That(after.Position.Y, Is.GreaterThan(stunnedAt.Y), "感電の後に吹き飛ばない");
            Assert.That(after.Velocity.X, Is.GreaterThan(0f), "球の進む向きに吹き飛ばない");
        }

        [Test]
        public void BallLightning_StunIsReplaced_ByTheNextHit()
        {
            // 感電の間にほかの攻撃を受けたら、その攻撃の吹き飛びとのけぞりで置き換わる（感電の後の吹き飛びは消える）。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(4f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            Step(world, 2);
            for (int t = 0; t < 60 && !world.GetState(1).Combat.IsStunned; t++)
            {
                Step(world, 1, Press(InputButtons.Super).WithAim(Vector2.UnitX));
            }
            Assert.That(world.GetState(1).Combat.HasStunKnockback, Is.True);
            // 撃った側を相手のすぐそばに置き、ほかの球が当たらないうちに地上技を当てる。
            Edit(world, 0, s => { s.Position = new Vector3(3f, GroundY, 0f); s.Facing = 1; s.Action = default; return s; });
            bool replaced = false;
            for (int t = 0; t < 40 && !replaced; t++)
            {
                Step(world, 1, t == 0 ? Press(InputButtons.Attack) : default(PlayerInput));
                replaced = world.LastHits.Any(h => !h.FromObject && h.Victim == 1 && h.Result == HitResult.Hit);
            }
            Assert.That(replaced, Is.True, "感電の間に攻撃が当たらない");
            Assert.That(world.GetState(1).Combat.HasStunKnockback, Is.False, "感電の後の吹き飛びが残った");
            Assert.That(world.GetState(1).Combat.IsStunned, Is.False, "攻撃を受けても感電が解けない");
        }

        [Test]
        public void BallLightning_EachBall_GoesWhereThePointerIsWhenItLaunches()
        {
            // R を短く押すだけで、球ごとに放つ瞬間のポインターの向きへ（ユーザーの要望、2026-10-22）。
            SimWorld world = World(new BoxTerrain(), new Vector3(0f, 20f, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            BallLightningTuning tuning = Sol.Skills.BallLightning;
            Vector2[] aims = { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX };
            Step(world, 1, Press(InputButtons.Super).WithAim(aims[0]));
            for (int t = 1; t < 2 + (tuning.Count - 1) * tuning.IntervalTicks + 4; t++)
            {
                // 2 つ目・3 つ目を放つ少し前に、ボタンを押さずにポインターだけ動かす。
                int index = Math.Min(tuning.Count - 1, Math.Max(0, (t - 2 + tuning.IntervalTicks / 2) / tuning.IntervalTicks));
                Step(world, 1, Press(InputButtons.None).WithAim(aims[index]));
                Edit(world, 0, s => { s.Velocity = Vector3.Zero; return s; });
            }
            List<WorldObject> balls = world.Objects.Items.Where(o => o.Kind == WorldObjectKind.BallLightning).OrderBy(o => o.SpawnTick).ToList();
            Assert.That(balls.Count, Is.EqualTo(3));
            Assert.That(balls[0].Velocity.X, Is.GreaterThan(5f), "1 つ目が右へ進まない");
            Assert.That(balls[1].Velocity.Y, Is.GreaterThan(5f), "2 つ目が上へ進まない（ボタンを離すとポインターを見ない）");
            Assert.That(balls[2].Velocity.X, Is.LessThan(-5f), "3 つ目が左へ進まない");
            Assert.That(tuning.Radius, Is.EqualTo(1.1f).Within(1e-4f), "球の大きさ（前の 2 倍）");
        }
    }
}
