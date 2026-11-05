using Airerage.Core.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 1 人分の情報の札（右上の表 ScoreboardHud と、結果の画面 MatchHud で同じ見た目にする。ユーザーの要望、2026-10-14）。
    /// 誘導表示の札（GuidePlate）と同じ形で、縁と斜めの線はプレイヤーの色。左にプレイヤーの色の四角と番号、その右にキャラクターの
    /// ロゴ（docs/13 T5-8。あれば）、下にプレイヤーネーム、右を 4 つに分けて BANK / COIN / K / F（BANK の文字と数だけ黄色）。
    /// 王冠は札の左に出す。WANTED（所持コインが 100 枚以上。ユーザーの要望、2026-11-02）の人は、キャラクターのロゴの所に手配書の印を押す
    /// （その間はロゴを隠す）: 出るときは大きく傾いた印が印鑑を押すように落ちてきて、押した瞬間に朱色の輪が広がって札が揺れる。外れるときは
    /// 光の粒になって散って消え、ロゴが戻る。WANTED の間は札が赤く脈打ち、縁が赤くなり、斜めに大きな「WANTED!」の赤いスタンプを押す
    /// （Look.WantedEmphasis。1 秒たつか外れると、少しふくらみながら薄くなって消え、印だけが残る。2026-11-02 の要望）。
    /// 撃破されたときの見た目（Look）: ひび割れ、灰色に暗く、文字（「RESPAWN n」）、白く光る。
    /// OnGUI の中で描く。使い終わったら Dispose でテクスチャを消す。
    /// </summary>
    public sealed class PlayerStatusPlate
    {
        private static readonly string[] Labels = { "BANK", "COIN", "K", "F" };
        private static readonly Color Gray = new Color(0.5f, 0.5f, 0.52f);

        private readonly GuidePlate _plate;
        private readonly Texture2D _crown;
        private readonly Texture2D _crack;
        private readonly Texture2D _poster;
        private readonly float _fontScale;
        private GUIStyle _label;
        private GUIStyle _number;
        private GUIStyle _iconText;
        private GUIStyle _name;
        private GUIStyle _overlay;
        private GUIStyle _wanted;
        private GUIStyle _stampText;

        /// <summary>撃破されたときなどの見た目（すべて 0 なら、ふつうの見た目）。</summary>
        public struct Look
        {
            /// <summary>灰色に暗くする割合（0〜1）。</summary>
            public float Gray;

            /// <summary>ひび割れの濃さ（0〜1）。</summary>
            public float Crack;

            /// <summary>白く光る強さ（0〜1）。</summary>
            public float Flash;

            /// <summary>揺れ（ピクセル）。</summary>
            public Vector2 Shake;

            /// <summary>暗くしたときに右の部分に重ねる文字（なければ null）。</summary>
            public string Overlay;

            /// <summary>100 枚のコインを取ったときの虹色の光の強さ（0〜1）。縁が虹色になり、右に「JACKPOT!」。</summary>
            public float Jackpot;

            /// <summary>WANTED の手配書の印を出すか。</summary>
            public bool Wanted;

            /// <summary>印を押す動きの進み（0〜1。1 で押し終わり）。</summary>
            public float WantedStamp;

            /// <summary>印が光の粒になって消える進み（0〜1。0 なら消えていない。1 で消え終わり）。</summary>
            public float WantedDissolve;

            /// <summary>
            /// WANTED の赤い強調の強さ（0〜1）: 札が赤く脈打ち、縁が赤くなり、斜めに大きな「WANTED!」の赤いスタンプ。WANTED になってから
            /// 1 秒は 1、その後と外れたときは 0 へ下がる（フェードアウト。印 Wanted は残る）。
            /// </summary>
            public float WantedEmphasis;
        }

        /// <summary>手配書の印の幅と高さ（ロゴの四角の大きさに対する割合）。</summary>
        private const float PosterWidth = 1.45f;
        private const float PosterHeight = 1.2f;
        private const int DissolveParticles = 22;

        public PlayerStatusPlate(int width, int height, float fontScale = 1f, int crackSeed = 7)
        {
            Width = width;
            Height = height;
            _fontScale = fontScale;
            _plate = new GuidePlate(width, height);
            _crown = GuideIcons.Crown(64);
            _poster = GuideIcons.WantedPoster(48, 64);
            _crack = GuideIcons.Crack(width, height, crackSeed);
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>札の左に出す王冠の幅（ピクセル）。</summary>
        public float CrownWidth => Height * 0.8f + 4f;

        /// <summary>
        /// number は四角に書く番号、name はプレイヤーネーム。bankColor は BANK の文字と数の色。logo はキャラクターのロゴ（なければ
        /// null。番号の四角だけを出す）。
        /// </summary>
        public void Draw(Rect rect, int number, string name, Color color, in CharacterState state, bool crown, Look look, Color bankColor,
            Texture2D logo = null, bool showBag = false)
        {
            EnsureStyles();
            rect.position += look.Shake;
            Color previous = GUI.color;
            Color frame = Color.Lerp(color, Gray, look.Gray);

            _plate.DrawBackground(rect);
            if (look.Gray > 0f)
            {
                _plate.DrawFill(rect, new Color(0f, 0f, 0f, 0.45f * look.Gray));
            }
            _plate.DrawFrame(rect, frame);

            // 左: プレイヤーの色の四角と番号、その右にキャラクターのロゴ、下にプレイヤーネーム。
            Rect area = _plate.IconRect(rect);
            float size = area.height * 0.66f;
            const float logoGap = 3f;
            bool wantedMark = look.Wanted || look.WantedDissolve > 0f;
            float pair = logo != null || wantedMark ? size * 2f + logoGap : size;
            var icon = new Rect(area.center.x - pair * 0.5f, area.y, size, size);
            GUI.color = HudFade.Apply(Color.Lerp(Color.Lerp(color, Color.black, 0.45f), Gray * 0.6f, look.Gray));
            GUI.DrawTexture(icon, Texture2D.whiteTexture);
            GUI.color = HudFade.Apply(Color.Lerp(color, Gray, look.Gray));
            GUI.DrawTexture(new Rect(icon.x + 2f, icon.y + 2f, icon.width - 4f, icon.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
            GuidePlate.DrawText(icon, number.ToString(), _iconText, Color.white);
            var logoRect = new Rect(icon.xMax + logoGap, icon.y, size, size);
            // WANTED の間はロゴの代わりに手配書の印（消えていく間は、ロゴが戻ってくる）。
            float logoAlpha = look.Wanted ? 0f : look.WantedDissolve > 0f ? Mathf.Clamp01(look.WantedDissolve * 1.5f - 0.3f) : 1f;
            if (logo != null && logoAlpha > 0f)
            {
                Color logoColor = Color.Lerp(Color.white, Gray, look.Gray);
                logoColor.a = logoAlpha;
                GUI.color = HudFade.Apply(logoColor);
                GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }
            float nameWidth = (area.center.x - rect.x) * 2f - 4f;
            var nameRect = new Rect(area.center.x - nameWidth * 0.5f, icon.yMax, nameWidth, rect.yMax - icon.yMax - 2f);
            GuidePlate.DrawText(nameRect, name, _name, Color.Lerp(Color.white, Gray, look.Gray));

            if (crown)
            {
                float crownSize = rect.height * 0.8f;
                GUI.DrawTexture(new Rect(rect.x - crownSize - 4f, rect.y + (rect.height - crownSize) * 0.5f, crownSize, crownSize), _crown, ScaleMode.ScaleToFit);
            }


            // 右: アイテムのモードなら、まずアイテムバッグの 3 枠（斜めの線で区切る。docs/20_item_plan.md）。残りを 4 つに分けて BANK / COIN / K / F。
            Rect right = _plate.RightRect(rect);
            if (showBag)
            {
                float bagWidth = BagWidth(rect.height);
                DrawBag(new Rect(right.x, right.y, bagWidth, right.height), state.Items, look.Gray);
                right = new Rect(right.x + bagWidth, right.y, right.width - bagWidth, right.height);
            }
            int[] values = { state.Coins.Banked, state.Coins.Held, state.Combat.Knockouts, state.Combat.Falls };
            float cell = right.width / Labels.Length;
            for (int c = 0; c < Labels.Length; c++)
            {
                var cellRect = new Rect(right.x + c * cell, right.y + 3f, cell, right.height - 6f);
                Color textColor = Color.Lerp(c == 0 ? bankColor : Color.white, Gray, look.Gray);
                GuidePlate.DrawText(cellRect, Labels[c], _label, new Color(textColor.r, textColor.g, textColor.b, 0.85f));
                GuidePlate.DrawText(cellRect, values[c].ToString(), _number, textColor);
                if (c > 0)
                {
                    GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.18f));
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y + 4f, 1f, cellRect.height - 8f), Texture2D.whiteTexture);
                    GUI.color = previous;
                }
            }

            if (look.Crack > 0f)
            {
                GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.85f * look.Crack));
                GUI.DrawTexture(rect, _crack);
                GUI.color = previous;
            }
            if (look.Gray > 0f && !string.IsNullOrEmpty(look.Overlay))
            {
                GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.55f * look.Gray));
                GUI.DrawTexture(new Rect(right.x, right.y + right.height * 0.2f, right.width, right.height * 0.6f), Texture2D.whiteTexture);
                GUI.color = previous;
                GuidePlate.DrawText(right, look.Overlay, _overlay, new Color(1f, 1f, 1f, look.Gray));
            }
            if (look.Flash > 0f)
            {
                _plate.DrawFill(rect, new Color(1f, 1f, 1f, 0.85f * look.Flash));
            }
            if (look.WantedEmphasis > 0f)
            {
                DrawWantedEmphasis(rect, look);
            }
            if (wantedMark)
            {
                DrawWanted(logoRect, look);
            }
            if (look.Jackpot > 0f)
            {
                // 虹色に光る（中が虹色に脈打ち、縁が虹色になり、右の部分に「JACKPOT!」。ユーザーの要望、2026-10-15）。
                float time = Time.time;
                Color hue = Color.HSVToRGB(Mathf.Repeat(time / 0.9f, 1f), 0.7f, 1f);
                _plate.DrawFill(rect, new Color(hue.r, hue.g, hue.b, (0.25f + 0.2f * Mathf.Sin(time * 14f)) * look.Jackpot));
                _plate.DrawRainbowFrame(rect, time, look.Jackpot);
                Rect jackpotArea = _plate.RightRect(rect);
                GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.6f * look.Jackpot));
                GUI.DrawTexture(new Rect(jackpotArea.x, jackpotArea.y + jackpotArea.height * 0.15f, jackpotArea.width, jackpotArea.height * 0.7f), Texture2D.whiteTexture);
                GUI.color = previous;
                Color text = Color.HSVToRGB(Mathf.Repeat(time / 0.9f + 0.5f, 1f), 0.45f, 1f);
                text.a = look.Jackpot;
                GuidePlate.DrawText(jackpotArea, "JACKPOT!", _overlay, text);
            }
            GUI.color = previous;
        }

        /// <summary>札の高さ height のときのアイテムバッグの幅（3 枠と区切り）。札をこれだけ広げる。</summary>
        public static float BagWidth(float height)
        {
            return (height - 12f) * 3f + 30f;
        }

        // 値は全員で同じ（ItemTuning の初めの値）。
        private static readonly Airerage.Core.Items.ItemTuning ItemValues = new Airerage.Core.Items.ItemTuning();

        // アイテムバッグ: 3 つの枠を斜めの線で区切り、入っているアイテムの絵を出す。アクティブアイテムのクールタイムの間は、残りの分だけ
        // 暗くする（効果が終わるのを待つ間は全部）。
        private static void DrawBag(Rect area, in Airerage.Core.Items.ItemBag bag, float gray)
        {
            Color previous = GUI.color;
            float size = area.height - 12f;
            float slot = (area.width - 6f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var cell = new Rect(area.x + 3f + i * slot + (slot - size) * 0.5f, area.y + 6f, size, size);
                GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.07f));
                GUI.DrawTexture(cell, Texture2D.whiteTexture);
                Airerage.Core.Items.ItemId id = bag.Get(i);
                Texture2D icon = ItemIcons.Get(id);
                if (icon != null)
                {
                    GUI.color = HudFade.Apply(Color.Lerp(Color.white, Gray, gray));
                    GUI.DrawTexture(cell, icon, ScaleMode.ScaleToFit);
                    int cooldown = bag.GetCooldown(i);
                    if (Airerage.Core.Items.ItemCatalog.IsActive(id) && cooldown != 0
                        && Airerage.Core.Items.ItemRules.ChargesLeft(bag, i, ItemValues) == 0)
                    {
                        int full = Mathf.Max(1, ItemValues.CooldownOf(id));
                        float left = cooldown < 0 ? 1f : Mathf.Clamp01(cooldown / (float)full);
                        GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.62f));
                        GUI.DrawTexture(new Rect(cell.x, cell.y, cell.width, cell.height * left), Texture2D.whiteTexture);
                    }
                }
                if (i > 0)
                {
                    // 斜めの区切り（右上から左下へ）。
                    Matrix4x4 matrix = GUI.matrix;
                    var pivot = new Vector2(area.x + 3f + i * slot, area.center.y);
                    GUIUtility.RotateAroundPivot(20f, pivot);
                    GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.35f));
                    GUI.DrawTexture(new Rect(pivot.x - 1f, area.y + 3f, 2f, area.height - 6f), Texture2D.whiteTexture);
                    GUI.matrix = matrix;
                }
            }
            // バッグと右の数の区切り。
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.25f));
            GUI.DrawTexture(new Rect(area.xMax - 1f, area.y + 4f, 1f, area.height - 8f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // WANTED の赤い強調: 札が赤く脈打ち、縁が赤くなり、札の上に斜めの「WANTED!」のスタンプ（二重の枠と文字。押すときは大きくから縮む）。
        private void DrawWantedEmphasis(Rect rect, Look look)
        {
            float strength = Mathf.Clamp01(look.WantedEmphasis);
            float time = Time.time;
            var red = new Color(0.95f, 0.12f, 0.08f);
            _plate.DrawFill(rect, new Color(red.r, red.g, red.b, (0.16f + 0.1f * (0.5f + 0.5f * Mathf.Sin(time * 5f))) * strength));
            _plate.DrawFrame(rect, red, strength);
            // スタンプ: 押す動きの初め（WantedStamp が 0.35 まで）は大きくから縮み、消えるとき（強さが 1 より下がる）は少しふくらみながら薄くなる。
            float stamp = Mathf.Clamp01(look.WantedStamp);
            float fall = look.Wanted ? Mathf.Clamp01(stamp / 0.35f) : 1f;
            float scale = Mathf.Lerp(2.6f, 1f, fall * fall) * (1f + 0.15f * (1f - strength));
            float alpha = strength * Mathf.Lerp(0.2f, 1f, fall);
            Vector2 center = rect.center + new Vector2(rect.width * 0.08f, 0f);
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUIUtility.RotateAroundPivot(-11f, center);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), center);
            var box = new Rect(center.x - rect.width * 0.36f, center.y - rect.height * 0.36f, rect.width * 0.72f, rect.height * 0.72f);
            var ink = new Color(0.92f, 0.08f, 0.06f, 0.78f * alpha);
            GUI.color = HudFade.Apply(ink);
            DrawOutline(box, 2.5f);
            DrawOutline(new Rect(box.x + 4f, box.y + 4f, box.width - 8f, box.height - 8f), 1.2f);
            Color text = ink;
            text.a = 0.9f * alpha;
            GUI.color = HudFade.Apply(text);
            GUI.Label(box, "WANTED!", _stampText);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        // 四角の縁（太さ thickness）。色は GUI.color。
        private static void DrawOutline(Rect rect, float thickness)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        }

        // WANTED の手配書の印（ロゴの四角 slot の所。押す動き・押した瞬間の朱色の輪・光の粒になって消える動き）。
        private void DrawWanted(Rect slot, Look look)
        {
            float width = slot.height * PosterWidth;
            float height = slot.height * PosterHeight;
            var center = slot.center;
            var poster = new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
            Color previous = GUI.color;
            Matrix4x4 matrix = GUI.matrix;
            if (look.WantedDissolve > 0f)
            {
                float t = Mathf.Clamp01(look.WantedDissolve);
                // 印は薄くなりながら少しふくらみ、光の粒が上と外へ散る。
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.6f));
                if (fade > 0f)
                {
                    GUIUtility.RotateAroundPivot(-6f, center);
                    GUIUtility.ScaleAroundPivot(Vector2.one * (1f + 0.25f * t), center);
                    DrawPoster(poster, fade);
                    GUI.matrix = matrix;
                }
                for (int i = 0; i < DissolveParticles; i++)
                {
                    float a = Hash(i * 3 + 1);
                    float b = Hash(i * 3 + 2);
                    float c = Hash(i * 3 + 3);
                    var start = new Vector2(center.x + (a - 0.5f) * width, center.y + (b - 0.5f) * height);
                    var velocity = new Vector2((a - 0.5f) * 90f, -40f - 90f * c);
                    Vector2 position = start + velocity * t - new Vector2(0f, 30f * t * t);
                    float size = Mathf.Lerp(3.5f, 1f, t) * (0.7f + 0.6f * c);
                    float sparkle = 0.6f + 0.4f * Mathf.Sin(t * 30f + i);
                    GUI.color = HudFade.Apply(new Color(1f, 0.95f, 0.7f, (1f - t) * sparkle));
                    GUI.DrawTexture(new Rect(position.x - size * 0.5f, position.y - size * 0.5f, size, size), Texture2D.whiteTexture);
                }
                GUI.color = previous;
                return;
            }
            // 押す動き: 大きく傾いて浮いた印が、落ちてきて押される。押した後に朱色の輪が広がる。
            float stamp = Mathf.Clamp01(look.WantedStamp);
            float fall = Mathf.Clamp01(stamp / 0.35f);
            float eased = fall * fall;
            if (fall >= 1f)
            {
                float ring = Mathf.Clamp01((stamp - 0.35f) / 0.65f);
                float radius = Mathf.Lerp(width * 0.5f, width * 1.4f, ring);
                GUI.color = HudFade.Apply(new Color(0.9f, 0.15f, 0.1f, 0.55f * (1f - ring)));
                DrawRingOutline(center, radius, 2f);
            }
            GUIUtility.RotateAroundPivot(Mathf.Lerp(-24f, -6f, eased), center);
            GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Lerp(2.4f, 1f, eased), center);
            DrawPoster(poster, Mathf.Lerp(0.15f, 1f, fall));
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        // 手配書の絵と、朱色の帯の上の「WANTED」の文字。
        private void DrawPoster(Rect poster, float alpha)
        {
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, alpha));
            GUI.DrawTexture(poster, _poster);
            var band = new Rect(poster.x, poster.y + poster.height * 0.06f, poster.width, poster.height * 0.26f);
            GuidePlate.DrawText(band, "WANTED", _wanted, new Color(1f, 0.95f, 0.85f, alpha));
        }

        // 中心 center・半径 radius の輪の外周を、小さな四角で描く（IMGUI に円がないので）。
        private static void DrawRingOutline(Vector2 center, float radius, float thickness)
        {
            const int Dots = 28;
            for (int i = 0; i < Dots; i++)
            {
                float angle = i * Mathf.PI * 2f / Dots;
                var p = new Vector2(center.x + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius);
                GUI.DrawTexture(new Rect(p.x - thickness, p.y - thickness, thickness * 2f, thickness * 2f), Texture2D.whiteTexture);
            }
        }

        // 粒ごとに決まった 0〜1 の値（乱数を使わない。毎フレーム同じ）。
        private static float Hash(int n)
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 65535f;
        }

        public void Dispose()
        {
            _plate.Dispose();
            Object.Destroy(_crown);
            Object.Destroy(_poster);
            Object.Destroy(_crack);
        }

        private void EnsureStyles()
        {
            if (_label != null)
            {
                return;
            }
            _label = Style(11, TextAnchor.UpperCenter);
            _number = Style(19, TextAnchor.LowerCenter);
            _iconText = Style(13, TextAnchor.MiddleCenter);
            _name = Style(11, TextAnchor.MiddleCenter);
            _name.clipping = TextClipping.Clip;
            _overlay = Style(16, TextAnchor.MiddleCenter);
            _wanted = Style(7, TextAnchor.MiddleCenter);
            _stampText = Style(24, TextAnchor.MiddleCenter);
            _stampText.fontStyle = FontStyle.BoldAndItalic;
        }

        private GUIStyle Style(int size, TextAnchor anchor)
        {
            return GameFonts.Use(new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * _fontScale),
                fontStyle = FontStyle.Bold,
                alignment = anchor,
                wordWrap = false,
            });
        }
    }
}
