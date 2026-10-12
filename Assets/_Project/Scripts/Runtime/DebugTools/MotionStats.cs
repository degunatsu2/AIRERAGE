using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// 調整のための計測（docs/06_prototype_plan.md T1-9）。ティックごとの状態から、今の速さ、最高速、
    /// 接続を解除した直後の速さ（出口速度）を求める。速さは XY 平面上の速度の大きさ（m/s）。
    /// </summary>
    public sealed class MotionStats
    {
        private bool _wasAttached;

        public float CurrentSpeed { get; private set; }
        public float MaxSpeed { get; private set; }

        /// <summary>最後に接続を解除したティックの速さ。まだ解除していなければ null。</summary>
        public float? LastExitSpeed { get; private set; }

        public void Step(in CharacterState state)
        {
            CurrentSpeed = new System.Numerics.Vector2(state.Velocity.X, state.Velocity.Y).Length();
            if (CurrentSpeed > MaxSpeed)
            {
                MaxSpeed = CurrentSpeed;
            }

            bool attached = state.Wire.Phase == WirePhase.Attached;
            if (_wasAttached && !attached)
            {
                LastExitSpeed = CurrentSpeed;
            }
            _wasAttached = attached;
        }

        public void Reset()
        {
            _wasAttached = false;
            CurrentSpeed = 0f;
            MaxSpeed = 0f;
            LastExitSpeed = null;
        }
    }
}
