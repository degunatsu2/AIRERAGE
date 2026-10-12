using System;
using Degunatsu.Core.Common;

namespace Degunatsu.Core.Wire
{
    /// <summary>接続中の運動モデル（docs/01_game_design.md 5.3）。</summary>
    public enum WireModelType : byte
    {
        /// <summary>A. 物理・慣性重視。</summary>
        Inertial,

        /// <summary>B. 操作追従重視。</summary>
        Responsive,

        /// <summary>
        /// A の試行版（D-11）。接続した瞬間に向いている方向へ、接続点を中心にロープ長を半径とする円を描いて回る。
        /// </summary>
        Orbit,
    }

    /// <summary>ワイヤーボタンの操作方式（D-07）。</summary>
    public enum WireButtonMode : byte
    {
        /// <summary>押している間だけ接続を保つ。</summary>
        Hold,

        /// <summary>もう一度押すと解除する。</summary>
        Toggle,
    }

    /// <summary>
    /// ワイヤーの調整値。初期値はすべて【仮】（docs/06_prototype_plan.md 4.2〜4.5）。
    /// 共通の値（射程、上限など）と、モデルごとの値を分けて持つ。
    /// </summary>
    [Serializable]
    public sealed class WireTuning
    {
        public WireModelType ModelType = WireModelType.Inertial;
        public WireButtonMode ButtonMode = WireButtonMode.Hold;

        /// <summary>最大長（m）。</summary>
        public float MaxLength = 12f;

        /// <summary>フックの弾速（m/s）。</summary>
        public float FireSpeed = 60f;

        /// <summary>外れたフックの回収時間（ティック）。</summary>
        public int MissRetractTicks = 10;

        /// <summary>外れたフックの回収後のクールダウン（ティック）。</summary>
        public int CooldownTicks = 20;

        /// <summary>
        /// 接続を解除した後のクールダウン（ティック）。0 なら解除した直後から射出できる
        /// （ユーザーの指示: 原則として解除後にクールタイムを生じさせない。2026-10-12）。
        /// </summary>
        public int ReleaseCooldownTicks = 0;

        /// <summary>
        /// 接続を解除したときに空中ジャンプの回数を回復するか（ユーザーの指示: 機動力を上げるため。2026-10-12）。
        /// 外れた場合（接続しなかった場合）は回復しない。
        /// </summary>
        public bool RestoreAirJumpsOnRelease = true;

        /// <summary>
        /// 接続中のスティックの入力が速さに与える影響（0〜1、0 なら影響なし）。【試行】
        /// 進む方向と同じ向きに入力すると速さが最大で (1 + この値) 倍に、逆向きなら (1 - この値) 倍になる。
        /// 進む方向はモデルが決める（B: 接続点へ向かう方向、A の試行版: 円の接線方向）。
        /// </summary>
        public float AttachedInputInfluence = 0.25f;

        /// <summary>エイムアシストの角度（度、照準方向から左右それぞれ）。</summary>
        public float AimAssistAngleDeg = 12f;

        /// <summary>スティックの入力がないときの照準角（度、向いている方向から上向きに）。</summary>
        public float NeutralAimAngleDeg = 45f;

        /// <summary>照準でスティックの入力を無視する範囲（0〜1）。</summary>
        public float AimDeadzone = 0.3f;

        /// <summary>ロープの最小の長さ（m）。</summary>
        public float MinRopeLength = 1f;

        /// <summary>最大接続時間（ティック）。</summary>
        public int MaxAttachTicks = 180;

        /// <summary>接続中の最高速（m/s）。両タイプ共通の上限。</summary>
        public float MaxAttachedSpeed = 22f;

        /// <summary>解除時の速度の上限（m/s）。両タイプ共通の上限。</summary>
        public float ExitSpeedCap = 18f;

