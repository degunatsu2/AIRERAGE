using Airerage.Runtime.Data;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Airerage.Editor
{
    /// <summary>
    /// キャラクター選択の画面（アイコンを選ぶ形。docs/15 6.9）を作る。基準の画面の大きさは 1920×1080（横に長い画面で高さが 1000 まで
    /// 縮んでも重ならない）:
    /// - 真ん中にキャラクターのアイコン（横に 4 つずつ。四隅は空ける）と人ごとのカーソル（色の枠と「1P」などの札）。
    /// - 四隅に 1P（左上）・2P（右上）・3P（左下）・4P（右下）の枠（ロゴ、名前、ハッシュタグ、機器か名前、状態）。
    /// - 下からせり出す技の説明（ロゴ、名前、ハッシュタグ、Q・E・R の技の名前・説明・クールタイム）。ふだんは隠す。
    /// </summary>
    internal static class CharacterSelectUiBuilder
    {
        private const int Columns = 4;
        private const float IconSize = 150f;
        private const float IconGap = 18f;
        private static readonly Vector2 CornerSize = new Vector2(430f, 290f);
        private const float CornerMargin = 24f;

        public static CharacterSelectScreen Create(Transform canvas)
        {
            CharacterRoster roster = SandboxWireSceneBuilder.LoadOrCreateCharacterRoster(SandboxWireSceneBuilder.LoadOrCreateCharacterConfig());
            RectTransform panel = MenuUiBuilder.CreateFull(canvas, "CharacterSelect");
            MenuUiBuilder.CreateFill(panel, "Dim", new Color(0f, 0f, 0f, 0.5f), Vector2.zero, Vector2.one);
            MenuUiBuilder.CreateText(panel, "Title", "CHARACTER SELECT", 50, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(900f, 70f), Color.white);
            Text timeLimit = MenuUiBuilder.CreateText(panel, "TimeLimit", string.Empty, 26, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(900f, 40f), MenuUiBuilder.Accent);

            var colors = new Color[CharacterSelectScreen.MaxPlayers];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = SandboxOnlineSceneBuilder.PlayerColors[i].Color;
            }

            // アイコンの並び（真ん中。横に Columns 個ずつ。最後は RANDOM。ユーザーの要望、2026-10-19）。
            int count = roster.Count + 1;
            int columns = Mathf.Min(Columns, count);
            int rows = (count + columns - 1) / columns;
            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.transform.SetParent(panel, false);
            var gridRect = (RectTransform)grid.transform;
            gridRect.anchorMin = gridRect.anchorMax = gridRect.pivot = new Vector2(0.5f, 0.5f);
            gridRect.anchoredPosition = new Vector2(0f, 20f);
            gridRect.sizeDelta = new Vector2(columns * IconSize + (columns - 1) * IconGap, rows * IconSize + (rows - 1) * IconGap);
            var icons = new Object[count];
            for (int i = 0; i < count; i++)
            {
                CharacterConfigAsset character = i < roster.Count ? roster.Get(i) : null;
                int row = i / columns;
                int rowLength = Mathf.Min(columns, count - row * columns);
                // 最後の行が短いときは真ん中に寄せる。
                float rowOffset = (columns - rowLength) * (IconSize + IconGap) * 0.5f;
                float x = rowOffset + (i % columns) * (IconSize + IconGap) + IconSize * 0.5f;
                float y = row * (IconSize + IconGap) + IconSize * 0.5f;
                icons[i] = CreateIcon(gridRect, i, character, new Vector2(x, -y));
            }
            var cursors = new Object[CharacterSelectScreen.MaxPlayers];
            var cursorTags = new Object[CharacterSelectScreen.MaxPlayers];
            for (int p = 0; p < CharacterSelectScreen.MaxPlayers; p++)
            {
                CreateCursor(gridRect, p, colors[p], out RectTransform cursor, out Text tag);
                cursors[p] = cursor;
                cursorTags[p] = tag;
            }
            Text message = MenuUiBuilder.CreateText(panel, "Message", string.Empty, 24, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -gridRect.sizeDelta.y * 0.5f - 40f), new Vector2(1000f, 40f), Color.white);

            // 四隅の枠。
            var corners = new Object[CharacterSelectScreen.MaxPlayers];
            var plates = new Object[CharacterSelectScreen.MaxPlayers];
            var devices = new Object[CharacterSelectScreen.MaxPlayers];
            var logos = new Object[CharacterSelectScreen.MaxPlayers];
            var names = new Object[CharacterSelectScreen.MaxPlayers];
            var tags = new Object[CharacterSelectScreen.MaxPlayers];
            var states = new Object[CharacterSelectScreen.MaxPlayers];
            Vector2[] anchors = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            for (int p = 0; p < CharacterSelectScreen.MaxPlayers; p++)
            {
                Vector2 anchor = anchors[p];
                var position = new Vector2(anchor.x == 0f ? CornerMargin : -CornerMargin, anchor.y == 0f ? CornerMargin : -CornerMargin);
                GameObject corner = CreateCorner(panel, p, anchor, position, colors[p], out SlantedPlate plate, out Text device,
                    out RawImage logo, out Text name, out Text tag, out Text state);
                corners[p] = corner;
                plates[p] = plate;
                devices[p] = device;
                logos[p] = logo;
                names[p] = name;
                tags[p] = tag;
                states[p] = state;
            }
            Text hint = MenuUiBuilder.CreateText(panel, "Hint", string.Empty, 19, FontStyle.Bold, TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(1000f, 36f), new Color(1f, 1f, 1f, 0.75f));

            CharacterDetailsParts details = CreateDetails(panel);

            var screen = panel.gameObject.AddComponent<CharacterSelectScreen>();
            SandboxWireSceneBuilder.SetReference(screen, "_roster", roster);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_icons", icons);
            SandboxWireSceneBuilder.SetInt(screen, "_columns", columns);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cursors", cursors);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cursorTags", cursorTags);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_corners", corners);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cornerPlates", plates);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cornerDevices", devices);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cornerLogos", logos);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cornerNames", names);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cornerTags", tags);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_cornerStates", states);
            SandboxWireSceneBuilder.SetReference(screen, "_details", details.Root);
            SandboxWireSceneBuilder.SetReference(screen, "_detailLogo", details.Logo);
            SandboxWireSceneBuilder.SetReference(screen, "_detailName", details.Name);
            SandboxWireSceneBuilder.SetReference(screen, "_detailTags", details.Tags);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_skillNames", details.SkillNames);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_skillDescriptions", details.Descriptions);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_skillCooldowns", details.Cooldowns);
            SandboxWireSceneBuilder.SetReference(screen, "_hint", hint);
            SandboxWireSceneBuilder.SetReference(screen, "_timeLimit", timeLimit);
            SandboxWireSceneBuilder.SetReference(screen, "_message", message);
            SandboxOnlineSceneBuilder.SetColors(screen, "_colors", colors);
            panel.gameObject.SetActive(false);
            return screen;
        }

        // アイコン 1 つ（斜めの札に、ロゴと小さな名前）。中心を center に置く。character が null なら RANDOM（大きな「?」）。
        private static CharacterIcon CreateIcon(RectTransform grid, int index, CharacterConfigAsset character, Vector2 center)
        {
            var go = new GameObject($"Icon{index + 1}", typeof(RectTransform));
            go.transform.SetParent(grid, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = new Vector2(IconSize, IconSize);
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.1f, 2f, new Color(0.05f, 0.08f, 0.13f, 0.9f), new Color(1f, 1f, 1f, 0.3f));
            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(go.transform, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = logoRect.anchorMax = logoRect.pivot = new Vector2(0.5f, 1f);
            logoRect.anchoredPosition = new Vector2(IconSize * 0.05f, -12f);
            logoRect.sizeDelta = new Vector2(IconSize * 0.62f, IconSize * 0.62f);
            if (character != null)
            {
                var logo = logoGo.AddComponent<RawImage>();
                logo.texture = character.Logo;
                logo.raycastTarget = false;
            }
            else
            {
                MenuUiBuilder.CreateText(logoGo.transform, "Question", "?", 72, FontStyle.Bold, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(IconSize * 0.62f, IconSize * 0.62f), MenuUiBuilder.Accent);
            }
            BestFit(MenuUiBuilder.CreateText(go.transform, "Name", character != null ? character.DisplayName : "RANDOM", 16, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(IconSize - 16f, 26f), Color.white), 10);
            var icon = go.AddComponent<CharacterIcon>();
            icon.Configure(index);
            return icon;
        }

        // カーソル（枠だけの斜めの札と、番号ごとにアイコンの四隅の 1 つに置く「1P」などの札）。
        private static void CreateCursor(RectTransform grid, int player, Color color, out RectTransform cursor, out Text tag)
        {
            var go = new GameObject($"Cursor{player + 1}P", typeof(RectTransform));
            go.transform.SetParent(grid, false);
            cursor = (RectTransform)go.transform;
            cursor.sizeDelta = new Vector2(IconSize, IconSize);
            var frame = go.AddComponent<SlantedPlate>();
            frame.Configure(0.1f, 4f, new Color(0f, 0f, 0f, 0f), color);
            frame.raycastTarget = false;
            Vector2[] corners = { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            Vector2 corner = corners[player];
            var offset = new Vector2(corner.x == 0f ? -4f : 4f, corner.y == 0f ? -4f : 4f);
            tag = MenuUiBuilder.CreateText(go.transform, "Tag", $"{player + 1}P", 24, FontStyle.BoldAndItalic,
                corner.x == 0f ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, corner, offset, new Vector2(60f, 30f), color);
            go.SetActive(false);
        }

        // 四隅の枠 1 つ（番号、機器か名前、ロゴ、名前、ハッシュタグ、状態）。
        private static GameObject CreateCorner(RectTransform panel, int player, Vector2 anchor, Vector2 position, Color color,
            out SlantedPlate plate, out Text device, out RawImage logo, out Text name, out Text tags, out Text state)
        {
            var go = new GameObject($"Corner{player + 1}P", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = CornerSize;
            plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.06f, 3f, new Color(0.03f, 0.05f, 0.09f, 0.45f), new Color(1f, 1f, 1f, 0.2f));
            plate.raycastTarget = false;
            MenuUiBuilder.CreateText(go.transform, "Number", $"{player + 1}P", 46, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(34f, -10f), new Vector2(120f, 56f), color);
            device = MenuUiBuilder.CreateText(go.transform, "Device", string.Empty, 18, FontStyle.Bold, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(-26f, -22f), new Vector2(240f, 30f), new Color(1f, 1f, 1f, 0.75f));
            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(go.transform, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = logoRect.anchorMax = logoRect.pivot = new Vector2(0f, 1f);
            logoRect.anchoredPosition = new Vector2(30f, -70f);
            logoRect.sizeDelta = new Vector2(140f, 140f);
            logo = logoGo.AddComponent<RawImage>();
            logo.raycastTarget = false;
            logo.enabled = false;
            name = BestFit(MenuUiBuilder.CreateText(go.transform, "Name", string.Empty, 28, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(184f, -84f), new Vector2(226f, 44f), Color.white), 16);
            tags = MenuUiBuilder.CreateText(go.transform, "Tags", string.Empty, 20, FontStyle.Bold, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(184f, -126f), new Vector2(226f, 80f), MenuUiBuilder.Accent);
            tags.horizontalOverflow = HorizontalWrapMode.Wrap;
            state = MenuUiBuilder.CreateText(go.transform, "State", string.Empty, 26, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(34f, 18f), new Vector2(380f, 40f), Color.white);
            return go;
        }

        private sealed class CharacterDetailsParts
        {
            public RectTransform Root;
            public RawImage Logo;
            public Text Name;
            public Text Tags;
            public Object[] SkillNames;
            public Object[] Descriptions;
            public Object[] Cooldowns;
        }

        // 下からせり出す技の説明（高さ 450。画面の下に置き、ふだんは隠す）。左にロゴ、その右に名前とハッシュタグ、その下に Q・E・R の 3 列。
        private static CharacterDetailsParts CreateDetails(RectTransform panel)
        {
            RectTransform info = MenuUiBuilder.CreateFull(panel, "Details");
            info.anchorMin = new Vector2(0f, 0f);
            info.anchorMax = new Vector2(1f, 0f);
            info.pivot = new Vector2(0.5f, 0f);
            info.offsetMin = new Vector2(0f, 0f);
            info.offsetMax = new Vector2(0f, 450f);
            MenuUiBuilder.CreateFill(info, "Band", new Color(0.01f, 0.02f, 0.04f, 0.92f), Vector2.zero, Vector2.one);
            MenuUiBuilder.CreateFill(info, "Edge", MenuUiBuilder.Accent, new Vector2(0f, 1f), new Vector2(1f, 1f)).rectTransform.offsetMin =
                new Vector2(0f, -3f);

            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(info, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = logoRect.anchorMax = logoRect.pivot = new Vector2(0f, 1f);
            logoRect.anchoredPosition = new Vector2(90f, -22f);
            logoRect.sizeDelta = new Vector2(150f, 150f);
            var logo = logoGo.AddComponent<RawImage>();
            logo.raycastTarget = false;

            // 名前とハッシュタグ（横に並べる。ハッシュタグは名前の長さに合わせて名前のすぐ右）。
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(info, false);
            var headerRect = (RectTransform)header.transform;
            headerRect.anchorMin = headerRect.anchorMax = headerRect.pivot = new Vector2(0f, 1f);
            headerRect.anchoredPosition = new Vector2(270f, -58f);
            headerRect.sizeDelta = new Vector2(1500f, 90f);
            var row = header.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.LowerLeft;
            row.spacing = 36f;
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            Text name = MenuUiBuilder.CreateText(header.transform, "Name", string.Empty, 60, FontStyle.BoldAndItalic,
                TextAnchor.LowerLeft, new Vector2(0f, 0f), Vector2.zero, new Vector2(500f, 84f), Color.white);
            Text tags = MenuUiBuilder.CreateText(header.transform, "Tags", string.Empty, 30, FontStyle.Bold,
                TextAnchor.LowerLeft, new Vector2(0f, 0f), Vector2.zero, new Vector2(500f, 52f), MenuUiBuilder.Accent);
            MenuUiBuilder.CreateText(info, "Close", "F1 / SELECT: CLOSE", 18, FontStyle.Bold, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(-40f, -16f), new Vector2(400f, 28f), new Color(1f, 1f, 1f, 0.6f));

            var skills = new GameObject("Skills", typeof(RectTransform));
            skills.transform.SetParent(info, false);
            var skillsRect = (RectTransform)skills.transform;
            skillsRect.anchorMin = new Vector2(0f, 1f);
            skillsRect.anchorMax = new Vector2(1f, 1f);
            skillsRect.pivot = new Vector2(0.5f, 1f);
            skillsRect.offsetMin = new Vector2(90f, -190f - 236f);
            skillsRect.offsetMax = new Vector2(-90f, -190f);
            string[] keys = { "Q", "E", "R" };
            int count = CharacterSelectScreen.SkillSlots.Length;
            var parts = new CharacterDetailsParts
            {
                Root = info, Logo = logo, Name = name, Tags = tags,
                SkillNames = new Object[count], Descriptions = new Object[count], Cooldowns = new Object[count],
            };
            for (int i = 0; i < count; i++)
            {
                RectTransform column = MenuUiBuilder.CreateFull(skills.transform, $"Skill{keys[i]}");
                column.anchorMin = new Vector2((float)i / count, 0f);
                column.anchorMax = new Vector2((float)(i + 1) / count, 1f);
                column.offsetMin = Vector2.zero;
                column.offsetMax = new Vector2(-30f, 0f);
                var plate = column.gameObject.AddComponent<SlantedPlate>();
                plate.Configure(0.06f, 2f, new Color(0.03f, 0.05f, 0.09f, 0.7f), new Color(1f, 1f, 1f, 0.25f));
                plate.raycastTarget = false;
                MenuUiBuilder.CreateText(column, "Key", keys[i], 36, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(26f, -12f), new Vector2(50f, 48f), MenuUiBuilder.Accent);
                Text skillName = MenuUiBuilder.CreateText(column, "Name", string.Empty, 32, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), Vector2.zero, Vector2.zero, Color.white);
                StretchTop(skillName, 80f, 20f, 12f, 48f);
                // 説明は 4 行まで（細い字。クールタイムの行は列の下に固定する）。
                Text description = MenuUiBuilder.CreateText(column, "Description", string.Empty, 22, FontStyle.Normal,
                    TextAnchor.UpperLeft, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.92f));
                StretchTop(description, 26f, 30f, 68f, 120f);
                description.horizontalOverflow = HorizontalWrapMode.Wrap;
                Text cooldown = MenuUiBuilder.CreateText(column, "Cooldown", string.Empty, 22, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Color(1f, 0.85f, 0.3f));
                StretchTop(cooldown, 26f, 20f, 192f, 34f);
                parts.SkillNames[i] = skillName;
                parts.Descriptions[i] = description;
                parts.Cooldowns[i] = cooldown;
            }
            info.gameObject.SetActive(false);
            return parts;
        }

        // 長い名前は枠に収まるまで小さくする（最小 min。1 行で収まる大きさを先に選ぶ）。
        private static Text BestFit(Text text, int min)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = min;
            text.resizeTextMaxSize = text.fontSize;
            return text;
        }

        // 文字を親の横幅いっぱいに広げる（左右の余白 left・right、親の上から top の位置に高さ height）。
        private static void StretchTop(Text text, float left, float right, float top, float height)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
