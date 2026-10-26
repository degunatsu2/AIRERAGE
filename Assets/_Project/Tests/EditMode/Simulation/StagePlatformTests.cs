using System;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Simulation
{
    /// <summary>
    /// 動く足場と扉（StagePlatform。ユーザーの要望、2026-10-26）: 道のりを動く足場には乗れて一緒に運ばれ、ワイヤーをつなぐと接続点が
    /// 一緒に動き、なくなるとワイヤーが外れる。扉はワイヤーをつなぐと上に開き、やめると閉じる（下に人がいれば閉じない）。
    /// 扉の開き具合は状態で、巻き戻しで戻る。
    /// </summary>
    public class StagePlatformTests
    {
        private static readonly MovementTuning Movement = new MovementTuning();

        // 左右に動く足場（中心 (0, 4)、幅 4m・厚さ 0.6m。60 ティックで 6m 右へ、また 60 ティックで戻る）。
        private static StagePlatform Shuttle(int activeTo = 0)
        {
            return StagePlatform.Path(new Vector2(0f, 4f), new Vector2(2f, 0.3f), true,
                new[] { new PlatformKey(0, Vector2.Zero), new PlatformKey(60, new Vector2(6f, 0f)), new PlatformKey(120, Vector2.Zero) }, 120,
                activeTo: activeTo);
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input = default)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int t = 0; t < ticks; t++)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i] = input;
                }
                world.Step(inputs);
            }
        }

        [Test]
        public void Path_MovesBetweenItsKeys_AndIsOnlyThereWhileActive()
        {
            StagePlatform platform = Shuttle(activeTo: 100);
            Assert.That(platform.TryGetCenter(30, 0, out Vector2 half), Is.True);
            Assert.That(half.X, Is.EqualTo(3f).Within(1e-4f), "道のりの途中の位置");
            Assert.That(platform.TryGetCenter(60, 0, out Vector2 far), Is.True);
            Assert.That(far.X, Is.EqualTo(6f).Within(1e-4f));
            Assert.That(platform.TryGetCenter(110, 0, out _), Is.False, "ある間の外なのにある");
            Assert.That(platform.TryGetCenter(150, 0, out Vector2 again), Is.True, "周期で繰り返さない");
            Assert.That(again.X, Is.EqualTo(3f).Within(1e-4f));
            Assert.Throws<ArgumentException>(() => StagePlatform.Path(Vector2.Zero, Vector2.One, false,
                new[] { new PlatformKey(5, Vector2.Zero) }, 60), "最初の点が 0 でないのに作れた");
        }

        [Test]
        public void StandingOnAMovingPlatform_CarriesYou()
        {
            var world = new SimWorld(new BoxTerrain(), null, null, null, null, null, new[] { Shuttle() });
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 4.3f + Movement.HalfHeight, 0f), 1);
            Step(world, 5);
            Assert.That(world.GetState(0).Grounded, Is.True, "足場に乗れない");
            Step(world, 25);
            CharacterState state = world.GetState(0);
            Assert.That(state.Grounded, Is.True, "運ばれている間に落ちた");
            Assert.That(state.Position.X, Is.EqualTo(3f).Within(0.3f), "足場と一緒に運ばれない");
        }

        [Test]
        public void AWireOnAMovingPlatform_MovesWithIt_AndIsReleasedWhenItIsGone()
        {
            var world = new SimWorld(new BoxTerrain(), null, null, null, null, null, new[] { Shuttle(activeTo: 50) });
            // 足場の真下 6m から、真上へワイヤーを撃つ。
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, -2f, 0f), 1);
            for (int t = 0; t < 20 && world.GetState(0).Wire.Phase != WirePhase.Attached; t++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Vector2.UnitY));
            }
            CharacterState attached = world.GetState(0);
            Assert.That(attached.Wire.Phase, Is.EqualTo(WirePhase.Attached), "動く足場につながらない");
            Assert.That(attached.Wire.AnchorBody, Is.EqualTo(1), "つないだ足場を覚えない");
            float anchorX = attached.Wire.Anchor.X;
            Step(world, 10, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Vector2.UnitY));
            Assert.That(world.GetState(0).Wire.Anchor.X, Is.EqualTo(anchorX + 1f).Within(0.05f), "接続点が足場と一緒に動かない");
            Step(world, 40, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(Vector2.UnitY));
            Assert.That(world.GetState(0).Wire.Phase, Is.Not.EqualTo(WirePhase.Attached), "足場がなくなってもワイヤーが外れない");
            Assert.That(world.GetState(0).Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.AnchorGone));
        }

        // 上下に動くアンカー付きのボラード（埠頭。中心 (x, 1.45) から、wait ティック待って move ティックで rise m 上へ、そのまま止まる）。
        private static StagePlatform Bollard(float x, int wait, int move, float rise)
        {
            return StagePlatform.Path(new Vector2(x, 1.45f), new Vector2(0.45f, 0.45f), true,
                new[] { new PlatformKey(0, Vector2.Zero), new PlatformKey(wait, Vector2.Zero), new PlatformKey(wait + move, new Vector2(0f, rise)) },
                wait + move + 600).WithAnchor();
        }

        [Test]
        public void AnAnchorOnAMovingPlatform_IsAimedAt_AndCarriesTheWireUp()
        {
            var world = new SimWorld(new BoxTerrain().AddBox(-30f, -2f, 30f, 0f), null, null, null, null, null, new[] { Bollard(4f, 60, 60, 10f) });
            world.AddCharacter(new CharacterConfig(), new Vector3(-3f, Movement.HalfHeight, 0f), 1);
            Step(world, 1);
            var collision = (Airerage.Core.Objects.WorldCollision)world.Collision;
            int last = collision.AnchorPoints.Count - 1;
            Assert.That(collision.AnchorPoints[last].X, Is.EqualTo(4f), "動く足場の中心がアンカーにならない");
            Assert.That(collision.AnchorPoints[last].Y, Is.EqualTo(1.45f).Within(1e-4f));
            Assert.That(collision.AnchorBodyOf(last), Is.EqualTo(1), "アンカーの足場を覚えない");
            // 少しずれた照準でも、エイムアシストでボラードへ向かい、ボラードの箱につながる。
            var aim = new Vector2(1f, 0.15f);
            for (int t = 0; t < 30 && world.GetState(0).Wire.Phase != WirePhase.Attached; t++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(aim));
            }
            CharacterState attached = world.GetState(0);
            Assert.That(attached.Wire.Phase, Is.EqualTo(WirePhase.Attached), "動くボラードのアンカーにつながらない");
            Assert.That(attached.Wire.AnchorBody, Is.EqualTo(1), "ボラードにつながっていない（空中につながった）");
            float startY = attached.Wire.Anchor.Y;
            // ボラードが上がると、接続点も一緒に上がる。
            Step(world, 125 - world.Tick, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(aim));
            CharacterState carried = world.GetState(0);
            Assert.That(carried.Wire.Phase, Is.EqualTo(WirePhase.Attached), "運ばれる間にワイヤーが外れた");
            Assert.That(carried.Wire.Anchor.Y, Is.EqualTo(startY + 10f).Within(0.05f), "接続点がボラードと一緒に上がらない");
            Assert.That(carried.Position.Y, Is.GreaterThan(Movement.HalfHeight + 3f), "ワイヤーで引き上げられない");
        }

        [Test]
        public void AHookAimedAtAMovingAnchor_MissesIfTheAnchorHasMovedAway()
        {
            // 撃ってすぐボラードが上へ速く動く。フックは撃ったときの位置へ飛ぶが、もうないので、何もない所にはつながらない。
            var world = new SimWorld(new BoxTerrain().AddBox(-30f, -2f, 30f, 0f), null, null, null, null, null, new[] { Bollard(6f, 2, 8, 12f) });
            world.AddCharacter(new CharacterConfig(), new Vector3(-4f, Movement.HalfHeight, 0f), 1);
            var aim = new Vector2(1f, 0.05f);
            bool attached = false;
            for (int t = 0; t < 40; t++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(aim));
                attached |= world.GetState(0).Wire.Phase == WirePhase.Attached;
            }
            Assert.That(attached, Is.False, "動いて離れたアンカーの元の位置（空中）につながった");
            Assert.That(world.GetState(0).Wire.LastMissReason, Is.EqualTo(WireMissReason.OutOfRange));
        }

        // 床（上面 y = 0）と天井（y = 4〜6。扉の通る穴 x = -0.5〜0.5 を開ける）。扉は穴の下（y = 0〜4）を閉じ、3.5m 上へ開く。
        private static SimWorld DoorWorld(out StagePlatform door)
        {
            BoxTerrain terrain = new BoxTerrain().AddBox(-30f, -2f, 30f, 0f).AddBox(-30f, 4f, -0.5f, 6f).AddBox(0.5f, 4f, 30f, 6f);
            door = StagePlatform.Door(new Vector2(0f, 2f), new Vector2(0.5f, 2f), true, 3.5f, 30, 60);
            var world = new SimWorld(terrain, null, null, null, null, null, new[] { door });
            return world;
        }

        [Test]
        public void ADoor_OpensWhileAWireIsOnIt_AndClosesAfter()
        {
            SimWorld world = DoorWorld(out StagePlatform door);
            world.AddCharacter(new CharacterConfig(), new Vector3(-5f, Movement.HalfHeight, 0f), 1);
            var toDoor = Vector2.Normalize(new Vector2(5f, 1.5f));
            int attachedAt = -1;
            for (int t = 0; t < 40 && attachedAt < 0; t++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(toDoor));
                if (world.GetState(0).Wire.Phase == WirePhase.Attached)
                {
                    attachedAt = t;
                }
            }
            Assert.That(attachedAt, Is.GreaterThanOrEqualTo(0), "扉にワイヤーがつながらない");
            Assert.That(world.GetState(0).Wire.AnchorBody, Is.EqualTo(1));
            Step(world, 40, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(toDoor));
            Assert.That(world.Stage.OpenOf(0), Is.EqualTo(StagePlatform.DoorScale), "つないでいるのに開ききらない");
            Assert.That(world.Platforms[0].TryGetCenter(world.Tick, world.Stage.OpenOf(0), out Vector2 open), Is.True);
            Assert.That(open.Y, Is.EqualTo(5.5f).Within(1e-3f), "扉が上に開かない");

            // 開いた扉の下をくぐれる（ワイヤーを外して右へ歩く）。
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(-1.5f, Movement.HalfHeight, 0f);
            world.SetState(0, state);
            Step(world, 1);
            Step(world, 25, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            Assert.That(world.GetState(0).Position.X, Is.GreaterThan(0.6f), "閉じきる前に、開いた扉の下を通れない");
            Step(world, 80);
            Assert.That(world.Stage.OpenOf(0), Is.Zero, "つなぐのをやめても閉じない");
        }

        [Test]
        public void ADoor_DoesNotCloseOntoSomeone_AndRollsBackWithTheWorld()
        {
            SimWorld world = DoorWorld(out _);
            world.AddCharacter(new CharacterConfig(), new Vector3(-5f, Movement.HalfHeight, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(-3f, Movement.HalfHeight, 0f), 1);
            // 扉を開けてから、もう 1 人を扉の下に立たせる。
            var toDoor = Vector2.Normalize(new Vector2(5f, 1.5f));
            for (int t = 0; t < 70; t++)
            {
                world.Step(new[] { PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(toDoor), default(PlayerInput) });
            }
            Assert.That(world.Stage.OpenOf(0), Is.GreaterThan(StagePlatform.DoorScale / 2), "前提: 扉が開いている");
            CharacterState under = world.GetState(1);
            under.Position = new Vector3(0f, Movement.HalfHeight, 0f);
            world.SetState(1, under);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            int openBefore = world.Stage.OpenOf(0);
            for (int t = 0; t < 120; t++)
            {
                world.Step(new[] { default(PlayerInput), default(PlayerInput) });
            }
            CharacterState below = world.GetState(1);
            Assert.That(below.Position.X, Is.EqualTo(0f).Within(0.3f), "扉の下の人が押し出された");
            Assert.That(world.Stage.OpenOf(0), Is.GreaterThan(0), "人の上に扉が閉じた");

            world.LoadFrom(snapshot);
            Assert.That(world.Stage.OpenOf(0), Is.EqualTo(openBefore), "巻き戻しで扉の開き具合が戻らない");
            Assert.That(world.Checksum(), Is.EqualTo(snapshot.Checksum()));
        }
        // ボタンで開く扉（ユーザーの要望、2026-10-26）: DoorWorld と同じ天井の穴と扉（ワイヤーがつながらない）、天井の下の左に組 1 のボタン。
        private static SimWorld ButtonDoorWorld()
        {
            BoxTerrain terrain = new BoxTerrain().AddBox(-30f, -2f, 30f, 0f).AddBox(-30f, 4f, -0.5f, 6f).AddBox(0.5f, 4f, 30f, 6f);
            StagePlatform door = StagePlatform.Door(new Vector2(0f, 2f), new Vector2(0.5f, 2f), false, 3.5f, 30, 180, 1);
            StagePlatform button = StagePlatform.Button(new Vector2(-2f, 3.7f), new Vector2(0.3f, 0.3f), 1);
            return new SimWorld(terrain, null, null, null, null, null, new[] { door, button });
        }

        [Test]
        public void AButtonDoor_OpensOnlyWhileAWireIsOnItsButton_AndTheDoorItselfTakesNoWire()
        {
            SimWorld world = ButtonDoorWorld();
            int doorIndex = world.Platforms[0].IsDoor ? 0 : 1;
            world.AddCharacter(new CharacterConfig(), new Vector3(-5f, Movement.HalfHeight, 0f), 1);
            // 扉そのものを狙っても、つながらず開かない。
            var toDoor = Vector2.Normalize(new Vector2(5f, 1.5f));
            Step(world, 30, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(toDoor));
            Assert.That(world.GetState(0).Wire.Phase, Is.Not.EqualTo(WirePhase.Attached), "ワイヤーがつながらない扉につながった");
            Assert.That(world.Stage.OpenOf(doorIndex), Is.Zero, "扉そのものを狙って開いた");
            Step(world, 40);

            // ボタンにつなぐと開く。
            Vector3 p = world.GetState(0).Position;
            var toButton = Vector2.Normalize(new Vector2(-2f - p.X, 3.7f - p.Y));
            for (int t = 0; t < 40 && world.GetState(0).Wire.Phase != WirePhase.Attached; t++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(toButton));
            }
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Attached), "ボタンにつながらない");
            Assert.That(world.Platforms[world.GetState(0).Wire.AnchorBody - 1].IsButton, Is.True);
            Step(world, 40, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(toButton));
            Assert.That(world.Stage.OpenOf(doorIndex), Is.EqualTo(StagePlatform.DoorScale), "ボタンにつないでいるのに開ききらない");

            // 離すと、閉じる速さ（180 ティック）でゆっくり閉じる。
            Step(world, 90);
            int half = world.Stage.OpenOf(doorIndex);
            Assert.That(half, Is.GreaterThan(StagePlatform.DoorScale / 4).And.LessThan(StagePlatform.DoorScale * 3 / 4), "閉じる速さが違う");
            Step(world, 100);
            Assert.That(world.Stage.OpenOf(doorIndex), Is.Zero, "離しても閉じない");
        }

        [Test]
        public void ATimedDoor_OpensAtTheStartOfEachPeriod()
        {
            BoxTerrain terrain = new BoxTerrain().AddBox(-30f, -2f, 30f, 0f);
            // 600 ティックごとに、初めの 180 ティックだけ開く（ずれ 0 なので、試合の始まりから開き始める）。
            StagePlatform bars = StagePlatform.TimedDoor(new Vector2(0f, 2f), new Vector2(0.3f, 2f), false, 4f, 20, 40, 600, 180);
            var world = new SimWorld(terrain, null, null, null, null, null, new[] { bars });
            Assert.That(bars.ScheduledOpen(0), Is.True);
            Assert.That(bars.ScheduledOpen(179), Is.True);
            Assert.That(bars.ScheduledOpen(180), Is.False);
            Assert.That(bars.ScheduledOpen(600), Is.True);
            Step(world, 60);
            Assert.That(world.Stage.OpenOf(0), Is.EqualTo(StagePlatform.DoorScale), "周期の初めに開かない");
            Step(world, 200);
            Assert.That(world.Stage.OpenOf(0), Is.Zero, "開いている間が過ぎても閉じない");
            Step(world, 400);
            Assert.That(world.Stage.OpenOf(0), Is.EqualTo(StagePlatform.DoorScale), "次の周期に開かない");
            Assert.Throws<ArgumentException>(() => StagePlatform.TimedDoor(Vector2.Zero, Vector2.One, false, 1f, 10, 10, 100, 100));
            Assert.Throws<ArgumentException>(() => StagePlatform.Button(Vector2.Zero, Vector2.One, 0));
        }
    }
}
