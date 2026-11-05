using UnityEngine;

namespace Airerage.Runtime.Presentation
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

        /// <summary>キャラクターの絵の代わり（今は仮キャラクターなので、プレイヤーの色の四角に濃い縁）。</summary>
        public static Texture2D Square(int size, Color color)
        {
            Color edge = Color.Lerp(color, Color.black, 0.45f);
            return Draw("IconSquare", size, (u, v) =>
            {
                if (u < 0.08f || u > 0.92f || v < 0.08f || v > 0.92f)
                {
                    return Color.clear;
                }
                return u < 0.18f || u > 0.82f || v < 0.18f || v > 0.82f ? edge : color;
            });
        }

        /// <summary>ホットエリアのフルーツ（ユーザーの要望、2026-11-02）: 橙色の丸い実に、白いつやと茶色のへた、緑の葉。</summary>
        public static Texture2D Fruit(int size)
        {
            var body = new Color(1f, 0.5f, 0.12f, 1f);
            var shade = new Color(0.86f, 0.32f, 0.08f, 1f);
            var shine = new Color(1f, 0.92f, 0.75f, 1f);
            var stem = new Color(0.42f, 0.26f, 0.12f, 1f);
            var leaf = new Color(0.35f, 0.85f, 0.3f, 1f);
            return Draw("IconFruit", size, (u, v) =>
            {
                // 葉（右上へ伸びる楕円）。
                float lx = (u - 0.64f) * 0.8f + (v - 0.2f) * 0.6f;
                float ly = -(u - 0.64f) * 0.6f + (v - 0.2f) * 0.8f;
                if ((lx / 0.17f) * (lx / 0.17f) + (ly / 0.08f) * (ly / 0.08f) < 1f)
                {
                    return leaf;
                }
                // へた。
                if (u > 0.47f && u < 0.54f && v > 0.12f && v < 0.3f)
                {
                    return stem;
                }
                float dx = u - 0.5f;
                float dy = v - 0.6f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 0.36f)
                {
                    return Color.clear;
                }
                float sx = u - 0.38f;
                float sy = v - 0.48f;
                if (sx * sx + sy * sy < 0.006f)
                {
                    return shine;
                }
                return r > 0.3f ? shade : body;
            });
        }

        /// <summary>
        /// WANTED の手配書（ユーザーの要望、2026-11-02）: 古い紙（焼けた縁）の上に、朱色の「WANTED」の帯（文字は描く側が重ねる）、
        /// 茶色の人の影、下に賞金の線。幅 width・高さ height。
        /// </summary>
        public static Texture2D WantedPoster(int width, int height)
        {
            var paper = new Color(0.95f, 0.87f, 0.66f, 1f);
            var burnt = new Color(0.62f, 0.42f, 0.2f, 1f);
            var band = new Color(0.78f, 0.12f, 0.08f, 1f);
            var ink = new Color(0.36f, 0.22f, 0.12f, 1f);
            return Draw("IconWantedPoster", width, height, (u, v) =>
            {
                if (u < 0.03f || u > 0.97f || v < 0.02f || v > 0.98f)
                {
                    return Color.clear;
                }
                float edge = Mathf.Min(Mathf.Min(u - 0.03f, 0.97f - u) * 1.3f, Mathf.Min(v - 0.02f, 0.98f - v));
                if (edge < 0.05f)
                {
                    return burnt;
                }
                if (v > 0.08f && v < 0.3f && u > 0.1f && u < 0.9f)
                {
                    return band;
                }
                // 人の影（頭と肩）。
                float hx = (u - 0.5f) / 0.15f;
                float hy = (v - 0.47f) / 0.11f;
                bool head = hx * hx + hy * hy < 1f;
                float sx = (u - 0.5f) / 0.32f;
                float sy = (v - 0.76f) / 0.17f;
                bool shoulders = v < 0.76f && sx * sx + sy * sy < 1f && v > 0.6f;
                if (head || shoulders)
                {
                    return ink;
                }
                if (v > 0.83f && v < 0.88f && u > 0.2f && u < 0.8f)
                {
                    return ink;
                }
                return Color.Lerp(paper, burnt, Mathf.Clamp01((0.12f - edge) / 0.12f) * 0.35f);
            });
        }

        /// <summary>撃破ドロップのかたまり: 金色のコインが 3 枚重なった山。</summary>
        public static Texture2D CoinPile(int size)
        {
            var gold = new Color(1f, 0.8f, 0.18f, 1f);
            var rim = new Color(0.8f, 0.55f, 0.08f, 1f);
            return Draw("IconCoinPile", size, (u, v) =>
            {
                // 下の2枚と上の1枚（横長の楕円）。上に描いたものを優先する。
                float[,] coins = { { 0.5f, 0.38f }, { 0.32f, 0.66f }, { 0.68f, 0.66f } };
                for (int i = 0; i < 3; i++)
                {
                    float dx = (u - coins[i, 0]) / 0.27f;
                    float dy = (v - coins[i, 1]) / 0.2f;
                    float d = dx * dx + dy * dy;
                    if (d < 1f)
                    {
                        return d > 0.6f ? rim : gold;
                    }
                }
                return Color.clear;
            });
        }

        /// <summary>撃破の衝撃（キルログ。ユーザーの要望、2026-10-30）: ぎざぎざの赤い衝撃波と、真ん中の橙から黄色の光。</summary>
        public static Texture2D Burst(int size)
        {
            var red = new Color(1f, 0.18f, 0.12f, 1f);
            var orange = new Color(1f, 0.55f, 0.15f, 1f);
            var yellow = new Color(1f, 0.92f, 0.45f, 1f);
            return Draw("IconBurst", size, (u, v) =>
            {
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // 10 本のとげ（長いとげと短いとげが交互）。
                float angle = Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.025f, 1f) * 10f;
                float spike = 1f - Mathf.Abs(Mathf.Repeat(angle, 1f) - 0.5f) * 2f;
                float tip = Mathf.FloorToInt(angle) % 2 == 0 ? 0.49f : 0.4f;
                float edge = Mathf.Lerp(0.27f, tip, spike);
                if (r > edge)
                {
                    return Color.clear;
                }
                return r < edge * 0.42f ? yellow : r < edge * 0.68f ? orange : red;
            });
        }

        /// <summary>倒れた人（キルログ。撃破された人）: 仰向けに倒れた人の形（左に頭、右へ体と曲げた脚、上へ伸びた腕）と、下の地面の線。</summary>
        public static Texture2D Fallen(int size, Color color)
        {
            return Draw("IconFallen", size, (u, v) =>
            {
                float dx = u - 0.17f;
                float dy = v - 0.6f;
                bool head = dx * dx + dy * dy < 0.0105f;
                bool body = Segment(u, v, 0.3f, 0.63f, 0.62f, 0.63f, 0.065f);
                bool arm = Segment(u, v, 0.4f, 0.6f, 0.5f, 0.36f, 0.045f);
                bool thigh = Segment(u, v, 0.62f, 0.63f, 0.76f, 0.47f, 0.05f);
                bool shin = Segment(u, v, 0.76f, 0.47f, 0.92f, 0.62f, 0.05f);
                bool ground = v > 0.8f && v < 0.86f && u > 0.04f && u < 0.96f;
                return head || body || arm || thigh || shin || ground ? color : Color.clear;
            });
        }

        /// <summary>右向きの矢印（キルログの「撃破した人 → 撃破された人」）。</summary>
        public static Texture2D Arrow(int size, Color color)
        {
            return Draw("IconArrow", size, (u, v) =>
            {
                bool shaft = u > 0.08f && u < 0.6f && Mathf.Abs(v - 0.5f) < 0.09f;
                bool head = u >= 0.5f && u < 0.94f && Mathf.Abs(v - 0.5f) < 0.34f * (0.94f - u) / 0.44f;
                return shaft || head ? color : Color.clear;
            });
        }

        /// <summary>
        /// 退出（結果の画面の EXIT。ユーザーの要望、2026-11-03）: 左向きに開いたドア（枠と、左の端を軸に手前へ開いた扉）と、その右に
        /// 戸口から出ていく右向きの矢印。矢印が通る所は枠の右の線を切る。
        /// </summary>
        public static Texture2D Exit(int size, Color color)
        {
            return Draw("IconExit", size, (u, v) =>
            {
                const float stroke = 0.055f;
                const float left = 0.08f;
                const float right = 0.54f;
                const float top = 0.08f;
                const float bottom = 0.9f;
                bool arrowBand = Mathf.Abs(v - 0.5f) < 0.17f;
                bool frameTop = v >= top && v < top + stroke && u >= left && u <= right;
                bool frameBottom = v > bottom - stroke && v <= bottom && u >= left && u <= right;
                bool frameRight = u > right - stroke && u <= right && v >= top && v <= bottom && !arrowBand;
                bool frameLeft = u >= left && u < left + stroke && v >= top && v <= bottom;
                // 扉: 左の端（枠の左の線）を軸に手前へ開き、右の端は短く下がって見える台形。取っ手のところは抜く。
                bool door = false;
                if (u >= left && u <= 0.33f)
                {
                    float t = (u - left) / (0.33f - left);
                    float doorTop = Mathf.Lerp(top, 0.2f, t);
                    float doorBottom = Mathf.Lerp(bottom, 0.98f, t);
                    door = v >= doorTop && v <= doorBottom;
                }
                float kx = u - 0.27f;
                float ky = v - 0.6f;
                bool knob = kx * kx + ky * ky < 0.0011f;
                bool shaft = u > 0.38f && u < 0.78f && Mathf.Abs(v - 0.5f) < 0.06f;
                bool head = u >= 0.72f && u < 0.98f && Mathf.Abs(v - 0.5f) < 0.2f * (0.98f - u) / 0.26f;
                if (knob)
                {
                    return Color.clear;
                }
                return frameTop || frameBottom || frameRight || frameLeft || door || shaft || head ? color : Color.clear;
            });
        }

        /// <summary>
        /// 自分（待機所の札の「YOU」の代わり。ユーザーの要望、2026-11-03）: 丸い枠の中の、頭と肩の人の形。
        /// </summary>
        public static Texture2D Person(int size, Color color)
        {
            return Draw("IconPerson", size, (u, v) =>
            {
                float dx = u - 0.5f;
                float dy = v - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 0.48f)
                {
                    return Color.clear;
                }
                if (r > 0.4f)
                {
                    return color;
                }
                float hx = u - 0.5f;
                float hy = v - 0.36f;
                bool head = hx * hx + hy * hy < 0.0225f;
                // 肩: 上が丸く、下は丸い枠まで伸びる（枠で切れる）。
                float sx = (u - 0.5f) / 0.27f;
                float sy = (v - 0.84f) / 0.27f;
                bool shoulders = v > 0.57f && (v >= 0.84f ? Mathf.Abs(u - 0.5f) < 0.27f : sx * sx + sy * sy < 1f);
                return head || shoulders ? color : Color.clear;
            });
        }

        // 点 (u, v) が、(x0, y0) から (x1, y1) への太さ radius × 2 の線の中か。
        private static bool Segment(float u, float v, float x0, float y0, float x1, float y1, float radius)
        {
            var p = new Vector2(u, v);
            var a = new Vector2(x0, y0);
            Vector2 ab = new Vector2(x1, y1) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).sqrMagnitude < radius * radius;
        }

        /// <summary>
        /// ひび割れ（白い線。札の上に重ねる）。中心の近くから枝分かれして広がる線を、種を決めた乱数で描く（毎回同じ形）。
        /// </summary>
        public static Texture2D Crack(int width, int height, int seed)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Crack",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            var random = new System.Random(seed);
            var start = new Vector2(width * (0.45f + 0.1f * (float)random.NextDouble()), height * 0.5f);
            for (int branch = 0; branch < 7; branch++)
            {
                float angle = branch / 7f * Mathf.PI * 2f + (float)random.NextDouble() * 0.6f;
                Vector2 point = start;
                int segments = 3 + random.Next(3);
                for (int s = 0; s < segments; s++)
                {
                    angle += ((float)random.NextDouble() - 0.5f) * 1.1f;
                    float length = height * (0.25f + 0.35f * (float)random.NextDouble()) * (branch % 2 == 0 ? 1.6f : 1f);
                    Vector2 next = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * length;
                    Line(pixels, width, height, point, next, s == 0 ? 1.6f : 1.1f);
                    point = next;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // 太さ thickness の線を、白で塗る（重なったところは濃いほうを残す）。
        private static void Line(Color[] pixels, int width, int height, Vector2 from, Vector2 to, float thickness)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)steps);
                int r = Mathf.CeilToInt(thickness);
                for (int y = (int)p.y - r; y <= (int)p.y + r; y++)
                {
                    for (int x = (int)p.x - r; x <= (int)p.x + r; x++)
                    {
                        if (x < 0 || x >= width || y < 0 || y >= height)
                        {
                            continue;
                        }
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), p);
                        float a = Mathf.Clamp01(thickness - d + 0.5f);
                        int index = y * width + x;
                        if (a > pixels[index].a)
                        {
                            pixels[index] = new Color(1f, 1f, 1f, a);
                        }
                    }
                }
            }
        }

        // 1 ピクセルずつ色を決めて描く（縁は 4 点の平均で少しなめらかにする）。
        private static Texture2D Draw(string name, int size, System.Func<float, float, Color> color)
        {
            return Draw(name, size, size, color);
        }

        // 幅 width・高さ height の絵（u, v は 0〜1。v は上が 0）。
        private static Texture2D Draw(string name, int width, int height, System.Func<float, float, Color> color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            float stepX = 1f / width;
            float stepY = 1f / height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 色は不透明さで重みをつけて平均する（縁が黒ずまないように）。
                    Color sum = Color.clear;
                    float alpha = 0f;
                    for (int s = 0; s < 4; s++)
                    {
                        float u = (x + 0.25f + 0.5f * (s % 2)) * stepX;
                        float v = (y + 0.25f + 0.5f * (s / 2)) * stepY;
                        Color c = color(u, v);
                        sum += c * c.a;
                        alpha += c.a;
                    }
                    Color pixel = alpha > 0f ? sum / alpha : Color.clear;
                    pixel.a = alpha * 0.25f;
                    // テクスチャは下の行から並ぶので、上下を反転する。
                    pixels[(height - 1 - y) * width + x] = pixel;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
