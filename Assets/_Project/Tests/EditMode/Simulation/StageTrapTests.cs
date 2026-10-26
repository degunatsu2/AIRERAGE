using System;
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
    /// 恐怖の館の悪魔の手と、研究所・競技場の仕掛け（ユーザーの要望、2026-10-26）:
    /// - 悪魔の手（StageZoneKind.Grab）: 触れた人を握って動けなくし（手と一緒に動き、何にも触れない）、時間が来たら牢屋へ移す。
    /// - 赤外線とミサイル（StageTrapKind.Tripwire）: レーダーに触れると、予告の後にミサイルがレーダーに沿って飛び、当たった人は
    ///   吹き飛んで威力の枚数のコインを落とす。触れた時刻は状態で、巻き戻しで戻る。
    /// - ピッチャーマシン（StageTrapKind.Pitcher）: ボールは跳ねて転がり、止まったら点滅して消える。速いボールに当たると吹き飛ぶ。
    ///   天井と壁ではよく跳ね返り、触れた人はバンパーのように弾き返される（遅いボールでもコインは落とさない。2026-10-26）。
    /// - 赤外線の待ち: 撃ってから、また触れられるようになるまで（表示は赤外線を消して、円形のゲージを出す。2026-10-26）。
    /// </summary>
    public class StageTrapTests
    {
        private static readonly MovementTuning Movement = new MovementTuning();

        private static BoxTerrain Floor()
        {
            return new BoxTerrain().AddBox(-60f, -2f, 60f, 0f);
        }

        private static void Step(SimWorld world, int ticks)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int t = 0; t < ticks; t++)
            {
                world.Step(inputs);
            }
        }

        [Test]
        public void TheDemonHand_GrabsYou_CarriesYou_ThenSendsYouToThePrison()
        {
            // 地面の上の手（幅 2m・高さ 2m）が、10 秒で 20m 行って戻る。握ってから 1.2 秒で (0, 20) の牢屋へ。
            var hand = new StageZone(StageZoneKind.Grab, -11f, 0f, -9f, 2f, Vector2.UnitY, 1.2f, 0f, default, new Vector2(0f, 20f),
                travel: new Vector2(20f, 0f), travelPeriod: 600);
            var world = new SimWorld(Floor(), null, new[] { hand });
            world.AddCharacter(new CharacterConfig(), new Vector3(-6f, Movement.HalfHeight, 0f), 1);
            int grabbedAt = -1;
            for (int t = 0; t < 200 && grabbedAt < 0; t++)
            {
                Step(world, 1);
                if (world.GetState(0).Combat.IsCaptured)
                {
                    grabbedAt = world.Tick;
                }
            }
            Assert.That(grabbedAt, Is.GreaterThan(0), "手が来ても握られない");
            CharacterState held = world.GetState(0);
            Assert.That(held.Combat.IsUntouchable, Is.True, "握られている間に触れられる");
            Assert.That(held.Combat.CaptureCount, Is.EqualTo(1));
            float heldX = held.Position.X;
            Step(world, 30);
            Assert.That(world.GetState(0).Position.X, Is.GreaterThan(heldX + 0.5f), "握られたまま手と一緒に動かない");
            Step(world, 60);
            CharacterState jailed = world.GetState(0);
            Assert.That(jailed.Combat.IsCaptured, Is.False, "1.2 秒たっても放されない");
            Assert.That(jailed.Position.Y, Is.GreaterThan(15f), "牢屋へ移らない");
            Assert.That(jailed.Combat.HazardImmunityTicks, Is.GreaterThan(0), "移った後に、また握られる");
        }

        // 床の上の高さ 1m を横切る赤外線（x = -10〜10）と、左の端の発射口から右へ撃つミサイル。
        private static StageTrap Tripwire()
        {
            return StageTrap.Tripwire(new Vector2(-10f, 0.9f), new Vector2(10f, 1.1f), new Vector2(-10f, 1f), Vector2.UnitX, 50f, 20f, 60, 40f, 30,
                120, new Vector2(0.6f, 0.25f));
        }

        [Test]
        public void TouchingTheInfrared_FiresAMissileAlongIt_ThatKnocksYouFar_AndDropsItsPower()
        {
            var world = new SimWorld(Floor(), null, null, null, null, null, null, new[] { Tripwire() });
            world.AddCharacter(new CharacterConfig(), new Vector3(4f, Movement.HalfHeight, 0f), 1);
            CharacterState start = world.GetState(0);
            start.Coins.Held = 100;
            world.SetState(0, start);
            Step(world, 1);
            int tripped = world.Stage.TrapTickOf(0);
            Assert.That(tripped, Is.EqualTo(1), "レーダーに触れても反応しない");
            Assert.That(StageTraps.TryGetMissile(world.Traps[0], world.Stage, 0, world.Tick, out bool arming, out _), Is.True);
            Assert.That(arming, Is.True, "撃つ前の予告がない");

            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            int hitAt = -1;
            for (int t = 0; t < 120 && hitAt < 0; t++)
            {
                Step(world, 1);
                if (world.GetState(0).Combat.HazardHitCount > 0)
                {
                    hitAt = world.Tick;
                }
            }
            Assert.That(hitAt, Is.GreaterThan(tripped + 30), "予告の前に当たった、か当たらない");
            CharacterState hit = world.GetState(0);
            Assert.That(hit.Velocity.X, Is.GreaterThan(30f), "ミサイルの向きに大きく吹き飛ばない");
            Assert.That(hit.Coins.Held, Is.EqualTo(40), "威力 60 の枚数を落とさない");
            Assert.That(world.Stage.TrapSpentOf(0), Is.True, "当たったミサイルが消えない");

            world.LoadFrom(snapshot);
            Assert.That(world.Stage.TrapTickOf(0), Is.EqualTo(tripped), "巻き戻しで触れた時刻が戻らない");
            Assert.That(world.Stage.TrapSpentOf(0), Is.False);
            Assert.That(world.Checksum(), Is.EqualTo(snapshot.Checksum()));

            // 飛び終わって待つ時間が過ぎたら、また撃てる（誰もいなければ待ったまま）。
            CharacterState away = world.GetState(0);
            away.Position = new Vector3(30f, Movement.HalfHeight, 0f);
            world.SetState(0, away);
            Step(world, 30 + world.Traps[0].FlightTicks + 120);
            Assert.That(world.Stage.TrapTickOf(0), Is.Zero, "待つ時間が過ぎても戻らない");
        }

        [Test]
        public void ThePitchedBall_BouncesRolls_StopsAndBlinks_ThenVanishes()
        {
            // 床の左に高さ 1m で、右上へ打ち出す（20 秒ごと）。
            StageTrap pitcher = StageTrap.Pitcher(new Vector2(-20f, 1f), new Vector2(16f, 14f), 1200, 0, 0.35f, 10, 20f, 6f, 90);
            pitcher.Prepare(Floor());
            Assert.That(pitcher.Path.Count, Is.GreaterThan(pitcher.StopTick + 89), "止まった後に点滅しない");
            int bounces = 0;
            float maxY = 0f;
            for (int age = 1; age < pitcher.StopTick; age++)
            {
                Vector2 before = pitcher.Path[age - 1];
                Vector2 now = pitcher.Path[age];
                maxY = MathF.Max(maxY, now.Y);
                if (age >= 2)
                {
                    Vector2 earlier = pitcher.Path[age - 2];
                    if (now.Y - before.Y > 0f && before.Y - earlier.Y < 0f)
                    {
                        bounces++;
                    }
                }
                Assert.That(now.Y, Is.GreaterThanOrEqualTo(0.3f), $"ボールが床に沈んだ（{age} ティック）");
            }
            Assert.That(maxY, Is.GreaterThan(4f), "空中に打ち出されない");
            Assert.That(bounces, Is.GreaterThanOrEqualTo(2), "跳ねない");
            Assert.That(pitcher.TryGetBall(pitcher.StopTick + 10, out _, out _, out _, out bool blinking), Is.True);
            Assert.That(blinking, Is.True, "止まったのに点滅しない");
            Assert.That(pitcher.TryGetBall(pitcher.Path.Count + 5, out _, out _, out _, out _), Is.False, "点滅の後に消えない");
            Assert.That(pitcher.TryGetBall(1200 + 3, out int age2, out _, out _, out _), Is.True, "次の周期に打ち出さない");
            Assert.That(age2, Is.EqualTo(3));
            // ずれのある機械は、試合の始まりに飛んでいるボールがなく、最初は周期 - ずれのティックに打ち出す。
            StageTrap late = StageTrap.Pitcher(new Vector2(-20f, 1f), new Vector2(16f, 14f), 1200, 300, 0.35f, 10, 20f, 6f, 90);
            late.Prepare(Floor());
            Assert.That(late.TryGetBall(0, out _, out _, out _, out _), Is.False, "試合の始まりにボールが飛んでいる");
            Assert.That(late.TryGetBall(899, out _, out _, out _, out _), Is.False);
            Assert.That(late.TryGetBall(900, out int firstAge, out _, out _, out _), Is.True, "周期 - ずれのティックに打ち出さない");
            Assert.That(firstAge, Is.Zero);
        }

        [Test]
        public void AFastBall_KnocksYouAway()
        {
            // 床のすぐ上をまっすぐ右へ転がる速いボール。
            StageTrap pitcher = StageTrap.Pitcher(new Vector2(-20f, 0.4f), new Vector2(24f, 0f), 1200, 0, 0.35f, 10, 20f, 6f, 60);
            var world = new SimWorld(Floor(), null, null, null, null, null, null, new[] { pitcher });
            world.AddCharacter(new CharacterConfig(), new Vector3(-14f, Movement.HalfHeight, 0f), 1);
            for (int t = 0; t < 60 && world.GetState(0).Combat.HazardHitCount == 0; t++)
            {
                Step(world, 1);
            }
            CharacterState hit = world.GetState(0);
            Assert.That(hit.Combat.HazardHitCount, Is.EqualTo(1), "速いボールに当たらない");
            Assert.That(hit.Velocity.X, Is.GreaterThan(5f), "ボールの向きに吹き飛ばない");
            Assert.That(hit.Action.Kind, Is.EqualTo(ActionKind.Hitstun));
        }

        [Test]
        public void AfterTheMissile_TheInfraredReloads_UntilItCanBeTouchedAgain()
        {
            var world = new SimWorld(Floor(), null, null, null, null, null, null, new[] { Tripwire() });
            world.AddCharacter(new CharacterConfig(), new Vector3(4f, Movement.HalfHeight, 0f), 1);
            Step(world, 1);
            StageTrap trap = world.Traps[0];
            Assert.That(StageTraps.TryGetReload(trap, world.Stage, 0, world.Tick, out _, out _), Is.False, "予告の間に待ちになった");
            Step(world, trap.ArmTicks);
            Assert.That(StageTraps.TryGetReload(trap, world.Stage, 0, world.Tick, out int first, out int total), Is.True, "撃っても待ちにならない");
            Assert.That(total, Is.EqualTo(trap.FlightTicks + trap.CooldownTicks));
            Assert.That(first, Is.EqualTo(total), "撃った瞬間に待ちの全体が残っていない");
            // 当たった人を離しておき、待ちが減っていって、終わると待ちでなくなる（また触れられる）。
            CharacterState away = world.GetState(0);
            away.Position = new Vector3(30f, Movement.HalfHeight, 0f);
            away.Velocity = Vector3.Zero;
            world.SetState(0, away);
            Step(world, 30);
            Assert.That(StageTraps.TryGetReload(trap, world.Stage, 0, world.Tick, out int later, out _), Is.True);
            Assert.That(later, Is.EqualTo(first - 30), "待ちが減らない");
            Step(world, later);
            Assert.That(StageTraps.TryGetReload(trap, world.Stage, 0, world.Tick, out _, out _), Is.False, "待ちが終わらない");
            Assert.That(world.Stage.TrapTickOf(0), Is.Zero, "待ちが終わっても触れられるようにならない");
        }

        [Test]
        public void TheBall_BouncesOffWallsAndCeilings_MuchHarderThanTheFloor()
        {
            // 床と、右の壁（x = 6）。右へまっすぐ投げたボールは壁でほとんど速さを失わずに跳ね返る。
            BoxTerrain room = new BoxTerrain().AddBox(-60f, -2f, 60f, 0f).AddBox(6f, 0f, 8f, 20f).AddBox(-60f, 12f, 60f, 14f);
            StageTrap wall = StageTrap.Pitcher(new Vector2(-4f, 6f), new Vector2(20f, 0f), 1200, 0, 0.8f, 10, 20f, 6f, 60);
            wall.Prepare(room);
            // 跳ね返ったティックの前後の、まるごと動いたティックの横の速さ。
            float before = 0f;
            float after = 0f;
            for (int age = 3; age + 2 < wall.Path.Count; age++)
            {
                if (wall.Path[age].X - wall.Path[age - 1].X < 0f)
                {
                    before = (wall.Path[age - 2].X - wall.Path[age - 3].X) * 60f;
                    after = (wall.Path[age + 1].X - wall.Path[age + 2].X) * 60f;
                    break;
                }
            }
            Assert.That(before, Is.GreaterThan(15f), "壁に当たらない");
            Assert.That(after, Is.GreaterThan(before * StageTrap.BallWallRestitution * 0.9f), "壁で大きく跳ね返らない");
            Assert.That(StageTrap.BallWallRestitution, Is.GreaterThan(StageTrap.BallRestitution), "壁が床より跳ね返らない");
            // 天井（y = 12）へまっすぐ上へ投げると、下向きに大きく跳ね返る。
            StageTrap ceiling = StageTrap.Pitcher(new Vector2(0f, 2f), new Vector2(0f, 30f), 1200, 0, 0.8f, 10, 20f, 6f, 60);
            ceiling.Prepare(room);
            int top = 1;
            while (top < ceiling.Path.Count && ceiling.Path[top].Y > ceiling.Path[top - 1].Y)
            {
                top++;
            }
            float down = (ceiling.Path[top - 1].Y - ceiling.Path[top].Y) * 60f;
            Assert.That(ceiling.Path[top - 1].Y, Is.GreaterThan(11f - 0.8f - 0.1f), "天井まで届かない");
            Assert.That(down, Is.GreaterThan(15f), "天井で大きく跳ね返らない");
        }

        [Test]
        public void TouchingTheBall_BouncesYouAwayLikeABumper_OnlyAFastBallKnocksCoinsOut()
        {
            // 床の上をゆっくり右へ転がるボール（当たる速さ 6 m/s より遅い）が、右に立っている人に触れる。
            StageTrap slow = StageTrap.Pitcher(new Vector2(-20f, 0.81f), new Vector2(5.5f, 0f), 1200, 0, 0.8f, 10, 20f, 6f, 60);
            var world = new SimWorld(Floor(), null, null, null, null, null, null, new[] { slow });
            world.AddCharacter(new CharacterConfig(), new Vector3(-17.5f, Movement.HalfHeight, 0f), 1);
            CharacterState start = world.GetState(0);
            start.Coins.Held = 50;
            world.SetState(0, start);
            float pushed = 0f;
            for (int t = 0; t < 180 && pushed <= 0f; t++)
            {
                Step(world, 1);
                if (world.GetState(0).Velocity.X > 10f)
                {
                    pushed = world.GetState(0).Velocity.X;
                }
            }
            CharacterState bumped = world.GetState(0);
            Assert.That(pushed, Is.GreaterThan(10f), "遅いボールに触れても弾き返されない");
            Assert.That(bumped.Combat.HazardHitCount, Is.Zero, "遅いボールで吹き飛んだ");
            Assert.That(bumped.Coins.Held, Is.EqualTo(50), "遅いボールでコインを落とした");
        }

        [Test]
        public void InvalidTraps_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => StageTrap.Tripwire(new Vector2(1f, 0f), new Vector2(0f, 1f), Vector2.Zero, Vector2.UnitX, 10f, 5f,
                10, 10f, 0, 0, Vector2.One));
            Assert.Throws<ArgumentException>(() => StageTrap.Pitcher(Vector2.Zero, Vector2.One, 0, 0, 0.3f, 1, 1f, 0f, 0));
            Assert.Throws<ArgumentException>(() => new StageZone(StageZoneKind.Grab, -1f, -1f, 1f, 1f, Vector2.UnitY, 1f, 0f, default, Vector2.Zero));
        }
    }
}
