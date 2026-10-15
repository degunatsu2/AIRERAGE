using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// 攻撃の命中を決めて当てる（docs/11_combat_plan.md T3-2）。SimWorld が全員のティックを進めた後に呼ぶ。
    /// - 攻撃の判定は、それぞれの出ている間だけ出る軸平行の箱（向きで左右反転。D-37）。やられ判定はキャラクターの箱。
    /// - 先に全員分の命中を集めてから、攻撃した側の番号、判定の順、当たった側の番号の順に当てる（同じティックの相打ちが順番で変わらない）。
    /// - 同じ判定は同じ相手に1回だけ当たる（二段技は、それぞれの判定が1回ずつ当たる）。ヒットストップ中の攻撃は新しく当たらない。
    /// - 当たった側: ワイヤーを外し、のけぞり（Hitstun）にして、吹き飛ばす（攻撃ごとに固定。撃破技は打つ直前の速さで倍率。D-38）。
    ///   撃破技の倍率は威力（ダメージ）にもかかる。FatalStrikeSpeed 以上で打った撃破技が当たると「FATAL」で、ヒットストップが
    ///   FatalHitstopMultiplier 倍になり、当たった側の FatalHitCount が増える（表示と効果音。ガードされたら FATAL にならない）。
    ///   最後にダメージを与えた人と時刻、コンボの数とダメージを記録する。HP はない（D-23）。
    ///   コンボは当てた人ごとで、のけぞりが終わるまで続く（別の人が当てると、その人の新しいコンボになる）。
    ///   コンボのダメージは、ダメージ報酬の元として CombatState.RewardDamage にも積む（docs/12 T4-5）。別の人の新しいコンボに
    ///   なったら、前のコンボの分を LastEndedCombos に出す（払うのは CombatRewardRules）。
    /// - 当たった側がガード中なら（T3-5。D-21）:
    ///   - ガードを始めてから JustGuardTicks 以内ならジャストガード: 当たった側は吹き飛ばず、すぐに動ける（Free）。ガードの
    ///     クールタイムが戻る（D-39）。攻撃した側は ParriedStunTicks の間のけぞり（隙）、少しだけ斜め上に弾かれる。
    ///   - それ以外はふつうのガード: 当たった側はガードのまま（ガード硬直の分だけ延びる）、吹き飛びは GuardKnockbackScale 倍。
    ///     のけぞらず、コンボにならない。最後にダメージを与えた人は記録する（削り。撃破の手柄になる）【仮】。
    ///   どちらも、攻撃した側の「当たった」（ActionState.Connected）にはならない（攻撃→攻撃などのキャンセルはできない）。
    ///   ガードの値は、当たった側の調整値を使う。
    /// - 両者にヒットストップを与える。
    /// - ゲージ（T3-6）: 当たれば当てた側が GaugeOnHit・受けた側が GaugeOnDamaged、ふつうのガードなら攻撃した側が GaugeOnGuarded・
    ///   守った側が GaugeOnGuard、ジャストガードなら守った側が GaugeOnJustGuard を得る（それぞれ自分の調整値。上限 MaxGauge）。
    ///   必殺技では、攻撃した側はゲージを得ない（続けて必殺技を出せないように。docs/13 T5-3）【仮】。受けた側・守った側は得る。
    /// </summary>
    public sealed class HitResolver
    {
        /// <summary>命中を決められるキャラクターの数の上限（当てた相手の記録のビット数による）。</summary>
        public const int MaxCharacters = 8;

        private readonly List<HitRecord> _hits = new List<HitRecord>();
        private readonly List<ComboEnd> _endedCombos = new List<ComboEnd>();

        /// <summary>
        /// 直前の Resolve で当たった命中（当てた順）。表示とデバッグ用で、シミュレーションの状態ではない
        /// （巻き戻しでは戻らない。次の Resolve で置き換わる）。
        /// </summary>
        public IReadOnlyList<HitRecord> LastHits => _hits;

        /// <summary>
        /// 直前の Resolve で、別の人の新しいコンボに置き換わって終わったコンボ（終わった順。ダメージ報酬に使う。docs/12 T4-5）。
        /// のけぞりが終わって終わったコンボは、ここには出ない（CombatState.RewardDamage に残る）。
        /// </summary>
        public IReadOnlyList<ComboEnd> LastEndedCombos => _endedCombos;

        /// <summary>LastHits と LastEndedCombos を空にする（命中を決めないティック。試合が終わった後など）。</summary>
        public void Clear()
        {
            _hits.Clear();
            _endedCombos.Clear();
        }

        /// <summary>このティックの命中を決めて当てる。tick は進めた後のティック（記録に使う）。</summary>
        public void Resolve(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick)
        {
            if (states.Count > MaxCharacters)
            {
                throw new InvalidOperationException($"命中を決められるのは {MaxCharacters} 人まで（{states.Count} 人）");
            }
            _hits.Clear();
            _endedCombos.Clear();
            for (int a = 0; a < states.Count; a++)
            {
                CharacterState attacker = states[a];
                if (attacker.Action.Kind != ActionKind.Attack || attacker.Combat.HitstopTicksLeft > 0)
                {
                    continue;
                }
                CombatTuning combat = configs[a].Combat;
                AttackData attack = combat.Get(attacker.Action.Attack);
                bool ko = attacker.Action.Attack == combat.KoAttack;
                float scale = ko ? combat.KoKnockbackScale(attacker.Action.StrikeSpeed) : 1f;
                bool fatal = ko && attacker.Action.StrikeSpeed >= combat.FatalStrikeSpeed;
                for (int h = 0; h < attack.Hits.Length; h++)
                {
                    AttackHit hitData = attack.Hits[h];
                    if (!hitData.IsActiveAt(attacker.Action.Ticks))
                    {
                        continue;
                    }
                    Hitbox(attacker, hitData, out Vector2 center, out Vector2 half);
                    for (int v = 0; v < states.Count; v++)
                    {
                        // 無敵（復帰を待つ間と、復帰した後。docs/12 T4-2）の相手には当たらない。
                        if (v == a || (attacker.Action.HitMask & HitBit(h, v)) != 0 || states[v].Combat.InvincibleTicksLeft > 0)
                        {
                            continue;
                        }
                        Vector3 target = states[v].Position;
                        Vector3 targetHalf = configs[v].Movement.HalfExtents;
                        if (MathF.Abs(target.X - center.X) < half.X + targetHalf.X
                            && MathF.Abs(target.Y - center.Y) < half.Y + targetHalf.Y)
                        {
                            _hits.Add(new HitRecord(a, v, attacker.Action.Attack, h, hitData, attacker.Facing, scale, fatal));
                        }
                    }
                }
            }

            for (int i = 0; i < _hits.Count; i++)
            {
                _hits[i] = Apply(states, configs, _hits[i], tick, _endedCombos);
            }
        }

        /// <summary>判定 hit の箱（中心と半分の大きさ）。表示にも使う。</summary>
        public static void Hitbox(in CharacterState attacker, AttackHit hit, out Vector2 center, out Vector2 half)
        {
            center = new Vector2(
                attacker.Position.X + hit.OffsetX * attacker.Facing,
                attacker.Position.Y + hit.OffsetY);
            half = new Vector2(hit.HalfWidth, hit.HalfHeight);
        }

        /// <summary>判定 hitIndex がキャラクター victim に当たったことを示す、ActionState.HitMask のビット。</summary>
        public static uint HitBit(int hitIndex, int victim)
        {
            return 1u << (hitIndex * MaxCharacters + victim);
        }

        // 判定のデータ、向き、倍率は、集めたときのものを使う（相打ちで、先に当てた命中が攻撃した側をのけぞらせていることがある）。
        // 結果（命中、ガード、ジャストガード）は当てるときに決める（同じティックの先の命中で、当たった側の状態が変わることがある）。
        private static HitRecord Apply(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, HitRecord hit, int tick,
            List<ComboEnd> endedCombos)
        {
            AttackHit data = hit.Data;
            CharacterState victim = states[hit.Victim];
            HitResult result = HitResult.Hit;
            if (victim.Action.Kind == ActionKind.Guard)
            {
                result = victim.Action.Ticks < configs[hit.Victim].Combat.JustGuardTicks ? HitResult.JustGuarded : HitResult.Guarded;
            }

            CombatTuning attackerCombat = configs[hit.Attacker].Combat;
            bool fatal = hit.Fatal && result == HitResult.Hit;
            int hitstop = data.HitstopTicks * (fatal ? attackerCombat.FatalHitstopMultiplier : 1);

            CharacterState attacker = states[hit.Attacker];
            if (attacker.Action.Kind == ActionKind.Attack)
            {
                attacker.Action.HitMask |= HitBit(hit.HitIndex, hit.Victim);
                attacker.Action.Connected |= result == HitResult.Hit;
            }
            attacker.Combat.HitstopTicksLeft = Math.Max(attacker.Combat.HitstopTicksLeft, hitstop);
            if (hit.Attack != AttackSlot.Super)
            {
                AddGauge(ref attacker.Combat, attackerCombat,
                    result == HitResult.Hit ? attackerCombat.GaugeOnHit : result == HitResult.Guarded ? attackerCombat.GaugeOnGuarded : 0);
            }
            states[hit.Attacker] = attacker;

            victim = states[hit.Victim];
            CombatTuning victimCombat = configs[hit.Victim].Combat;
            AddGauge(ref victim.Combat, victimCombat,
                result == HitResult.Hit ? victimCombat.GaugeOnDamaged
                : result == HitResult.Guarded ? victimCombat.GaugeOnGuard : victimCombat.GaugeOnJustGuard);
            victim.Combat.HitstopTicksLeft = Math.Max(victim.Combat.HitstopTicksLeft, hitstop);
            if (fatal)
            {
                victim.Combat.FatalHitCount++;
            }
            bool startsCombo = false;
            switch (result)
            {
                case HitResult.JustGuarded:
                    JustGuard(states, configs[hit.Victim].Combat, ref victim, hit);
                    break;
                case HitResult.Guarded:
                    Guard(configs[hit.Victim].Combat, ref victim, hit, tick);
                    break;
                default:
                    startsCombo = Hit(configs[hit.Victim].Wire, ref victim, hit, tick, endedCombos);
                    break;
            }
            states[hit.Victim] = victim;
            return hit.WithResult(result, startsCombo);
        }

        private static void AddGauge(ref CombatState combat, CombatTuning tuning, int amount)
        {
            combat.Gauge = Math.Min(tuning.MaxGauge, combat.Gauge + amount);
        }

        // 当てる。新しいコンボの最初の命中なら true（被弾ドロップに使う。docs/12 T4-6）。前のコンボの払っていないダメージ報酬の元が
        // 残っていれば、endedCombos に出す（T4-5）。
        private static bool Hit(WireTuning wire, ref CharacterState victim, HitRecord hit, int tick, List<ComboEnd> endedCombos)
        {
            AttackHit data = hit.Data;
            WireController.Interrupt(ref victim, wire);
            // コンボは当てた人ごと。のけぞっている間に同じ人が当てるとつながり、別の人が当てるとその人の新しいコンボになる
            // （コンボが切れるのは、のけぞりが終わったときだけ。ユーザーの指示、2026-10-13）。
            bool comboContinues = victim.Action.Kind == ActionKind.Hitstun && victim.Combat.HasLastAttacker
                && victim.Combat.LastAttacker == hit.Attacker;
            victim.Action = new ActionState
            {
                Kind = ActionKind.Hitstun,
                Duration = data.HitstunTicks,
            };
            victim.Velocity = new Vector3(hit.Knockback.X, hit.Knockback.Y, 0f);
            victim.JumpBufferTicksLeft = 0;
            victim.Combat.HasLastAttacker = true;
            victim.Combat.LastAttacker = (byte)hit.Attacker;
            victim.Combat.LastDamagedTick = tick;
            victim.Combat.ComboCount = comboContinues ? victim.Combat.ComboCount + 1 : 1;
            victim.Combat.ComboDamage = (comboContinues ? victim.Combat.ComboDamage : 0) + hit.Damage;
            bool rewardContinues = comboContinues && victim.Combat.RewardAttacker == hit.Attacker;
            if (!rewardContinues && victim.Combat.RewardDamage > 0)
            {
                endedCombos.Add(new ComboEnd(victim.Combat.RewardAttacker, hit.Victim, victim.Combat.RewardDamage));
            }
            victim.Combat.RewardDamage = (rewardContinues ? victim.Combat.RewardDamage : 0) + hit.Damage;
            victim.Combat.RewardAttacker = (byte)hit.Attacker;
            return !comboContinues;
        }

        // ふつうのガード: ガードのまま（ガード硬直の分だけ延ばす）、弱く吹き飛ぶ。削りとして最後にダメージを与えた人を記録する【仮】。
        private static void Guard(CombatTuning combat, ref CharacterState defender, HitRecord hit, int tick)
        {
            int stunEnd = defender.Action.Ticks + combat.GuardStunTicks + 1;
            defender.Action.Duration = Math.Max(defender.Action.Duration, stunEnd);
            defender.Action.CancelFromTick = Math.Max(defender.Action.CancelFromTick, stunEnd);
            defender.Velocity = new Vector3(hit.Knockback.X, hit.Knockback.Y, 0f) * combat.GuardKnockbackScale;
            defender.Combat.GuardedCount++;
            defender.Combat.HasLastAttacker = true;
            defender.Combat.LastAttacker = (byte)hit.Attacker;
            defender.Combat.LastDamagedTick = tick;
        }

        // ジャストガード: 守った側はすぐに動けて、ガードのクールタイムが戻る（D-39）。攻撃した側は隙（のけぞり）になる。
        private static void JustGuard(List<CharacterState> states, CombatTuning combat, ref CharacterState defender, HitRecord hit)
        {
            defender.Action = default;
            defender.Combat.GuardCooldownTicksLeft = 0;
            defender.Combat.JustGuardCount++;

            CharacterState attacker = states[hit.Attacker];
            attacker.Action = new ActionState
            {
                Kind = ActionKind.Hitstun,
                Duration = combat.ParriedStunTicks,
            };
            // 守った側から離れる向きへ、少しだけ斜め上に弾く（同じ位置なら、向いている方向の逆へ）。
            float away = attacker.Position.X > defender.Position.X ? 1f
                : attacker.Position.X < defender.Position.X ? -1f : -attacker.Facing;
            attacker.Velocity = new Vector3(away * combat.ParriedPushbackX, combat.ParriedPushbackY, 0f);
            attacker.JumpBufferTicksLeft = 0;
            states[hit.Attacker] = attacker;
        }
    }

    /// <summary>終わったコンボ1つ（HitResolver.LastEndedCombos。ダメージ報酬の元。docs/12 T4-5）。</summary>
    public readonly struct ComboEnd
    {
        /// <summary>コンボを当てていた人（ダメージ報酬を受け取る）。</summary>
        public readonly int Attacker;
        public readonly int Victim;

        /// <summary>コンボのダメージの合計。</summary>
        public readonly int Damage;

        public ComboEnd(int attacker, int victim, int damage)
        {
            Attacker = attacker;
            Victim = victim;
            Damage = damage;
        }

        public override string ToString()
        {
            return $"P{Attacker}->P{Victim} {Damage}";
        }
    }

    /// <summary>命中の結果。</summary>
    public enum HitResult : byte
    {
        /// <summary>当たった（のけぞり、吹き飛び、コンボ）。</summary>
        Hit,

        /// <summary>ふつうのガードで受けた。</summary>
        Guarded,

        /// <summary>ジャストガードされた（攻撃した側に隙）。</summary>
        JustGuarded,
    }

    /// <summary>当たった命中1つ（HitResolver.LastHits）。判定のデータ、向き、倍率は、命中を集めたときのもの。</summary>
    public readonly struct HitRecord
    {
        public readonly int Attacker;
        public readonly int Victim;
        public readonly AttackSlot Attack;

        /// <summary>攻撃の何番目の判定か（0 から）。</summary>
        public readonly int HitIndex;

        public readonly AttackHit Data;
        public readonly int Facing;

        /// <summary>吹き飛びと威力の倍率（撃破技は打つ直前の速さによる。それ以外は 1。D-38）。</summary>
        public readonly float KnockbackScale;

        /// <summary>FatalStrikeSpeed 以上で打った撃破技か（当たれば FATAL。ガードされたら FATAL にならない）。</summary>
        public readonly bool Fatal;

        /// <summary>結果（命中、ガード、ジャストガード）。</summary>
        public readonly HitResult Result;

        /// <summary>当たって、当たった側の新しいコンボの最初の命中になったか（被弾ドロップ。docs/12 T4-6）。</summary>
        public readonly bool StartsCombo;

        public HitRecord(int attacker, int victim, AttackSlot attack, int hitIndex, AttackHit data, int facing, float knockbackScale,
            bool fatal = false, HitResult result = HitResult.Hit, bool startsCombo = false)
        {
            Attacker = attacker;
            Victim = victim;
            Attack = attack;
            HitIndex = hitIndex;
            Data = data;
            Facing = facing;
            KnockbackScale = knockbackScale;
            Fatal = fatal;
            Result = result;
            StartsCombo = startsCombo;
        }

        public HitRecord WithResult(HitResult result, bool startsCombo = false)
        {
            return new HitRecord(Attacker, Victim, Attack, HitIndex, Data, Facing, KnockbackScale, Fatal, result, startsCombo);
        }

        /// <summary>当たった側の吹き飛びの速度（m/s、X は右が正）。</summary>
        public Vector2 Knockback => new Vector2(Data.KnockbackX * Facing, Data.KnockbackY) * KnockbackScale;

        /// <summary>威力（ダメージ。撃破技は倍率をかけて四捨五入）。コンボが終わったときに、ダメージ報酬になる（docs/12 T4-5）。</summary>
        public int Damage => (int)MathF.Round(Data.Damage * KnockbackScale);

        /// <summary>FATAL として当たったか。</summary>
        public bool IsFatalHit => Fatal && Result == HitResult.Hit;
    }
}
