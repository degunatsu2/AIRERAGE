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
    /// - 両者にヒットストップを与える。
    /// </summary>
    public sealed class HitResolver
    {
        /// <summary>命中を決められるキャラクターの数の上限（当てた相手の記録のビット数による）。</summary>
        public const int MaxCharacters = 8;

        private readonly List<Hit> _hits = new List<Hit>();

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
                            _hits.Add(new Hit(a, v, h, hitData, attacker.Facing, scale));
                        }
                    }
                }
            }

            foreach (Hit hit in _hits)
            {
                Apply(states, configs, hit, tick);
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
        private static void Apply(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, Hit hit, int tick)
        {
            AttackHit data = hit.Data;
            CharacterState attacker = states[hit.Attacker];
            if (attacker.Action.Kind == ActionKind.Attack)
            {
                attacker.Action.HitMask |= HitBit(hit.HitIndex, hit.Victim);
            }
            attacker.Combat.HitstopTicksLeft = Math.Max(attacker.Combat.HitstopTicksLeft, data.HitstopTicks);
            states[hit.Attacker] = attacker;

            CharacterState victim = states[hit.Victim];
            WireController.Interrupt(ref victim, configs[hit.Victim].Wire);
            bool comboContinues = victim.Action.Kind == ActionKind.Hitstun;
            victim.Action = new ActionState
            {
                Kind = ActionKind.Hitstun,
                Duration = data.HitstunTicks,
            };
            victim.Velocity = new Vector3(data.KnockbackX * hit.Facing, data.KnockbackY, 0f) * hit.KnockbackScale;
            victim.JumpBufferTicksLeft = 0;
            victim.Combat.HitstopTicksLeft = Math.Max(victim.Combat.HitstopTicksLeft, data.HitstopTicks);
            victim.Combat.HasLastAttacker = true;
            victim.Combat.LastAttacker = (byte)hit.Attacker;
            victim.Combat.LastDamagedTick = tick;
            victim.Combat.ComboCount = comboContinues ? victim.Combat.ComboCount + 1 : 1;
            victim.Combat.ComboDamage = (comboContinues ? victim.Combat.ComboDamage : 0) + data.Damage;
            states[hit.Victim] = victim;
        }

        private readonly struct Hit
        {
            public readonly int Attacker;
            public readonly int Victim;
            public readonly int HitIndex;
            public readonly AttackHit Data;
            public readonly int Facing;
            public readonly float KnockbackScale;

            public Hit(int attacker, int victim, int hitIndex, AttackHit data, int facing, float knockbackScale)
            {
                Attacker = attacker;
                Victim = victim;
                HitIndex = hitIndex;
                Data = data;
                Facing = facing;
                KnockbackScale = knockbackScale;
            }
        }
    }
}
