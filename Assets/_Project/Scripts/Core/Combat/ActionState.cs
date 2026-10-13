using System;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// アクション状態の種類（docs/01_game_design.md 6.2。排他的でどれか1つ）。
    /// ガードなどは、作るときに加える（docs/11_combat_plan.md）。
    /// </summary>
    public enum ActionKind : byte
    {
        Free,
        Attack,

        /// <summary>のけぞり（攻撃を受けた後、入力を受け付けない）。</summary>
        Hitstun,
    }

    /// <summary>
    /// 攻撃の種類（docs/01_game_design.md 6.1）。地上は方向に関係なく「地上技」1つ、空中はニュートラル／横／上／下（D-35）。
    /// </summary>
    public enum AttackSlot : byte
    {
        Ground,
        AirNeutral,
        AirSide,
        AirUp,
        AirDown,
    }

    /// <summary>攻撃の段階（フレームデータ）。</summary>
    public enum AttackPhase : byte
    {
        /// <summary>発生（判定が出る前）。</summary>
        Startup,

        /// <summary>持続（判定が出ている）。</summary>
        Active,

        /// <summary>硬直（判定が消えた後、動けない）。</summary>
        Recovery,
    }

    /// <summary>キャラクターのアクション状態。値だけで完結させ、巻き戻しと比較ができるようにする。</summary>
    public struct ActionState : IEquatable<ActionState>
    {
        public ActionKind Kind;

        /// <summary>攻撃中の攻撃の種類（Kind が Attack のときだけ意味がある）。</summary>
        public AttackSlot Attack;

        /// <summary>今のアクションを始めてからのティック数（始めたティックが 0）。</summary>
        public int Ticks;

        /// <summary>今のアクションの長さ（ティック）。のけぞりの長さに使う（攻撃の長さは攻撃のデータから決まる）。</summary>
        public int Duration;

        /// <summary>今の攻撃がすでに当たった相手（ビットがキャラクターの番号）。同じ攻撃は同じ相手に1回だけ当たる。</summary>
        public byte HitMask;

        public bool IsFree => Kind == ActionKind.Free;

        public bool Equals(ActionState other)
        {
            return Kind == other.Kind && Attack == other.Attack && Ticks == other.Ticks
                && Duration == other.Duration && HitMask == other.HitMask;
        }

        public override bool Equals(object obj)
        {
            return obj is ActionState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Kind, Attack, Ticks, Duration, HitMask);
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ActionKind.Attack: return $"{Kind}({Attack}, {Ticks}, hit={HitMask})";
                case ActionKind.Hitstun: return $"{Kind}({Ticks}/{Duration})";
                default: return Kind.ToString();
            }
        }
    }
}
