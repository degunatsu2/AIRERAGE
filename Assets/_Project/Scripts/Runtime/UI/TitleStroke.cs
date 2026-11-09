using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 白い背景の上の大きな見出しとロゴの縁取り（D-92 の T2。2026-11-09 のユーザーの決定）: 文字は白、周りに画面の色を暗くした太い縁、
    /// その下に画面の色を白に寄せた淡い色の影を少し下へずらして重ねる。文字の網（Text の頂点）を写して重ねるので、字体はそのまま。
    /// 画面の色は AccentTint（またはメインメニューの項目の選択）で変わり、0.25 秒で移る。前の見出しの印（TitleMark）もこの色に合わせる。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class TitleStroke : BaseMeshEffect
    {
        /// <summary>画面の色が移る秒数。</summary>
        public const float AccentSeconds = 0.25f;

        // 縁の方向の数（多いほど丸い縁）と、影に使う方向の数。
        private const int StrokeDirections = 16;
        private const int ShadowDirections = 8;
        // 縁の暗い色を作る夜の色と混ぜる割合、影の淡い色を作る白と混ぜる割合（見本のページと同じ）。
        private static readonly Color Night = new Color(12f / 255f, 20f / 255f, 46f / 255f, 1f);
        private const float DeepMix = 0.45f;
        private const float PastelMix = 0.55f;

        private static readonly List<UIVertex> Source = new List<UIVertex>();
        private static readonly List<UIVertex> Output = new List<UIVertex>();

        [Tooltip("縁の太さ（文字の外へ。ピクセル）")]
        [SerializeField] private float _width = 6f;
        [Tooltip("影を下へずらす量（ピクセル）")]
        [SerializeField] private float _drop = 8f;
        // 画面の色の今の状態（画面の切り替えの写しにも引き継ぐため、保存する値にする）。
        [SerializeField, HideInInspector] private Color _from = new Color(0.35f, 0.85f, 1f, 1f);
        [SerializeField, HideInInspector] private Color _to = new Color(0.35f, 0.85f, 1f, 1f);
        [SerializeField, HideInInspector] private float _at = -10f;

        /// <summary>縁の暗い色（画面の色を夜の色に寄せる）。</summary>
        public static Color Deep(Color accent)
        {
            Color c = Color.Lerp(accent, Night, DeepMix);
            c.a = 1f;
            return c;
        }

        /// <summary>影の淡い色（画面の色を白に寄せる。白い背景の帯と同じ割合）。</summary>
        public static Color Pastel(Color accent)
        {
            Color c = Color.Lerp(accent, Color.white, PastelMix);
            c.a = 1f;
            return c;
        }

        /// <summary>今の画面の色（移っている途中なら途中の色）。</summary>
        public Color Accent
        {
            get
            {
                float t = Application.isPlaying ? (Time.unscaledTime - _at) / AccentSeconds : 1f;
                return t >= 1f ? _to : Color.Lerp(_from, _to, UiEase.CssEase(t));
            }
        }

        /// <summary>移っている先の画面の色（テスト用）。</summary>
        public Color TargetAccent => _to;

        /// <summary>縁の太さ（テスト用）。</summary>
        public float Width => _width;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(float width, float drop, Color accent)
        {
            _width = width;
            _drop = drop;
            accent.a = 1f;
            _from = accent;
            _to = accent;
            _at = -10f;
            Refresh();
        }

        /// <summary>画面の色を accent にする（出していないときはすぐ、出しているときは 0.25 秒で移る）。</summary>
        public void SetAccent(Color accent)
        {
            accent.a = 1f;
            if (_to == accent)
            {
                return;
            }
            bool live = Application.isPlaying && isActiveAndEnabled;
            _from = live ? Accent : accent;
            _to = accent;
            _at = live ? Time.unscaledTime : -10f;
            Refresh();
        }

        private void Update()
        {
            if (Application.isPlaying && Time.unscaledTime - _at < AccentSeconds + 0.05f)
            {
                Refresh();
            }
        }

        // 文字と前の印を描き直す。
        private void Refresh()
        {
            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
            TitleMark mark = GetComponentInChildren<TitleMark>(true);
            if (mark != null)
            {
                mark.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive())
            {
                return;
            }
            Source.Clear();
            vh.GetUIVertexStream(Source);
            if (Source.Count == 0)
            {
                return;
            }
            Color accent = Accent;
            Color deep = Deep(accent);
            Color pastel = Pastel(accent);
            Output.Clear();
            // 影（縁の形を下へずらした淡い色）→ 縁（暗い色）→ 文字（白）の順に重ねる。
            for (int d = 0; d < ShadowDirections; d++)
            {
                float a = 2f * Mathf.PI * d / ShadowDirections;
                Copy(new Vector2(Mathf.Cos(a) * _width, Mathf.Sin(a) * _width - _drop), pastel);
            }
            for (int d = 0; d < StrokeDirections; d++)
            {
                float a = 2f * Mathf.PI * d / StrokeDirections;
                Copy(new Vector2(Mathf.Cos(a) * _width, Mathf.Sin(a) * _width), deep);
            }
            Output.AddRange(Source);
            vh.Clear();
            vh.AddUIVertexTriangleStream(Output);
        }

        // 文字の網を offset だけずらし、色 c（濃さは元の文字の濃さ）で写す。
        private static void Copy(Vector2 offset, Color c)
        {
            for (int i = 0; i < Source.Count; i++)
            {
                UIVertex v = Source[i];
                v.position.x += offset.x;
                v.position.y += offset.y;
                Color32 original = v.color;
                v.color = new Color(c.r, c.g, c.b, c.a * original.a / 255f);
                Output.Add(v);
            }
        }
    }
}
