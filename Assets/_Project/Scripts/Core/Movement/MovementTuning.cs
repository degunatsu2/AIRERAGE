using System;
using System.Numerics;
using Airerage.Core.Common;

namespace Airerage.Core.Movement
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

        /// <summary>
        /// 空中での走行の最高速（m/s。0 なら RunSpeed と同じ）。地上と空中で速さの違うキャラクターに使う（JUGGERNAUT は地上 +50%・空中 −50%。
        /// ユーザーの案、2026-10-29）。
        /// </summary>
        public float AirRunSpeed;

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

        /// <summary>
        /// 地上のジャンプの発生（ティック。0 ならすぐ跳ぶ）。押してからこの長さだけ足場に残ってから跳ぶ（JUGGERNAUT。ユーザーの案、2026-10-29）。
        /// 待っている間に攻撃・ガード・ワイヤーなどを始めたら跳ばない。空中ジャンプはすぐ跳ぶ。
        /// </summary>
        public int JumpStartupTicks;

        /// <summary>空中ジャンプの回数。</summary>
        public int AirJumps = 1;

        /// <summary>足場を離れた後もジャンプできる猶予（ティック）。</summary>
        public int CoyoteTicks = 6;

        /// <summary>着地前に押したジャンプを受け付ける猶予（ティック）。</summary>
        public int JumpBufferTicks = 6;

        /// <summary>スティックの入力を無視する範囲（0〜1）。</summary>
        public float MoveDeadzone = 0.2f;

        /// <summary>壁ジャンプの横・縦の初速（m/s。壁から離れる向き。ユーザーの要望、2026-10-19）【仮】。</summary>
        public float WallJumpSpeedX = 9f;
        public float WallJumpSpeedY = 13f;

        /// <summary>
        /// 壁に張り付いていられる長さ（ティック。4 秒）。過ぎたら壁から離れ、着地するか壁ジャンプするまで張り付けない
        /// （ユーザーの要望、2026-10-20）【仮】。
        /// </summary>
        public int WallClingMaxTicks = 240;

        /// <summary>壁に張り付いている間、ずり落ちる速さ（m/s。張り付いた瞬間から少しずつ落ちる）【仮】。</summary>
        public float WallSlideSpeed = 1f;

        /// <summary>この速さ（m/s）より速く上へ動いているときは、壁に当たっても張り付かない（ジャンプ台などで昇っている途中）【仮】。</summary>
        public float WallClingMaxRiseSpeed = 6f;

        /// <summary>当たり判定の箱の半分の幅（m）。</summary>
        public float HalfWidth = 0.4f;

        /// <summary>当たり判定の箱の半分の高さ（m）。</summary>
        public float HalfHeight = 0.9f;

        public Vector3 HalfExtents => new Vector3(HalfWidth, HalfHeight, HalfWidth);

        /// <summary>同じ値を持つ別のインスタンス（行動の速さの強化で、値を変えた調整値を作るのに使う。SpeedBoosts）。</summary>
        public MovementTuning Clone()
        {
            return (MovementTuning)MemberwiseClone();
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(MovementTuning));
            v.Positive(nameof(RunSpeed), RunSpeed);
            v.NonNegative(nameof(AirRunSpeed), AirRunSpeed);
            v.NonNegative(nameof(JumpStartupTicks), JumpStartupTicks);
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
            v.NonNegative(nameof(WallJumpSpeedX), WallJumpSpeedX);
            v.NonNegative(nameof(WallJumpSpeedY), WallJumpSpeedY);
            v.Positive(nameof(WallClingMaxTicks), WallClingMaxTicks);
            v.NonNegative(nameof(WallSlideSpeed), WallSlideSpeed);
            v.NonNegative(nameof(WallClingMaxRiseSpeed), WallClingMaxRiseSpeed);
            v.Positive(nameof(HalfWidth), HalfWidth);
            v.Positive(nameof(HalfHeight), HalfHeight);
            return v;
        }
    }
}
