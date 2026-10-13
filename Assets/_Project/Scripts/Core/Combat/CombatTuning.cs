using System;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 1つの攻撃のデータ（docs/01_game_design.md 6.4）。フレームデータ（発生・持続・硬直）と攻撃中の移動。
    /// 判定の形、ダメージ、キャンセル窓などは、命中を作るときに加える（docs/11_combat_plan.md）。
    /// 速度の X は向いている方向を正とする（左を向いていれば反転する）。
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

        /// <summary>持続の間、速度を DashVelocity にするか（しないなら、それまでの勢いのまま）。</summary>
        public bool DashWhileActive;

        /// <summary>持続の間の速度（m/s）。</summary>
        public float DashVelocityX;
        public float DashVelocityY;

        /// <summary>硬直に入るティックに、速度を LaunchVelocity にするか。地上技は必ず上向きに飛び出す（D-35）。</summary>
        public bool LaunchAtRecovery;

        /// <summary>硬直に入るときの速度（m/s）。</summary>
        public float LaunchVelocityX;
        public float LaunchVelocityY;

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
    /// 地上技1つと、空中のニュートラル／横／上／下の4つ（D-35）。
    /// </summary>
    [Serializable]
    public sealed class CombatTuning
    {
        /// <summary>
        /// 地上技。前へ大きく踏み込み（持続）、硬直に入るときに必ず上へ飛び出して空中に移る（D-35。空中戦がテーマのため、
        /// 地上に居座る利点を与えず、「素早く空中を動くために一度地上に降りる」選択肢にする）。
        /// </summary>
        public AttackData Ground = new AttackData(5, 8, 14)
        {
            DashWhileActive = true,
            DashVelocityX = 16f,
            LaunchAtRecovery = true,
            LaunchVelocityX = 9f,
            LaunchVelocityY = 11f,
        };

        public AttackData AirNeutral = new AttackData(5, 4, 10);
        public AttackData AirSide = new AttackData(7, 4, 14);
        public AttackData AirUp = new AttackData(6, 4, 14);
        public AttackData AirDown = new AttackData(9, 4, 16);

        public AttackData Get(AttackSlot slot)
        {
            switch (slot)
            {
                case AttackSlot.Ground: return Ground;
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
            if (Ground != null && !(Ground.LaunchAtRecovery && Ground.LaunchVelocityY > 0f))
            {
                v.Fail("Ground: 地上技は、硬直に入るときに必ず上向きに飛び出す（LaunchAtRecovery と正の LaunchVelocityY が必要。D-35）");
            }
            return v;
        }
    }
}
