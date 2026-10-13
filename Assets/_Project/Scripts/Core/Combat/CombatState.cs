using System;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 戦闘のための、アクション状態とは別に続く値（docs/11_combat_plan.md T3-2）。値だけで完結させる。
    /// HP はない（D-23）。ダメージはコインへの変換（Phase 4）と、コンボの表示に使う。
    /// </summary>
    public struct CombatState : IEquatable<CombatState>
    {
        /// <summary>ヒットストップの残り（ティック）。0 でなければ、このキャラクターは止まっている。</summary>
        public int HitstopTicksLeft;

        /// <summary>最後にダメージを与えたキャラクターがいるか。撃破の手柄に使う（D-23、D-40）。</summary>
        public bool HasLastAttacker;

        /// <summary>最後にダメージを与えたキャラクターの番号。</summary>
        public byte LastAttacker;

        /// <summary>最後にダメージを受けたティック。</summary>
        public int LastDamagedTick;

        /// <summary>今受けているコンボの、つながった攻撃の数（のけぞりが切れると次の命中で 1 からになる）。</summary>
        public int ComboCount;

        /// <summary>今受けているコンボのダメージの合計。</summary>
        public int ComboDamage;

        public bool Equals(CombatState other)
        {
            return HitstopTicksLeft == other.HitstopTicksLeft
                && HasLastAttacker == other.HasLastAttacker
                && LastAttacker == other.LastAttacker
                && LastDamagedTick == other.LastDamagedTick
                && ComboCount == other.ComboCount
                && ComboDamage == other.ComboDamage;
        }

        public override bool Equals(object obj)
        {
            return obj is CombatState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HitstopTicksLeft, HasLastAttacker, LastAttacker, LastDamagedTick, ComboCount, ComboDamage);
        }

        public override string ToString()
        {
            string attacker = HasLastAttacker ? $"P{LastAttacker}@{LastDamagedTick}" : "-";
            return $"hitstop={HitstopTicksLeft} last={attacker} combo={ComboCount}({ComboDamage})";
        }
    }
}
