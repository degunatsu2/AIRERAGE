using Airerage.Runtime.Data;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Airerage.Editor
{
    /// <summary>
    /// キャラクター選択の画面を作る（D-95 の L3 × G1。2026-11-14 のユーザーの決定）。基準の画面の大きさは 1920×1080（横に長い画面・
    /// 16:10 でも重ならないように、左の一覧は画面の左の真ん中、右の大きな表示は右の真ん中、プレイヤーの札は右下を基準に置く）:
    /// - 左: メインメニューと同じ白い背景（右の端が境目。縫い目とぼかし）に、見出し・ローカル対戦の設定の行・キャラクターの名前の札の
    ///   縦の一覧（最後は RANDOM。札は白い背景の境目と同じ 18° に傾け、高さに応じて横へずらす）と人ごとのカーソル。
    /// - 右（後ろは試合の画面のまま）: 選んでいる人の番号と状態、キャラクターの大きなロゴ（キャラクターの色の菱形の上）、名前（白に
    ///   キャラクターの色の縁取り）、ハッシュタグ、Q・E・R の技の名前。その下に知らせ。
    /// - 右下: プレイヤーの札（番号、機器か名前、ロゴ、名前、ハッシュタグ、状態）を横に並べる（5 人以上は小さくして 2 段）。
    /// - 下からせり出す技の説明（F1 ／ Select。ふだんは隠す）。
    /// </summary>
    internal static class CharacterSelectUiBuilder
    {
        // 名前の札の大きさと間（縦に並べる）。
        private const float PlateWidth = 470f;
        private const float PlateHeight = 74f;
        private const float PlateStep = 86f;
        // 1 枚目の札の真ん中の高さ（画面の下から。基準の画面で）。
        private const float FirstPlateY = 863f;
        // 白い背景の幅（高さの真ん中で）。札の右の端は、その高さの境目からこれだけ内側（縫い目とカーソルの枠の分）。
        private const float PanelWidth = 700f;
        private const float PlateInset = 36f;
        private static readonly Vector2 CornerSize = new Vector2(430f, 290f);
        // 操作の説明（2 行まで）の後ろの黒い帯の高さ。
        private const float HintStripHeight = 72f;
        // 右の大きな表示の大きさ（右の真ん中を基準に置く）と、ロゴの菱形の真ん中と大きさ。
        private static readonly Vector2 ShowcaseSize = new Vector2(1140f, 1080f);
        private static readonly Vector2 ArtCenter = new Vector2(340f, 560f);
        private const float DiamondSize = 380f;
        private const float LogoSize = 400f;

        // 画面の高さの真ん中から above ピクセル上の所で、白い背景（帯と同じ 18° の傾き）が横へずれる量。
        private static float SlantShift(float above) => MainMenuPanel.EdgeLean * above;

        // 枠の幅に収まらない文は、1 行（または改行どおり）のまま字を小さくして収める。
        private static void FitInWidth(Text text, int maxSize)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = maxSize;
        }

        public static CharacterSelectScreen Create(Transform canvas)
        {
            CharacterRoster roster = SandboxWireSceneBuilder.LoadOrCreateCharacterRoster(SandboxWireSceneBuilder.LoadOrCreateCharacterConfig());
            RectTransform panel = MenuUiBuilder.CreateFull(canvas, "CharacterSelect");
            // 後ろは試合の画面（G1）。文字が読みやすいように少しだけ暗くする。
            MenuUiBuilder.CreateFill(panel, "Dim", new Color(0f, 0f, 0f, 0.3f), Vector2.zero, Vector2.one);
            // 左の一覧の後ろの白い背景（画面の左から幅 PanelWidth。右の端が境目。帯は入ってきたモードの色）。
            // 帯は境目の近くだけにして、一覧の後ろは白にする（2026-11-14 のユーザーの要望: 左の一覧の後ろに白い背景）。
            MainMenuPanel backdrop = MenuUiBuilder.CreateWhitePanel(panel, "Backdrop", new Vector2(0f, 0f), new Vector2(0f, 1f), 0.9f, 0.84f,
                MenuUiBuilder.Accent);
            backdrop.rectTransform.offsetMin = Vector2.zero;
            backdrop.rectTransform.offsetMax = new Vector2(PanelWidth, 0f);
            AccentTint.Mark(backdrop);
            MenuUiBuilder.CreateHintStrip(panel, HintStripHeight);
            MenuUiBuilder.StyleTitle(MenuUiBuilder.CreateText(panel, "Title", "CHARACTER SELECT", 50, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(92f, -30f), new Vector2(720f, 70f), MenuUiBuilder.Ink, false), MenuUiBuilder.Accent);
            // 設定の行（ローカル対戦の時間の制限・モード・CPU の数）は、白い背景の幅に収まるように、長いときは縮める。
            Text timeLimit = AccentTint.MarkOnWhite(MenuUiBuilder.CreateText(panel, "TimeLimit", string.Empty, 24, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(64f, -104f), new Vector2(PanelWidth - 40f, 40f), MenuUiBuilder.Accent));
            FitInWidth(timeLimit, 24);

            var colors = new Color[CharacterSelectScreen.MaxPlayers];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = SandboxOnlineSceneBuilder.PlayerColors[i].Color;
            }

            // 名前の札の縦の一覧（左の真ん中を基準に。最後は RANDOM。札は境目と同じ向きに傾け、高さに応じて横へずらす）。
            int count = roster.Count + 1;
            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.transform.SetParent(panel, false);
            var gridRect = (RectTransform)grid.transform;
            gridRect.anchorMin = gridRect.anchorMax = gridRect.pivot = new Vector2(0f, 0.5f);
            gridRect.anchoredPosition = Vector2.zero;
            gridRect.sizeDelta = new Vector2(PanelWidth + 300f, 1080f);
            // 画面の切り替えで、札を 1 つずつめくって並べる（D-87 の T9）。
            grid.AddComponent<TransitionGroup>();
            var icons = new Object[count];
            for (int i = 0; i < count; i++)
            {
                CharacterConfigAsset character = i < roster.Count ? roster.Get(i) : null;
                float y = FirstPlateY - i * PlateStep;
                float right = PanelWidth + SlantShift(y - 540f) - PlateInset;
                icons[i] = CreateIcon(gridRect, i, character, new Vector2(right - PlateWidth * 0.5f, y));
            }
            var cursors = new Object[CharacterSelectScreen.MaxPlayers];
            var cursorTags = new Object[CharacterSelectScreen.MaxPlayers];
            for (int p = 0; p < CharacterSelectScreen.MaxPlayers; p++)
            {
                CreateCursor(gridRect, p, colors[p], out RectTransform cursor, out Text tag);
                cursors[p] = cursor;
                cursorTags[p] = tag;
            }

            ShowcaseParts showcase = CreateShowcase(panel);

            // プレイヤーの札（右下に横に並べる。置き場所と大きさは画面が人数から決める CharacterSelectScreen.PlateHome）。
            var corners = new Object[CharacterSelectScreen.MaxPlayers];
            var plates = new Object[CharacterSelectScreen.MaxPlayers];
            var devices = new Object[CharacterSelectScreen.MaxPlayers];
            var logos = new Object[CharacterSelectScreen.MaxPlayers];
            var names = new Object[CharacterSelectScreen.MaxPlayers];
            var tags = new Object[CharacterSelectScreen.MaxPlayers];
            var states = new Object[CharacterSelectScreen.MaxPlayers];
            for (int p = 0; p < CharacterSelectScreen.MaxPlayers; p++)
            {
                GameObject corner = CreateCorner(panel, p, new Vector2(1f, 0f), Vector2.zero, colors[p], out SlantedPlate plate, out Text device,
                    out RawImage logo, out Text name, out Text tag, out Text state);
                var rect = (RectTransform)corner.transform;
                rect.pivot = Vector2.zero;
                rect.anchoredPosition = CharacterSelectScreen.PlateHome(p, false, CornerSize);
                rect.localScale = Vector3.one * CharacterSelectScreen.PlateScale;
                corners[p] = corner;
                plates[p] = plate;
                devices[p] = device;
                logos[p] = logo;
                names[p] = name;
                tags[p] = tag;
                states[p] = state;
            }
            // 操作の説明は 2 行まで（ローカル対戦の MODE・CPU・チームの操作。T9-6）。
            Text hint = MenuUiBuilder.CreateText(panel, "Hint", string.Empty, 19, FontStyle.Bold, TextAnchor.LowerCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(1500f, 56f), Color.white);

            CharacterDetailsParts details = CreateDetails(panel);

            var screen = panel.gameObject.AddComponent<CharacterSelectScreen>();
            SandboxWireSceneBuilder.SetReference(screen, "_roster", roster);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_icons", icons);
            SandboxWireSceneBuilder.SetInt(screen, "_columns", 1);
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
            SandboxWireSceneBuilder.SetReference(screen, "_message", showcase.Message);
            SandboxWireSceneBuilder.SetReference(screen, "_showcase", showcase.Root);
            SandboxWireSceneBuilder.SetReference(screen, "_showDiamond", showcase.Diamond);
            SandboxWireSceneBuilder.SetReference(screen, "_showLogo", showcase.Logo);
            SandboxWireSceneBuilder.SetReference(screen, "_showQuestion", showcase.Question);
            SandboxWireSceneBuilder.SetReference(screen, "_showPlayer", showcase.Player);
            SandboxWireSceneBuilder.SetReference(screen, "_showName", showcase.Name);
            SandboxWireSceneBuilder.SetReference(screen, "_showTags", showcase.Tags);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_showSkillKeys", showcase.SkillKeys);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_showSkillNames", showcase.SkillNames);
            SandboxOnlineSceneBuilder.SetColors(screen, "_colors", colors);
            // 選んだとき・決めたときの演出（D-89。ユーザーの決定、2026-11-07）。
            panel.gameObject.AddComponent<CharacterSelectMotion>();
            panel.gameObject.SetActive(false);
            return screen;
        }

        // 名前の札 1 つ（境目と同じ向きの斜めの札に、左にロゴ、右に名前）。中心を center に置く（一覧の左下が原点）。character が null なら
        // RANDOM（ロゴの所に「?」）。
        private static CharacterIcon CreateIcon(RectTransform grid, int index, CharacterConfigAsset character, Vector2 center)
        {
            var go = new GameObject($"Icon{index + 1}", typeof(RectTransform));
            go.transform.SetParent(grid, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = new Vector2(PlateWidth, PlateHeight);
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(MainMenuPanel.EdgeLean, 2f, new Color(0.05f, 0.08f, 0.13f, 0.92f), new Color(1f, 1f, 1f, 0.3f));
            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(go.transform, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = logoRect.anchorMax = logoRect.pivot = new Vector2(0f, 0.5f);
            float logoSize = PlateHeight - 12f;
            logoRect.anchoredPosition = new Vector2(PlateHeight * MainMenuPanel.EdgeLean * 0.5f + 22f, 0f);
            logoRect.sizeDelta = new Vector2(logoSize, logoSize);
            if (character != null)
            {
                var logo = logoGo.AddComponent<RawImage>();
                logo.texture = character.Logo;
                logo.raycastTarget = false;
            }
            else
            {
                AccentTint.Mark(MenuUiBuilder.CreateText(logoGo.transform, "Question", "?", 52, FontStyle.Bold, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(logoSize, logoSize), MenuUiBuilder.Accent));
            }
            BestFit(MenuUiBuilder.CreateText(go.transform, "Name", character != null ? character.DisplayName : "RANDOM", 30, FontStyle.BoldAndItalic,
                TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(logoRect.anchoredPosition.x + logoSize + 18f, 0f),
                new Vector2(PlateWidth - logoRect.anchoredPosition.x - logoSize - 46f, 44f), Color.white), 16);
            var icon = go.AddComponent<CharacterIcon>();
            icon.Configure(index);
            return icon;
        }

        // カーソル（枠だけの斜めの札と、番号ごとに札の四隅の 1 つに置く「1P」などの札）。
        private static void CreateCursor(RectTransform grid, int player, Color color, out RectTransform cursor, out Text tag)
        {
            var go = new GameObject($"Cursor{player + 1}P", typeof(RectTransform));
            go.transform.SetParent(grid, false);
            cursor = (RectTransform)go.transform;
            cursor.sizeDelta = new Vector2(PlateWidth, PlateHeight);
            var frame = go.AddComponent<SlantedPlate>();
            frame.Configure(MainMenuPanel.EdgeLean, 4f, new Color(0f, 0f, 0f, 0f), color);
            frame.raycastTarget = false;
            // 1P〜4P は札の四隅、5P〜8P は上下の辺の 1/4・3/4 の所。
            Vector2[] corners =
            {
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.3f, 1f), new Vector2(0.7f, 1f), new Vector2(0.3f, 0f), new Vector2(0.7f, 0f),
            };
            Vector2 corner = corners[player];
            var offset = new Vector2(corner.x == 0f ? -4f : corner.x == 1f ? 4f : 0f, corner.y == 0f ? -14f : 14f);
            TextAnchor alignment = corner.x == 0f ? TextAnchor.MiddleLeft : corner.x == 1f ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            tag = MenuUiBuilder.CreateText(go.transform, "Tag", $"{player + 1}P", 24, FontStyle.BoldAndItalic,
                alignment, corner, offset, new Vector2(60f, 30f), color);
            go.SetActive(false);
        }

        private sealed class ShowcaseParts
        {
            public RectTransform Root;
            public Graphic Diamond;
            public RawImage Logo;
            public Text Question;
            public Text Player;
            public Text Name;
            public Text Tags;
            public Text Message;
            public Object[] SkillKeys;
            public Object[] SkillNames;
        }

        // 右の大きな表示（右の真ん中を基準に。原点はその左下）: 番号と状態、キャラクターの色の菱形の上の大きなロゴ（RANDOM は「?」）、
        // 名前（白にキャラクターの色の縁取り。右寄せ）、ハッシュタグ、Q・E・R の技の名前の札。知らせはその下（表示を隠しても出す）。
        private static ShowcaseParts CreateShowcase(RectTransform panel)
        {
            var holder = new GameObject("ShowcaseArea", typeof(RectTransform));
            holder.transform.SetParent(panel, false);
            var area = (RectTransform)holder.transform;
            area.anchorMin = area.anchorMax = area.pivot = new Vector2(1f, 0.5f);
            area.anchoredPosition = Vector2.zero;
            area.sizeDelta = ShowcaseSize;
            var go = new GameObject("Showcase", typeof(RectTransform));
            go.transform.SetParent(area, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var parts = new ShowcaseParts { Root = root };

            // ロゴの菱形（キャラクターの色）と、その内側の淡い菱形。
            parts.Diamond = Diamond(root, "Diamond", DiamondSize, new Color(1f, 1f, 1f, 0.92f));
            Diamond(root, "DiamondInner", DiamondSize * 0.9f, new Color(1f, 1f, 1f, 0.32f));
            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(root, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = logoRect.anchorMax = Vector2.zero;
            logoRect.pivot = new Vector2(0.5f, 0.5f);
            logoRect.anchoredPosition = ArtCenter;
            logoRect.sizeDelta = new Vector2(LogoSize, LogoSize);
            parts.Logo = logoGo.AddComponent<RawImage>();
            parts.Logo.raycastTarget = false;
            parts.Question = MenuUiBuilder.CreateText(root, "Question", "?", 260, FontStyle.Bold, TextAnchor.MiddleCenter,
                Vector2.zero, ArtCenter, new Vector2(LogoSize, LogoSize), Color.white);
            parts.Question.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            parts.Player = MenuUiBuilder.CreateText(root, "Player", string.Empty, 44, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                Vector2.zero, new Vector2(120f, 845f), new Vector2(560f, 60f), Color.white);
            // 名前（右寄せ。白にキャラクターの色の縁取り。名前が変わると 1 文字ずつはねる）。
            Text name = MenuUiBuilder.CreateText(root, "Name", string.Empty, 80, FontStyle.BoldAndItalic, TextAnchor.MiddleRight,
                Vector2.zero, new Vector2(1100f, 975f), new Vector2(1000f, 104f), Color.white, false);
            name.rectTransform.pivot = new Vector2(1f, 0.5f);
            BestFit(name, 40);
            var stroke = name.gameObject.AddComponent<TitleStroke>();
            stroke.Configure(8f, 10f, MenuUiBuilder.Accent);
            name.gameObject.AddComponent<LetterRise>();
            parts.Name = name;
            parts.Tags = MenuUiBuilder.CreateText(root, "Tags", string.Empty, 30, FontStyle.Bold, TextAnchor.MiddleRight,
                Vector2.zero, new Vector2(1100f, 895f), new Vector2(1000f, 46f), Color.white);
            parts.Tags.rectTransform.pivot = new Vector2(1f, 0.5f);

            // Q・E・R の技の名前（境目と同じ向きの斜めの札を、下の段ほど少し左へ）。
            string[] keys = { "Q", "E", "R" };
            int count = CharacterSelectScreen.SkillSlots.Length;
            parts.SkillKeys = new Object[count];
            parts.SkillNames = new Object[count];
            for (int k = 0; k < count; k++)
            {
                const float width = 560f;
                const float height = 64f;
                var row = new GameObject($"Skill{keys[k]}", typeof(RectTransform));
                row.transform.SetParent(root, false);
                var rowRect = (RectTransform)row.transform;
                rowRect.anchorMin = rowRect.anchorMax = Vector2.zero;
                rowRect.pivot = new Vector2(1f, 0.5f);
                rowRect.anchoredPosition = new Vector2(1120f - k * height * MainMenuPanel.EdgeLean * 1.3f, 700f - k * 80f);
                rowRect.sizeDelta = new Vector2(width, height);
                var plate = row.AddComponent<SlantedPlate>();
                plate.Configure(MainMenuPanel.EdgeLean, 2f, new Color(0.03f, 0.05f, 0.09f, 0.88f), new Color(1f, 1f, 1f, 0.25f));
                plate.raycastTarget = false;
                // キーの四角（キャラクターの色）と文字。
                var keyGo = new GameObject("Key", typeof(RectTransform));
                keyGo.transform.SetParent(row.transform, false);
                var keyRect = (RectTransform)keyGo.transform;
                keyRect.anchorMin = keyRect.anchorMax = keyRect.pivot = new Vector2(0f, 0.5f);
                keyRect.anchoredPosition = new Vector2(30f, 0f);
                keyRect.sizeDelta = new Vector2(44f, 44f);
                var keyBox = keyGo.AddComponent<SlantedPlate>();
                keyBox.Configure(MainMenuPanel.EdgeLean, 0f, Color.white, Color.clear);
                keyBox.raycastTarget = false;
                MenuUiBuilder.CreateText(keyGo.transform, "Letter", keys[k], 28, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f), MenuUiBuilder.Ink, false);
                Text skill = BestFit(MenuUiBuilder.CreateText(row.transform, "Name", string.Empty, 28, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.5f), new Vector2(92f, 0f), new Vector2(width - 120f, 44f), Color.white), 16);
                parts.SkillKeys[k] = keyBox;
                parts.SkillNames[k] = skill;
            }

            // 知らせ（ローカル対戦の参加の案内・ネットワーク対戦のだれを待っているか。2 行まで）。ロゴの菱形に重ならないように右寄せ。
            parts.Message = MenuUiBuilder.CreateText(area, "Message", string.Empty, 26, FontStyle.Bold, TextAnchor.MiddleRight,
                Vector2.zero, new Vector2(1100f, 420f), new Vector2(560f, 84f), Color.white);
            parts.Message.rectTransform.pivot = new Vector2(1f, 0.5f);
            FitInWidth(parts.Message, 26);
            return parts;
        }

        // 45° 回した四角（菱形）。中心はロゴと同じ。
        private static Graphic Diamond(RectTransform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = ArtCenter;
            rect.sizeDelta = new Vector2(size, size);
            rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
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
            tags = AccentTint.Mark(MenuUiBuilder.CreateText(go.transform, "Tags", string.Empty, 20, FontStyle.Bold, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(184f, -126f), new Vector2(226f, 80f), MenuUiBuilder.Accent));
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
            AccentTint.Mark(MenuUiBuilder.CreateFill(info, "Edge", MenuUiBuilder.Accent, new Vector2(0f, 1f), new Vector2(1f, 1f))).rectTransform.offsetMin =
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
            Text tags = AccentTint.Mark(MenuUiBuilder.CreateText(header.transform, "Tags", string.Empty, 30, FontStyle.Bold,
                TextAnchor.LowerLeft, new Vector2(0f, 0f), Vector2.zero, new Vector2(500f, 52f), MenuUiBuilder.Accent));
            MenuUiBuilder.Localize(MenuUiBuilder.CreateText(info, "Close", "F1 / SELECT: CLOSE", 18, FontStyle.Bold, TextAnchor.UpperRight,
                new Vector2(1f, 1f), new Vector2(-40f, -16f), new Vector2(400f, 28f), new Color(1f, 1f, 1f, 0.6f)), "F1 ／ SELECT: 閉じる");

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
                AccentTint.Mark(MenuUiBuilder.CreateText(column, "Key", keys[i], 36, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(26f, -12f), new Vector2(50f, 48f), MenuUiBuilder.Accent));
                Text skillName = MenuUiBuilder.CreateText(column, "Name", string.Empty, 32, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), Vector2.zero, Vector2.zero, Color.white);
                StretchTop(skillName, 80f, 20f, 12f, 48f);
                // 説明は 4 行まで（細い字。クールタイムの行は列の下に固定する）。文なので、英語でも読みやすい日本語の字体（M PLUS 1。
                // 英字も入っている）で書き、行の間を少し詰める（字体の行の高さが大きいので。D-77）。
                Text description = MenuUiBuilder.CreateText(column, "Description", string.Empty, 21, FontStyle.Normal,
                    TextAnchor.UpperLeft, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.92f));
                StretchTop(description, 26f, 30f, 68f, 120f);
                description.horizontalOverflow = HorizontalWrapMode.Wrap;
                description.font = GameFonts.Japanese;
                description.lineSpacing = 0.95f;
                // 句読点を行の頭に置かずに折り返す（枠の幅が変わったら入れ直す）。
                description.gameObject.AddComponent<LineBreakText>();
                Text cooldown = MenuUiBuilder.CreateText(column, "Cooldown", string.Empty, 22, FontStyle.Bold, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Color(1f, 0.85f, 0.3f));
                StretchTop(cooldown, 26f, 20f, 196f, 34f);
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
