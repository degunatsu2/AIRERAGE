using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Movement;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Wire;
using Airerage.Runtime.Data;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// キャラクターの状態を箱で表示する（プロトタイプ用）。直前と現在のティックの状態を補間する。
    /// 戦闘の状態を色で示す（判定と見た目ができるまでの仮の表示。docs/11_combat_plan.md）: 攻撃の段階（発生・持続・硬直。
    /// 撃破技の発生は金色のチャージ）、ヒットストップ、のけぞり。攻撃の判定が出ている間は、その箱を線で描く（_hitboxLine があれば）。
    /// 撃破技のチャージの間は、チャージのエフェクトと「CHARGE!」を出す（_chargeEffect があれば。ルーレットの技は出さない）。
    /// ワイヤーキャンセルのクールタイムの間は、周りに円形のゲージと「WIRE CANCEL」を出す（_wireCancelGauge があれば。P-1）。
    /// ガード中・ジャストガードの成功は _guardEffect、ガードのクールタイムは _guardGauge で示す（T3-5。あれば）。
    /// 攻撃を受けた瞬間は火花（_hitSpark）、FATAL を受けた瞬間は虹色の光と「FATAL!」（_fatalBurst）、攻撃をワイヤーで
    /// キャンセルした瞬間は紫の光（_wireCancelBurst）を出す（T3-7。あれば）。ジャンプ台・加速リングが効いた瞬間は _gimmickBurst。
    /// 特殊攻撃を出した瞬間は _specialBurst、必殺技を出した瞬間は虹色の光と「SUPER!」（_superBurst）を出す（docs/13 T5-4。あれば）。
    /// ルーレットの最大（JACKPOT）を受けた瞬間は _jackpotBurst、コインの効率が上がっている間（フィーバータイム）は周りに金色のゲージ
    /// （_feverGauge）を出す（RISK TAKER。docs/14。あれば）。雷の波動のクールタイムの間は、水色のゲージ（_waveGauge。SOLDERER。あれば）。
    /// ウインドクローク・キャッスルインジエアー（STORM RUNNER）の残りは、黄緑と空色のゲージ（_cloakGauge・_skyGauge。あれば。周りの風は BoostAuraView）。
    /// 体の周りのゲージは RingGaugeStack にまとめて、重ならないように並べる（古いゲージほど外側、文字は上へ積む。ユーザーの要望、2026-10-30）。
    /// 撃破されて復帰を待っている間は見えなくし、復帰した後の無敵の間は点滅させる（docs/12 T4-2）。
    /// 回る技（AttackData.SpinPerHit）の間は、判定 1 つにつき 1 回転させる。前転する技（AttackData.Flip。THIEF の空中 N）は、最初の判定から
    /// 最後の判定の終わりまでに前へ 1 回転させる。
    /// 透明の間（THIEF の E）は、体と、体の周りのゲージ・光を見せない（足元の影は ThiefEffectsView）。分身（RenderClone）は、体の見た目
    /// （箱・衣装・向きの印）の写しを、分身の位置に出す（分身のワイヤーも線で描く）。必殺技がバンクハイスト（THIEF の R）なら、出した瞬間の
    /// 光を出さず、詠唱を止めてコインを得たときに必殺技の虹色の光を出す（ユーザーの案）。
    /// 向いている方向は、箱の前の面の目の帯と、横へ突き出た印（_facingMarker。あれば）で示す（向きで左右を反転する。ユーザーの要望、2026-10-16）。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        // 無敵の間の点滅の速さ（回/秒）。
        private const float BlinkPerSecond = 10f;

        private enum Tint
        {
            None,
            Startup,
            Charge,
            Active,
            Recovery,
            Hitstop,
            Hitstun,
            Frozen,
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color _startupColor = new Color(1f, 0.85f, 0.2f);
        [Tooltip("撃破技の発生（チャージ）の色（D-38。エフェクトができるまでの仮の表示）")]
        [SerializeField] private Color _chargeColor = new Color(1f, 0.72f, 0f);
        [SerializeField] private Color _activeColor = new Color(0.95f, 0.15f, 0.15f);
        [SerializeField] private Color _recoveryColor = new Color(0.45f, 0.5f, 0.8f);
        [SerializeField] private Color _hitstopColor = Color.white;
        [SerializeField] private Color _hitstunColor = new Color(1f, 0.55f, 0.85f);
        [Tooltip("吹雪で凍っている間の色")]
        [SerializeField] private Color _frozenColor = new Color(0.6f, 0.9f, 1f);
        [Tooltip("攻撃の判定を描く線（4辺。なくてもよい）")]
        [SerializeField] private LineRenderer _hitboxLine;
        [Tooltip("撃破技のチャージのエフェクト（なくてもよい）")]
        [SerializeField] private ChargeEffectView _chargeEffect;
        [Tooltip("ワイヤーキャンセルのクールタイムのゲージ（なくてもよい）")]
        [SerializeField] private RingGaugeView _wireCancelGauge;
        [Tooltip("ガードとジャストガードの表示（なくてもよい）")]
        [SerializeField] private GuardEffectView _guardEffect;
        [Tooltip("ガードのクールタイムのゲージ（なくてもよい）")]
        [SerializeField] private RingGaugeView _guardGauge;
        [Tooltip("攻撃を受けた瞬間の火花（なくてもよい）")]
        [SerializeField] private BurstEffectView _hitSpark;
        [Tooltip("FATAL を受けた瞬間の虹色の光と「FATAL!」（なくてもよい）")]
        [SerializeField] private BurstEffectView _fatalBurst;
        [Tooltip("攻撃をワイヤーでキャンセルした瞬間の光（なくてもよい）")]
        [SerializeField] private BurstEffectView _wireCancelBurst;
        [Tooltip("のけぞり中に地形で跳ね返った瞬間の光（なくてもよい）")]
        [SerializeField] private BurstEffectView _bounceBurst;
        [Tooltip("ジャンプ台・加速リングが効いた瞬間の光（なくてもよい）")]
        [SerializeField] private BurstEffectView _gimmickBurst;
        [Tooltip("特殊攻撃を出した瞬間の光（なくてもよい）")]
        [SerializeField] private BurstEffectView _specialBurst;
        [Tooltip("必殺技を出した瞬間の虹色の光と「SUPER!」（なくてもよい）")]
        [SerializeField] private BurstEffectView _superBurst;
        [Tooltip("ルーレットの最大（JACKPOT）を受けた瞬間の光と「JACKPOT!」（なくてもよい）")]
        [SerializeField] private BurstEffectView _jackpotBurst;
        [Tooltip("コインの効率が上がっている残りのゲージ（フィーバータイム。なくてもよい）")]
        [SerializeField] private RingGaugeView _feverGauge;
        [Tooltip("撃破されて復帰を待つ間の、出現位置の周りの大きなゲージ（なくてもよい）")]
        [SerializeField] private RingGaugeView _respawnGauge;
        [Tooltip("雷の波動（インバータ）を次に出せるまでのゲージ（SOLDERER。なくてもよい）")]
        [SerializeField] private RingGaugeView _waveGauge;
        [Tooltip("キャラクターの仮の見た目（衣装。なければ箱のまま）")]
        [SerializeField] private CostumeView _costume;
        [Tooltip("壁に張り付いていられる残り（紫の円形ゲージ。ユーザーの要望、2026-10-20）")]
        [SerializeField] private RingGaugeView _wallClingGauge;
        [Tooltip("水の中で溺れるまでの残り（水色。あれば。ユーザーの要望、2026-10-26）")]
        [SerializeField] private RingGaugeView _waterGauge;
        [Tooltip("押し続けている間は続く技（JUGGERNAUT の地上技）を、続けられる限界までの残り（あれば。ユーザーの要望、2026-10-29）")]
        [SerializeField] private RingGaugeView _sustainGauge;
        [Tooltip("ウインドクローク（STORM RUNNER の Q）の残り（黄緑。あれば）")]
        [SerializeField] private RingGaugeView _cloakGauge;
        [Tooltip("キャッスルインジエアー（STORM RUNNER の R）の残り（空色。あれば）")]
        [SerializeField] private RingGaugeView _skyGauge;
        [Tooltip("水に飛び込んだ瞬間の水しぶき（あれば）")]
        [SerializeField] private BurstEffectView _splashBurst;
        [Tooltip("悪魔の手に握られた見た目（恐怖の館。ユーザーの要望、2026-10-26）")]
        [SerializeField] private CaptureEffectView _captureEffect;
        [Tooltip("向きの印（目の帯と突き出た印をまとめた子。右向きで作り、左を向いたら左右を反転する。なければ出さない）")]
        [SerializeField] private Transform _facingMarker;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private Tint _shown;
        private int _shownJustGuards;
        private int _shownGuarded;
        private int _shownDamagedTick;
        private int _shownFatalHits;
        private int _shownWireCancelCooldown;
        private int _shownBounces;
        private int _shownGimmicks;
        private int _shownSplashes;
        private bool _shownCaptured;

        // 悪魔の手に握られてから、指が閉じきるまでと、渦に吸い込まれきるまで（ティック。見た目だけ）。
        private const int CaptureGripTicks = 24;
        private const int CaptureSuckTicks = 40;
        private int _shownHazardHits;
        private int _shownSpecial1Cooldown;
        private int _shownSpecial2Cooldown;
        private int _shownGauge;
        private int _shownJackpots;
        private int _shownHeistPayouts;
        private int _feverTicks;
        private int _waveTicks;
        private Renderer[] _facingRenderers;
        private RingGaugeStack _gaugeStack;

        /// <summary>今表示している攻撃の段階（攻撃中でなければ null）。</summary>
        public AttackPhase? ShownPhase
        {
            get
            {
                switch (_shown)
                {
                    case Tint.Startup:
                    case Tint.Charge:
                        return AttackPhase.Startup;
                    case Tint.Active: return AttackPhase.Active;
                    case Tint.Recovery: return AttackPhase.Recovery;
                    default: return null;
                }
            }
        }

        /// <summary>のけぞり（ヒットストップを含む）を表示しているか。</summary>
        public bool ShowingHit => _shown == Tint.Hitstop || _shown == Tint.Hitstun;

        /// <summary>攻撃の判定を描いているか。</summary>
        public bool ShowingHitbox => _hitboxLine != null && _hitboxLine.enabled;

        /// <summary>撃破技のチャージのエフェクト（なければ null）。</summary>
        public ChargeEffectView ChargeEffect => _chargeEffect;

        /// <summary>ワイヤーキャンセルのクールタイムのゲージ（なければ null）。</summary>
        public RingGaugeView WireCancelGauge => _wireCancelGauge;

        /// <summary>ガードとジャストガードの表示（なければ null）。</summary>
        public GuardEffectView GuardEffect => _guardEffect;

        /// <summary>ガードのクールタイムのゲージ（なければ null）。</summary>
        public RingGaugeView GuardGauge => _guardGauge;

        public BurstEffectView HitSpark => _hitSpark;
        public BurstEffectView FatalBurst => _fatalBurst;
        public BurstEffectView WireCancelBurst => _wireCancelBurst;
        public BurstEffectView BounceBurst => _bounceBurst;
        public BurstEffectView GimmickBurst => _gimmickBurst;
        public BurstEffectView SpecialBurst => _specialBurst;
        public BurstEffectView SuperBurst => _superBurst;
        public BurstEffectView JackpotBurst => _jackpotBurst;
        public RingGaugeView FeverGauge => _feverGauge;

        /// <summary>復帰までの残りのゲージ（なければ null）。</summary>
        public RingGaugeView RespawnGauge => _respawnGauge;
        public RingGaugeView WallClingGauge => _wallClingGauge;
        public RingGaugeView WaterGauge => _waterGauge;
        public RingGaugeView SustainGauge => _sustainGauge;
        public RingGaugeView CloakGauge => _cloakGauge;
        public RingGaugeView SkyGauge => _skyGauge;

        /// <summary>体の周りのゲージの並べ方（重ならないように、古いゲージほど外側に）。</summary>
        public RingGaugeStack GaugeStack => _gaugeStack ??= new RingGaugeStack(AroundGauges());
        public BurstEffectView SplashBurst => _splashBurst;
        public CaptureEffectView CaptureEffect => _captureEffect;

        /// <summary>悪魔の手の渦に吸い込まれきって、見えなくしているか。</summary>
        public bool HiddenForCapture { get; private set; }
        public RingGaugeView WaveGauge => _waveGauge;
        public CostumeView Costume => _costume;

        /// <summary>キャラクター character の衣装を出す（試合の仕組みが毎フレーム呼ぶ。変わったときだけ入れ替える）。</summary>
        public void SetCostume(CharacterConfigAsset character)
        {
            if (_costume != null)
            {
                _costume.Show(character);
            }
        }

        /// <summary>撃破されて復帰を待っているので、見えなくしているか。</summary>
        public bool HiddenForRespawn { get; private set; }

        /// <summary>透明（THIEF の E）なので、体と周りのゲージを見えなくしているか。</summary>
        public bool HiddenForInvisible { get; private set; }

        /// <summary>メーデー（JUGGERNAUT の R）でジェット機に吊られて画面の外にいるので、見えなくしているか。</summary>
        public bool HiddenForAway { get; private set; }

        /// <summary>分身（THIEF の E）を出しているか。</summary>
        public bool ShowingClone => _clone != null && _clone.activeSelf;

        /// <summary>分身を出している位置（出していなければ体の位置）。</summary>
        public Vector3 ClonePosition => ShowingClone ? _clone.transform.position : transform.position;

        /// <summary>無敵なので点滅させているか。</summary>
        public bool Blinking { get; private set; }

        /// <summary>向きの印が示している向き（1 = 右、-1 = 左。印がなければ 0）。</summary>
        public int ShownFacing => _facingMarker == null ? 0 : _facingMarker.localScale.x < 0f ? -1 : 1;

        /// <summary>向きの印を見せているか。</summary>
        public bool ShowsFacingMarker => _facingRenderers != null && _facingRenderers.Length > 0 && _facingRenderers[0].enabled;

        public void Render(in CharacterState previous, in CharacterState current, float alpha, Vector3 halfExtents)
        {
            transform.position = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            transform.localScale = halfExtents * 2f;
        }

        /// <summary>
        /// アクション状態（攻撃の段階、ヒットストップ、のけぞり）を色で示し、攻撃の判定を描く。
        /// respawnDelayTicks は復帰を待つ長さ（MatchRules.RespawnDelayTicks。復帰までのゲージの全体の長さに使う）。
        /// movement があれば、壁に張り付いている間に残りの時間を紫の円形ゲージで示す。
        /// </summary>
        public void RenderAction(in CharacterState current, CombatTuning combat, int respawnDelayTicks = 0, MovementTuning movement = null,
            SkillTuning skills = null)
        {
            RenderAway(current, skills);
            RenderWallCling(current, movement);
            RenderWater(current);
            RenderHitbox(current, combat);
            RenderSpin(current, combat);
            RenderSustain(current, combat);
            RenderFacing(current);
            Tint tint = TintOf(current, combat);
            RenderCharge(current, combat, tint == Tint.Charge);
            RenderCooldownGauges(current, combat);
            RenderGuard(current);
            RenderBursts(current, combat);
            RenderBoostGauges(current, skills);
            RenderCapture(current);
            RenderVisibility(current);
            RenderRespawn(current, respawnDelayTicks);
            if (HiddenForInvisible || current.Combat.IsAway)
            {
                HideIndicators();
            }
            // 出しているゲージを、重ならないように並べて描く。
            GaugeStack.Arrange();
            if (tint == _shown)
            {
                return;
            }
            _shown = tint;
            if (_renderer == null && !TryGetComponent(out _renderer))
            {
                return;
            }
            if (tint == Tint.None)
            {
                _renderer.SetPropertyBlock(null);
                return;
            }
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
            _block.SetColor(BaseColorId, ColorOf(tint));
            _renderer.SetPropertyBlock(_block);
        }

        // 抹茶のクリームに張り付いた長さ（ティック。円形のゲージの割合に使う）。
        private int _creamStickTicks;

        // 壁に張り付いていられる残り（WallCling.Remaining）。抹茶のクリーム（PASTRY CHEF の E）に張り付いている間と、感電している間は、
        // 解けるまでの残り。
        // 張り付いていなければ隠す。
        private void RenderWallCling(in CharacterState state, MovementTuning movement)
        {
            if (_wallClingGauge == null)
            {
                return;
            }
            float remaining = movement != null ? WallCling.Remaining(state, movement) : 0f;
            // 感電（SOLDERER の雷の波動・ボールライトニング）も、解けるまでの残りを同じゲージで出す。
            int stick = Mathf.Max(state.Combat.CreamStickTicksLeft, state.Combat.StunTicksLeft);
            _creamStickTicks = stick > 0 ? Mathf.Max(_creamStickTicks, stick) : 0;
            if (stick > 0)
            {
                remaining = stick / (float)_creamStickTicks;
            }
            if (remaining > 0f)
            {
                _wallClingGauge.Show(transform.position, remaining);
            }
            else if (_wallClingGauge.IsShowing)
            {
                _wallClingGauge.Hide();
            }
        }

        // 水の中で溺れるまでの残り（陸に着くかワイヤーをつなぐまで。水から出ている間も、残りがあれば出す）。溺れたら隠す。
        private void RenderWater(in CharacterState state)
        {
            if (_waterGauge == null)
            {
                return;
            }
            CombatState c = state.Combat;
            if (c.WaterTicksLeft > 0 && c.WaterFloatTicks > 0 && !c.IsDrowning && !c.IsWaitingToRespawn)
            {
                _waterGauge.Show(transform.position, c.WaterTicksLeft / (float)c.WaterFloatTicks);
            }
            else if (_waterGauge.IsShowing)
            {
                _waterGauge.Hide();
            }
        }

        // 押し続けている間は続く技（AttackData.HeldSustain）を押し続けている間: 続けられる限界（SustainMaxTicks）までの残り。離したら隠す。
        private void RenderSustain(in CharacterState state, CombatTuning combat)
        {
            if (_sustainGauge == null)
            {
                return;
            }
            AttackData attack = state.Action.Kind == ActionKind.Attack ? combat.Get(state.Action.Attack) : null;
            bool holding = attack != null && attack.HeldSustain && attack.SustainMaxTicks > 0 && state.Action.Charge >= 0;
            if (holding)
            {
                _sustainGauge.Show(transform.position, 1f - state.Action.Ticks / (float)attack.SustainMaxTicks);
            }
            else if (_sustainGauge.IsShowing)
            {
                _sustainGauge.Hide();
            }
        }

        /// <summary>回る技で、今回している角度（度。回っていなければ 0）。</summary>
        public float SpinDegrees { get; private set; }

        /// <summary>前転する技で、今回している角度（度。画面の上で反時計回りが正。回っていなければ 0）。</summary>
        public float FlipDegrees { get; private set; }

        /// <summary>前転する技（AttackData.Flip）の、攻撃の経過 ticks での回転（度。最初の判定から最後の判定の終わりまでに 0 → 360）。</summary>
        public static float FlipAngle(AttackData attack, float ticks)
        {
            AttackHit[] hits = attack.Hits;
            if (hits.Length == 0)
            {
                return 0f;
            }
            float from = hits[0].StartTick;
            float to = from;
            foreach (AttackHit hit in hits)
            {
                from = Mathf.Min(from, hit.StartTick);
                to = Mathf.Max(to, hit.StartTick + hit.ActiveTicks);
            }
            return 360f * Mathf.Clamp01((ticks - from) / Mathf.Max(1f, to - from));
        }

        /// <summary>
        /// 水平に回る砲塔の先の判定（AttackHit.SweepTicks）を持つ攻撃の、経過 ticks での回転（度。判定が前にあるとき 0、後ろにあるとき 180。
        /// HitResolver.SweptOffsetX と同じ回り方）。そういう判定がなければ false。
        /// </summary>
        public static bool SweepAngle(AttackData attack, int ticks, out float degrees)
        {
            degrees = 0f;
            foreach (AttackHit hit in attack.Hits)
            {
                if (hit.SweepTicks > 0)
                {
                    int t = Mathf.Max(0, ticks - hit.StartTick);
                    degrees = 360f * (t % hit.SweepTicks) / hit.SweepTicks;
                    return true;
                }
            }
            return false;
        }

        /// <summary>メーデーで吊られて上がって見える高さ（m。表示だけ。シミュレーションの位置は吊られた場所・降りる場所のまま）。</summary>
        public const float AwayLiftHeight = 26f;

        /// <summary>
        /// メーデー（JUGGERNAUT の R）の間の、表示の上へのずれ（m）: 吊られて上がる間は 0 → AwayLiftHeight（だんだん速く）、場所を選ぶ間は
        /// 画面の外（AwayLiftHeight）、運ばれて降りる間は AwayLiftHeight → 0（だんだん遅く）。吊られていなければ 0。
        /// </summary>
        public static float AwayOffset(in CharacterState state, JuggernautTuning tuning)
        {
            if (!state.Combat.IsAway || tuning == null)
            {
                return 0f;
            }
            float p = JuggernautRules.MaydayProgress(state, tuning);
            switch (state.Combat.MaydayPhase)
            {
                case JuggernautRules.MaydayLift: return AwayLiftHeight * p * p;
                case JuggernautRules.MaydayCarry: return AwayLiftHeight * (1f - p) * (1f - p);
                default: return AwayLiftHeight;
            }
        }

        // メーデーの間: 吊られて上がる・降りる高さだけ上へずらして見せ、場所を選ぶ間（画面の外）は見せない。
        private void RenderAway(in CharacterState state, SkillTuning skills)
        {
            HiddenForAway = state.Combat.MaydayPhase == JuggernautRules.MaydaySelect;
            float offset = AwayOffset(state, skills?.Juggernaut);
            if (offset > 0f)
            {
                transform.position += Vector3.up * offset;
            }
        }

        // 回る技（AttackData.SpinPerHit。つむじ風）: 判定 1 つにつき 1 回転し、終わったら元の向きで止まる（ユーザーの要望、2026-10-16）。
        private void RenderSpin(in CharacterState state, CombatTuning combat)
        {
            float degrees = 0f;
            float flip = 0f;
            if (state.Action.Kind == ActionKind.Attack)
            {
                AttackData attack = combat.Get(state.Action.Attack);
                if (attack.SpinPerHit)
                {
                    degrees = Mathf.Repeat(BoostAuraView.SpinAngle(attack, state.Action.Ticks), 360f) * state.Facing;
                }
                else if (SweepAngle(attack, state.Action.Ticks, out float sweep))
                {
                    // 回る砲塔（JUGGERNAUT の地上技）: 砲塔の先の判定（AttackHit.SweepTicks）と同じ速さで、体ごと水平に回る。
                    degrees = sweep;
                }
                else if (attack.Flip)
                {
                    // 前転: 右向きなら画面の上で時計回り（頭が前へ）。
                    flip = -FlipAngle(attack, state.Action.Ticks) * state.Facing;
                }
            }
            if (degrees != SpinDegrees || flip != FlipDegrees)
            {
                SpinDegrees = degrees;
                FlipDegrees = flip;
                transform.rotation = Quaternion.Euler(0f, degrees, flip);
            }
        }

        // 向きの印: 右向きで作ってあり、左を向いたら左右を反転する（回る技の間は、体と一緒に回る）。
        private void RenderFacing(in CharacterState state)
        {
            if (_facingMarker == null)
            {
                return;
            }
            float sign = state.Facing < 0 ? -1f : 1f;
            Vector3 scale = _facingMarker.localScale;
            if (!Mathf.Approximately(Mathf.Sign(scale.x), sign))
            {
                _facingMarker.localScale = new Vector3(Mathf.Abs(scale.x) * sign, scale.y, scale.z);
            }
        }

        // 復帰を待つ間、出現位置の周りに大きなゲージ（残りの割合）を出す（数字ではなくゲージで。ユーザーの要望、2026-10-13）。
        private void RenderRespawn(in CharacterState state, int respawnDelayTicks)
        {
            if (_respawnGauge == null)
            {
                return;
            }
            int left = state.Combat.RespawnTicksLeft;
            if (left <= 0)
            {
                if (_respawnGauge.IsShowing)
                {
                    _respawnGauge.Hide();
                }
                return;
            }
            _respawnGauge.Show(transform.position, (float)left / Mathf.Max(respawnDelayTicks, left));
        }

        private void RenderVisibility(in CharacterState state)
        {
            HiddenForRespawn = state.Combat.IsWaitingToRespawn;
            HiddenForInvisible = !HiddenForRespawn && state.Combat.InvisibleTicksLeft > 0;
            Blinking = !HiddenForRespawn && state.Combat.InvincibleTicksLeft > 0;
            if (_renderer == null && !TryGetComponent(out _renderer))
            {
                return;
            }
            _renderer.enabled = !HiddenForRespawn && !HiddenForCapture && !HiddenForInvisible && !HiddenForAway
                && !(Blinking && Mathf.Repeat(Time.time * BlinkPerSecond, 1f) >= 0.5f);
            if (_facingMarker != null)
            {
                _facingRenderers = _facingRenderers ?? _facingMarker.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer marker in _facingRenderers)
                {
                    marker.enabled = _renderer.enabled;
                }
            }
        }

        // 悪魔の手に握られている間: 指が閉じて体を握り、紫の渦に体が小さくなりながら回って吸い込まれ、見えなくなる。放された（牢屋に
        // 現れた）瞬間に、その場で渦が一瞬広がる。
        private void RenderCapture(in CharacterState state)
        {
            CombatState c = state.Combat;
            if (c.IsCaptured)
            {
                float grip = Mathf.Clamp01(c.CaptureTicks / (float)CaptureGripTicks);
                float suck = Mathf.Clamp01((c.CaptureTicks - CaptureGripTicks) / (float)CaptureSuckTicks);
                if (_captureEffect != null)
                {
                    _captureEffect.Show(transform.position, grip, suck);
                }
                transform.localScale *= Mathf.Max(0.02f, 1f - suck);
                transform.rotation = Quaternion.Euler(0f, suck * 900f, 0f);
                HiddenForCapture = suck >= 1f;
                _shownCaptured = true;
                return;
            }
            HiddenForCapture = false;
            if (_shownCaptured)
            {
                _shownCaptured = false;
                transform.rotation = Quaternion.Euler(0f, SpinDegrees, FlipDegrees);
                if (_captureEffect != null)
                {
                    _captureEffect.Hide();
                    if (!c.IsWaitingToRespawn)
                    {
                        _captureEffect.Arrive(transform.position);
                    }
                }
            }
        }

        // 状態の変化（受けた時刻、FATAL を受けた数、ワイヤーキャンセルのクールタイムの始まり）から、一瞬の光を出す。
        private void RenderBursts(in CharacterState state, CombatTuning combat)
        {
            CombatState c = state.Combat;
            Vector3 center = transform.position;
            // 透明の間は、居場所が分かる光を出さない（透明になった瞬間の技の光だけは出す）。
            bool shown = c.InvisibleTicksLeft <= 0;
            if (c.FatalHitCount > _shownFatalHits && shown && _fatalBurst != null)
            {
                _fatalBurst.Play(center);
            }
            else if (((c.LastDamagedTick != _shownDamagedTick && state.Action.Kind == ActionKind.Hitstun) || c.HazardHitCount > _shownHazardHits)
                && shown && _hitSpark != null)
            {
                // 攻撃と、ステージの危険（溶岩流・つらら）に当たったとき。
                _hitSpark.Play(center);
            }
            _shownHazardHits = c.HazardHitCount;
            if (c.WireCancelCooldownTicksLeft > _shownWireCancelCooldown && shown && _wireCancelBurst != null)
            {
                _wireCancelBurst.Play(center);
            }
            if (c.BounceCount > _shownBounces && shown && _bounceBurst != null)
            {
                _bounceBurst.Play(center);
            }
            if (c.SplashCount > _shownSplashes && shown && _splashBurst != null)
            {
                _splashBurst.Play(center);
            }
            _shownSplashes = c.SplashCount;
            // ジャンプ台・加速リング・壁ジャンプ。
            int gimmicks = state.JumpPadCount + state.BoostRingCount + state.WallJumpCount;
            if (gimmicks > _shownGimmicks && shown && _gimmickBurst != null)
            {
                _gimmickBurst.Play(center);
            }
            // 特殊攻撃はクールタイムが始まった（増えた）とき、必殺技はゲージが減って必殺技を出しているとき。
            bool special = c.Special1CooldownTicksLeft > _shownSpecial1Cooldown || c.Special2CooldownTicksLeft > _shownSpecial2Cooldown;
            if (special && _specialBurst != null)
            {
                _specialBurst.Play(center);
            }
            // バンクハイスト（THIEF の R）は、出した瞬間の光を出さない（ユーザーの案）。
            bool super = c.Gauge < _shownGauge && state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Super
                && combat.Super.Effect != SkillEffect.BankHeist;
            if (super && shown && _superBurst != null)
            {
                _superBurst.Play(center);
            }
            // バンクハイストは、詠唱を止めてコインを得たときに、ほかのキャラクターの必殺技と同じ虹色の光（ユーザーの要望、2026-10-29）。
            if (c.HeistPayoutCount > _shownHeistPayouts && shown && _superBurst != null)
            {
                _superBurst.Play(center);
            }
            _shownHeistPayouts = c.HeistPayoutCount;
            if (c.JackpotHitCount > _shownJackpots && shown && _jackpotBurst != null)
            {
                _jackpotBurst.Play(center);
            }
            _shownJackpots = c.JackpotHitCount;
            if (_feverGauge != null)
            {
                if (c.CoinBoostTicksLeft > 0)
                {
                    // 全体の長さは、効率が上がったときの残り（いちばん大きかった値）。
                    _feverTicks = Mathf.Max(_feverTicks, c.CoinBoostTicksLeft);
                    _feverGauge.Show(center, c.CoinBoostTicksLeft / (float)_feverTicks);
                }
                else
                {
                    _feverTicks = 0;
                    if (_feverGauge.IsShowing)
                    {
                        _feverGauge.Hide();
                    }
                }
            }
            if (_waveGauge != null)
            {
                // 全体の長さは、波動を出したときの残り（いちばん大きかった値）。
                _waveTicks = c.WaveCooldownTicks > 0 ? Mathf.Max(_waveTicks, c.WaveCooldownTicks) : 0;
                ShowOrHide(_waveGauge, _waveTicks > 0 ? c.WaveCooldownTicks / (float)_waveTicks : 0f);
            }
            _shownSpecial1Cooldown = c.Special1CooldownTicksLeft;
            _shownSpecial2Cooldown = c.Special2CooldownTicksLeft;
            _shownGauge = c.Gauge;
            _shownGimmicks = gimmicks;
            _shownBounces = c.BounceCount;
            _shownFatalHits = c.FatalHitCount;
            _shownDamagedTick = c.LastDamagedTick;
            _shownWireCancelCooldown = c.WireCancelCooldownTicksLeft;
        }

        private void RenderGuard(in CharacterState state)
        {
            if (_guardEffect != null)
            {
                // 数が増えたら成功した瞬間（巻き戻しや位置のリセットで減ったときは何もしない）。
                if (state.Combat.JustGuardCount > _shownJustGuards)
                {
                    _guardEffect.OnJustGuard();
                }
                if (state.Combat.GuardedCount > _shownGuarded)
                {
                    _guardEffect.OnGuarded();
                }
                _guardEffect.Render(transform.position, state.Action.Kind == ActionKind.Guard);
            }
            _shownJustGuards = state.Combat.JustGuardCount;
            _shownGuarded = state.Combat.GuardedCount;
        }

        // ワイヤーキャンセルとガードのクールタイムのゲージ（両方を出すときの並べ方は RingGaugeStack）。
        private void RenderCooldownGauges(in CharacterState state, CombatTuning combat)
        {
            if (_wireCancelGauge != null)
            {
                ShowOrHide(_wireCancelGauge, Fraction(state.Combat.WireCancelCooldownTicksLeft, combat.WireCancelCooldownTicks));
            }
            if (_guardGauge != null)
            {
                ShowOrHide(_guardGauge, Fraction(state.Combat.GuardCooldownTicksLeft, combat.GuardCooldownTicks));
            }
        }

        // ウインドクロークとキャッスルインジエアー（STORM RUNNER）の残り。技の調整値がなければ出さない（ダミー）。
        private void RenderBoostGauges(in CharacterState state, SkillTuning skills)
        {
            bool alive = !state.Combat.IsWaitingToRespawn;
            if (_cloakGauge != null)
            {
                ShowOrHide(_cloakGauge, alive && skills != null ? Fraction(state.Combat.SpeedBoostTicksLeft, skills.SpeedBoost.DurationTicks) : 0f);
            }
            if (_skyGauge != null)
            {
                ShowOrHide(_skyGauge, alive && skills != null ? Fraction(state.Combat.SkyBoostTicksLeft, skills.SkyBoost.DurationTicks) : 0f);
            }
        }

        // 体の周りに出すゲージ（並べる順は、同じフレームに出たときの内から外）。
        private RingGaugeView[] AroundGauges()
        {
            return new[] { _wallClingGauge, _wireCancelGauge, _guardGauge, _waterGauge, _cloakGauge, _feverGauge, _sustainGauge, _waveGauge, _skyGauge };
        }

        // 透明の間: 体の周りのゲージと、ため・判定の表示を隠す。
        private void HideIndicators()
        {
            foreach (RingGaugeView gauge in AroundGauges())
            {
                if (gauge != null && gauge.IsShowing)
                {
                    gauge.Hide();
                }
            }
            if (_chargeEffect != null && _chargeEffect.IsShowing)
            {
                _chargeEffect.Hide();
            }
            if (_hitboxLine != null)
            {
                _hitboxLine.enabled = false;
            }
        }

        // 分身（THIEF の E）の、体の見た目の写しと、分身のワイヤーの線。
        private GameObject _clone;
        private Transform _cloneFacing;
        private LineRenderer _cloneWire;
        private CharacterCostume _cloneCostume;
        private Vector3 _cloneFrom;
        private Vector3 _cloneTo;
        private float _cloneChangedAt;
        private int _cloneTick = int.MinValue;

        /// <summary>
        /// 分身（THIEF の E）を出す（試合の仕組みが毎フレーム呼ぶ。exists が false なら隠す）。見た目は、この体（箱・衣装・向きの印）の
        /// 写し。tick は分身の状態のティック（変わったら、前の位置から 1 ティックかけて動かす）。wire は線の見た目の元（この人の
        /// WireView。なければ分身のワイヤーは描かない）。
        /// </summary>
        public void RenderClone(bool exists, in CharacterState clone, Vector3 halfExtents, int tick, WireView wire)
        {
            if (!exists)
            {
                if (_clone != null && _clone.activeSelf)
                {
                    _clone.SetActive(false);
                }
                if (_cloneWire != null)
                {
                    _cloneWire.enabled = false;
                }
                _cloneTick = int.MinValue;
                return;
            }
            CharacterCostume costume = _costume != null ? _costume.Current : CharacterCostume.Standard;
            if (_clone == null || costume != _cloneCostume)
            {
                BuildClone(costume);
            }
            Vector3 target = clone.Position.ToUnity();
            if (!_clone.activeSelf || _cloneTick == int.MinValue)
            {
                _clone.SetActive(true);
                _cloneFrom = target;
                _cloneTo = target;
                _cloneChangedAt = Time.time;
                _cloneTick = tick;
            }
            else if (tick != _cloneTick)
            {
                _cloneFrom = _clone.transform.position;
                _cloneTo = target;
                _cloneChangedAt = Time.time;
                _cloneTick = tick;
            }
            Vector3 position = Vector3.Lerp(_cloneFrom, _cloneTo, Mathf.Clamp01((Time.time - _cloneChangedAt) / SimTime.DeltaTime));
            _clone.transform.position = position;
            _clone.transform.localScale = halfExtents * 2f;
            if (_cloneFacing != null)
            {
                float sign = clone.Facing < 0 ? -1f : 1f;
                Vector3 scale = _cloneFacing.localScale;
                _cloneFacing.localScale = new Vector3(Mathf.Abs(scale.x) * sign, scale.y, scale.z);
            }
            RenderCloneWire(clone, position, wire);
        }

        // 体の見た目を写す（隠した親の下で複製し、動き出す前にスクリプトと線を外す）。衣装が変わったら作り直す。
        private void BuildClone(CharacterCostume costume)
        {
            if (_clone != null)
            {
                Destroy(_clone);
            }
            var holder = new GameObject("CloneHolder");
            holder.SetActive(false);
            GameObject copy = Instantiate(gameObject, holder.transform);
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                DestroyImmediate(behaviour);
            }
            foreach (Renderer part in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (part is MeshRenderer)
                {
                    part.enabled = true;
                }
                else
                {
                    DestroyImmediate(part);
                }
            }
            copy.name = name + "Clone";
            copy.transform.SetParent(null, false);
            copy.transform.rotation = Quaternion.identity;
            Destroy(holder);
            _clone = copy;
            _cloneCostume = costume;
            _cloneFacing = _facingMarker != null ? copy.transform.Find(_facingMarker.name) : null;
        }

        // 分身のワイヤー（フックが飛んでいる間と接続中。線は、この人のワイヤーの線と同じ見た目）。
        private void RenderCloneWire(in CharacterState clone, Vector3 from, WireView wire)
        {
            WireState w = clone.Wire;
            bool shown = wire != null && (w.Phase == WirePhase.Firing || w.Phase == WirePhase.Attached);
            if (!shown)
            {
                if (_cloneWire != null)
                {
                    _cloneWire.enabled = false;
                }
                return;
            }
            if (_cloneWire == null)
            {
                var source = wire.GetComponent<LineRenderer>();
                _cloneWire = new GameObject(name + "CloneWire").AddComponent<LineRenderer>();
                _cloneWire.useWorldSpace = true;
                _cloneWire.sharedMaterial = source.sharedMaterial;
                _cloneWire.widthCurve = source.widthCurve;
                _cloneWire.widthMultiplier = source.widthMultiplier;
                _cloneWire.startColor = source.startColor;
                _cloneWire.endColor = source.endColor;
                _cloneWire.numCapVertices = source.numCapVertices;
                _cloneWire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Vector3 to = w.Phase == WirePhase.Attached ? w.Anchor.ToUnity() : w.HookPosition.ToUnity();
            int pivots = w.Phase == WirePhase.Attached ? w.Pivots.Count : 0;
            _cloneWire.positionCount = pivots + 2;
            _cloneWire.SetPosition(0, from);
            for (int i = 0; i < pivots; i++)
            {
                _cloneWire.SetPosition(i + 1, w.Pivots[pivots - 1 - i].ToUnity());
            }
            _cloneWire.SetPosition(pivots + 1, to);
            _cloneWire.enabled = true;
        }

        // 体を隠したら（試合が終わった・抜けた）、分身も隠す。
        private void OnDisable()
        {
            if (_clone != null)
            {
                _clone.SetActive(false);
            }
            if (_cloneWire != null)
            {
                _cloneWire.enabled = false;
            }
            _cloneTick = int.MinValue;
        }

        private void OnDestroy()
        {
            if (_clone != null)
            {
                Destroy(_clone);
            }
            if (_cloneWire != null)
            {
                Destroy(_cloneWire.gameObject);
            }
        }

        private void ShowOrHide(RingGaugeView gauge, float fraction)
        {
            if (fraction > 0f)
            {
                gauge.Show(transform.position, fraction);
            }
            else if (gauge.IsShowing)
            {
                gauge.Hide();
            }
        }

        private static float Fraction(int left, int total)
        {
            return left > 0 && total > 0 ? (float)left / total : 0f;
        }

        private void RenderCharge(in CharacterState state, CombatTuning combat, bool charging)
        {
            if (_chargeEffect == null)
            {
                return;
            }
            if (!charging)
            {
                if (_chargeEffect.IsShowing)
                {
                    _chargeEffect.Hide();
                }
                return;
            }
            int chargeTicks = combat.Get(state.Action.Attack).FirstHitTick;
            _chargeEffect.Show(transform.position, (state.Action.Ticks + 1f) / chargeTicks);
        }

        private static Tint TintOf(in CharacterState state, CombatTuning combat)
        {
            if (state.Combat.HitstopTicksLeft > 0 && state.Action.Kind == ActionKind.Hitstun)
            {
                return Tint.Hitstop;
            }
            if (state.Combat.IsFrozen)
            {
                return Tint.Frozen;
            }
            switch (state.Action.Kind)
            {
                case ActionKind.Hitstun:
                    return Tint.Hitstun;
                case ActionKind.Attack:
                    switch (combat.Get(state.Action.Attack).PhaseAt(state.Action.Ticks))
                    {
                        // ルーレットの技は、チャージの代わりに頭上のルーレット（RouletteView）で示す。
                        case AttackPhase.Startup:
                            return state.Action.Attack == combat.KoAttack && !combat.Get(state.Action.Attack).Roulette ? Tint.Charge : Tint.Startup;
                        case AttackPhase.Active: return Tint.Active;
                        default: return Tint.Recovery;
                    }
                default:
                    return Tint.None;
            }
        }

        private Color ColorOf(Tint tint)
        {
            switch (tint)
            {
                case Tint.Startup: return _startupColor;
                case Tint.Charge: return _chargeColor;
                case Tint.Active: return _activeColor;
                case Tint.Recovery: return _recoveryColor;
                case Tint.Hitstop: return _hitstopColor;
                case Tint.Frozen: return _frozenColor;
                default: return _hitstunColor;
            }
        }

        private void RenderHitbox(in CharacterState state, CombatTuning combat)
        {
            if (_hitboxLine == null)
            {
                return;
            }
            AttackHit hit = null;
            if (state.Action.Kind == ActionKind.Attack)
            {
                AttackData attack = combat.Get(state.Action.Attack);
                foreach (AttackHit candidate in attack.Hits)
                {
                    bool active = candidate.IsActiveAt(state.Action.Ticks) && attack.Uses(candidate.Charge, state.Action.Charge);
                    hit = hit ?? (active ? candidate : null);
                }
            }
            _hitboxLine.enabled = hit != null;
            if (hit == null)
            {
                return;
            }
            HitResolver.Hitbox(state, hit, out System.Numerics.Vector2 center, out System.Numerics.Vector2 half);
            const float z = -0.6f; // キャラクターの手前に描く
            _hitboxLine.loop = true;
            _hitboxLine.positionCount = 4;
            // 傾いた判定（AttackHit.AngleDeg）は、傾けて描く。
            float angle = HitResolver.HitAngle(state, hit);
            for (int i = 0; i < 4; i++)
            {
                System.Numerics.Vector2 corner = HitShapes.Corner(center, half, angle, i);
                _hitboxLine.SetPosition(i, new Vector3(corner.X, corner.Y, z));
            }
        }
    }
}
