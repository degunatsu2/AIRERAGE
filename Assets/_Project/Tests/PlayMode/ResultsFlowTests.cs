using System;
using System.Collections;
using Airerage.Core.Match;
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
    /// 結果からの戻り（docs/15 T6-8）: ローカル対戦の結果の画面を、ゲームパッドで選んで REMATCH（同じ人で再戦）、CHARACTER SELECT
    /// （同じ人とキャラクターのまま、ローカル対戦のキャラクター選択へ）、キーボードで MAIN MENU（メニューへ）。
    /// 試合は短いルールにして終わらせる。
    /// </summary>
    public sealed class ResultsFlowTests
    {
        [UnityTest]
        public IEnumerator LocalResults_Rematch_CharacterSelect_AndMainMenu_WithTheGamepadAndKeyboard()
        {
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                yield return null;
                menu.Local.Button.onClick.Invoke();
                yield return null;

                // キーボードが P1、ゲームパッドが P2（キャラクターを 1 つずらす）。全員 READY で、最初のステージ。
                CharacterSelectScreen join = menu.CharacterSelect;
                yield return Tap(keyboard, Key.Enter);
                yield return Press(gamepad, GamepadButton.South);
                yield return Press(gamepad, GamepadButton.DpadRight);
                int p1 = join.CharacterOf(0);
                int p2 = join.CharacterOf(1);
                yield return Tap(keyboard, Key.Enter);
                yield return Press(gamepad, GamepadButton.South);
                yield return null;
                Assert.That(menu.ShowingStageSelect, Is.True, "全員が READY でステージ選択に進まない");
                menu.StageSelect.Option(0).Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Local && !flow.IsLoading, 10f, "ローカル対戦にならない");

                // 短い試合にして、結果を出す。
                var runner = UnityEngine.Object.FindFirstObjectByType<LocalMatchRunner>();
                MatchRules rules = runner.MatchRules;
                rules.CountdownTicks = 30;
                rules.DurationTicks = 60;
                runner.Restart();
                var results = UnityEngine.Object.FindFirstObjectByType<ResultsMenu>();
                Assert.That(results, Is.Not.Null, "結果の画面の項目がない");
                Assert.That(results.IsOpen, Is.False, "試合中に結果の項目を出している");
                yield return WaitFor(() => results.IsOpen, 10f, "試合が終わっても結果の項目を出さない");
                yield return null;
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(results.Rematch.gameObject), "REMATCH を選んでいない");
                Assert.That(results.CharacterSelect.Button.interactable && results.MainMenu.Button.interactable, Is.True);

                // ゲームパッドの A で REMATCH: 同じ人で始め直し、項目を閉じる。
                yield return Press(gamepad, GamepadButton.South);
                Assert.That(runner.CurrentTick, Is.LessThan(10), "REMATCH で始め直さない");
                yield return null;
                Assert.That(results.IsOpen, Is.False, "始め直したのに結果の項目が残っている");
                Assert.That(runner.CharacterCount, Is.EqualTo(2));
                Assert.That(runner.Players[1].Character, Is.EqualTo(p2), "再戦でキャラクターが変わった");

                // もう一度終わらせて、十字キーの右で CHARACTER SELECT へ移り、A で決める。
                yield return WaitFor(() => results.IsOpen, 10f, "再戦が終わっても結果の項目を出さない");
                yield return null;
                yield return null;
                yield return Press(gamepad, GamepadButton.DpadRight);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(results.CharacterSelect.gameObject),
                    "十字キーの右で CHARACTER SELECT へ移らない");
                yield return Press(gamepad, GamepadButton.South);
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューのシーンに戻らない");
                yield return null;
                menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                Assert.That(menu.ShowingCharacterSelect, Is.True, "キャラクター選択を出さない（メインメニューへ戻った）");
                join = menu.CharacterSelect;
                Assert.That(join.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Local));
                Assert.That(join.JoinedCount, Is.EqualTo(2), "参加した人が残っていない");
                Assert.That(join.CharacterOf(0), Is.EqualTo(p1));
                Assert.That(join.CharacterOf(1), Is.EqualTo(p2), "選んでいたキャラクターでない");
                Assert.That(join.IsReady(0) || join.IsReady(1), Is.False, "戻ったのに READY のまま");

                // 同じ人でもう一度始め、終わったらキーボードで MAIN MENU（右へ 2 つ、Enter）。
                yield return Tap(keyboard, Key.Enter);
                yield return Press(gamepad, GamepadButton.South);
                yield return null;
                menu.StageSelect.Option(0).Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Local && !flow.IsLoading, 10f, "ローカル対戦にならない");
                runner = UnityEngine.Object.FindFirstObjectByType<LocalMatchRunner>();
                runner.MatchRules.CountdownTicks = 30;
                runner.MatchRules.DurationTicks = 60;
                runner.Restart();
                results = UnityEngine.Object.FindFirstObjectByType<ResultsMenu>();
                yield return WaitFor(() => results.IsOpen, 10f, "結果の項目を出さない");
                yield return null;
                yield return null;
                yield return Tap(keyboard, Key.RightArrow);
                yield return Tap(keyboard, Key.RightArrow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(results.MainMenu.gameObject), "矢印で MAIN MENU へ移らない");
                yield return Tap(keyboard, Key.Enter);
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューに戻らない");
                yield return null;
                menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                Assert.That(menu.ShowingCharacterSelect, Is.False, "MAIN MENU なのにキャラクター選択を出した");
                Assert.That(UnityEngine.Object.FindFirstObjectByType<LocalMatchRunner>(), Is.Null, "試合の仕組みが残っている");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(gamepad);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        private static IEnumerator Tap(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static IEnumerator Press(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(button));
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
    }
}
