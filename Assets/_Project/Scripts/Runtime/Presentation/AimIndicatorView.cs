using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーの照準のインジケーター。半透明の線で射出の方向を示し、終点の印と色で結果の予測を示す
    /// （接続できる / 接続できない地形に当たる / 届かない）。射程の限界は円で示す
    /// （色は仮にワイヤーと同じ紫。後でキャラクターとワイヤーの輪郭線と一緒に、プレイヤーカラーに合わせる）。
    /// 予測は WireTargeting.PredictShot（射出と同じ計算）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class AimIndicatorView : MonoBehaviour
    {
        private const int RingSegments = 72;
        private const int MarkerSegments = 24;

        [SerializeField] private LineRenderer _aimLine;
        [SerializeField] private LineRenderer _targetMarker;
        [SerializeField] private LineRenderer _rangeRing;

        [Header("色（半透明）")]
        [SerializeField] private Color _attachColor = new Color(0.15f, 0.95f, 0.4f, 0.8f);
        [SerializeField] private Color _blockedColor = new Color(1f, 0.25f, 0.25f, 0.7f);
        [SerializeField] private Color _outOfRangeColor = new Color(0.1f, 0.1f, 0.15f, 0.45f);
        [SerializeField] private Color _rangeColor = new Color(0.6f, 0.2f, 0.85f, 0.55f);
        [Tooltip("すぐに射出できないとき（回収中・クールダウン中）の不透明度の倍率")]
        [SerializeField, Range(0f, 1f)] private float _notReadyAlphaScale = 0.35f;

        [Header("形")]
        [Tooltip("照準の線を、キャラクターの中心からこの距離だけ離して描き始める（m）")]
        [SerializeField] private float _lineStartOffset = 0.6f;
        [Tooltip("終点の印の半径（m）")]
        [SerializeField] private float _markerRadius = 0.35f;

        private void Awake()
        {
            _rangeRing.positionCount = RingSegments;
            _rangeRing.loop = true;
            _targetMarker.positionCount = MarkerSegments;
            _targetMarker.loop = true;
            _aimLine.positionCount = 2;
        }

        public void Render(Vector3 origin, in WireState wire, in WireShotPreview preview, float maxLength)
        {
            float alphaScale = wire.Phase == WirePhase.Ready ? 1f : _notReadyAlphaScale;

            SetColor(_rangeRing, _rangeColor, alphaScale);
            DrawCircle(_rangeRing, origin, maxLength, RingSegments);

            // フックが飛んでいる間と接続中は、照準を出さない（射出できないため）。
            bool aiming = wire.Phase != WirePhase.Firing && wire.Phase != WirePhase.Attached;
            _aimLine.enabled = aiming;
            _targetMarker.enabled = aiming && preview.Result != WireShotResult.OutOfRange;
            if (!aiming)
            {
                return;
            }

            Color color = preview.Result == WireShotResult.Attach ? _attachColor
                : preview.Result == WireShotResult.HitNonAttachable ? _blockedColor
                : _outOfRangeColor;
            Vector3 end = preview.EndPoint.ToUnity();
            Vector3 direction = preview.Aim.Direction.ToUnity();
            float length = Vector3.Distance(origin, end);
            Vector3 start = origin + direction * Mathf.Min(_lineStartOffset, length);

            SetColor(_aimLine, color, alphaScale);
            _aimLine.SetPosition(0, start);
            _aimLine.SetPosition(1, end);

            if (_targetMarker.enabled)
            {
                SetColor(_targetMarker, color, alphaScale);
                DrawCircle(_targetMarker, end, _markerRadius, MarkerSegments);
            }
        }

        private static void SetColor(LineRenderer line, Color color, float alphaScale)
        {
            color.a *= alphaScale;
            line.startColor = color;
            line.endColor = color;
        }

        private static void DrawCircle(LineRenderer line, Vector3 center, float radius, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }
    }
}
