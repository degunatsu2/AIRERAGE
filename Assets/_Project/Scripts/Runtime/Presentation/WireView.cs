using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーを線で表示する（プロトタイプ用）。フックが飛んでいる間はフックまで、接続中は接続点まで線を引き、接続点に印を置く。
    /// ロープが地形の角に巻き付いているときは、角（支点）で折れ曲がった線にする（D-33）。
    /// 外れたフックを回収している間は、外れた位置からキャラクターへ戻ってくるフックまで線を引く。
    /// 接続中は、接続点の周りに、接続を保てる残り時間（MaxAttachTicks まで）を円形のゲージで示す（_attachGauge があれば。
    /// ユーザーの要望、2026-10-13）。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WireView : MonoBehaviour
    {
        [SerializeField] private Transform _anchorMarker;
        [Tooltip("接続を保てる残り時間のゲージ（なくてもよい）")]
        [SerializeField] private RingGaugeView _attachGauge;

        private LineRenderer _line;
        private Color _startColor;
        private Color _endColor;

        // 雷を帯びたワイヤー（SOLDERER のインバータ）の、ジグザグの点の数と揺れ幅（m）。
        private const int ElectricPoints = 14;
        private const float ElectricJitter = 0.18f;
        private static readonly Color ElectricColor = new Color(1f, 0.95f, 0.35f);
        private static readonly Color ElectricEndColor = new Color(0.45f, 0.85f, 1f);

        /// <summary>雷を帯びた見た目で描いているか（テスト用）。</summary>
        public bool ShowsElectric { get; private set; }

        /// <summary>接続を保てる残り時間のゲージ（なければ null）。</summary>
        public RingGaugeView AttachGauge => _attachGauge;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _startColor = _line.startColor;
            _endColor = _line.endColor;
            Hide();
        }

        public void Render(in CharacterState previous, in CharacterState current, float alpha, WireTuning tuning)
        {
            WireState wire = current.Wire;
            if (wire.Phase != WirePhase.Firing && wire.Phase != WirePhase.Attached && wire.Phase != WirePhase.Retracting)
            {
                Hide();
                return;
            }

            Vector3 from = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            Vector3 to;
            if (wire.Phase == WirePhase.Attached)
            {
                to = wire.Anchor.ToUnity();
            }
            else if (wire.Phase == WirePhase.Retracting && wire.Whip)
            {
                // 鞭（インバータの間に外れて、押したまま）: 伸ばしたまま振り回す。
                to = Vector3.Lerp(previous.Wire.Whip ? previous.Wire.HookPosition.ToUnity() : wire.HookPosition.ToUnity(),
                    wire.HookPosition.ToUnity(), alpha);
            }
            else if (wire.Phase == WirePhase.Retracting)
            {
                // 外れた位置（HookPosition）から、回収の時間をかけてキャラクターへ戻す。
                float progress = tuning.MissRetractTicks > 0 ? Mathf.Clamp01((wire.PhaseTicks + alpha) / tuning.MissRetractTicks) : 1f;
                to = Vector3.Lerp(wire.HookPosition.ToUnity(), from, progress);
            }
            else
            {
                // 直前のティックもフックが飛んでいれば補間する（射出した直後は現在の位置）。
                to = previous.Wire.Phase == WirePhase.Firing
                    ? Vector3.Lerp(previous.Wire.HookPosition.ToUnity(), wire.HookPosition.ToUnity(), alpha)
                    : wire.HookPosition.ToUnity();
            }

            _line.enabled = true;
            if (wire.Phase == WirePhase.Attached && wire.Pivots.Count > 0)
            {
                // キャラクター → 最後の支点 → … → 最初の支点 → 接続点。
                int count = wire.Pivots.Count;
                _line.positionCount = count + 2;
                _line.SetPosition(0, from);
                for (int i = 0; i < count; i++)
                {
                    _line.SetPosition(i + 1, wire.Pivots[count - 1 - i].ToUnity());
                }
                _line.SetPosition(count + 1, to);
            }
            else if (current.Combat.InverterOn)
            {
                // 雷を帯びたワイヤー: 両端を固定して、間をジグザグに揺らす。
                _line.positionCount = ElectricPoints;
                Vector3 along = to - from;
                Vector3 side = new Vector3(-along.y, along.x, 0f).normalized;
                int frame = Mathf.FloorToInt(Time.time * 30f);
                for (int i = 0; i < ElectricPoints; i++)
                {
                    float t = i / (float)(ElectricPoints - 1);
                    float jitter = i == 0 || i == ElectricPoints - 1 ? 0f
                        : (Mathf.PerlinNoise(i * 0.73f, frame * 0.37f) - 0.5f) * 2f * ElectricJitter * (wire.Whip ? 1.6f : 1f);
                    _line.SetPosition(i, from + along * t + side * jitter);
                }
            }
            else
            {
                _line.positionCount = 2;
                _line.SetPosition(0, from);
                _line.SetPosition(1, to);
            }
            ShowsElectric = current.Combat.InverterOn;
            _line.startColor = ShowsElectric ? ElectricColor : _startColor;
            _line.endColor = ShowsElectric ? ElectricEndColor : _endColor;
            if (_anchorMarker != null)
            {
                _anchorMarker.gameObject.SetActive(wire.Phase == WirePhase.Attached);
                _anchorMarker.position = to;
            }
            if (_attachGauge != null)
            {
                if (wire.Phase == WirePhase.Attached && tuning.MaxAttachTicks > 0)
                {
                    _attachGauge.Show(to, 1f - (wire.PhaseTicks + alpha) / tuning.MaxAttachTicks);
                }
                else if (_attachGauge.IsShowing)
                {
                    _attachGauge.Hide();
                }
            }
        }

        private void Hide()
        {
            _line.enabled = false;
            if (_anchorMarker != null)
            {
                _anchorMarker.gameObject.SetActive(false);
            }
            if (_attachGauge != null && _attachGauge.IsShowing)
            {
                _attachGauge.Hide();
            }
        }
    }
}
