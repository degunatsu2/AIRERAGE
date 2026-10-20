using System;
using System.Collections;
using Airerage.Runtime.Flow;
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
    /// ゲームを終了する前の確認（ユーザーの要望、2026-10-20）: タイトルの Esc とメインメニューの QUIT で「ゲームを終了します。本当に
    /// よろしいですか？」が出て、最初は「いいえ」を選んでいる。いいえ・戻るで元の画面へ戻り、はいで終える。
    /// </summary>
    public sealed class QuitDialogTests
    {
        [UnityTest]
        public IEnumerator TitleEsc_AndMainMenuQuit_AskBeforeQuitting()
        {
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                flow.AllowQuit = false;
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                Assert.That(menu.ShowingTitle, Is.True);

                // タイトルで Esc: 終了の確認が出て、「いいえ」を選んでいる。
                yield return Press(keyboard, Key.Escape);
                Assert.That(menu.ShowingQuitDialog, Is.True, "タイトルで Esc を押しても終了の確認が出ない");
                Assert.That(menu.QuitDialog.Message, Does.Contain("ゲームを終了します"));
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.QuitDialog.No.gameObject), "最初に「いいえ」を選んでいない");
                Assert.That(menu.ShowingTitle, Is.True, "確認の後ろのタイトルが消えた");
                yield return WaitSeconds(0.3f);

                // いいえでタイトルへ戻り、もう一度ボタンを押すとメインメニューへ。
                menu.QuitDialog.No.Button.onClick.Invoke();
                Assert.That(menu.ShowingQuitDialog, Is.False);
                Assert.That(menu.ShowingTitle, Is.True, "いいえでタイトルに戻らない");
                Assert.That(flow.QuitRequested, Is.False);
                yield return Press(keyboard, Key.Enter);
                Assert.That(menu.ShowingMain, Is.True, "タイトルからメインメニューへ進めない");
                yield return null;

                // メインメニューの QUIT: 確認が出て、戻る操作（Esc）でメインメニューの QUIT へ戻る。
                menu.Quit.Button.onClick.Invoke();
                Assert.That(menu.ShowingQuitDialog, Is.True, "QUIT で終了の確認が出ない");
                Assert.That(flow.QuitRequested, Is.False, "確認の前に終わろうとした");
                yield return WaitSeconds(0.3f);
                ExecuteEvents.Execute(menu.QuitDialog.No.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.cancelHandler);
                Assert.That(menu.ShowingQuitDialog, Is.False, "戻る操作で閉じない");
                yield return null;
                yield return null;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.Quit.gameObject), "QUIT の項目に戻らない");

                // はいで終える。
                menu.Quit.Button.onClick.Invoke();
                yield return WaitSeconds(0.3f);
                menu.QuitDialog.Yes.Button.onClick.Invoke();
                Assert.That(flow.QuitRequested, Is.True, "はいでも終わらない");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        private static IEnumerator Press(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
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
