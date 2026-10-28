using System;
using System.Numerics;

namespace Airerage.Core.Combat
{
    /// <summary>
    /// アクション状態の種類（docs/01_game_design.md 6.2。排他的でどれか1つ）。
    /// </summary>
    public enum ActionKind : byte
    {
        Free,
        Attack,

        /// <summary>のけぞり（攻撃を受けた後、入力を受け付けない）。</summary>
        Hitstun,

        /// <summary>ガード（全方向。始めの JustGuardTicks がジャストガード。D-21。docs/11_combat_plan.md T3-5）。</summary>
        Guard,
    }

    /// <summary>
    /// 攻撃の種類（docs/01_game_design.md 6.1）。地上は方向に関係なく「地上技」1つ、空中はニュートラル／横／上／下（D-35）。
    /// 特殊攻撃 2 つ（それぞれのクールタイム）と必殺技（ゲージを消費）は、地上でも空中でも同じ技（D-53、D-55。docs/13 T5-2、T5-3）。
    /// </summary>
    public enum AttackSlot : byte
    {
        Ground,
        AirNeutral,
        AirSide,
        AirUp,
        AirDown,

        /// <summary>特殊攻撃 1（キーボードの Q、ゲームパッドの LB）。</summary>
        Special1,

        /// <summary>特殊攻撃 2（キーボードの E、ゲームパッドの RB）。</summary>
        Special2,

        /// <summary>必殺技（キーボードの R、ゲームパッドの右スティックの押し込み【仮】）。</summary>
        Super,
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

        /// <summary>
        /// 今のアクションの長さ（ティック）。のけぞりとガードの長さに使う（攻撃の長さは攻撃のデータから決まる）。
        /// ガードは、攻撃をガードするとガード硬直の分だけ延びる。
        /// </summary>
        public int Duration;

        /// <summary>
        /// 今の攻撃がすでに当たった相手。判定 h（0〜7）がキャラクター v（0〜7）に当たったら、ビット h × 8 + v を立てる
        /// （同じ判定は同じ相手に1回だけ当たる。0 でなければ、この攻撃は何かに当たった。THIEF の 5 段の刺突のために 64 ビットにした）。
        /// </summary>
        public ulong HitMask;

        /// <summary>撃破技を打つ直前の速さ（m/s）。吹き飛びの倍率に使う（D-38）。</summary>
        public float StrikeSpeed;

        /// <summary>今の攻撃が相手に当たったか（ガードされたものは含まない）。攻撃→攻撃などのキャンセルの条件（D-17、D-42）。</summary>
        public bool Connected;

        /// <summary>
        /// ガードを攻撃・ワイヤーでキャンセルできるようになる Ticks（ユーザーの要望、2026-10-13）。ガードを始めた次のティックから
        /// できるが、攻撃をガードするとガード硬直が終わるまでできない。
        /// </summary>
        public int CancelFromTick;

        /// <summary>技の効果（AttackData.Effect）を起こしたか（ヒットストップで止まっていても 1 回だけ起こす）。</summary>
        public bool EffectDone;

        /// <summary>技を出す前にためた長さ（ティック。長押しの技。AttackData.HoldMaxTicks）。</summary>
        public int Charge;

        /// <summary>攻撃を始めたときの照準の方向（InputFrame.Aim。長さ 1 以下。照準の向きに置く技に使う）。</summary>
        public Vector2 Aim;

        /// <summary>走り続ける地上技（AttackData.Run）の今の速さ（m/s。強化の倍率をかける前）。</summary>
        public float RunSpeed;

        /// <summary>走り続ける地上技で、逆向きに入力してからの反転の残り（ティック。0 なら反転していない）。</summary>
        public int TurnTicksLeft;

        /// <summary>行動の速さの強化（SpeedBoosts）で、攻撃のフレームを進める端数（%）。100 になったら 1 ティック多く進める。</summary>
        public int TickRemainder;

        /// <summary>このティックに攻撃のフレームを進めた数（ふつうは 1。強化で 2 になることがある。飛ばしたフレームの動きに使う）。</summary>
        public int Advance;

        /// <summary>今の攻撃が最後に当たった（ガードを含む）ときの Ticks（走り続ける地上技で、同じ相手にまた当たれるまでを数える）。</summary>
        public int LastHitTicks;

        /// <summary>効果中にもう一度出した技か（ウインドクロークを解いて突風を撃つ。溜めと反動がある。SpeedBoostTuning）。</summary>
        public bool Recast;

        public bool IsFree => Kind == ActionKind.Free;

        public bool Equals(ActionState other)
        {
            return Kind == other.Kind && Attack == other.Attack && Ticks == other.Ticks
                && Duration == other.Duration && HitMask == other.HitMask && StrikeSpeed.Equals(other.StrikeSpeed)
                && Connected == other.Connected && CancelFromTick == other.CancelFromTick
                && EffectDone == other.EffectDone && Charge == other.Charge && Aim.Equals(other.Aim) && RunSpeed.Equals(other.RunSpeed)
                && TurnTicksLeft == other.TurnTicksLeft && TickRemainder == other.TickRemainder && Advance == other.Advance
                && LastHitTicks == other.LastHitTicks && Recast == other.Recast;
        }

        public override bool Equals(object obj)
        {
            return obj is ActionState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HashCode.Combine(Kind, Attack, Ticks, Duration, HitMask, StrikeSpeed, Connected, CancelFromTick),
                EffectDone, Charge, Aim, RunSpeed, TurnTicksLeft, TickRemainder, HashCode.Combine(Advance, LastHitTicks, Recast));
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ActionKind.Attack: return $"{Kind}({Attack}, {Ticks}, hit={HitMask:x}{(Connected ? " connected" : "")})";
                case ActionKind.Hitstun:
                case ActionKind.Guard:
                    return $"{Kind}({Ticks}/{Duration})";
                default: return Kind.ToString();
            }
        }
    }
}
