using System;

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
        /// 今の攻撃がすでに当たった相手。判定 h（0〜3）がキャラクター v（0〜7）に当たったら、ビット h × 8 + v を立てる
        /// （同じ判定は同じ相手に1回だけ当たる。0 でなければ、この攻撃は何かに当たった）。
        /// </summary>
        public uint HitMask;

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

        public bool IsFree => Kind == ActionKind.Free;

        public bool Equals(ActionState other)
        {
            return Kind == other.Kind && Attack == other.Attack && Ticks == other.Ticks
                && Duration == other.Duration && HitMask == other.HitMask && StrikeSpeed.Equals(other.StrikeSpeed)
                && Connected == other.Connected && CancelFromTick == other.CancelFromTick
                && EffectDone == other.EffectDone && Charge == other.Charge;
        }

        public override bool Equals(object obj)
        {
            return obj is ActionState other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HashCode.Combine(Kind, Attack, Ticks, Duration, HitMask, StrikeSpeed, Connected, CancelFromTick),
                EffectDone, Charge);
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
