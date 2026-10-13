using System;
using Degunatsu.Core.Input;

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

        /// <summary>
        /// 今の連携で、攻撃→攻撃のキャンセル（1回まで。D-17）を使ったか。地上技→空中の攻撃は数えない（D-42）。
        /// ワイヤーで接続したとき、攻撃がキャンセルされずに最後まで終わったとき、のけぞったとき、Free で着地しているときに戻る。
        /// </summary>
        public bool AttackCancelUsed;

        /// <summary>攻撃をワイヤーでキャンセルできるまでの残り（ティック。P-1）。</summary>
        public int WireCancelCooldownTicksLeft;

        /// <summary>攻撃ボタンの先行入力の残り（ティック）。0 でなければ、攻撃を始められるようになったときに出る。</summary>
        public int AttackBufferTicksLeft;

        /// <summary>先行入力した攻撃の、空中での種類（押したときのスティックの向きで決める。地上なら地上技になる）。</summary>
        public AttackSlot BufferedAirAttack;

        /// <summary>先行入力したときの横の向き（-1、0、1。0 なら向きを変えない）。</summary>
        public sbyte BufferedFacing;

        /// <summary>次のガードができるまでの残り（ティック。ガードが終わってから数える。D-21）。</summary>
        public int GuardCooldownTicksLeft;

        /// <summary>ジャストガードに成功した数（表示の「PARRY!」と記録に使う）。</summary>
        public int JustGuardCount;

        /// <summary>ふつうのガードで攻撃を受けた数（表示の「GUARD!」と記録に使う）。</summary>
        public int GuardedCount;

        /// <summary>ヒットストップ中に押したジャンプ。止まりが解けたティックに押したとみなす（先行入力。攻撃は AttackBufferTicksLeft）。</summary>
        public InputButtons HitstopPresses;

        public bool Equals(CombatState other)
        {
            return HitstopTicksLeft == other.HitstopTicksLeft
                && HasLastAttacker == other.HasLastAttacker
                && LastAttacker == other.LastAttacker
                && LastDamagedTick == other.LastDamagedTick
                && ComboCount == other.ComboCount
                && ComboDamage == other.ComboDamage
                && Knockouts == other.Knockouts
                && Falls == other.Falls
                && AttackCancelUsed == other.AttackCancelUsed
                && WireCancelCooldownTicksLeft == other.WireCancelCooldownTicksLeft
                && AttackBufferTicksLeft == other.AttackBufferTicksLeft
                && BufferedAirAttack == other.BufferedAirAttack
                && BufferedFacing == other.BufferedFacing
                && GuardCooldownTicksLeft == other.GuardCooldownTicksLeft
                && JustGuardCount == other.JustGuardCount
                && GuardedCount == other.GuardedCount
                && HitstopPresses == other.HitstopPresses;
        }

        public override bool Equals(object obj)
        {
            return obj is CombatState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                HashCode.Combine(HitstopTicksLeft, HasLastAttacker, LastAttacker, LastDamagedTick, ComboCount, ComboDamage, Knockouts, Falls),
                HashCode.Combine(AttackCancelUsed, WireCancelCooldownTicksLeft, AttackBufferTicksLeft, BufferedAirAttack, BufferedFacing),
                GuardCooldownTicksLeft, JustGuardCount, GuardedCount, HitstopPresses);
        }

        public override string ToString()
        {
            string attacker = HasLastAttacker ? $"P{LastAttacker}@{LastDamagedTick}" : "-";
            return $"hitstop={HitstopTicksLeft} last={attacker} combo={ComboCount}({ComboDamage}) ko={Knockouts} falls={Falls}"
                + $" cancel={(AttackCancelUsed ? "used" : "-")} wireCd={WireCancelCooldownTicksLeft} buf={AttackBufferTicksLeft}({BufferedAirAttack},{BufferedFacing}) guardCd={GuardCooldownTicksLeft} parry={JustGuardCount} guarded={GuardedCount} held={HitstopPresses}";
        }
    }
}
