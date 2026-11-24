using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// 動く足場と扉（StagePlatform。ユーザーの要望、2026-10-26）の決まり。SimWorld が呼ぶ。
    /// - ティックの初め（Carry）: 前のティックから動いた足場に乗っている人と、横に張り付いている人を、足場と一緒に動かす。足場・扉に
    ///   つないだワイヤーの接続点も動かす（足場がなくなったら外れる）。
    /// - ティックの終わり（UpdateDoors）: 扉が開こうとしていれば（周期・ボタン・扉そのもの。IsAccessed）開き、いなければ閉じる
    ///   （閉じる先に人がいれば待つ）。荷役の網はばねのように伸び縮みする（StepHoist）。
    /// - 全員を進めた後（Bounce）: 荷役の網に重なった人を跳ね返す（バンパーと同じ決まり）。
    /// 当たりは WorldCollision.SetPlatforms（地形と同じように乗れる・ぶつかる。Attachable ならワイヤーがつながる。荷役の網は当たらない）。
    /// </summary>
    public static class StagePlatforms
    {
        /// <summary>
        /// 荷役の網のばね（2026-11-21 の要望。1 ティックあたり。伸び具合の差に掛けるかたさと、速さに掛ける減衰）【仮】: つないでいる間は
        /// 速く伸び（約 0.45 秒で下がりきり、2 割ほど行きすぎて跳ね返る）、外すとゆっくり戻る（行きすぎずに約 4 秒）。
        /// </summary>
        public const float HoistPullStiffness = 0.017f;
        public const float HoistPullDamping = 0.12f;
        public const float HoistReturnStiffness = 0.0004f;
        public const float HoistReturnDamping = 0.04f;

        /// <summary>ぶら下がった人が落ちる速さ 1 m/s あたりに、網を引き下げる加速度（m/s²）【仮】。強く落ちてつなぐほど大きく跳ね返る。</summary>
        public const float HoistTugPerSpeed = 3f;

        /// <summary>網が伸びすぎる・縮みすぎる限り（下がる長さに対する割合）。</summary>
        public const float HoistMaxStretch = 1.6f;
        public const float HoistMaxRecoil = 0.3f;
        /// <summary>乗っている・張り付いているとみなす、足場との隙間（m）。</summary>
        public const float ContactGap = 0.06f;

        /// <summary>1 ティックにこれより大きく動いた足場（道のりの最初に戻った）では、人を運ばない（m）。</summary>
        public const float MaxCarryStep = 3f;

        /// <summary>足場 index の、ティック tick（previous なら前のティック）の中心。なければ false。</summary>
        public static bool TryGetCenter(IReadOnlyList<StagePlatform> platforms, StageState stage, int index, int tick, bool previous,
            out Vector2 center)
        {
            StagePlatform platform = platforms[index];
            if (platform.UsesState)
            {
                return platform.TryGetCenter(tick, previous ? stage.PreviousOf(index) : stage.OpenOf(index), out center);
            }
            return platform.TryGetCenter(previous ? tick - 1 : tick, 0, out center);
        }

        /// <summary>
        /// ティック tick の初めに、前のティックから動いた足場に乗っている・張り付いている人を運び、つないだワイヤーの接続点を動かす。
        /// なくなった足場・扉につないでいたワイヤーは外す。
        /// </summary>
        public static void Carry(IReadOnlyList<StagePlatform> platforms, StageState stage, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, int tick)
        {
            if (platforms == null || platforms.Count == 0)
            {
                return;
            }
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                // メーデーで吊られている人（JUGGERNAUT の R）は、その場にいないので運ばない。
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway)
                {
                    continue;
                }
                CharacterConfig config = configs[c];
                bool changed = CarryBody(platforms, stage, ref state, config, tick);
                changed |= MoveAnchor(platforms, stage, ref state, config.Wire, tick);
                if (changed)
                {
                    states[c] = state;
                }
            }
        }

        // 乗っている・張り付いている足場があれば、その動いた分だけ動かす（最初に見つけた足場だけ）。
        private static bool CarryBody(IReadOnlyList<StagePlatform> platforms, StageState stage, ref CharacterState state, CharacterConfig config,
            int tick)
        {
            if (!state.Grounded && state.WallSide == 0)
            {
                return false;
            }
            float halfWidth = config.Movement.HalfWidth;
            float halfHeight = config.Movement.HalfHeight;
            Vector3 p = state.Position;
            for (int i = 0; i < platforms.Count; i++)
            {
                // 通り抜ける足場（荷役の網）には乗れない。
                if (!platforms[i].IsSolid)
                {
                    continue;
                }
                if (!TryGetCenter(platforms, stage, i, tick, true, out Vector2 before) || !TryGetCenter(platforms, stage, i, tick, false, out Vector2 now))
                {
                    continue;
                }
                Vector2 delta = now - before;
                if (delta == Vector2.Zero || delta.LengthSquared() > MaxCarryStep * MaxCarryStep)
                {
                    continue;
                }
                platforms[i].BoundsAt(before, out Vector2 min, out Vector2 max);
                bool standing = state.Grounded && MathF.Abs(p.Y - halfHeight - max.Y) <= ContactGap
                    && p.X + halfWidth > min.X && p.X - halfWidth < max.X;
                bool clinging = state.WallSide != 0 && p.Y + halfHeight > min.Y && p.Y - halfHeight < max.Y
                    && (state.WallSide > 0 ? MathF.Abs(p.X + halfWidth - min.X) <= ContactGap : MathF.Abs(p.X - halfWidth - max.X) <= ContactGap);
                if (standing || clinging)
                {
                    state.Position = new Vector3(p.X + delta.X, p.Y + delta.Y, p.Z);
                    return true;
                }
            }
            return false;
        }

        // 足場・扉につないだワイヤーの接続点を、足場の動いた分だけ動かす（足場がなくなったら外す）。
        private static bool MoveAnchor(IReadOnlyList<StagePlatform> platforms, StageState stage, ref CharacterState state, WireTuning wire, int tick)
        {
            if (state.Wire.Phase != WirePhase.Attached || state.Wire.AnchorBody == 0 || state.Wire.AnchorBody >= StageTraps.DonutBodyBase)
            {
                // ドーナツにつないだワイヤーは StageTraps.CarryWires が運ぶ。
                return false;
            }
            int index = state.Wire.AnchorBody - 1;
            if (index >= platforms.Count || !TryGetCenter(platforms, stage, index, tick, false, out Vector2 now))
            {
                WireController.Release(ref state, WireReleaseReason.AnchorGone, wire);
                return true;
            }
            if (!TryGetCenter(platforms, stage, index, tick, true, out Vector2 before))
            {
                return false;
            }
            Vector2 delta = now - before;
            if (delta == Vector2.Zero)
            {
                return false;
            }
            if (delta.LengthSquared() > MaxCarryStep * MaxCarryStep)
            {
                WireController.Release(ref state, WireReleaseReason.AnchorGone, wire);
                return true;
            }
            var move = new Vector3(delta.X, delta.Y, 0f);
            state.Wire.Anchor += move;
            state.Wire.HookPosition += move;
            return true;
        }

        /// <summary>
        /// 扉 index が、ティック tick に開こうとしているか: 周期で開く扉は周期の開いている間、ボタンで開く扉は同じ組のボタンに、そうでない
        /// 扉は扉そのものに、だれかがワイヤーをつないでいる間。
        /// </summary>
        public static bool IsAccessed(IReadOnlyList<StagePlatform> platforms, int index, IReadOnlyList<CharacterState> states, int tick)
        {
            StagePlatform door = platforms[index];
            if (door.OpenPeriod > 0)
            {
                return door.ScheduledOpen(tick);
            }
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (state.Combat.HasLeft || state.Wire.Phase != WirePhase.Attached || state.Wire.AnchorBody == 0)
                {
                    continue;
                }
                int body = state.Wire.AnchorBody - 1;
                if (door.Group > 0)
                {
                    if (body < platforms.Count && platforms[body].IsButton && platforms[body].Group == door.Group)
                    {
                        return true;
                    }
                }
                else if (body == index)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>ボタン index に、だれかがワイヤーをつないでいるか（表示用）。</summary>
        public static bool IsPressed(int index, IReadOnlyList<CharacterState> states)
        {
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (!state.Combat.HasLeft && state.Wire.Phase == WirePhase.Attached && state.Wire.AnchorBody == index + 1)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ティックの終わりに、扉の開き具合を進める: 開こうとしていれば（IsAccessed。tick は進めた後のティック）開き（OpenTicks で全開）、
        /// いなければ閉じる（CloseTicks で閉じきる。閉じた先の扉にだれかが重なるなら、そのティックは閉じない）。扉でない足場は何もしない。
        /// </summary>
        public static void UpdateDoors(IReadOnlyList<StagePlatform> platforms, StageState stage, List<CharacterState> states,
            IReadOnlyList<CharacterConfig> configs, int tick)
        {
            if (platforms == null)
            {
                return;
            }
            for (int i = 0; i < platforms.Count; i++)
            {
                StagePlatform door = platforms[i];
                if (door.IsHoist)
                {
                    StepHoist(door, stage, i, states, IsAccessed(platforms, i, states, tick));
                    continue;
                }
                if (!door.IsDoor)
                {
                    continue;
                }
                int open = stage.OpenOf(i);
                int next;
                if (IsAccessed(platforms, i, states, tick))
                {
                    next = Math.Min(StagePlatform.DoorScale, open + Step(door.OpenTicks));
                }
                else
                {
                    next = Math.Max(0, open - Step(door.CloseTicks));
                    if (next < open && Blocked(door, next, states, configs))
                    {
                        next = open;
                    }
                }
                stage.Advance(i, next);
            }
        }

        /// <summary>
        /// 荷役の網 index の伸び具合をばねで 1 ティック進める（accessed ならだれかがワイヤーをつないでいる）。つないでいる間は伸びきった所へ
        /// かたいばねで引かれ（少し行きすぎて跳ね返る）、つないだ人が落ちる勢いが強いほど強く引き下げられる。外すと、やわらかいばねで
        /// 行きすぎずにゆっくり戻る。値は整数で持つ（どの PC でも同じ）。
        /// </summary>
        public static void StepHoist(StagePlatform hoist, StageState stage, int index, IReadOnlyList<CharacterState> states, bool accessed)
        {
            float scale = StagePlatform.HoistScale;
            float e = stage.OpenOf(index);
            float v = stage.SpeedOf(index);
            float target = accessed ? scale : 0f;
            float k = accessed ? HoistPullStiffness : HoistReturnStiffness;
            float c = accessed ? HoistPullDamping : HoistReturnDamping;
            float a = k * (target - e) - c * v;
            if (accessed)
            {
                // ぶら下がった人の落ちる勢い（m/s）を、網の伸び具合の単位の加速度にする。
                float dt = SimTime.DeltaTime;
                a += HoistTugPerSpeed * FallSpeedOfHangers(states, index) * dt * dt * scale / hoist.OpenHeight;
            }
            v += a;
            e += v;
            float max = scale * HoistMaxStretch;
            float min = -scale * HoistMaxRecoil;
            if (e > max)
            {
                e = max;
                v = MathF.Min(v, 0f);
            }
            if (e < min)
            {
                e = min;
                v = MathF.Max(v, 0f);
            }
            // 止まりかけたら、ぴたりと止める（伸び具合の 0.05% 未満のずれと、ごく遅い速さ）。
            if (MathF.Abs(e - target) < scale * 0.0005f && MathF.Abs(v) < scale * 0.0002f)
            {
                e = target;
                v = 0f;
            }
            stage.Advance(index, (int)MathF.Round(e), (int)MathF.Round(v));
        }

        // 荷役の網 index にワイヤーをつないでいる人の、下へ落ちる速さの合計（m/s。上へ動いている人は数えない）。
        private static float FallSpeedOfHangers(IReadOnlyList<CharacterState> states, int index)
        {
            float sum = 0f;
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (!state.Combat.HasLeft && state.Wire.Phase == WirePhase.Attached && state.Wire.AnchorBody == index + 1 && state.Velocity.Y < 0f)
                {
                    sum += -state.Velocity.Y;
                }
            }
            return sum;
        }

        /// <summary>
        /// 全員を進めた後に、荷役の網（BouncePower が正）に重なった人を跳ね返す（網の中心から外向き。横長の網なら、上から乗れば上へ。
        /// バンパーと同じ決まり StageZones.BumpAlong）。復帰を待っている人・メーデーで吊られている人は除く。
        /// </summary>
        public static void Bounce(IReadOnlyList<StagePlatform> platforms, StageState stage, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            int tick)
        {
            if (platforms == null)
            {
                return;
            }
            for (int i = 0; i < platforms.Count; i++)
            {
                StagePlatform net = platforms[i];
                if (!net.IsHoist || !(net.BouncePower > 0f) || !TryGetCenter(platforms, stage, i, tick, false, out Vector2 center))
                {
                    continue;
                }
                for (int c = 0; c < states.Count; c++)
                {
                    CharacterState state = states[c];
                    if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway || state.Combat.HasLeft)
                    {
                        continue;
                    }
                    float hw = configs[c].Movement.HalfWidth;
                    float hh = configs[c].Movement.HalfHeight;
                    float dx = (state.Position.X - center.X) / (net.HalfSize.X + hw);
                    float dy = (state.Position.Y - center.Y) / (net.HalfSize.Y + hh);
                    if (MathF.Abs(dx) >= 1f || MathF.Abs(dy) >= 1f)
                    {
                        continue;
                    }
                    float length = MathF.Sqrt(dx * dx + dy * dy);
                    Vector2 normal = length > 1e-4f ? new Vector2(dx / length, dy / length) : Vector2.UnitY;
                    if (StageZones.BumpAlong(ref state, normal, net.BouncePower))
                    {
                        states[c] = state;
                    }
                }
            }
        }

        // 1 ティックに進む開き具合（全体をティック数で割り、切り上げる）。
        private static int Step(int ticks)
        {
            return (StagePlatform.DoorScale + ticks - 1) / ticks;
        }

        // 開き具合 open の扉に、だれかが重なるか。
        private static bool Blocked(StagePlatform door, int open, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs)
        {
            door.TryGetCenter(0, open, out Vector2 center);
            door.BoundsAt(center, out Vector2 min, out Vector2 max);
            for (int c = 0; c < states.Count; c++)
            {
                CharacterState state = states[c];
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway)
                {
                    continue;
                }
                float hw = configs[c].Movement.HalfWidth;
                float hh = configs[c].Movement.HalfHeight;
                Vector3 p = state.Position;
                if (p.X + hw > min.X + 1e-3f && p.X - hw < max.X - 1e-3f && p.Y + hh > min.Y + 1e-3f && p.Y - hh < max.Y - 1e-3f)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
