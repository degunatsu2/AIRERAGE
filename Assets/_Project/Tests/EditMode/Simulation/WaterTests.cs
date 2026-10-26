using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using NUnit.Framework;

namespace Airerage.Core.Tests.Simulation
{
    /// <summary>
    /// 水（StageZoneKind.Water。ユーザーの要望、2026-10-26）: 着水すると 5 秒は浮けて、その後は溺れて沈み、撃破される。着水の勢いが
    /// 強すぎると撃破の線まで沈んで撃破される。陸に着くかワイヤーをつなぐと、溺れるまでの残りが戻る。
    /// </summary>
    public class WaterTests
    {
        private const float Surface = -4f;
        private const float Bottom = -10f;
        private const float PlungeSpeed = 24f;
        private static readonly MovementTuning Movement = new MovementTuning();

        // 左に陸（x -20〜-10、上面 y = 0）、右は海（水面 y = -4、底は撃破の線 y = -10）。
        private static SimWorld World()
        {
            BoxTerrain terrain = new BoxTerrain().AddBox(-20f, -30f, -10f, 0f);
            var water = new StageZone(StageZoneKind.Water, -10f, Bottom, 40f, Surface, Vector2.Zero, 5f, PlungeSpeed);
            var world = new SimWorld(terrain, new StageBounds(-50f, Bottom, 50f, 40f), new[] { water });
            world.AddCharacter(new CharacterConfig(), new Vector3(-15f, Movement.HalfHeight, 0f), 1);
            return world;
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input = default)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step(new[] { input });
            }
        }

        private static void Place(SimWorld world, Vector3 position, Vector3 velocity)
        {
            CharacterState state = world.GetState(0);
            state.Position = position;
            state.Velocity = velocity;
            state.Grounded = false;
            world.SetState(0, state);
        }

        [Test]
        public void FallingIn_FloatsAtTheSurface()
        {
            SimWorld world = World();
            Place(world, new Vector3(5f, 2f, 0f), Vector3.Zero);
            Step(world, 200);

            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.SplashCount, Is.EqualTo(1), "着水を数えない");
            Assert.That(state.Position.Y, Is.EqualTo(Surface - StageZones.WaterFloatDepth).Within(0.15f), "水面に浮かない");
            Assert.That(state.Velocity.Y, Is.EqualTo(0f).Within(0.5f));
            Assert.That(state.Combat.WaterTicksLeft, Is.InRange(1, 5 * 60 - 1), "溺れるまでの残りを数えない");
            Assert.That(state.Combat.WaterFloatTicks, Is.EqualTo(5 * 60));
            Assert.That(state.Combat.IsDrowning, Is.False);
        }

        [Test]
        public void Swimming_IsSlow()
        {
            SimWorld world = World();
            Place(world, new Vector3(5f, Surface - StageZones.WaterFloatDepth, 0f), new Vector3(15f, 0f, 0f));
            Step(world, 40, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            Assert.That(world.GetState(0).Velocity.X, Is.LessThan(StageZones.WaterSwimSpeed + 1f), "水の中で速く動ける");
            Assert.That(world.GetState(0).Velocity.X, Is.GreaterThan(1f), "泳げない");
        }

        [Test]
        public void WhileFloating_YouCanJumpOutOfTheWater()
        {
            SimWorld world = World();
            Place(world, new Vector3(5f, 2f, 0f), Vector3.Zero);
            Step(world, 90);
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Jump));
            float highest = world.GetState(0).Position.Y;
            for (int i = 0; i < 30; i++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Jump));
                highest = System.MathF.Max(highest, world.GetState(0).Position.Y);
            }
            Assert.That(highest, Is.GreaterThan(Surface + 1.5f), "水面からジャンプできない");
        }

        [Test]
        public void AfterFiveSeconds_YouDrown_SinkAndAreKnockedOut()
        {
            SimWorld world = World();
            Place(world, new Vector3(5f, 2f, 0f), Vector3.Zero);
            int drownedAt = -1;
            for (int t = 0; t < 6 * 60 && drownedAt < 0; t++)
            {
                Step(world, 1);
                if (world.GetState(0).Combat.IsDrowning)
                {
                    drownedAt = t;
                }
            }
            Assert.That(drownedAt, Is.InRange(5 * 60 - 10, 5 * 60 + 40), "5 秒で溺れない");
            CharacterState drowning = world.GetState(0);
            Assert.That(drowning.Combat.IsUntouchable, Is.True, "溺れている人に攻撃が当たる");
            float y = drowning.Position.Y;
            Step(world, 20, PlayerInput.FromAxes(0f, 1f, InputButtons.Jump));
            Assert.That(world.GetState(0).Position.Y, Is.LessThan(y - 0.5f), "溺れても沈まない（ジャンプで上がれる）");

            int falls = 0;
            for (int t = 0; t < StageZones.DrownKnockoutTicks + 10 && falls == 0; t++)
            {
                Step(world, 1);
                falls = world.GetState(0).Combat.Falls;
            }
            Assert.That(falls, Is.EqualTo(1), "沈んでも撃破されない");
            Assert.That(world.GetState(0).Combat.IsDrowning, Is.False, "撃破の後も溺れたまま");
        }

        [TestCase(PlungeSpeed + 2f, true)]
        [TestCase(PlungeSpeed - 4f, false)]
        [TestCase(20f, false)]
        public void HittingTheWaterTooFast_SinksToTheKnockoutLine(float speed, bool knockedOut)
        {
            SimWorld world = World();
            Place(world, new Vector3(5f, Surface + 0.3f, 0f), new Vector3(0f, -speed, 0f));
            CharacterState state = world.GetState(0);
            // のけぞりなら、落ちる速さの上限で切られない（D-43）。
            state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = 60 };
            world.SetState(0, state);
            int falls = 0;
            float deepest = Surface;
            for (int t = 0; t < 90 && falls == 0; t++)
            {
                Step(world, 1);
                falls = world.GetState(0).Combat.Falls;
                deepest = System.MathF.Min(deepest, world.GetState(0).Position.Y);
            }
            Assert.That(falls == 1, Is.EqualTo(knockedOut), $"{speed} m/s で着水したとき（いちばん深い所 {deepest:F2}）");
        }

        [Test]
        public void TheTimer_KeepsGoingWhenYouHopOut_AndResetsWhenYouLand()
        {
            SimWorld world = World();
            Place(world, new Vector3(5f, 2f, 0f), Vector3.Zero);
            Step(world, 120);
            int left = world.GetState(0).Combat.WaterTicksLeft;
            // 跳んで水から出て、また落ちても、残りは戻らない。
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Jump));
            Step(world, 60);
            Assert.That(world.GetState(0).Combat.WaterTicksLeft, Is.LessThan(left), "跳んで水から出ると残りが戻った");
            Assert.That(world.GetState(0).Combat.WaterTicksLeft, Is.GreaterThan(0));

            // 陸に上がると戻る。
            Place(world, new Vector3(-15f, Movement.HalfHeight + 0.05f, 0f), Vector3.Zero);
            Step(world, 5);
            Assert.That(world.GetState(0).Grounded, Is.True);
            Assert.That(world.GetState(0).Combat.WaterTicksLeft, Is.EqualTo(0), "陸に上がっても残りが戻らない");
        }
    }
}
