using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Simulation
{
    /// <summary>
    /// ステージのギミック（StageZone）をキャラクターに効かせる。CharacterSimulator が移動と接地の後に呼ぶ
    /// （ヒットストップで止まっているティックには呼ばない）。並べた順に効かせる（SimWorld が決まった順に並べる）。
    /// のけぞり中、ガード中、攻撃中、ワイヤーの接続中も同じように効く（吹き飛ばされた相手がジャンプ台で打ち上がる、など）。
    /// </summary>
    public static class StageZones
    {
        /// <summary>ジャンプ台の向きの速さがこの割合（Power に対して）以上なら、もう打ち上がっているので打ち上げ直さない。</summary>
        public const float PadRetriggerRatio = 0.5f;

        /// <param name="startPosition">このティックの初めの位置（加速リングに入ったかを調べる）。</param>
        public static void Apply(ref CharacterState state, Vector3 startPosition, Vector2 halfExtents, IReadOnlyList<StageZone> zones)
        {
            if (zones == null)
            {
                return;
            }
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
            state.Grounded = false;
            state.CoyoteTicksLeft = 0;
            state.JumpBufferTicksLeft = 0;
            state.AirJumpsUsed = 0;
            state.JumpPadCount++;
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
            if (along <= 0f)
            {
                return;
            }
            if (along < zone.Power)
            {
                SetAlong(ref state.Velocity, zone.Direction, along, zone.Power);
            }
            if (state.Velocity.Y > 0f)
            {
                state.Grounded = false;
            }
            state.BoostRingCount++;
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
