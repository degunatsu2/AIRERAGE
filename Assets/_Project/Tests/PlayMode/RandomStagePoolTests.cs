using System;
using System.Collections;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
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
    /// オプションの RANDOM STAGES（ユーザーの要望、2026-10-26）: ステージ選択と同じ画面が出て、札の左端のチェックを付け外しする。
    /// チェックを外したステージは、ステージ選択の RANDOM で選ばれない。0 個の間は閉じられない。DONE か戻るでオプションへ。
    /// </summary>
    public sealed class RandomStagePoolTests
    {
        private InputSettings.EditorInputBehaviorInPlayMode _savedBehavior;
        private InputSettings.BackgroundBehavior _savedBackground;
        private Keyboard _keyboard;

        [SetUp]
        public void SetUp()
        {
            RandomStagePool.DeleteSaved();
            _savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            _savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [TearDown]
        public void TearDown()
        {
            RandomStagePool.DeleteSaved();
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode = _savedBehavior;
            InputSystem.settings.backgroundBehavior = _savedBackground;
        }

        [UnityTest]
        public IEnumerator UncheckedStages_AreNeverPickedByRandom()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
            menu.ContinueFromTitle();
            yield return null;
            menu.Options.Button.onClick.Invoke();
            yield return null;
            OptionsScreen options = menu.OptionsScreen;
            Assert.That(options.RandomStages, Is.Not.Null, "RANDOM STAGES の項目がない");
            options.RandomStages.Button.onClick.Invoke();
            yield return null;

            StageSelectScreen stages = menu.StageSelect;
            Assert.That(menu.ShowingStageSelect, Is.True, "ステージ選択と同じ画面が出ない");
            Assert.That(stages.PoolMode, Is.True);
            Assert.That(stages.RandomOption.gameObject.activeSelf, Is.False, "RANDOM STAGES に RANDOM の札が出ている");
            for (int i = 0; i < stages.Count; i++)
            {
                Assert.That(stages.CheckShown(i), Is.True, $"初めは {i} 番にチェックが付いていない");
            }

            // すべて外す・すべてチェック・DONE（2026-11-06）。0 個の間は閉じられない。
            Assert.That(stages.DoneOption.gameObject.activeSelf && stages.CheckAllOption.gameObject.activeSelf, Is.True,
                "RANDOM STAGES に DONE とすべてチェックの札がない");
            stages.UncheckAllOption.Button.onClick.Invoke();
            for (int i = 0; i < stages.Count; i++)
            {
                Assert.That(stages.CheckShown(i), Is.False, $"すべて外すで {i} 番が外れない");
            }
            stages.DoneOption.Button.onClick.Invoke();
            yield return null;
            Assert.That(menu.ShowingOptions, Is.False, "チェックが 0 個なのに閉じた");
            stages.CheckAllOption.Button.onClick.Invoke();
            Assert.That(stages.CheckShown(0) && stages.CheckShown(stages.Count - 1), Is.True, "すべてチェックで付かない");

            // 2 番だけを残して外す。
            const int kept = 2;
            for (int i = 0; i < stages.Count; i++)
            {
                if (i != kept)
                {
                    stages.Option(i).Button.onClick.Invoke();
                    Assert.That(stages.CheckShown(i), Is.False, $"{i} 番のチェックが外れない");
                }
            }
            Assert.That(stages.CheckShown(kept), Is.True);
            Assert.That(menu.ShowingOptions, Is.False, "決定でオプションに戻った");

            // DONE でオプションの RANDOM STAGES へ。
            stages.DoneOption.Button.onClick.Invoke();
            yield return null;
            Assert.That(menu.ShowingOptions, Is.True, "DONE でオプションに戻らない");
            Assert.That(stages.PoolMode, Is.False);

            // ステージ選択の RANDOM は、チェックの付いたステージだけから選ぶ。
            ulong mask = RandomStagePool.Mask(flow.Stages.Stages);
            Assert.That(RandomStagePool.Indices(stages.Count, mask), Is.EqualTo(new[] { kept }));
            options.Close();
            yield return null;
            menu.Training.Button.onClick.Invoke();
            yield return null;
            yield return null;
            yield return Tap(_keyboard, Key.Enter);
                        yield return WaitFor(() => menu.ShowingStageSelect, 3f, "前提: ステージ選択（決定の演出の後）");
            Assert.That(stages.PoolMode, Is.False);
            for (int i = 0; i < stages.Count; i++)
            {
                Assert.That(stages.CheckShown(i), Is.False, "ふつうのステージ選択にチェックが出ている");
            }
            StageDefinition confirmed = null;
            stages.Confirmed += stage => confirmed = stage;
            stages.RandomOption.Button.onClick.Invoke();
            yield return WaitFor(() => confirmed != null, 10f, "ルーレットが止まらない");
            Assert.That(confirmed, Is.SameAs(flow.Stages.Get(kept)), "チェックを外したステージが選ばれた");
            // 読み込みの途中で終えると、次のテストのシーンに試合のシーンだけが重なる。
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            Assert.That(flow.CurrentStage, Is.SameAs(confirmed));
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
