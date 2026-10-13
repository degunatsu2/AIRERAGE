using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 残り時間を、円の周りの導火線のように短くなっていくゲージで示す（ユーザーの要望、2026-10-13）。
    /// 上（12 時）から時計回りに残りの分だけ弧を描き、燃えている先端に火花を置く。全体の長さを薄い輪で示す。
    /// ワイヤーキャンセルのクールタイム（キャラクターの周り。「WIRE CANCEL」）と、ワイヤーの接続の残り時間（接続点の周り）に使う。
    /// 呼ぶ側が毎フレーム Show / Hide を呼ぶ。状態は読むだけ。
    /// </summary>
    public sealed class RingGaugeView : MonoBehaviour
    {
        private const int FullCirclePoints = 64;
        private const int SparkPoints = 8;

        // キャラクターの手前に描く。
        private const float Z = -0.65f;

        [Tooltip("全体の長さを示す薄い輪")]
        [SerializeField] private LineRenderer _track;
        [Tooltip("残りを示す弧（導火線）")]
        [SerializeField] private LineRenderer _fuse;
        [Tooltip("燃えている先端の火花")]
        [SerializeField] private LineRenderer _spark;
        [Tooltip("文字の位置を画面に写すカメラ（文字がなければ不要）")]
        [SerializeField] private Camera _camera;
        [Tooltip("ゲージの上に出す文字（空なら出さない）")]
        [SerializeField] private string _label = string.Empty;
        [SerializeField] private float _radius = 1.3f;
        [SerializeField] private float _width = 0.12f;
        [SerializeField] private Color _color = new Color(0.65f, 0.3f, 1f, 0.55f);
        [SerializeField] private Color _sparkColor = new Color(1f, 0.92f, 0.65f, 0.95f);
        [SerializeField] private int _fontSize = 20;

        private GUIStyle _style;
        private Vector3 _center;

        /// <summary>今表示しているか。</summary>
        public bool IsShowing { get; private set; }

        /// <summary>表示している残りの割合（0〜1）。</summary>
        public float Fraction { get; private set; }

        public string Label => _label;

        /// <summary>最後に描いたゲージの中心。</summary>
        public Vector3 Center => _center;

        private void Awake()
        {
            Hide();
        }

        /// <summary>文字と半径（と色）を設定する（シーンのビルダー用）。</summary>
        public void Configure(string label, float radius, Color? color = null)
        {
            _label = label ?? string.Empty;
            _radius = radius;
            if (color.HasValue)
            {
                _color = color.Value;
            }
        }

        /// <summary>center の周りに、残りの割合 fraction（0〜1）のゲージを描く。0 以下なら隠す。</summary>
        public void Show(Vector3 center, float fraction)
        {
            if (fraction <= 0f)
            {
                Hide();
                return;
            }
            IsShowing = true;
            Fraction = Mathf.Min(fraction, 1f);
            _center = center;

            if (_track != null)
            {
                Color track = _color;
                track.a *= 0.3f;
                DrawArc(_track, center, 1f, _width * 0.6f, track);
            }
            Vector3 tip = DrawArc(_fuse, center, Fraction, _width, _color);
            if (_spark != null)
            {
                // 火花は少しちらつく（表示の時間で決める。シミュレーションには影響しない）。
                float flicker = 1f + 0.25f * Mathf.Sin(Time.time * 45f);
                _spark.positionCount = SparkPoints;
                _spark.loop = true;
                for (int i = 0; i < SparkPoints; i++)
                {
                    float angle = Mathf.PI * 2f * i / SparkPoints;
                    float r = _width * 0.9f * flicker;
                    _spark.SetPosition(i, tip + new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, -0.01f));
                }
                _spark.widthMultiplier = _width * 0.8f;
                _spark.startColor = _spark.endColor = _sparkColor;
                _spark.enabled = true;
            }
        }

        public void Hide()
        {
            IsShowing = false;
            Fraction = 0f;
            SetEnabled(_track, false);
            SetEnabled(_fuse, false);
            SetEnabled(_spark, false);
        }

        // 上（12 時）から時計回りに、全周の fraction の弧を描き、先端の位置を返す。
        private Vector3 DrawArc(LineRenderer line, Vector3 center, float fraction, float width, Color color)
        {
            int points = Mathf.Max(2, Mathf.CeilToInt(FullCirclePoints * fraction) + 1);
            Vector3 tip = center;
            if (line == null)
            {
                return tip;
            }
            line.loop = false;
            line.positionCount = points;
            for (int i = 0; i < points; i++)
            {
                float angle = Mathf.PI * 0.5f - Mathf.PI * 2f * fraction * i / (points - 1);
                tip = center + new Vector3(Mathf.Cos(angle) * _radius, Mathf.Sin(angle) * _radius, Z);
                line.SetPosition(i, tip);
            }
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.enabled = true;
            return tip;
        }

        private static void SetEnabled(LineRenderer line, bool enabled)
        {
            if (line != null)
            {
                line.enabled = enabled;
            }
        }

        private void OnGUI()
        {
            if (!IsShowing || _camera == null || string.IsNullOrEmpty(_label))
            {
                return;
            }
            Vector3 screen = _camera.WorldToScreenPoint(_center + new Vector3(0f, _radius + 0.35f, 0f));
            if (screen.z <= 0f)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = _fontSize };
            }
            var rect = new Rect(screen.x - 120f, Screen.height - screen.y - 15f, 240f, 30f);
            Color previous = GUI.color;
            GUI.color = new Color(0.1f, 0f, 0.15f, 0.8f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), _label, _style);
            GUI.color = new Color(0.85f, 0.7f, 1f, 0.95f);
            GUI.Label(rect, _label, _style);
            GUI.color = previous;
        }
    }
}
