using System;
using System.Numerics;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Input;
using Degunatsu.Core.Wire;

namespace Degunatsu.Core.Simulation
{
    /// <summary>
    /// キャラクター1人分のシミュレーション状態。値だけで完結させ、コピー・比較・巻き戻しができるようにする。
    /// </summary>
    public struct CharacterState : IEquatable<CharacterState>
    {
        public Vector3 Position;
        public Vector3 Velocity;

        /// <summary>向いている方向（+1 = 右、-1 = 左）。</summary>
        public int Facing;

        public bool Grounded;
        public int CoyoteTicksLeft;
        public int JumpBufferTicksLeft;
        public int AirJumpsUsed;

        /// <summary>前のティックで押されていたボタン（押した瞬間の判定に使う）。</summary>
        public InputButtons PreviousButtons;

        public WireState Wire;

        /// <summary>アクション状態（Free / 攻撃など。docs/01_game_design.md 6.2）。</summary>
        public ActionState Action;

        /// <summary>ヒットストップ、最後にダメージを与えた人、コンボなど（docs/11_combat_plan.md T3-2）。</summary>
        public CombatState Combat;

        /// <summary>ジャンプ台で打ち上がった数（表示と音に使う。StageZones）。</summary>
        public int JumpPadCount;

        /// <summary>加速リングで加速した数（表示と音に使う。StageZones）。</summary>
        public int BoostRingCount;

        public static CharacterState Spawn(Vector3 position, int facing)
        {
            return new CharacterState
            {
                Position = new Vector3(position.X, position.Y, 0f),
                Facing = facing < 0 ? -1 : 1,
            };
        }

        public bool Equals(CharacterState other)
        {
            return Position.Equals(other.Position)
                && Velocity.Equals(other.Velocity)
                && Facing == other.Facing
                && Grounded == other.Grounded
                && CoyoteTicksLeft == other.CoyoteTicksLeft
                && JumpBufferTicksLeft == other.JumpBufferTicksLeft
                && AirJumpsUsed == other.AirJumpsUsed
                && PreviousButtons == other.PreviousButtons
                && Wire.Equals(other.Wire)
                && Action.Equals(other.Action)
                && Combat.Equals(other.Combat)
                && JumpPadCount == other.JumpPadCount
                && BoostRingCount == other.BoostRingCount;
        }

        public override bool Equals(object obj)
        {
            return obj is CharacterState other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Position);
            hash.Add(Velocity);
            hash.Add(Facing);
            hash.Add(Grounded);
            hash.Add(CoyoteTicksLeft);
            hash.Add(JumpBufferTicksLeft);
            hash.Add(AirJumpsUsed);
            hash.Add(PreviousButtons);
            hash.Add(Wire);
            hash.Add(Action);
            hash.Add(Combat);
            hash.Add(JumpPadCount);
            hash.Add(BoostRingCount);
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return $"pos={Position} vel={Velocity} grounded={Grounded} wire={Wire.Phase} action={Action} {Combat}";
        }
    }
}
