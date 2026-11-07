using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Editor
{
    /// <summary>
    /// メニューと一時停止の画面（uGUI。D-65、D-69）を作る部品（docs/15 T6-1）。基準の画面の大きさは 1920×1080。
    /// 文字はゲームの字体（GameFonts。英字は Orbitron、足りない字は M PLUS 1。D-77）。
    /// </summary>
    internal static class MenuUiBuilder
    {
        public static readonly Color Accent = new Color(0.35f, 0.85f, 1f);
        // 札の中は不透明（裏の札が透けないように。2026-11-08）。
        private static readonly Color PlateFill = new Color(0.03f, 0.05f, 0.09f, 1f);
        private static readonly Color PlateBorder = new Color(1f, 1f, 1f, 0.35f);

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

        /// <summary>
        /// メインメニューの項目のイメージカラー（SINGLE・LOCAL・NETWORK・TRAINING・OPTIONS・QUIT の順。2026-11-07。値は MenuAccents）。
        /// 札の縁・裏の札・縁を回る光・左の絵・右の見出しの札、キャラクター選択へ入る斜めの札、その項目から入った画面の文字に使う。
        /// </summary>
        public static Color[] MainAccents => MenuAccents.MainItems();

        /// <summary>OPTIONS のイメージカラー（オプションの画面の札の縁と裏の札・文字。2026-11-07）。</summary>
        public static Color OptionsAccent => MenuAccents.Options;

        /// <summary>
        /// 見出しの札（HeadingPlate。白地に黒の文字、イメージカラーの縁と少し角度のずれた裏の札。2026-11-08 のユーザーの要望）。札は
        /// anchor を基準に、pivot（札の右端なら (1, 0.5)）を position に置く。高さ height、文字の大きさ fontSize（幅が maxWidth を超えるなら
        /// 小さくする）。メインメニューとステージ選択の右の見出し。
        /// </summary>
        public static HeadingPlate CreateHeadingPlate(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position,
            float height, int fontSize, float maxWidth, float padding)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = anchor;
            root.pivot = pivot;
            root.anchoredPosition = position;
            root.sizeDelta = new Vector2(maxWidth, height);
            var faces = new RectTransform[2];
            var plates = new SlantedPlate[2];
            var texts = new Text[2];
            for (int i = 0; i < 2; i++)
            {
                var face = new GameObject(i == 0 ? "PlateA" : "PlateB", typeof(RectTransform));
                face.transform.SetParent(root, false);
                var rect = (RectTransform)face.transform;
                rect.anchorMin = rect.anchorMax = pivot;
                rect.pivot = pivot;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(maxWidth, height);
                var plate = face.AddComponent<SlantedPlate>();
                plate.Configure(0.22f, 5f, new Color(0.97f, 0.98f, 1f, 1f), Accent);
                // 見本のページの見出しの裏の札（高さ 96 で左へ 13・下へ 11、時計回りに 1.3°）を、札の高さに合わせて大きくする（2026-11-08）。
                plate.ConfigureBack(1.3f, new Vector2(-13f, -11f) * (height / 96f));
                plate.raycastTarget = false;
                face.AddComponent<CanvasGroup>().alpha = 0f;
                Text word = CreateText(rect, "Word", string.Empty, fontSize, FontStyle.Italic, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(maxWidth, height), new Color(0.05f, 0.06f, 0.09f), false);
                word.font = GameFonts.LatinBlack;
                faces[i] = rect;
                plates[i] = plate;
                texts[i] = word;
            }
            var heading = go.AddComponent<HeadingPlate>();
            heading.Configure(faces, plates, texts, padding, maxWidth, fontSize);
            return heading;
        }

        /// <summary>
        /// 説明の札（DescriptionPlate。2026-11-08）: 白地に黒の文字の斜めの札。anchor を基準に、札の右上の角を position に置く。幅 width、
        /// 高さは文に合わせる（実行中）。縁と裏の札の色は出すときに決める。文は M PLUS 1 で、句読点を行の頭に置かずに折り返す。
        /// </summary>
        public static DescriptionPlate CreateDescriptionPlate(Transform parent, string name, Vector2 anchor, Vector2 position, float width, int fontSize,
            bool wrapRules = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, 120f);
            var plate = go.AddComponent<SlantedPlate>();
            plate.Configure(0.12f, 4f, new Color(0.97f, 0.98f, 1f, 1f), Accent);
            plate.ConfigureBack(1f, new Vector2(-12f, -10f));
            plate.raycastTarget = false;
            Text text = CreateText(rect, "Description", string.Empty, fontSize, FontStyle.Bold, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), Vector2.zero, Vector2.zero, Ink, false);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            // 札の左の傾きの分と、上下の余白（DescriptionPlate の余白と同じ 22）。
            textRect.offsetMin = new Vector2(44f, 22f);
            textRect.offsetMax = new Vector2(-34f, -22f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            // 説明は文なので、英語でも読みやすい日本語の字体（M PLUS 1。英字も入っている。D-77）。
            text.font = GameFonts.JapaneseBold;
            if (wrapRules)
            {
                // 実行中に文を入れる札は、句読点を行の頭に置かずに折り返す（決まった文を LocalizedText で出す札には付けない）。
                text.gameObject.AddComponent<LineBreakText>();
            }
            var description = go.AddComponent<DescriptionPlate>();
            description.Configure(text, 22f, 84f);
            return description;
        }

        /// <summary>白い背景の上の文字の色（黒に近い紺。2026-11-08）。</summary>
        public static readonly Color Ink = new Color(0.05f, 0.06f, 0.09f);

        /// <summary>
        /// クリックした所の火花の層（ClickSparks。2026-11-07）。parent の最後の子として画面いっぱいに置く（クリックは受け止めない）。
        /// </summary>
        public static ClickSparks CreateClickSparks(Transform parent)
        {
            RectTransform rect = CreateFull(parent, "ClickSparks");
            rect.SetAsLastSibling();
            return rect.gameObject.AddComponent<ClickSparks>();
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

        /// <summary>
        /// 説明の文（操作の説明など）text に、今の英語の文と日本語の文 japanese を持たせる（LocalizedText。オプションの LANGUAGE で
        /// 切り替わる。2026-11-03 のユーザーの要望）。同じ文字を返す。
        /// </summary>
        public static Text Localize(Text text, string japanese)
        {
            LocalizedText.Attach(text, text.text, japanese);
            return text;
        }

        /// <summary>
        /// 画面の下の操作の説明（右下に右寄せで置く。左下の札や項目と重ならないように。ユーザーの要望、2026-11-06）。english は英語、
        /// japanese は日本語の文（LocalizedText）。
        /// </summary>
        public static Text CreateHint(Transform parent, string english, string japanese)
        {
            // 下の辺だけ黒い帯にして、操作の説明を不透明な白の文字で読みやすく（ユーザーの要望、2026-11-08）。
            CreateHintStrip(parent, HintStripHeight);
            Text hint = CreateText(parent, "Hint", english, 22, FontStyle.Bold, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-48f, 18f),
                new Vector2(1760f, 40f), Color.white);
            return Localize(hint, japanese);
        }

        /// <summary>操作の説明の後ろの黒い帯の高さ（ピクセル）。</summary>
        public const float HintStripHeight = 64f;

        /// <summary>画面の下の辺の黒い帯（操作の説明の後ろ。2026-11-08）。parent の今の最後の子として置く。</summary>
        public static Image CreateHintStrip(Transform parent, float height)
        {
            Image strip = CreateFill(parent, "HintStrip", new Color(0.02f, 0.03f, 0.05f, 1f), Vector2.zero, new Vector2(1f, 0f));
            strip.rectTransform.offsetMax = new Vector2(0f, height);
            return strip;
        }

        /// <summary>
        /// 白い背景（MainMenuPanel。D-91 の M2 を白地に。2026-11-08）を anchorMin〜anchorMax に広げて置く。bandLeft・stripLeft は帯と細い帯の
        /// 左端（地の幅に対する割合）、slant は地の左右の端の傾き。帯の色は accent（実行中に AccentTint や項目の選択で変わる）。
        /// </summary>
        public static MainMenuPanel CreateWhitePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float bandLeft, float stripLeft,
            float slant, Color accent)
        {
            RectTransform rect = CreateFull(parent, name);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            var panel = rect.gameObject.AddComponent<MainMenuPanel>();
            panel.raycastTarget = false;
            panel.Configure(bandLeft, stripLeft, slant);
            panel.SetAccent(accent);
            return panel;
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
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            GameFonts.Use(label);
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
            // 見出しは白い背景の上なので黒（ネットワーク対戦の接続の画面。2026-11-08）。
            CreateText(row.transform, "Label", label, 26, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), Vector2.zero, new Vector2(170f, size.y), Ink, false);

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
            // ゲームパッドでは、選んだだけでは入力を始めない（決定で始める）。
            box.AddComponent<InputFieldGamepadControl>();
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
        /// 項目の左に小さな絵（MenuIcon。D-88 の B12。選んでいる間だけ動く）を足し、名前を絵の分だけ右へずらす（メインメニュー）。
        /// </summary>
        public static MenuIcon AddMenuIcon(MenuOption option, MenuIcon.Shape shape)
        {
            var rect = (RectTransform)option.transform;
            float height = rect.sizeDelta.y;
            float size = height * 0.62f;
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(option.transform, false);
            var iconRect = (RectTransform)go.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(size, size);
            iconRect.anchoredPosition = new Vector2(height * 0.35f + 26f + size * 0.5f, 0f);
            var icon = go.AddComponent<MenuIcon>();
            icon.Configure(shape);
            RectTransform label = option.LabelText.rectTransform;
            label.anchoredPosition += new Vector2(size + 18f, 0f);
            label.sizeDelta -= new Vector2(size + 18f, 0f);
            return icon;
        }

        /// <summary>
        /// 文字の演出（D-90。2026-11-08）の W17（chevrons なら、名前の前に項目の色の「›››」を出し、名前を右へ寄せる）と W16（subJapanese が
        /// 空でなければ、札の右に小さな説明をすべり出させる。英語の設定では subEnglish）を付ける。W1・W2 はどの項目にも MenuOption が付ける。
        /// 大きさは見本のページの札（高さ 58）を基準に、札の高さに合わせる。
        /// </summary>
        public static void AddTextMotion(MenuOption option, bool chevrons, string subEnglish = "", string subJapanese = "")
        {
            var rect = (RectTransform)option.transform;
            float height = rect.sizeDelta.y;
            float unit = height / 58f;
            ChevronMarks marks = null;
            if (chevrons)
            {
                var go = new GameObject("Chevrons", typeof(RectTransform));
                go.transform.SetParent(option.transform, false);
                var marksRect = (RectTransform)go.transform;
                marksRect.anchorMin = marksRect.anchorMax = new Vector2(0f, 0.5f);
                marksRect.pivot = new Vector2(0f, 0.5f);
                marksRect.sizeDelta = new Vector2(ChevronMarks.WidthFor(height), height);
                // 名前の書き出しより少し左から（名前は ›› の分だけ右へ寄る）。
                marksRect.anchoredPosition = new Vector2(option.LabelText.rectTransform.anchoredPosition.x - 12f * unit, 0f);
                marks = go.AddComponent<ChevronMarks>();
                marks.raycastTarget = false;
                marks.color = option.Accent;
            }
            Text sub = null;
            if (!string.IsNullOrEmpty(subJapanese))
            {
                // 初めは見えない（濃さは文字の色に持つ。選んだときに MenuOption が濃くする）。
                Color hidden = option.Accent;
                hidden.a = 0f;
                sub = Localize(CreateText(option.transform, "Sub", subEnglish, 18, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(1f, 0.5f),
                    new Vector2(-30f * unit, 0f), new Vector2(300f, height), hidden, false), subJapanese);
            }
            option.ConfigureTextMotion(marks, sub);
        }

        /// <summary>
        /// ゲームを終了する前の確認（QuitConfirmDialog。ユーザーの要望、2026-10-20）: 画面全体を暗くし（クリックを通さない）、真ん中の
        /// 斜めの札に「ゲームを終了します。本当によろしいですか？」と、「はい」「いいえ」の 2 つの札。ほかの画面より手前に出すため、
        /// Canvas の最後に作る。
        /// </summary>
        public static QuitConfirmDialog CreateQuitDialog(Transform canvas)
        {
            RectTransform root = CreateFull(canvas, "QuitConfirm");
            Image dim = CreateFill(root, "Dim", new Color(0f, 0f, 0f, 0.72f), Vector2.zero, Vector2.one);
            dim.raycastTarget = true;
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1060f, 360f);
            var plate = panel.AddComponent<SlantedPlate>();
            // 中は不透明な白、文字は黒（ユーザーの要望、2026-11-08。「はい」「いいえ」の札はそのまま）。縁は QUIT のイメージカラー。
            plate.Configure(0.12f, 4f, new Color(0.97f, 0.98f, 1f, 1f), MenuAccents.Quit);
            Text message = CreateText(panel.transform, "Message", QuitConfirmDialog.MessageJapanese, 40, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(980f, 90f), new Color(0.05f, 0.06f, 0.09f), false);
            MenuOption yes = CreateMenuItem(panel.transform, "Yes", "はい", new Vector2(0.5f, 0f), new Vector2(-330f, 150f), new Vector2(300f, 76f));
            MenuOption no = CreateMenuItem(panel.transform, "No", "いいえ", new Vector2(0.5f, 0f), new Vector2(50f, 150f), new Vector2(300f, 76f));
            yes.SetSelectedShift(new Vector2(0f, 6f));
            no.SetSelectedShift(new Vector2(0f, 6f));
            yes.SetAccent(MenuAccents.Quit, true);
            no.SetAccent(MenuAccents.Quit, true);
            var dialog = root.gameObject.AddComponent<QuitConfirmDialog>();
            SandboxWireSceneBuilder.SetReference(dialog, "_message", message);
            SandboxWireSceneBuilder.SetReference(dialog, "_yes", yes);
            SandboxWireSceneBuilder.SetReference(dialog, "_no", no);
            root.gameObject.SetActive(false);
            return dialog;
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
        /// 結果の画面の項目（docs/15 T6-8。REMATCH、CHARACTER SELECT、MAIN MENU を画面の下に横に並べる）。controls は IMatchControls、
        /// hud は結果を出しているかを知る MatchHud。
        /// </summary>
        public static ResultsMenu CreateResultsMenu(MonoBehaviour controls, MatchHud hud, InputActionAsset inputActions)
        {
            // 結果を出している間に止める、試合中の表示（画面に描く表示。結果の表 MatchHud と、版の表示は残す）。
            var huds = new System.Collections.Generic.List<Object>();
            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                System.Type type = behaviour.GetType();
                if (type.Namespace == typeof(MatchHud).Namespace && type != typeof(MatchHud)
                    && type.GetMethod("OnGUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) != null)
                {
                    huds.Add(behaviour);
                }
            }
            Canvas canvas = CreateCanvas("ResultsMenu", 15);
            RectTransform panel = CreateFull(canvas.transform, "Panel");
            var bottom = new Vector2(0.5f, 0f);
            // 札の幅は、横に広い字体（Orbitron）の「CHARACTER SELECT」が収まる大きさ（D-77）。
            var size = new Vector2(520f, 72f);
            const float gap = 24f;
            float left = -(size.x * 3f + gap * 2f) * 0.5f;
            MenuOption rematch = CreateMenuItem(panel, "Rematch", "REMATCH", bottom, new Vector2(left, 150f), size);
            MenuOption characters = CreateMenuItem(panel, "CharacterSelect", "CHARACTER SELECT", bottom, new Vector2(left + size.x + gap, 150f), size);
            MenuOption menu = CreateMenuItem(panel, "MainMenu", "MAIN MENU", bottom, new Vector2(left + (size.x + gap) * 2f, 150f), size);
            foreach (MenuOption option in new[] { rematch, characters, menu })
            {
                // 上で右上の EXIT へ移れるように、向きは自動（押せない札は飛ばす）。
                option.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                option.SetSelectedShift(new Vector2(0f, 12f));
                // 選んでいない項目は暗く下げる（D-88 の B13）。
                option.SetRecede(true);
            }
            // 右上の EXIT（ネットワーク対戦だけ。自分だけ部屋を出る。2026-11-03 のユーザーの要望）: 左に印（ドアと矢印）、右に EXIT。
            var exitSize = new Vector2(250f, 64f);
            MenuOption exit = CreateMenuItem(panel, "Exit", "EXIT", Vector2.one, new Vector2(-exitSize.x - 40f, -32f), exitSize);
            exit.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            exit.SetSelectedShift(new Vector2(-10f, 0f));
            RectTransform exitLabel = exit.LabelText.rectTransform;
            exitLabel.anchoredPosition += new Vector2(50f, 0f);
            var iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(exit.transform, false);
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(exitSize.y * 0.35f + 18f, 0f);
            iconRect.sizeDelta = new Vector2(46f, 46f);
            var exitIcon = iconObject.AddComponent<RawImage>();
            exitIcon.raycastTarget = false;
            // 案内の文字は、遊んでいるモードのイメージカラー（ResultsMenu が開くときに塗る。2026-11-08）。
            Text note = AccentTint.Mark(CreateText(panel, "Note", string.Empty, 24, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                bottom, new Vector2(0f, 46f), new Vector2(1400f, 40f), Accent));
            CreateClickSparks(panel);

            var results = canvas.gameObject.AddComponent<ResultsMenu>();
            SandboxWireSceneBuilder.SetReference(results, "_controls", controls);
            SandboxWireSceneBuilder.SetReference(results, "_hud", hud);
            SandboxWireSceneBuilder.SetReference(results, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(results, "_panel", panel.gameObject);
            SandboxWireSceneBuilder.SetReference(results, "_rematch", rematch);
            SandboxWireSceneBuilder.SetReference(results, "_characterSelect", characters);
            SandboxWireSceneBuilder.SetReference(results, "_menu", menu);
            SandboxWireSceneBuilder.SetReference(results, "_exit", exit);
            SandboxWireSceneBuilder.SetReference(results, "_exitIcon", exitIcon);
            SandboxWireSceneBuilder.SetReference(results, "_note", note);
            SandboxWireSceneBuilder.SetReferenceArray(results, "_hideDuringResults", huds.ToArray());
            return results;
        }

        /// <summary>
        /// 一時停止のメニュー（Esc かゲームパッドの Start。RESUME、RESET POSITION、BACK TO MENU）。オフラインの試合と検証用のシーンに置く
        /// （runner は IPauseTarget）。
        /// </summary>
        public static PauseMenu CreatePauseMenu(MonoBehaviour runner, InputActionAsset inputActions)
        {
            // ほかのすべての画面（結果の画面・試合の表示）より手前（2026-11-08）。
            Canvas canvas = CreateCanvas("PauseMenu", 100);
            RectTransform panel = CreateFull(canvas.transform, "Panel");
            CreateFill(panel, "Dim", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, Vector2.one);
            CreateText(panel, "Title", "PAUSE", 72, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(800f, 100f), Color.white);
            var center = new Vector2(0.5f, 0.5f);
            var size = new Vector2(520f, 72f);
            MenuOption resume = CreateMenuItem(panel, "Resume", "RESUME", center, new Vector2(-260f, 110f), size);
            MenuOption reset = CreateMenuItem(panel, "ResetPosition", "RESET POSITION", center, new Vector2(-260f, 20f), size);
            MenuOption menu = CreateMenuItem(panel, "BackToMenu", "BACK TO MENU", center, new Vector2(-260f, -70f), size);
            foreach (MenuOption option in new[] { resume, reset, menu })
            {
                // 選んでいない項目は暗く下げる（D-88 の B13）。名前の前の「›››」（W17）。
                option.SetRecede(true);
                AddTextMotion(option, true);
            }
            Localize(CreateText(panel, "Hint", "Esc / Start: close    Enter / A: OK", 20, FontStyle.Bold, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(800f, 40f), new Color(1f, 1f, 1f, 0.7f)),
                "Esc ／ Start: 閉じる　　Enter ／ A: 決定");
            CreateClickSparks(panel);

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
