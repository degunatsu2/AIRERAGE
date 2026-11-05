using System.Collections.Generic;
using Airerage.Core.Items;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// アイテムの絵（ユーザーの案の見た目。docs/20_item_plan.md）をコードで描いて作る（仮の絵。本番の絵ができたら差し替える）。
    /// アイテムごとに 1 枚を作って使い回す（HideAndDontSave。ゲームを終えるまで残す）。
    /// - ブースター: ダッシュの線（左へ流れる 3 本）が付いた文字（Q 水色・E 緑・R 赤。極は金色の文字と、金の縁の線）。
    /// - 光子の盾: エネルギーでできた水色の盾。マガジンマガジン: 雑誌のような本。グリーンバナナ: 緑のバナナ。ダイアモンドアレイ: ダイアの鉄アレイ。
    /// - エネルギーショット: 青い炎をまとった光弾。櫓落とし: 細長い臙脂色の刀身の刀。イヤーボーン: 耳の形の骨。イカロスウイング: 蝋の羽。
    ///   砂岩ボール: 砂岩のサッカーボールを描いた四角いアイコン。
    /// - 追加の 25 個（2026-11-05）は ItemIcons.More.cs。
    /// </summary>
    public static partial class ItemIcons
    {
        public const int Size = 64;

        private static readonly Dictionary<ItemId, Texture2D> Cache = new Dictionary<ItemId, Texture2D>();

        private static readonly Color Gold = new Color(1f, 0.82f, 0.2f, 1f);
        private static readonly Color GoldDark = new Color(0.75f, 0.52f, 0.08f, 1f);

        /// <summary>アイテム id の絵（なしなら null）。</summary>
        public static Texture2D Get(ItemId id)
        {
            if (!ItemCatalog.IsValid(id))
            {
                return null;
            }
            if (Cache.TryGetValue(id, out Texture2D texture) && texture != null)
            {
                return texture;
            }
            texture = Draw(id);
            Cache[id] = texture;
            return texture;
        }

        /// <summary>アイテム id の主な色（拾ったときの光、落ちているアイテムの縁）。</summary>
        public static Color Accent(ItemId id)
        {
            switch (id)
            {
                case ItemId.QuickBooster:
                    return new Color(0.45f, 0.85f, 1f);
                case ItemId.EnergyBooster:
                    return new Color(0.4f, 0.95f, 0.45f);
                case ItemId.UltimateBooster:
                    return new Color(1f, 0.35f, 0.35f);
                case ItemId.QuickBoosterEx:
                case ItemId.EnergyBoosterEx:
                case ItemId.UltimateBoosterEx:
                    return Gold;
                case ItemId.PhotonShield:
                    return new Color(0.45f, 0.9f, 1f);
                case ItemId.MagazineMagazine:
                    return new Color(1f, 0.6f, 0.3f);
                case ItemId.GreenBanana:
                    return new Color(0.5f, 0.9f, 0.3f);
                case ItemId.DiamondDumbbell:
                    return new Color(0.75f, 0.95f, 1f);
                case ItemId.EnergyShot:
                    return new Color(0.35f, 0.6f, 1f);
                case ItemId.Yaguraotoshi:
                    return new Color(0.65f, 0.12f, 0.18f);
                case ItemId.EarBone:
                    return new Color(0.95f, 0.92f, 0.82f);
                case ItemId.IcarusWing:
                    return new Color(1f, 0.95f, 0.75f);
                case ItemId.SandstoneBall:
                    return new Color(0.85f, 0.7f, 0.45f);
                default:
                    return AccentMore(id);
            }
        }

        private static Texture2D Draw(ItemId id)
        {
            switch (id)
            {
                case ItemId.QuickBooster:
                    return Booster("ItemQuick", 'Q', new Color(0.45f, 0.85f, 1f), new Color(0.2f, 0.45f, 1f), false);
                case ItemId.EnergyBooster:
                    return Booster("ItemEnergy", 'E', new Color(0.35f, 0.9f, 0.4f), new Color(1f, 0.9f, 0.2f), false);
                case ItemId.UltimateBooster:
                    return Booster("ItemUltimate", 'R', new Color(1f, 0.25f, 0.25f), new Color(1f, 0.5f, 0.75f), false);
                case ItemId.QuickBoosterEx:
                    return Booster("ItemQuickEx", 'Q', Gold, new Color(0.2f, 0.45f, 1f), true);
                case ItemId.EnergyBoosterEx:
                    return Booster("ItemEnergyEx", 'E', Gold, new Color(1f, 0.9f, 0.2f), true);
                case ItemId.UltimateBoosterEx:
                    return Booster("ItemUltimateEx", 'R', Gold, new Color(1f, 0.5f, 0.75f), true);
                case ItemId.PhotonShield:
                    return Shield();
                case ItemId.MagazineMagazine:
                    return Magazine();
                case ItemId.GreenBanana:
                    return Banana();
                case ItemId.DiamondDumbbell:
                    return Dumbbell();
                case ItemId.EnergyShot:
                    return Shot();
                case ItemId.Yaguraotoshi:
                    return Sword();
                case ItemId.EarBone:
                    return EarBone();
                case ItemId.IcarusWing:
                    return Wing();
                case ItemId.SandstoneBall:
                    return Ball();
                default:
                    return DrawMore(id);
            }
        }

        // ---------------------------------------------------------------- 絵

        // ダッシュの線のついた文字（ブースター）。gold なら、線を金の縁で囲む（極）。
        private static Texture2D Booster(string name, char letter, Color letterColor, Color dashColor, bool gold)
        {
            return Paint(name, (u, v) =>
            {
                // 文字（右寄り）。
                float lx = (u - 0.6f) / 0.36f;
                float ly = (v - 0.5f) / 0.4f;
                float letterInk = Letter(letter, lx, ly, 0.16f);
                float outline = Letter(letter, lx, ly, 0.27f);
                if (letterInk > 0f)
                {
                    Color c = letterColor;
                    if (gold)
                    {
                        // 金の文字は上が明るい。
                        c = Color.Lerp(Gold, new Color(1f, 0.96f, 0.7f), Mathf.Clamp01(0.6f - v));
                    }
                    return c;
                }
                if (outline > 0f)
                {
                    return gold ? GoldDark : new Color(0.05f, 0.08f, 0.15f, 1f);
                }
                // ダッシュの線（左へ流れる 3 本。左ほど細く薄い）。
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.32f + i * 0.18f;
                    float x0 = 0.04f + i * 0.04f;
                    float x1 = 0.42f;
                    if (u >= x0 && u <= x1)
                    {
                        float t = (u - x0) / (x1 - x0);
                        float half = 0.012f + 0.035f * t;
                        float d = Mathf.Abs(v - y);
                        if (d < half)
                        {
                            Color c = dashColor;
                            c.a = 0.35f + 0.65f * t;
                            return c;
                        }
                        if (gold && d < half + 0.022f)
                        {
                            Color g = Gold;
                            g.a = 0.4f + 0.6f * t;
                            return g;
                        }
                    }
                }
                return Color.clear;
            });
        }

        // 文字 letter（Q・E・R）を、(-1〜1) の座標 (x, y) で太さ width の線で描く（下が y 正）。重なれば 1。
        private static float Letter(char letter, float x, float y, float width)
        {
            switch (letter)
            {
                case 'Q':
                {
                    float r = Mathf.Sqrt(x * x / 0.7f + y * y);
                    bool ring = Mathf.Abs(r - 0.78f) < width;
                    bool tail = Segment(x, y, 0.25f, 0.45f, 0.8f, 1.05f, width);
                    return ring || tail ? 1f : 0f;
                }
                case 'E':
                {
                    bool spine = Segment(x, y, -0.55f, -0.9f, -0.55f, 0.9f, width);
                    bool top = Segment(x, y, -0.55f, -0.9f, 0.6f, -0.9f, width);
                    bool middle = Segment(x, y, -0.55f, 0f, 0.4f, 0f, width);
                    bool bottom = Segment(x, y, -0.55f, 0.9f, 0.6f, 0.9f, width);
                    return spine || top || middle || bottom ? 1f : 0f;
                }
                case 'R':
                {
                    bool spine = Segment(x, y, -0.55f, -0.9f, -0.55f, 0.9f, width);
                    bool top = Segment(x, y, -0.55f, -0.9f, 0.15f, -0.9f, width);
                    bool middle = Segment(x, y, -0.55f, 0.05f, 0.15f, 0.05f, width);
                    float bx = x - 0.15f;
                    float by = y + 0.425f;
                    bool bowl = bx > 0f && Mathf.Abs(Mathf.Sqrt(bx * bx / 0.25f + by * by / 0.23f) - 1f) < width * 1.6f;
                    bool leg = Segment(x, y, -0.05f, 0.05f, 0.62f, 0.9f, width);
                    return spine || top || middle || bowl || leg ? 1f : 0f;
                }
                default:
                    return 0f;
            }
        }

        private static Texture2D Shield()
        {
            var edge = new Color(0.55f, 0.95f, 1f, 1f);
            return Paint("ItemShield", (u, v) =>
            {
                // 上が平らで下がとがった盾（エネルギーの縁と、透ける中の格子）。
                float x = (u - 0.5f) / 0.4f;
                float y = (v - 0.12f) / 0.8f;
                if (y < 0f || y > 1f)
                {
                    return Color.clear;
                }
                float halfWidth = y < 0.45f ? 1f : Mathf.Lerp(1f, 0f, (y - 0.45f) / 0.55f) + 0.02f;
                float dx = Mathf.Abs(x);
                if (dx > halfWidth)
                {
                    return Color.clear;
                }
                if (halfWidth - dx < 0.14f || y < 0.06f)
                {
                    return edge;
                }
                bool grid = Mathf.Repeat(u * 8f, 1f) < 0.12f || Mathf.Repeat(v * 8f, 1f) < 0.12f;
                return grid ? new Color(0.6f, 0.95f, 1f, 0.7f) : new Color(0.3f, 0.75f, 1f, 0.38f + 0.25f * (1f - y));
            });
        }

        private static Texture2D Magazine()
        {
            return Paint("ItemMagazine", (u, v) =>
            {
                // 少し傾いた雑誌: 表紙（橙）と白い題字の帯、写真の枠、背の影。
                float x = u - 0.5f;
                float y = v - 0.5f;
                float angle = -0.18f;
                float rx = x * Mathf.Cos(angle) - y * Mathf.Sin(angle);
                float ry = x * Mathf.Sin(angle) + y * Mathf.Cos(angle);
                if (Mathf.Abs(rx) > 0.3f || Mathf.Abs(ry) > 0.4f)
                {
                    return Color.clear;
                }
                if (rx < -0.25f)
                {
                    return new Color(0.6f, 0.25f, 0.1f, 1f);
                }
                if (ry < -0.24f && ry > -0.34f && rx > -0.2f && rx < 0.24f)
                {
                    return Color.white;
                }
                if (ry > -0.16f && ry < 0.2f && rx > -0.16f && rx < 0.22f)
                {
                    return Mathf.Abs(ry - 0.02f) < 0.03f ? new Color(1f, 0.9f, 0.4f, 1f) : new Color(0.25f, 0.55f, 0.85f, 1f);
                }
                if (ry > 0.26f && ry < 0.32f && rx > -0.18f && rx < 0.1f)
                {
                    return new Color(1f, 1f, 1f, 0.85f);
                }
                return new Color(1f, 0.55f, 0.25f, 1f);
            });
        }

        private static Texture2D Banana()
        {
            var green = new Color(0.45f, 0.82f, 0.25f, 1f);
            var dark = new Color(0.2f, 0.45f, 0.1f, 1f);
            return Paint("ItemBanana", (u, v) =>
            {
                // 下へ曲がった三日月形（大きい円から小さい円を抜く）と、両端の茶色の先。
                float ax = u - 0.5f;
                float ay = v - 0.18f;
                float outer = ax * ax + ay * ay;
                float bx = u - 0.5f;
                float by = v - 0.02f;
                float inner = bx * bx + by * by;
                bool body = outer < 0.42f * 0.42f && inner > 0.4f * 0.4f && v > 0.32f;
                if (!body)
                {
                    return Color.clear;
                }
                if (u < 0.16f || u > 0.84f)
                {
                    return new Color(0.35f, 0.22f, 0.1f, 1f);
                }
                return Mathf.Abs(Mathf.Sqrt(outer) - 0.36f) < 0.015f ? dark : Color.Lerp(green, new Color(0.7f, 0.95f, 0.4f), Mathf.Clamp01((0.55f - v) * 3f));
            });
        }

        private static Texture2D Dumbbell()
        {
            var diamond = new Color(0.78f, 0.95f, 1f, 1f);
            var facet = new Color(0.55f, 0.82f, 1f, 1f);
            return Paint("ItemDumbbell", (u, v) =>
            {
                // 握り（銀の棒）と、両端のダイア（ひし形の面）。
                bool bar = u > 0.25f && u < 0.75f && Mathf.Abs(v - 0.5f) < 0.06f;
                foreach (float cx in new[] { 0.2f, 0.8f })
                {
                    float dx = Mathf.Abs(u - cx);
                    float dy = Mathf.Abs(v - 0.5f);
                    if (dx / 0.16f + dy / 0.3f < 1f)
                    {
                        bool bright = (u - cx) * (v - 0.5f) < 0f;
                        bool sparkle = dx < 0.02f || dy < 0.02f;
                        return sparkle ? Color.white : bright ? diamond : facet;
                    }
                }
                return bar ? new Color(0.75f, 0.78f, 0.82f, 1f) : Color.clear;
            });
        }

        private static Texture2D Shot()
        {
            return Paint("ItemShot", (u, v) =>
            {
                // 白く光る芯と、上へ揺らぐ青い炎。
                float x = u - 0.5f;
                float y = v - 0.58f;
                float r = Mathf.Sqrt(x * x + y * y);
                if (r < 0.14f)
                {
                    return Color.Lerp(Color.white, new Color(0.7f, 0.85f, 1f), r / 0.14f);
                }
                float flame = 0.3f - (0.58f - v) * 0.55f + 0.04f * Mathf.Sin(u * 30f + v * 12f);
                if (Mathf.Abs(x) < flame * (v < 0.58f ? 0.7f : 1f) && v > 0.08f && r < 0.36f + (0.58f - v) * 0.3f)
                {
                    float t = Mathf.Clamp01(r / 0.4f);
                    Color c = Color.Lerp(new Color(0.4f, 0.75f, 1f), new Color(0.15f, 0.3f, 1f), t);
                    c.a = 1f - t * 0.4f;
                    return c;
                }
                return Color.clear;
            });
        }

        private static Texture2D Sword()
        {
            var blade = new Color(0.55f, 0.08f, 0.14f, 1f);
            var edge = new Color(0.85f, 0.35f, 0.4f, 1f);
            return Paint("ItemSword", (u, v) =>
            {
                // 左下から右上への刀: 細長い臙脂色の刀身、金の鍔、黒い柄。
                float t = (u + (1f - v)) * 0.5f;
                float d = (u - (1f - v)) * 0.7071f;
                if (t > 0.3f && t < 0.95f && Mathf.Abs(d) < 0.035f)
                {
                    return d > 0.01f ? edge : blade;
                }
                if (t > 0.26f && t < 0.3f && Mathf.Abs(d) < 0.09f)
                {
                    return Gold;
                }
                if (t > 0.06f && t < 0.26f && Mathf.Abs(d) < 0.045f)
                {
                    bool wrap = Mathf.Repeat(t * 40f, 1f) < 0.5f;
                    return wrap ? new Color(0.12f, 0.1f, 0.12f, 1f) : new Color(0.3f, 0.22f, 0.2f, 1f);
                }
                return Color.clear;
            });
        }

        private static Texture2D EarBone()
        {
            var bone = new Color(0.95f, 0.92f, 0.82f, 1f);
            var shade = new Color(0.75f, 0.7f, 0.58f, 1f);
            return Paint("ItemEarBone", (u, v) =>
            {
                // 耳の形（C の字の外の縁と、内側の渦）。
                float x = (u - 0.48f) / 0.36f;
                float y = (v - 0.48f) / 0.42f;
                float r = Mathf.Sqrt(x * x + y * y);
                float angle = Mathf.Atan2(y, x);
                bool outer = r > 0.72f && r < 1f && !(angle > -0.45f && angle < 0.55f && x > 0f);
                float ix = (u - 0.5f) / 0.18f;
                float iy = (v - 0.5f) / 0.22f;
                float ir = Mathf.Sqrt(ix * ix + iy * iy);
                bool inner = ir > 0.55f && ir < 1f && Mathf.Atan2(iy, ix) < 1.5f;
                bool lobe = Segment(u, v, 0.45f, 0.86f, 0.62f, 0.88f, 0.07f);
                if (outer || lobe)
                {
                    return r > 0.92f ? shade : bone;
                }
                return inner ? shade : Color.clear;
            });
        }

        private static Texture2D Wing()
        {
            var wax = new Color(1f, 0.95f, 0.75f, 1f);
            var drip = new Color(0.95f, 0.8f, 0.45f, 1f);
            return Paint("ItemWing", (u, v) =>
            {
                // 羽ばたく形: 上の縁から右下へ並ぶ羽根（だんだん短く）と、溶けて垂れる蝋。
                if (Segment(u, v, 0.12f, 0.3f, 0.85f, 0.18f, 0.05f))
                {
                    return drip;
                }
                for (int i = 0; i < 5; i++)
                {
                    float x0 = 0.2f + i * 0.14f;
                    float length = 0.55f - i * 0.07f;
                    if (Segment(u, v, x0, 0.25f, x0 - 0.06f, 0.25f + length, 0.06f))
                    {
                        return Mathf.Repeat((u + v) * 9f, 1f) < 0.15f ? drip : wax;
                    }
                }
                bool dropOfWax = Segment(u, v, 0.3f, 0.82f, 0.3f, 0.9f, 0.025f) || Segment(u, v, 0.58f, 0.7f, 0.58f, 0.78f, 0.022f);
                return dropOfWax ? drip : Color.clear;
            });
        }

        private static Texture2D Ball()
        {
            var sand = new Color(0.86f, 0.72f, 0.48f, 1f);
            var sandDark = new Color(0.6f, 0.45f, 0.28f, 1f);
            return Paint("ItemBall", (u, v) =>
            {
                // 四角いアイコン（暗い枠）に、砂岩の色のサッカーボール（五角形の模様と、ざらざらの点）。
                if (u < 0.04f || u > 0.96f || v < 0.04f || v > 0.96f)
                {
                    return new Color(0.25f, 0.2f, 0.15f, 1f);
                }
                float x = u - 0.5f;
                float y = v - 0.5f;
                float r = Mathf.Sqrt(x * x + y * y);
                if (r > 0.38f)
                {
                    return new Color(0.45f, 0.62f, 0.85f, 1f);
                }
                if (r > 0.355f)
                {
                    return sandDark;
                }
                float angle = Mathf.Atan2(y, x);
                bool center = r < 0.11f && Mathf.Cos(5f * (angle + 0.3f)) * 0.02f + 0.1f > r;
                bool patch = false;
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2f / 5f + 0.3f;
                    float px = Mathf.Cos(a) * 0.28f;
                    float py = Mathf.Sin(a) * 0.28f;
                    patch |= (x - px) * (x - px) + (y - py) * (y - py) < 0.006f;
                }
                bool grain = Mathf.Repeat(Mathf.Sin(u * 211f) * 43758.5f + Mathf.Sin(v * 157f) * 12345.6f, 1f) < 0.08f;
                return center || patch ? sandDark : grain ? Color.Lerp(sand, sandDark, 0.4f) : sand;
            });
        }

        // ---------------------------------------------------------------- 描く道具

        // 点 (u, v) が、(x0, y0) から (x1, y1) への太さ radius × 2 の線の中か。
        private static bool Segment(float u, float v, float x0, float y0, float x1, float y1, float radius)
        {
            var p = new Vector2(u, v);
            var a = new Vector2(x0, y0);
            Vector2 ab = new Vector2(x1, y1) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (p - (a + ab * t)).sqrMagnitude < radius * radius;
        }

        // 1 ピクセルずつ色を決めて描く（4 点の平均で縁をなめらかにする。u, v は 0〜1、v は上が 0）。
        private static Texture2D Paint(string name, System.Func<float, float, Color> color)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[Size * Size];
            float step = 1f / Size;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
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
                    pixel.a = alpha / 4f;
                    // テクスチャは下の行から（v は上が 0）。
                    pixels[(Size - 1 - y) * Size + x] = pixel;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
