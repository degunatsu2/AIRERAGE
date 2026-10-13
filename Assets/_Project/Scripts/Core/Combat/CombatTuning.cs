using System;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 1つの攻撃のデータ（docs/01_game_design.md 6.4）。今はフレームデータ（発生・持続・硬直）だけ。
    /// 判定の形、ダメージ、キャンセル窓などは、命中を作るときに加える（docs/11_combat_plan.md）。
    /// </summary>
    [Serializable]
    public sealed class AttackData
    {
        /// <summary>発生（ティック）。押してから判定が出るまで。</summary>
        public int StartupTicks = 5;

        /// <summary>持続（ティック）。判定が出ている長さ。</summary>
        public int ActiveTicks = 3;

        /// <summary>硬直（ティック）。判定が消えてから動けるようになるまで。</summary>
        public int RecoveryTicks = 10;

        public AttackData()
        {
        }

        public AttackData(int startupTicks, int activeTicks, int recoveryTicks)
        {
            StartupTicks = startupTicks;
            ActiveTicks = activeTicks;
            RecoveryTicks = recoveryTicks;
        }

        /// <summary>攻撃の全体の長さ（ティック）。</summary>
        public int TotalTicks => StartupTicks + ActiveTicks + RecoveryTicks;

        /// <summary>攻撃を始めてから ticks ティック目の段階。</summary>
        public AttackPhase PhaseAt(int ticks)
        {
            if (ticks < StartupTicks)
            {
                return AttackPhase.Startup;
            }
            return ticks < StartupTicks + ActiveTicks ? AttackPhase.Active : AttackPhase.Recovery;
        }

        public TuningValidator Validate(string name)
        {
            var v = new TuningValidator(name);
            v.Positive(nameof(StartupTicks), StartupTicks);
            v.Positive(nameof(ActiveTicks), ActiveTicks);
            v.NonNegative(nameof(RecoveryTicks), RecoveryTicks);
            return v;
        }
    }

    /// <summary>
    /// 戦闘の調整値（攻撃のセット）。初期値はすべて【仮】（docs/11_combat_plan.md）。
    /// 地上／空中 × ニュートラル／横／上／下 の8つの攻撃を持つ。
    /// </summary>
    [Serializable]
    public sealed class CombatTuning
    {
        public AttackData GroundNeutral = new AttackData(5, 3, 10);
        public AttackData GroundSide = new AttackData(8, 4, 16);
        public AttackData GroundUp = new AttackData(7, 4, 16);
        public AttackData GroundDown = new AttackData(6, 3, 14);
        public AttackData AirNeutral = new AttackData(5, 4, 10);
        public AttackData AirSide = new AttackData(7, 4, 14);
        public AttackData AirUp = new AttackData(6, 4, 14);
        public AttackData AirDown = new AttackData(9, 4, 16);

        public AttackData Get(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.GroundNeutral: return GroundNeutral;
                case AttackSlot.GroundSide: return GroundSide;
                case AttackSlot.GroundUp: return GroundUp;
                case AttackSlot.GroundDown: return GroundDown;
                case AttackSlot.AirNeutral: return AirNeutral;
                case AttackSlot.AirSide: return AirSide;
                case AttackSlot.AirUp: return AirUp;
                case AttackSlot.AirDown: return AirDown;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "不明な攻撃の種類");
            }
        }

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(CombatTuning));
            foreach (AttackSlot slot in (AttackSlot[])Enum.GetValues(typeof(AttackSlot)))
            {
                AttackData attack = Get(slot);
                v.NotNull(slot.ToString(), attack);
                if (attack != null)
                {
                    v.Merge(attack.Validate($"{nameof(CombatTuning)}.{slot}"));
                }
            }
            return v;
        }
    }
}
