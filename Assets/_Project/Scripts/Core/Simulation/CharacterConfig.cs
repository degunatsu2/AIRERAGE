using System;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Movement;
using Airerage.Core.Skills;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
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
        public CombatTuning Combat = new CombatTuning();

        /// <summary>技の効果の値（置く物、投射物、強化。docs/13 T5-6）。</summary>
        public SkillTuning Skills = new SkillTuning();

        /// <summary>不正な値があれば InvalidTuningException を投げる。</summary>
        public void Validate()
        {
            var v = new TuningValidator(nameof(CharacterConfig));
            v.NotNull(nameof(Movement), Movement);
            v.NotNull(nameof(Wire), Wire);
            v.NotNull(nameof(Combat), Combat);
            v.NotNull(nameof(Skills), Skills);
            if (Movement != null)
            {
                v.Merge(Movement.Validate());
            }
            if (Wire != null)
            {
                v.Merge(Wire.Validate());
            }
            if (Combat != null)
            {
                v.Merge(Combat.Validate());
            }
            if (Skills != null)
            {
                v.Merge(Skills.Validate());
            }
            v.ThrowIfInvalid();
        }
    }
}
