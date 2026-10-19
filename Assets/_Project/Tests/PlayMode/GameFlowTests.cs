using System;
using System.Collections;
using Airerage.Core.Simulation;
using Airerage.Net;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
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
    /// ゲームの流れ（docs/15 T6-1、T6-2、T6-6。D-66）: App から始めると、タイトル → メインメニュー（背景にステージ）→ トレーニング
    /// （ステージのシーンに試合の仕組みを重ねる。出現位置はステージのもの）→ 一時停止 → メニューへ。ネットワーク対戦からも戻れる。
    /// 検証用のシーンを直接再生したときの一時停止のメニュー。
    /// </summary>
    public sealed class GameFlowTests
    {
        [UnityTest]
        public IEnumerator App_ShowsTheTitle_TheMenu_AndTraining_OnTheStage_ThenGoesBack()
        {
            int savedCharacter = PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0);
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                // キャラクター選択のカーソルは、前に選んだキャラクター（オンラインの待機所と同じ）から始まる。
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, 1);
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                Assert.That(flow, Is.Not.Null, "App のシーンに GameFlow がない");
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                Assert.That(flow.LoadedScenes, Is.EquivalentTo(new[] { "Stage_Athletic", "Menu" }), "メニューの背景にステージがない");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Stage_Athletic"), "光と空がステージのものでない");

                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                var sounds = UnityEngine.Object.FindFirstObjectByType<UiSounds>();
                Assert.That(sounds, Is.Not.Null, "App のシーンにメニューの効果音がない");
                Assert.That(menu.ShowingTitle, Is.True, "最初にタイトルを出さない");
                menu.ContinueFromTitle();
                yield return null;
                yield return null;
                Assert.That(menu.ShowingMain, Is.True, "タイトルの後にメインメニューを出さない");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.Local.gameObject), "使える最初の項目（LOCAL VS）を選んでいない");
                Assert.That(sounds.History, Is.EqualTo(new[] { SoundId.UiConfirm }), "タイトルを抜けたときに決定の音だけを鳴らさない（最初の項目は音なしで選ぶ）");

                // カーソルを動かすと移動の音、決定すると決定の音（ユーザーの要望、2026-10-16）。
                EventSystem.current.SetSelectedGameObject(menu.Training.gameObject);
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiMove), "カーソルを動かしても音が鳴らない");
                Assert.That(menu.Single.Button.interactable, Is.False, "SINGLE は Phase 7 まで選べない");
                Assert.That(menu.Local.Button.interactable, Is.True, "LOCAL VS を選べない（T6-5）");
                Assert.That(menu.Options.Button.interactable, Is.True, "OPTIONS を選べない（T6-9）");
                Assert.That(menu.Training.Button.interactable && menu.Quit.Button.interactable, Is.True);

                // TRAINING → キャラクター選択（T6-3）。
                menu.Training.Button.onClick.Invoke();
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiConfirm), "決定しても音が鳴らない");
                yield return null;
                yield return null;
                Assert.That(menu.ShowingCharacterSelect, Is.True, "TRAINING でキャラクター選択を出さない");
                Assert.That(menu.ShowingMain, Is.False);
                CharacterSelectScreen characters = menu.CharacterSelect;
                Assert.That(characters.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Single));
                Assert.That(characters.Count, Is.EqualTo(4), "4 体のアイコンがない");
                Assert.That(characters.CharacterOf(0), Is.EqualTo(1), "前に選んだキャラクターから始まらない");
                Assert.That(characters.ShownName(0), Is.EqualTo(CharacterPresets.RiskTakerName), "1P の枠にカーソルのキャラクターを出さない");
                Assert.That(characters.ShownTags(0), Is.EqualTo("#コイン重視  #大器晩成"), "1P の枠にハッシュタグを出さない");
                Assert.That(characters.DetailsOpen, Is.False, "技の説明をはじめから出している");

                // 矢印でアイコンを移る。F1 で技の説明が下から出て、カーソルを動かすとついてくる。もう一度 F1 で引っ込む。
                yield return Tap(keyboard, Key.RightArrow);
                yield return Tap(keyboard, Key.RightArrow);
                Assert.That(characters.CharacterOf(0), Is.EqualTo(3), "矢印でカーソルが動かない");
                Assert.That(characters.ShownName(0), Is.EqualTo(CharacterPresets.SpaceTravelerName));
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiMove), "カーソルを動かしても音が鳴らない");
                yield return Tap(keyboard, Key.F1);
                Assert.That(characters.DetailsOpen, Is.True, "F1 で技の説明を出さない");
                Assert.That(characters.ShownDetailName, Is.EqualTo(CharacterPresets.SpaceTravelerName));
                Assert.That(characters.ShownSkillName(0), Is.EqualTo("ワープポータル"));
                Assert.That(characters.ShownSkillName(1), Is.EqualTo("スペースミサイル"));
                Assert.That(characters.ShownSkillName(2), Is.EqualTo("コズミックレイ"));
                Assert.That(characters.ShownSkillDescription(0), Does.Contain("ポータル"), "技の説明を出さない");
                Assert.That(characters.ShownSkillCooldown(0), Is.EqualTo("クールタイム 10 秒  ／  スタック 2"));
                Assert.That(characters.ShownSkillCooldown(1), Is.EqualTo("クールタイム 6 秒"));
                Assert.That(characters.ShownSkillCooldown(2), Is.EqualTo("必要ゲージ 100%"));
                yield return Tap(keyboard, Key.LeftArrow);
                Assert.That(characters.ShownDetailName, Is.EqualTo(CharacterPresets.StormRunnerName), "技の説明がカーソルについてこない");
                Assert.That(characters.ShownSkillCooldown(0), Is.EqualTo("クールタイム 10 秒（効果が終わってから）"));
                yield return Tap(keyboard, Key.F1);
                Assert.That(characters.DetailsOpen, Is.False, "もう一度 F1 で引っ込まない");
                yield return Tap(keyboard, Key.RightArrow);

                // 戻る操作で 1 つ前の画面へ（キャラクター選択 → メインメニューの TRAINING）。
                yield return Tap(keyboard, Key.Escape);
                yield return null;
                Assert.That(menu.ShowingMain, Is.True, "キャラクター選択から戻れない");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.Training.gameObject), "戻ったときに TRAINING を選んでいない");
                menu.Training.Button.onClick.Invoke();
                yield return null;
                yield return null;
                yield return Tap(keyboard, Key.RightArrow);
                yield return Tap(keyboard, Key.RightArrow);
                yield return Tap(keyboard, Key.Enter);
                Assert.That(PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0), Is.EqualTo(3), "選んだキャラクターを覚えない");
                yield return null;

                // ステージ選択（T6-4）: 戻るとキャラクター選択、決定でトレーニング。
                Assert.That(menu.ShowingStageSelect, Is.True, "キャラクターを決めてもステージ選択を出さない");
                StageSelectScreen stages = menu.StageSelect;
                Assert.That(stages.Count, Is.EqualTo(flow.Stages.Count));
                Assert.That(stages.ShownName, Is.EqualTo(flow.Stages.Get(0).DisplayName));
                stages.Option(0).OnCancel(null);
                yield return null;
                yield return null;
                Assert.That(menu.ShowingCharacterSelect, Is.True, "ステージ選択から戻れない");
                Assert.That(characters.CharacterOf(0), Is.EqualTo(3), "戻ったときに選んだキャラクターにいない");
                yield return Tap(keyboard, Key.Enter);
                yield return null;
                stages.Option(0).Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
                Assert.That(flow.CurrentStage, Is.SameAs(flow.Stages.Get(0)), "選んだステージでない");
                Assert.That(sounds != null, Is.True, "画面を移ると効果音が消える（決定の音が途切れる）");
                Assert.That(flow.LoadedScenes, Is.EqualTo(new[] { "Stage_Athletic", "Match_Training" }), "ステージを先に、試合の仕組みを後に読み込んでいない");
                Assert.That(UnityEngine.Object.FindFirstObjectByType<MenuScreens>(), Is.Null, "メニューが残っている");
                var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
                Assert.That(runner, Is.Not.Null);
                Assert.That(runner.enabled, Is.True, "試合の仕組みが動いていない（ステージの物が見つからない）");
                ISimulationFeed feed = runner;
                Assert.That(feed.CharacterCount, Is.EqualTo(4), "自分とダミー 3 体でない");
                Assert.That(runner.PlayerCharacter.DisplayName, Is.EqualTo(CharacterPresets.SpaceTravelerName), "選んだキャラクターで始まらない");
                Vector3 spawn = SpawnPointAuthoring.Collect()[0];
                Assert.That(Mathf.Abs(runner.PlayerState.Position.X - spawn.x), Is.LessThan(0.5f), "ステージの 1 番目の出現位置に出ない");
                yield return WaitFor(() => runner.PlayerState.Grounded, 3f, "ステージの地面に立たない");

                // 一時停止: シミュレーションが止まり、メニューへ戻れる。
                var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
                pause.Open();
                yield return null;
                yield return null;
                Assert.That(pause.IsOpen, Is.True);
                Assert.That(runner.SpeedScale, Is.Zero, "止まらない");
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiPause), "一時停止を開いても音が鳴らない");
                Assert.That(UnityEngine.Object.FindObjectsByType<UiSounds>(FindObjectsSortMode.None).Length, Is.EqualTo(1), "効果音が 2 つ以上ある");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(pause.Resume.gameObject), "RESUME を選んでいない");
                Assert.That(pause.BackToMenu.gameObject.activeSelf, Is.True, "App から始めたのに BACK TO MENU がない");
                int tick = feed.CurrentTick;
                yield return WaitSeconds(0.3f);
                Assert.That(feed.CurrentTick, Is.EqualTo(tick), "止めている間もシミュレーションが進む");

                pause.BackToMenu.Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューに戻らない");
                Assert.That(UnityEngine.Object.FindFirstObjectByType<SimulationRunner>(), Is.Null, "試合の仕組みが残っている");
                yield return null;
                menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                Assert.That(menu.ShowingMain, Is.True, "戻ったときにタイトルからやり直す");
            }
            finally
            {
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, savedCharacter);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        [UnityTest]
        public IEnumerator PauseMenu_InASandbox_Resets_AndHasNoBackToMenu()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
            Assert.That(pause, Is.Not.Null, "検証用のシーンに一時停止のメニューがない");
            yield return WaitSeconds(0.5f);
            Vector3 spawn = new Vector3(runner.PlayerState.Position.X, runner.PlayerState.Position.Y, 0f);
            CharacterState moved = runner.PlayerState;
            moved.Position += new System.Numerics.Vector3(6f, 0f, 0f);
            runner.SetPlayerState(moved);

            pause.Open();
            yield return null;
            yield return null;
            Assert.That(EventSystem.current, Is.Not.Null, "EventSystem を用意しない");
            var sandboxSounds = UnityEngine.Object.FindFirstObjectByType<UiSounds>();
            Assert.That(sandboxSounds, Is.Not.Null, "App から始めていないときに、効果音を用意しない");
            Assert.That(sandboxSounds.History, Does.Contain(SoundId.UiPause), "一時停止を開いても音が鳴らない");
            Assert.That(pause.BackToMenu.gameObject.activeSelf, Is.False, "App から始めていないのに BACK TO MENU がある");
            pause.ResetPosition.Button.onClick.Invoke();
            yield return null;
            Assert.That(pause.IsOpen, Is.False, "RESET POSITION の後に閉じない");
            Assert.That(runner.SpeedScale, Is.EqualTo(1f), "閉じた後に動き出さない");
            Assert.That(Mathf.Abs(runner.PlayerState.Position.X - spawn.x), Is.LessThan(0.5f), "出現位置に戻らない");
        }

        [UnityTest]
        public IEnumerator PauseMenu_EscapeAgain_Closes()
        {
            // 一時停止のメニューは、もう一度 Esc を押すと閉じる（RESUME と同じ。ユーザーの要望、2026-10-17）。Esc は UI の「戻る」でも
            // あるので、同じ押下で閉じてから開き直さないこと。
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                SceneManager.LoadScene("Sandbox_Wire");
                yield return null;
                var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
                var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
                yield return WaitSeconds(0.3f);

                yield return Tap(keyboard, Key.Escape);
                Assert.That(pause.IsOpen, Is.True, "Esc で開かない");
                yield return null;
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(pause.Resume.gameObject), "RESUME を選んでいない");
                var sounds = UnityEngine.Object.FindFirstObjectByType<UiSounds>();
                int opened = CountOf(sounds, SoundId.UiPause);

                yield return Tap(keyboard, Key.Escape);
                yield return null;
                Assert.That(pause.IsOpen, Is.False, "もう一度 Esc を押しても閉じない");
                Assert.That(runner.SpeedScale, Is.EqualTo(1f), "閉じた後に動き出さない");
                Assert.That(CountOf(sounds, SoundId.UiPause), Is.EqualTo(opened), "閉じた直後に開き直した");

                yield return Tap(keyboard, Key.Escape);
                Assert.That(pause.IsOpen, Is.True, "閉じた後に、もう一度 Esc で開かない");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        [UnityTest]
        public IEnumerator PauseMenu_BlocksGameInput_WhileOpen()
        {
            // 一時停止のメニューを開いている間は、十字キーでカメラが寄り引きせず、閉じたときの決定（A）でジャンプしない
            // （ユーザーの指摘、2026-10-19）。閉じた後は、離してからもう一度押せば使える。
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                SceneManager.LoadScene("Sandbox_Wire");
                yield return null;
                var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
                var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
                var zoom = UnityEngine.Object.FindFirstObjectByType<Airerage.Runtime.Presentation.CameraZoom>();
                Assert.That(zoom, Is.Not.Null);
                yield return WaitFor(() => runner.PlayerState.Grounded, 3f, "地面に立たない");
                float distance = zoom.TargetDistance;

                yield return PressPad(gamepad, GamepadButton.Start);
                Assert.That(pause.IsOpen, Is.True, "Start で一時停止しない");
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadDown));
                yield return WaitSeconds(0.3f);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
                Assert.That(zoom.TargetDistance, Is.EqualTo(distance), "一時停止中に十字キーでカメラが寄り引きした");

                // 十字キーはメニューの項目を移る（下で RESET POSITION、上で RESUME に戻る）。
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(pause.ResetPosition.gameObject), "十字キーで項目を移れない");
                yield return PressPad(gamepad, GamepadButton.DpadUp);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(pause.Resume.gameObject));
                Assert.That(zoom.TargetDistance, Is.EqualTo(distance), "一時停止中に十字キーでカメラが寄り引きした");

                // RESUME を A で決定して、そのまま押し続けてもジャンプしない。
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                yield return null;
                yield return null;
                Assert.That(pause.IsOpen, Is.False, "A で RESUME が決まらない");
                yield return WaitSeconds(0.3f);
                Assert.That(runner.PlayerState.Grounded, Is.True, "メニューの決定に使った A でジャンプした");
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;

                // 離した後は、A でジャンプでき、十字キーでカメラが動く。
                yield return PressPad(gamepad, GamepadButton.South);
                yield return WaitFor(() => !runner.PlayerState.Grounded, 1f, "閉じた後に A でジャンプできない");
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.DpadDown));
                yield return WaitSeconds(0.2f);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
                Assert.That(zoom.TargetDistance, Is.GreaterThan(distance), "閉じた後に十字キーでカメラが動かない");
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        // ゲームパッドのボタンを押して、次のフレームで離す。
        private static IEnumerator PressPad(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
            yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            yield return null;
        }

        // キーを押して、次のフレームで離す。
        private static IEnumerator Tap(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static int CountOf(UiSounds sounds, SoundId id)
        {
            int count = 0;
            foreach (SoundId played in sounds.History)
            {
                if (played == id)
                {
                    count++;
                }
            }
            return count;
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
