using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 誘導表示の札（GuidePlate）と右上の表に使う絵を、コードで描いて作る（仮の絵。本番の絵ができたら差し替える）。
    /// どれも色つきのテクスチャで、使い終わったら Destroy する。
    /// </summary>
    public static class GuideIcons
    {
        /// <summary>拠点（銀行の建物）: 三角の屋根、柱 4 本、土台。</summary>
        public static Texture2D Bank(int size, Color color)
        {
            return Draw("IconBank", size, (u, v) =>
            {
                // u, v は 0〜1（v は上が 0）。
                bool roof = v >= 0.08f && v < 0.34f && Mathf.Abs(u - 0.5f) < 0.46f * (v - 0.08f) / 0.26f;
                bool beam = v >= 0.34f && v < 0.42f && u > 0.08f && u < 0.92f;
                bool column = v >= 0.42f && v < 0.8f && u > 0.14f && u < 0.86f && Mathf.Repeat((u - 0.14f) / 0.72f * 4f, 1f) < 0.5f;
                bool step1 = v >= 0.8f && v < 0.87f && u > 0.08f && u < 0.92f;
                bool step2 = v >= 0.87f && v < 0.95f && u > 0.02f && u < 0.98f;
                return roof || beam || column || step1 || step2 ? color : Color.clear;
            });
        }

        /// <summary>100 枚のコイン: 虹色の輪のコイン（中は金色）。</summary>
        public static Texture2D RainbowCoin(int size)
        {
            return Draw("IconRainbowCoin", size, (u, v) =>
            {
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 0.47f)
                {
                    return Color.clear;
                }
                if (r > 0.3f)
                {
                    float hue = Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f), 1f);
                    return Color.HSVToRGB(hue, 0.75f, 1f);
                }
                return r > 0.26f ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 0.82f, 0.2f, 1f);
            });
        }

        /// <summary>王冠（1 位の印）: 3 つの山と玉、土台。金色。</summary>
        public static Texture2D Crown(int size)
        {
            var gold = new Color(1f, 0.82f, 0.18f, 1f);
            var jewel = new Color(1f, 0.35f, 0.3f, 1f);
            return Draw("IconCrown", size, (u, v) =>
            {
                bool band = v >= 0.68f && v < 0.88f && u > 0.12f && u < 0.88f;
                // 山: 0.15〜0.85 を 3 つに分け、それぞれ中央が高い三角。
                float x = (u - 0.12f) / 0.76f;
                bool peaks = false;
                if (x >= 0f && x <= 1f && v >= 0.25f && v < 0.68f)
                {
                    float local = Mathf.Repeat(x * 3f, 1f);
                    float height = 1f - Mathf.Abs(local - 0.5f) * 2f;
                    peaks = (0.68f - v) / 0.43f < height;
                }
                bool ball = false;
                for (int i = 0; i < 3; i++)
                {
                    float cx = 0.12f + 0.76f * (i + 0.5f) / 3f;
                    float dx = u - cx;
                    float dy = v - 0.2f;
                    ball |= dx * dx + dy * dy < 0.007f;
                }
                float jx = u - 0.5f;
                float jy = v - 0.78f;
                if (jx * jx + jy * jy < 0.004f)
                {
                    return jewel;
                }
                return band || peaks || ball ? gold : Color.clear;
            });
        }

        // 1 ピクセルずつ色を決めて描く（縁は 4 点の平均で少しなめらかにする）。
        private static Texture2D Draw(string name, int size, System.Func<float, float, Color> color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[size * size];
            float step = 1f / size;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 色は不透明さで重みをつけて平均する（縁が黒ずまないように）。
                    Color sum = Color.clear;
                    float alpha = 0f;
                    for (int s = 0; s < 4; s++)
                    {
                        float u = (x + 0.25f + 0.5f * (s % 2)) * step;
                        float v = (y + 0.25f + 0.5f * (s / 2)) * step;
                        Color c = color(u, v);
                        sum += c * c.a;
                        alpha += c.a;
                    }
                    Color pixel = alpha > 0f ? sum / alpha : Color.clear;
                    pixel.a = alpha * 0.25f;
                    // テクスチャは下の行から並ぶので、上下を反転する。
                    pixels[(size - 1 - y) * size + x] = pixel;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
