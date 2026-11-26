using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Skills;
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

        /// <summary>水に浮いているときの、中心の水面からの深さ（m。頭が水の上に出る）【仮】。</summary>
        public const float WaterFloatDepth = 0.35f;

        /// <summary>水の中で浮き上がる速さ（m/s）と、そこまでの加速（m/s²）【仮】。</summary>
        public const float WaterRiseSpeed = 4.5f;
        public const float WaterBuoyancy = 25f;

        /// <summary>
        /// 水の中の横の速さの上限（m/s。泳ぐ速さ）と、それより速いときに落とす割合（m/s²）【仮】。落とす割合は 2026-11-03 に 40 から 50 へ
        /// （空中の加速を 30 から 36 に上げたので、前へ入れたまま泳ぐ速さへ戻るまでの時間が前と同じくらいになるように。加速より十分に大きくする）。
        /// </summary>
        public const float WaterSwimSpeed = 4f;
        public const float WaterDrag = 50f;

        /// <summary>水面に浮いている間、ジャンプできるようにする長さ（コヨーテタイム。ティック）。</summary>
        public const int WaterJumpCoyoteTicks = 2;

        /// <summary>悪魔の手で牢屋へ移った後、また握られないティック数【仮】。</summary>
        public const int GrabImmunityTicks = 120;

        /// <summary>コイルガンが電気をためる長さ（ティック。1 秒。ユーザーの要望、2026-11-24）。</summary>
        public const int CoilChargeTicks = 60;

        /// <summary>
        /// コイルガンの側のクールタイム（ティック。2026-11-25 の要望: 人の側ではなくコイルガンの側で数える。撃ち出した直後に明ける短さ）。
        /// コイルガンは、中に人がいる間と、撃ち出してからこのティック数の間は使用中で、ほかの人も、撃ち出された人も入らない（IsCoilBusy）。
        /// 撃ち出された人は、ほかのコイルガンにはすぐ入れる。長さは、40 m/s で撃ち出された人が、中心から 3.8 × 2.6m のコイルの外へ
        /// 出るまで（どの向きでも 5 ティックほど）より少し長い 10 ティック（約 0.17 秒）。前は人の側で 40 ティック、ほかの危険にも当たらなかった。
        /// </summary>
        public const int CoilCooldownTicks = 10;

        /// <summary>コイルガンの照準の、向きとみなす最小の長さ（これより短い照準の入力では、前の向きのまま）。</summary>
        public const float CoilAimDeadzone = 0.2f;

        /// <summary>悪魔の手に握られている人の中心の、手の上の辺からの高さ（m。体の下が指の間に入る）【仮】。</summary>
        public const float GrabHoldHeight = 0.4f;

        /// <summary>溺れてから沈む速さ（m/s）と、撃破の線に届かなくても撃破にするティック数【仮】。</summary>
        public const float DrownSinkSpeed = 2.5f;
        public const int DrownKnockoutTicks = 180;

        /// <summary>電気を帯びたワイヤー（研究所のプラズマ球）のロープの太さ（半分。m）と、同じ相手にもう一度当たるまで（ティック）【仮】。</summary>
        public const float ChargedWireHalfThickness = 0.3f;
        public const int ChargedWireRehitTicks = 40;

        /// <summary>
        /// 電気を帯びたワイヤーが当たったときの判定のデータ【仮】（呼ぶたびに作る。静的な可変の値を持たない）。威力 4、ロープから離れる
        /// 向きへ小さく弾く（ロープに触れ続けないように）。
        /// </summary>
        public static AttackHit ChargedWireHit()
        {
            return new AttackHit { Damage = 4, Gauge = 60, HitstopTicks = 6, HitstunTicks = 22, KnockbackX = 9f, KnockbackY = 7f };
        }

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
        /// 氷: 地面に立って重なっている間、このティックの横の速さの変化（velocityBefore からの差）のうち、遅くなる向き（止まる・向きを
        /// 変える）の変化を Power 倍にする（勢いがあるとよく滑る）。止まった状態や同じ向きに速くなる変化は、ふつうの足場と同じ
        /// （動き出しが遅くならない。2026-11-03 のユーザーの要望）。何もしていないとき（Action.IsFree）だけ（攻撃の動きは変えない）。
        /// </summary>
        public static void ApplyFriction(ref CharacterState state, Vector3 velocityBefore, Vector2 halfExtents, IReadOnlyList<StageZone> zones,
            int tick = 0)
        {
            // スパイクの靴底（アイテム）を持っていれば、氷の上でも滑らない。
            if (zones == null || !state.Grounded || !state.Action.IsFree || state.Items.CountOf(Items.ItemId.SpikeSoles) > 0)
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
                    float before = velocityBefore.X;
                    float after = state.Velocity.X;
                    bool speedingUp = MathF.Abs(after) > MathF.Abs(before) && (before == 0f || MathF.Sign(after) == MathF.Sign(before));
                    if (!speedingUp)
                    {
                        state.Velocity.X = before + (after - before) * zone.Power;
                    }
                    return;
                }
            }
        }

        /// <summary>水 zone の中（中心が水面より下で、範囲の中）か。</summary>
        public static bool InWater(in StageZone zone, System.Numerics.Vector3 position)
        {
            return position.X >= zone.MinX && position.X <= zone.MaxX && position.Y < zone.MaxY && position.Y >= zone.MinY;
        }

        /// <summary>
        /// 水の中で下向きの速さを落とす割合（m/s²）: 水面に Limit で入ると、浮く深さから範囲の下の辺まで沈んで止まる（Limit² / (2 × 深さ)）。
        /// </summary>
        public static float WaterDeceleration(in StageZone zone)
        {
            float depth = MathF.Max(0.5f, zone.MaxY - WaterFloatDepth - zone.MinY);
            return zone.Limit * zone.Limit / (2f * depth);
        }

        /// <summary>
        /// 水の力（移動の前。速度を決めた直後）: 水の中なら、このティックにかかった重力を打ち消し、下へ動いていれば一定の割合で止め、止まったら浮き
        /// 上がり、浮く深さで止める（ジャンプ・波・吹き飛びで速く上へ動いている人は止めない）。横は泳ぐ速さまで遅くする。溺れている人には
        /// 何もしない（CharacterSimulator が沈める）。ワイヤーの接続中と壁の張り付き中は呼ばない。
        /// </summary>
        /// <param name="velocityBeforeY">速度を決める前の上下の速さ（このティックにかかった重力を、かかった分だけ打ち消す。落ちる速さの上限で
        /// 重力がかからなかったときは打ち消さない）。</param>
        public static void ApplyWaterForces(ref CharacterState state, float velocityBeforeY, float gravity, IReadOnlyList<StageZone> zones, int tick)
        {
            if (zones == null || state.Combat.DrownTicks > 0)
            {
                return;
            }
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Kind != StageZoneKind.Water)
                {
                    continue;
                }
                StageZone zone = zones[i].At(tick);
                if (!InWater(zone, state.Position))
                {
                    continue;
                }
                float dt = SimTime.DeltaTime;
                float pulled = velocityBeforeY - state.Velocity.Y;
                if (pulled > 0f)
                {
                    state.Velocity.Y += MathF.Min(pulled, gravity * dt);
                }
                float floatLine = zone.MaxY - WaterFloatDepth;
                if (state.Velocity.Y < 0f)
                {
                    state.Velocity.Y = MathF.Min(0f, state.Velocity.Y + WaterDeceleration(zone) * dt);
                }
                else if (state.Position.Y < floatLine)
                {
                    if (state.Velocity.Y < WaterRiseSpeed)
                    {
                        state.Velocity.Y = MathF.Min(WaterRiseSpeed, state.Velocity.Y + WaterBuoyancy * dt);
                    }
                }
                else if (state.Velocity.Y <= WaterRiseSpeed)
                {
                    state.Velocity.Y = 0f;
                }
                float capped = Math.Clamp(state.Velocity.X, -WaterSwimSpeed, WaterSwimSpeed);
                state.Velocity.X = SimMath.MoveTowards(state.Velocity.X, capped, WaterDrag * dt);
                return;
            }
        }

        /// <summary>
        /// 水の状態（移動の後）: 水に入ったら溺れるまでの残りを数え始め（陸に着くかワイヤーをつなぐと戻る）、水の中にいる間だけ減らし、
        /// 0 になったら溺れる。水面に浮いている間はジャンプできるようにし、壁には張り付かない。溺れている間は数えるだけ。
        /// </summary>
        private static void UpdateWater(ref CharacterState state, Vector3 startPosition, CharacterConfig config, IReadOnlyList<StageZone> zones,
            int tick)
        {
            if (state.Combat.DrownTicks > 0)
            {
                state.Combat.DrownTicks++;
                return;
            }
            bool attached = state.Wire.Phase == WirePhase.Attached;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Kind != StageZoneKind.Water)
                {
                    continue;
                }
                StageZone zone = zones[i].At(tick);
                if (!InWater(zone, state.Position))
                {
                    continue;
                }
                if (startPosition.Y >= zone.MaxY)
                {
                    state.Combat.SplashCount++;
                }
                int total = Math.Max(1, (int)MathF.Round(zone.Power * SimTime.TicksPerSecond));
                if (state.Combat.WaterTicksLeft <= 0 || attached)
                {
                    state.Combat.WaterTicksLeft = total;
                    state.Combat.WaterFloatTicks = total;
                }
                state.WallSide = 0;
                if (!attached && --state.Combat.WaterTicksLeft <= 0)
                {
                    Drown(ref state, config.Wire);
                    return;
                }
                if (state.Position.Y >= zone.MaxY - WaterFloatDepth - 0.3f)
                {
                    state.CoyoteTicksLeft = Math.Max(state.CoyoteTicksLeft, WaterJumpCoyoteTicks);
                }
                return;
            }
            if (state.Grounded || attached)
            {
                state.Combat.WaterTicksLeft = 0;
                state.Combat.WaterFloatTicks = 0;
            }
        }

        // 溺れる: 動けなくなり、沈み始める（何にも触れない。撃破の線を越えるか DrownKnockoutTicks で撃破）。
        private static void Drown(ref CharacterState state, WireTuning wire)
        {
            WireController.Interrupt(ref state, wire);
            state.Combat.WaterTicksLeft = 0;
            state.Combat.DrownTicks = 1;
            state.Combat.Holding = false;
            state.Combat.HoldTicks = 0;
            state.Combat.StunTicksLeft = 0;
            state.Combat.HasStunKnockback = false;
            state.Combat.FrozenTicksLeft = 0;
            state.Action = default;
            state.Velocity = new Vector3(0f, -DrownSinkSpeed, 0f);
            state.WallSide = 0;
            state.JumpBufferTicksLeft = 0;
            state.CoyoteTicksLeft = 0;
        }

        /// <param name="startPosition">このティックの初めの位置（加速リング・ワープゲートに入ったかを調べる）。</param>
        /// <param name="tick">試合のティック数（周期と動き）。</param>
        /// <param name="seed">試合の種（ランダムな周期と位置）。</param>
        /// <param name="collision">地形（吹雪の風上に地形があるか。null なら吹雪で必ず凍る）。</param>
        /// <param name="coilUsers">コイルガンが使用中かを調べる、全員の状態（SimWorld のキャラクター。このティックに先に進めた人は進めた後）。null なら調べない。</param>
        /// <param name="coilClones">同じく THIEF の分身（番号は coilUsers と同じ。持ち主の分身が出ている間だけ数える）。</param>
        public static void Apply(ref CharacterState state, Vector3 startPosition, CharacterConfig config, IReadOnlyList<StageZone> zones, int tick,
            int seed = 0, ICollisionQuery collision = null, IReadOnlyList<CharacterState> coilUsers = null,
            IReadOnlyList<CharacterState> coilClones = null)
        {
            if (zones == null)
            {
                return;
            }
            var halfExtents = new Vector2(config.Movement.HalfExtents.X, config.Movement.HalfExtents.Y);
            UpdateWater(ref state, startPosition, config, zones, tick);
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].SparesTeam(state.Combat.Team))
                {
                    // チーム対戦: 味方の THIEF が置いた危険・吹雪・悪魔の手は効かない（D-81）。
                    continue;
                }
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
                    case StageZoneKind.Water:
                        // 移動の前（ApplyWaterForces）と、この関数の初め（UpdateWater）に効かせる。
                        break;
                    case StageZoneKind.Grab:
                        if (CanBeHitByStage(state) && !state.Combat.IsUntouchable && state.Combat.HazardImmunityTicks <= 0
                            && zone.Overlaps(state.Position, halfExtents))
                        {
                            Capture(ref state, i, config.Wire);
                            // 握られたら、ほかのギミックは効かない。
                            return;
                        }
                        break;
                    case StageZoneKind.CoilGun:
                        // コイルガンが使用中（だれかが中にいる・撃ち出した直後）なら入らない（同じティックに先に入った人も数える）。
                        if (CanBeHitByStage(state) && !state.Combat.IsUntouchable && zone.Overlaps(state.Position, halfExtents)
                            && !IsCoilBusy(i, coilUsers, coilClones))
                        {
                            // コイルの中に収まる（悪魔の手と同じ握られ方）。コイルの向きの初めはギミックの向き。
                            Capture(ref state, i, config.Wire);
                            state.Combat.SkillAim = zone.Direction;
                            state.Position = new Vector3(zone.Center.X, zone.Center.Y, 0f);
                            return;
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"知らないギミックの種類です: {zone.Kind}");
                }
            }
        }

        // 悪魔の手に握られる: 動けなくなり（ワイヤー・攻撃・ため・凍り・感電は終わる）、手と一緒に動き始める。
        private static void Capture(ref CharacterState state, int zoneIndex, WireTuning wire)
        {
            WireController.Interrupt(ref state, wire);
            state.Action = default;
            state.Combat.Holding = false;
            state.Combat.HoldTicks = 0;
            state.Combat.FrozenTicksLeft = 0;
            state.Combat.StunTicksLeft = 0;
            state.Combat.HasStunKnockback = false;
            state.Combat.CreamStickTicksLeft = 0;
            state.Combat.CaptureTicks = 1;
            state.Combat.CaptureZone = zoneIndex;
            state.Combat.CaptureCount++;
            state.Velocity = Vector3.Zero;
            state.Grounded = false;
            state.WallSide = 0;
            state.JumpBufferTicksLeft = 0;
            state.CoyoteTicksLeft = 0;
        }

        /// <summary>
        /// 悪魔の手に握られている人を 1 ティック進める（CharacterSimulator が、ほかの処理の代わりに呼ぶ）: 手の上に握られたまま手と
        /// 一緒に動き、握られてから手の Power 秒で、手の Target（牢屋）へ移る（止まった状態。GrabImmunityTicks の間は、また握られない）。
        /// </summary>
        public static void HoldCapture(ref CharacterState state, IReadOnlyList<StageZone> zones, int tick, Vector2 aim = default)
        {
            int index = state.Combat.CaptureZone;
            if (zones != null && index >= 0 && index < zones.Count && zones[index].Kind == StageZoneKind.CoilGun)
            {
                HoldInCoil(ref state, zones[index].At(tick), aim);
                return;
            }
            if (zones == null || index < 0 || index >= zones.Count || zones[index].Kind != StageZoneKind.Grab)
            {
                // 手がない（ありえないが、状態を壊さないように放す）。
                state.Combat.CaptureTicks = 0;
                return;
            }
            StageZone hand = zones[index].At(tick);
            state.Combat.CaptureTicks++;
            state.Velocity = Vector3.Zero;
            int total = Math.Max(1, (int)MathF.Round(hand.Power * SimTime.TicksPerSecond));
            if (state.Combat.CaptureTicks > total)
            {
                state.Position = new Vector3(hand.Target.X, hand.Target.Y, 0f);
                state.Combat.CaptureTicks = 0;
                state.Combat.HazardImmunityTicks = GrabImmunityTicks;
                state.Grounded = false;
                return;
            }
            state.Position = new Vector3((hand.MinX + hand.MaxX) * 0.5f, hand.MaxY + GrabHoldHeight, 0f);
        }

        /// <summary>
        /// コイルガンの中の人を 1 ティック進める: コイルの中心に収まったまま、照準 aim（CoilAimDeadzone 以上の長さのとき）を
        /// コイルの向き（CombatState.SkillAim）として覚え、収まってから CoilChargeTicks たったら、その向きへ速さ Power で撃ち出す
        /// （空中ジャンプが戻る。2026-11-24 の要望）。撃ち出してから CoilCooldownTicks の間は、このコイルガンが使用中（CombatState.CoilCooldownTicks。
        /// 数えるのは SimWorld.Step の初め。人の側の危険の免除はない。2026-11-25）。
        /// </summary>
        private static void HoldInCoil(ref CharacterState state, StageZone coil, Vector2 aim)
        {
            if (aim.LengthSquared() >= CoilAimDeadzone * CoilAimDeadzone)
            {
                state.Combat.SkillAim = aim;
            }
            state.Combat.CaptureTicks++;
            state.Velocity = Vector3.Zero;
            state.Position = new Vector3(coil.Center.X, coil.Center.Y, 0f);
            if (state.Combat.CaptureTicks <= CoilChargeTicks)
            {
                return;
            }
            Vector2 direction = CoilDirection(state.Combat.SkillAim, coil.Direction);
            state.Velocity = new Vector3(direction.X * coil.Power, direction.Y * coil.Power, 0f);
            state.Combat.CaptureTicks = 0;
            state.Combat.CoilCooldownZone = state.Combat.CaptureZone;
            state.Combat.CoilCooldownTicks = CoilCooldownTicks;
            TakeOff(ref state);
        }

        /// <summary>コイルガンの向き（照準 aim が短ければ、ギミックの向き fallback）。表示も同じ向きを使う。</summary>
        public static Vector2 CoilDirection(Vector2 aim, Vector2 fallback)
        {
            float length = aim.Length();
            return length >= CoilAimDeadzone && length > SimMath.Epsilon ? aim / length : fallback;
        }

        /// <summary>state がコイルガンの中にいるか（握られている先が zones のコイルガン）。表示と音の区別に使う。</summary>
        public static bool IsInCoil(in CharacterState state, IReadOnlyList<StageZone> zones)
        {
            int index = state.Combat.CaptureZone;
            return state.Combat.CaptureTicks > 0 && zones != null && index >= 0 && index < zones.Count && zones[index].Kind == StageZoneKind.CoilGun;
        }

        /// <summary>state が握られていた先（CaptureZone）が、zones のコイルガンか（放された直後の区別に使う）。</summary>
        public static bool WasCoil(in CharacterState state, IReadOnlyList<StageZone> zones)
        {
            int index = state.Combat.CaptureZone;
            return zones != null && index >= 0 && index < zones.Count && zones[index].Kind == StageZoneKind.CoilGun;
        }

        /// <summary>
        /// コイルガン zoneIndex（効かせるギミックの一覧での番号。CombatState.CaptureZone と同じ）が使用中か: だれか（キャラクター・出ている
        /// 分身）が中にいるか、撃ち出してから CoilCooldownTicks たっていない（2026-11-25 の要望: コイルガンの側のクールタイム。同時に 1 人だけ）。
        /// </summary>
        public static bool IsCoilBusy(int zoneIndex, IReadOnlyList<CharacterState> characters, IReadOnlyList<CharacterState> clones = null)
        {
            if (characters == null)
            {
                return false;
            }
            for (int k = 0; k < characters.Count; k++)
            {
                if (HoldsCoil(characters[k], zoneIndex))
                {
                    return true;
                }
            }
            if (clones != null)
            {
                for (int k = 0; k < clones.Count && k < characters.Count; k++)
                {
                    if (characters[k].Combat.CloneTicksLeft > 0 && HoldsCoil(clones[k], zoneIndex))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HoldsCoil(in CharacterState state, int zoneIndex)
        {
            return (state.Combat.CaptureTicks > 0 && state.Combat.CaptureZone == zoneIndex)
                || (state.Combat.CoilCooldownTicks > 0 && state.Combat.CoilCooldownZone == zoneIndex);
        }

        /// <summary>コイルガンの側のクールタイムを 1 ティック進める（SimWorld.Step の初めに、全員と分身に）。</summary>
        public static void CountDownCoil(List<CharacterState> states)
        {
            for (int k = 0; k < states.Count; k++)
            {
                if (states[k].Combat.CoilCooldownTicks > 0)
                {
                    CharacterState state = states[k];
                    state.Combat.CoilCooldownTicks--;
                    states[k] = state;
                }
            }
        }

        /// <summary>コイルガンの中で、撃ち出すまでのため具合（0〜1。中にいなければ 0）。</summary>
        public static float CoilCharge(in CharacterState state)
        {
            return state.Combat.CaptureTicks <= 0 ? 0f : Math.Min(1f, state.Combat.CaptureTicks / (float)CoilChargeTicks);
        }

        // 外から入ったかを調べる範囲: 動くギミックは 1 つ前のティックの範囲（ゲートのほうが動いてきて重なったときも、入ったとみなす）。
        private static StageZone Before(in StageZone source, in StageZone now, int tick)
        {
            return source.Moves ? source.At(tick - 1) : now;
        }

        /// <summary>
        /// キャラクターのワイヤーが電気を帯びているか: 動かない接続点（地形・アンカーポイント）につないでいて、接続点が電気を帯びた危険
        /// （StageZone.Electric。研究所のプラズマ球）の範囲の中にある。
        /// </summary>
        public static bool IsWireCharged(in CharacterState state, IReadOnlyList<StageZone> zones)
        {
            if (zones == null || state.Wire.Phase != WirePhase.Attached || state.Wire.AnchorBody != 0)
            {
                return false;
            }
            for (int i = 0; i < zones.Count; i++)
            {
                StageZone zone = zones[i];
                if (zone.Electric && zone.Contains(state.Wire.Anchor))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 電気を帯びたワイヤー（2026-11-20 の要望: 研究所のプラズマ球にワイヤーをつないでいる間、ワイヤーに電気の攻撃判定を纏わせる）:
        /// 接続点から支点を順にたどってキャラクターまでのロープのどこかに触れた相手（つないだ人とその味方・干渉されない人以外）に、
        /// ChargedWireHit を当てる（hits に加える。当てたのはつないだ人。同じ相手には ChargedWireRehitTicks ごと。鞭と同じ
        /// CombatState.WhipGuardTicks で数える）。ロープから離れる向きに弾く。
        /// </summary>
        public static void StepChargedWires(IReadOnlyList<StageZone> zones, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            List<HitRecord> hits)
        {
            if (zones == null)
            {
                return;
            }
            for (int a = 0; a < states.Count; a++)
            {
                CharacterState owner = states[a];
                if (owner.Combat.IsUntouchable || !IsWireCharged(owner, zones))
                {
                    continue;
                }
                WireState wire = owner.Wire;
                for (int v = 0; v < states.Count; v++)
                {
                    CharacterState target = states[v];
                    if (v == a || target.Combat.IsUntouchable || target.Combat.WhipGuardTicks > 0 || TeamRules.Allies(states, a, v))
                    {
                        continue;
                    }
                    Vector3 half = configs[v].Movement.HalfExtents;
                    var center = new Vector2(target.Position.X, target.Position.Y);
                    var reach = new Vector2(half.X + ChargedWireHalfThickness, half.Y + ChargedWireHalfThickness);
                    if (!TouchesRope(wire, owner.Position, center, reach, out Vector2 near))
                    {
                        continue;
                    }
                    target.Combat.WhipGuardTicks = ChargedWireRehitTicks;
                    states[v] = target;
                    // ロープから離れる向き（触れた所の左右。真上・真下なら、つないだ人から離れる向き）。
                    float side = center.X - near.X;
                    int facing = MathF.Abs(side) > 1e-3f ? (side > 0f ? 1 : -1) : (center.X >= owner.Position.X ? 1 : -1);
                    hits.Add(new HitRecord(a, v, AttackSlot.Ground, 0, ChargedWireHit(), facing, AttackPower.Normal, fromObject: true));
                }
            }
        }

        // ロープ（接続点 → 支点 → キャラクター）のどこかが、中心 center・半分の大きさ half の箱に触れるか。near は触れた区間の、
        // 箱の中心にいちばん近い点。
        private static bool TouchesRope(in WireState wire, Vector3 holder, Vector2 center, Vector2 half, out Vector2 near)
        {
            var from = new Vector2(wire.Anchor.X, wire.Anchor.Y);
            int pivots = wire.Pivots.Count;
            for (int i = 0; i <= pivots; i++)
            {
                Vector3 next = i < pivots ? wire.Pivots[i] : holder;
                var to = new Vector2(next.X, next.Y);
                if (ElectricRules.SegmentTouchesBox(from, to, center, half))
                {
                    near = NearestOnSegment(from, to, center);
                    return true;
                }
                from = to;
            }
            near = default;
            return false;
        }

        private static Vector2 NearestOnSegment(Vector2 from, Vector2 to, Vector2 point)
        {
            Vector2 d = to - from;
            float length = d.LengthSquared();
            if (!(length > 1e-8f))
            {
                return from;
            }
            float t = Vector2.Dot(point - from, d) / length;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return from + d * t;
        }

        /// <summary>
        /// 危険の柱が、今のティックに効いているか（効いていれば、その柱の横の範囲）。周期の始まりから ActiveTicks の間だけ効く。
        /// </summary>
        public static bool HazardColumnAt(in StageZone zone, int tick, int seed, out float minX, out float maxX)
        {
            return HazardColumnAt(zone, tick, seed, out minX, out maxX, out _);
        }

        /// <summary>HazardColumnAt と同じ。elapsed は効き始めてからのティック数（落ちてくる物の帯 StageZone.FallBand に使う）。</summary>
        public static bool HazardColumnAt(in StageZone zone, int tick, int seed, out float minX, out float maxX, out int elapsed)
        {
            zone.Cycle(tick, seed, out int cycle, out int start, out _);
            elapsed = tick - start;
            if (cycle < 0 || elapsed >= zone.ActiveTicks)
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
        internal static bool CanBeHitByStage(in CharacterState state)
        {
            return !state.Combat.IsWaitingToRespawn && state.Combat.InvincibleTicksLeft <= 0;
        }

        private static void TryHazard(ref CharacterState state, StageZone zone, Vector2 halfExtents, int tick, int seed, WireTuning wire)
        {
            if (!HazardColumnAt(zone, tick, seed, out float minX, out float maxX, out int elapsed))
            {
                return;
            }
            Vector3 p = state.Position;
            bool hit = zone.FallLength > 0f ? zone.OverlapsFallBand(p, halfExtents, minX, maxX, elapsed) : zone.OverlapsColumn(p, halfExtents, minX, maxX);
            if (!hit)
            {
                return;
            }
            float speed = zone.Limit > 0f ? zone.Limit : HazardKnockbackSpeed(zone.Power);
            // 柱の真ん中から外へ少し押し出す（同じ所に落ちてこないように）。
            float away = p.X >= (minX + maxX) * 0.5f ? 1f : -1f;
            Strike(ref state, new Vector3(zone.Direction.X * speed + away * 3f, zone.Direction.Y * speed, 0f), Math.Max(1, (int)zone.Power),
                zone.ActiveTicks + 20, wire);
        }

        /// <summary>
        /// ステージの危険に当たる（溶岩流・つらら・研究所のミサイル・競技場のボール）: ワイヤーが外れ、velocity で吹き飛んでのけぞり
        /// （HazardHitstunTicks。ヒットストップ HazardHitstopTicks）、power の枚数のコインを落とす（SimWorld が CoinDropRules.DropOnHazards
        /// で落とす。ユーザーの要望、2026-10-20・21）。immunityTicks の間は、ほかのステージの危険に当たらない。
        /// </summary>
        public static void Strike(ref CharacterState state, Vector3 velocity, int power, int immunityTicks, WireTuning wire)
        {
            WireController.Interrupt(ref state, wire);
            state.Velocity = velocity;
            state.Action = new ActionState { Kind = ActionKind.Hitstun, Duration = HazardHitstunTicks };
            state.Combat.Holding = false;
            state.Combat.HoldTicks = 0;
            state.Combat.FrozenTicksLeft = 0;
            state.Combat.StunTicksLeft = 0;
            state.Combat.HasStunKnockback = false;
            state.Combat.HitstopTicksLeft = Math.Max(state.Combat.HitstopTicksLeft, HazardHitstopTicks);
            state.Combat.HazardImmunityTicks = Math.Max(state.Combat.HazardImmunityTicks, immunityTicks);
            state.Combat.HazardHitCount++;
            state.Combat.HazardDropPower = Math.Max(1, power);
            state.Grounded = false;
            state.WallSide = 0;
            state.JumpBufferTicksLeft = 0;
        }

        /// <summary>ステージの危険に当たる状態か（撃破されて待っている間・干渉されない間・危険の後の当たらない間は当たらない）。</summary>
        public static bool CanBeStruck(in CharacterState state)
        {
            return CanBeHitByStage(state) && !state.Combat.IsUntouchable && state.Combat.HazardImmunityTicks <= 0;
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
