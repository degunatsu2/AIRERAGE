using Airerage.Core.Items;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 追加の 25 個のアイテムの仮の絵（ユーザーが選んだ案、2026-11-05。docs/20_item_plan.md の見た目）。描き方は ItemIcons と同じ
    /// （u, v は 0〜1、v は上が 0）。
    /// </summary>
    public static partial class ItemIcons
    {
        private static readonly Color Silver = new Color(0.82f, 0.85f, 0.9f, 1f);
        private static readonly Color SilverDark = new Color(0.5f, 0.54f, 0.6f, 1f);
        private static readonly Color Ink = new Color(0.08f, 0.08f, 0.1f, 1f);

        private static Texture2D _random;

        /// <summary>ランダムセレクトの絵（虹色の縁のさいころと「?」）。</summary>
        public static Texture2D RandomIcon()
        {
            if (_random != null)
            {
                return _random;
            }
            _random = Paint("ItemRandom", (u, v) =>
            {
                float x = Mathf.Abs(u - 0.5f);
                float y = Mathf.Abs(v - 0.5f);
                float box = Mathf.Max(x, y);
                if (box > 0.4f)
                {
                    return Color.clear;
                }
                if (box > 0.34f)
                {
                    return Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(v - 0.5f, u - 0.5f) / (Mathf.PI * 2f), 1f), 0.65f, 1f);
                }
                // 「?」: 上の弧、右下への線、点。
                float r = Ellipse(u, v, 0.5f, 0.38f, 0.14f, 0.12f);
                bool hook = r > 0.62f && r < 1.25f && !(u < 0.5f && v > 0.38f);
                bool stem = Segment(u, v, 0.55f, 0.48f, 0.5f, 0.6f, 0.04f);
                bool dot = Ellipse(u, v, 0.5f, 0.72f, 0.05f, 0.05f) < 1f;
                return hook || stem || dot ? Color.white : new Color(0.12f, 0.14f, 0.22f, 1f);
            });
            return _random;
        }

        private static Color AccentMore(ItemId id)
        {
            switch (id)
            {
                case ItemId.SkyBoots:
                    return new Color(0.9f, 0.95f, 1f);
                case ItemId.GoldMagnet:
                case ItemId.VaultKey:
                case ItemId.GoldBell:
                    return Gold;
                case ItemId.IronBoots:
                    return new Color(0.6f, 0.64f, 0.7f);
                case ItemId.SteelWire:
                case ItemId.LongHook:
                case ItemId.Reflector:
                case ItemId.ChainHook:
                    return Silver;
                case ItemId.SpareBattery:
                case ItemId.BottledThunder:
                    return new Color(1f, 0.9f, 0.25f);
                case ItemId.PhoenixFeather:
                    return new Color(1f, 0.45f, 0.15f);
                case ItemId.AdversityCharm:
                case ItemId.BigMagnet:
                case ItemId.PocketRocket:
                    return new Color(1f, 0.3f, 0.3f);
                case ItemId.SpikeSoles:
                    return new Color(0.7f, 0.9f, 1f);
                case ItemId.FeatherHat:
                    return new Color(0.75f, 0.55f, 1f);
                case ItemId.GrappleBomb:
                    return new Color(1f, 0.55f, 0.2f);
                case ItemId.FoldingPlatform:
                    return new Color(1f, 0.85f, 0.25f);
                case ItemId.ReturnRemote:
                    return new Color(0.5f, 1f, 0.75f);
                case ItemId.Hourglass:
                    return new Color(0.45f, 0.7f, 1f);
                case ItemId.BubbleBarrier:
                    return new Color(0.75f, 0.9f, 1f);
                case ItemId.PocketTrampoline:
                    return new Color(0.35f, 0.75f, 1f);
                case ItemId.DecoyDoll:
                    return new Color(0.95f, 0.85f, 0.55f);
                case ItemId.BankSeal:
                    return new Color(0.95f, 0.3f, 0.2f);
                default:
                    return Color.white;
            }
        }

        private static Texture2D DrawMore(ItemId id)
        {
            switch (id)
            {
                case ItemId.SkyBoots:
                    return SkyBoots();
                case ItemId.GoldMagnet:
                    return Magnet("ItemGoldMagnet", Gold, GoldDark, Silver);
                case ItemId.VaultKey:
                    return Key();
                case ItemId.IronBoots:
                    return IronBoots();
                case ItemId.SteelWire:
                    return Coil();
                case ItemId.LongHook:
                    return Harpoon();
                case ItemId.SpareBattery:
                    return Battery();
                case ItemId.GoldBell:
                    return Bell();
                case ItemId.PhoenixFeather:
                    return Feather("ItemPhoenix", new Color(1f, 0.25f, 0.12f, 1f), new Color(1f, 0.7f, 0.15f, 1f));
                case ItemId.AdversityCharm:
                    return Charm();
                case ItemId.SpikeSoles:
                    return SpikeSole();
                case ItemId.FeatherHat:
                    return Hat();
                case ItemId.GrappleBomb:
                    return Bomb();
                case ItemId.Reflector:
                    return Hexagon();
                case ItemId.FoldingPlatform:
                    return FoldingBoard();
                case ItemId.BigMagnet:
                    return Magnet("ItemBigMagnet", new Color(0.9f, 0.15f, 0.15f, 1f), new Color(0.55f, 0.05f, 0.08f, 1f), Silver);
                case ItemId.ReturnRemote:
                    return Remote();
                case ItemId.Hourglass:
                    return HourglassIcon();
                case ItemId.ChainHook:
                    return Chain();
                case ItemId.BubbleBarrier:
                    return Bubble();
                case ItemId.PocketRocket:
                    return Rocket();
                case ItemId.PocketTrampoline:
                    return Trampoline();
                case ItemId.DecoyDoll:
                    return Doll();
                case ItemId.BottledThunder:
                    return Bottle();
                case ItemId.BankSeal:
                    return Seal();
                default:
                    return null;
            }
        }

        // 楕円 (cx, cy, rx, ry) の中での正規化した距離（1 で縁）。
        private static float Ellipse(float u, float v, float cx, float cy, float rx, float ry)
        {
            float x = (u - cx) / rx;
            float y = (v - cy) / ry;
            return Mathf.Sqrt(x * x + y * y);
        }

        private static bool Box(float u, float v, float x0, float y0, float x1, float y1)
        {
            return u >= x0 && u <= x1 && v >= y0 && v <= y1;
        }

        // 白い翼の付いたスニーカー。
        private static Texture2D SkyBoots()
        {
            return Paint("ItemSkyBoots", (u, v) =>
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.2f + i * 0.09f;
                    if (Segment(u, v, 0.42f, 0.45f, 0.12f + i * 0.05f, y, 0.045f))
                    {
                        return Mathf.Abs(v - y) < 0.01f ? new Color(0.75f, 0.85f, 0.95f, 1f) : Color.white;
                    }
                }
                if (Box(u, v, 0.18f, 0.72f, 0.88f, 0.8f))
                {
                    return new Color(0.4f, 0.65f, 1f, 1f);
                }
                bool upper = Box(u, v, 0.28f, 0.42f, 0.55f, 0.72f) || (Ellipse(u, v, 0.66f, 0.72f, 0.24f, 0.18f) < 1f && v < 0.72f);
                if (upper)
                {
                    bool lace = Mathf.Abs(u - 0.5f) < 0.05f && Mathf.Repeat(v * 20f, 1f) < 0.35f && v < 0.66f;
                    return lace ? new Color(0.4f, 0.65f, 1f, 1f) : new Color(0.95f, 0.96f, 1f, 1f);
                }
                return Color.clear;
            });
        }

        // U 字の磁石（両端は別の色）。
        private static Texture2D Magnet(string name, Color body, Color shade, Color tip)
        {
            return Paint(name, (u, v) =>
            {
                float r = Ellipse(u, v, 0.5f, 0.52f, 0.32f, 0.32f);
                bool arc = v > 0.52f && r < 1f && r > 0.45f;
                bool arms = (Box(u, v, 0.18f, 0.14f, 0.355f, 0.52f) || Box(u, v, 0.645f, 0.14f, 0.82f, 0.52f));
                if (!arc && !arms)
                {
                    return Color.clear;
                }
                if (v < 0.28f)
                {
                    return tip;
                }
                return u < 0.3f || r > 0.9f ? shade : body;
            });
        }

        private static Texture2D Key()
        {
            return Paint("ItemKey", (u, v) =>
            {
                float r = Ellipse(u, v, 0.3f, 0.5f, 0.18f, 0.18f);
                bool ring = r < 1f && r > 0.5f;
                bool shaft = Box(u, v, 0.45f, 0.46f, 0.88f, 0.54f);
                bool teeth = Box(u, v, 0.72f, 0.54f, 0.78f, 0.68f) || Box(u, v, 0.82f, 0.54f, 0.88f, 0.64f);
                if (ring || shaft || teeth)
                {
                    return v < 0.49f ? new Color(1f, 0.95f, 0.6f, 1f) : Gold;
                }
                return Color.clear;
            });
        }

        // 重い鉄のブーツ（鋲の並んだ L 字）。
        private static Texture2D IronBoots()
        {
            var iron = new Color(0.45f, 0.48f, 0.54f, 1f);
            return Paint("ItemIronBoots", (u, v) =>
            {
                bool shaft = Box(u, v, 0.25f, 0.1f, 0.55f, 0.65f);
                bool foot = Box(u, v, 0.25f, 0.6f, 0.85f, 0.82f) && Ellipse(u, v, 0.62f, 0.82f, 0.3f, 0.26f) < 1f || Box(u, v, 0.25f, 0.6f, 0.6f, 0.82f);
                bool sole = Box(u, v, 0.22f, 0.82f, 0.88f, 0.9f);
                if (sole)
                {
                    return Ink;
                }
                if (!shaft && !foot)
                {
                    return Color.clear;
                }
                bool rivet = (Mathf.Abs(u - 0.32f) < 0.03f || Mathf.Abs(u - 0.48f) < 0.03f) && Mathf.Repeat(v * 6f, 1f) < 0.25f && v < 0.6f;
                bool band = Mathf.Abs(v - 0.2f) < 0.03f;
                return rivet ? Silver : band ? SilverDark : u < 0.32f ? new Color(0.35f, 0.38f, 0.44f, 1f) : iron;
            });
        }

        // 巻いた銀色のワイヤーの束。
        private static Texture2D Coil()
        {
            return Paint("ItemSteelWire", (u, v) =>
            {
                float r = Ellipse(u, v, 0.5f, 0.5f, 0.38f, 0.3f);
                if (r > 1f || r < 0.35f)
                {
                    return Segment(u, v, 0.82f, 0.55f, 0.95f, 0.85f, 0.025f) ? Silver : Color.clear;
                }
                bool groove = Mathf.Repeat(r * 6f, 1f) < 0.3f;
                return groove ? SilverDark : Color.Lerp(Color.white, Silver, r);
            });
        }

        // 長い銛の先（返しの付いた矢じりと長い柄）。
        private static Texture2D Harpoon()
        {
            return Paint("ItemLongHook", (u, v) =>
            {
                if (Segment(u, v, 0.1f, 0.9f, 0.62f, 0.38f, 0.035f))
                {
                    return new Color(0.55f, 0.4f, 0.25f, 1f);
                }
                bool head = Segment(u, v, 0.58f, 0.42f, 0.92f, 0.08f, 0.05f);
                bool barbs = Segment(u, v, 0.7f, 0.3f, 0.6f, 0.2f, 0.03f) || Segment(u, v, 0.7f, 0.3f, 0.8f, 0.4f, 0.03f);
                return head || barbs ? (u + v < 1.05f ? Color.white : Silver) : Color.clear;
            });
        }

        // 稲妻の印の電池。
        private static Texture2D Battery()
        {
            return Paint("ItemBattery", (u, v) =>
            {
                if (Box(u, v, 0.42f, 0.08f, 0.58f, 0.16f))
                {
                    return Silver;
                }
                if (!Box(u, v, 0.28f, 0.16f, 0.72f, 0.92f))
                {
                    return Color.clear;
                }
                bool bolt = Segment(u, v, 0.56f, 0.24f, 0.42f, 0.54f, 0.04f) || Segment(u, v, 0.42f, 0.54f, 0.58f, 0.52f, 0.04f)
                    || Segment(u, v, 0.58f, 0.52f, 0.44f, 0.84f, 0.04f);
                if (bolt)
                {
                    return new Color(1f, 0.92f, 0.25f, 1f);
                }
                return u < 0.34f || u > 0.66f ? new Color(0.12f, 0.3f, 0.2f, 1f) : new Color(0.2f, 0.5f, 0.32f, 1f);
            });
        }

        private static Texture2D Bell()
        {
            return Paint("ItemBell", (u, v) =>
            {
                if (Ellipse(u, v, 0.5f, 0.14f, 0.07f, 0.07f) < 1f && Ellipse(u, v, 0.5f, 0.14f, 0.07f, 0.07f) > 0.5f)
                {
                    return GoldDark;
                }
                float width = 0.14f + (v - 0.2f) * 0.42f;
                bool body = v > 0.2f && v < 0.76f && Mathf.Abs(u - 0.5f) < width;
                bool lip = Box(u, v, 0.18f, 0.72f, 0.82f, 0.8f);
                bool clapper = Ellipse(u, v, 0.5f, 0.84f, 0.08f, 0.07f) < 1f;
                if (clapper)
                {
                    return GoldDark;
                }
                if (body || lip)
                {
                    return u < 0.42f && v < 0.6f ? new Color(1f, 0.95f, 0.6f, 1f) : Gold;
                }
                return Color.clear;
            });
        }

        // 曲がった羽根（芯と、根元から先へ色の変わる羽）。
        private static Texture2D Feather(string name, Color root, Color tipColor)
        {
            return Paint(name, (u, v) =>
            {
                float t = Mathf.Clamp01((u + (1f - v)) * 0.5f);
                float centerLine = 1f - u - 0.08f * Mathf.Sin(t * Mathf.PI);
                float d = v - centerLine;
                if (t < 0.15f)
                {
                    return Mathf.Abs(d) < 0.02f ? new Color(0.95f, 0.9f, 0.8f, 1f) : Color.clear;
                }
                float half = 0.17f * Mathf.Sin((t - 0.15f) / 0.85f * Mathf.PI);
                if (Mathf.Abs(d) < 0.015f)
                {
                    return new Color(1f, 0.95f, 0.85f, 1f);
                }
                if (Mathf.Abs(d) < half)
                {
                    bool split = Mathf.Repeat(t * 18f + d * 4f, 1f) < 0.08f;
                    Color c = Color.Lerp(root, tipColor, t);
                    return split ? Color.clear : c;
                }
                return Color.clear;
            });
        }

        // 赤い紐のお守り（角の丸い袋と、上の結び目）。
        private static Texture2D Charm()
        {
            var red = new Color(0.85f, 0.12f, 0.15f, 1f);
            return Paint("ItemCharm", (u, v) =>
            {
                if (Ellipse(u, v, 0.5f, 0.12f, 0.12f, 0.08f) < 1f && Ellipse(u, v, 0.5f, 0.12f, 0.12f, 0.08f) > 0.45f)
                {
                    return new Color(1f, 0.35f, 0.35f, 1f);
                }
                bool bag = Box(u, v, 0.26f, 0.22f, 0.74f, 0.9f) && !(v > 0.82f && (Ellipse(u, v, 0.34f, 0.82f, 0.08f, 0.08f) > 1f && u < 0.34f
                    || Ellipse(u, v, 0.66f, 0.82f, 0.08f, 0.08f) > 1f && u > 0.66f));
                if (!bag)
                {
                    return Color.clear;
                }
                if (Box(u, v, 0.26f, 0.22f, 0.74f, 0.3f))
                {
                    return Gold;
                }
                bool letters = Box(u, v, 0.44f, 0.38f, 0.56f, 0.76f) && Mathf.Repeat(v * 12f, 1f) < 0.45f;
                return letters ? Gold : red;
            });
        }

        // 鋲の付いた靴底。
        private static Texture2D SpikeSole()
        {
            return Paint("ItemSpikeSoles", (u, v) =>
            {
                bool sole = Ellipse(u, v, 0.5f, 0.42f, 0.36f, 0.22f) < 1f && v < 0.56f || Box(u, v, 0.14f, 0.42f, 0.86f, 0.6f);
                if (sole)
                {
                    return v < 0.38f ? new Color(0.35f, 0.3f, 0.3f, 1f) : new Color(0.2f, 0.18f, 0.18f, 1f);
                }
                for (int i = 0; i < 5; i++)
                {
                    float x = 0.2f + i * 0.15f;
                    float width = (0.85f - v) * 0.18f;
                    if (v > 0.6f && v < 0.85f && Mathf.Abs(u - x) < width)
                    {
                        return u < x ? Silver : SilverDark;
                    }
                }
                return Color.clear;
            });
        }

        // 羽根の付いた帽子。
        private static Texture2D Hat()
        {
            var felt = new Color(0.35f, 0.25f, 0.55f, 1f);
            return Paint("ItemFeatherHat", (u, v) =>
            {
                float t = Mathf.Clamp01((u - 0.5f) / 0.42f);
                if (u > 0.5f && Mathf.Abs(v - (0.42f - t * 0.32f)) < 0.07f * Mathf.Sin(t * Mathf.PI) + 0.012f)
                {
                    return Mathf.Abs(v - (0.42f - t * 0.32f)) < 0.012f ? Color.white : new Color(0.85f, 0.75f, 1f, 1f);
                }
                if (Ellipse(u, v, 0.5f, 0.72f, 0.44f, 0.1f) < 1f)
                {
                    return new Color(0.25f, 0.18f, 0.42f, 1f);
                }
                if (Box(u, v, 0.28f, 0.38f, 0.72f, 0.72f) && Ellipse(u, v, 0.5f, 0.5f, 0.24f, 0.3f) < 1.15f)
                {
                    return Box(u, v, 0.28f, 0.6f, 0.72f, 0.67f) ? Gold : felt;
                }
                return Color.clear;
            });
        }

        // 鉤の付いた丸い爆弾。
        private static Texture2D Bomb()
        {
            return Paint("ItemBomb", (u, v) =>
            {
                if (Segment(u, v, 0.62f, 0.3f, 0.78f, 0.12f, 0.025f))
                {
                    return new Color(0.75f, 0.6f, 0.4f, 1f);
                }
                if (Ellipse(u, v, 0.8f, 0.1f, 0.06f, 0.06f) < 1f)
                {
                    return new Color(1f, 0.75f, 0.2f, 1f);
                }
                bool hooks = Segment(u, v, 0.2f, 0.75f, 0.08f, 0.86f, 0.03f) || Segment(u, v, 0.5f, 0.86f, 0.5f, 0.96f, 0.03f)
                    || Segment(u, v, 0.8f, 0.75f, 0.92f, 0.86f, 0.03f);
                if (hooks)
                {
                    return Silver;
                }
                float r = Ellipse(u, v, 0.5f, 0.58f, 0.32f, 0.32f);
                if (r < 1f)
                {
                    bool shine = Ellipse(u, v, 0.4f, 0.47f, 0.07f, 0.05f) < 1f;
                    return shine ? new Color(0.6f, 0.6f, 0.65f, 1f) : Color.Lerp(new Color(0.22f, 0.22f, 0.26f, 1f), Ink, r);
                }
                return Color.clear;
            });
        }

        // 銀色の六角の板。
        private static Texture2D Hexagon()
        {
            return Paint("ItemReflector", (u, v) =>
            {
                float x = Mathf.Abs(u - 0.5f);
                float y = Mathf.Abs(v - 0.5f);
                float hex = Mathf.Max(x * 0.866f + y * 0.5f, y);
                if (hex > 0.38f)
                {
                    return Color.clear;
                }
                if (hex > 0.33f)
                {
                    return SilverDark;
                }
                bool shine = Mathf.Abs((u - v) - (-0.12f)) < 0.05f || Mathf.Abs((u - v) - 0.05f) < 0.02f;
                return shine ? Color.white : Color.Lerp(Silver, new Color(0.65f, 0.75f, 0.9f, 1f), v);
            });
        }

        // 黄色い折りたたみの板（3 枚の板をジグザグに）。
        private static Texture2D FoldingBoard()
        {
            var yellow = new Color(1f, 0.82f, 0.2f, 1f);
            return Paint("ItemPlatform", (u, v) =>
            {
                bool a = Segment(u, v, 0.12f, 0.75f, 0.38f, 0.3f, 0.06f);
                bool b = Segment(u, v, 0.38f, 0.3f, 0.62f, 0.75f, 0.06f);
                bool c = Segment(u, v, 0.62f, 0.75f, 0.88f, 0.3f, 0.06f);
                if (Ellipse(u, v, 0.38f, 0.3f, 0.045f, 0.045f) < 1f || Ellipse(u, v, 0.62f, 0.75f, 0.045f, 0.045f) < 1f)
                {
                    return SilverDark;
                }
                if (a || c)
                {
                    return yellow;
                }
                return b ? new Color(0.85f, 0.65f, 0.12f, 1f) : Color.clear;
            });
        }

        // アンテナの付いたリモコン。
        private static Texture2D Remote()
        {
            return Paint("ItemRemote", (u, v) =>
            {
                if (Segment(u, v, 0.58f, 0.3f, 0.75f, 0.06f, 0.02f))
                {
                    return Silver;
                }
                if (Ellipse(u, v, 0.75f, 0.06f, 0.04f, 0.04f) < 1f)
                {
                    return new Color(1f, 0.3f, 0.3f, 1f);
                }
                if (!Box(u, v, 0.32f, 0.28f, 0.68f, 0.92f))
                {
                    return Color.clear;
                }
                if (Ellipse(u, v, 0.5f, 0.42f, 0.08f, 0.06f) < 1f)
                {
                    return new Color(0.5f, 1f, 0.75f, 1f);
                }
                bool button = v > 0.55f && Mathf.Repeat((u - 0.36f) * 6.6f, 1f) < 0.55f && Mathf.Repeat((v - 0.55f) * 8f, 1f) < 0.55f && u < 0.64f;
                return button ? new Color(0.6f, 0.62f, 0.7f, 1f) : new Color(0.2f, 0.22f, 0.27f, 1f);
            });
        }

        // 青い砂の砂時計。
        private static Texture2D HourglassIcon()
        {
            var wood = new Color(0.55f, 0.35f, 0.2f, 1f);
            var sand = new Color(0.4f, 0.65f, 1f, 1f);
            return Paint("ItemHourglass", (u, v) =>
            {
                if (Box(u, v, 0.2f, 0.08f, 0.8f, 0.15f) || Box(u, v, 0.2f, 0.85f, 0.8f, 0.92f))
                {
                    return wood;
                }
                if (Box(u, v, 0.22f, 0.15f, 0.27f, 0.85f) || Box(u, v, 0.73f, 0.15f, 0.78f, 0.85f))
                {
                    return wood;
                }
                float half = 0.04f + Mathf.Abs(v - 0.5f) * 0.6f;
                if (v > 0.15f && v < 0.85f && Mathf.Abs(u - 0.5f) < half)
                {
                    bool filled = v > 0.66f || (v > 0.32f && v < 0.5f) || (Mathf.Abs(u - 0.5f) < 0.015f && v >= 0.5f);
                    return filled ? sand : new Color(0.85f, 0.95f, 1f, 0.45f);
                }
                return Color.clear;
            });
        }

        // 鎖の付いた鉤爪。
        private static Texture2D Chain()
        {
            return Paint("ItemChainHook", (u, v) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    float cx = 0.14f + i * 0.13f;
                    float cy = 0.86f - i * 0.13f;
                    float r = i % 2 == 0 ? Ellipse(u, v, cx, cy, 0.08f, 0.05f) : Ellipse(u, v, cx, cy, 0.05f, 0.08f);
                    if (r < 1f && r > 0.5f)
                    {
                        return i % 2 == 0 ? Silver : SilverDark;
                    }
                }
                bool claw = Segment(u, v, 0.62f, 0.38f, 0.82f, 0.12f, 0.035f) || Segment(u, v, 0.62f, 0.38f, 0.9f, 0.4f, 0.035f)
                    || Segment(u, v, 0.82f, 0.12f, 0.9f, 0.2f, 0.025f) || Segment(u, v, 0.9f, 0.4f, 0.86f, 0.5f, 0.025f);
                return claw ? new Color(0.92f, 0.94f, 1f, 1f) : Color.clear;
            });
        }

        // 虹色のシャボン玉。
        private static Texture2D Bubble()
        {
            return Paint("ItemBubble", (u, v) =>
            {
                float r = Ellipse(u, v, 0.5f, 0.5f, 0.4f, 0.4f);
                if (r > 1f)
                {
                    return Color.clear;
                }
                if (Ellipse(u, v, 0.36f, 0.34f, 0.08f, 0.05f) < 1f)
                {
                    return Color.white;
                }
                float hue = Mathf.Repeat(Mathf.Atan2(v - 0.5f, u - 0.5f) / (Mathf.PI * 2f) + r * 0.5f, 1f);
                Color c = Color.HSVToRGB(hue, 0.45f, 1f);
                c.a = r > 0.86f ? 0.95f : 0.15f + 0.4f * r;
                return c;
            });
        }

        // 小さな赤いロケット（右上へ。後ろに炎）。
        private static Texture2D Rocket()
        {
            return Paint("ItemRocket", (u, v) =>
            {
                float t = (u + (1f - v)) * 0.5f;
                float d = (u - (1f - v)) * 0.7071f;
                if (t > 0.08f && t < 0.3f && Mathf.Abs(d) < (t - 0.08f) * 0.5f)
                {
                    return Color.Lerp(new Color(1f, 0.95f, 0.4f, 1f), new Color(1f, 0.4f, 0.1f, 1f), Mathf.Abs(d) * 10f);
                }
                if (t > 0.3f && t < 0.88f)
                {
                    float half = t > 0.72f ? (0.88f - t) * 0.6f : 0.1f;
                    if (Mathf.Abs(d) < half)
                    {
                        if (Ellipse(u, v, 0.58f, 0.42f, 0.05f, 0.05f) < 1f)
                        {
                            return new Color(0.6f, 0.85f, 1f, 1f);
                        }
                        return d > 0.03f ? new Color(0.7f, 0.1f, 0.12f, 1f) : new Color(0.95f, 0.2f, 0.2f, 1f);
                    }
                    if (t < 0.42f && Mathf.Abs(d) < 0.2f && Mathf.Abs(d) > 0.1f)
                    {
                        return Silver;
                    }
                }
                return Color.clear;
            });
        }

        // 小さな丸いトランポリン（青い布と脚）。
        private static Texture2D Trampoline()
        {
            return Paint("ItemTrampoline", (u, v) =>
            {
                float r = Ellipse(u, v, 0.5f, 0.45f, 0.4f, 0.15f);
                if (r < 0.82f)
                {
                    return new Color(0.2f, 0.55f, 1f, 1f);
                }
                if (r < 1f)
                {
                    return Silver;
                }
                bool legs = v > 0.5f && v < 0.85f && (Mathf.Abs(u - 0.18f) < 0.03f || Mathf.Abs(u - 0.82f) < 0.03f || Mathf.Abs(u - 0.5f) < 0.03f);
                bool springs = v > 0.58f && v < 0.62f && u > 0.14f && u < 0.86f && Mathf.Repeat(u * 14f, 1f) < 0.5f;
                return legs ? SilverDark : springs ? Silver : Color.clear;
            });
        }

        // わら人形（十字の形と、白い帯）。
        private static Texture2D Doll()
        {
            var straw = new Color(0.9f, 0.78f, 0.45f, 1f);
            var dark = new Color(0.7f, 0.55f, 0.28f, 1f);
            return Paint("ItemDecoy", (u, v) =>
            {
                bool head = Ellipse(u, v, 0.5f, 0.22f, 0.13f, 0.13f) < 1f;
                bool arms = Box(u, v, 0.16f, 0.36f, 0.84f, 0.46f);
                bool body = Box(u, v, 0.4f, 0.3f, 0.6f, 0.7f);
                bool legs = Segment(u, v, 0.45f, 0.68f, 0.34f, 0.92f, 0.05f) || Segment(u, v, 0.55f, 0.68f, 0.66f, 0.92f, 0.05f);
                if (Box(u, v, 0.38f, 0.52f, 0.62f, 0.58f) && body)
                {
                    return new Color(1f, 0.95f, 0.9f, 1f);
                }
                if (head || arms || body || legs)
                {
                    return Mathf.Repeat((u * 0.3f + v) * 22f, 1f) < 0.25f ? dark : straw;
                }
                return Color.clear;
            });
        }

        // コルクの栓の瓶に入った黒い雲と雷。
        private static Texture2D Bottle()
        {
            return Paint("ItemBottle", (u, v) =>
            {
                if (Box(u, v, 0.42f, 0.06f, 0.58f, 0.16f))
                {
                    return new Color(0.7f, 0.5f, 0.3f, 1f);
                }
                bool neck = Box(u, v, 0.4f, 0.16f, 0.6f, 0.32f);
                bool body = Ellipse(u, v, 0.5f, 0.62f, 0.3f, 0.3f) < 1f;
                if (!neck && !body)
                {
                    return Color.clear;
                }
                bool cloud = Ellipse(u, v, 0.42f, 0.55f, 0.13f, 0.09f) < 1f || Ellipse(u, v, 0.58f, 0.53f, 0.14f, 0.1f) < 1f
                    || Ellipse(u, v, 0.5f, 0.6f, 0.18f, 0.08f) < 1f;
                bool bolt = Segment(u, v, 0.54f, 0.64f, 0.46f, 0.74f, 0.022f) || Segment(u, v, 0.46f, 0.74f, 0.55f, 0.76f, 0.022f)
                    || Segment(u, v, 0.55f, 0.76f, 0.48f, 0.86f, 0.022f);
                if (bolt)
                {
                    return new Color(1f, 0.92f, 0.3f, 1f);
                }
                if (cloud && body)
                {
                    return new Color(0.15f, 0.15f, 0.2f, 1f);
                }
                bool rim = body && Ellipse(u, v, 0.5f, 0.62f, 0.3f, 0.3f) > 0.88f || neck && (u < 0.43f || u > 0.57f);
                return rim ? new Color(0.75f, 0.9f, 0.95f, 0.9f) : new Color(0.6f, 0.8f, 0.9f, 0.3f);
            });
        }

        // 朱色の印鑑（持ち手と、下に押した印）。
        private static Texture2D Seal()
        {
            var vermilion = new Color(0.9f, 0.25f, 0.15f, 1f);
            return Paint("ItemSeal", (u, v) =>
            {
                if (Ellipse(u, v, 0.5f, 0.16f, 0.12f, 0.1f) < 1f)
                {
                    return new Color(0.4f, 0.2f, 0.12f, 1f);
                }
                if (Box(u, v, 0.4f, 0.2f, 0.6f, 0.5f))
                {
                    return u < 0.46f ? new Color(0.6f, 0.32f, 0.2f, 1f) : new Color(0.5f, 0.26f, 0.16f, 1f);
                }
                if (Box(u, v, 0.3f, 0.5f, 0.7f, 0.6f))
                {
                    return vermilion;
                }
                float r = Ellipse(u, v, 0.5f, 0.8f, 0.3f, 0.13f);
                if (r < 1f && r > 0.75f)
                {
                    return vermilion;
                }
                bool mark = r < 0.75f && Mathf.Repeat(u * 10f, 1f) < 0.35f && Mathf.Abs(v - 0.8f) < 0.06f;
                return mark ? vermilion : Color.clear;
            });
        }
    }
}
