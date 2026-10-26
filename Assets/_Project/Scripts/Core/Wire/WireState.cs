using System;
using System.Numerics;

namespace Airerage.Core.Wire
{
    /// <summary>
    /// ワイヤーのフェーズ（docs/01_game_design.md 5.2）。両タイプ共通。
    /// </summary>
    public enum WirePhase : byte
    {
        /// <summary>射出できる。</summary>
        Ready,

        /// <summary>フックが飛んでいる。攻撃でキャンセルできない。</summary>
        Firing,

        /// <summary>接続中。「ワイヤーで引っ張られている状態」（D-18）。</summary>
        Attached,

        /// <summary>外れたフックを回収中。</summary>
        Retracting,

        /// <summary>再射出までの待ち時間。</summary>
        Cooldown,
    }

    /// <summary>
    /// 接続が解除された理由。デバッグ表示とテストで使う。
    /// </summary>
    public enum WireReleaseReason : byte
    {
        None,
        ButtonReleased,
        ButtonToggled,
        MaxDuration,
        LineBlocked,

        /// <summary>攻撃を受けて外れた（docs/11_combat_plan.md T3-2）。</summary>
        Hit,

        /// <summary>攻撃でキャンセルした（ワイヤー→攻撃。慣性を引き継ぐ。D-19）。</summary>
        Attack,

        /// <summary>ガードした（接続中にガードすると、ワイヤーを解除して慣性を引き継ぐ。【仮】T3-5）。</summary>
        Guard,

        /// <summary>つないでいた置かれた物（ミラーボール）が消えた（docs/14）。</summary>
        AnchorGone,

        /// <summary>ワープポータルで移った（ロープがつながらなくなる。SPACE TRAVELER）。</summary>
        Warped,
    }

    /// <summary>
    /// 外れた理由。
    /// </summary>
    public enum WireMissReason : byte
    {
        None,
        OutOfRange,
        HitNonAttachable,
        ButtonReleased,
        ButtonToggled,

        /// <summary>フックが飛んでいる間に攻撃を受けて、やめた（docs/11_combat_plan.md T3-2）。</summary>
        Interrupted,
    }

    /// <summary>
    /// ワイヤーの状態。ネットワーク同期や巻き戻しのため、値だけで完結させる。
    /// </summary>
    public struct WireState : IEquatable<WireState>
    {
        public WirePhase Phase;

        /// <summary>現在のフェーズに入ってからのティック数。</summary>
        public int PhaseTicks;

        /// <summary>フックの現在位置（Firing 中）。</summary>
        public Vector3 HookPosition;

        /// <summary>フックの進行方向（Firing 中、正規化済み）。</summary>
        public Vector3 HookDirection;

        /// <summary>フックが進んだ距離（Firing 中）。</summary>
        public float HookTraveled;

        /// <summary>エイムアシストで狙っているアンカーポイントがあるか。</summary>
        public bool HasTargetPoint;

        /// <summary>エイムアシストで狙っているアンカーポイント。</summary>
        public Vector3 TargetPoint;

        /// <summary>接続点（Attached 中）。フックが刺さった点。</summary>
        public Vector3 Anchor;

        /// <summary>
        /// 接続点が付いている物（0 なら動かない地形・アンカーポイント。動く足場・扉なら、その番号 + 1。接続点は足場と一緒に動く。
        /// StagePlatforms。ユーザーの要望、2026-10-26）。
        /// </summary>
        public byte AnchorBody;

        /// <summary>ロープが地形の角に巻き付いてできた支点（D-33）。</summary>
        public RopePivots Pivots;

        /// <summary>
        /// 今のロープの長さ（最後の支点、または支点がなければ接続点から、キャラクターまでの長さ）。
        /// キャラクターと今の支点の距離はこれを超えない。
        /// </summary>
        public float RopeLength;

        /// <summary>接続した瞬間の速さ（m/s）。接続直後の補助を使うかの判断に使う。</summary>
        public float AttachSpeed;

        /// <summary>Cooldown の残りティック。</summary>
        public int CooldownTicksLeft;

        public WireReleaseReason LastReleaseReason;
        public WireMissReason LastMissReason;

        /// <summary>
        /// 鞭（SOLDERER のインバータの間に外れたワイヤー。ボタンを押している間は回収せず、照準の方向へ伸ばして振り回す。WireController）。
        /// 回収の段階（Retracting）の間だけ使う。
        /// </summary>
        public bool Whip;

        /// <summary>
        /// ワイヤーで引っ張られている状態か（D-18: 接続中フェーズと同じ）。
        /// ワイヤー→攻撃のキャンセル判定は、運動モデルに関係なくこの値だけを見る。
        /// </summary>
        public bool IsPulling => Phase == WirePhase.Attached;

        /// <summary>今ロープが曲がっている点（最後の支点。支点がなければ接続点）。運動モデルはこの点を中心に動く。</summary>
        public Vector3 ActivePivot => Pivots.Count > 0 ? Pivots[Pivots.Count - 1] : Anchor;

        public bool Equals(WireState other)
        {
            return Phase == other.Phase
                && PhaseTicks == other.PhaseTicks
                && HookPosition.Equals(other.HookPosition)
                && HookDirection.Equals(other.HookDirection)
                && HookTraveled.Equals(other.HookTraveled)
                && HasTargetPoint == other.HasTargetPoint
                && TargetPoint.Equals(other.TargetPoint)
                && Anchor.Equals(other.Anchor)
                && AnchorBody == other.AnchorBody
                && Pivots.Equals(other.Pivots)
                && RopeLength.Equals(other.RopeLength)
                && AttachSpeed.Equals(other.AttachSpeed)
                && CooldownTicksLeft == other.CooldownTicksLeft
                && LastReleaseReason == other.LastReleaseReason
                && LastMissReason == other.LastMissReason && Whip == other.Whip;
        }

        public override bool Equals(object obj)
        {
            return obj is WireState other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Phase);
            hash.Add(PhaseTicks);
            hash.Add(HookPosition);
            hash.Add(HookDirection);
            hash.Add(HookTraveled);
            hash.Add(HasTargetPoint);
            hash.Add(TargetPoint);
            hash.Add(Anchor);
            hash.Add(AnchorBody);
            hash.Add(Pivots);
            hash.Add(RopeLength);
            hash.Add(AttachSpeed);
            hash.Add(CooldownTicksLeft);
            hash.Add(LastReleaseReason);
            hash.Add(LastMissReason);
            hash.Add(Whip);
            return hash.ToHashCode();
        }
    }
}
