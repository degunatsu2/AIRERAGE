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
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
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

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private Tint _shown;

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

        public void Render(in CharacterState previous, in CharacterState current, float alpha, Vector3 halfExtents)
        {
            transform.position = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            transform.localScale = halfExtents * 2f;
        }

        /// <summary>アクション状態（攻撃の段階、ヒットストップ、のけぞり）を色で示し、攻撃の判定を描く。</summary>
        public void RenderAction(in CharacterState current, CombatTuning combat)
        {
            RenderHitbox(current, combat);
            Tint tint = TintOf(current, combat);
            RenderCharge(current, combat, tint == Tint.Charge);
            RenderWireCancelCooldown(current, combat);
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

        private void RenderWireCancelCooldown(in CharacterState state, CombatTuning combat)
        {
            if (_wireCancelGauge == null)
            {
                return;
            }
            int left = state.Combat.WireCancelCooldownTicksLeft;
            if (left <= 0 || combat.WireCancelCooldownTicks <= 0)
            {
                if (_wireCancelGauge.IsShowing)
                {
                    _wireCancelGauge.Hide();
                }
                return;
            }
            _wireCancelGauge.Show(transform.position, (float)left / combat.WireCancelCooldownTicks);
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
