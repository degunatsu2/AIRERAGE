using System;

namespace Degunatsu.Core.Combat
{
    /// <summary>
    /// アクション状態の種類（docs/01_game_design.md 6.2。排他的でどれか1つ）。
    /// 今は Free と Attack だけ。ガードや被弾などは、作るときに加える（docs/11_combat_plan.md）。
    /// </summary>
    public enum ActionKind : byte
    {
        Free,
        Attack,
    }

    /// <summary>攻撃の種類（地上／空中 × ニュートラル／横／上／下。docs/01_game_design.md 6.1）。</summary>
    public enum AttackSlot : byte
    {
        GroundNeutral,
        GroundSide,
        GroundUp,
        GroundDown,
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

        public bool IsFree => Kind == ActionKind.Free;

        public bool Equals(ActionState other)
        {
            return Kind == other.Kind && Attack == other.Attack && Ticks == other.Ticks;
        }

        public override bool Equals(object obj)
        {
            return obj is ActionState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Kind, Attack, Ticks);
        }

        public override string ToString()
        {
            return Kind == ActionKind.Attack ? $"{Kind}({Attack}, {Ticks})" : Kind.ToString();
        }
    }
}
