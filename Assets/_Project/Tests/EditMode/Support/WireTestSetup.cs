using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Tests.Support
{
    /// <summary>
    /// 接続済みの状態から始めるテストの補助。
    /// </summary>
    public static class WireTestSetup
    {
        /// <summary>接続時間の上限を十分長くした設定。</summary>
        public static CharacterConfig LongAttachConfig(WireModelType modelType)
        {
            var config = new CharacterConfig();
            config.Wire.ModelType = modelType;
            config.Wire.MaxAttachTicks = 100000;
            return config;
        }

        /// <summary>position で anchor に接続し、ワイヤーボタンを押し続けている状態にする。</summary>
        public static void AttachAt(SimHarness harness, Vector3 position, Vector3 anchor, Vector3 velocity = default)
        {
            var state = harness.State;
            state.Position = position;
            state.Velocity = velocity;
            state.Grounded = false;
            state.PreviousButtons = InputButtons.Wire;
            state.Wire = new WireState
            {
                Phase = WirePhase.Attached,
                Anchor = anchor,
                RopeLength = Vector3.Distance(position, anchor),
            };
            harness.State = state;
        }

        public static float DistanceToAnchor(CharacterState state)
        {
            Vector3 d = state.Position - state.Wire.Anchor;
            d.Z = 0f;
            return d.Length();
        }
    }
}
