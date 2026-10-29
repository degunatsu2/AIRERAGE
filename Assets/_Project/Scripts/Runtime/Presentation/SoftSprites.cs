using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 起動時に描く、やわらかい板の絵（白で描き、色は板ごとに掛ける）。THIEF の煙（ThiefEffectsView）と、重ねた爆発（LayeredExplosionView）が使う。
    /// 作った絵は使う側が Destroy で片付ける（Release）。
    /// </summary>
    public static class SoftSprites
    {
        private const int Size = 128;

        /// <summary>
        /// 煙のかたまり: 内側は不透明、ふちはやわらかく、ゆらいだ形で薄くなる（明るさにもむら）。layers は濃さ（同じ絵を何枚重ねた濃さにするか）。
        /// </summary>
        public static Sprite CreatePuff(int layers, string name = "SoftPuff")
        {
            return Create(name, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                float noise = Fbm(u * 2.3f + 5.1f, v * 2.3f + 2.7f);
                float edge = 1f - Mathf.SmoothStep(0.55f, 1f, r + (noise - 0.5f) * 0.3f);
                float single = Mathf.Clamp01(edge * (1.6f + (noise - 0.5f) * 0.4f));
                float alpha = 1f - Mathf.Pow(1f - single, Mathf.Max(1, layers));
                float shade = Mathf.Lerp(0.8f, 1f, noise);
                return new Color(shade, shade, shade, alpha);
            });
        }

        /// <summary>光の玉: 真ん中が明るく、外へなめらかに消える（ふちの形にゆらぎはない）。</summary>
        public static Sprite CreateGlow(string name = "SoftGlow")
        {
            return Create(name, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                float alpha = Mathf.Clamp01(1f - r);
                return new Color(1f, 1f, 1f, alpha * alpha * (3f - 2f * alpha));
            });
        }

        /// <summary>作った絵とその絵の元の画像を片付ける。</summary>
        public static void Release(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }
            Object.Destroy(sprite.texture);
            Object.Destroy(sprite);
        }

        private static Sprite Create(string name, System.Func<float, float, Color> pixel)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    pixels[y * Size + x] = pixel(u, v);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        // 重ねたゆらぎ（0〜1）。
        private static float Fbm(float x, float y)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += Mathf.PerlinNoise(x, y) * amplitude;
                total += amplitude;
                x = x * 2.03f + 1.7f;
                y = y * 2.03f + 3.1f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }
    }
}
