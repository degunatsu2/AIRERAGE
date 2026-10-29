using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// 技で置く物は、地形に埋まるなら、重ならないいちばん近い所へ直す（PlacementRules。ユーザーの要望、2026-10-29）。THIEF が空中に置いた
    /// ベルトコンベアは、見た目どおりの足場を持つ。
    /// </summary>
    public class PlacementTests
    {
        private const float GroundY = 0.91f;

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

        [Test]
        public void FreePosition_KeepsAPlaceThatIsAlreadyFree()
        {
            BoxTerrain terrain = TestArenas.Ground();
            Vector2 at = PlacementRules.FreePosition(terrain, new Vector2(0f, 3f), new Vector2(0.5f, 0.5f), Vector2.Zero);
            Assert.That(at, Is.EqualTo(new Vector2(0f, 3f)));
        }

        [Test]
        public void FreePosition_PushesABoxOutOfAWall_ToTheNearestSide()
        {
            BoxTerrain terrain = new BoxTerrain().AddBox(2f, -5f, 3f, 5f);
            Vector2 at = PlacementRules.FreePosition(terrain, new Vector2(2.2f, 0f), new Vector2(0.5f, 0.5f), Vector2.Zero);
            Assert.That(PlacementRules.Overlaps(terrain, at, new Vector2(0.5f, 0.5f)), Is.False, "まだ埋まっている");
            Assert.That(at.X, Is.EqualTo(1.5f).Within(0.01f), "いちばん近い側（左）の、壁に触れる所に置いていない");
            Assert.That(at.Y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void FreePosition_KeepsThePlace_WhenNothingIsFreeNearby()
        {
            BoxTerrain terrain = new BoxTerrain().AddBox(-50f, -50f, 50f, 50f);
            Vector2 at = PlacementRules.FreePosition(terrain, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), Vector2.Zero);
            Assert.That(at, Is.EqualTo(Vector2.Zero));
        }

        [Test]
        public void MirrorBall_UnderALowCeiling_IsPlacedJustBelowIt()
        {
            // 天井の下面は y = 2.5（ミラーボールはキャラクターの中心の 2.2m 上 = 3.11 に置くので、そのままだと天井に埋まる）。
            BoxTerrain terrain = TestArenas.Ground().AddBox(-20f, 2.5f, 20f, 9f);
            var world = new SimWorld(terrain);
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Special1));
            Step(world, 30);
            WorldObject ball = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.MirrorBall);
            float radius = world.GetConfig(0).Skills.MirrorBall.Radius;
            Assert.That(ball.Position.Y + radius, Is.LessThanOrEqualTo(2.5f + 0.01f), "天井に埋まった");
            Assert.That(ball.Position.Y + radius, Is.GreaterThan(2.5f - 0.05f), "天井の下のいちばん近い所に置いていない");
            Assert.That(ball.Position.X, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void PlacedConveyor_InMidair_HasFootingThatCarriesPeople()
        {
            var lane = new StageZone(StageZoneKind.SpeedLane, 20f, 0f, 24f, 0.3f, Vector2.UnitX, 9f);
            var world = new SimWorld(TestArenas.Ground(), zones: new[] { lane });
            world.AddCharacter(CharacterPresets.Thief(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(-20f, GroundY, 0f));
            Step(world, 2);
            Place(world, new Vector2(1f, 0f));
            WorldObject placed = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.PlacedZone);
            Assert.That(placed.ChargeMilli, Is.EqualTo(1), "空中に置いたベルトコンベアに足場がない");
            WorldCollision.FootingBox(placed, out Vector2 min, out Vector2 max);
            Assert.That(min.Y, Is.GreaterThan(0.5f), "前提: 床から浮いた所に置いていない");

            // 足場の上に人を落とすと、乗って運ばれる。
            CharacterState rider = world.GetState(1);
            rider.Position = new Vector3(placed.Position.X - 1f, max.Y + 2f, 0f);
            rider.Velocity = Vector3.Zero;
            world.SetState(1, rider);
            Step(world, 40);
            rider = world.GetState(1);
            Assert.That(rider.Grounded, Is.True, "足場に乗れない");
            Assert.That(rider.Position.Y - world.GetConfig(1).Movement.HalfHeight, Is.EqualTo(max.Y).Within(0.05f), "足場の上に立っていない");
            Assert.That(rider.Velocity.X, Is.GreaterThan(5f), "ベルトコンベアで運ばれない");
        }

        [Test]
        public void PlacedConveyor_OnTheFloor_HasNoFooting_AndIsNotBuried()
        {
            var lane = new StageZone(StageZoneKind.SpeedLane, 20f, 0f, 24f, 0.3f, Vector2.UnitX, 9f);
            // 厚い床（上面 y = 0。埋まった物は、床の下より床の上のほうが近い）。
            var world = new SimWorld(new BoxTerrain().AddBox(-100f, -6f, 100f, 0f), zones: new[] { lane });
            world.AddCharacter(CharacterPresets.Thief(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            // 真下に置くと床に埋まるので、床の上へ直る（床に置いたベルトコンベアには足場はいらない）。
            Place(world, new Vector2(0f, -1f));
            WorldObject placed = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.PlacedZone);
            Assert.That(placed.Position.Y - placed.End.Y, Is.EqualTo(0f).Within(0.01f), "床の上に置いていない");
            Assert.That(placed.ChargeMilli, Is.EqualTo(0), "床に置いたベルトコンベアに足場を付けた");
        }

        // 奪ったことにして（ギミック 0）、照準 aim で Q を短く押して置く。
        private static void Place(SimWorld world, Vector2 aim)
        {
            CharacterState thief = world.GetState(0);
            thief.Combat.StealKind = StealKind.Zone;
            thief.Combat.StealSource = 0;
            thief.Combat.StealUses = 1;
            thief.Combat.Special1CooldownTicksLeft = 0;
            world.SetState(0, thief);
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Special1).WithAim(aim));
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.None).WithAim(aim));
            Step(world, 10);
        }
    }
}
