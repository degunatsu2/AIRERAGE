using System;
using System.Numerics;
using Airerage.Core.Economy;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Wire;

namespace Airerage.Core.Simulation
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

        /// <summary>
        /// 張り付いている壁の側（-1 = 左の壁、+1 = 右の壁、0 = 張り付いていない。WallJump。ユーザーの要望、2026-10-19）と、
        /// 張り付いてからのティック数。
        /// </summary>
        public int WallSide;
        public int WallClingTicks;

        /// <summary>壁ジャンプをした数（表示と音に使う）。</summary>
        public int WallJumpCount;

        /// <summary>地上のジャンプの発生を待っている残り（ティック。MovementTuning.JumpStartupTicks。0 なら待っていない）。</summary>
        public int JumpSquatTicksLeft;

        /// <summary>コイン（所持と確定。docs/12 T4-3）。増減は CoinLedger だけが行う。</summary>
        public CoinPurse Coins;

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
                && BoostRingCount == other.BoostRingCount
                && WallSide == other.WallSide
                && WallClingTicks == other.WallClingTicks
                && WallJumpCount == other.WallJumpCount
                && JumpSquatTicksLeft == other.JumpSquatTicksLeft
                && Coins.Equals(other.Coins);
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
            hash.Add(WallSide);
            hash.Add(WallClingTicks);
            hash.Add(WallJumpCount);
            hash.Add(JumpSquatTicksLeft);
            hash.Add(Coins);
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return $"pos={Position} vel={Velocity} grounded={Grounded} wire={Wire.Phase} action={Action} {Combat} coins=({Coins})";
        }
    }
}
