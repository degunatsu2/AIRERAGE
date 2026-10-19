using System;
using Airerage.Core.Input;

namespace Airerage.Core.Combat
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

        /// <summary>
        /// 先行入力した攻撃の種類。通常攻撃なら空中での種類（押したときのスティックの向きで決める。地上なら地上技になる）、
        /// 特殊攻撃と必殺技ならその技（地上でも空中でも同じ）。
        /// </summary>
        public AttackSlot BufferedAttack;

        /// <summary>先行入力したときの横の向き（-1、0、1。0 なら向きを変えない）。</summary>
        public sbyte BufferedFacing;

        /// <summary>
        /// ゲージ（0〜CombatTuning.MaxGauge。docs/01_game_design.md 6.7、docs/11_combat_plan.md T3-6）。攻撃の命中・被弾・ガード・
        /// ジャストガードで増える。撃破されて戻っても引き継ぐ【仮】。必殺技を出すと CombatTuning.SuperGaugeCost だけ減る（D-55）。
        /// </summary>
        public int Gauge;

        /// <summary>
        /// 特殊攻撃 1 をもう一度使えるまでの残り（ティック。使い始めたときから数える。D-55）。撃破されると 0 に戻る【仮】。
        /// 負（CooldownWhileActive）なら、技の効果が続いている間で、効果が終わってから数え始める（セーフティボックスなど）。
        /// </summary>
        public int Special1CooldownTicksLeft;

        /// <summary>特殊攻撃 2 をもう一度使えるまでの残り（ティック）。</summary>
        public int Special2CooldownTicksLeft;

        /// <summary>次のガードができるまでの残り（ティック。ガードが終わってから数える。D-21）。</summary>
        public int GuardCooldownTicksLeft;

        /// <summary>のけぞり中に地形で跳ね返った数（表示と効果音に使う）。</summary>
        public int BounceCount;

        /// <summary>FATAL（速く打った撃破技）を受けた数（表示の「FATAL!」と効果音に使う）。</summary>
        public int FatalHitCount;

        /// <summary>ジャストガードに成功した数（表示の「PARRY!」と記録に使う）。</summary>
        public int JustGuardCount;

        /// <summary>ふつうのガードで攻撃を受けた数（表示の「GUARD!」と記録に使う）。</summary>
        public int GuardedCount;

        /// <summary>ヒットストップ中に押したジャンプ。止まりが解けたティックに押したとみなす（先行入力。攻撃は AttackBufferTicksLeft）。</summary>
        public InputButtons HitstopPresses;

        /// <summary>撃破されてから復帰するまでの残り（ティック。docs/12 T4-2）。0 より大きい間は出現位置で待ち、動かず、攻撃も受けない。</summary>
        public int RespawnTicksLeft;

        /// <summary>無敵の残り（ティック。攻撃を受けない）。復帰を待つ間と、復帰した後の MatchRules.InvincibleTicks の間。</summary>
        public int InvincibleTicksLeft;

        /// <summary>
        /// 今受けているコンボの、まだ払っていないダメージ（ダメージ報酬の元。docs/12 T4-5）。コンボが終わったとき（のけぞりが
        /// 終わる、別の人の新しいコンボになる、撃破される）に、RewardAttacker へダメージ報酬を払って 0 にする（CombatRewardRules）。
        /// </summary>
        public int RewardDamage;

        /// <summary>RewardDamage を受け取るキャラクター（そのコンボを当てている人）の番号。</summary>
        public byte RewardAttacker;

        /// <summary>技のボタンを押して、ためているか（長押しの技。AttackData.HoldMaxTicks。動ける）。</summary>
        public bool Holding;

        /// <summary>ためている技の枠と、ためた長さ（ティック）。</summary>
        public AttackSlot HoldSlot;
        public int HoldTicks;

        /// <summary>攻撃を始めた数（攻撃 1 回ごとの番号。壊せる物に同じ攻撃が何度も当たらないように、ルーレットの種に使う）。</summary>
        public int AttackSerial;

        /// <summary>コインの効率が上がっている残り（ティック。フィーバータイム）。</summary>
        public int CoinBoostTicksLeft;

        /// <summary>コインの効率で足す、まだ 1 枚にならない分（1/1000 枚単位）。1 枚になったらティックの終わりに足す。</summary>
        public int CoinBonusMilli;

        /// <summary>ルーレットの最大（JACKPOT）の攻撃を受けた数（表示の「JACKPOT!」に使う）。</summary>
        public int JackpotHitCount;

        /// <summary>FEVER の間に得たゲージの上乗せの端数（ゲージの 1/100。CombatTuning.AddGauge）。</summary>
        public int GaugeBonusRemainder;

        /// <summary>行動の速さの強化の残り（ティック。STORM RUNNER のウインドクローク。SpeedBoosts）。</summary>
        public int SpeedBoostTicksLeft;

        /// <summary>空の強化の残り（ティック。速さとワイヤー。STORM RUNNER のキャッスルインジエアー。SpeedBoosts）。</summary>
        public int SkyBoostTicksLeft;

        /// <summary>特殊攻撃のうち、まだ戻っていない回数（使える回数が 2 以上の技。CombatTuning.Special1MaxCharges）。</summary>
        public int Special1Spent;
        public int Special2Spent;

        /// <summary>
        /// 技の照準（技のボタン Q・E・R を押している間の、最後のティックの照準。ActionController）。技はこの照準で出す（ためている間の
        /// ミサイルの狙いの表示にも使う）。オンラインでは、照準を送るのは技のボタンを押している間とワイヤーを押したティックだけ。
        /// </summary>
        public System.Numerics.Vector2 SkillAim;

        /// <summary>ゆっくり落ちている間は、それを始めた攻撃の番号 + 1（0 なら落ちていない。SPACE TRAVELER の地上技の後）。</summary>
        public int SlowFallSerial;

        /// <summary>置いているワープポータルの数（SkillRules がティックの終わりに数える。コズミックレイの条件）。</summary>
        public int PortalCount;

        /// <summary>今いるワープポータルの番号（外から入ったときだけ移る。0 はなし）と、移った後にまた移れるまで（ティック）。</summary>
        public int InPortalId;
        public int WarpCooldownTicks;

        /// <summary>ワープポータルで移った数（表示と効果音に使う）。</summary>
        public int WarpCount;

        /// <summary>凍って動けない残り（ティック。吹雪。StageZones）と、凍った数（表示と効果音に使う）。</summary>
        public int FrozenTicksLeft;
        public int FreezeCount;

        /// <summary>ステージの危険（溶岩流、つらら）にもう当たらない残り（ティック。同じ噴き出しに 1 回だけ当たる）と、当たった数（表示と効果音）。</summary>
        public int HazardImmunityTicks;
        public int HazardHitCount;

        /// <summary>凍っているか。</summary>
        public bool IsFrozen => FrozenTicksLeft > 0;

        /// <summary>クールタイムを、技の効果が終わるまで止めておく印（Special1CooldownTicksLeft などの値）。</summary>
        public const int CooldownWhileActive = -1;

        /// <summary>撃破されて、復帰を待っているか。</summary>
        public bool IsWaitingToRespawn => RespawnTicksLeft > 0;

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
                && BufferedAttack == other.BufferedAttack
                && BufferedFacing == other.BufferedFacing
                && Gauge == other.Gauge
                && Special1CooldownTicksLeft == other.Special1CooldownTicksLeft
                && Special2CooldownTicksLeft == other.Special2CooldownTicksLeft
                && GuardCooldownTicksLeft == other.GuardCooldownTicksLeft
                && BounceCount == other.BounceCount
                && FatalHitCount == other.FatalHitCount
                && JustGuardCount == other.JustGuardCount
                && GuardedCount == other.GuardedCount
                && HitstopPresses == other.HitstopPresses
                && RespawnTicksLeft == other.RespawnTicksLeft
                && InvincibleTicksLeft == other.InvincibleTicksLeft
                && RewardDamage == other.RewardDamage
                && RewardAttacker == other.RewardAttacker
                && Holding == other.Holding
                && HoldSlot == other.HoldSlot
                && HoldTicks == other.HoldTicks
                && AttackSerial == other.AttackSerial
                && CoinBoostTicksLeft == other.CoinBoostTicksLeft
                && CoinBonusMilli == other.CoinBonusMilli
                && JackpotHitCount == other.JackpotHitCount
                && GaugeBonusRemainder == other.GaugeBonusRemainder
                && SpeedBoostTicksLeft == other.SpeedBoostTicksLeft && SkyBoostTicksLeft == other.SkyBoostTicksLeft
                && Special1Spent == other.Special1Spent && Special2Spent == other.Special2Spent && SkillAim.Equals(other.SkillAim)
                && SlowFallSerial == other.SlowFallSerial && PortalCount == other.PortalCount && InPortalId == other.InPortalId
                && WarpCooldownTicks == other.WarpCooldownTicks && WarpCount == other.WarpCount
                && FrozenTicksLeft == other.FrozenTicksLeft && FreezeCount == other.FreezeCount
                && HazardImmunityTicks == other.HazardImmunityTicks && HazardHitCount == other.HazardHitCount;
        }

        public override bool Equals(object obj)
        {
            return obj is CombatState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                HashCode.Combine(HitstopTicksLeft, HasLastAttacker, LastAttacker, LastDamagedTick, ComboCount, ComboDamage, Knockouts, Falls),
                HashCode.Combine(AttackCancelUsed, WireCancelCooldownTicksLeft, AttackBufferTicksLeft, BufferedAttack, BufferedFacing,
                    Special1CooldownTicksLeft, Special2CooldownTicksLeft),
                HashCode.Combine(Gauge, GuardCooldownTicksLeft, BounceCount, FatalHitCount, JustGuardCount, GuardedCount, HitstopPresses),
                HashCode.Combine(RespawnTicksLeft, InvincibleTicksLeft, RewardDamage, RewardAttacker),
                HashCode.Combine(Holding, HoldSlot, HoldTicks, AttackSerial, CoinBoostTicksLeft, CoinBonusMilli, JackpotHitCount,
                    GaugeBonusRemainder),
                HashCode.Combine(SpeedBoostTicksLeft, SkyBoostTicksLeft, Special1Spent, Special2Spent, SkillAim, SlowFallSerial, PortalCount,
                    HashCode.Combine(InPortalId, WarpCooldownTicks, WarpCount, FrozenTicksLeft, FreezeCount, HazardImmunityTicks, HazardHitCount)));
        }

        public override string ToString()
        {
            string attacker = HasLastAttacker ? $"P{LastAttacker}@{LastDamagedTick}" : "-";
            return $"hitstop={HitstopTicksLeft} last={attacker} combo={ComboCount}({ComboDamage}) ko={Knockouts} falls={Falls}"
                + $" cancel={(AttackCancelUsed ? "used" : "-")} wireCd={WireCancelCooldownTicksLeft} buf={AttackBufferTicksLeft}({BufferedAttack},{BufferedFacing}) gauge={Gauge} sp={Special1CooldownTicksLeft}/{Special2CooldownTicksLeft} guardCd={GuardCooldownTicksLeft} bounce={BounceCount} fatal={FatalHitCount} parry={JustGuardCount} guarded={GuardedCount} held={HitstopPresses} respawn={RespawnTicksLeft} inv={InvincibleTicksLeft} reward={RewardDamage}->P{RewardAttacker} hold={(Holding ? HoldSlot + ":" + HoldTicks : "-")} serial={AttackSerial} boost={CoinBoostTicksLeft} bonus={CoinBonusMilli} jackpot={JackpotHitCount} gaugeBonus={GaugeBonusRemainder} speed={SpeedBoostTicksLeft} sky={SkyBoostTicksLeft} spent={Special1Spent}/{Special2Spent} slowFall={SlowFallSerial} portals={PortalCount} inPortal={InPortalId} warpCd={WarpCooldownTicks} frozen={FrozenTicksLeft}({FreezeCount}) hazard={HazardImmunityTicks}({HazardHitCount})";
        }
    }
}
