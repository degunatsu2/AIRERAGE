using Airerage.Core.Combat;
using Airerage.Core.Movement;
using Airerage.Core.Simulation;
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
    /// 撃破されて復帰を待っている間は見えなくし、復帰した後の無敵の間は点滅させる（docs/12 T4-2）。
    /// 回る技（AttackData.SpinPerHit）の間は、判定 1 つにつき 1 回転させる。
    /// 向いている方向は、箱の前の面の目の帯と、横へ突き出た印（_facingMarker。あれば）で示す（向きで左右を反転する。ユーザーの要望、2026-10-16）。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        // 無敵の間の点滅の速さ（回/秒）。
        private const float BlinkPerSecond = 10f;

        // ワイヤーキャンセルとガードのクールタイムのゲージを同時に出すときの半径（m）。内側の文字は下、外側の文字は上に出す
        // （ゲージと文字が重ならないように。ユーザーの指摘、2026-10-13）。
        private const float StackedInnerRadius = 1.2f;
        private const float StackedOuterRadius = 2.3f;

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
        private int _shownHazardHits;
        private int _shownSpecial1Cooldown;
        private int _shownSpecial2Cooldown;
        private int _shownGauge;
        private int _shownJackpots;
        private int _feverTicks;
        private int _waveTicks;
        private Renderer[] _facingRenderers;

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
        public void RenderAction(in CharacterState current, CombatTuning combat, int respawnDelayTicks = 0, MovementTuning movement = null)
        {
            RenderWallCling(current, movement);
            RenderHitbox(current, combat);
            RenderSpin(current, combat);
            RenderFacing(current);
            Tint tint = TintOf(current, combat);
            RenderCharge(current, combat, tint == Tint.Charge);
            RenderCooldownGauges(current, combat);
            RenderGuard(current);
            RenderBursts(current);
            RenderVisibility(current);
            RenderRespawn(current, respawnDelayTicks);
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

        /// <summary>回る技で、今回している角度（度。回っていなければ 0）。</summary>
        public float SpinDegrees { get; private set; }

        // 回る技（AttackData.SpinPerHit。つむじ風）: 判定 1 つにつき 1 回転し、終わったら元の向きで止まる（ユーザーの要望、2026-10-16）。
        private void RenderSpin(in CharacterState state, CombatTuning combat)
        {
            float degrees = 0f;
            if (state.Action.Kind == ActionKind.Attack)
            {
                AttackData attack = combat.Get(state.Action.Attack);
                if (attack.SpinPerHit)
                {
                    degrees = Mathf.Repeat(BoostAuraView.SpinAngle(attack, state.Action.Ticks), 360f) * state.Facing;
                }
            }
            if (degrees != SpinDegrees)
            {
                SpinDegrees = degrees;
                transform.rotation = Quaternion.Euler(0f, degrees, 0f);
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
            Blinking = !HiddenForRespawn && state.Combat.InvincibleTicksLeft > 0;
            if (_renderer == null && !TryGetComponent(out _renderer))
            {
                return;
            }
            _renderer.enabled = !HiddenForRespawn && !(Blinking && Mathf.Repeat(Time.time * BlinkPerSecond, 1f) >= 0.5f);
            if (_facingMarker != null)
            {
                _facingRenderers = _facingRenderers ?? _facingMarker.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer marker in _facingRenderers)
                {
                    marker.enabled = _renderer.enabled;
                }
            }
        }

        // 状態の変化（受けた時刻、FATAL を受けた数、ワイヤーキャンセルのクールタイムの始まり）から、一瞬の光を出す。
        private void RenderBursts(in CharacterState state)
        {
            CombatState c = state.Combat;
            Vector3 center = transform.position;
            if (c.FatalHitCount > _shownFatalHits && _fatalBurst != null)
            {
                _fatalBurst.Play(center);
            }
            else if (((c.LastDamagedTick != _shownDamagedTick && state.Action.Kind == ActionKind.Hitstun) || c.HazardHitCount > _shownHazardHits)
                && _hitSpark != null)
            {
                // 攻撃と、ステージの危険（溶岩流・つらら）に当たったとき。
                _hitSpark.Play(center);
            }
            _shownHazardHits = c.HazardHitCount;
            if (c.WireCancelCooldownTicksLeft > _shownWireCancelCooldown && _wireCancelBurst != null)
            {
                _wireCancelBurst.Play(center);
            }
            if (c.BounceCount > _shownBounces && _bounceBurst != null)
            {
                _bounceBurst.Play(center);
            }
            // ジャンプ台・加速リング・壁ジャンプ。
            int gimmicks = state.JumpPadCount + state.BoostRingCount + state.WallJumpCount;
            if (gimmicks > _shownGimmicks && _gimmickBurst != null)
            {
                _gimmickBurst.Play(center);
            }
            // 特殊攻撃はクールタイムが始まった（増えた）とき、必殺技はゲージが減って必殺技を出しているとき。
            bool special = c.Special1CooldownTicksLeft > _shownSpecial1Cooldown || c.Special2CooldownTicksLeft > _shownSpecial2Cooldown;
            if (special && _specialBurst != null)
            {
                _specialBurst.Play(center);
            }
            bool super = c.Gauge < _shownGauge && state.Action.Kind == ActionKind.Attack && state.Action.Attack == AttackSlot.Super;
            if (super && _superBurst != null)
            {
                _superBurst.Play(center);
            }
            if (c.JackpotHitCount > _shownJackpots && _jackpotBurst != null)
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

        // ワイヤーキャンセルとガードのクールタイムのゲージ。両方を出すときは、半径を離し、文字を上と下に分ける。
        private void RenderCooldownGauges(in CharacterState state, CombatTuning combat)
        {
            float wire = Fraction(state.Combat.WireCancelCooldownTicksLeft, combat.WireCancelCooldownTicks);
            float guard = Fraction(state.Combat.GuardCooldownTicksLeft, combat.GuardCooldownTicks);
            bool both = wire > 0f && guard > 0f && _wireCancelGauge != null && _guardGauge != null;
            if (_wireCancelGauge != null)
            {
                _wireCancelGauge.SetLayout(both ? StackedInnerRadius : (float?)null, both);
                ShowOrHide(_wireCancelGauge, wire);
            }
            if (_guardGauge != null)
            {
                _guardGauge.SetLayout(both ? StackedOuterRadius : (float?)null, false);
                ShowOrHide(_guardGauge, guard);
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
