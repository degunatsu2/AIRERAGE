using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 右へ傾いた平行四辺形の札（uGUI。D-65、D-69。右上の札 GuidePlate と同じ形）。中の色は Graphic の color、縁は BorderColor。
    /// 上の辺が下の辺より Slant（高さに対する割合）だけ右にずれる。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SlantedPlate : MaskableGraphic
    {
        [Tooltip("傾き（高さに対する、上の辺の右へのずれの割合）")]
        [SerializeField] private float _slant = 0.35f;
        [Tooltip("縁の太さ（ピクセル）。0 なら縁なし")]
        [SerializeField] private float _borderWidth = 3f;
        [SerializeField] private Color _borderColor = new Color(1f, 1f, 1f, 0.6f);

        public Color BorderColor
        {
            get => _borderColor;
            set
            {
                if (_borderColor != value)
                {
                    _borderColor = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(float slant, float borderWidth, Color fill, Color border)
        {
            _slant = slant;
            _borderWidth = borderWidth;
            color = fill;
            _borderColor = border;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float shift = r.height * _slant;
            // 外の四隅（左下、右下、右上、左上）。
            Vector2[] outer =
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMax - shift, r.yMin),
                new Vector2(r.xMax, r.yMax), new Vector2(r.xMin + shift, r.yMax),
            };
            AddQuad(vh, outer, color);
            if (_borderWidth <= 0f || _borderColor.a <= 0f)
            {
                return;
            }
            // 内の四隅（縁の太さだけ内へ。斜めの辺は横にずらす）。
            float b = _borderWidth;
            float side = b * Mathf.Sqrt(1f + _slant * _slant);
            Vector2[] inner =
            {
                new Vector2(r.xMin + side + b * _slant, r.yMin + b), new Vector2(r.xMax - shift - side + b * _slant, r.yMin + b),
                new Vector2(r.xMax - side - b * _slant, r.yMax - b), new Vector2(r.xMin + shift + side - b * _slant, r.yMax - b),
            };
            Color border = _borderColor;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                AddQuad(vh, new[] { outer[i], outer[j], inner[j], inner[i] }, border);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2[] corners, Color color)
        {
            int start = vh.currentVertCount;
            foreach (Vector2 corner in corners)
            {
                vh.AddVert(corner, color, Vector2.zero);
            }
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
