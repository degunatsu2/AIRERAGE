using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 残り時間を、円の周りの導火線のように短くなっていくゲージで示す（ユーザーの要望、2026-10-13）。
    /// 上（12 時）から時計回りに残りの分だけ弧を描き、燃えている先端に火花を置く。全体の長さを薄い輪で示す。
    /// ワイヤーキャンセルのクールタイム（キャラクターの周り。「WIRE CANCEL」）、ワイヤーの接続の残り時間（接続点の周り）、
    /// 撃破された後の復帰までの残り（出現位置の周りに大きく。「RESPAWN」。docs/12 T4-2）に使う。
    /// 呼ぶ側が毎フレーム Show / Hide を呼ぶ。状態は読むだけ。
    /// ゲージには縁を付ける（暗い帯の上に弧を描き、外側に明るい細い線。ユーザーの要望、2026-10-16: 同じ色のオーラと見分けにくい）。
    /// 縁は DrawFrame で、ほかの円形のゲージ（強化の残り、金庫のチャージ）にも同じものを付ける。
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

        /// <summary>縁の暗い帯（ゲージの太さに足す分。m）と、外側の明るい線の太さ（m）。</summary>
        public const float FrameExtra = 0.08f;
        public const float RimWidth = 0.025f;
        private static readonly Color FrameColor = new Color(0.04f, 0.04f, 0.07f, 0.85f);
        private static readonly Color RimColor = new Color(1f, 1f, 1f, 0.75f);

        private GUIStyle _style;
        private Vector3 _center;
        private LineRenderer _border;
        private LineRenderer _rim;
        private float _layoutRadius = -1f;
        private bool _labelBelow;

        /// <summary>今表示しているか。</summary>
        public bool IsShowing { get; private set; }

        /// <summary>縁を描いているか（テスト用）。</summary>
        public bool ShowsFrame => _border != null && _border.enabled && _rim != null && _rim.enabled;

        /// <summary>表示している残りの割合（0〜1）。</summary>
        public float Fraction { get; private set; }

        public string Label => _label;

        /// <summary>最後に描いたゲージの中心。</summary>
        public Vector3 Center => _center;

        /// <summary>今の半径（SetLayout で変えていなければ、設定した半径）。</summary>
        public float Radius => _layoutRadius > 0f ? _layoutRadius : _radius;

        /// <summary>文字をゲージの下に出しているか（ふつうは上）。</summary>
        public bool LabelBelow => _labelBelow;

        /// <summary>
        /// 同じキャラクターの周りに複数のゲージを出すときの配置（ユーザーの指摘: 重なって見にくい）。radius が null なら設定した半径に戻す。
        /// labelBelow なら文字をゲージの下に出す。
        /// </summary>
        public void SetLayout(float? radius, bool labelBelow)
        {
            _layoutRadius = radius ?? -1f;
            _labelBelow = labelBelow;
        }

        private void Awake()
        {
            if (_fuse != null)
            {
                CreateFrame(_fuse, out _border, out _rim);
                if (_track != null)
                {
                    _track.sortingOrder = _fuse.sortingOrder;
                }
                if (_spark != null)
                {
                    _spark.sortingOrder = _fuse.sortingOrder + 1;
                }
            }
            Hide();
        }

        /// <summary>
        /// 円形のゲージの縁の線（暗い帯と、外側の明るい線）を、ゲージの弧 like と同じ親の下に作る（弧より先・後に描く）。
        /// </summary>
        public static void CreateFrame(LineRenderer like, out LineRenderer border, out LineRenderer rim)
        {
            border = Object.Instantiate(like, like.transform.parent);
            border.name = like.name + "Frame";
            border.sortingOrder = like.sortingOrder - 1;
            border.loop = true;
            border.enabled = false;
            rim = Object.Instantiate(like, like.transform.parent);
            rim.name = like.name + "Rim";
            rim.sortingOrder = like.sortingOrder + 1;
            rim.loop = true;
            rim.enabled = false;
        }

        /// <summary>中心 center、半径 radius、太さ width のゲージの縁を描く（show が false なら隠す）。z はゲージの奥行き。</summary>
        public static void DrawFrame(LineRenderer border, LineRenderer rim, Vector3 center, float radius, float width, float z, bool show)
        {
            if (border == null || rim == null)
            {
                return;
            }
            border.enabled = show;
            rim.enabled = show;
            if (!show)
            {
                return;
            }
            Circle(border, center, radius, z + 0.01f);
            border.widthMultiplier = width + FrameExtra;
            border.startColor = border.endColor = FrameColor;
            Circle(rim, center, radius + (width + FrameExtra) * 0.5f, z - 0.005f);
            rim.widthMultiplier = RimWidth;
            rim.startColor = rim.endColor = RimColor;
        }

        private static void Circle(LineRenderer line, Vector3 center, float radius, float z)
        {
            line.loop = true;
            line.positionCount = FullCirclePoints;
            for (int i = 0; i < FullCirclePoints; i++)
            {
                float angle = Mathf.PI * 2f * i / FullCirclePoints;
                line.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, z));
            }
        }

        /// <summary>文字と半径（と色、太さ、文字の大きさ）を設定する（シーンのビルダー用）。</summary>
        public void Configure(string label, float radius, Color? color = null, float? width = null, int? fontSize = null)
        {
            _label = label ?? string.Empty;
            _radius = radius;
            if (color.HasValue)
            {
                _color = color.Value;
            }
            if (width.HasValue)
            {
                _width = width.Value;
            }
            if (fontSize.HasValue)
            {
                _fontSize = fontSize.Value;
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

            DrawFrame(_border, _rim, center, Radius, _width, Z, true);
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
            SetEnabled(_border, false);
            SetEnabled(_rim, false);
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
                tip = center + new Vector3(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius, Z);
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
            float labelY = _labelBelow ? -(Radius + 0.35f) : Radius + 0.35f;
            Vector3 screen = _camera.WorldToScreenPoint(_center + new Vector3(0f, labelY, 0f));
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
            // 文字はゲージと同じ色を明るくしたもの（ユーザーの指摘: 文字の色をゲージの色に合わせる）。
            Color text = Color.Lerp(_color, Color.white, 0.35f);
            text.a = 0.95f;
            GUI.color = text;
            GUI.Label(rect, _label, _style);
            GUI.color = previous;
        }
    }
}
