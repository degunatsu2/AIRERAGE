using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Wire;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// 攻撃の命中を決めて当てる（docs/11_combat_plan.md T3-2）。SimWorld が全員のティックを進めた後に呼ぶ。
    /// - 攻撃の判定は、それぞれの出ている間だけ出る軸平行の箱（向きで左右反転。D-37）。やられ判定はキャラクターの箱。
    /// - 先に全員分の命中を集めてから、攻撃した側の番号、判定の順、当たった側の番号の順に当てる（同じティックの相打ちが順番で変わらない）。
    /// - 同じ判定は同じ相手に1回だけ当たる（二段技は、それぞれの判定が1回ずつ当たる）。ヒットストップ中の攻撃は新しく当たらない。
    /// - 当たった側: ワイヤーを外し、のけぞり（Hitstun）にして、吹き飛ばす（攻撃ごとに固定。撃破技は打つ直前の速さで倍率。D-38）。
    ///   倍率と上乗せは AttackPower が決める（D-64）。撃破技の倍率は威力（ダメージ）にもかかる。FatalStrikeSpeed 以上で打った撃破技が当たると「FATAL」で、ヒットストップが
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
    /// - ゲージ（T3-6）: 当たれば当てた側が判定のゲージ（AttackHit.Gauge。技ごと。2026-10-22）・受けた側が GaugeOnDamaged、ふつうの
    ///   ガードなら攻撃した側が判定のゲージの GaugeGuardedPercent %・守った側が GaugeOnGuard、ジャストガードなら守った側が
    ///   GaugeOnJustGuard を得る（上限 MaxGauge）。必殺技の判定はゲージが 0（続けて必殺技を出せないように。docs/13 T5-3）【仮】。
    /// - 投射物の命中（docs/13 T5-6）: SkillRules が集めた命中（HitRecord.FromObject）も、キャラクターの攻撃の後に同じ規則で当てる。
    ///   ただし、撃った人は止まらず（ヒットストップなし）、今の攻撃の「当たった」にもならず、ジャストガードされても隙にならない。
    /// - 壊せる物（セーフティボックス）: 攻撃の判定が重なったら、物にダメージを与える（同じ攻撃の同じ判定は 1 回だけ）。攻撃した側は
    ///   キャラクターに当てたときと同じヒットストップで止まる（ユーザーの要望、2026-10-16）。壊れたかどうかは SkillRules が決める。
    /// - ルーレット（AttackData.Roulette）: 吹き飛びと威力の倍率を、試合の種と攻撃の番号（CombatState.AttackSerial）で決める
    ///   （AttackData.RouletteScales のどれか。撃破技の速さの倍率の代わり）。最大なら JACKPOT で、受けた側の JackpotHitCount が増える。
    ///   ためた技の最後の段階（オールインの虹）で当てたときも JACKPOT。
    /// - FEVER（CombatState.CoinBoostTicksLeft）の間に得るゲージは、CoinBoostTuning.GaugeBonusPercent だけ増える（CombatTuning.AddGauge）。
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

        /// <summary>
        /// このティックの命中を決めて当てる。tick は進めた後のティック（記録に使う）。objects は壊せる物を含むワールドの物
        /// （null なら物はない）、seed は試合の種（ルーレット）、extraHits は投射物の命中（null ならない）。
        /// </summary>
        public void Resolve(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick, WorldObjects objects = null,
            int seed = 0, IReadOnlyList<HitRecord> extraHits = null)
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
                // 復帰した後の無干渉の間は、自分の攻撃も相手に当たらない（MatchRules.InvincibleTicks）。
                if (attacker.Action.Kind != ActionKind.Attack || attacker.Combat.HitstopTicksLeft > 0 || attacker.Combat.IsUntouchable)
                {
                    continue;
                }
                CombatTuning combat = configs[a].Combat;
                AttackData attack = combat.Get(attacker.Action.Attack);
                AttackPower power = AttackPower.Of(attacker, a, configs[a], seed);
                // このティックに、判定のどれか 1 つが当たった相手（AttackData.ExclusiveHits）。
                uint exclusive = 0;
                for (int h = 0; h < attack.Hits.Length; h++)
                {
                    AttackHit hitData = attack.Hits[h];
                    if (!hitData.IsActiveAt(attacker.Action.Ticks) || !attack.Uses(hitData.Charge, attacker.Action.Charge))
                    {
                        continue;
                    }
                    Hitbox(attacker, hitData, out Vector2 center, out Vector2 half);
                    float angle = HitAngle(attacker, hitData);
                    if (objects != null && DamageObjects(objects, configs, a, attacker.Combat.AttackSerial, h, center, HitShapes.Bounds(half, angle),
                            AttackPower.Damage(hitData.Damage, power.Multiplier, power.BonusPercent)))
                    {
                        // 壊せる物に当てたら、キャラクターに当てたときと同じように止まる（ユーザーの要望、2026-10-16）。
                        CharacterState stopped = states[a];
                        stopped.Combat.HitstopTicksLeft = Math.Max(stopped.Combat.HitstopTicksLeft, hitData.HitstopTicks);
                        states[a] = stopped;
                    }
                    for (int v = 0; v < states.Count; v++)
                    {
                        // 干渉されない相手（復帰を待つ間と、復帰した後の無干渉。docs/12 T4-2）には当たらない。
                        if (v == a || (attacker.Action.HitMask & HitBit(h, v)) != 0 || states[v].Combat.IsUntouchable)
                        {
                            continue;
                        }
                        if (attack.ExclusiveHits && ((exclusive & (1u << v)) != 0 || HitAny(attacker.Action.HitMask, attack.Hits.Length, v)))
                        {
                            continue;
                        }
                        Vector3 target = states[v].Position;
                        var targetCenter = new Vector2(target.X, target.Y);
                        Vector3 targetHalf = configs[v].Movement.HalfExtents;
                        bool touches = hitData.Critical
                            ? HitShapes.ContainsPoint(center, half, angle, targetCenter, combat.CriticalCenterRadius)
                            : HitShapes.Overlaps(center, half, angle, targetCenter, new Vector2(targetHalf.X, targetHalf.Y));
                        if (touches)
                        {
                            AttackPower hitPower = hitData.Critical ? power.WithCritical(hitData.CriticalScale) : power;
                            if (hitData.LoneFatal && !HitAny(attacker.Action.HitMask, h, v))
                            {
                                // 前の判定をどれも当てずに、この判定だけを当てた（THIEF の空中上の 5 段目）。
                                hitPower = hitPower.WithLoneFatal(hitData.LoneFatalDamage, hitData.LoneFatalKnockbackScale);
                            }
                            _hits.Add(new HitRecord(a, v, attacker.Action.Attack, h, hitData, attacker.Facing, hitPower));
                            exclusive |= 1u << v;
                        }
                    }
                }
            }

            if (extraHits != null)
            {
                foreach (HitRecord extra in extraHits)
                {
                    // 無敵の相手には当たらない（集めた後に復帰や無敵が変わることはないが、念のため当てる直前にも調べる）。
                    if (!states[extra.Victim].Combat.IsUntouchable)
                    {
                        _hits.Add(extra);
                    }
                }
            }

            for (int i = 0; i < _hits.Count; i++)
            {
                _hits[i] = Apply(states, configs, _hits[i], tick, _endedCombos);
            }
        }

        /// <summary>ルーレットの目（AttackData.RouletteScales の番号）。試合の種、攻撃した人、攻撃の番号から決まる（全員で同じ）。</summary>
        public static int RouletteRoll(int seed, int attacker, int attackSerial)
        {
            return StableHash.Range(seed, RouletteSalt + attacker, attackSerial, AttackData.RouletteScales.Length);
        }

        private const int RouletteSalt = 7100;

        /// <summary>壊せる物に、攻撃した人 attacker の攻撃（番号 serial の判定 hit）を当てたときの、物が覚える値（0 ではない）。</summary>
        public static int ObjectHitKey(int attacker, int serial, int hit)
        {
            return (attacker + 1) * 0x1000000 + (serial & 0xFFFFF) * 4 + (hit & 3);
        }

        // 判定の箱が重なった壊せる物（セーフティボックス）にダメージを与える（同じ攻撃の同じ判定は 1 回だけ）。与えたら true。
        private static bool DamageObjects(WorldObjects objects, IReadOnlyList<CharacterConfig> configs, int attacker, int serial, int hit,
            Vector2 center, Vector2 half, int damage)
        {
            int key = ObjectHitKey(attacker, serial, hit);
            bool damaged = false;
            for (int i = 0; i < objects.Count; i++)
            {
                WorldObject item = objects[i];
                if (item.Kind != WorldObjectKind.Safe || item.Ended || item.HasHitKey(key) || item.Owner >= configs.Count)
                {
                    continue;
                }
                SafeTuning safe = configs[item.Owner].Skills.Safe;
                if (MathF.Abs(item.Position.X - center.X) < half.X + safe.HalfWidth
                    && MathF.Abs(item.Position.Y - center.Y) < half.Y + safe.HalfHeight)
                {
                    item.Damage += Math.Max(1, damage);
                    item.AddHitKey(key);
                    objects[i] = item;
                    damaged = true;
                }
            }
            return damaged;
        }

        /// <summary>判定 hit の箱（中心と半分の大きさ）。表示にも使う。</summary>
        public static void Hitbox(in CharacterState attacker, AttackHit hit, out Vector2 center, out Vector2 half)
        {
            center = new Vector2(
                attacker.Position.X + hit.OffsetX * attacker.Facing,
                attacker.Position.Y + hit.OffsetY);
            half = new Vector2(hit.HalfWidth, hit.HalfHeight);
        }

        /// <summary>判定 hit の傾き（ラジアン。左を向いていれば左右を反転する）。</summary>
        public static float HitAngle(in CharacterState attacker, AttackHit hit)
        {
            return hit.AngleDeg * MathF.PI / 180f * (attacker.Facing < 0 ? -1f : 1f);
        }

        // 判定のどれか（count 個）が、キャラクター victim にもう当たったか。
        private static bool HitAny(ulong mask, int count, int victim)
        {
            for (int h = 0; h < count; h++)
            {
                if ((mask & HitBit(h, victim)) != 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>判定 hitIndex がキャラクター victim に当たったことを示す、ActionState.HitMask のビット。</summary>
        public static ulong HitBit(int hitIndex, int victim)
        {
            return 1UL << (hitIndex * MaxCharacters + victim);
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
            if (!hit.FromObject)
            {
                if (attacker.Action.Kind == ActionKind.Attack)
                {
                    attacker.Action.HitMask |= HitBit(hit.HitIndex, hit.Victim);
                    attacker.Action.Connected |= result == HitResult.Hit;
                    attacker.Action.LastHitTicks = attacker.Action.Ticks;
                }
                attacker.Combat.HitstopTicksLeft = Math.Max(attacker.Combat.HitstopTicksLeft, hitstop);
            }
            // 当てた側のゲージは判定ごと（必殺技の判定は 0）。ガードされたら割合だけ、ジャストガードされたら得ない。
            CombatTuning.AddGauge(ref attacker.Combat,
                result == HitResult.Hit ? data.Gauge : result == HitResult.Guarded ? data.Gauge * attackerCombat.GaugeGuardedPercent / 100 : 0,
                configs[hit.Attacker].Skills.CoinBoost);
            states[hit.Attacker] = attacker;

            victim = states[hit.Victim];
            CombatTuning victimCombat = configs[hit.Victim].Combat;
            CombatTuning.AddGauge(ref victim.Combat,
                result == HitResult.Hit ? victimCombat.GaugeOnDamaged
                : result == HitResult.Guarded ? victimCombat.GaugeOnGuard : victimCombat.GaugeOnJustGuard,
                configs[hit.Victim].Skills.CoinBoost);
            victim.Combat.HitstopTicksLeft = Math.Max(victim.Combat.HitstopTicksLeft, hitstop);
            if (fatal)
            {
                victim.Combat.FatalHitCount++;
            }
            if (hit.Jackpot && result == HitResult.Hit)
            {
                victim.Combat.JackpotHitCount++;
            }
            bool startsCombo = false;
            switch (result)
            {
                case HitResult.JustGuarded:
                    JustGuard(states, configs[hit.Victim].Combat, ref victim, hit, punishAttacker: !hit.FromObject);
                    break;
                case HitResult.Guarded:
                    Guard(configs[hit.Victim].Combat, ref victim, hit, tick);
                    break;
                default:
                    startsCombo = Hit(configs[hit.Victim].Wire, configs[hit.Victim].Skills.Inverter.StunGuardTicks, ref victim, hit, tick, endedCombos);
                    break;
            }
            states[hit.Victim] = victim;
            return hit.WithResult(result, startsCombo);
        }

        // 当てる。新しいコンボの最初の命中なら true（被弾ドロップに使う。docs/12 T4-6）。前のコンボの払っていないダメージ報酬の元が
        // 残っていれば、endedCombos に出す（T4-5）。
        private static bool Hit(WireTuning wire, int stunGuardTicks, ref CharacterState victim, HitRecord hit, int tick, List<ComboEnd> endedCombos)
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
            // ためている技（長押し）は、攻撃を受けたらやめる（何も出ない）。
            victim.Combat.Holding = false;
            victim.Combat.HoldTicks = 0;
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
            // 追加効果の感電: 先に感電してその場に止まり、感電が終わってから吹き飛ぶ（のけぞりのまま待つので、コンボと手柄は続く。
            // ユーザーの要望、2026-10-26）。次の攻撃を受けたら、その攻撃のもので置き換わる。
            victim.Combat.HasStunKnockback = false;
            victim.Combat.StunTicksLeft = 0;
            if (data.StunTicks > 0 && victim.Combat.StunGuardTicks <= 0)
            {
                victim.Action.Duration = data.StunTicks + data.HitstunTicks;
                victim.Combat.StunTicksLeft = data.StunTicks;
                victim.Combat.StunGuardTicks = data.StunTicks + stunGuardTicks;
                victim.Combat.StunCount++;
                victim.Combat.HasStunKnockback = true;
                victim.Combat.StunKnockbackX = hit.Knockback.X;
                victim.Combat.StunKnockbackY = hit.Knockback.Y;
                victim.Velocity = Vector3.Zero;
                victim.WallSide = 0;
            }
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
        private static void JustGuard(List<CharacterState> states, CombatTuning combat, ref CharacterState defender, HitRecord hit,
            bool punishAttacker)
        {
            defender.Action = default;
            defender.Combat.GuardCooldownTicksLeft = 0;
            defender.Combat.JustGuardCount++;
            if (!punishAttacker)
            {
                // 投射物をジャストガードしても、撃った人（離れている）は隙にならない。
                return;
            }

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

        /// <summary>強さ（倍率・上乗せと内訳。集めたときのもの。AttackPower。D-64）。</summary>
        public readonly AttackPower Power;

        /// <summary>結果（命中、ガード、ジャストガード）。</summary>
        public readonly HitResult Result;

        /// <summary>当たって、当たった側の新しいコンボの最初の命中になったか（被弾ドロップ。docs/12 T4-6）。</summary>
        public readonly bool StartsCombo;

        /// <summary>投射物の命中か（撃った人は止まらず、今の攻撃にも影響しない。docs/13 T5-6）。</summary>
        public readonly bool FromObject;

        public HitRecord(int attacker, int victim, AttackSlot attack, int hitIndex, AttackHit data, int facing, AttackPower power,
            HitResult result = HitResult.Hit, bool startsCombo = false, bool fromObject = false)
        {
            Attacker = attacker;
            Victim = victim;
            Attack = attack;
            HitIndex = hitIndex;
            Data = data;
            Facing = facing;
            Power = power;
            Result = result;
            StartsCombo = startsCombo;
            FromObject = fromObject;
        }

        public HitRecord WithResult(HitResult result, bool startsCombo = false)
        {
            return new HitRecord(Attacker, Victim, Attack, HitIndex, Data, Facing, Power, result, startsCombo, FromObject);
        }

        /// <summary>倍率（吹き飛びと威力。撃破技は打つ直前の速さ、ルーレットの技は目による。それ以外は 1。D-38）。</summary>
        public float Multiplier => Power.Multiplier;

        /// <summary>威力の上乗せ（%）。</summary>
        public int BonusPercent => Power.BonusPercent;

        /// <summary>吹き飛びの上乗せ（%）。</summary>
        public int KnockbackBonusPercent => Power.KnockbackBonusPercent;

        /// <summary>FatalStrikeSpeed 以上で打った撃破技か（当たれば FATAL。ガードされたら FATAL にならない）。</summary>
        public bool Fatal => Power.Fatal;

        /// <summary>JACKPOT の攻撃か（ルーレットの最大か、ためた技の最後の段階。当たれば JACKPOT の演出）。</summary>
        public bool Jackpot => Power.Jackpot;

        /// <summary>当たった側の吹き飛びの速度（m/s、X は右が正。倍率と吹き飛びの上乗せをかける）。</summary>
        public Vector2 Knockback => new Vector2(Data.KnockbackX * Facing, Data.KnockbackY) * Power.KnockbackScale;

        /// <summary>
        /// 威力（ダメージ。倍率と威力の上乗せをかけて四捨五入。AttackPower）。コンボが終わったときに、ダメージ報酬になる（docs/12 T4-5）。
        /// </summary>
        public int Damage => Power.DamageOverride >= 0 ? Power.DamageOverride : AttackPower.Damage(Data.Damage, Power.Multiplier, Power.BonusPercent);

        /// <summary>FATAL として当たったか。</summary>
        public bool IsFatalHit => Fatal && Result == HitResult.Hit;
    }
}
