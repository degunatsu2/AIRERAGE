using System;
using System.Collections;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Input;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// オプション（docs/15 T6-9。D-68）: メインメニューの OPTIONS から開き、左右で音量を変えるとすぐに当たって保存され、閉じると
    /// メインメニューの OPTIONS へ戻る。試合の BGM の音量に効く。キーの割り当ては、決定してから仮想のキーボード・ゲームパッドで
    /// 押したキーに変わり、同じキーの操作とは入れ替わり、試合の入力（InputSystemAdapter）に当たり、元に戻せる。
    /// </summary>
    public sealed class OptionsTests
    {
        private InputSettings.EditorInputBehaviorInPlayMode _savedBehavior;
        private InputSettings.BackgroundBehavior _savedBackground;

        [SetUp]
        public void SetUp()
        {
            GameSettings.DeleteSaved();
            _savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.DeleteSaved();
            AudioListener.volume = 1f;
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedBehavior;
            InputSystem.settings.backgroundBehavior = _savedBackground;
        }

        [UnityTest]
        public IEnumerator Volumes_ChangeWithLeftRight_AreSaved_AndReachTheMatch()
        {
            MenuScreens menu = null;
            yield return OpenOptions(m => menu = m);
            OptionsScreen options = menu.OptionsScreen;
            Assert.That(options.ShowingControls, Is.False, "最初は GENERAL のページでない");
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(options.Page.gameObject), "PAGE の項目を選んでいない");
            Assert.That(options.GeneralValue(OptionsScreen.Row.Master), Does.Contain("100%"));

            // 全体の音量を左で 3 回（70%）、BGM を 5 回（50%）。
            for (int i = 0; i < 3; i++)
            {
                Move(options.GeneralRow(OptionsScreen.Row.Master).gameObject, MoveDirection.Left);
            }
            for (int i = 0; i < 5; i++)
            {
                Move(options.GeneralRow(OptionsScreen.Row.Bgm).gameObject, MoveDirection.Left);
            }
            Assert.That(options.GeneralValue(OptionsScreen.Row.Master), Does.Contain("70%"));
            // 値の左半分のクリックで左、右半分のクリックで右（ユーザーの要望、2026-10-20）。
            OptionArrow[] arrows = options.GeneralRow(OptionsScreen.Row.Master).GetComponentsInChildren<OptionArrow>();
            Assert.That(arrows.Length, Is.EqualTo(2), "値の左右のクリックの的がない");
            Click(Array.Find(arrows, a => a.Direction < 0).gameObject);
            Assert.That(options.GeneralValue(OptionsScreen.Row.Master), Is.EqualTo("60%"), "左半分のクリックで下がらない");
            Click(Array.Find(arrows, a => a.Direction > 0).gameObject);
            Click(Array.Find(arrows, a => a.Direction > 0).gameObject);
            Assert.That(options.GeneralValue(OptionsScreen.Row.Master), Is.EqualTo("80%"), "右半分のクリックで上がらない");
            Click(Array.Find(arrows, a => a.Direction < 0).gameObject);
            Assert.That(AudioListener.volume, Is.EqualTo(0.7f).Within(1e-4f), "全体の音量がすぐに当たらない");
            Assert.That(GameSettings.Load().BgmVolume, Is.EqualTo(5), "変えた音量が保存されない");
            // 決定でも次の値へ（端から回る）。垂直同期は ON ↔ OFF。
            options.GeneralRow(OptionsScreen.Row.VSync).Button.onClick.Invoke();
            Assert.That(options.GeneralValue(OptionsScreen.Row.VSync), Does.Contain("OFF"));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(0));
            options.GeneralRow(OptionsScreen.Row.VSync).Button.onClick.Invoke();

            // 戻る操作で閉じて、メインメニューの OPTIONS へ。
            options.Back.Button.onClick.Invoke();
            yield return null;
            yield return null;
            Assert.That(menu.ShowingOptions, Is.False);
            Assert.That(menu.ShowingMain, Is.True, "閉じてもメインメニューに戻らない");
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.Options.gameObject), "OPTIONS の項目に戻らない");

            // 試合の BGM の音量に効く（BGM の元の音量 × 50%）。
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            flow.StartTraining(flow.Stages.Get(0));
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            var audio = UnityEngine.Object.FindFirstObjectByType<GameAudio>();
            Assert.That(audio.SeScale, Is.EqualTo(1f));
            Assert.That(audio.BgmVolume, Is.EqualTo(0.3f * 0.5f).Within(1e-3f), "試合の BGM に音量が効かない");
        }

        [UnityTest]
        public IEnumerator Language_SwitchesTheStageDescriptions_BetweenJapaneseAndEnglish()
        {
            MenuScreens menu = null;
            yield return OpenOptions(m => menu = m);
            OptionsScreen options = menu.OptionsScreen;
            // 初めは日本語（ユーザーの要望、2026-10-27）。ステージ選択と同じ画面（RANDOM STAGES）の説明は日本語。
            Assert.That(options.GeneralValue(OptionsScreen.Row.Language), Is.EqualTo(OptionsScreen.JapaneseLabel), "初めが日本語でない");
            StageSelectScreen stages = menu.StageSelect;
            yield return OpenRandomStages(options);
            Assert.That(stages.Highlighted, Is.Not.Null, "前提: ステージにカーソルがある");
            Assert.That(stages.ShownDescription, Is.EqualTo(stages.Highlighted.DescriptionJa), "ステージの説明が日本語でない");
            stages.Option(0).OnCancel(null);
            yield return null;

            // 決定（左右でも）で ENGLISH に切り替わって保存され、説明が英語になる。ほかの表示は英語のまま。
            options.GeneralRow(OptionsScreen.Row.Language).Button.onClick.Invoke();
            Assert.That(options.GeneralValue(OptionsScreen.Row.Language), Is.EqualTo(OptionsScreen.EnglishLabel));
            Assert.That(GameSettings.LoadLanguage(), Is.EqualTo(GameLanguage.English), "言語が保存されない");
            yield return OpenRandomStages(options);
            Assert.That(stages.ShownDescription, Is.EqualTo(stages.Highlighted.Description), "ステージの説明が英語にならない");
            stages.Option(0).OnCancel(null);
            yield return null;

            // キャラクター選択のハッシュタグと技の説明も英語（2026-10-27）。
            options.Close();
            yield return null;
            menu.Training.Button.onClick.Invoke();
            yield return null;
            yield return null;
            CharacterSelectScreen characters = menu.CharacterSelect;
            Assert.That(menu.ShowingCharacterSelect, Is.True, "前提: キャラクター選択");
            Assert.That(characters.ShownTags(0), Does.StartWith("#"));
            Assert.That(characters.ShownTags(0), Does.Not.Match("[ぁ-ヿ一-龯]"), "ハッシュタグが英語にならない");

            // 日本語に戻す。
            GameSettings settings = GameSettings.Load();
            settings.Language = GameLanguage.Japanese;
            settings.Save();
            Assert.That(GameSettings.LoadLanguage(), Is.EqualTo(GameLanguage.Japanese));
        }

        // オプションから RANDOM STAGES（ステージ選択と同じ画面）を開き、ステージにカーソルが来るのを待つ。
        private static IEnumerator OpenRandomStages(OptionsScreen options)
        {
            options.RandomStages.Button.onClick.Invoke();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Bindings_ChangeWithAKeyPress_Swap_ReachTheMatchInput_AndReset()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            var mouse = InputSystem.AddDevice<Mouse>();
            InputActionAsset asset = null;
            try
            {
                MenuScreens menu = null;
                yield return OpenOptions(m => menu = m);
                OptionsScreen options = menu.OptionsScreen;
                Move(options.Page.gameObject, MoveDirection.Right);
                Assert.That(options.ShowingControls, Is.True, "左右で CONTROLS のページにならない");
                int jump = Array.IndexOf(BindingSettings.Actions, "Jump");
                int attack = Array.IndexOf(BindingSettings.Actions, "Attack");
                int wire = Array.IndexOf(BindingSettings.Actions, "Wire");
                Assert.That(options.KeyCell(jump).Label, Is.EqualTo("SPACE"));
                Assert.That(options.PadCell(jump).Label, Is.EqualTo("A"));
                // 攻撃とワイヤーのキーボードの列は、マウスのボタン（ユーザーの要望、2026-10-20）。
                Assert.That(options.KeyCell(attack).Label, Is.EqualTo("RIGHT CLICK"));
                Assert.That(options.KeyCell(wire).Label, Is.EqualTo("LEFT CLICK"));

                // ジャンプのキーを L に。
                options.KeyCell(jump).Button.onClick.Invoke();
                Assert.That(options.IsRebinding, Is.True, "決定してもキーの変更が始まらない");
                Assert.That(options.PromptText, Does.Contain("PRESS A KEY OR MOUSE BUTTON"));
                yield return Press(keyboard, Key.L);
                yield return WaitFor(() => !options.IsRebinding, 2f, "キーを押しても変更が終わらない");
                Assert.That(options.KeyCell(jump).Label, Is.EqualTo("L"));

                // 攻撃のキーを L に: ジャンプと入れ替わる（ジャンプは攻撃の前の右クリック）。
                yield return WaitSeconds(0.3f);
                options.KeyCell(attack).Button.onClick.Invoke();
                yield return Press(keyboard, Key.L);
                yield return WaitFor(() => !options.IsRebinding, 2f, "攻撃のキーの変更が終わらない");
                Assert.That(options.KeyCell(attack).Label, Is.EqualTo("L"));
                Assert.That(options.KeyCell(jump).Label, Is.EqualTo("RIGHT CLICK"), "同じキーの操作と入れ替わらない");

                // ワイヤーをマウスのサイドのボタンに。
                yield return WaitSeconds(0.3f);
                options.KeyCell(wire).Button.onClick.Invoke();
                yield return Press(mouse, MouseButton.Back);
                yield return WaitFor(() => !options.IsRebinding, 2f, "マウスのボタンで変更が終わらない");
                Assert.That(options.KeyCell(wire).Label, Is.EqualTo("MOUSE 4"), "マウスのサイドのボタンにできない");

                // ゲームパッドのジャンプを Y に。Esc ／ Start でやめると変わらない。
                yield return WaitSeconds(0.3f);
                options.PadCell(jump).Button.onClick.Invoke();
                Assert.That(options.PromptText, Does.Contain("PRESS A BUTTON"));
                yield return Press(gamepad, GamepadButton.North);
                yield return WaitFor(() => !options.IsRebinding, 2f, "ゲームパッドのボタンの変更が終わらない");
                Assert.That(options.PadCell(jump).Label, Is.EqualTo("Y"));
                yield return WaitSeconds(0.3f);
                options.PadCell(attack).Button.onClick.Invoke();
                yield return Press(gamepad, GamepadButton.Start);
                yield return WaitFor(() => !options.IsRebinding, 2f, "Start でやめられない");
                Assert.That(options.PadCell(attack).Label, Is.EqualTo("X"), "やめたのに変わった");
                Assert.That(options.PromptText, Does.Contain("CANCELLED"));
                // 案内は 3 秒で消える。
                yield return WaitSeconds(3.3f);
                Assert.That(options.PromptText, Is.Empty, "変えた結果の案内が消えない");

                // 試合の入力（アセットの複製から作る）に、保存した割り当てが当たる。
                Assert.That(GameSettings.LoadBindingOverrides(), Is.Not.Empty, "割り当てが保存されない");
                asset = UnityEngine.Object.Instantiate(options.InputActions);
                using (new InputSystemAdapter(asset))
                {
                    InputAction jumpAction = asset.FindAction("Player/Jump", throwIfNotFound: true);
                    InputAction attackAction = asset.FindAction("Player/Attack", throwIfNotFound: true);
                    Assert.That(Effective(jumpAction, BindingSettings.Column.Keyboard), Is.EqualTo("<Mouse>/rightButton"));
                    Assert.That(Effective(asset.FindAction("Player/Wire", throwIfNotFound: true), BindingSettings.Column.Keyboard),
                        Is.EqualTo("<Mouse>/backButton"));
                    Assert.That(Effective(attackAction, BindingSettings.Column.Keyboard), Is.EqualTo("<Keyboard>/l"));
                    Assert.That(Effective(jumpAction, BindingSettings.Column.Gamepad), Is.EqualTo("<Gamepad>/buttonNorth"));
                }

                // RESET CONTROLS で元に戻り、保存も消える。
                yield return WaitSeconds(0.3f);
                options.ResetControls.Button.onClick.Invoke();
                Assert.That(options.KeyCell(jump).Label, Is.EqualTo("SPACE"));
                Assert.That(options.KeyCell(attack).Label, Is.EqualTo("RIGHT CLICK"));
                Assert.That(options.KeyCell(wire).Label, Is.EqualTo("LEFT CLICK"));
                Assert.That(options.PadCell(jump).Label, Is.EqualTo("A"));
                Assert.That(GameSettings.LoadBindingOverrides(), Is.EqualTo("{\"bindings\":[]}").Or.Empty, "元に戻しても保存が残る");
            }
            finally
            {
                var screen = UnityEngine.Object.FindFirstObjectByType<OptionsScreen>(FindObjectsInactive.Include);
                if (screen != null)
                {
                    screen.InputActions.RemoveAllBindingOverrides();
                }
                if (asset != null)
                {
                    UnityEngine.Object.Destroy(asset);
                }
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static string Effective(InputAction action, BindingSettings.Column column)
        {
            return action.bindings[BindingSettings.BindingIndex(action, column)].effectivePath;
        }

        // App から始めて、タイトルを抜け、メインメニューの OPTIONS を決定する。
        private static IEnumerator OpenOptions(Action<MenuScreens> found)
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
            menu.ContinueFromTitle();
            yield return null;
            yield return null;
            Assert.That(menu.Options.Button.interactable, Is.True, "OPTIONS を選べない");
            menu.Options.Button.onClick.Invoke();
            yield return null;
            Assert.That(menu.ShowingOptions, Is.True, "OPTIONS を決定してもオプションの画面が出ない");
            Assert.That(menu.ShowingMain, Is.False);
            found(menu);
        }

        // マウスの左クリックを送る。
        private static void Click(GameObject target)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
        }

        // 選んでいる項目に、左右・上下の移動を送る（キーボード・ゲームパッドの移動と同じ）。
        private static void Move(GameObject target, MoveDirection direction)
        {
            var data = new AxisEventData(EventSystem.current) { moveDir = direction };
            ExecuteEvents.Execute(target, data, ExecuteEvents.moveHandler);
        }

        private static IEnumerator Press(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static IEnumerator Press(Mouse mouse, MouseButton button)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(button));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
        }

        private static IEnumerator Press(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string message)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > seconds)
                {
                    Assert.Fail(message);
                }
                yield return null;
            }
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
            }
        }
    }
}
