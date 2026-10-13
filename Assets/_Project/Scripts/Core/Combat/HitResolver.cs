using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 攻撃の命中を決めて当てる（docs/11_combat_plan.md T3-2）。SimWorld が全員のティックを進めた後に呼ぶ。
    /// - 攻撃の判定は、それぞれの出ている間だけ出る軸平行の箱（向きで左右反転。D-37）。やられ判定はキャラクターの箱。
    /// - 先に全員分の命中を集めてから、攻撃した側の番号、判定の順、当たった側の番号の順に当てる（同じティックの相打ちが順番で変わらない）。
    /// - 同じ判定は同じ相手に1回だけ当たる（二段技は、それぞれの判定が1回ずつ当たる）。ヒットストップ中の攻撃は新しく当たらない。
    /// - 当たった側: ワイヤーを外し、のけぞり（Hitstun）にして、吹き飛ばす（攻撃ごとに固定。撃破技は打つ直前の速さで倍率。D-38）。
    ///   最後にダメージを与えた人と時刻、コンボの数とダメージを記録する。HP はない（D-23）。
    ///   コンボは当てた人ごとで、のけぞりが終わるまで続く（別の人が当てると、その人の新しいコンボになる）。
    /// - 当たった側がガード中なら（T3-5。D-21）:
    ///   - ガードを始めてから JustGuardTicks 以内ならジャストガード: 当たった側は吹き飛ばず、すぐに動ける（Free）。ガードの
    ///     クールタイムが戻る（D-39）。攻撃した側は ParriedStunTicks の間のけぞる（隙。吹き飛びはない）。
    ///   - それ以外はふつうのガード: 当たった側はガードのまま（ガード硬直の分だけ延びる）、吹き飛びは GuardKnockbackScale 倍。
    ///     のけぞらず、コンボにならない。最後にダメージを与えた人は記録する（削り。撃破の手柄になる）【仮】。
    ///   どちらも、攻撃した側の「当たった」（ActionState.Connected）にはならない（攻撃→攻撃などのキャンセルはできない）。
    ///   ガードの値は、当たった側の調整値を使う。
    /// - 両者にヒットストップを与える。
    /// </summary>
    public sealed class HitResolver
    {
        /// <summary>命中を決められるキャラクターの数の上限（当てた相手の記録のビット数による）。</summary>
        public const int MaxCharacters = 8;

        private readonly List<HitRecord> _hits = new List<HitRecord>();

        /// <summary>
        /// 直前の Resolve で当たった命中（当てた順）。表示とデバッグ用で、シミュレーションの状態ではない
        /// （巻き戻しでは戻らない。次の Resolve で置き換わる）。
        /// </summary>
        public IReadOnlyList<HitRecord> LastHits => _hits;

        /// <summary>このティックの命中を決めて当てる。tick は進めた後のティック（記録に使う）。</summary>
        public void Resolve(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick)
        {
            if (states.Count > MaxCharacters)
            {
                throw new InvalidOperationException($"命中を決められるのは {MaxCharacters} 人まで（{states.Count} 人）");
            }
            _hits.Clear();
            for (int a = 0; a < states.Count; a++)
            {
                CharacterState attacker = states[a];
                if (attacker.Action.Kind != ActionKind.Attack || attacker.Combat.HitstopTicksLeft > 0)
                {
                    continue;
                }
                CombatTuning combat = configs[a].Combat;
                AttackData attack = combat.Get(attacker.Action.Attack);
                float scale = attacker.Action.Attack == combat.KoAttack ? combat.KoKnockbackScale(attacker.Action.StrikeSpeed) : 1f;
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
                        if (v == a || (attacker.Action.HitMask & HitBit(h, v)) != 0)
                        {
                            continue;
                        }
                        Vector3 target = states[v].Position;
                        Vector3 targetHalf = configs[v].Movement.HalfExtents;
                        if (MathF.Abs(target.X - center.X) < half.X + targetHalf.X
                            && MathF.Abs(target.Y - center.Y) < half.Y + targetHalf.Y)
                        {
                            _hits.Add(new HitRecord(a, v, attacker.Action.Attack, h, hitData, attacker.Facing, scale));
                        }
                    }
                }
            }

            for (int i = 0; i < _hits.Count; i++)
            {
                _hits[i] = Apply(states, configs, _hits[i], tick);
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
        private static HitRecord Apply(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, HitRecord hit, int tick)
        {
            AttackHit data = hit.Data;
            CharacterState victim = states[hit.Victim];
            HitResult result = HitResult.Hit;
            if (victim.Action.Kind == ActionKind.Guard)
            {
                result = victim.Action.Ticks < configs[hit.Victim].Combat.JustGuardTicks ? HitResult.JustGuarded : HitResult.Guarded;
            }

            CharacterState attacker = states[hit.Attacker];
            if (attacker.Action.Kind == ActionKind.Attack)
            {
                attacker.Action.HitMask |= HitBit(hit.HitIndex, hit.Victim);
                attacker.Action.Connected |= result == HitResult.Hit;
            }
            attacker.Combat.HitstopTicksLeft = Math.Max(attacker.Combat.HitstopTicksLeft, data.HitstopTicks);
            states[hit.Attacker] = attacker;

            victim = states[hit.Victim];
            victim.Combat.HitstopTicksLeft = Math.Max(victim.Combat.HitstopTicksLeft, data.HitstopTicks);
            switch (result)
            {
                case HitResult.JustGuarded:
                    JustGuard(states, configs[hit.Victim].Combat, ref victim, hit);
                    break;
                case HitResult.Guarded:
                    Guard(configs[hit.Victim].Combat, ref victim, hit, tick);
                    break;
                default:
                    Hit(configs[hit.Victim].Wire, ref victim, hit, tick);
                    break;
            }
            states[hit.Victim] = victim;
            return hit.WithResult(result);
        }

        private static void Hit(WireTuning wire, ref CharacterState victim, HitRecord hit, int tick)
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
            victim.Combat.ComboDamage = (comboContinues ? victim.Combat.ComboDamage : 0) + data.Damage;
        }

        // ふつうのガード: ガードのまま（ガード硬直の分だけ延ばす）、弱く吹き飛ぶ。削りとして最後にダメージを与えた人を記録する【仮】。
        private static void Guard(CombatTuning combat, ref CharacterState defender, HitRecord hit, int tick)
        {
            defender.Action.Duration = Math.Max(defender.Action.Duration, defender.Action.Ticks + combat.GuardStunTicks + 1);
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
            attacker.JumpBufferTicksLeft = 0;
            states[hit.Attacker] = attacker;
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

        /// <summary>吹き飛びの倍率（撃破技は打つ直前の速さによる。それ以外は 1。D-38）。</summary>
        public readonly float KnockbackScale;

        /// <summary>結果（命中、ガード、ジャストガード）。</summary>
        public readonly HitResult Result;

        public HitRecord(int attacker, int victim, AttackSlot attack, int hitIndex, AttackHit data, int facing, float knockbackScale,
            HitResult result = HitResult.Hit)
        {
            Attacker = attacker;
            Victim = victim;
            Attack = attack;
            HitIndex = hitIndex;
            Data = data;
            Facing = facing;
            KnockbackScale = knockbackScale;
            Result = result;
        }

        public HitRecord WithResult(HitResult result)
        {
            return new HitRecord(Attacker, Victim, Attack, HitIndex, Data, Facing, KnockbackScale, result);
        }

        /// <summary>当たった側の吹き飛びの速度（m/s、X は右が正）。</summary>
        public Vector2 Knockback => new Vector2(Data.KnockbackX * Facing, Data.KnockbackY) * KnockbackScale;
    }
}
