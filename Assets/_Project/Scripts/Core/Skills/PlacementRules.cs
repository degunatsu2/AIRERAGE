using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// 技で置く物（設置物）の位置を、地形（動く足場と扉を含む）に埋まらないように直す（ユーザーの要望、2026-10-29）。置く位置で地形に
    /// 重なるなら、重ならない場所のうち、もとの位置にいちばん近い所へ移す（GridStep の格子で近い順に探し、見つけた方向で境目まで詰める。
    /// 同じ近さなら、置いた人の側）。SearchRadius の中に見つからなければ、もとの位置のまま。物の形は軸平行の箱で調べる（丸い物は外接する
    /// 正方形、傾いたわたがしの足場は外接する長方形）。
    /// 対象は照準の方向に置く物に限らない: ミラーボール・セーフティボックス（RISK TAKER）、ワープポータル（SPACE TRAVELER）、わたがしの足場
    /// （PASTRY CHEF）、THIEF が置いたギミックと仕掛け。風の帯（STORM RUNNER の SKY ROCKET）は、置いた人から伸びる風の範囲なので直さない
    /// （ステージの風と同じく、地形と重なってよい）。
    /// THIEF が空中に置いたベルトコンベア（SpeedLane）には、見た目どおりの足場を持たせる（ChargeMilli = 1。WorldCollision.FootingBox が
    /// 当たりにする）。
    /// SkillRules.FireEffects が、物を置いたその場で呼ぶ（置いた瞬間の効果より前）。値と地形だけで決まるので、PC が違っても同じ。
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>探す格子の間隔と、探す範囲の半径（m）。</summary>
        public const float GridStep = 0.2f;
        public const float SearchRadius = 8f;

        // 見つけた方向で境目まで詰める回数（1/2^10 まで）。
        private const int RefineSteps = 10;

        // 下に地形があるかを調べる長さ（m）。
        private const float SupportProbe = 0.12f;

        /// <summary>
        /// objects の番号 from 以降（このティックに置いた物）を、地形に埋まらない位置へ直す。owner は置いた人（置く前の状態）。
        /// </summary>
        public static void Settle(WorldObjects objects, int from, ICollisionQuery collision, in CharacterState owner, CharacterConfig config,
            ThiefContext thief)
        {
            if (collision == null)
            {
                return;
            }
            var ownerPosition = new Vector2(owner.Position.X, owner.Position.Y);
            for (int o = Math.Max(0, from); o < objects.Count; o++)
            {
                WorldObject item = objects[o];
                if (item.Ended || !TryGetHalfExtents(item, config, out Vector2 half))
                {
                    continue;
                }
                item.Position = FreePosition(collision, item.Position, half, ownerPosition);
                if (item.Kind == WorldObjectKind.PlacedZone && IsConveyor(item, thief) && !IsSupported(collision, item.Position, half))
                {
                    item.ChargeMilli = 1;
                }
                objects[o] = item;
            }
        }

        /// <summary>置いた物 item の、埋まりを調べる箱の半分の大きさ（直さない物なら false）。</summary>
        public static bool TryGetHalfExtents(in WorldObject item, CharacterConfig config, out Vector2 half)
        {
            switch (item.Kind)
            {
                case WorldObjectKind.MirrorBall:
                    half = new Vector2(config.Skills.MirrorBall.Radius, config.Skills.MirrorBall.Radius);
                    return true;
                case WorldObjectKind.Safe:
                    half = new Vector2(config.Skills.Safe.HalfWidth, config.Skills.Safe.HalfHeight);
                    return true;
                case WorldObjectKind.Portal:
                    half = new Vector2(config.Skills.Portal.Radius, config.Skills.Portal.Radius);
                    return true;
                case WorldObjectKind.CottonCandy:
                {
                    // 向き Velocity（足場の表）に垂直な横長の長方形の、外接する長方形。
                    Vector2 normal = item.Velocity;
                    half = new Vector2(MathF.Abs(normal.Y) * item.End.X + MathF.Abs(normal.X) * item.End.Y,
                        MathF.Abs(normal.X) * item.End.X + MathF.Abs(normal.Y) * item.End.Y);
                    return true;
                }
                case WorldObjectKind.PlacedZone:
                    half = item.End;
                    return true;
                case WorldObjectKind.PlacedTrap:
                    half = new Vector2(config.Skills.Thief.TrapBodyRadius, config.Skills.Thief.TrapBodyRadius);
                    return true;
                default:
                    half = default;
                    return false;
            }
        }

        /// <summary>
        /// 中心 position・半分の大きさ half の箱が地形に重ならない位置のうち、position にいちばん近い所（重ならなければ position のまま。
        /// 見つからなければ position）。同じ近さなら preferToward の側。
        /// </summary>
        public static Vector2 FreePosition(ICollisionQuery collision, Vector2 position, Vector2 half, Vector2 preferToward)
        {
            if (!Overlaps(collision, position, half))
            {
                return position;
            }
            int cells = (int)(SearchRadius / GridStep);
            var offsets = new List<(int X, int Y)>();
            for (int y = -cells; y <= cells; y++)
            {
                for (int x = -cells; x <= cells; x++)
                {
                    if ((x != 0 || y != 0) && x * x + y * y <= cells * cells)
                    {
                        offsets.Add((x, y));
                    }
                }
            }
            Vector2 toward = preferToward - position;
            offsets.Sort((a, b) =>
            {
                int byDistance = (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y);
                if (byDistance != 0)
                {
                    return byDistance;
                }
                int bySide = (b.X * toward.X + b.Y * toward.Y).CompareTo(a.X * toward.X + a.Y * toward.Y);
                if (bySide != 0)
                {
                    return bySide;
                }
                return a.Y != b.Y ? b.Y.CompareTo(a.Y) : a.X.CompareTo(b.X);
            });
            foreach ((int X, int Y) cell in offsets)
            {
                Vector2 offset = new Vector2(cell.X, cell.Y) * GridStep;
                if (Overlaps(collision, position + offset, half))
                {
                    continue;
                }
                // 見つけた方向で、重なる所と重ならない所の境目まで詰める（重ならない側を残す）。
                float inside = 0f;
                float outside = 1f;
                for (int step = 0; step < RefineSteps; step++)
                {
                    float middle = (inside + outside) * 0.5f;
                    if (Overlaps(collision, position + offset * middle, half))
                    {
                        inside = middle;
                    }
                    else
                    {
                        outside = middle;
                    }
                }
                return position + offset * outside;
            }
            return position;
        }

        /// <summary>中心 center・半分の大きさ half の箱が、地形（動く足場と扉を含む）に重なるか（触れているだけは重ならない）。</summary>
        public static bool Overlaps(ICollisionQuery collision, Vector2 center, Vector2 half)
        {
            return collision.ComputePenetration(new Vector3(center.X, center.Y, 0f), new Vector3(half.X, half.Y, half.X), out _);
        }

        /// <summary>置いたギミック item が、ベルトコンベア（SpeedLane）の写しか。</summary>
        public static bool IsConveyor(in WorldObject item, ThiefContext thief)
        {
            return thief != null && item.Stored >= 0 && item.Stored < thief.Zones.Count && thief.Zones[item.Stored].Kind == StageZoneKind.SpeedLane;
        }

        // 範囲（中心 center・半分の大きさ half）の下の端の、左右の端と真ん中のすぐ下に、どれも地形があるか（床に置いた）。
        private static bool IsSupported(ICollisionQuery collision, Vector2 center, Vector2 half)
        {
            float bottom = center.Y - half.Y + 0.01f;
            float inset = MathF.Min(0.05f, half.X);
            float[] xs = { center.X - half.X + inset, center.X, center.X + half.X - inset };
            foreach (float x in xs)
            {
                if (!collision.Raycast(new Vector3(x, bottom, 0f), new Vector3(0f, -1f, 0f), SupportProbe, out _))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
