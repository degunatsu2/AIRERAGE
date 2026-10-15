using Airerage.Core.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 1 人分の情報の札（右上の表 ScoreboardHud と、結果の画面 MatchHud で同じ見た目にする。ユーザーの要望、2026-10-14）。
    /// 誘導表示の札（GuidePlate）と同じ形で、縁と斜めの線はプレイヤーの色。左にキャラクターの絵（今はプレイヤーの色の四角と番号）と
    /// プレイヤーネーム、右を 4 つに分けて BANK / COIN / K / F（BANK の文字と数だけ黄色）。王冠は札の左に出す。
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
        private readonly float _fontScale;
        private GUIStyle _label;
        private GUIStyle _number;
        private GUIStyle _iconText;
        private GUIStyle _name;
        private GUIStyle _overlay;

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
        }

        public PlayerStatusPlate(int width, int height, float fontScale = 1f, int crackSeed = 7)
        {
            Width = width;
            Height = height;
            _fontScale = fontScale;
            _plate = new GuidePlate(width, height);
            _crown = GuideIcons.Crown(64);
            _crack = GuideIcons.Crack(width, height, crackSeed);
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>札の左に出す王冠の幅（ピクセル）。</summary>
        public float CrownWidth => Height * 0.8f + 4f;

        /// <summary>number は四角に書く番号、name はプレイヤーネーム。bankColor は BANK の文字と数の色。</summary>
        public void Draw(Rect rect, int number, string name, Color color, in CharacterState state, bool crown, Look look, Color bankColor)
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

            // 左: キャラクターの絵（今はプレイヤーの色の四角と番号）と、その下にプレイヤーネーム。
            Rect area = _plate.IconRect(rect);
            float size = area.height * 0.66f;
            var icon = new Rect(area.center.x - size * 0.5f, area.y, size, size);
            GUI.color = Color.Lerp(Color.Lerp(color, Color.black, 0.45f), Gray * 0.6f, look.Gray);
            GUI.DrawTexture(icon, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(color, Gray, look.Gray);
            GUI.DrawTexture(new Rect(icon.x + 2f, icon.y + 2f, icon.width - 4f, icon.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
            GuidePlate.DrawText(icon, number.ToString(), _iconText, Color.white);
            float nameWidth = (area.center.x - rect.x) * 2f - 4f;
            var nameRect = new Rect(area.center.x - nameWidth * 0.5f, icon.yMax, nameWidth, rect.yMax - icon.yMax - 2f);
            GuidePlate.DrawText(nameRect, name, _name, Color.Lerp(Color.white, Gray, look.Gray));

            if (crown)
            {
                float crownSize = rect.height * 0.8f;
                GUI.DrawTexture(new Rect(rect.x - crownSize - 4f, rect.y + (rect.height - crownSize) * 0.5f, crownSize, crownSize), _crown, ScaleMode.ScaleToFit);
            }

            // 右: 4 つに分けて BANK / COIN / K / F。
            Rect right = _plate.RightRect(rect);
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
                    GUI.color = new Color(1f, 1f, 1f, 0.18f);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y + 4f, 1f, cellRect.height - 8f), Texture2D.whiteTexture);
                    GUI.color = previous;
                }
            }

            if (look.Crack > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.85f * look.Crack);
                GUI.DrawTexture(rect, _crack);
                GUI.color = previous;
            }
            if (look.Gray > 0f && !string.IsNullOrEmpty(look.Overlay))
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f * look.Gray);
                GUI.DrawTexture(new Rect(right.x, right.y + right.height * 0.2f, right.width, right.height * 0.6f), Texture2D.whiteTexture);
                GUI.color = previous;
                GuidePlate.DrawText(right, look.Overlay, _overlay, new Color(1f, 1f, 1f, look.Gray));
            }
            if (look.Flash > 0f)
            {
                _plate.DrawFill(rect, new Color(1f, 1f, 1f, 0.85f * look.Flash));
            }
            if (look.Jackpot > 0f)
            {
                // 虹色に光る（中が虹色に脈打ち、縁が虹色になり、右の部分に「JACKPOT!」。ユーザーの要望、2026-10-15）。
                float time = Time.time;
                Color hue = Color.HSVToRGB(Mathf.Repeat(time / 0.9f, 1f), 0.7f, 1f);
                _plate.DrawFill(rect, new Color(hue.r, hue.g, hue.b, (0.25f + 0.2f * Mathf.Sin(time * 14f)) * look.Jackpot));
                _plate.DrawRainbowFrame(rect, time, look.Jackpot);
                Rect jackpotArea = _plate.RightRect(rect);
                GUI.color = new Color(0f, 0f, 0f, 0.6f * look.Jackpot);
                GUI.DrawTexture(new Rect(jackpotArea.x, jackpotArea.y + jackpotArea.height * 0.15f, jackpotArea.width, jackpotArea.height * 0.7f), Texture2D.whiteTexture);
                GUI.color = previous;
                Color text = Color.HSVToRGB(Mathf.Repeat(time / 0.9f + 0.5f, 1f), 0.45f, 1f);
                text.a = look.Jackpot;
                GuidePlate.DrawText(jackpotArea, "JACKPOT!", _overlay, text);
            }
            GUI.color = previous;
        }

        public void Dispose()
        {
            _plate.Dispose();
            Object.Destroy(_crown);
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
        }

        private GUIStyle Style(int size, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(size * _fontScale),
                fontStyle = FontStyle.Bold,
                alignment = anchor,
                wordWrap = false,
            };
        }
    }
}
