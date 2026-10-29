using System;
using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Movement;
using Airerage.Core.Physics;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
{
    /// <summary>
    /// キャラクター1人分の1ティックの処理の順序を決める。
    /// 入力 → アクション（攻撃）→ ワイヤーのフェーズ → 速度（ワイヤーまたは通常移動）→ 移動と衝突 → ロープ長の補正 → 接地
    /// → のけぞり中の跳ね返り → 壁への張り付き（WallCling） → ステージのギミック（StageZones。氷と地面の風は速度を決めるとき）。
    /// 壁に張り付いている間は、走る・跳ぶ代わりに WallCling.Hold（ジャンプで壁ジャンプ）。凍っている間（吹雪）は入力を受け付けず、横に動かない。
    /// アクション中（攻撃中・のけぞり中）は、移動・ジャンプ・ワイヤーの入力を無視し、向きも変えない（ActionController.ControlInput）。
    /// キャンセル（攻撃→ジャンプ、攻撃→ワイヤー）で Free に戻ったティックは、入力をそのまま使い、このティックにジャンプ・射出する。
    /// ヒットストップの間は、何も進めずに止まる（ボタンの前の状態も変えないので、押し続けたボタンは止まりが解けてから効く。
    /// 止まっている間に押して離した攻撃（押したときの向き）とジャンプも、止まりが解けてから効く）。
    /// 撃破されて復帰を待っている間も、何も進めない（docs/12 T4-2）。悪魔の手に握られている間は StageZones.HoldCapture だけ。
    /// </summary>
    public sealed class CharacterSimulator
    {
        private readonly KinematicMotor _motor;
        private readonly WireController _wire;
        private readonly IReadOnlyList<StageZone> _zones;
        private readonly ICollisionQuery _collision;

        /// <param name="zones">ステージのギミック（効かせる順に並べたもの）。null ならない。</param>
        public CharacterSimulator(ICollisionQuery collision, IReadOnlyList<StageZone> zones = null)
        {
            if (collision == null)
            {
                throw new ArgumentNullException(nameof(collision));
            }
            _motor = new KinematicMotor(collision);
            _wire = new WireController(collision, _motor);
            _zones = zones;
            _collision = collision;
        }

        /// <param name="tick">試合のティック数（周期・動きのあるステージのギミック）。</param>
        /// <param name="seed">試合の種（ステージのギミックのランダムな周期と位置）。</param>
        public void Step(ref CharacterState state, PlayerInput input, CharacterConfig config, int tick = 0, int seed = 0)
        {
            if (state.Combat.HasLeft)
            {
                // 試合から抜けた人は、ずっと動かない（docs/15 T6-10）。
                state.PreviousButtons = input.Buttons;
                return;
            }
            if (state.Combat.IsWaitingToRespawn)
            {
                // 撃破されて復帰を待っている間は動かない（ボタンの前の状態だけ更新し、待ち終わった直後に押したとみなさない）。
                state.Combat.RespawnTicksLeft--;
                CountDownInvincibility(ref state);
                state.PreviousButtons = input.Buttons;
                return;
            }
            if (state.Combat.CaptureTicks > 0)
            {
                // 悪魔の手に握られている間は動けず、手と一緒に動き、時間が来たら牢屋へ移る（ユーザーの要望、2026-10-26）。
                StageZones.HoldCapture(ref state, _zones, tick);
                CountDownInvincibility(ref state);
                state.PreviousButtons = input.Buttons;
                return;
            }
            if (state.Combat.HitstopTicksLeft > 0)
            {
                state.Combat.HitstopTicksLeft--;
                ActionController.RecordHitstopPresses(ref state, input, config);
                return;
            }

            InputFrame frame = ActionController.TakeHitstopPresses(ref state, input, out PlayerInput effectiveInput);
            MovementTuning movement = config.Movement;
            var halfExtents = movement.HalfExtents;
            var halfExtents2 = new System.Numerics.Vector2(halfExtents.X, halfExtents.Y);

            // 吹雪で凍っている間と、抹茶のクリームに張り付いている間と、感電している間は、入力を受け付けない（攻撃を受けてのけぞったら解ける。
            // ただし、攻撃の追加効果の感電（AttackHit.StunTicks）は、のけぞりのまま先に感電する）。
            if (state.Action.Kind == ActionKind.Hitstun)
            {
                state.Combat.FrozenTicksLeft = 0;
                state.Combat.CreamStickTicksLeft = 0;
                // バンクハイストの詠唱は、のけぞったら止まる（コインは得ない。ユーザーの回答、2026-10-28）。
                state.Combat.HeistTicks = 0;
                if (!state.Combat.HasStunKnockback)
                {
                    state.Combat.StunTicksLeft = 0;
                }
            }
            if (state.Combat.StunGuardTicks > 0)
            {
                state.Combat.StunGuardTicks--;
            }
            if (state.Combat.WhipGuardTicks > 0)
            {
                state.Combat.WhipGuardTicks--;
            }
            bool frozen = state.Combat.FrozenTicksLeft > 0;
            bool stuck = state.Combat.CreamStickTicksLeft > 0;
            bool stunned = state.Combat.StunTicksLeft > 0;
            bool drowning = state.Combat.DrownTicks > 0;
            // バンクハイストの詠唱の間は、その場で止まって入力を受け付けない（もう一度の R は ThiefRules が押したボタンから調べる）。
            bool chanting = state.Combat.HeistTicks > 0;
            if (stunned)
            {
                state.Combat.StunTicksLeft--;
            }
            if (frozen)
            {
                state.Combat.FrozenTicksLeft--;
            }
            if (stuck)
            {
                state.Combat.CreamStickTicksLeft--;
            }
            if (frozen || stuck || stunned || drowning || chanting)
            {
                effectiveInput = default;
                frame = new InputFrame(default, state.PreviousButtons);
            }
            else
            {
                // PASTRY CHEF の E をためている間のワイヤー・攻撃のボタンは、クリームの味を選ぶのに使う（ワイヤーと攻撃は出ない）。
                ActionController.TakeHoldChoice(ref state, ref frame, ref effectiveInput, config.Combat);
            }

            _motor.Depenetrate(ref state.Position, halfExtents);
            System.Numerics.Vector3 startPosition = state.Position;
            if (state.Action.IsFree && state.WallSide == 0)
            {
                UpdateFacing(ref state, frame, movement);
            }

            CountDownInvincibility(ref state);
            if (state.Combat.HazardImmunityTicks > 0)
            {
                state.Combat.HazardImmunityTicks--;
            }
            ActionController.Update(ref state, frame, config);
            if (!state.Action.IsFree || ActionController.IsRootedHold(state, config.Combat) || state.Combat.FlakStance)
            {
                frame = new InputFrame(ActionController.ControlInput(state, effectiveInput, config.Combat, state.Grounded), state.PreviousButtons);
                state.JumpBufferTicksLeft = 0;
                // 攻撃・ガードなどを始めたら、ジャンプの発生を待つのをやめる（跳ばない）。
                state.JumpSquatTicksLeft = 0;
            }

            _wire.UpdatePhase(ref state, frame, config.Wire);

            bool wasGrounded = state.Grounded;
            int airJumpsBefore = state.AirJumpsUsed;
            bool attached = state.Wire.Phase == WirePhase.Attached;
            if (state.WallSide != 0 && !WallCling.Continues(state, frame, movement, _motor, halfExtents))
            {
                // 張り付いていた長さは残す（着地か壁ジャンプで 0 に戻る。離れて張り付き直しても 4 秒を超えない）。
                state.WallSide = 0;
            }
            if (attached)
            {
                state.Velocity = _wire.ComputeAttachedVelocity(ref state, frame, config.Wire, movement.Gravity);
                state.JumpBufferTicksLeft = 0;
                state.JumpSquatTicksLeft = 0;
            }
            else if (state.WallSide != 0)
            {
                WallCling.Hold(ref state, frame, movement);
            }
            else
            {
                System.Numerics.Vector3 velocityBefore = state.Velocity;
                float drift = StageZones.GroundDrift(state, halfExtents2, _zones, tick, seed);
                Locomotion.UpdateVelocity(ref state, frame, movement, drift);
                ActionController.ApplyMotion(ref state, config);
                LimitSlowFall(ref state, airJumpsBefore, config);
                StageZones.ApplyFriction(ref state, velocityBefore, halfExtents2, _zones, tick);
                StageZones.ApplyWaterForces(ref state, velocityBefore.Y, movement.Gravity, _zones, tick);
            }
            if (frozen)
            {
                state.Velocity.X = 0f;
            }
            if (drowning)
            {
                // 溺れている: 動けず、まっすぐ沈む。
                state.Velocity = new System.Numerics.Vector3(0f, -StageZones.DrownSinkSpeed, 0f);
                state.WallSide = 0;
            }
            else if (stunned)
            {
                // 感電: 重力も受けず、その場にとどまる。攻撃の追加効果の感電が終わったら、その攻撃の吹き飛びで飛ぶ。
                state.Velocity = System.Numerics.Vector3.Zero;
                if (state.Combat.StunTicksLeft == 0 && state.Combat.HasStunKnockback)
                {
                    state.Velocity = new System.Numerics.Vector3(state.Combat.StunKnockbackX, state.Combat.StunKnockbackY, 0f);
                    state.Combat.HasStunKnockback = false;
                    state.Grounded = false;
                }
            }
            else if (chanting)
            {
                // 詠唱: 重力も受けず、その場にとどまる。
                state.Velocity = System.Numerics.Vector3.Zero;
            }
            else if (stuck)
            {
                // 抹茶のクリームに張り付いている: 壁の張り付きと同じ速さで、少しずつずり落ちる。
                state.Velocity = new System.Numerics.Vector3(0f, -movement.WallSlideSpeed, 0f);
            }

            System.Numerics.Vector3 beforeMove = state.Position;
            System.Numerics.Vector3 velocityBeforeMove = state.Velocity;
            _motor.Move(ref state.Position, ref state.Velocity, halfExtents, state.Velocity * SimTime.DeltaTime);

            if (attached)
            {
                _wire.UpdateRopeWrap(ref state, beforeMove, config.Wire);
                _wire.ApplyRopeConstraint(ref state, halfExtents);
            }

            Locomotion.ApplyGroundState(ref state, _motor.ProbeGround(state.Position, halfExtents), movement);
            if (state.Grounded)
            {
                state.WallSide = 0;
                state.WallClingTicks = 0;
            }
            if (state.Grounded || attached)
            {
                // 着地・ワイヤーの接続で、ゆっくり落ちるのは終わる。
                state.Combat.SlowFallSerial = 0;
            }
            ActionController.AfterMove(ref state, wasGrounded, velocityBeforeMove, config);
            Bounce(ref state, velocityBeforeMove, config.Combat);
            WallCling.TryStart(ref state, velocityBeforeMove, movement, _motor, halfExtents);
            StageZones.Apply(ref state, startPosition, config, _zones, tick, seed, _collision);

            // ゲームプレイは XY 平面（docs/01_game_design.md 4章）。
            state.Position.Z = 0f;
            state.Velocity.Z = 0f;
            state.PreviousButtons = input.Buttons;
        }

        /// <summary>
        /// ゆっくり落ちる（SPACE TRAVELER の地上技の後。C-4）: 落ちる速さを SlowFallTuning.MaxFallSpeed までにする。ジャンプ・空中ジャンプ・
        /// ワイヤー・ほかの攻撃や技・ガード・被弾（とその後の着地）で終わる。左右には動ける。
        /// </summary>
        private static void LimitSlowFall(ref CharacterState state, int airJumpsBefore, CharacterConfig config)
        {
            if (state.Combat.SlowFallSerial == 0)
            {
                return;
            }
            bool otherAttack = state.Action.Kind == ActionKind.Attack && state.Combat.AttackSerial + 1 != state.Combat.SlowFallSerial;
            if (otherAttack || state.Action.Kind == ActionKind.Guard || state.Action.Kind == ActionKind.Hitstun
                || state.Wire.Phase != WirePhase.Ready && state.Wire.Phase != WirePhase.Cooldown
                || state.AirJumpsUsed != airJumpsBefore)
            {
                state.Combat.SlowFallSerial = 0;
                return;
            }
            float max = config.Skills.SlowFall.MaxFallSpeed;
            if (state.Velocity.Y < -max)
            {
                state.Velocity.Y = -max;
            }
        }

        /// <summary>
        /// のけぞり中に床・壁・天井へ勢いよくぶつかったら跳ね返る（ユーザーの要望、2026-10-13）。地形は軸平行の箱なので、
        /// ぶつかると速度の X か Y が消える（床は、移動の衝突か接地のどちらかで消える）。消えた成分が BounceMinSpeed 以上なら、
        /// 逆向きに BounceRestitution 倍で返す。床で跳ね返ったら接地をやめる。接地の後に呼ぶ。
        /// </summary>
        private static void Bounce(ref CharacterState state, System.Numerics.Vector3 before, CombatTuning combat)
        {
            if (state.Action.Kind != ActionKind.Hitstun)
            {
                return;
            }
            bool bounced = false;
            if (MathF.Abs(before.X) >= combat.BounceMinSpeed && MathF.Abs(state.Velocity.X) < MathF.Abs(before.X) * 0.5f)
            {
                state.Velocity.X = -before.X * combat.BounceRestitution;
                bounced = true;
            }
            if (MathF.Abs(before.Y) >= combat.BounceMinSpeed && MathF.Abs(state.Velocity.Y) < MathF.Abs(before.Y) * 0.5f)
            {
                state.Velocity.Y = -before.Y * combat.BounceRestitution;
                if (state.Velocity.Y > 0f)
                {
                    state.Grounded = false;
                }
                bounced = true;
            }
            if (bounced)
            {
                state.Combat.BounceCount++;
            }
        }

        private static void CountDownInvincibility(ref CharacterState state)
        {
            if (state.Combat.InvincibleTicksLeft > 0)
            {
                state.Combat.InvincibleTicksLeft--;
            }
        }

        private static void UpdateFacing(ref CharacterState state, in InputFrame input, MovementTuning movement)
        {
            if (input.Move.X >= movement.MoveDeadzone)
            {
                state.Facing = 1;
            }
            else if (input.Move.X <= -movement.MoveDeadzone)
            {
                state.Facing = -1;
            }
        }
    }
}
