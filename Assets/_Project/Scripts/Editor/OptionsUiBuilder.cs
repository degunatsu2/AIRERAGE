using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Editor
{
    /// <summary>
    /// オプションの画面（OptionsScreen。docs/15 T6-9）を作る。左に縦に並んだ斜めの札（メインメニューと同じ形）。いちばん上で
    /// ページ（GENERAL ／ CONTROLS）を切り替え、いちばん下に BACK。CONTROLS は操作ごとに、キーボードとゲームパッドの 2 つの札。
    /// </summary>
    internal static class OptionsUiBuilder
    {
        private static readonly string[] GeneralLabels = { "MASTER VOLUME", "BGM VOLUME", "SE VOLUME", "DISPLAY", "RESOLUTION", "VSYNC" };
        private const float Left = 110f;
        private const float Width = 980f;
        private const float RowHeight = 60f;

        public static OptionsScreen Create(Transform canvas, InputActionAsset inputActions)
        {
            RectTransform panel = MenuUiBuilder.CreateFull(canvas, "Options");
            MenuUiBuilder.CreateFill(panel, "Side", new Color(0f, 0f, 0f, 0.55f), Vector2.zero, new Vector2(0.62f, 1f));
            MenuUiBuilder.CreateText(panel, "Title", "OPTIONS", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(90f, -60f), new Vector2(900f, 90f), Color.white);
            var anchor = new Vector2(0f, 1f);

            MenuOption page = MenuUiBuilder.CreateMenuItem(panel, "Page", "PAGE", anchor, new Vector2(Left, -150f), new Vector2(Width, RowHeight));
            Text pageValue = Value(page, "<  GENERAL  >");
            page.gameObject.AddComponent<OptionStepper>();

            // GENERAL: 音量・画面の項目（左に名前、右に値）と、初めの値に戻す項目。
            RectTransform general = MenuUiBuilder.CreateFull(panel, "General");
            var generalRows = new Object[GeneralLabels.Length];
            var generalValues = new Object[GeneralLabels.Length];
            for (int i = 0; i < GeneralLabels.Length; i++)
            {
                MenuOption row = MenuUiBuilder.CreateMenuItem(general, GeneralLabels[i].Replace(" ", string.Empty), GeneralLabels[i], anchor,
                    new Vector2(Left, -235f - i * 70f), new Vector2(Width, RowHeight));
                row.gameObject.AddComponent<OptionStepper>();
                generalRows[i] = row;
                generalValues[i] = Value(row, string.Empty);
            }
            MenuOption resetGeneral = MenuUiBuilder.CreateMenuItem(general, "ResetGeneral", "RESET TO DEFAULT", anchor,
                new Vector2(Left, -235f - GeneralLabels.Length * 70f - 8f), new Vector2(Width, RowHeight));

            // CONTROLS: 見出し（キーボード・ゲームパッド）、操作ごとの名前と 2 つの札、元に戻す項目。
            RectTransform controls = MenuUiBuilder.CreateFull(panel, "Controls");
            const float keyX = Left + 330f;
            const float padX = Left + 700f;
            MenuUiBuilder.CreateText(controls, "KeyboardHeader", "KEYBOARD", 24, FontStyle.Bold, TextAnchor.MiddleLeft, anchor,
                new Vector2(keyX + 30f, -228f), new Vector2(320f, 40f), MenuUiBuilder.Accent);
            MenuUiBuilder.CreateText(controls, "GamepadHeader", "GAMEPAD", 24, FontStyle.Bold, TextAnchor.MiddleLeft, anchor,
                new Vector2(padX + 30f, -228f), new Vector2(280f, 40f), MenuUiBuilder.Accent);
            int actions = BindingSettings.Actions.Length;
            var keyCells = new Object[actions];
            var padCells = new Object[actions];
            for (int i = 0; i < actions; i++)
            {
                float y = -268f - i * 60f;
                MenuUiBuilder.CreateText(controls, $"{BindingSettings.Actions[i]}Label", BindingSettings.Labels[i], 32, FontStyle.BoldAndItalic,
                    TextAnchor.MiddleLeft, anchor, new Vector2(Left + 20f, y), new Vector2(300f, 54f), Color.white);
                MenuOption key = MenuUiBuilder.CreateMenuItem(controls, $"{BindingSettings.Actions[i]}Key", "-", anchor, new Vector2(keyX, y),
                    new Vector2(340f, 54f));
                MenuOption pad = MenuUiBuilder.CreateMenuItem(controls, $"{BindingSettings.Actions[i]}Pad", "-", anchor, new Vector2(padX, y),
                    new Vector2(280f, 54f));
                key.SetSelectedShift(new Vector2(0f, 4f));
                pad.SetSelectedShift(new Vector2(0f, 4f));
                keyCells[i] = key;
                padCells[i] = pad;
            }
            MenuOption resetControls = MenuUiBuilder.CreateMenuItem(controls, "ResetControls", "RESET CONTROLS", anchor,
                new Vector2(Left, -268f - actions * 60f - 4f), new Vector2(Width, RowHeight));
            controls.gameObject.SetActive(false);

            MenuOption back = MenuUiBuilder.CreateMenuItem(panel, "Back", "BACK", anchor, new Vector2(Left, -780f), new Vector2(Width, RowHeight));
            Text prompt = MenuUiBuilder.CreateText(panel, "Prompt", string.Empty, 28, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(90f, 125f), new Vector2(1100f, 44f), MenuUiBuilder.Accent);
            MenuUiBuilder.CreateText(panel, "Hint", "Up / Down: select     Left / Right: change     Enter / A: OK     Esc / B: back", 22,
                FontStyle.Bold, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(90f, 50f), new Vector2(1200f, 40f), new Color(1f, 1f, 1f, 0.7f));

            var screen = panel.gameObject.AddComponent<OptionsScreen>();
            SandboxWireSceneBuilder.SetReference(screen, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(screen, "_page", page);
            SandboxWireSceneBuilder.SetReference(screen, "_pageValue", pageValue);
            SandboxWireSceneBuilder.SetReference(screen, "_generalPage", general.gameObject);
            SandboxWireSceneBuilder.SetReference(screen, "_controlsPage", controls.gameObject);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_generalRows", generalRows);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_generalValues", generalValues);
            SandboxWireSceneBuilder.SetReference(screen, "_resetGeneral", resetGeneral);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_keyCells", keyCells);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_padCells", padCells);
            SandboxWireSceneBuilder.SetReference(screen, "_resetControls", resetControls);
            SandboxWireSceneBuilder.SetReference(screen, "_back", back);
            SandboxWireSceneBuilder.SetReference(screen, "_prompt", prompt);
            panel.gameObject.SetActive(false);
            return screen;
        }

        // 項目の右に寄せた値の文字。
        private static Text Value(MenuOption row, string text)
        {
            return MenuUiBuilder.CreateText(row.transform, "Value", text, 30, FontStyle.BoldAndItalic, TextAnchor.MiddleRight,
                new Vector2(1f, 0.5f), new Vector2(-48f, 0f), new Vector2(520f, RowHeight), MenuUiBuilder.Accent);
        }
    }
}
