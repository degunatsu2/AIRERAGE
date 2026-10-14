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
    }

    /// <summary>接続中のロープの長さの扱い（上下の入力ごとに選ぶ。InertialWireTuning。D-41、D-50）。</summary>
    public enum WireLengthMode : byte
    {
        /// <summary>巻き取って縮める（ReelSpeed。接続直後に遅ければ接続点の方向へ強く引く）。スティックはロープに垂直な方向へ振る。</summary>
        Reel = 0,

        /// <summary>長さを保つ（張力を保ったまま振る）。左右の入力だけで振る（HoldSwingAcceleration）。</summary>
        Hold = 1,

        /// <summary>伸ばしながら振る（ReelOutSpeed。最大長まで）。左右の入力で振る。</summary>
        ReelOut = 2,
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
    /// ワイヤーの調整値。初期値はすべて【仮】（docs/06_prototype_plan.md 4.2〜4.4）。
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
        /// 進む方向はモデルが決める（B: 接続点へ向かう方向）。A はスティックで振りに力を加える（InertialWireTuning.SwingAcceleration）ので使わない。
        /// </summary>
        public float AttachedInputInfluence = 0.25f;

        /// <summary>エイムアシストの角度（度、照準方向から左右それぞれ）。</summary>
        public float AimAssistAngleDeg = 6f;

        /// <summary>スティックの入力がないときの照準角（度、向いている方向から上向きに）。</summary>
        public float NeutralAimAngleDeg = 45f;

        /// <summary>照準でスティックの入力を無視する範囲（0〜1）。</summary>
        public float AimDeadzone = 0.3f;

        /// <summary>ロープの最小の長さ（m）。</summary>
        public float MinRopeLength = 1f;

        /// <summary>
        /// 接続中にロープ（キャラクターと接続点を結ぶ線）が地形に遮られたら解除するか。
        /// false ならロープは地形を通り抜け、接続を保つ（ユーザーのプレイテスト: 壁に引っかかったときに解除されるのが気になる。2026-10-13。D-33）。
        /// </summary>
        public bool ReleaseWhenLineBlocked = false;

        /// <summary>
        /// ロープを地形の角に巻き付けるか（D-33。ユーザーの決定で試す。2026-10-13）。巻き付けられないとき（支点が多すぎるなど）は、
        /// ReleaseWhenLineBlocked に従う（解除するか、ロープが地形を通り抜ける）。
        /// </summary>
        public bool RopeWrapping = true;

        /// <summary>最大接続時間（ティック）。</summary>
        public int MaxAttachTicks = 180;

        /// <summary>接続中の最高速（m/s）。両タイプ共通の上限。</summary>
        public float MaxAttachedSpeed = 22f;

        /// <summary>解除時の速度の上限（m/s）。両タイプ共通の上限。</summary>
        public float ExitSpeedCap = 18f;

        /// <summary>
        /// 接続した瞬間に接続点から遠ざかる向きに動いていたとき、その向きの速さを接続点へ向かう向きに反転させる割合（0〜1）
        /// （ユーザーの要望、2026-10-14。D-50）。1 なら速さを保ったまま反転する（慣性が乗っている方向と逆にフックを刺すと、
        /// 同じ速さで逆へ向かう）。0 なら以前のとおり、ロープが遠ざかる向きの速さを止める。両タイプ共通【仮】。
        /// </summary>
        public float AttachReverseOutward = 1f;

        public InertialWireTuning Inertial = new InertialWireTuning();
        public ResponsiveWireTuning Responsive = new ResponsiveWireTuning();

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
            v.InRange(nameof(AttachReverseOutward), AttachReverseOutward, 0f, 1f);
            v.NotNull(nameof(Inertial), Inertial);
            v.NotNull(nameof(Responsive), Responsive);
            if (Inertial != null)
            {
                v.Merge(Inertial.Validate());
            }
            if (Responsive != null)
            {
                v.Merge(Responsive.Validate());
            }
            return v;
        }
    }

    /// <summary>タイプA（物理・慣性重視）の調整値。</summary>
    [Serializable]
    public sealed class InertialWireTuning
    {
        /// <summary>
        /// 縦の入力がないときのロープの長さの扱い。長さを保つ（張力を保ったまま振る。以前は下を押したときの動き。
        /// ユーザーの要望で標準にした、2026-10-14。D-50）。
        /// </summary>
        public WireLengthMode NeutralMode = WireLengthMode.Hold;

        /// <summary>上（UpInputThreshold 以上）を押しているときの扱い。巻き取る（以前の標準の動き。D-50）。</summary>
        public WireLengthMode UpMode = WireLengthMode.Reel;

        /// <summary>下（DownInputThreshold 以下）を押しているときの扱い。伸ばしながら振る（D-50）。</summary>
        public WireLengthMode DownMode = WireLengthMode.ReelOut;

        /// <summary>ロープを巻き取る速さ（m/s。Reel）。縮むほど角運動量の保存で振りが速くなる。</summary>
        public float ReelSpeed = 8f;

        /// <summary>ロープを伸ばす速さ（m/s。ReelOut。最大長まで）【仮】。</summary>
        public float ReelOutSpeed = 6f;

        /// <summary>
        /// 上とみなすスティックの縦の大きさ（0〜1。D-41）。真上に近い入力だけを数え、斜め上（約 0.71）は数えない
        /// （ユーザーの記録では、振っている間に斜め上を押し続けることが多いため。2026-10-13）。
        /// </summary>
        public float UpInputThreshold = 0.9f;

        /// <summary>下とみなすスティックの縦の大きさ（0〜1。D-41）。斜め下（約 0.71）も数える（下を押したまま左右で振れるように）。</summary>
        public float DownInputThreshold = 0.5f;

        /// <summary>
        /// 長さを保つ・伸ばす間に、左右の入力で振る力（m/s²）。縦の入力は使わない（斜め下でも左右いっぱいの力になる）。
        /// </summary>
        public float HoldSwingAcceleration = 16f;

        /// <summary>スティックでロープに垂直な方向へ加える力（m/s²）。</summary>
        public float SwingAcceleration = 10f;

        /// <summary>減衰（1/s）。0 ならエネルギーが保存される。</summary>
        public float Damping = 0f;

        /// <summary>
        /// 接続直後の速さの下限（m/s。0 なら使わない）。止まっている状態から接続したときの初速の遅さを補う
        /// （ユーザーの指示、2026-10-12。2026-10-14 に 14 から 16 へ少し速くした）。接続した瞬間の速さがこれより遅いときだけ、速さがこの値に届くように
        /// 接続点の方向へ強く引く（巻き取りも速くなる）。振りの速さが育てば、通常の巻き取りに戻る。
        /// ロープの長さを保つ・伸ばすときは、引かずに、振りの方向（ロープに垂直）の速さをこの値まで上げる（D-41）。
        /// </summary>
        public float StartPullSpeed = 16f;

        /// <summary>接続してから、StartPullSpeed の下限を保つティック数。</summary>
        public int StartPullTicks = 20;

        /// <summary>その後、下限を巻き取りの速さ（ReelSpeed）まで直線的に下げるティック数。</summary>
        public int StartPullFadeTicks = 20;

        public TuningValidator Validate()
        {
            var v = new TuningValidator(nameof(InertialWireTuning));
            v.NonNegative(nameof(ReelSpeed), ReelSpeed);
            v.NonNegative(nameof(ReelOutSpeed), ReelOutSpeed);
            v.InRange(nameof(UpInputThreshold), UpInputThreshold, 0.1f, 1f);
            v.InRange(nameof(DownInputThreshold), DownInputThreshold, 0.1f, 1f);
            v.NonNegative(nameof(HoldSwingAcceleration), HoldSwingAcceleration);
            v.NonNegative(nameof(SwingAcceleration), SwingAcceleration);
            v.NonNegative(nameof(Damping), Damping);
            v.NonNegative(nameof(StartPullSpeed), StartPullSpeed);
            v.NonNegative(nameof(StartPullTicks), StartPullTicks);
            v.NonNegative(nameof(StartPullFadeTicks), StartPullFadeTicks);
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
}
