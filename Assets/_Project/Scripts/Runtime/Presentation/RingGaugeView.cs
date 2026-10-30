using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 残り時間を示す円形のゲージ（ユーザーの要望、2026-10-13）。見た目はすべての円形ゲージで共通の SlantedRingGauge（斜めの区切りの輪に、
    /// 光る先端とグラデーションの導火線。D-76、2026-10-29）。上（12 時）から時計回りに残りの分だけ光り、減っていく。
    /// ワイヤーキャンセルのクールタイム（キャラクターの周り。「WIRE CANCEL」）、ワイヤーの接続の残り時間（接続点の周り）、
    /// 撃破された後の復帰までの残り（出現位置の周りに大きく。「RESPAWN」。docs/12 T4-2）などに使う。
    /// 名前は、ゲージの右上の斜めの札に出す（右上の札と同じ形）。
    /// 呼ぶ側が毎フレーム Show（出すとき）か Hide を呼ぶ。同じキャラクターの周りのゲージは RingGaugeStack にまとめ、重ならないように
    /// 並べる（古いゲージほど外側、文字は上。その場合は Show で描かず、RingGaugeStack.Arrange で描く）。状態は読むだけ。
    /// </summary>
    public sealed class RingGaugeView : MonoBehaviour
    {
        // キャラクターの手前に描く。
        private const float Z = -0.65f;
        // 札の斜め（高さに対する横のずれの割合）と、文字の周りの余白（画面の点）。
        private const float PlateShear = 0.35f;
        private const float PlatePadding = 6f;
        // 並べ直したときに、輪の半径と文字の高さが目標へ寄る速さ（1/秒。0.15 秒ほどで寄る）。
        private const float LayoutSharpness = 20f;

        [Tooltip("ゲージを描く材質（頂点の色で塗る。Sprites-Default）")]
        [SerializeField] private Material _material;
        [Tooltip("文字の位置を画面に写すカメラ（文字がなければ不要）")]
        [SerializeField] private Camera _camera;
        [Tooltip("ゲージの名前（空なら出さない）")]
        [SerializeField] private string _label = string.Empty;
        [SerializeField] private float _radius = 1.3f;
        [SerializeField] private float _width = 0.18f;
        [SerializeField] private Color _color = new Color(0.65f, 0.3f, 1f, 0.55f);
        [SerializeField] private int _fontSize = 20;
        [SerializeField] private int _sortingOrder = 5;

        private static readonly Color PlateColor = new Color(0.06f, 0.05f, 0.1f, 0.88f);

        private SlantedRingGauge _gauge;
        private GUIStyle _style;
        private Vector3 _center;

        // 並べ方（RingGaugeStack）。目標の半径・文字の高さと、今描いている値（滑らかに目標へ寄る。出た瞬間は目標から）。
        private RingGaugeStack _stack;
        private float _targetRadius = -1f;
        private float _shownRadius = -1f;
        private float _targetLabelOffset;
        private float _shownLabelOffset = -1f;
        private float _anchorRadius = -1f;
        private float _anchorWidth;
        private float _labelHeight = -1f;

        /// <summary>今表示しているか。</summary>
        public bool IsShowing { get; private set; }

        /// <summary>縁（区切りごとの暗い縁と外側の線）を描いているか（テスト用）。</summary>
        public bool ShowsFrame => _gauge != null && _gauge.ShowsFrame;

        /// <summary>表示している残りの割合（0〜1）。</summary>
        public float Fraction { get; private set; }

        public string Label => _label;

        /// <summary>最後に描いたゲージの中心。</summary>
        public Vector3 Center => _center;

        /// <summary>今描いている半径（並べ直している間は、目標へ動いている途中の値）。</summary>
        public float Radius => _shownRadius > 0f ? _shownRadius : TargetRadius;

        /// <summary>並べ方で決まった目標の半径（並べていなければ、設定した半径）。</summary>
        public float TargetRadius => _targetRadius > 0f ? _targetRadius : _radius;

        /// <summary>設定した半径（ほかのゲージがなければ、この半径に出す）。</summary>
        public float BaseRadius => _radius;

        /// <summary>輪の太さ（m）。</summary>
        public float Width => _width;

        /// <summary>名前の札を出すか。</summary>
        public bool HasLabel => !string.IsNullOrEmpty(_label);

        /// <summary>名前の札の高さ（画面の点。描く前は文字の大きさからの見積もり）。</summary>
        public float LabelHeight => _labelHeight > 0f ? _labelHeight : _fontSize * 1.3f + PlatePadding * 0.6f;

        /// <summary>名前の札を、起点からどれだけ上に積んでいるか（画面の点。目標。いちばん新しいゲージは 0）。</summary>
        public float LabelOffset => _targetLabelOffset;

        /// <summary>同じキャラクターの周りのゲージとして並べているか。</summary>
        public bool InStack => _stack != null;

        /// <summary>名前の札の起点（世界の座標。いちばん外の輪の右上）。</summary>
        public Vector3 LabelAnchor
        {
            get
            {
                float radius = _anchorRadius > 0f ? _anchorRadius : Radius;
                float width = _anchorRadius > 0f ? _anchorWidth : _width;
                return _center + new Vector3(radius * 0.35f, radius + width, 0f);
            }
        }

        internal void JoinStack(RingGaugeStack stack)
        {
            _stack = stack;
        }

        internal void SetStackTarget(float radius, float labelOffset)
        {
            _targetRadius = radius;
            _targetLabelOffset = labelOffset;
        }

        // 並べたゲージを描く（半径と文字の高さを目標へ寄せる）。
        internal void DrawInStack()
        {
            float blend = 1f - Mathf.Exp(-LayoutSharpness * Time.deltaTime);
            _shownRadius = _shownRadius > 0f ? Mathf.Lerp(_shownRadius, TargetRadius, blend) : TargetRadius;
            _shownLabelOffset = _shownLabelOffset >= 0f ? Mathf.Lerp(_shownLabelOffset, _targetLabelOffset, blend) : _targetLabelOffset;
            _gauge?.Draw(_center, _shownRadius, _width, Fraction, _color, Z);
        }

        internal void SetLabelAnchor(float radius, float width)
        {
            _anchorRadius = radius;
            _anchorWidth = width;
        }

        private void Awake()
        {
            if (_material == null)
            {
                Debug.LogError($"{name}: ゲージの材質が設定されていません", this);
                enabled = false;
                return;
            }
            _gauge = new SlantedRingGauge(this, name + "Mesh", _material, _sortingOrder);
            Hide();
        }

        private void OnDestroy()
        {
            _gauge?.Destroy();
        }

        private void LateUpdate()
        {
            // 隠した後の、終わりの白い輪。
            _gauge?.Advance();
        }

        /// <summary>名前と半径（と色、太さ、文字の大きさ）を設定する（シーンのビルダー用）。</summary>
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
            if (_gauge == null)
            {
                return;
            }
            IsShowing = true;
            Fraction = Mathf.Min(fraction, 1f);
            _center = center;
            if (_stack == null)
            {
                _gauge.Draw(center, Radius, _width, Fraction, _color, Z);
            }
        }

        public void Hide()
        {
            IsShowing = false;
            Fraction = 0f;
            _gauge?.Hide();
            // 次に出るときは、その時の目標の半径から（前の位置から動き出さない）。
            _shownRadius = -1f;
            _shownLabelOffset = -1f;
            _targetRadius = -1f;
            _targetLabelOffset = 0f;
            _anchorRadius = -1f;
        }

        private void OnGUI()
        {
            if (!IsShowing || string.IsNullOrEmpty(_label))
            {
                return;
            }
            if (_camera == null)
            {
                // ステージのシーンに置いたゲージ（研究所の発射装置）は、試合のシーンのカメラを使う。
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }
            // 札の左下を、輪の右上（並べているときは、いちばん外の輪の右上から、新しいゲージの札の上に積んだ所）に付ける。
            Vector3 screen = _camera.WorldToScreenPoint(LabelAnchor);
            if (screen.z <= 0f)
            {
                return;
            }
            if (_style == null)
            {
                _style = GameFonts.Use(new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = _fontSize, padding = new RectOffset(0, 0, 0, 0),
                });
            }
            var content = new GUIContent(_label);
            Vector2 size = _style.CalcSize(content);
            float width = size.x + PlatePadding * 2f + size.y * PlateShear;
            float height = size.y + PlatePadding * 0.6f;
            _labelHeight = height;
            float x = screen.x;
            float y = Screen.height - screen.y - height - Mathf.Max(0f, _shownLabelOffset);
            var plate = new Rect(x, y, width, height);
            Color text = Color.Lerp(_color, Color.white, 0.35f);
            text.a = 0.95f;
            Color edge = _color;
            edge.a = 0.95f;
            DrawSlanted(new Rect(plate.x - 1.5f, plate.y - 1.5f, plate.width + 3f, plate.height + 3f), edge);
            DrawSlanted(plate, PlateColor);
            Color previous = GUI.color;
            GUI.color = text;
            GUI.Label(plate, content, _style);
            GUI.color = previous;
        }

        // 斜めの札（右上の札と同じ向き: 上の辺が右へずれた平行四辺形）。
        private static void DrawSlanted(Rect rect, Color color)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            float centerY = rect.y + rect.height * 0.5f;
            // x' = x − shear × (y − 中央の y)（上ほど右へ）。
            Matrix4x4 shear = Matrix4x4.identity;
            shear.m01 = -PlateShear;
            shear.m03 = PlateShear * centerY;
            GUI.matrix = previousMatrix * shear;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }
}
