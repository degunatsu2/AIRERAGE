using System;
using System.Numerics;

namespace Degunatsu.Core.Wire
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

        /// <summary>接続点（Attached 中）。</summary>
        public Vector3 Anchor;

        /// <summary>ロープの長さ。キャラクターと接続点の距離はこれを超えない。</summary>
        public float RopeLength;

        /// <summary>Cooldown の残りティック。</summary>
        public int CooldownTicksLeft;

        public WireReleaseReason LastReleaseReason;
        public WireMissReason LastMissReason;

        /// <summary>
        /// ワイヤーで引っ張られている状態か（D-18: 接続中フェーズと同じ）。
        /// ワイヤー→攻撃のキャンセル判定は、運動モデルに関係なくこの値だけを見る。
        /// </summary>
        public bool IsPulling => Phase == WirePhase.Attached;

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
                && RopeLength.Equals(other.RopeLength)
                && CooldownTicksLeft == other.CooldownTicksLeft
                && LastReleaseReason == other.LastReleaseReason
                && LastMissReason == other.LastMissReason;
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
            hash.Add(RopeLength);
            hash.Add(CooldownTicksLeft);
            hash.Add(LastReleaseReason);
            hash.Add(LastMissReason);
            return hash.ToHashCode();
        }
    }
}
