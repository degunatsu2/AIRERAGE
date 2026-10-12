using System;
using Degunatsu.Core.Common;
using Degunatsu.Core.Movement;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Simulation
{
    /// <summary>
    /// キャラクター1人分の調整値の組み合わせ。参照で共有するので、プレイ中の調整が次のティックから反映される。
    /// シミュレーションはこの値を書き換えない。
    /// </summary>
    [Serializable]
    public sealed class CharacterConfig
    {
        public MovementTuning Movement = new MovementTuning();
        public WireTuning Wire = new WireTuning();

        /// <summary>不正な値があれば InvalidTuningException を投げる。</summary>
        public void Validate()
        {
            var v = new TuningValidator(nameof(CharacterConfig));
            v.NotNull(nameof(Movement), Movement);
            v.NotNull(nameof(Wire), Wire);
            if (Movement != null)
            {
                v.Merge(Movement.Validate());
            }
            if (Wire != null)
            {
                v.Merge(Wire.Validate());
            }
            v.ThrowIfInvalid();
        }
    }
}