        public InertialWireTuning Inertial = new InertialWireTuning();
        public ResponsiveWireTuning Responsive = new ResponsiveWireTuning();
        public OrbitWireTuning Orbit = new OrbitWireTuning();

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(WireTuning));
            v.Positive(nameof(MaxLength), MaxLength);
            v.Positive(nameof(FireSpeed), FireSpeed);
            v.NonNegative(nameof(MissRetractTicks), MissRetractTicks);
            v.NonNegative(nameof(CooldownTicks), CooldownTicks);
            v.NonNegative(nameof(ReleaseCooldownTicks), ReleaseCooldownTicks);
            v.InRange(nameof(AimAssistAngleDeg), AimAssistAngleDeg, 0f, 90f);
            v.InRange(nameof(NeutralAimAngleDeg), NeutralAimAngleDeg, -90f, 90f);
            v.InRange(nameof(AimDeadzone), AimDeadzone, 0f, 0.95f);
            v.Positive(nameof(MinRopeLength), MinRopeLength);
            v.AtMost(nameof(MinRopeLength), MinRopeLength, nameof(MaxLength), MaxLength);
            v.Positive(nameof(MaxAttachTicks), MaxAttachTicks);
            v.Positive(nameof(MaxAttachedSpeed), MaxAttachedSpeed);
            v.Positive(nameof(ExitSpeedCap), ExitSpeedCap);
            v.InRange(nameof(AttachedInputInfluence), AttachedInputInfluence, 0f, 1f);
            v.NotNull(nameof(Inertial), Inertial);
            v.NotNull(nameof(Responsive), Responsive);
            v.NotNull(nameof(Orbit), Orbit);
            if (Inertial != null)
            {
                v.Merge(Inertial.Validate());
            }
            if (Responsive != null)
            {
                v.Merge(Responsive.Validate());
            }
            if (Orbit != null)
            {
                v.Merge(Orbit.Validate());
            }
            return v;
        }
    }

    /// <summary>タイプA（物理・慣性重視）の調整値。</summary>
    [Serializable]
    public sealed class InertialWireTuning
    {
        /// <summary>ロープを巻き取る速さ（m/s）。縮むほど角運動量の保存で振りが速くなる。</summary>
        public float ReelSpeed = 8f;

        /// <summary>スティックでロープに垂直な方向へ加える力（m/s²）。</summary>
        public float SwingAcceleration = 10f;

        /// <summary>減衰（1/s）。0 ならエネルギーが保存される。</summary>
        public float Damping = 0f;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(InertialWireTuning));
            v.NonNegative(nameof(ReelSpeed), ReelSpeed);
            v.NonNegative(nameof(SwingAcceleration), SwingAcceleration);
            v.NonNegative(nameof(Damping), Damping);
            return v;
        }
    }

    /// <summary>タイプB（操作追従重視）の調整値。</summary>
    [Serializable]
    public sealed class ResponsiveWireTuning
    {
        /// <summary>接続点へ向かう目標速度（m/s）。</summary>
        public float PullSpeed = 16f;

        /// <summary>目標速度への加速度（m/s²）。</summary>
        public float PullAcceleration = 120f;

        /// <summary>スティックで牽引方向に垂直にずらせる速度の上限（m/s）。</summary>
        public float SteerSpeed = 6f;

        /// <summary>牽引中の重力の倍率。</summary>
        public float GravityScale = 0.2f;

        /// <summary>接続点に到着したとみなす距離（m）。到着後はその場で静止してぶら下がる。</summary>
        public float ArrivalRadius = 1f;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(ResponsiveWireTuning));
            v.Positive(nameof(PullSpeed), PullSpeed);
            v.Positive(nameof(PullAcceleration), PullAcceleration);
            v.NonNegative(nameof(SteerSpeed), SteerSpeed);
            v.NonNegative(nameof(GravityScale), GravityScale);
            v.NonNegative(nameof(ArrivalRadius), ArrivalRadius);
            return v;
        }
    }

    /// <summary>タイプA の試行版（円運動）の調整値。</summary>
    [Serializable]
    public sealed class OrbitWireTuning
    {
        /// <summary>円を回る速さ（m/s）。</summary>
        public float OrbitSpeed = 14f;

        /// <summary>回る速さへの加速度（m/s²）。接続した瞬間の速度から、この加速度で回る速度に近づく。</summary>
        public float OrbitAcceleration = 90f;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(OrbitWireTuning));
            v.Positive(nameof(OrbitSpeed), OrbitSpeed);
            v.Positive(nameof(OrbitAcceleration), OrbitAcceleration);
            return v;
        }
    }
}
