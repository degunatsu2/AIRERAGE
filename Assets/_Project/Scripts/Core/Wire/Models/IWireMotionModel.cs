using System;
using System.Numerics;

namespace Degunatsu.Core.Wire.Models
{
    /// <summary>
    /// 接続中の1ティック分の入力。
    /// </summary>
    public readonly struct WireMotionContext
    {
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly Vector3 Anchor;
        public readonly float RopeLength;

        /// <summary>接続してからのティック数（接続したティックが 0）。</summary>
        public readonly int AttachedTicks;

        /// <summary>接続した瞬間の速さ（m/s）。</summary>
        public readonly float AttachSpeed;

        public readonly Vector2 Stick;
        public readonly float Gravity;
        public readonly float DeltaTime;
        public readonly WireTuning Tuning;

        public WireMotionContext(
            Vector3 position, Vector3 velocity, Vector3 anchor, float ropeLength, int attachedTicks, float attachSpeed,
            Vector2 stick, float gravity, float deltaTime, WireTuning tuning)
        {
            Position = position;
            Velocity = velocity;
            Anchor = anchor;
            RopeLength = ropeLength;
            AttachedTicks = attachedTicks;
            AttachSpeed = attachSpeed;
            Stick = stick;
            Gravity = gravity;
            DeltaTime = deltaTime;
            Tuning = tuning;
        }

        /// <summary>
        /// スティックの入力による速さの倍率（WireTuning.AttachedInputInfluence）。モデルに共通の計算。
        /// direction（正規化済み）は、そのモデルで進む方向。同じ向きに入力すると速く、逆向きなら遅くなる。
        /// </summary>
        public float InputSpeedScale(Vector3 direction)
        {
            float along = Math.Clamp(Vector3.Dot(new Vector3(Stick.X, Stick.Y, 0f), direction), -1f, 1f);
            return 1f + Tuning.AttachedInputInfluence * along;
        }
    }

    /// <summary>
    /// 接続中の1ティック分の結果。
    /// </summary>
    public readonly struct WireMotionResult
    {
        /// <summary>このティックの移動に使う速度。</summary>
        public readonly Vector3 Velocity;

        /// <summary>このティックの後のロープ長。キャラクターと接続点の距離はこれを超えないよう補正される。</summary>
        public readonly float RopeLength;

        public WireMotionResult(Vector3 velocity, float ropeLength)
        {
            Velocity = velocity;
            RopeLength = ropeLength;
        }
    }

    /// <summary>
    /// 接続中の運動を計算する差し替え可能な部品（docs/03_architecture.md 4.3）。
    /// フェーズ遷移・照準・解除・速度の上限・ロープ長の補正は WireController が共通で行い、
    /// モデルは「速度」と「ロープ長」だけを決める。状態を持たない。
    /// </summary>
    public interface IWireMotionModel
    {
        WireMotionResult Step(in WireMotionContext context);
    }
}
