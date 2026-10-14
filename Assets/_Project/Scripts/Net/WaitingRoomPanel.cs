using System;
using System.Collections.Generic;
using Degunatsu.Runtime.Presentation;
using UnityEngine;

namespace Degunatsu.Net
{
    /// <summary>
    /// 待機所（ロビー）の表示（ユーザーの要望、2026-10-14）。ホストもクライアントも、画面の中央に同じ顔ぶれを出す
    /// （OnlineSession.Roster）。番号ごとに 1 枚の札（GuidePlate。右上の表と同じ形）:
    /// - つながっている人: 縁と斜めの線はプレイヤーの色、左にプレイヤーの色の四角と番号、右にプレイヤーネーム（なければ P1〜P4）と
    ///   「HOST」「YOU」の印。
    /// - まだいない番号: 灰色の縁で、「WAITING...」がゆっくり明滅する。
    /// 札が埋まった・空いた瞬間は、札が白く光る。座標は OnGUI の座標。使い終わったら Dispose でテクスチャを消す。
    /// </summary>
    public sealed class WaitingRoomPanel : IDisposable
    {
        public const int PlateWidth = 420;
        public const int PlateHeight = 50;
        private const float PlateGap = 8f;
        private const float FlashSeconds = 0.45f;

        private static readonly Color Gray = new Color(0.45f, 0.45f, 0.5f);

        private readonly GuidePlate _plate = new GuidePlate(PlateWidth, PlateHeight);
        private readonly bool[] _connected = new bool[OnlineSession.MaxPlayers];
        private readonly float[] _changedAt = new float[OnlineSession.MaxPlayers];
        private GUIStyle _title;
        private GUIStyle _count;
        private GUIStyle _subtitle;
        private GUIStyle _name;
        private GUIStyle _tag;
        private GUIStyle _number;
        private GUIStyle _waiting;

        public WaitingRoomPanel()
        {
            for (int p = 0; p < _changedAt.Length; p++)
            {
                _changedAt[p] = float.NegativeInfinity;
            }
        }

        /// <summary>札の並びの高さ（題と、MaxPlayers 枚の札）。下にボタンなどを置くのに使う。</summary>
        public static float Height => 70f + OnlineSession.MaxPlayers * (PlateHeight + PlateGap);

        /// <summary>
        /// 題と札を描く（左上が origin）。colorOf はプレイヤーの色、subtitle は題の下の小さな説明。
        /// 返り値は、札の下の y（OnGUI の座標）。
        /// </summary>
        public float Draw(Vector2 origin, IReadOnlyList<LobbySlot> roster, int localPlayer, Func<int, Color> colorOf, string subtitle)
        {
            EnsureStyles();
            int connected = 0;
            for (int p = 0; p < roster.Count; p++)
            {
                connected += roster[p].Connected ? 1 : 0;
            }
            GuidePlate.DrawText(new Rect(origin.x, origin.y, PlateWidth, 36f), "WAITING ROOM", _title, Color.white);
            GuidePlate.DrawText(new Rect(origin.x, origin.y, PlateWidth, 36f), $"{connected} / {roster.Count}", _count, Color.white);
            GuidePlate.DrawText(new Rect(origin.x, origin.y + 34f, PlateWidth, 22f), subtitle, _subtitle, new Color(1f, 1f, 1f, 0.75f));

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(origin.x, origin.y + 60f, PlateWidth, 2f), Texture2D.whiteTexture);
            GUI.color = previous;

            float y = origin.y + 70f;
            for (int p = 0; p < roster.Count && p < OnlineSession.MaxPlayers; p++)
            {
                TrackChange(p, roster[p].Connected);
                DrawSlot(new Rect(origin.x, y, PlateWidth, PlateHeight), p, roster[p], p == localPlayer, colorOf(p));
                y += PlateHeight + PlateGap;
            }
            return y;
        }

        public void Dispose()
        {
            _plate.Dispose();
        }

        private void TrackChange(int slot, bool connected)
        {
            if (connected != _connected[slot])
            {
                _connected[slot] = connected;
                _changedAt[slot] = Time.unscaledTime;
            }
        }

        private void DrawSlot(Rect rect, int slot, LobbySlot entry, bool isLocal, Color color)
        {
            Color previous = GUI.color;
            _plate.DrawBackground(rect);
            Rect icon = _plate.IconRect(rect);
            Rect right = _plate.RightRect(rect);
            if (entry.Connected)
            {
                _plate.DrawFrame(rect, color);
                GUI.color = Color.Lerp(color, Color.black, 0.45f);
                GUI.DrawTexture(icon, Texture2D.whiteTexture);
                GUI.color = color;
                GUI.DrawTexture(new Rect(icon.x + 2f, icon.y + 2f, icon.width - 4f, icon.height - 4f), Texture2D.whiteTexture);
                GUI.color = previous;
                GuidePlate.DrawText(icon, (slot + 1).ToString(), _number, Color.white);

                string name = string.IsNullOrEmpty(entry.Name) ? $"P{slot + 1}" : entry.Name;
                var nameRect = new Rect(right.x + 10f, right.y, right.width - 120f, right.height);
                GuidePlate.DrawText(nameRect, name, _name, Color.white);
                float tagX = right.xMax - 10f;
                if (isLocal)
                {
                    tagX = DrawTag(tagX, right, "YOU", color);
                }
                if (slot == 0)
                {
                    DrawTag(tagX, right, "HOST", new Color(1f, 0.85f, 0.2f));
                }
            }
            else
            {
                float pulse = 0.45f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f + slot));
                _plate.DrawFrame(rect, Gray, 0.8f);
                GUI.color = new Color(Gray.r, Gray.g, Gray.b, 0.35f);
                GUI.DrawTexture(icon, Texture2D.whiteTexture);
                GUI.color = previous;
                GuidePlate.DrawText(icon, (slot + 1).ToString(), _number, new Color(1f, 1f, 1f, 0.5f));
                int dots = 1 + (int)(Time.unscaledTime * 2f) % 3;
                GuidePlate.DrawText(new Rect(right.x + 10f, right.y, right.width - 20f, right.height),
                    "WAITING" + new string('.', dots), _waiting, new Color(1f, 1f, 1f, pulse));
            }
            float flash = Mathf.Clamp01(1f - (Time.unscaledTime - _changedAt[slot]) / FlashSeconds);
            if (flash > 0f)
            {
                _plate.DrawFill(rect, new Color(1f, 1f, 1f, 0.8f * flash));
            }
            GUI.color = previous;
        }

        // 右の端から左へ、小さな印（枠つきの文字）を置く。次の印を置く x を返す。
        private float DrawTag(float rightX, Rect area, string text, Color color)
        {
            float width = 12f + text.Length * 10f;
            var rect = new Rect(rightX - width, area.center.y - 11f, width, 22f);
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.9f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
            GuidePlate.DrawText(rect, text, _tag, Color.white);
            return rect.x - 6f;
        }

        private void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }
            _title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _count = new GUIStyle(_title) { fontSize = 24, alignment = TextAnchor.MiddleRight };
            _subtitle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _name = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            _tag = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _number = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _waiting = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            foreach (GUIStyle style in new[] { _title, _count, _subtitle, _name, _tag, _number, _waiting })
            {
                style.normal.textColor = Color.white;
            }
        }
    }
}
