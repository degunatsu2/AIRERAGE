using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Movement;
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
    /// PASTRY CHEF（docs/14 1.4。ユーザーの案、2026-10-21）: わたがしの足場（足元・照準の方向、弾く、ワイヤーを弾く）、クリーム（味の選び方、
    /// 張り付く、速さ、壁の張り付き・壁ジャンプ・ワイヤー）、マカロン（次々と投げる、色ごとの威力、止まって消える）。
    /// </summary>
    public class PastryChefTests
    {
        private const float GroundY = 0.91f;
        private static readonly CharacterConfig Chef = CharacterPresets.PastryChef();

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

        private static List<WorldObject> OfKind(SimWorld world, WorldObjectKind kind)
        {
            return world.Objects.Items.Where(o => o.Kind == kind).ToList();
        }

        // 右に壁（x = 5〜6）のある地面。
        private static BoxTerrain WallArena()
        {
            return new BoxTerrain().AddBox(-30f, -1f, 30f, 0f).AddBox(5f, 0f, 6f, 20f);
        }

        private static WorldObject Patch(int owner, CreamFlavor flavor, Vector2 center, Vector2 half, Vector2 normal)
        {
            return new WorldObject
            {
                Kind = WorldObjectKind.CreamPatch, Owner = owner, Slot = AttackSlot.Special2, Position = center, End = half,
                Velocity = normal, ExpireTick = int.MaxValue, Variant = (byte)flavor,
            };
        }

        [Test]
        public void Preset_IsValid()
        {
            Assert.DoesNotThrow(() => CharacterPresets.PastryChef().Validate());
            Assert.That(Chef.Combat.Special1.Effect, Is.EqualTo(SkillEffect.CottonCandy));
            Assert.That(Chef.Combat.Special1CooldownTicks, Is.EqualTo(480), "Q のクールタイムが 8 秒でない");
            Assert.That(Chef.Combat.Special2.Effect, Is.EqualTo(SkillEffect.Cream));
            Assert.That(Chef.Combat.Special2CooldownTicks, Is.EqualTo(1080), "E のクールタイムが 18 秒でない");
            Assert.That(Chef.Combat.Super.Effect, Is.EqualTo(SkillEffect.TeaParty));
            Assert.That(Chef.Combat.SuperGaugeCost, Is.EqualTo(CombatTuning.MaxGauge), "ティーパーティが 100% でない");
        }

        [Test]
        public void FluffyBase_Tap_PlacesAnUpwardPlatformUnderTheFeet_AndBouncesUp()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            List<WorldObject> candies = OfKind(world, WorldObjectKind.CottonCandy);
            Assert.That(candies.Count, Is.EqualTo(1), "わたがしの足場を置かない");
            Assert.That(candies[0].Velocity.Y, Is.EqualTo(1f).Within(1e-4f), "足元の足場が上を向いていない");
            Assert.That(candies[0].Position.Y, Is.LessThan(0.1f), "足元に置かない");
            Assert.That(candies[0].Position.X, Is.EqualTo(0f).Within(0.05f));
            Step(world, 2);
            Assert.That(world.GetState(0).Velocity.Y, Is.GreaterThan(Chef.Skills.CottonCandy.BouncePower * 0.8f), "上へ弾かない");
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.GreaterThan(Chef.Combat.Special1CooldownTicks - 10));

            Step(world, Chef.Skills.CottonCandy.DurationTicks);
            Assert.That(OfKind(world, WorldObjectKind.CottonCandy), Is.Empty, "10 秒で消えない");
        }

        [Test]
        public void FluffyBase_Hold_PlacesTheBaseCloseInTheAimDirection()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(0f, GroundY + 4f, 0f));
            var aim = new Vector2(1f, -1f);
            for (int t = 0; t < 20; t++)
            {
                Step(world, 1, Press(InputButtons.Special1).WithAim(aim));
            }
            Assert.That(OfKind(world, WorldObjectKind.CottonCandy), Is.Empty, "長押しの間に置いた");
            Assert.That(world.GetState(0).Combat.Holding, Is.True);
            Vector3 at = world.GetState(0).Position;
            Step(world, 2, Press(InputButtons.None).WithAim(aim));
            WorldObject candy = OfKind(world, WorldObjectKind.CottonCandy).Single();
            Vector2 expected = Vector2.Normalize(aim);
            Assert.That(candy.Velocity.X, Is.EqualTo(expected.X).Within(0.02f), "照準の方向を向いていない");
            Assert.That(candy.Velocity.Y, Is.EqualTo(expected.Y).Within(0.02f));
            float distance = Vector2.Distance(candy.Position, new Vector2(at.X, at.Y));
            Assert.That(distance, Is.EqualTo(Chef.Skills.CottonCandy.PlaceDistance).Within(0.6f), "目の前に置かない");
        }

        [Test]
        public void CottonCandy_BlocksTheWireHook_ButNotTheRope()
        {
            var collision = new WorldCollision(new BoxTerrain().AddBox(-30f, 10f, 30f, 11f, attachable: true));
            var objects = new WorldObjects();
            objects.Add(new WorldObject
            {
                Kind = WorldObjectKind.CottonCandy, Position = new Vector2(0f, 5f), Velocity = Vector2.UnitY,
                End = new Vector2(1.8f, 0.4f), ExpireTick = int.MaxValue,
            });
            collision.Refresh(objects);
            Assert.That(collision.RaycastHook(Vector3.Zero, Vector3.UnitY, 20f, out CastHit hit), Is.True);
            Assert.That(hit.Point.Y, Is.EqualTo(4.6f).Within(1e-3f), "足場の下の面で止まらない");
            Assert.That(hit.Attachable, Is.False, "わたがしにワイヤーがつながる");
            Assert.That(hit.Normal.Y, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(collision.Raycast(Vector3.Zero, Vector3.UnitY, 20f, out CastHit rope), Is.True);
            Assert.That(rope.Point.Y, Is.EqualTo(10f).Within(1e-3f), "ロープの遮りにわたがしを数えた");
            Assert.That(collision.RaycastHook(new Vector3(5f, 0f, 0f), Vector3.UnitY, 20f, out CastHit beside), Is.True);
            Assert.That(beside.Attachable, Is.True, "足場の横を通る線まで止めた");
        }

        [Test]
        public void SweetTrap_Tap_ShootsOrangeCreamThatSticksToTheWall()
        {
            var world = new SimWorld(WallArena());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(0f, GroundY + 2f, 0f));
            Step(world, 1, Press(InputButtons.Special2).WithAim(Vector2.UnitX));
            Step(world, 1, Press(InputButtons.None).WithAim(Vector2.UnitX));
            Step(world, 6);
            Assert.That(OfKind(world, WorldObjectKind.CreamShot).Count, Is.EqualTo(Chef.Skills.Cream.ShotCount), "扇形に飛ばさない");
            Step(world, 40);
            List<WorldObject> patches = OfKind(world, WorldObjectKind.CreamPatch);
            Assert.That(patches.Count, Is.GreaterThanOrEqualTo(3), "壁に張り付かない");
            Assert.That(patches.All(p => p.Variant == (byte)CreamFlavor.Orange), Is.True, "短く押してもオレンジでない");
            Assert.That(patches.Any(p => PastryRules.IsWallPatch(p) && p.Position.X < 5f && p.Position.X > 4.7f), Is.True,
                "壁の面に張り付いていない");
            Step(world, Chef.Skills.Cream.PatchTicks);
            Assert.That(OfKind(world, WorldObjectKind.CreamPatch), Is.Empty, "10 秒で消えない");
        }

        [Test]
        public void SweetTrap_HoldWithAttack_IsMatcha_AndWithWire_IsOrange_WithoutAttackingOrFiring()
        {
            var world = new SimWorld(WallArena());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 10, Press(InputButtons.Special2).WithAim(Vector2.UnitX));
            Step(world, 1, Press(InputButtons.Special2 | InputButtons.Wire).WithAim(Vector2.UnitX));
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Ready), "ワイヤーを撃った");
            Assert.That(world.GetState(0).Combat.HoldVariant, Is.EqualTo((byte)CreamFlavor.Orange));
            Step(world, 3, Press(InputButtons.Special2).WithAim(Vector2.UnitX));
            Step(world, 1, Press(InputButtons.Special2 | InputButtons.Attack).WithAim(Vector2.UnitX));
            Assert.That(world.GetState(0).Combat.HoldVariant, Is.EqualTo((byte)CreamFlavor.Matcha), "攻撃で抹茶にならない");
            Assert.That(world.GetState(0).Combat.Holding, Is.True, "攻撃でためるのが止まった");
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Free), "攻撃が出た");
            Step(world, 2, Press(InputButtons.Attack).WithAim(Vector2.UnitX));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special2), "離したときにクリームを出さない");
            Step(world, 40);
            List<WorldObject> patches = OfKind(world, WorldObjectKind.CreamPatch);
            Assert.That(patches.Count, Is.GreaterThan(0));
            Assert.That(patches.All(p => p.Variant == (byte)CreamFlavor.Matcha), Is.True, "抹茶のクリームでない");
        }

        [Test]
        public void Cream_OnTheFloor_ChangesSpeed_OrangeForEveryone_MatchaNotForTheOwner()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(20f, GroundY, 0f));
            world.Objects.Add(Patch(0, CreamFlavor.Orange, new Vector2(0f, 0.12f), new Vector2(3f, 0.12f), Vector2.UnitY));
            world.Objects.Add(Patch(0, CreamFlavor.Matcha, new Vector2(20f, 0.12f), new Vector2(3f, 0.12f), Vector2.UnitY));
            world.Objects.Add(Patch(1, CreamFlavor.Matcha, new Vector2(-20f, 0.12f), new Vector2(3f, 0.12f), Vector2.UnitY));
            Step(world, 2);
            Assert.That(world.GetState(0).Combat.CreamSpeedPercent, Is.EqualTo(Chef.Skills.Cream.OrangeSpeedPercent), "オレンジで速くならない");
            Assert.That(world.GetState(1).Combat.CreamSpeedPercent, Is.EqualTo(Chef.Skills.Cream.MatchaSpeedPercent), "抹茶で遅くならない");
            CharacterConfig fast = SpeedBoosts.EffectiveConfig(world.GetState(0), Chef);
            Assert.That(fast.Movement.RunSpeed, Is.EqualTo(Chef.Movement.RunSpeed * 1.3f).Within(1e-3f));
            CharacterConfig slow = SpeedBoosts.EffectiveConfig(world.GetState(1), new CharacterConfig());
            Assert.That(slow.Movement.RunSpeed, Is.EqualTo(new CharacterConfig().Movement.RunSpeed * 0.7f).Within(1e-3f));

            // 置いた本人は自分の抹茶で遅くならない。
            Edit(world, 0, s => { s.Position = new Vector3(-20f, GroundY, 0f); return s; });
            Edit(world, 1, s => { s.Position = new Vector3(-20f, GroundY, 0f); return s; });
            Step(world, 1);
            Assert.That(world.GetState(1).Combat.CreamSpeedPercent, Is.Zero, "自分の抹茶で遅くなった");
            Assert.That(world.GetState(0).Combat.CreamSpeedPercent, Is.EqualTo(Chef.Skills.Cream.MatchaSpeedPercent));

            // 離れると戻る。
            Edit(world, 0, s => { s.Position = new Vector3(10f, GroundY, 0f); return s; });
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.CreamSpeedPercent, Is.Zero, "クリームから離れても速さが戻らない");
        }

        [Test]
        public void Matcha_SlowsAttacks()
        {
            var normal = new SimWorld(TestArenas.Ground());
            normal.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            var slowed = new SimWorld(TestArenas.Ground());
            slowed.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f));
            slowed.AddCharacter(CharacterPresets.PastryChef(), new Vector3(20f, GroundY, 0f));
            slowed.Objects.Add(Patch(1, CreamFlavor.Matcha, new Vector2(0f, 0.12f), new Vector2(3f, 0.12f), Vector2.UnitY));
            Step(normal, 2);
            Step(slowed, 2);
            Step(normal, 1, Press(InputButtons.Attack));
            Step(slowed, 1, Press(InputButtons.Attack));
            Step(normal, 20);
            Step(slowed, 20);
            Assert.That(slowed.GetState(0).Action.Ticks, Is.LessThan(normal.GetState(0).Action.Ticks - 4), "抹茶の上で攻撃が遅くならない");
        }

        [Test]
        public void MatchaOnAWall_SticksForOneSecond_SlidingDown_ThenNotAgainUntilLeaving()
        {
            var world = new SimWorld(WallArena());
            world.AddCharacter(new CharacterConfig(), new Vector3(4.6f, 6f, 0f));
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(-20f, GroundY, 0f));
            world.Objects.Add(Patch(1, CreamFlavor.Matcha, new Vector2(4.875f, 6f), new Vector2(0.125f, 2f), -Vector2.UnitX));
            Step(world, 1);
            CharacterState stuck = world.GetState(0);
            Assert.That(stuck.Combat.CreamStickTicksLeft, Is.GreaterThan(0), "抹茶の壁に張り付かない");
            Assert.That(stuck.Combat.CreamNoCling, Is.True);
            float y = stuck.Position.Y;
            Step(world, 30, Press(InputButtons.Jump));
            CharacterState sliding = world.GetState(0);
            Assert.That(sliding.Combat.CreamStickTicksLeft, Is.GreaterThan(0));
            Assert.That(sliding.Position.Y, Is.LessThan(y - 0.3f).And.GreaterThan(y - 1f), "少しずつずり落ちない");
            Assert.That(sliding.Position.X, Is.EqualTo(4.6f).Within(0.01f), "張り付いている間に動けた");
            Step(world, Chef.Skills.Cream.MatchaStickTicks);
            CharacterState free = world.GetState(0);
            Assert.That(free.Combat.CreamStickTicksLeft, Is.Zero, "1 秒で離れない");
            Assert.That(free.WallSide, Is.Zero, "抹茶の壁に張り付けた");
        }

        [Test]
        public void OrangeOnAWall_LetsTheWireAttach_AndMatchaBlocksIt()
        {
            var terrain = new BoxTerrain().AddBox(5f, -10f, 6f, 10f).AddBox(-6f, -10f, -5f, 10f, attachable: true);
            var collision = new WorldCollision(terrain);
            var objects = new WorldObjects();
            objects.Add(Patch(0, CreamFlavor.Orange, new Vector2(4.875f, 0f), new Vector2(0.125f, 0.7f), -Vector2.UnitX));
            objects.Add(Patch(0, CreamFlavor.Matcha, new Vector2(-4.875f, 0f), new Vector2(0.125f, 0.7f), Vector2.UnitX));
            collision.Refresh(objects);
            Assert.That(collision.RaycastHook(Vector3.Zero, Vector3.UnitX, 20f, out CastHit orange), Is.True);
            Assert.That(orange.Attachable, Is.True, "オレンジの上にワイヤーがつながらない");
            Assert.That(collision.RaycastHook(new Vector3(0f, 3f, 0f), Vector3.UnitX, 20f, out CastHit bare), Is.True);
            Assert.That(bare.Attachable, Is.False, "クリームのない所までつながる");
            Assert.That(collision.RaycastHook(Vector3.Zero, -Vector3.UnitX, 20f, out CastHit matcha), Is.True);
            Assert.That(matcha.Attachable, Is.False, "抹茶の上にワイヤーがつながる");
        }

        [Test]
        public void OrangeWall_WallJumpIsOneAndAHalfTimes()
        {
            var tuning = new MovementTuning();
            var state = new CharacterState { WallSide = 1 };
            WallCling.Jump(ref state, tuning);
            var boosted = new CharacterState { WallSide = 1 };
            boosted.Combat.CreamWallJumpPercent = 150;
            WallCling.Jump(ref boosted, tuning);
            Assert.That(boosted.Velocity.X, Is.EqualTo(state.Velocity.X * 1.5f).Within(1e-4f));
            Assert.That(boosted.Velocity.Y, Is.EqualTo(state.Velocity.Y * 1.5f).Within(1e-4f));
        }

        [Test]
        public void TeaParty_ThrowsSixMacaronsOneAfterAnother_ThatStopAndVanish()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(0f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Super));
            Step(world, 6);
            List<WorldObject> macarons = OfKind(world, WorldObjectKind.Macaron);
            Assert.That(macarons.Count, Is.EqualTo(6), "マカロンが 6 個でない");
            Assert.That(world.GetState(0).Combat.Gauge, Is.Zero, "ゲージを 100% 使わない");
            int tick = world.Tick;
            Assert.That(macarons.Count(m => PastryRules.IsLaunched(m, tick)), Is.LessThan(6), "一度に全部投げた");
            Assert.That(macarons.Select(m => m.SpawnTick).Distinct().Count(), Is.EqualTo(6), "次々と投げない");
            Step(world, 60);
            Assert.That(OfKind(world, WorldObjectKind.Macaron).Any(m => m.Position.Y > 4f), Is.True, "上へ投げない");
            Step(world, 600);
            Assert.That(OfKind(world, WorldObjectKind.Macaron), Is.Empty, "止まって消えない");
        }

        [Test]
        public void TeaParty_ColorsFollowTheOdds()
        {
            TeaPartyTuning tuning = Chef.Skills.TeaParty;
            var objects = new WorldObjects();
            var counts = new int[3];
            for (int tick = 0; tick < 400; tick++)
            {
                objects.Clear();
                PastryRules.FireTeaParty(objects, new CharacterState(), 0, Chef, tick);
                foreach (WorldObject macaron in objects.Items)
                {
                    counts[macaron.Variant]++;
                }
            }
            float total = counts.Sum();
            Assert.That(counts[0] / total, Is.EqualTo(tuning.GreenPercent / 100f).Within(0.04f), "緑の割合");
            Assert.That(counts[1] / total, Is.EqualTo(tuning.YellowPercent / 100f).Within(0.04f), "黄の割合");
            Assert.That(counts[2] / total, Is.EqualTo(0.05f).Within(0.02f), "虹の割合");
        }

        [Test]
        public void Macarons_HitHarderAndFartherByColor()
        {
            float Hit(MacaronColor color, out int damage)
            {
                var world = new SimWorld(TestArenas.Ground());
                world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(-20f, GroundY, 0f));
                world.AddCharacter(new CharacterConfig(), new Vector3(0f, 5f, 0f));
                world.Objects.Add(new WorldObject
                {
                    Kind = WorldObjectKind.Macaron, Owner = 0, Slot = AttackSlot.Super, Position = new Vector2(-1f, 5f),
                    Velocity = new Vector2(10f, 0f), ExpireTick = int.MaxValue, Variant = (byte)color,
                    BonusPercent = Chef.Skills.TeaParty.BonusPercentOf(color),
                });
                Step(world, 8);
                Assert.That(world.LastHits.Count + world.GetState(1).Combat.ComboDamage, Is.GreaterThan(0), $"{color} が当たらない");
                damage = world.GetState(1).Combat.ComboDamage;
                Step(world, 40);
                return world.GetState(1).Position.X;
            }

            float green = Hit(MacaronColor.Green, out int greenDamage);
            float yellow = Hit(MacaronColor.Yellow, out int yellowDamage);
            float rainbow = Hit(MacaronColor.Rainbow, out int rainbowDamage);
            Assert.That(greenDamage, Is.EqualTo(20));
            Assert.That(yellowDamage, Is.EqualTo(40));
            Assert.That(rainbowDamage, Is.EqualTo(100));
            Assert.That(yellow, Is.GreaterThan(green + 1f), "黄が緑より遠くへ飛ばない");
            Assert.That(rainbow, Is.GreaterThan(yellow + 2f), "虹が黄より遠くへ飛ばない");
        }
    }
}
