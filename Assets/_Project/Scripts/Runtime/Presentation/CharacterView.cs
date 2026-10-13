using Degunatsu.Core.Combat;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// キャラクターの状態を箱で表示する（プロトタイプ用）。直前と現在のティックの状態を補間する。
    /// 戦闘の状態を色で示す（判定と見た目ができるまでの仮の表示。docs/11_combat_plan.md）: 攻撃の段階（発生・持続・硬直。
    /// 撃破技の発生は金色のチャージ）、ヒットストップ、のけぞり。攻撃の判定が出ている間は、その箱を線で描く（_hitboxLine があれば）。
    /// 撃破技のチャージの間は、チャージのエフェクトと「CHARGE!」を出す（_chargeEffect があれば）。
    /// ワイヤーキャンセルのクールタイムの間は、周りに円形のゲージと「WIRE CANCEL」を出す（_wireCancelGauge があれば。P-1）。
    /// ガード中・ジャストガードの成功は _guardEffect、ガードのクールタイムは _guardGauge で示す（T3-5。あれば）。
    /// 攻撃を受けた瞬間は火花（_hitSpark）、FATAL を受けた瞬間は虹色の光と「FATAL!」（_fatalBurst）、攻撃をワイヤーで
    /// キャンセルした瞬間は紫の光（_wireCancelBurst）を出す（T3-7。あれば）。ジャンプ台・加速リングが効いた瞬間は _gimmickBurst。
    /// 撃破されて復帰を待っている間は見えなくし、復帰した後の無敵の間は点滅させる（docs/12 T4-2）。
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
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Color _startupColor = new Color(1f, 0.85f, 0.2f);
        [Tooltip("撃破技の発生（チャージ）の色（D-38。エフェクトができるまでの仮の表示）")]
        [SerializeField] private Color _chargeColor = new Color(1f, 0.72f, 0f);
        [SerializeField] private Color _activeColor = new Color(0.95f, 0.15f, 0.15f);
        [SerializeField] private Color _recoveryColor = new Color(0.45f, 0.5f, 0.8f);
        [SerializeField] private Color _hitstopColor = Color.white;
        [SerializeField] private Color _hitstunColor = new Color(1f, 0.55f, 0.85f);
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
        [Tooltip("撃破されて復帰を待つ間の、出現位置の周りの大きなゲージ（なくてもよい）")]
        [SerializeField] private RingGaugeView _respawnGauge;

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

        /// <summary>復帰までの残りのゲージ（なければ null）。</summary>
        public RingGaugeView RespawnGauge => _respawnGauge;

        /// <summary>撃破されて復帰を待っているので、見えなくしているか。</summary>
        public bool HiddenForRespawn { get; private set; }

        /// <summary>無敵なので点滅させているか。</summary>
        public bool Blinking { get; private set; }

        public void Render(in CharacterState previous, in CharacterState current, float alpha, Vector3 halfExtents)
        {
            transform.position = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            transform.localScale = halfExtents * 2f;
        }

        /// <summary>
        /// アクション状態（攻撃の段階、ヒットストップ、のけぞり）を色で示し、攻撃の判定を描く。
        /// respawnDelayTicks は復帰を待つ長さ（MatchRules.RespawnDelayTicks。復帰までのゲージの全体の長さに使う）。
        /// </summary>
        public void RenderAction(in CharacterState current, CombatTuning combat, int respawnDelayTicks = 0)
        {
            RenderHitbox(current, combat);
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
            else if (c.LastDamagedTick != _shownDamagedTick && state.Action.Kind == ActionKind.Hitstun && _hitSpark != null)
            {
                _hitSpark.Play(center);
            }
            if (c.WireCancelCooldownTicksLeft > _shownWireCancelCooldown && _wireCancelBurst != null)
            {
                _wireCancelBurst.Play(center);
            }
            if (c.BounceCount > _shownBounces && _bounceBurst != null)
            {
                _bounceBurst.Play(center);
            }
            int gimmicks = state.JumpPadCount + state.BoostRingCount;
            if (gimmicks > _shownGimmicks && _gimmickBurst != null)
            {
                _gimmickBurst.Play(center);
            }
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
            switch (state.Action.Kind)
            {
                case ActionKind.Hitstun:
                    return Tint.Hitstun;
                case ActionKind.Attack:
                    switch (combat.Get(state.Action.Attack).PhaseAt(state.Action.Ticks))
                    {
                        case AttackPhase.Startup: return state.Action.Attack == combat.KoAttack ? Tint.Charge : Tint.Startup;
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
                foreach (AttackHit candidate in combat.Get(state.Action.Attack).Hits)
                {
                    hit = hit ?? (candidate.IsActiveAt(state.Action.Ticks) ? candidate : null);
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
            _hitboxLine.SetPosition(0, new Vector3(center.X - half.X, center.Y - half.Y, z));
            _hitboxLine.SetPosition(1, new Vector3(center.X + half.X, center.Y - half.Y, z));
            _hitboxLine.SetPosition(2, new Vector3(center.X + half.X, center.Y + half.Y, z));
            _hitboxLine.SetPosition(3, new Vector3(center.X - half.X, center.Y + half.Y, z));
        }
    }
}
