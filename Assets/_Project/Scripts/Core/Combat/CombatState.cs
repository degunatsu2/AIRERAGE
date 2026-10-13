using System;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 戦闘のための、アクション状態とは別に続く値（docs/11_combat_plan.md T3-2、T3-3）。値だけで完結させる。
    /// HP はない（D-23）。ダメージはコインへの変換（Phase 4）と、コンボの表示に使う。
    /// 撃破の数（Knockouts、Falls）だけは、撃破されて出現位置に戻っても引き継ぐ。それ以外は戻ると 0 になる。
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

        /// <summary>撃破した数（撃破された相手に、最後にダメージを与えていた）。確認用の記録で、スコアは Phase 4。</summary>
        public int Knockouts;

        /// <summary>ステージの外に出た（撃破された）数。だれにも攻撃されずに出た場合も数える。</summary>
        public int Falls;

        public bool Equals(CombatState other)
        {
            return HitstopTicksLeft == other.HitstopTicksLeft
                && HasLastAttacker == other.HasLastAttacker
                && LastAttacker == other.LastAttacker
                && LastDamagedTick == other.LastDamagedTick
                && ComboCount == other.ComboCount
                && ComboDamage == other.ComboDamage
                && Knockouts == other.Knockouts
                && Falls == other.Falls;
        }

        public override bool Equals(object obj)
        {
            return obj is CombatState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HitstopTicksLeft, HasLastAttacker, LastAttacker, LastDamagedTick, ComboCount, ComboDamage, Knockouts, Falls);
        }

        public override string ToString()
        {
            string attacker = HasLastAttacker ? $"P{LastAttacker}@{LastDamagedTick}" : "-";
            return $"hitstop={HitstopTicksLeft} last={attacker} combo={ComboCount}({ComboDamage}) ko={Knockouts} falls={Falls}";
        }
    }
}
