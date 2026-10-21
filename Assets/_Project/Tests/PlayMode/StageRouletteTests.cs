using System;
using System.Collections;
using System.Collections.Generic;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// ステージ選択の RANDOM（ユーザーの要望、2026-10-21）: 決めると、カーソルがルーレットのようにステージの札を回り、選んだステージで
    /// 止まってから、そのステージで始まる。回っている間は戻る・ほかの札の決定を受け付けない。
    /// </summary>
    public sealed class StageRouletteTests
    {
        [UnityTest]
        public IEnumerator Random_SpinsThroughTheStages_ThenStartsTheOneItStopsOn()
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
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                yield return null;
                menu.Training.Button.onClick.Invoke();
                yield return null;
                yield return null;
                yield return Tap(keyboard, Key.Enter);
                yield return null;
                Assert.That(menu.ShowingStageSelect, Is.True, "前提: ステージ選択");
                StageSelectScreen stages = menu.StageSelect;
                Assert.That(stages.RandomOption, Is.Not.Null, "RANDOM の札がない");

                var visited = new List<StageDefinition>();
                StageDefinition confirmed = null;
                stages.HighlightedStageChanged += stage =>
                {
                    if (stage != null)
                    {
                        visited.Add(stage);
                    }
                };
                stages.Confirmed += stage => confirmed = stage;
                stages.RandomOption.Button.onClick.Invoke();
                Assert.That(stages.Rolling, Is.True, "RANDOM でルーレットが回らない");
                stages.Option(0).Button.onClick.Invoke();
                stages.Option(0).OnCancel(null);
                Assert.That(confirmed, Is.Null, "回っている間にほかの札で決まった");
                Assert.That(menu.ShowingStageSelect, Is.True, "回っている間に戻った");

                yield return WaitFor(() => confirmed != null, 8f, "ルーレットが止まらない");
                Assert.That(visited.Count, Is.GreaterThanOrEqualTo(stages.Count * 2), "札を回らない");
                Assert.That(visited[visited.Count - 1], Is.SameAs(confirmed), "止まったステージと決めたステージが違う");
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
                Assert.That(flow.CurrentStage, Is.SameAs(confirmed), "ルーレットのステージで始まらない");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
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
