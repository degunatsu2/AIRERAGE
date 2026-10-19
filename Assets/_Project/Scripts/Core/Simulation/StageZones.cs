using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// ステージのギミック（StageZone）をキャラクターに効かせる。CharacterSimulator が呼ぶ（ヒットストップで止まっているティックには呼ばない）。
    /// - ApplyFriction: 移動の前（速度を決めた直後）。氷の滑り。
    /// - Apply: 移動と接地の後。ほかのすべての種類。
    /// 並べた順に効かせる（SimWorld が決まった順に並べる）。のけぞり中、ガード中、攻撃中、ワイヤーの接続中も同じように効く
    /// （吹き飛ばされた相手がジャンプ台で打ち上がる、など）。ただし、氷は何もしていないときだけ、低重力はワイヤーの接続中は効かない。
    /// ギミックは状態を持たない。周期のある風は、試合のティック数 tick で決まる（巻き戻しても同じ結果になる）。
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

        /// <summary>
        /// 氷: 地面に立って重なっている間、このティックの横の速さの変化（velocityBefore からの差）を Power 倍にする。何もしていないとき
        /// （Action.IsFree）だけ（攻撃の動きは変えない）。
        /// </summary>
        public static void ApplyFriction(ref CharacterState state, Vector3 velocityBefore, Vector2 halfExtents, IReadOnlyList<StageZone> zones)
        {
            if (zones == null || !state.Grounded || !state.Action.IsFree)
            {
                return;
            }
            for (int i = 0; i < zones.Count; i++)
            {
                StageZone zone = zones[i];
                if (zone.Kind == StageZoneKind.Ice && zone.Overlaps(state.Position, halfExtents))
                {
                    state.Velocity.X = velocityBefore.X + (state.Velocity.X - velocityBefore.X) * zone.Power;
                    return;
                }
            }
        }

        /// <param name="startPosition">このティックの初めの位置（加速リング・ワープゲートに入ったかを調べる）。</param>
        /// <param name="tick">試合のティック数（周期のある風）。</param>
        public static void Apply(ref CharacterState state, Vector3 startPosition, CharacterConfig config, IReadOnlyList<StageZone> zones, int tick)
        {
            if (zones == null)
            {
                return;
            }
            var halfExtents = new Vector2(config.Movement.HalfExtents.X, config.Movement.HalfExtents.Y);
            for (int i = 0; i < zones.Count; i++)
            {
                StageZone zone = zones[i];
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
                        if (!zone.Overlaps(startPosition, halfExtents) && zone.Swept(startPosition, state.Position, halfExtents))
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
                        if (zone.Overlaps(state.Position, halfExtents))
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
                        if (zone.IsActive(tick) && zone.Overlaps(state.Position, halfExtents))
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
                        if (state.Combat.WarpCooldownTicks <= 0 && !zone.Overlaps(startPosition, halfExtents)
                            && zone.Swept(startPosition, state.Position, halfExtents))
                        {
                            Warp(ref state, zone, config.Wire);
                            // 移った後は、ティックの初めの位置からの通り道がもう意味を持たないので、ほかのギミックを調べない。
                            return;
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"知らないギミックの種類です: {zone.Kind}");
                }
            }
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
            float along = Along(state.Velocity, normal);
            if (along >= zone.Power * PadRetriggerRatio)
            {
                return;
            }
            SetAlong(ref state.Velocity, normal, along, zone.Power);
            TakeOff(ref state);
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
