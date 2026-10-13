using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// 攻撃の命中を決めて当てる（docs/11_combat_plan.md T3-2）。SimWorld が全員のティックを進めた後に呼ぶ。
    /// - 攻撃の判定は、持続の間だけ出る軸平行の箱（向きで左右反転。D-37）。やられ判定はキャラクターの箱。
    /// - 先に全員分の命中を集めてから、攻撃した側の番号、当たった側の番号の順に当てる（同じティックの相打ちが順番で変わらない）。
    /// - 同じ攻撃は同じ相手に1回だけ当たる。ヒットストップ中の攻撃は新しく当たらない。
    /// - 当たった側: ワイヤーを外し【仮】、のけぞり（Hitstun）にして、吹き飛ばす（攻撃ごとに固定。D-38 の (a)）。
    ///   最後にダメージを与えた人と時刻、コンボの数とダメージを記録する。HP はない（D-23）。
    /// - 両者にヒットストップを与える。
    /// </summary>
    public sealed class HitResolver
    {
        private readonly List<Hit> _hits = new List<Hit>();

        /// <summary>このティックの命中を決めて当てる。tick は進めた後のティック（記録に使う）。</summary>
        public void Resolve(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, int tick)
        {
            _hits.Clear();
            for (int a = 0; a < states.Count; a++)
            {
                CharacterState attacker = states[a];
                if (attacker.Action.Kind != ActionKind.Attack || attacker.Combat.HitstopTicksLeft > 0)
                {
                    continue;
                }
                AttackData attack = configs[a].Combat.Get(attacker.Action.Attack);
                if (attack.PhaseAt(attacker.Action.Ticks) != AttackPhase.Active)
                {
                    continue;
                }
                Hitbox(attacker, attack, out Vector2 center, out Vector2 half);
                for (int v = 0; v < states.Count; v++)
                {
                    if (v == a || (attacker.Action.HitMask & (1 << v)) != 0)
                    {
                        continue;
                    }
                    Vector3 target = states[v].Position;
                    Vector3 targetHalf = configs[v].Movement.HalfExtents;
                    if (MathF.Abs(target.X - center.X) < half.X + targetHalf.X
                        && MathF.Abs(target.Y - center.Y) < half.Y + targetHalf.Y)
                    {
                        _hits.Add(new Hit(a, v, attack, attacker.Facing));
                    }
                }
            }

            foreach (Hit hit in _hits)
            {
                Apply(states, configs, hit, tick);
            }
        }

        /// <summary>攻撃の判定の箱（中心と半分の大きさ）。表示にも使う。</summary>
        public static void Hitbox(in CharacterState attacker, AttackData attack, out Vector2 center, out Vector2 half)
        {
            center = new Vector2(
                attacker.Position.X + attack.HitboxOffsetX * attacker.Facing,
                attacker.Position.Y + attack.HitboxOffsetY);
            half = new Vector2(attack.HitboxHalfWidth, attack.HitboxHalfHeight);
        }

        // 攻撃のデータと向きは、集めたときのものを使う（相打ちで、先に当てた命中が攻撃した側をのけぞらせていることがある）。
        private static void Apply(List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, Hit hit, int tick)
        {
            AttackData attack = hit.Attack;
            CharacterState attacker = states[hit.Attacker];
            if (attacker.Action.Kind == ActionKind.Attack)
            {
                attacker.Action.HitMask |= (byte)(1 << hit.Victim);
            }
            attacker.Combat.HitstopTicksLeft = Math.Max(attacker.Combat.HitstopTicksLeft, attack.HitstopTicks);
            states[hit.Attacker] = attacker;

            CharacterState victim = states[hit.Victim];
            WireController.Interrupt(ref victim, configs[hit.Victim].Wire);
            bool comboContinues = victim.Action.Kind == ActionKind.Hitstun;
            victim.Action = new ActionState
            {
                Kind = ActionKind.Hitstun,
                Duration = attack.HitstunTicks,
            };
            victim.Velocity = new Vector3(attack.KnockbackX * hit.Facing, attack.KnockbackY, 0f);
            victim.JumpBufferTicksLeft = 0;
            victim.Combat.HitstopTicksLeft = Math.Max(victim.Combat.HitstopTicksLeft, attack.HitstopTicks);
            victim.Combat.HasLastAttacker = true;
            victim.Combat.LastAttacker = (byte)hit.Attacker;
            victim.Combat.LastDamagedTick = tick;
            victim.Combat.ComboCount = comboContinues ? victim.Combat.ComboCount + 1 : 1;
            victim.Combat.ComboDamage = (comboContinues ? victim.Combat.ComboDamage : 0) + attack.Damage;
            states[hit.Victim] = victim;
        }

        private readonly struct Hit
        {
            public readonly int Attacker;
            public readonly int Victim;
            public readonly AttackData Attack;
            public readonly int Facing;

            public Hit(int attacker, int victim, AttackData attack, int facing)
            {
                Attacker = attacker;
                Victim = victim;
                Attack = attack;
                Facing = facing;
            }
        }
    }
}
