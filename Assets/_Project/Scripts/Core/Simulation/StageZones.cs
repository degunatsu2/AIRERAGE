using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Physics;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// ステージのギミック（StageZone）をキャラクターに効かせる。CharacterSimulator が呼ぶ（ヒットストップで止まっているティックには呼ばない）。
    /// - GroundDrift: 速度を決める前。地面に立っている人が横向きの風で流される速さ（走る速さの目標をずらす）。
    /// - ApplyFriction: 移動の前（速度を決めた直後）。氷の滑り。
    /// - Apply: 移動と接地の後。ほかのすべての種類。
    /// 並べた順に効かせる（SimWorld が決まった順に並べる）。のけぞり中、ガード中、攻撃中、ワイヤーの接続中も同じように効く
    /// （吹き飛ばされた相手がジャンプ台で打ち上がる、など）。ただし、氷は何もしていないときだけ、低重力はワイヤーの接続中は効かない。
    /// ギミックは状態を持たない。周期・動き・ランダムな位置は、試合のティック数 tick と試合の種 seed で決まる（巻き戻しても同じ結果になる）。
    /// </summary>
    public static class StageZones
    {
        /// <summary>ジャンプ台・バンパーの向きの速さがこの割合（Power に対して）以上なら、もう飛んでいるので打ち出し直さない。</summary>
        public const float PadRetriggerRatio = 0.5f;

        /// <summary>
        /// 発射台の向きの速さがこの割合以上なら、打ち出し直さない（ジャンプ台より高い。スピードレーンで速く走ってきた人も打ち出す。
        /// 打ち出した直後は速さが Power なので、通り抜けるまで打ち出し直さない）。
        /// </summary>
        public const float LauncherRetriggerRatio = 0.9f;

        /// <summary>ワープゲートで移った直後に、ほかのゲートで移らないティック数【仮】（出口のすぐそばのゲートで行き来し続けないように）。</summary>
        public const int WarpGateCooldownTicks = 30;

        /// <summary>地面に立っている人が横向きの風で流される速さ（風の速さの上限に対する割合）【仮】。</summary>
        public const float GroundWindRatio = 0.6f;

        /// <summary>危険（溶岩流、つらら）に当たったときの、のけぞり・ヒットストップ（ティック）【仮】。</summary>
        public const int HazardHitstunTicks = 40;
        public const int HazardHitstopTicks = 8;

        /// <summary>吹雪で風上に地形があるかを調べる距離（m。ギミックの Limit が 0 のとき）【仮】。</summary>
        public const float BlizzardShelterDistance = 60f;

        /// <summary>吹雪で凍っている長さ（ティック）【仮】。威力（Power）は落とすコインの枚数（2026-10-21）。</summary>
        public const int BlizzardFreezeTicks = 120;

        /// <summary>危険の吹き飛ばす速さ（m/s）を決めていない（Limit が 0）ときの、威力からの速さ: 8 + 威力 × 0.25【仮】。</summary>
        public static float HazardKnockbackSpeed(float power)
        {
            return 8f + power * 0.25f;
        }

        /// <summary>
        /// 地面に立っている人が、重なっている横向きの風で流される速さ（m/s。右が正）。走る速さの目標に足す（Locomotion）。
        /// 地面にいないときは 0（空中では Apply の加速で流される）。
        /// </summary>
        public static float GroundDrift(in CharacterState state, Vector2 halfExtents, IReadOnlyList<StageZone> zones, int tick, int seed)
        {
            if (zones == null || !state.Grounded)
            {
                return 0f;
            }
            float drift = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Kind != StageZoneKind.Gust)
                {
                    continue;
                }
                StageZone zone = zones[i].At(tick);
                if (zone.IsActive(tick, seed) && zone.Overlaps(state.Position, halfExtents))
                {
                    drift += zone.Direction.X * zone.Limit * GroundWindRatio;
                }
            }
            return drift;
        }

        /// <summary>
        /// 氷: 地面に立って重なっている間、このティックの横の速さの変化（velocityBefore からの差）を Power 倍にする。何もしていないとき
        /// （Action.IsFree）だけ（攻撃の動きは変えない）。
        /// </summary>
        public static void ApplyFriction(ref CharacterState state, Vector3 velocityBefore, Vector2 halfExtents, IReadOnlyList<StageZone> zones,
            int tick = 0)
        {
            if (zones == null || !state.Grounded || !state.Action.IsFree)
            {
                return;
            }
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Kind != StageZoneKind.Ice)
                {
                    continue;
                }
                StageZone zone = zones[i].At(tick);
                if (zone.Overlaps(state.Position, halfExtents))
                {
                    state.Velocity.X = velocityBefore.X + (state.Velocity.X - velocityBefore.X) * zone.Power;
                    return;
                }
            }
        }

        /// <param name="startPosition">このティックの初めの位置（加速リング・ワープゲートに入ったかを調べる）。</param>
        /// <param name="tick">試合のティック数（周期と動き）。</param>
        /// <param name="seed">試合の種（ランダムな周期と位置）。</param>
        /// <param name="collision">地形（吹雪の風上に地形があるか。null なら吹雪で必ず凍る）。</param>
        public static void Apply(ref CharacterState state, Vector3 startPosition, CharacterConfig config, IReadOnlyList<StageZone> zones, int tick,
            int seed = 0, ICollisionQuery collision = null)
        {
            if (zones == null)
            {
                return;
            }
            var halfExtents = new Vector2(config.Movement.HalfExtents.X, config.Movement.HalfExtents.Y);
            for (int i = 0; i < zones.Count; i++)
            {
                StageZone zone = zones[i].At(tick);
                switch (zone.Kind)
                {
                    case StageZoneKind.JumpPad:
                        if (zone.Overlaps(state.Position, halfExtents))
                        {
                            Launch(ref state, zone);
                        }
                        break;
                    case StageZoneKind.Updraft:
                        if (zone.Overlaps(state.Position, halfExtents))
                        {
                            Lift(ref state, zone);
                        }
                        break;
                    case StageZoneKind.BoostRing:
                        if (!Before(zones[i], zone, tick).Overlaps(startPosition, halfExtents) && zone.Swept(startPosition, state.Position, halfExtents))
                        {
                            Boost(ref state, zone);
                        }
                        break;
                    case StageZoneKind.SpeedLane:
                        if (state.Grounded && zone.Overlaps(state.Position, halfExtents))
                        {
                            Carry(ref state, zone);
                        }
                        break;
                    case StageZoneKind.Launcher:
                        if (zone.IsActive(tick, seed) && zone.Overlaps(state.Position, halfExtents))
                        {
                            Fire(ref state, zone);
                        }
                        break;
                    case StageZoneKind.Bumper:
                        if (zone.Overlaps(state.Position, halfExtents))
                        {
                            Bump(ref state, zone, halfExtents);
                        }
                        break;
                    case StageZoneKind.Gust:
                        if (zone.IsActive(tick, seed) && zone.Overlaps(state.Position, halfExtents))
                        {
                            Blow(ref state, zone);
                        }
                        break;
                    case StageZoneKind.Ice:
                        // 移動の前に効かせる（ApplyFriction）。
                        break;
                    case StageZoneKind.LowGravity:
                        if (!state.Grounded && state.Wire.Phase != WirePhase.Attached && zone.Overlaps(state.Position, halfExtents))
                        {
                            Float(ref state, zone, config.Movement.Gravity);
                        }
                        break;
                    case StageZoneKind.Warp:
                        if (state.Combat.WarpCooldownTicks <= 0 && !Before(zones[i], zone, tick).Overlaps(startPosition, halfExtents)
                            && zone.Swept(startPosition, state.Position, halfExtents))
                        {
                            Warp(ref state, zone, config.Wire);
                            // 移った後は、ティックの初めの位置からの通り道がもう意味を持たないので、ほかのギミックを調べない。
                            return;
                        }
                        break;
                    case StageZoneKind.Hazard:
                        if (CanBeHitByStage(state) && state.Combat.HazardImmunityTicks <= 0)
                        {
                            TryHazard(ref state, zone, halfExtents, tick, seed, config.Wire);
                        }
                        break;
                    case StageZoneKind.Blizzard:
                        if (CanBeHitByStage(state) && !state.Combat.IsFrozen && zone.IsActive(tick, seed)
                            && zone.Overlaps(state.Position, halfExtents)
                            && !Sheltered(state.Position, zone.Direction, collision, zone.Limit > 0f ? zone.Limit : BlizzardShelterDistance))
                        {
                            Freeze(ref state, zone, config.Wire);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"知らないギミックの種類です: {zone.Kind}");
                }
            }
        }

        // 外から入ったかを調べる範囲: 動くギミックは 1 つ前のティックの範囲（ゲートのほうが動いてきて重なったときも、入ったとみなす）。
        private static StageZone Before(in StageZone source, in StageZone now, int tick)
        {
            return source.Moves ? source.At(tick - 1) : now;
        }

        /// <summary>
        /// 危険の柱が、今のティックに効いているか（効いていれば、その柱の横の範囲）。周期の始まりから ActiveTicks の間だけ効く。
        /// </summary>
        public static bool HazardColumnAt(in StageZone zone, int tick, int seed, out float minX, out float maxX)
        {
            zone.Cycle(tick, seed, out int cycle, out int start, out _);
            if (cycle < 0 || tick - start >= zone.ActiveTicks)
            {
                minX = 0f;
                maxX = 0f;
                return false;
            }
            zone.HazardColumn(cycle, seed, out minX, out maxX);
            return true;
        }

        /// <summary>吹雪: 位置 position から風上（direction の逆）へ distance m の中に、地形があるか（あれば影に隠れている）。</summary>
        public static bool Sheltered(Vector3 position, Vector2 direction, ICollisionQuery collision, float distance = BlizzardShelterDistance)
        {
            if (collision == null)
            {
                return false;
            }
            var upwind = new Vector3(-direction.X, -direction.Y, 0f);
            return collision.Raycast(position, upwind, distance, out _);
        }

        // ステージの危険に当たる状態か（撃破されて待っている間・復帰の無敵の間は当たらない）。
        private static bool CanBeHitByStage(in CharacterState state)
        {
            return !state.Combat.IsWaitingToRespawn && state.Combat.InvincibleTicksLeft <= 0;
        }

        private static void TryHazard(ref CharacterState state, StageZone zone, Vector2 halfExtents, int tick, int seed, WireTuning wire)
        {
            if (!HazardColumnAt(zone, tick, seed, out float minX, out float maxX))
            {
                return;
            }
            Vector3 p = state.Position;
            if (p.X + halfExtents.X < minX || p.X - halfExtents.X > maxX || p.Y + halfExtents.Y < zone.MinY || p.Y - halfExtents.Y > zone.MaxY)
            {
                return;
            }
            WireController.Interrupt(ref state, wire);
            float speed = zone.Limit > 0f ? zone.Limit : HazardKnockbackSpeed(zone.Power);
            // 柱の真ん中から外へ少し押し出す（同じ所に落ちてこないように）。
            float away = p.X >= (minX + maxX) * 0.5f ? 1f : -1f;
            state.Velocity = new Vector3(zone.Direction.X * speed + away * 3f, zone.Direction.Y * speed, 0f);
            state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = HazardHitstunTicks };
            state.Combat.Holding = false;
            state.Combat.HoldTicks = 0;
            state.Combat.FrozenTicksLeft = 0;
            state.Combat.HitstopTicksLeft = Math.Max(state.Combat.HitstopTicksLeft, HazardHitstopTicks);
            state.Combat.HazardImmunityTicks = zone.ActiveTicks + 20;
            state.Combat.HazardHitCount++;
            // 威力の枚数のコインを落とす（SimWorld が CoinDropRules.DropOnHazards で落とす。ユーザーの要望、2026-10-20・21）。
            state.Combat.HazardDropPower = Math.Max(1, (int)zone.Power);
            state.Grounded = false;
            state.WallSide = 0;
            state.JumpBufferTicksLeft = 0;
        }

        private static void Freeze(ref CharacterState state, StageZone zone, WireTuning wire)
        {
            WireController.Interrupt(ref state, wire);
            if (state.Action.Kind != ActionKind.Hitstun)
            {
                state.Action = default;
            }
            state.Combat.Holding = false;
            state.Combat.HoldTicks = 0;
            state.Combat.FrozenTicksLeft = BlizzardFreezeTicks;
            state.Combat.FreezeCount++;
            // 凍ったら威力の枚数のコインを落とす（ユーザーの要望、2026-10-21）。
            state.Combat.HazardDropPower = Math.Max(1, (int)zone.Power);
            state.WallSide = 0;
            state.Velocity.X = 0f;
            state.JumpBufferTicksLeft = 0;
        }

        private static void Launch(ref CharacterState state, StageZone zone)
        {
            float along = Along(state.Velocity, zone.Direction);
            if (along >= zone.Power * PadRetriggerRatio)
            {
                return;
            }
            SetAlong(ref state.Velocity, zone.Direction, along, zone.Power);
            TakeOff(ref state);
        }

        private static void Lift(ref CharacterState state, StageZone zone)
        {
            if (state.Velocity.Y >= zone.Limit)
            {
                return;
            }
            state.Velocity.Y = MathF.Min(state.Velocity.Y + zone.Power * SimTime.DeltaTime, zone.Limit);
            if (state.Velocity.Y > 0f)
            {
                state.Grounded = false;
                state.WallSide = 0;
            }
        }

        private static void Boost(ref CharacterState state, StageZone zone)
        {
            float along = Along(state.Velocity, zone.Direction);
            if (along == 0f)
            {
                // 向きに対して真横に入った（どちら向きに加速するか決まらない）。
                return;
            }
            // 通っている向き（Direction か、その逆）に加速する（両方向。ユーザーの要望、2026-10-13）。
            Vector2 axis = along > 0f ? zone.Direction : -zone.Direction;
            float speed = MathF.Abs(along);
            if (speed < zone.Power)
            {
                SetAlong(ref state.Velocity, axis, speed, zone.Power);
            }
            if (state.Velocity.Y > 0f)
            {
                state.Grounded = false;
            }
            state.WallSide = 0;
            state.BoostRingCount++;
        }

        // スピードレーン: 向きの速さを Power 以上にする（直交する成分は残す。床の上なので、ふつうは横の速さ）。
        private static void Carry(ref CharacterState state, StageZone zone)
        {
            float along = Along(state.Velocity, zone.Direction);
            if (along < zone.Power)
            {
                SetAlong(ref state.Velocity, zone.Direction, along, zone.Power);
            }
        }

        // 発射台: 速度をまるごと向き × Power にする。
        private static void Fire(ref CharacterState state, StageZone zone)
        {
            if (Along(state.Velocity, zone.Direction) >= zone.Power * LauncherRetriggerRatio)
            {
                return;
            }
            state.Velocity.X = zone.Direction.X * zone.Power;
            state.Velocity.Y = zone.Direction.Y * zone.Power;
            TakeOff(ref state);
        }

        // バンパー: 中心から外向き（範囲の大きさで割った向き。横長の看板なら、上に乗れば上へ、横から当たれば横へ）に弾き返す。
        private static void Bump(ref CharacterState state, StageZone zone, Vector2 halfExtents)
        {
            Vector2 center = zone.Center;
            float dx = (state.Position.X - center.X) / ((zone.MaxX - zone.MinX) * 0.5f + halfExtents.X);
            float dy = (state.Position.Y - center.Y) / ((zone.MaxY - zone.MinY) * 0.5f + halfExtents.Y);
            float length = MathF.Sqrt(dx * dx + dy * dy);
            Vector2 normal = length > 1e-4f ? new Vector2(dx / length, dy / length) : Vector2.UnitY;
            BumpAlong(ref state, normal, zone.Power);
        }

        /// <summary>
        /// 弾き返す（バンパー、わたがしの足場）: 向き normal（長さ 1）の速さを power にする（直交する成分は残す。もうその向きに
        /// power × PadRetriggerRatio 以上で離れていれば何もしない）。空中ジャンプが戻る。弾いたら true。
        /// </summary>
        public static bool BumpAlong(ref CharacterState state, Vector2 normal, float power)
        {
            float along = Along(state.Velocity, normal);
            if (along >= power * PadRetriggerRatio)
            {
                return false;
            }
            SetAlong(ref state.Velocity, normal, along, power);
            TakeOff(ref state);
            return true;
        }

        // 風: 向きに Power（m/s²）で加速する（その向きの速さは Limit まで）。
        private static void Blow(ref CharacterState state, StageZone zone)
        {
            float along = Along(state.Velocity, zone.Direction);
            if (along >= zone.Limit)
            {
                return;
            }
            float add = MathF.Min(zone.Power * SimTime.DeltaTime, zone.Limit - along);
            state.Velocity.X += zone.Direction.X * add;
            state.Velocity.Y += zone.Direction.Y * add;
            if (state.Velocity.Y > 0f)
            {
                state.Grounded = false;
                if (zone.Direction.Y > 0.5f)
                {
                    state.WallSide = 0;
                }
            }
        }

        // 低重力: このティックにかかった重力のうち (1 - Power) を戻し、落ちる速さを Limit までにする（のけぞり中の吹き飛びは切らない）。
        private static void Float(ref CharacterState state, StageZone zone, float gravity)
        {
            state.Velocity.Y += gravity * (1f - zone.Power) * SimTime.DeltaTime;
            if (state.Velocity.Y < -zone.Limit && state.Action.Kind != ActionKind.Hitstun)
            {
                state.Velocity.Y = -zone.Limit;
            }
        }

        // ワープゲート: 出口へ移る（速度はそのまま。つないでいたワイヤーは外れる）。
        private static void Warp(ref CharacterState state, StageZone zone, WireTuning wire)
        {
            state.Position = new Vector3(zone.Target.X, zone.Target.Y, 0f);
            state.Grounded = false;
            state.WallSide = 0;
            state.Combat.WarpCooldownTicks = WarpGateCooldownTicks;
            state.Combat.WarpCount++;
            if (state.Wire.Phase == WirePhase.Attached)
            {
                WireController.Release(ref state, WireReleaseReason.Warped, wire);
            }
        }

        // ジャンプ台・発射台・バンパーで飛んだ: 地面を離れ、空中ジャンプが戻る。
        private static void TakeOff(ref CharacterState state)
        {
            state.Grounded = false;
            state.WallSide = 0;
            state.CoyoteTicksLeft = 0;
            state.JumpBufferTicksLeft = 0;
            state.AirJumpsUsed = 0;
            state.JumpPadCount++;
        }

        private static float Along(Vector3 velocity, Vector2 direction)
        {
            return velocity.X * direction.X + velocity.Y * direction.Y;
        }

        // 向き direction の成分を along から speed に変える（直交する成分は残す）。
        private static void SetAlong(ref Vector3 velocity, Vector2 direction, float along, float speed)
        {
            float add = speed - along;
            velocity.X += direction.X * add;
            velocity.Y += direction.Y * add;
        }
    }
}
