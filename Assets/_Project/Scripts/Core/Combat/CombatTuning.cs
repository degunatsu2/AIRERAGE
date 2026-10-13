using System;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 1つの攻撃のデータ（docs/01_game_design.md 6.4）。フレームデータ（発生・持続・硬直）、攻撃中の移動、命中（T3-2）。
    /// キャンセル窓などは、作るときに加える（docs/11_combat_plan.md）。
    /// 位置と速度の X は向いている方向を正とする（左を向いていれば反転する）。
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

        /// <summary>攻撃の判定（持続の間だけ出る軸平行の箱。D-37）の、キャラクターの中心からの位置（m）。</summary>
        public float HitboxOffsetX = 0.9f;
        public float HitboxOffsetY;

        /// <summary>攻撃の判定の半分の大きさ（m）。</summary>
        public float HitboxHalfWidth = 0.6f;
        public float HitboxHalfHeight = 0.6f;

        /// <summary>ダメージ。HP はなく（D-23）、コインへの変換（Phase 4）とコンボの表示に使う。</summary>
        public int Damage = 5;

        /// <summary>ヒットストップ（ティック）。当てた側と当たった側の両方が止まる。</summary>
        public int HitstopTicks = 6;

        /// <summary>のけぞり（ティック）。当たった側が入力を受け付けない。ヒットストップの後から数える。</summary>
        public int HitstunTicks = 20;

        /// <summary>当たった側の吹き飛びの速度（m/s。X は攻撃した側の向いている方向が正）。攻撃ごとに固定（D-38 の (a)）。</summary>
        public float KnockbackX = 8f;
        public float KnockbackY = 6f;

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
            v.Positive(nameof(HitboxHalfWidth), HitboxHalfWidth);
            v.Positive(nameof(HitboxHalfHeight), HitboxHalfHeight);
            v.NonNegative(nameof(Damage), Damage);
            v.NonNegative(nameof(HitstopTicks), HitstopTicks);
            v.NonNegative(nameof(HitstunTicks), HitstunTicks);
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
            HitboxOffsetX = 0.8f, HitboxHalfWidth = 0.7f, HitboxHalfHeight = 0.7f,
            Damage = 6, HitstopTicks = 6, HitstunTicks = 22, KnockbackX = 10f, KnockbackY = 9f,
        };

        /// <summary>空中・ニュートラル: 体の周りを広く。軽く浮かせる（コンボの始動）。</summary>
        public AttackData AirNeutral = new AttackData(5, 4, 10)
        {
            HitboxOffsetX = 0f, HitboxHalfWidth = 1.1f, HitboxHalfHeight = 1.1f,
            Damage = 4, HitstopTicks = 5, HitstunTicks = 20, KnockbackX = 5f, KnockbackY = 7f,
        };

        /// <summary>空中・横: 前へ長く。横へ大きく飛ばす（横からステージの外へ）。</summary>
        public AttackData AirSide = new AttackData(7, 4, 14)
        {
            HitboxOffsetX = 1.1f, HitboxOffsetY = 0.1f, HitboxHalfWidth = 0.8f, HitboxHalfHeight = 0.5f,
            Damage = 6, HitstopTicks = 7, HitstunTicks = 24, KnockbackX = 14f, KnockbackY = 4f,
        };

        /// <summary>空中・上: 頭上。真上へ打ち上げる（追撃の始動）。</summary>
        public AttackData AirUp = new AttackData(6, 4, 14)
        {
            HitboxOffsetX = 0.2f, HitboxOffsetY = 1.1f, HitboxHalfWidth = 0.7f, HitboxHalfHeight = 0.7f,
            Damage = 5, HitstopTicks = 6, HitstunTicks = 22, KnockbackX = 2f, KnockbackY = 14f,
        };

        /// <summary>空中・下: 足元。真下へたたき落とす（下からステージの外へ）。発生が遅い。</summary>
        public AttackData AirDown = new AttackData(9, 4, 16)
        {
            HitboxOffsetX = 0.1f, HitboxOffsetY = -1.1f, HitboxHalfWidth = 0.6f, HitboxHalfHeight = 0.7f,
            Damage = 7, HitstopTicks = 9, HitstunTicks = 26, KnockbackX = 2f, KnockbackY = -16f,
        };

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
