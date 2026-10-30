using System.Collections.Generic;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 上の中央のタイマー（ユーザーの選択、2026-10-14。案 T1 ＋ 細い流れのバー）: 画面の上の辺に付いた台形の札（黒い中身と縁）に、
    /// 小さな見出し（TIME / OVERTIME）と大きな数字、その下に試合全体の残りを示す細い線。札の下には試合の流れのバー（試合の 3 分を
    /// 1 本の線にして、今の位置、拠点が移る時刻の ◆、100 枚のコインが出る時刻の虹色の点、COIN RUSH の金色の区間）。
    /// 形のテクスチャは大きさごとに1回だけ作り、色は描くときに GUI.color でつける。座標は OnGUI の座標。Dispose でテクスチャを消す。
    /// </summary>
    public sealed class TimerPlate : System.IDisposable
    {
        public const int Width = 240;
        public const int Height = 78;
        public const float TimelineWidth = 300f;
        private const int Border = 3;
        /// <summary>台形の下の辺の両端が、上の辺より内側に入る割合（幅に対して）。</summary>
        public const float Slant = 0.12f;

        /// <summary>台形の右の斜めの縁が、1 ピクセル下がるごとに左へ寄る量（ピクセル）。お知らせの札の斜めの縁を合わせるのに使う。</summary>
        public const float EdgeSlope = Width * Slant / Height;

        private static readonly Color Frame = new Color(0.62f, 0.68f, 0.8f, 0.9f);
        private static readonly Color Fill = new Color(0.03f, 0.05f, 0.09f, 0.82f);
        private static readonly Color BankMark = new Color(0.31f, 0.84f, 1f);
        private static readonly Color RushZone = new Color(1f, 0.8f, 0.2f, 0.65f);

        private readonly Texture2D _fill;
        private readonly Texture2D _frame;
        private readonly Texture2D _diamond;
        private readonly Texture2D _dot;
        private GUIStyle _label;
        private GUIStyle _digits;

        public TimerPlate()
        {
            _fill = Create("TimerPlateFill", Width, Height, (x, y) => Inside(x, y, 0f) ? 1f : 0f);
            _frame = Create("TimerPlateFrame", Width, Height, (x, y) => Inside(x, y, 0f) && !Inside(x, y, Border) ? 1f : 0f);
            _diamond = Create("TimerDiamond", 16, 16, (x, y) => Mathf.Abs(x + 0.5f - 8f) + Mathf.Abs(y + 0.5f - 8f) <= 7.5f ? 1f : 0f);
            _dot = Create("TimerDot", 16, 16, (x, y) =>
            {
                float dx = x + 0.5f - 8f;
                float dy = y + 0.5f - 8f;
                return dx * dx + dy * dy <= 56f ? 1f : 0f;
            });
        }

        /// <summary>試合の流れのバーに置く印（試合の始まり 0 から時間切れ 1 までの位置）。</summary>
        public struct Timeline
        {
            /// <summary>今の位置（0〜1。負なら流れのバーを出さない）。</summary>
            public float Progress;

            /// <summary>COIN RUSH の区間の始まり（0〜1。負ならない）。</summary>
            public float RushStart;

            /// <summary>100 枚のコインが出る位置（0〜1。負ならない）。</summary>
            public float Jackpot;

            /// <summary>拠点が移る位置（0〜1）。</summary>
            public List<float> BankMoves;
        }

        /// <summary>試合の流れのバーの印を、ルールから求める（時間の制限がなければ Progress が負）。</summary>
        public static Timeline TimelineOf(MatchRules rules, int tick, List<float> bankMoves)
        {
            bankMoves.Clear();
            var timeline = new Timeline { Progress = -1f, RushStart = -1f, Jackpot = -1f, BankMoves = bankMoves };
            if (rules == null || !rules.HasTimeLimit)
            {
                return timeline;
            }
            float duration = rules.DurationTicks;
            timeline.Progress = Mathf.Clamp01((tick - rules.CountdownTicks) / duration);
            if (rules.LateGameTicks > 0)
            {
                timeline.RushStart = Mathf.Clamp01(1f - rules.LateGameTicks / duration);
            }
            int jackpot = FieldCoinRules.JackpotSpawnTick(rules);
            if (jackpot >= 0)
            {
                timeline.Jackpot = Mathf.Clamp01((jackpot - rules.CountdownTicks) / duration);
            }
            for (int move = rules.BankMoveIntervalTicks; move < rules.DurationTicks; move += rules.BankMoveIntervalTicks)
            {
                bankMoves.Add(move / duration);
            }
            return timeline;
        }

        /// <summary>
        /// 札を、上の辺の中央 centerX に描く。label は見出し、digits は数字、remaining は試合全体の残り（0〜1。負なら線を出さない）、
        /// scale は数字の脈打ち（1 がふつう）。返り値は札の下の y。
        /// </summary>
        /// <summary>上の辺の中央が centerX のときの、高さ y（OnGUI の座標）での台形の右の縁の x。</summary>
        public static float RightEdgeAt(float centerX, float y)
        {
            return centerX + Width * 0.5f - EdgeSlope * Mathf.Clamp(y, 0f, Height);
        }

        public float Draw(float centerX, string label, string digits, Color digitsColor, float remaining, float scale, Timeline timeline)
        {
            EnsureStyles();
            Color previous = GUI.color;
            var rect = new Rect(centerX - Width * 0.5f, 0f, Width, Height);
            GUI.color = HudFade.Apply(Fill);
            GUI.DrawTexture(rect, _fill);
            GUI.color = HudFade.Apply(Frame);
            GUI.DrawTexture(rect, _frame);
            GUI.color = previous;

            GuidePlate.DrawText(new Rect(rect.x, rect.y + 4f, rect.width, 14f), label, _label, new Color(1f, 1f, 1f, 0.6f));
            Matrix4x4 matrix = GUI.matrix;
            var digitsRect = new Rect(rect.x, rect.y + 14f, rect.width, 46f);
            if (scale != 1f)
            {
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), digitsRect.center);
            }
            GuidePlate.DrawText(digitsRect, digits, _digits, digitsColor);
            GUI.matrix = matrix;

            if (remaining >= 0f)
            {
                var bar = new Rect(centerX - 70f, rect.y + 62f, 140f, 4f);
                GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.18f));
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = HudFade.Apply(digitsColor);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(remaining), bar.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }

            float bottom = rect.yMax;
            if (timeline.Progress >= 0f)
            {
                bottom = DrawTimeline(new Rect(centerX - TimelineWidth * 0.5f, rect.yMax + 6f, TimelineWidth, 14f), timeline);
            }
            GUI.color = previous;
            return bottom;
        }

        public void Dispose()
        {
            Object.Destroy(_fill);
            Object.Destroy(_frame);
            Object.Destroy(_diamond);
            Object.Destroy(_dot);
        }

        private float DrawTimeline(Rect area, Timeline timeline)
        {
            Color previous = GUI.color;
            float midY = area.center.y;
            GUI.color = HudFade.Apply(new Color(0.02f, 0.04f, 0.08f, 0.6f));
            GUI.DrawTexture(new Rect(area.x, midY - 2f, area.width, 4f), Texture2D.whiteTexture);
            if (timeline.RushStart >= 0f)
            {
                GUI.color = HudFade.Apply(RushZone);
                float x = area.x + area.width * timeline.RushStart;
                GUI.DrawTexture(new Rect(x, midY - 4f, area.xMax - x, 8f), Texture2D.whiteTexture);
            }
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.9f));
            GUI.DrawTexture(new Rect(area.x, midY - 2f, area.width * timeline.Progress, 4f), Texture2D.whiteTexture);

            if (timeline.BankMoves != null)
            {
                foreach (float move in timeline.BankMoves)
                {
                    GUI.color = HudFade.Apply(move <= timeline.Progress ? new Color(BankMark.r, BankMark.g, BankMark.b, 0.45f) : BankMark);
                    GUI.DrawTexture(new Rect(area.x + area.width * move - 6f, midY - 6f, 12f, 12f), _diamond);
                }
            }
            if (timeline.Jackpot >= 0f)
            {
                GUI.color = HudFade.Apply(Color.HSVToRGB(Mathf.Repeat(Time.time / 1.6f, 1f), 0.7f, 1f));
                GUI.DrawTexture(new Rect(area.x + area.width * timeline.Jackpot - 7f, midY - 7f, 14f, 14f), _dot);
            }
            GUI.color = HudFade.Apply(Color.white);
            GUI.DrawTexture(new Rect(area.x + area.width * timeline.Progress - 2f, midY - 9f, 4f, 18f), Texture2D.whiteTexture);
            GUI.color = previous;
            return area.yMax;
        }

        // 台形（上の辺が広く、下の辺の両端が Slant の割合だけ内側）の中か。inset は内側へ縮める量（ピクセル）。
        private static bool Inside(int x, int yFromTop, float inset)
        {
            float px = x + 0.5f;
            float py = yFromTop + 0.5f;
            if (py < 0f || py > Height - inset)
            {
                return false;
            }
            float edge = Width * Slant * (py / Height);
            return px >= edge + inset && px <= Width - edge - inset;
        }

        private void EnsureStyles()
        {
            if (_label != null)
            {
                return;
            }
            _label = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            _digits = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            _label.normal.textColor = Color.white;
            _digits.normal.textColor = Color.white;
        }

        internal static Texture2D Create(string name, int width, int height, System.Func<int, int, float> alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            for (int yFromTop = 0; yFromTop < height; yFromTop++)
            {
                int row = (height - 1 - yFromTop) * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[row + x] = new Color(1f, 1f, 1f, alpha(x, yFromTop));
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
