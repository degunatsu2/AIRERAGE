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
        private float _width = 1f;

        // 雷を帯びたワイヤー（SOLDERER のインバータ）の、ジグザグの点の数と揺れ幅（m）。
        private const int ElectricPoints = 14;
        private const float ElectricJitter = 0.18f;
        private static readonly Color ElectricColor = new Color(1f, 0.95f, 0.35f);
        private static readonly Color ElectricEndColor = new Color(0.45f, 0.85f, 1f);

        // プラズマ球の電気を帯びたワイヤー（研究所。2026-11-20 の要望）の色: 球の中の光の筋と同じ赤紫〜青紫。1 区間あたりの点の数。
        private static readonly Color ChargedColor = new Color(1f, 0.6f, 1f);
        private static readonly Color ChargedEndColor = new Color(0.6f, 0.5f, 1f);
        private const int ChargedPointsPerSegment = 10;
        private readonly System.Collections.Generic.List<Vector3> _rope = new System.Collections.Generic.List<Vector3>();

        /// <summary>雷を帯びた見た目で描いているか（テスト用）。</summary>
        public bool ShowsElectric { get; private set; }

        /// <summary>プラズマ球の電気を帯びた見た目で描いているか（テスト用）。</summary>
        public bool ShowsCharged { get; private set; }

        /// <summary>電気を帯びているとき、接続点（球の真ん中）と、ロープが球から出ていく向きの次の点（支点かキャラクター）。</summary>
        public Vector3 ChargedAnchor { get; private set; }
        public Vector3 ChargedToward { get; private set; }

        /// <summary>接続を保てる残り時間のゲージ（なければ null）。</summary>
        public RingGaugeView AttachGauge => _attachGauge;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _startColor = _line.startColor;
            _endColor = _line.endColor;
            _width = _line.widthMultiplier;
            Hide();
        }

        /// <param name="charged">ワイヤーが電気を帯びているか（研究所のプラズマ球につないでいる。Core の StageZones.IsWireCharged）。</param>
        public void Render(in CharacterState previous, in CharacterState current, float alpha, WireTuning tuning, bool charged = false)
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
            charged &= wire.Phase == WirePhase.Attached;
            if (charged)
            {
                // 電気を帯びたワイヤー: キャラクター → 支点 → 接続点の折れ線の、区間ごとに両端を固定して間を細かく揺らす（球の中の光の筋の
                // ように、細かくちらつく）。
                _rope.Clear();
                _rope.Add(from);
                for (int i = wire.Pivots.Count - 1; i >= 0; i--)
                {
                    _rope.Add(wire.Pivots[i].ToUnity());
                }
                _rope.Add(to);
                ChargedAnchor = to;
                ChargedToward = _rope[_rope.Count - 2];
                int segments = _rope.Count - 1;
                _line.positionCount = segments * ChargedPointsPerSegment + 1;
                int frame = Mathf.FloorToInt(Time.time * 40f);
                int k = 0;
                for (int seg = 0; seg < segments; seg++)
                {
                    Vector3 a = _rope[seg];
                    Vector3 along = _rope[seg + 1] - a;
                    Vector3 side = new Vector3(-along.y, along.x, 0f).normalized;
                    for (int i = 0; i < ChargedPointsPerSegment; i++, k++)
                    {
                        float t = i / (float)ChargedPointsPerSegment;
                        float jitter = i == 0 ? 0f : (Mathf.PerlinNoise(k * 0.81f, frame * 0.43f) - 0.5f) * 2f * ElectricJitter * 1.3f;
                        _line.SetPosition(k, a + along * t + side * jitter);
                    }
                }
                _line.SetPosition(k, to);
            }
            else if (wire.Phase == WirePhase.Attached && wire.Pivots.Count > 0)
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
            ShowsCharged = charged;
            _line.startColor = charged ? ChargedColor : ShowsElectric ? ElectricColor : _startColor;
            _line.endColor = charged ? ChargedEndColor : ShowsElectric ? ElectricEndColor : _endColor;
            // 電気を帯びたロープは太く（遠くからも分かるように）。
            _line.widthMultiplier = charged ? _width * 1.8f : _width;
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
            ShowsCharged = false;
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
