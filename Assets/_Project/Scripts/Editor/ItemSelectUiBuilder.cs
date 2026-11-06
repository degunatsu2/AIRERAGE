using Airerage.Core.Items;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Editor
{
    /// <summary>
    /// アイテムを選ぶ画面（ItemSelectScreen。ユーザーの要望、2026-11-05。docs/20_item_plan.md）を作る。ステージ選択と同じ形: 左にアイテムの札を
    /// 2 列に並べ（札の中の左に絵、右に名前）、札の左の外にバッグのキー（Z・X・C）の箱かチェックの箱、右にカーソルのアイテムの効果の
    /// ポップアップ（絵・名前・種類・説明）。
    /// 2026-11-05: 札はスクロールの中身（左。ホイール・ドラッグ・カーソルで動く）に入れ、パッシブとアクティブの見出しで分ける。初期アイテムでは
    /// いちばん上に RANDOM の札（チェックの箱）。札の位置は実行中に決める（ItemSelectScreen.Layout）。DONE はスクロールの下に固定。
    /// </summary>
    internal static class ItemSelectUiBuilder
    {
        private const float PlateWidth = 360f;
        private const float PlateHeight = 64f;
        private const float IconSize = 50f;
        private static readonly Vector2 ScrollPosition = new Vector2(86f, -176f);
        private static readonly Vector2 ScrollSize = new Vector2(900f, 690f);

        public static ItemSelectScreen Create(Transform canvas, InputActionAsset inputActions)
        {
            RectTransform panel = MenuUiBuilder.CreateFull(canvas, "ItemSelect");
            MenuUiBuilder.CreateFill(panel, "Side", new Color(0f, 0f, 0f, 0.45f), Vector2.zero, new Vector2(0.49f, 1f));
            Text title = MenuUiBuilder.CreateText(panel, "Title", "START ITEMS", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(90f, -50f), new Vector2(1200f, 90f), Color.white);
            // 案内は、見出しとスクロールの上の端のちょうど間（2026-11-06）。
            Text note = MenuUiBuilder.CreateText(panel, "Note", string.Empty, 24, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(110f, -131f), new Vector2(1000f, 38f), MenuUiBuilder.Accent);
            note.font = GameFonts.JapaneseBold;

            RectTransform content = OnlineMenuUiBuilder.CreateScrollArea(panel, ScrollPosition, ScrollSize, out ScrollRect scroll);
            scroll.name = "ItemScroll";
            Text passiveHeader = CreateHeader(content, "PassiveHeader", "PASSIVE");
            Text activeHeader = CreateHeader(content, "ActiveHeader", "ACTIVE");
            MenuOption random = CreatePlate(content, "Random", "RANDOM");
            RawImage randomIcon = CreateIcon(random.transform);
            FlowSceneBuilder.CreateStageCheck(random.transform, out GameObject randomCheck, out GameObject randomMark);
            random.SetPinned((RectTransform)randomCheck.transform);
            // ドロップするアイテムの、すべてにチェック ／ すべて外す（2026-11-06）。
            MenuOption checkAll = CreatePlate(content, "CheckAll", "CHECK ALL");
            MenuOption uncheckAll = CreatePlate(content, "UncheckAll", "UNCHECK ALL");

            var options = new Object[ItemCatalog.Count];
            var keyBoxes = new Object[ItemCatalog.Count];
            var keys = new Object[ItemCatalog.Count];
            var checkBoxes = new Object[ItemCatalog.Count];
            var checkMarks = new Object[ItemCatalog.Count];
            var icons = new Object[ItemCatalog.Count];
            for (int i = 0; i < ItemCatalog.Count; i++)
            {
                ItemId id = ItemCatalog.At(i);
                MenuOption option = CreatePlate(content, $"Item{(int)id}", ItemTexts.Name(id));
                options[i] = option;
                icons[i] = CreateIcon(option.transform);
                CreateKey(option.transform, out GameObject box, out Text key);
                keyBoxes[i] = box;
                keys[i] = key;
                FlowSceneBuilder.CreateStageCheck(option.transform, out GameObject check, out GameObject mark);
                checkBoxes[i] = check;
                checkMarks[i] = mark;
                // 選んで札が右へ動いても、左のキーとチェックの箱は動かさない（押しやすいように。2026-11-06）。
                option.SetPinned((RectTransform)box.transform, (RectTransform)check.transform);
            }
            // DONE はスクロールの下に固定（いつでも見える）。
            MenuOption done = CreatePlate(panel, "Done", "DONE");
            ((RectTransform)done.transform).anchoredPosition = new Vector2(150f, ScrollPosition.y - ScrollSize.y - 16f);

            // 効果のポップアップ（右。カーソルの札の高さに動く）。
            var popupObject = new GameObject("Popup", typeof(RectTransform));
            popupObject.transform.SetParent(panel, false);
            var popup = (RectTransform)popupObject.transform;
            popup.anchorMin = popup.anchorMax = popup.pivot = new Vector2(0f, 1f);
            popup.anchoredPosition = new Vector2(1000f, -200f);
            popup.sizeDelta = new Vector2(840f, 330f);
            Image back = popupObject.AddComponent<Image>();
            back.color = new Color(0.03f, 0.05f, 0.09f, 0.9f);
            back.raycastTarget = false;
            MenuUiBuilder.CreateFill(popup, "TopLine", MenuUiBuilder.Accent, new Vector2(0f, 1f), new Vector2(1f, 1f)).rectTransform.offsetMin =
                new Vector2(0f, -4f);
            MenuUiBuilder.CreateFill(popup, "LeftLine", MenuUiBuilder.Accent, new Vector2(0f, 0f), new Vector2(0f, 1f)).rectTransform.offsetMax =
                new Vector2(6f, 0f);
            var popupIcon = new GameObject("Icon", typeof(RectTransform)).AddComponent<RawImage>();
            popupIcon.transform.SetParent(popup, false);
            popupIcon.rectTransform.anchorMin = popupIcon.rectTransform.anchorMax = popupIcon.rectTransform.pivot = new Vector2(0f, 1f);
            popupIcon.rectTransform.anchoredPosition = new Vector2(30f, -26f);
            popupIcon.rectTransform.sizeDelta = new Vector2(150f, 150f);
            Text popupName = MenuUiBuilder.CreateText(popup, "Name", string.Empty, 40, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(204f, -30f), new Vector2(610f, 60f), Color.white);
            popupName.font = GameFonts.JapaneseBold;
            popupName.resizeTextForBestFit = true;
            popupName.resizeTextMinSize = 24;
            popupName.resizeTextMaxSize = 40;
            Text popupKind = MenuUiBuilder.CreateText(popup, "Kind", string.Empty, 24, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(204f, -96f), new Vector2(610f, 40f), MenuUiBuilder.Accent);
            popupKind.font = GameFonts.JapaneseBold;
            Text description = MenuUiBuilder.CreateText(popup, "Description", string.Empty, 25, FontStyle.Bold, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(30f, -192f), new Vector2(780f, 128f), new Color(1f, 1f, 1f, 0.9f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.font = GameFonts.JapaneseBold;
            description.gameObject.AddComponent<LineBreakText>();
            popupObject.SetActive(false);

            MenuUiBuilder.CreateHint(panel, "↑↓←→: select     Wheel: scroll     Enter / A: choose     Esc / B: done", "↑↓←→:選択　　ホイール:スクロール　　Enter ／ A: 決定（もう一度で外す）　　Esc ／ B: 終わる");

            var screen = panel.gameObject.AddComponent<ItemSelectScreen>();
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_options", options);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_keyBoxes", keyBoxes);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_keys", keys);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_checkBoxes", checkBoxes);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_checkMarks", checkMarks);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_icons", icons);
            SandboxWireSceneBuilder.SetReference(screen, "_random", random);
            SandboxWireSceneBuilder.SetReference(screen, "_checkAll", checkAll);
            SandboxWireSceneBuilder.SetReference(screen, "_uncheckAll", uncheckAll);
            SandboxWireSceneBuilder.SetReference(screen, "_randomCheck", randomCheck);
            SandboxWireSceneBuilder.SetReference(screen, "_randomMark", randomMark);
            SandboxWireSceneBuilder.SetReference(screen, "_randomIcon", randomIcon);
            SandboxWireSceneBuilder.SetReference(screen, "_content", content);
            SandboxWireSceneBuilder.SetReference(screen, "_scroll", scroll);
            SandboxWireSceneBuilder.SetReference(screen, "_passiveHeader", passiveHeader);
            SandboxWireSceneBuilder.SetReference(screen, "_activeHeader", activeHeader);
            SandboxWireSceneBuilder.SetReference(screen, "_done", done);
            SandboxWireSceneBuilder.SetReference(screen, "_title", title);
            SandboxWireSceneBuilder.SetReference(screen, "_note", note);
            SandboxWireSceneBuilder.SetReference(screen, "_popup", popup);
            SandboxWireSceneBuilder.SetReference(screen, "_popupIcon", popupIcon);
            SandboxWireSceneBuilder.SetReference(screen, "_popupName", popupName);
            SandboxWireSceneBuilder.SetReference(screen, "_popupKind", popupKind);
            SandboxWireSceneBuilder.SetReference(screen, "_popupDescription", description);
            SandboxWireSceneBuilder.SetReference(screen, "_inputActions", inputActions);
            panel.gameObject.SetActive(false);
            return screen;
        }

        // 区切りの見出し（PASSIVE ／ ACTIVE。水色の斜めの文字と下の線）。位置は実行中に決める。
        private static Text CreateHeader(RectTransform content, string name, string label)
        {
            Text header = MenuUiBuilder.CreateText(content, name, label, 28, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
                Vector2.zero, new Vector2(820f, 40f), MenuUiBuilder.Accent);
            header.rectTransform.pivot = new Vector2(0f, 1f);
            header.font = GameFonts.JapaneseBold;
            Image line = MenuUiBuilder.CreateFill(header.rectTransform, "Line", new Color(MenuUiBuilder.Accent.r, MenuUiBuilder.Accent.g, MenuUiBuilder.Accent.b, 0.5f),
                new Vector2(0f, 0f), new Vector2(1f, 0f));
            line.rectTransform.offsetMin = new Vector2(0f, -2f);
            line.rectTransform.offsetMax = new Vector2(0f, 0f);
            return header;
        }

        // アイテムの札（2 列。位置は実行中に決める）。名前は絵の右（日本語も出せる字体）。
        private static MenuOption CreatePlate(RectTransform parent, string name, string label)
        {
            MenuOption option = MenuUiBuilder.CreateMenuItem(parent, name, label, new Vector2(0f, 1f), Vector2.zero, new Vector2(PlateWidth, PlateHeight));
            Text text = option.LabelText;
            text.fontSize = 24;
            text.font = GameFonts.JapaneseBold;
            text.rectTransform.anchoredPosition += new Vector2(IconSize + 10f, 0f);
            text.rectTransform.sizeDelta = new Vector2(PlateWidth - IconSize - 90f, PlateHeight);
            // 長い名前（アルティメットブースター極）も札に収める（折り返しを許すと、大きさを合わせる機能が 1 行に収まるまで縮める）。
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = 24;
            option.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            return option;
        }

        // 札の中の左のアイテムの絵。
        private static RawImage CreateIcon(Transform plate)
        {
            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<RawImage>();
            icon.transform.SetParent(plate, false);
            RectTransform rect = icon.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(PlateHeight * 0.35f + 14f, 0f);
            rect.sizeDelta = new Vector2(IconSize, IconSize);
            icon.raycastTarget = false;
            return icon;
        }

        // 札の左の外の、入るバッグのキー（水色の箱に黒い文字。選んだときだけ出す）。
        private static void CreateKey(Transform plate, out GameObject box, out Text key)
        {
            box = new GameObject("Key", typeof(RectTransform));
            box.transform.SetParent(plate, false);
            var rect = (RectTransform)box.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-6f, 0f);
            rect.sizeDelta = new Vector2(46f, 46f);
            box.AddComponent<Image>().color = MenuUiBuilder.Accent;
            key = MenuUiBuilder.CreateText(rect, "Text", "Z", 26, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(46f, 46f), new Color(0.03f, 0.05f, 0.09f));
            key.resizeTextForBestFit = true;
            key.resizeTextMinSize = 12;
            key.resizeTextMaxSize = 26;
            box.SetActive(false);
        }
    }
}
