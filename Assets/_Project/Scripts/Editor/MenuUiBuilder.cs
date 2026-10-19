using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Editor
{
    /// <summary>
    /// メニューと一時停止の画面（uGUI。D-65、D-69）を作る部品（docs/15 T6-1）。基準の画面の大きさは 1920×1080。
    /// 文字は Unity に入っている字体（LegacyRuntime）を使う（字体のファイルを足さない）。
    /// </summary>
    internal static class MenuUiBuilder
    {
        public static readonly Color Accent = new Color(0.35f, 0.85f, 1f);
        private static readonly Color PlateFill = new Color(0.03f, 0.05f, 0.09f, 0.78f);
        private static readonly Color PlateBorder = new Color(1f, 1f, 1f, 0.35f);

        private static Font _font;

        private static Font BuiltinFont => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>画面いっぱいの Canvas（画面に重ねる。大きさは 1920×1080 を基準に合わせる）。</summary>
        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>親いっぱいに広がる、空の入れ物。</summary>
        public static RectTransform CreateFull(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>色を塗った四角（暗くする背景など）。anchorMin〜anchorMax の範囲に広げる。</summary>
        public static Image CreateFill(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = CreateFull(parent, name);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>文字。anchor（0〜1）に pivot を合わせ、position だけずらす。</summary>
        public static Text CreateText(Transform parent, string name, string text, int size, FontStyle style, TextAnchor alignment,
            Vector2 anchor, Vector2 position, Vector2 boxSize, Color color, bool outline = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = boxSize;
            var label = go.AddComponent<Text>();
            label.font = BuiltinFont;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            if (outline)
            {
                var effect = go.AddComponent<Outline>();
                effect.effectColor = new Color(0f, 0f, 0f, 0.65f);
                effect.effectDistance = new Vector2(2f, -2f);
            }
            return label;
        }

        /// <summary>
        /// 文字の入力欄（左に見出し、右に入力する枠）。anchor（0〜1）を基準に、左上を position に置く。キーボードで入力し、
        /// キーボード・ゲームパッドの上下で項目を移る。
        /// </summary>
        public static InputField CreateInputField(Transform parent, string name, string label, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = rowRect.anchorMax = anchor;
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = position;
            rowRect.sizeDelta = size;
            CreateText(row.transform, "Label", label, 26, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(170f, size.y), Color.white);

            var box = new GameObject("Field", typeof(RectTransform));
            box.transform.SetParent(row.transform, false);
            var boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = new Vector2(0f, 0f);
            boxRect.anchorMax = new Vector2(1f, 1f);
            boxRect.offsetMin = new Vector2(180f, 0f);
            boxRect.offsetMax = Vector2.zero;
            var plate = box.AddComponent<SlantedPlate>();
            plate.Configure(0.25f, 3f, PlateFill, PlateBorder);
            Text text = CreateText(box.transform, "Text", string.Empty, 28, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(size.y * 0.25f + 16f, 0f), new Vector2(size.x - 240f, size.y), Color.white, false);
            text.supportRichText = false;
            Text placeholder = CreateText(box.transform, "Placeholder", "...", 28, FontStyle.Italic, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(size.y * 0.25f + 16f, 0f), new Vector2(size.x - 240f, size.y), new Color(1f, 1f, 1f, 0.35f), false);
            var field = box.AddComponent<InputField>();
            field.targetGraphic = plate;
            field.textComponent = text;
            field.placeholder = placeholder;
            var colors = field.colors;
            colors.highlightedColor = new Color(0.8f, 0.95f, 1f);
            colors.selectedColor = Accent;
            field.colors = colors;
            field.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            return field;
        }

        /// <summary>
        /// メニューの項目 1 つ（斜めの札、左に名前、右に使えないときの理由）。anchor（0〜1）を基準に、札の左上を position に置く。
        /// </summary>
        public static MenuOption CreateMenuItem(Transform parent, string name, string label, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.35f, 3f, PlateFill, PlateBorder);
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = plate;
            button.navigation = new Navigation { mode = Navigation.Mode.Vertical };

            Text text = CreateText(go.transform, "Label", label, 34, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(size.y * 0.35f + 26f, 0f), new Vector2(size.x - 60f, size.y), Color.white);
            Text note = CreateText(go.transform, "Note", string.Empty, 16, FontStyle.Bold, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(-34f, 0f), new Vector2(260f, size.y), new Color(1f, 0.85f, 0.3f, 0.85f), false);
            var item = go.AddComponent<MenuOption>();
            item.Configure(plate, text, note, Accent);
            return item;
        }

        /// <summary>
        /// キャラクター選択の札（縦長の斜めの札に、ロゴと名前。選んでいるときは上へ出る）。anchor を基準に、札の左上を position に置く。
        /// </summary>
        public static MenuOption CreateCharacterCard(Transform parent, string name, Texture2D logo, string displayName, Vector2 anchor,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.12f, 3f, PlateFill, PlateBorder);
            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = plate;
            button.navigation = new Navigation { mode = Navigation.Mode.Horizontal };

            var logoGo = new GameObject("Logo", typeof(RectTransform));
            logoGo.transform.SetParent(go.transform, false);
            var logoRect = (RectTransform)logoGo.transform;
            logoRect.anchorMin = logoRect.anchorMax = logoRect.pivot = new Vector2(0.5f, 1f);
            float logoSize = size.x * 0.62f;
            logoRect.anchoredPosition = new Vector2(size.y * 0.12f * 0.5f, -26f);
            logoRect.sizeDelta = new Vector2(logoSize, logoSize);
            var image = logoGo.AddComponent<RawImage>();
            image.texture = logo;
            image.raycastTarget = false;

            Text text = CreateText(go.transform, "Name", displayName, 24, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(size.x - 20f, 40f), Color.white);
            var option = go.AddComponent<MenuOption>();
            option.Configure(plate, text, null, Accent);
            option.SetSelectedShift(new Vector2(0f, 16f));
            return option;
        }

        /// <summary>
        /// 一時停止のメニュー（Esc かゲームパッドの Start。RESUME、RESET POSITION、BACK TO MENU）。オフラインの試合と検証用のシーンに置く
        /// （runner は IPauseTarget）。
        /// </summary>
        public static PauseMenu CreatePauseMenu(MonoBehaviour runner, InputActionAsset inputActions)
        {
            Canvas canvas = CreateCanvas("PauseMenu", 20);
            RectTransform panel = CreateFull(canvas.transform, "Panel");
            CreateFill(panel, "Dim", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            CreateText(panel, "Title", "PAUSE", 72, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(800f, 100f), Color.white);
            var center = new Vector2(0.5f, 0.5f);
            var size = new Vector2(520f, 72f);
            MenuOption resume = CreateMenuItem(panel, "Resume", "RESUME", center, new Vector2(-260f, 110f), size);
            MenuOption reset = CreateMenuItem(panel, "ResetPosition", "RESET POSITION", center, new Vector2(-260f, 20f), size);
            MenuOption menu = CreateMenuItem(panel, "BackToMenu", "BACK TO MENU", center, new Vector2(-260f, -70f), size);
            CreateText(panel, "Hint", "Esc / Start: close    Enter / A: OK", 20, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(800f, 40f), new Color(1f, 1f, 1f, 0.7f));

            var pause = canvas.gameObject.AddComponent<PauseMenu>();
            SandboxWireSceneBuilder.SetReference(pause, "_runner", runner);
            SandboxWireSceneBuilder.SetReference(pause, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(pause, "_panel", panel.gameObject);
            SandboxWireSceneBuilder.SetReference(pause, "_resume", resume);
            SandboxWireSceneBuilder.SetReference(pause, "_reset", reset);
            SandboxWireSceneBuilder.SetReference(pause, "_menu", menu);
            return pause;
        }
    }
}
