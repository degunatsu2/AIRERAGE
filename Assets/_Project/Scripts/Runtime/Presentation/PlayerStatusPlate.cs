using System.Collections.Generic;
using System.Globalization;
using Airerage.Core.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 1 人分の情報の札（右上の表 ScoreboardHud と、結果の画面 MatchHud で同じ見た目にする）。2026-11-06 にユーザーが選んだ形
    /// （案 A の 2 段 ＋ 案 C の印 ＋ 案 F の順位の金属の色と動き ＋ 案 B の短い書き方）:
    /// - 札は斜めの辺の平行四辺形。縁はプレイヤーの色（チーム対戦はチームの色）。上の段と下の段の間に、縁と同じ色と太さの直線。
    /// - 上の段: 順位の四角（1 位 金・2 位 銀・3 位 銅・4 位以下 鉄。確定コインが 0 なら鉄）、キャラクターのロゴ、プレイヤーネーム。
    ///   アイテムのモードなら、灰色の細い斜め線で区切って右にアイテムバッグ（1 枠が順位の四角と同じ大きさ）。順位の四角は上の縁と
    ///   区切りの線の真ん中に置く（上と下の隙間が同じ）。
    /// - 下の段: 金庫の印と BANK（大きく、黄色）、コインの印と COIN、衝撃の印と K（撃破した数）、倒れた人の印と F（撃破された数）。
    ///   間を灰色の細い斜め線（縁と同じ角度）で区切る。COIN・K・F は同じ大きさ。数が欄に入らなければ字を小さくする（斜め線に重ならない）。
    ///   10,000 以上は 12.4K、1,000,000 以上は 1.2M と短く書く（abbreviate。結果の画面は正確な数）。
    /// - 1 位の札は、LeaderPulseSeconds ごとに、画面の外の誘導表示（拠点の移動・ホットエリア。EdgeGuideLayer）と同じ光り方をする: 札が白く光り、
    ///   札の周りに縁の色の枠（札と同じ斜めの形）が広がって消える（2026-11-06。前の斜めに走る光はカクついて見えた）。BANK が増えたら数が
    ///   数え上がり、上に「+増えた数」が浮かぶ（Look.Gain）。
    /// - 札の後ろに、縁の色の裏の札を少し角度をずらして重ねる（メニューの札と同じ見た目。2026-11-08 のユーザーの要望。札の中は裏の札が
    ///   透けないように不透明）。
    /// 王冠は札の左に出す。WANTED（所持コインが 100 枚以上。2026-11-02）の人は、キャラクターのロゴの所に手配書の印を押す
    /// （その間はロゴを隠す）: 出るときは大きく傾いた印が印鑑を押すように落ちてきて、押した瞬間に朱色の輪が広がって札が揺れる。外れるときは
    /// 光の粒になって散って消え、ロゴが戻る。WANTED の間は札が赤く脈打ち、縁が赤くなり、斜めに大きな「WANTED!」の赤いスタンプを押す
    /// （Look.WantedEmphasis。1 秒たつか外れると、少しふくらみながら薄くなって消え、印だけが残る）。
    /// 撃破されたときの見た目（Look）: ひび割れ、灰色に暗く、下の段に文字（「RESPAWN n」）、白く光る。
    /// 札の形（塗り・縁・区切り・光・虹色の縁）は大きさごとに 1 回だけテクスチャに焼き、色は描くときに GUI.color でつける。縁は札の内側に
    /// 描き、テクスチャには 1 点の余白を取る（斜めの辺と右下の角まで欠けずに出る）。
    /// OnGUI の中で描く。使い終わったら Dispose でテクスチャを消す。
    /// </summary>
    public sealed class PlayerStatusPlate
    {
        // ---------------------------------------------------------------- 形と配置（倍率 1 のときの画面の点）

        /// <summary>札の幅と高さ（倍率 1）。アイテムのモードでも幅は変わらない。</summary>
        public const float BaseWidth = 320f;
        public const float BaseHeight = 64f;

        /// <summary>縁と、上の段と下の段の区切りの線の太さ。</summary>
        public const float Border = 2f;

        /// <summary>斜めの辺と区切りの斜め線の傾き（縦 1 に対する横）。辺は上ほど右へ寄る。</summary>
        public const float Slant = 0.22f;

        /// <summary>上の段と下の段の区切りの線の上の端（線の太さは Border）。</summary>
        public const float DividerY = 32f;

        /// <summary>順位の四角とロゴの大きさ。アイテムバッグの 1 枠も同じ大きさ（ユーザーの指定）。</summary>
        public const float RankSize = 22f;

        private const float Pad = 1f;
        private const float LogoGap = 4f;
        private const float NameGap = 6f;
        private const float SlotGap = 3f;
        private const int BagSlots = 3;
        private const float SeparatorInset = 3f;
        private const float SeparatorWidth = 1.2f;
        private const float CellPad = 3.5f;
        private const float EdgePad = 3f;
        private static readonly float[] CellWeights = { 0.33f, 0.29f, 0.2f, 0.18f };
        private const int BankFont = 20;
        private const int ValueFont = 15;
        private const float IconSize = 14f;
        private const float IconGap = 3f;
        private const int RainbowFrames = 12;

        /// <summary>1 位の札が光る間隔（秒）。画面の外の誘導表示は 1 秒ごとだが、ずっと出ている札なので間を空ける【仮】。</summary>
        public const float LeaderPulseSeconds = 3f;

        private static readonly Color Gray = new Color(0.5f, 0.5f, 0.52f);
        private static readonly Color Back = new Color(0.03f, 0.05f, 0.09f, 1f);
        // 裏の札のずれ（倍率 1 の画面の点。左下へ）と、端がはみ出す量（倍率 1 の画面の点。この量になるように回す）。
        private static readonly Vector2 BackPlateOffset = new Vector2(-6f, 5f);
        private const float BackPlatePeek = 4f;
        private static readonly Color SeparatorColor = new Color(0.62f, 0.65f, 0.7f, 0.55f);
        private static readonly Color GainColor = new Color(0.49f, 1f, 0.6f);

        /// <summary>順位の四角の色（中と縁）: 1 位 金・2 位 銀・3 位 銅・4 位以下 鉄。</summary>
        private static readonly Color[] MetalFill =
        {
            new Color(1f, 0.83f, 0.3f), new Color(0.85f, 0.89f, 0.93f), new Color(0.85f, 0.58f, 0.35f), new Color(0.52f, 0.58f, 0.65f),
        };
        private static readonly Color[] MetalEdge =
        {
            new Color(1f, 0.95f, 0.69f), Color.white, new Color(1f, 0.82f, 0.68f), new Color(0.77f, 0.82f, 0.86f),
        };

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

            /// <summary>暗くしたときに下の段に重ねる文字（なければ null）。</summary>
            public string Overlay;

            /// <summary>100 枚のコインを取ったときの虹色の光の強さ（0〜1）。縁が虹色になり、下の段に「JACKPOT!」。</summary>
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

            /// <summary>数え上げている途中の BANK の数（HasShownBank のときだけ。なければ状態の確定コイン）。</summary>
            public bool HasShownBank;
            public int ShownBank;

            /// <summary>BANK が増えた数（0 なら出さない）と、「+増えた数」が浮かんで消える進み（0〜1）。</summary>
            public int Gain;
            public float GainAge;
        }

        /// <summary>札の中の配置（倍率 1、札の左上が原点）。テストと描くときに使う。</summary>
        public struct Layout
        {
            public Rect Rank;
            public Rect Logo;
            public Rect Name;
            public Rect[] Slots;

            /// <summary>上の段の区切りの斜め線（段の真ん中の高さでの x）。</summary>
            public float[] TopSeparators;

            /// <summary>下の段の区切りの斜め線（段の真ん中の高さでの x）。</summary>
            public float[] BottomSeparators;

            /// <summary>下の段の 4 つの欄（BANK / COIN / K / F）の、数と印を置ける範囲（斜め線と縁にかからない）。</summary>
            public Rect[] Cells;

            /// <summary>下の段の全体（重ねる文字・JACKPOT! を置く）。</summary>
            public Rect BottomRow;
        }

        /// <summary>手配書の印の幅と高さ（ロゴの四角の大きさに対する割合）。</summary>
        private const float PosterWidth = 1.45f;
        private const float PosterHeight = 1.2f;
        private const int DissolveParticles = 22;

        // 描くときに使う配置（毎フレーム作らない）。
        private static readonly Layout BagLayout = LayoutOf(true);
        private static readonly Layout PlainLayout = LayoutOf(false);

        private readonly float _scale;
        private readonly Texture2D _fill;
        private readonly Texture2D _frame;
        private readonly Texture2D _separatorsBag;
        private readonly Texture2D _separatorsPlain;
        private readonly List<Texture2D> _rainbow = new List<Texture2D>();
        private readonly Texture2D _crown;
        private readonly Texture2D _crack;
        private readonly Texture2D _poster;
        private readonly Texture2D[] _icons;
        private readonly GUIContent _content = new GUIContent();
        private GUIStyle _rank;
        private GUIStyle _number;
        private GUIStyle _name;
        private GUIStyle _overlay;
        private GUIStyle _gain;
        private GUIStyle _wanted;
        private GUIStyle _stampText;

        /// <param name="scale">大きさの倍率（右上の表は 1、結果の画面は大きく）。</param>
        public PlayerStatusPlate(float scale = 1f, int crackSeed = 7)
        {
            _scale = Mathf.Max(0.25f, scale);
            Width = Mathf.RoundToInt(BaseWidth * _scale);
            Height = Mathf.RoundToInt(BaseHeight * _scale);
            _fill = Bake("StatusPlateFill", (x, y) => White(FillAlpha(x, y, _scale)));
            _frame = Bake("StatusPlateFrame", (x, y) => White(FrameAlpha(x, y, _scale)));
            _separatorsBag = Bake("StatusPlateSeparatorsBag", (x, y) => White(SeparatorAlpha(BagLayout, x, y, _scale)));
            _separatorsPlain = Bake("StatusPlateSeparators", (x, y) => White(SeparatorAlpha(PlainLayout, x, y, _scale)));
            _crown = GuideIcons.Crown(64);
            _poster = GuideIcons.WantedPoster(48, 64);
            _crack = GuideIcons.Crack(Width, Height, crackSeed);
            _icons = new[]
            {
                GuideIcons.Bank(48, new Color(1f, 0.85f, 0.2f)), GuideIcons.Coin(48), GuideIcons.Burst(48), GuideIcons.Fallen(48, new Color(1f, 0.5f, 0.48f)),
            };
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>札の左に出す王冠の幅（ピクセル）。</summary>
        public float CrownWidth => CrownSize + 4f;

        private float CrownSize => Height * 0.62f;

        // ---------------------------------------------------------------- 形（倍率 1 の座標。テストからも使う）

        /// <summary>高さ y での左の斜めの辺の x（上ほど右）。</summary>
        public static float LeftAt(float y)
        {
            return Slant * (BaseHeight - y);
        }

        /// <summary>高さ y での右の斜めの辺の x（下ほど左）。</summary>
        public static float RightAt(float y)
        {
            return BaseWidth - Slant * y;
        }

        // 札の内側までの距離（画面の点。外なら負）。
        private static float InsideDistance(float x, float y)
        {
            float cos = 1f / Mathf.Sqrt(1f + Slant * Slant);
            float vertical = Mathf.Min(y, BaseHeight - y);
            float horizontal = Mathf.Min(x - LeftAt(y), RightAt(y) - x) * cos;
            return Mathf.Min(vertical, horizontal);
        }

        /// <summary>札の中の塗りの濃さ（x, y は倍率 1 の座標、scale はテクスチャの倍率。境目はなめらか）。</summary>
        public static float FillAlpha(float x, float y, float scale)
        {
            return Mathf.Clamp01(InsideDistance(x, y) * scale + 0.5f);
        }

        /// <summary>
        /// 縁と、上の段と下の段の区切りの直線の濃さ。縁は札の内側に太さ Border で描く（斜めの辺も上下の辺も同じ太さ。右下の角まで欠けない）。
        /// 区切りの線は縁と同じ太さで、左右の斜めの辺の間に引く。
        /// </summary>
        public static float FrameAlpha(float x, float y, float scale)
        {
            float d = InsideDistance(x, y) * scale;
            float b = Border * scale;
            float edge = Mathf.Clamp01(d + 0.5f) - Mathf.Clamp01(d - b + 0.5f);
            float p = (y - DividerY) * scale;
            float line = (Mathf.Clamp01(p + 0.5f) - Mathf.Clamp01(p - b + 0.5f)) * Mathf.Clamp01(d + 0.5f);
            return Mathf.Max(edge, line);
        }

        // 灰色の細い斜め線（上の段: 名前とアイテムバッグの間、下の段: 数の間）の濃さ。
        private static float SeparatorAlpha(Layout layout, float x, float y, float scale)
        {
            float alpha = 0f;
            alpha = Mathf.Max(alpha, SeparatorsAlpha(layout.TopSeparators, Border, DividerY, x, y, scale));
            alpha = Mathf.Max(alpha, SeparatorsAlpha(layout.BottomSeparators, DividerY + Border, BaseHeight - Border, x, y, scale));
            return alpha;
        }

        private static float SeparatorsAlpha(float[] centers, float top, float bottom, float x, float y, float scale)
        {
            if (centers == null || centers.Length == 0)
            {
                return 0f;
            }
            float from = top + SeparatorInset;
            float to = bottom - SeparatorInset;
            float vertical = Mathf.Clamp01((y - from) * scale + 0.5f) * Mathf.Clamp01((to - y) * scale + 0.5f);
            if (vertical <= 0f)
            {
                return 0f;
            }
            float mid = (top + bottom) * 0.5f;
            float cos = 1f / Mathf.Sqrt(1f + Slant * Slant);
            float best = 0f;
            foreach (float center in centers)
            {
                float line = center + Slant * (mid - y);
                float distance = Mathf.Abs(x - line) * cos * scale;
                best = Mathf.Max(best, Mathf.Clamp01(SeparatorWidth * scale * 0.5f + 0.5f - distance));
            }
            return best * vertical;
        }

        /// <summary>斜め線 center（段の真ん中の高さでの x）の、段の中での左右のふれ幅（線の太さの半分を含む）。</summary>
        private static float SeparatorReach(float rowHeight)
        {
            return Slant * (rowHeight * 0.5f - SeparatorInset) + SeparatorWidth * 0.5f;
        }

        /// <summary>札の中の配置（倍率 1）。bag はアイテムバッグを出すか。</summary>
        public static Layout LayoutOf(bool bag)
        {
            var layout = new Layout();
            float topMid = (Border + DividerY) * 0.5f;
            float rankY = topMid - RankSize * 0.5f;
            float rankX = LeftAt(rankY) + Border + EdgePad;
            layout.Rank = new Rect(rankX, rankY, RankSize, RankSize);
            layout.Logo = new Rect(layout.Rank.xMax + LogoGap, rankY, RankSize, RankSize);
            float nameRight;
            if (bag)
            {
                // バッグの右下の角が右の斜めの辺にかからないように。
                float bagRight = RightAt(rankY + RankSize) - Border - EdgePad;
                float bagWidth = BagSlots * RankSize + (BagSlots - 1) * SlotGap;
                float bagLeft = bagRight - bagWidth;
                layout.Slots = new Rect[BagSlots];
                for (int i = 0; i < BagSlots; i++)
                {
                    layout.Slots[i] = new Rect(bagLeft + i * (RankSize + SlotGap), rankY, RankSize, RankSize);
                }
                // 斜め線は、バッグの左上の角と名前の右下の角のどちらにもかからない所。
                float reach = SeparatorReach(DividerY - Border);
                float center = bagLeft - CellPad - reach;
                layout.TopSeparators = new[] { center };
                nameRight = center - reach - CellPad;
            }
            else
            {
                layout.Slots = new Rect[0];
                layout.TopSeparators = new float[0];
                nameRight = RightAt(rankY + RankSize) - Border - EdgePad;
            }
            float nameX = layout.Logo.xMax + NameGap;
            layout.Name = new Rect(nameX, rankY, Mathf.Max(0f, nameRight - nameX), RankSize);

            // 下の段: 4 つの欄を重みで分け、間に斜め線。欄の数と印は、斜め線と縁にかからない範囲に置く。
            float rowTop = DividerY + Border;
            float rowBottom = BaseHeight - Border;
            float rowHeight = rowBottom - rowTop;
            float left = LeftAt(rowTop + SeparatorInset) + Border + EdgePad;
            float right = RightAt(rowBottom - SeparatorInset) - Border - EdgePad;
            float total = 0f;
            foreach (float weight in CellWeights)
            {
                total += weight;
            }
            var bounds = new float[CellWeights.Length + 1];
            bounds[0] = left;
            for (int i = 0; i < CellWeights.Length; i++)
            {
                bounds[i + 1] = bounds[i] + (right - left) * CellWeights[i] / total;
            }
            float cellReach = SeparatorReach(rowHeight) + CellPad;
            layout.BottomSeparators = new float[CellWeights.Length - 1];
            layout.Cells = new Rect[CellWeights.Length];
            for (int i = 0; i < CellWeights.Length; i++)
            {
                if (i > 0)
                {
                    layout.BottomSeparators[i - 1] = bounds[i];
                }
                float from = i == 0 ? bounds[i] : bounds[i] + cellReach;
                float to = i == CellWeights.Length - 1 ? bounds[i + 1] : bounds[i + 1] - cellReach;
                layout.Cells[i] = new Rect(from, rowTop, to - from, rowHeight);
            }
            layout.BottomRow = new Rect(left, rowTop, right - left, rowHeight);
            return layout;
        }

        /// <summary>
        /// 数の書き方（案 B）: abbreviate なら 10,000 以上は 12.4K、100,000 以上は 124K、1,000,000 以上は 1.2M、10,000,000 以上は
        /// 12.4M のように短く書く（切り捨て。境目で上の単位に繰り上がらない）。それより小さい数と、abbreviate でないときは 3 桁ごとに
        /// カンマを入れる。
        /// </summary>
        public static string FormatCount(int value, bool abbreviate)
        {
            if (!abbreviate || value < 10000)
            {
                return value.ToString("N0", CultureInfo.InvariantCulture);
            }
            if (value < 100000)
            {
                return (value / 100 / 10f).ToString("0.0", CultureInfo.InvariantCulture) + "K";
            }
            if (value < 1000000)
            {
                return (value / 1000).ToString(CultureInfo.InvariantCulture) + "K";
            }
            if (value < 100000000)
            {
                return (value / 100000 / 10f).ToString("0.0", CultureInfo.InvariantCulture) + "M";
            }
            return (value / 1000000).ToString(CultureInfo.InvariantCulture) + "M";
        }

        // ---------------------------------------------------------------- 描く

        /// <summary>
        /// rank は順位の四角に書く順位（1 から）、scored は確定コイン（チーム対戦はチームの合計）が 1 枚以上か（0 なら順位の四角は鉄の色）。
        /// name はプレイヤーネーム、color は縁の色、bankColor は BANK の数の色。logo はキャラクターのロゴ（なければ null）。showBag は
        /// アイテムバッグを出すか、abbreviate は大きな数を短く書くか（結果の画面は正確な数）。
        /// </summary>
        public void Draw(Rect rect, int rank, bool scored, string name, Color color, in CharacterState state, bool crown, Look look, Color bankColor,
            Texture2D logo = null, bool showBag = false, bool abbreviate = true)
        {
            EnsureStyles();
            rect.position += look.Shake;
            Color previous = GUI.color;
            Layout layout = showBag ? BagLayout : PlainLayout;
            float sx = rect.width / BaseWidth;
            float sy = rect.height / BaseHeight;
            var drawRect = new Rect(rect.x - Pad * sx, rect.y - Pad * sy, rect.width + Pad * 2f * sx, rect.height + Pad * 2f * sy);
            Color frame = Color.Lerp(color, Gray, look.Gray);

            // 裏の札（縁の色。札の真ん中を中心に少し回して、左下へずらす）。
            DrawBackPlate(drawRect, frame, sx, sy);
            DrawTexture(drawRect, _fill, Back);
            if (look.Gray > 0f)
            {
                DrawTexture(drawRect, _fill, new Color(0f, 0f, 0f, 0.45f * look.Gray));
            }
            DrawTexture(drawRect, _frame, frame);
            DrawTexture(drawRect, showBag ? _separatorsBag : _separatorsPlain, SeparatorColor);

            // 上の段: 順位の四角、ロゴ、名前、アイテムバッグ。
            DrawRank(Map(rect, layout.Rank), rank, scored, look.Gray);
            Rect logoRect = Map(rect, layout.Logo);
            bool wantedMark = look.Wanted || look.WantedDissolve > 0f;
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
            GuidePlate.DrawText(Map(rect, layout.Name), name, _name, Color.Lerp(Color.white, Gray, look.Gray));
            for (int i = 0; i < layout.Slots.Length; i++)
            {
                DrawSlot(Map(rect, layout.Slots[i]), state.Items, i, look.Gray);
            }

            // 下の段: BANK（大きく）・COIN・K・F。
            int bank = look.HasShownBank ? look.ShownBank : state.Coins.Banked;
            int[] values = { bank, state.Coins.Held, state.Combat.Knockouts, state.Combat.Falls };
            Rect bankText = default;
            for (int c = 0; c < values.Length; c++)
            {
                Color textColor = Color.Lerp(c == 0 ? bankColor : Color.white, Gray, look.Gray);
                string text = c < 2 ? FormatCount(values[c], abbreviate) : values[c].ToString(CultureInfo.InvariantCulture);
                Rect drawn = DrawCell(Map(rect, layout.Cells[c]), _icons[c], text, Mathf.RoundToInt((c == 0 ? BankFont : ValueFont) * sy), textColor,
                    look.Gray);
                if (c == 0)
                {
                    bankText = drawn;
                }
            }

            if (crown)
            {
                float size = CrownSize;
                float edge = rect.x + LeftAt(BaseHeight * 0.5f) * sx;
                GUI.color = HudFade.Apply(Color.white);
                GUI.DrawTexture(new Rect(edge - size - 2f, rect.y + (rect.height - size) * 0.5f, size, size), _crown, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }

            Rect bottom = Map(rect, layout.BottomRow);
            if (look.Crack > 0f)
            {
                GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.85f * look.Crack));
                GUI.DrawTexture(rect, _crack);
                GUI.color = previous;
            }
            if (look.Gray > 0f && !string.IsNullOrEmpty(look.Overlay))
            {
                GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.6f * look.Gray));
                GUI.DrawTexture(new Rect(bottom.x, bottom.y + bottom.height * 0.12f, bottom.width, bottom.height * 0.76f), Texture2D.whiteTexture);
                GUI.color = previous;
                GuidePlate.DrawText(bottom, look.Overlay, _overlay, new Color(1f, 1f, 1f, look.Gray));
            }
            if (look.Flash > 0f)
            {
                DrawTexture(drawRect, _fill, new Color(1f, 1f, 1f, 0.85f * look.Flash));
            }
            if (rank == 1 && scored && look.Gray <= 0f)
            {
                DrawLeaderPulse(rect, drawRect, frame);
            }
            if (look.WantedEmphasis > 0f)
            {
                DrawWantedEmphasis(rect, drawRect, look);
            }
            if (wantedMark)
            {
                DrawWanted(logoRect, look);
            }
            if (look.Jackpot > 0f)
            {
                // 虹色に光る（中が虹色に脈打ち、縁が虹色になり、下の段に「JACKPOT!」。2026-10-15）。
                float time = Time.time;
                Color hue = Color.HSVToRGB(Mathf.Repeat(time / 0.9f, 1f), 0.7f, 1f);
                DrawTexture(drawRect, _fill, new Color(hue.r, hue.g, hue.b, (0.25f + 0.2f * Mathf.Sin(time * 14f)) * look.Jackpot));
                DrawTexture(drawRect, RainbowFrame(time), new Color(1f, 1f, 1f, look.Jackpot));
                GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.6f * look.Jackpot));
                GUI.DrawTexture(new Rect(bottom.x, bottom.y + bottom.height * 0.12f, bottom.width, bottom.height * 0.76f), Texture2D.whiteTexture);
                GUI.color = previous;
                Color text = Color.HSVToRGB(Mathf.Repeat(time / 0.9f + 0.5f, 1f), 0.45f, 1f);
                text.a = look.Jackpot;
                GuidePlate.DrawText(bottom, "JACKPOT!", _overlay, text);
            }
            if (look.Gain > 0 && look.GainAge < 1f && bankText.width > 0f)
            {
                // 「+増えた数」: BANK の数の上から浮かび上がって消える。
                float age = Mathf.Clamp01(look.GainAge);
                float rise = (1f - (1f - age) * (1f - age)) * 16f * sy;
                float alpha = 1f - age * age;
                string gain = "+" + FormatCount(look.Gain, abbreviate);
                _content.text = gain;
                float popWidth = _gain.CalcSize(_content).x + 8f * sx;
                var popRect = new Rect(bankText.x + bankText.width * 0.5f - popWidth * 0.5f, bankText.y - 12f * sy - rise, popWidth, 16f * sy);
                // 上の段の名前やロゴに重なっても読めるように、暗い下地を敷く。
                GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.55f * alpha));
                GUI.DrawTexture(popRect, Texture2D.whiteTexture);
                GUI.color = previous;
                Color pop = GainColor;
                pop.a = alpha;
                GuidePlate.DrawText(popRect, gain, _gain, pop);
            }
            GUI.color = previous;
        }

        // 倍率 1 の札の中の四角を、画面の四角へ。
        private static Rect Map(Rect plate, Rect local)
        {
            float sx = plate.width / BaseWidth;
            float sy = plate.height / BaseHeight;
            return new Rect(plate.x + local.x * sx, plate.y + local.y * sy, local.width * sx, local.height * sy);
        }

        /// <summary>裏の札の角度（度。画面で時計回りが正。札の端が BackPlatePeek だけはみ出す角度。テスト用にも公開）。</summary>
        public static float BackPlateAngle => -Mathf.Atan2(BackPlatePeek, BaseWidth * 0.5f) * Mathf.Rad2Deg;

        private void DrawBackPlate(Rect drawRect, Color color, float sx, float sy)
        {
            Matrix4x4 previous = GUI.matrix;
            var rect = new Rect(drawRect.x + BackPlateOffset.x * sx, drawRect.y + BackPlateOffset.y * sy, drawRect.width, drawRect.height);
            GUIUtility.RotateAroundPivot(BackPlateAngle, rect.center);
            Color c = color;
            c.a *= 0.95f;
            DrawTexture(rect, _fill, c);
            GUI.matrix = previous;
        }

        private static void DrawTexture(Rect rect, Texture2D texture, Color color)
        {
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(color);
            GUI.DrawTexture(rect, texture);
            GUI.color = previous;
        }

        // 順位の四角: 金属の色の縁と中、真ん中に順位。確定コインが 0 なら鉄の色。
        private void DrawRank(Rect box, int rank, bool scored, float gray)
        {
            int metal = !scored ? 3 : Mathf.Clamp(rank - 1, 0, 3);
            Color edge = Color.Lerp(MetalEdge[metal], Gray, gray);
            Color fill = Color.Lerp(MetalFill[metal], Gray * 0.8f, gray);
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(edge);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            float inset = Mathf.Max(1f, 1.5f * box.height / RankSize);
            GUI.color = HudFade.Apply(fill);
            GUI.DrawTexture(new Rect(box.x + inset, box.y + inset, box.width - inset * 2f, box.height - inset * 2f), Texture2D.whiteTexture);
            GUI.color = previous;
            Color text = metal == 3 ? Color.white : new Color(0.04f, 0.07f, 0.1f);
            _rank.normal.textColor = Color.white;
            GUI.color = HudFade.Apply(text);
            GUI.Label(box, rank.ToString(CultureInfo.InvariantCulture), _rank);
            GUI.color = previous;
        }

        // 下の段の 1 つの欄: 印と数をまとめて欄の真ん中に置く。入らなければ字を小さくする（斜め線にかからない）。描いた数の四角を返す。
        private Rect DrawCell(Rect cell, Texture2D icon, string text, int fontSize, Color color, float gray)
        {
            float k = cell.height / (BaseHeight - DividerY - Border * 2f);
            float iconSize = IconSize * k;
            float gap = IconGap * k;
            _content.text = text;
            int size = Mathf.Max(8, fontSize);
            _number.fontSize = size;
            float textWidth = _number.CalcSize(_content).x;
            while (size > 8 && iconSize + gap + textWidth > cell.width)
            {
                size--;
                _number.fontSize = size;
                textWidth = _number.CalcSize(_content).x;
            }
            float total = iconSize + gap + textWidth;
            float x = Mathf.Max(cell.x, cell.center.x - total * 0.5f);
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(Color.Lerp(Color.white, Gray, gray));
            GUI.DrawTexture(new Rect(x, cell.center.y - iconSize * 0.5f, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
            GUI.color = previous;
            var textRect = new Rect(x + iconSize + gap, cell.y, textWidth + 2f, cell.height);
            GuidePlate.DrawText(textRect, text, _number, color);
            return textRect;
        }

        // 値は全員で同じ（ItemTuning の初めの値）。
        private static readonly Airerage.Core.Items.ItemTuning ItemValues = new Airerage.Core.Items.ItemTuning();

        // アイテムバッグの 1 枠: 入っているアイテムの絵。アクティブアイテムのクールタイムの間は、残りの分だけ暗くして秒数を出す
        // （効果が終わるのを待つ間は全部暗く）。
        private static void DrawSlot(Rect cell, in Airerage.Core.Items.ItemBag bag, int slot, float gray)
        {
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.08f));
            GUI.DrawTexture(cell, Texture2D.whiteTexture);
            Airerage.Core.Items.ItemId id = bag.Get(slot);
            Texture2D icon = ItemIcons.Get(id);
            if (icon != null)
            {
                GUI.color = HudFade.Apply(Color.Lerp(Color.white, Gray, gray));
                GUI.DrawTexture(cell, icon, ScaleMode.ScaleToFit);
                int cooldown = bag.GetCooldown(slot);
                if (Airerage.Core.Items.ItemCatalog.IsActive(id) && cooldown != 0
                    && Airerage.Core.Items.ItemRules.ChargesLeft(bag, slot, ItemValues) == 0)
                {
                    int full = Mathf.Max(1, ItemValues.CooldownOf(id));
                    float left = cooldown < 0 ? 1f : Mathf.Clamp01(cooldown / (float)full);
                    GUI.color = HudFade.Apply(new Color(0f, 0f, 0f, 0.62f));
                    GUI.DrawTexture(new Rect(cell.x, cell.y, cell.width, cell.height * left), Texture2D.whiteTexture);
                    if (cooldown > 0)
                    {
                        // 残りの秒数をアイコンの真ん中に（2026-11-05）。
                        DrawSeconds(cell, cooldown);
                    }
                }
            }
            GUI.color = previous;
        }

        private static GUIStyle _seconds;

        // クールタイムの残りの秒数（切り上げ。影を付けた白い太字）。
        private static void DrawSeconds(Rect cell, int ticks)
        {
            if (_seconds == null)
            {
                _seconds = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow });
            }
            _seconds.fontSize = Mathf.Max(9, Mathf.RoundToInt(cell.height * 0.6f));
            string text = Mathf.CeilToInt(ticks / (float)Airerage.Core.Common.SimTime.TicksPerSecond).ToString(CultureInfo.InvariantCulture);
            _seconds.normal.textColor = HudFade.Apply(new Color(0f, 0f, 0f, 0.9f));
            GUI.color = Color.white;
            GUI.Label(new Rect(cell.x + 1.5f, cell.y + 1.5f, cell.width, cell.height), text, _seconds);
            _seconds.normal.textColor = HudFade.Apply(Color.white);
            GUI.Label(cell, text, _seconds);
        }

        /// <summary>
        /// 1 位の札の光の進み（0〜1。光っていなければ 1）。LeaderPulseSeconds ごとに、画面の外の誘導表示と同じ長さ（EdgeGuideLayer.AppearSeconds）
        /// で白く光って枠が広がる。
        /// </summary>
        public static float LeaderPulse(float time)
        {
            return Mathf.Clamp01(Mathf.Repeat(time, LeaderPulseSeconds) / EdgeGuideLayer.AppearSeconds);
        }

        // 1 位の札の光（EdgeGuideLayer の光り方と同じ: 白く光ってから戻り、縁の色の枠が広がって消える）。
        private void DrawLeaderPulse(Rect rect, Rect drawRect, Color color)
        {
            float flash = LeaderPulse(Time.time);
            if (flash >= 1f)
            {
                return;
            }
            DrawTexture(drawRect, _fill, new Color(1f, 1f, 1f, 0.55f * Mathf.Clamp01(1f - flash / 0.5f)));
            DrawSpreadingOutline(rect, color, flash);
        }

        // 札の周りに広がって消える枠（札と同じ平行四辺形。広がり方・太さ・薄くなり方は EdgeGuideLayer の枠と同じ）。
        private static void DrawSpreadingOutline(Rect rect, Color color, float progress)
        {
            float t = 1f - (1f - progress) * (1f - progress) * (1f - progress);
            float spread = 6f + 34f * t;
            float thickness = Mathf.Lerp(4f, 1f, progress);
            float sx = rect.width / BaseWidth;
            float slant = Slant * sx * BaseHeight / rect.height;
            float secant = Mathf.Sqrt(1f + slant * slant);
            float top = rect.y - spread;
            float bottom = rect.yMax + spread;
            float height = bottom - top;
            // 外へ spread だけ離した平行四辺形の 4 つの角（左の辺は上ほど右）。
            float leftBottom = rect.x - slant * spread - spread * secant;
            float leftTop = leftBottom + slant * height;
            float rightTop = rect.xMax + slant * spread + spread * secant;
            float rightBottom = rightTop - slant * height;
            Color previous = GUI.color;
            GUI.color = HudFade.Apply(new Color(color.r, color.g, color.b, (1f - progress) * 0.9f));
            GUI.DrawTexture(new Rect(leftTop, top, rightTop - leftTop, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(leftBottom, bottom - thickness, rightBottom - leftBottom, thickness), Texture2D.whiteTexture);
            float angle = Mathf.Atan(slant) * Mathf.Rad2Deg;
            float length = height * secant;
            DrawSlantedLine(new Vector2((leftTop + leftBottom) * 0.5f, (top + bottom) * 0.5f), length, thickness, angle);
            DrawSlantedLine(new Vector2((rightTop + rightBottom) * 0.5f, (top + bottom) * 0.5f), length, thickness, angle);
            GUI.color = previous;
        }

        // 中心 center・長さ length・太さ thickness の縦の線を、angle 度だけ時計回りに傾けて描く（上が右へ寄る）。色は GUI.color。
        private static void DrawSlantedLine(Vector2 center, float length, float thickness, float angle)
        {
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, center);
            GUI.DrawTexture(new Rect(center.x - thickness * 0.5f, center.y - length * 0.5f, thickness, length), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }

        private Texture2D RainbowFrame(float time)
        {
            if (_rainbow.Count == 0)
            {
                for (int f = 0; f < RainbowFrames; f++)
                {
                    float phase = f / (float)RainbowFrames;
                    _rainbow.Add(Bake($"StatusPlateRainbow{f}", (x, y) =>
                    {
                        Color c = Color.HSVToRGB(Mathf.Repeat(phase + (x + y) / (BaseWidth + BaseHeight), 1f), 0.7f, 1f);
                        c.a = FrameAlpha(x, y, _scale);
                        return c;
                    }));
                }
            }
            int index = Mathf.FloorToInt(Mathf.Repeat(time * 0.8f, 1f) * RainbowFrames) % RainbowFrames;
            return _rainbow[index];
        }

        // WANTED の赤い強調: 札が赤く脈打ち、縁が赤くなり、札の上に斜めの「WANTED!」のスタンプ（二重の枠と文字。押すときは大きくから縮む）。
        private void DrawWantedEmphasis(Rect rect, Rect drawRect, Look look)
        {
            float strength = Mathf.Clamp01(look.WantedEmphasis);
            float time = Time.time;
            var red = new Color(0.95f, 0.12f, 0.08f);
            DrawTexture(drawRect, _fill, new Color(red.r, red.g, red.b, (0.16f + 0.1f * (0.5f + 0.5f * Mathf.Sin(time * 5f))) * strength));
            DrawTexture(drawRect, _frame, new Color(red.r, red.g, red.b, strength));
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
            var box = new Rect(center.x - rect.width * 0.32f, center.y - rect.height * 0.3f, rect.width * 0.64f, rect.height * 0.6f);
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
            Object.Destroy(_fill);
            Object.Destroy(_frame);
            Object.Destroy(_separatorsBag);
            Object.Destroy(_separatorsPlain);
            foreach (Texture2D texture in _rainbow)
            {
                Object.Destroy(texture);
            }
            _rainbow.Clear();
            Object.Destroy(_crown);
            Object.Destroy(_poster);
            Object.Destroy(_crack);
            foreach (Texture2D icon in _icons)
            {
                Object.Destroy(icon);
            }
        }

        private static Color White(float alpha)
        {
            return new Color(1f, 1f, 1f, alpha);
        }

        // 札の形を焼く: テクスチャの点 (i, j) を、倍率 1 の座標（余白 Pad の分ずらす）にして color を呼ぶ（j は上から）。
        private Texture2D Bake(string name, System.Func<float, float, Color> color)
        {
            int width = Mathf.CeilToInt((BaseWidth + Pad * 2f) * _scale);
            int height = Mathf.CeilToInt((BaseHeight + Pad * 2f) * _scale);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            float sx = width / (BaseWidth + Pad * 2f);
            float sy = height / (BaseHeight + Pad * 2f);
            var pixels = new Color[width * height];
            for (int j = 0; j < height; j++)
            {
                float y = (j + 0.5f) / sy - Pad;
                int row = (height - 1 - j) * width;
                for (int i = 0; i < width; i++)
                {
                    pixels[row + i] = color((i + 0.5f) / sx - Pad, y);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void EnsureStyles()
        {
            if (_rank != null)
            {
                return;
            }
            _rank = Style(13, TextAnchor.MiddleCenter);
            _number = Style(ValueFont, TextAnchor.MiddleLeft);
            _number.clipping = TextClipping.Overflow;
            _name = Style(13, TextAnchor.MiddleLeft);
            _name.clipping = TextClipping.Clip;
            _overlay = Style(16, TextAnchor.MiddleCenter);
            _gain = Style(13, TextAnchor.MiddleCenter);
            _gain.clipping = TextClipping.Overflow;
            _wanted = Style(7, TextAnchor.MiddleCenter);
            _stampText = Style(22, TextAnchor.MiddleCenter);
            _stampText.fontStyle = FontStyle.BoldAndItalic;
        }

        private GUIStyle Style(int size, TextAnchor anchor)
        {
            GUIStyle style = GameFonts.Use(new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * _scale),
                fontStyle = FontStyle.Bold,
                alignment = anchor,
                wordWrap = false,
            });
            style.padding = new RectOffset(0, 0, 0, 0);
            return style;
        }
    }
}
