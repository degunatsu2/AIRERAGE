using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 誘導表示の札（ユーザーの要望、2026-10-14）: 横長の長方形を、斜めの線で左 1 : 右 2 に分ける。左に絵、右に半透明の黒の背景と
    /// 白い文字。長方形の縁と斜めの線は色をつける（拠点は水色、100 枚のコインは虹色、右上の表はプレイヤーの色）。
    /// 札の形（左と右の塗り、縁と斜めの線）は、大きさごとにテクスチャを1回だけ作り、色は描くときに GUI.color でつける。
    /// 虹色の縁は、色をずらしたテクスチャを何枚か作っておき、時間で切り替える。座標は OnGUI の座標（左上が原点）。
    /// 使い終わったら Dispose でテクスチャを消す。
    /// </summary>
    public sealed class GuidePlate
    {
        private const int RainbowFrames = 12;

        private readonly int _width;
        private readonly int _height;
        private readonly int _border;
        private readonly Texture2D _left;
        private readonly Texture2D _right;
        private readonly Texture2D _frame;
        private readonly List<Texture2D> _rainbow = new List<Texture2D>();

        /// <param name="width">札の幅（ピクセル）。</param>
        /// <param name="height">札の高さ（ピクセル）。</param>
        /// <param name="border">縁と斜めの線の太さ（ピクセル）。</param>
        public GuidePlate(int width, int height, int border = 3)
        {
            _width = width;
            _height = height;
            _border = border;
            _left = Create("GuidePlateLeft", (x, y) => Inside(x, y) && LeftOfSlant(x, y) ? 1f : 0f);
            _right = Create("GuidePlateRight", (x, y) => Inside(x, y) && !LeftOfSlant(x, y) ? 1f : 0f);
            _frame = Create("GuidePlateFrame", FrameAlpha);
        }

        public Vector2 Size => new Vector2(_width, _height);

        /// <summary>札を描く（背景、縁、左の絵）。text が空でなければ、右の部分の中央に白い文字で出す。</summary>
        public void Draw(Rect rect, Color frameColor, Texture2D icon, string text, GUIStyle textStyle, float alpha = 1f)
        {
            DrawBackground(rect, alpha);
            DrawFrame(rect, frameColor, alpha);
            DrawIcon(rect, icon, alpha);
            if (!string.IsNullOrEmpty(text))
            {
                DrawText(RightRect(rect), text, textStyle, new Color(1f, 1f, 1f, alpha));
            }
        }

        /// <summary>虹色の縁の札を描く（縁の色は time で移り変わる）。</summary>
        public void DrawRainbow(Rect rect, float time, Texture2D icon, string text, GUIStyle textStyle, float alpha = 1f)
        {
            DrawBackground(rect, alpha);
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, alpha));
            GUI.DrawTexture(rect, RainbowFrame(time));
            GUI.color = previous;
            DrawIcon(rect, icon, alpha);
            if (!string.IsNullOrEmpty(text))
            {
                DrawText(RightRect(rect), text, textStyle, new Color(1f, 1f, 1f, alpha));
            }
        }

        /// <summary>虹色の縁と斜めの線だけを描く（色は time で移り変わる）。</summary>
        public void DrawRainbowFrame(Rect rect, float time, float alpha = 1f)
        {
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, alpha));
            GUI.DrawTexture(rect, RainbowFrame(time));
            GUI.color = previous;
        }

        /// <summary>背景（左は少し薄い黒、右は半透明の黒）。</summary>
        public void DrawBackground(Rect rect, float alpha = 1f)
        {
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.35f * alpha));
            GUI.DrawTexture(rect, _left);
            GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.6f * alpha));
            GUI.DrawTexture(rect, _right);
            GUI.color = previous;
        }

        /// <summary>縁と斜めの線。</summary>
        public void DrawFrame(Rect rect, Color color, float alpha = 1f)
        {
            Color previous = GUI.color;
            color.a *= alpha;
            GUI.color = HudFade.Apply(color);
            GUI.DrawTexture(rect, _frame);
            GUI.color = previous;
        }

        /// <summary>左の部分に絵を描く（斜めの線にかからない大きさで、中央に）。</summary>
        public void DrawIcon(Rect rect, Texture2D icon, float alpha = 1f)
        {
            if (icon == null)
            {
                return;
            }
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, alpha));
            GUI.DrawTexture(IconRect(rect), icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        /// <summary>
        /// 左の部分の、絵を置く正方形（札の大きさに合わせて伸び縮みする）。左の部分は斜めの線で台形になっているので、高さの真ん中での
        /// 左の端と斜めの線の中央に置き、下の角が斜めの線にかからない大きさにする（以前は狭い下の端に合わせていて、左に寄っていた）。
        /// </summary>
        public Rect IconRect(Rect rect)
        {
            float scaleX = rect.width / _width;
            float scaleY = rect.height / _height;
            float half = _height * 0.5f;
            float centerX = (_border + SlantX(half) - _border * 0.5f) * 0.5f;
            float size = _height - _border * 2f - 6f;
            while (size > 4f && (centerX + size * 0.5f > SlantX(half + size * 0.5f) - _border || centerX - size * 0.5f < _border + 2f))
            {
                size -= 1f;
            }
            return new Rect(rect.x + (centerX - size * 0.5f) * scaleX, rect.y + (half - size * 0.5f) * scaleY, size * scaleX, size * scaleY);
        }

        /// <summary>札の形（左と右の部分）を、1 色で塗る（光らせる、暗くするなど）。</summary>
        public void DrawFill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(color);
            GUI.DrawTexture(rect, _left);
            GUI.DrawTexture(rect, _right);
            GUI.color = previous;
        }

        /// <summary>右の部分（文字を置く）。斜めの線の、いちばん右（上の端）より右。</summary>
        public Rect RightRect(Rect rect)
        {
            float scaleX = rect.width / _width;
            float left = (SlantX(0) + _border) * scaleX;
            return new Rect(rect.x + left, rect.y, rect.width - left - _border * scaleX, rect.height);
        }

        public void Dispose()
        {
            Object.Destroy(_left);
            Object.Destroy(_right);
            Object.Destroy(_frame);
            foreach (Texture2D texture in _rainbow)
            {
                Object.Destroy(texture);
            }
            _rainbow.Clear();
        }

        /// <summary>白い文字（影つき）。</summary>
        public static void DrawText(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.7f * color.a));
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.color = HudFade.Apply(color);
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }

        /// <summary>札を、画面の外の物の方向の端に置く（矢印の内側。画面からはみ出さない）。</summary>
        public Rect PlaceAtEdge(Vector2 edge, float angle, float arrowSize, float margin)
        {
            Vector2 center = ScreenEdgeMarker.Inward(edge, angle, arrowSize * 0.5f + Mathf.Max(_width, _height) * 0.5f + 4f);
            float x = Mathf.Clamp(center.x - _width * 0.5f, margin, Screen.width - margin - _width);
            float y = Mathf.Clamp(center.y - _height * 0.5f, margin, Screen.height - margin - _height);
            return new Rect(x, y, _width, _height);
        }

        private Texture2D RainbowFrame(float time)
        {
            if (_rainbow.Count == 0)
            {
                for (int f = 0; f < RainbowFrames; f++)
                {
                    float phase = f / (float)RainbowFrames;
                    _rainbow.Add(CreateColored($"GuidePlateRainbow{f}", (x, y) =>
                    {
                        float a = FrameAlpha(x, y);
                        Color c = Color.HSVToRGB(Mathf.Repeat(phase + (x + y) / (float)(_width + _height), 1f), 0.7f, 1f);
                        c.a = a;
                        return c;
                    }));
                }
            }
            int index = Mathf.FloorToInt(Mathf.Repeat(time * 0.8f, 1f) * RainbowFrames) % RainbowFrames;
            return _rainbow[index];
        }

        // 斜めの線の x（上の端 y = 0 から下の端 y = height へ、右上から左下へ傾く）。線の中心が幅の 1/3。
        private float SlantX(float yFromTop)
        {
            float lean = _height * 0.22f;
            return _width / 3f + lean - 2f * lean * (yFromTop / _height);
        }

        private bool Inside(int x, int yFromTop)
        {
            return x >= 0 && x < _width && yFromTop >= 0 && yFromTop < _height;
        }

        private bool LeftOfSlant(int x, int yFromTop)
        {
            return x + 0.5f < SlantX(yFromTop + 0.5f);
        }

        private float FrameAlpha(int x, int yFromTop)
        {
            bool edge = x < _border || x >= _width - _border || yFromTop < _border || yFromTop >= _height - _border;
            float d = Mathf.Abs(x + 0.5f - SlantX(yFromTop + 0.5f));
            float slant = Mathf.Clamp01(_border * 0.5f + 0.5f - d);
            return edge ? 1f : slant;
        }

        private Texture2D Create(string name, System.Func<int, int, float> alpha)
        {
            return CreateColored(name, (x, y) => new Color(1f, 1f, 1f, alpha(x, y)));
        }

        private Texture2D CreateColored(string name, System.Func<int, int, Color> color)
        {
            var texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[_width * _height];
            for (int yFromTop = 0; yFromTop < _height; yFromTop++)
            {
                int row = (_height - 1 - yFromTop) * _width;
                for (int x = 0; x < _width; x++)
                {
                    pixels[row + x] = color(x, yFromTop);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
