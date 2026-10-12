using System;
using System.Numerics;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Movement
{
    /// <summary>
    /// 基本移動の調整値。初期値はすべて【仮】（docs/06_prototype_plan.md 4.1）。
    /// Unity 側では ScriptableObject がこのクラスを持ち、インスペクターで編集する。
    /// </summary>
    [Serializable]
    public sealed class MovementTuning
    {
        /// <summary>走行の最高速（m/s）。</summary>
        public float RunSpeed = 8f;

        /// <summary>地上での加速・減速（m/s²）。</summary>
        public float GroundAcceleration = 80f;

        /// <summary>空中での加速（m/s²）。</summary>
        public float AirAcceleration = 30f;

        /// <summary>空中で入力がないときの減速（m/s²）。小さいほど慣性が長く残る。</summary>
        public float AirDrag = 2f;

        /// <summary>重力加速度（m/s²）。</summary>
        public float Gravity = 30f;

        /// <summary>最大落下速度（m/s）。</summary>
        public float MaxFallSpeed = 20f;

        /// <summary>ジャンプの初速（m/s）。</summary>
        public float JumpSpeed = 12f;

        /// <summary>空中ジャンプの回数。</summary>
        public int AirJumps = 1;

        /// <summary>足場を離れた後もジャンプできる猶予（ティック）。</summary>
        public int CoyoteTicks = 6;

        /// <summary>着地前に押したジャンプを受け付ける猶予（ティック）。</summary>
        public int JumpBufferTicks = 6;

        /// <summary>スティックの入力を無視する範囲（0〜1）。</summary>
        public float MoveDeadzone = 0.2f;

        /// <summary>当たり判定の箱の半分の幅（m）。</summary>
        public float HalfWidth = 0.4f;

        /// <summary>当たり判定の箱の半分の高さ（m）。</summary>
        public float HalfHeight = 0.9f;

        public Vector3 HalfExtents => new Vector3(HalfWidth, HalfHeight, HalfWidth);

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(MovementTuning));
            v.Positive(nameof(RunSpeed), RunSpeed);
            v.Positive(nameof(GroundAcceleration), GroundAcceleration);
            v.NonNegative(nameof(AirAcceleration), AirAcceleration);
            v.NonNegative(nameof(AirDrag), AirDrag);
            v.NonNegative(nameof(Gravity), Gravity);
            v.Positive(nameof(MaxFallSpeed), MaxFallSpeed);
            v.NonNegative(nameof(JumpSpeed), JumpSpeed);
            v.NonNegative(nameof(AirJumps), AirJumps);
            v.NonNegative(nameof(CoyoteTicks), CoyoteTicks);
            v.NonNegative(nameof(JumpBufferTicks), JumpBufferTicks);
            v.InRange(nameof(MoveDeadzone), MoveDeadzone, 0f, 0.95f);
            v.Positive(nameof(HalfWidth), HalfWidth);
            v.Positive(nameof(HalfHeight), HalfHeight);
            return v;
        }
    }
}
