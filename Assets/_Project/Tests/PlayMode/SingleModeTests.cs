using System;
using System.Collections;
using Airerage.Core.Ai;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
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
    /// シングルモード（docs/16 T7-2）: SINGLE → キャラクター選択 → CPU の強さ（4 段階）→ ステージ選択 → 自分＋CPU 3 体の試合。CPU が動いて攻撃し、
    /// CPU の技の札は出さない。結果の「キャラクター選択へ」でシングルモードのキャラクター選択へ戻る。
    /// </summary>
    public sealed class SingleModeTests
    {
        [UnityTest]
        public IEnumerator Single_PicksACharacterAndLevel_ThenPlaysAgainstThreeCpus()
        {
            int savedCharacter = PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0);
            int savedLevel = PlayerPrefs.GetInt(CpuLevelScreen.PreferenceKey, (int)CpuLevel.Normal);
            InputSettings.EditorInputBehaviorInPlayMode savedBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, 2);
                PlayerPrefs.SetInt(CpuLevelScreen.PreferenceKey, (int)CpuLevel.Normal);
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                menu.ContinueFromTitle();
                yield return null;
                yield return null;
                Assert.That(menu.Single.Button.interactable, Is.True, "SINGLE を選べない");

                // SINGLE → キャラクター選択（1 人の画面）。決定で CPU の強さ。
                menu.Single.Button.onClick.Invoke();
                yield return null;
                yield return null;
                Assert.That(menu.ShowingCharacterSelect, Is.True, "SINGLE でキャラクター選択を出さない");
                Assert.That(menu.CharacterSelect.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Single));
                yield return Tap(keyboard, Key.Enter);
                yield return null;
                Assert.That(menu.ShowingCpuLevel, Is.True, "キャラクターを決めても CPU の強さを出さない");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.CpuLevel.Option(CpuLevel.Normal).gameObject),
                    "前に選んだ強さ（NORMAL）を選んでいない");

                // 戻るとキャラクター選択、もう一度決めて HELL（4 段階。D-71）。
                menu.CpuLevel.Option(CpuLevel.Normal).OnCancel(null);
                yield return null;
                yield return null;
                Assert.That(menu.ShowingCharacterSelect, Is.True, "CPU の強さから戻れない");
                yield return Tap(keyboard, Key.Enter);
                yield return null;
                menu.CpuLevel.Option(CpuLevel.Hell).Button.onClick.Invoke();
                yield return null;
                Assert.That(menu.ShowingStageSelect, Is.True, "強さを決めてもステージ選択を出さない");
                Assert.That(PlayerPrefs.GetInt(CpuLevelScreen.PreferenceKey), Is.EqualTo((int)CpuLevel.Hell), "選んだ強さを覚えない");

                menu.StageSelect.Option(0).Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Local && !flow.IsLoading, 10f, "試合にならない");
                Assert.That(flow.LocalSingle, Is.True);
                var runner = UnityEngine.Object.FindFirstObjectByType<LocalMatchRunner>();
                Assert.That(runner.CharacterCount, Is.EqualTo(4), "自分＋CPU 3 体でない");
                Assert.That(runner.IsCpu(0), Is.False, "自分が CPU になっている");
                Assert.That(runner.Players[0].Character, Is.EqualTo(2), "選んだキャラクターで始まらない");
                for (int p = 1; p < 4; p++)
                {
                    Assert.That(runner.IsCpu(p), Is.True, $"P{p + 1} が CPU でない");
                    Assert.That(runner.Players[p].CpuLevel, Is.EqualTo(CpuLevel.Hell));
                    Assert.That(runner.Players[p].Character, Is.Not.EqualTo(2), "CPU が自分と同じキャラクター（ほかがあるのに）");
                    Assert.That(runner.LabelOf(p), Is.EqualTo($"CPU{p + 1}"));
                }

                // CPU が動き、攻撃する。CPU の技の札は出さない（自分の札だけ）。
                Vector3 before = runner.World.GetState(1).Position.ToUnity();
                yield return WaitFor(() => runner.CpuOf(1).AttacksStarted + runner.CpuOf(2).AttacksStarted + runner.CpuOf(3).AttacksStarted > 0,
                    15f, "CPU が攻撃しない");
                Assert.That(Vector3.Distance(runner.World.GetState(1).Position.ToUnity(), before), Is.GreaterThan(0.5f), "CPU が動かない");
                foreach (SkillHud hud in UnityEngine.Object.FindObjectsByType<SkillHud>(FindObjectsSortMode.None))
                {
                    bool cpu = hud.Player >= 0 && runner.IsCpu(hud.Player);
                    for (int i = 0; i < 3; i++)
                    {
                        Assert.That(cpu && hud.SlotOf(i).Shown, Is.False, $"CPU（P{hud.Player + 1}）の技の札を出している");
                    }
                }

                // 結果の「キャラクター選択へ」: シングルモードのキャラクター選択へ戻る。
                runner.ReturnToCharacterSelect();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューに戻らない");
                yield return null;
                menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                Assert.That(menu.ShowingCharacterSelect, Is.True, "キャラクター選択に戻らない");
                Assert.That(menu.CharacterSelect.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Single), "シングルモードの画面でない");
            }
            finally
            {
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, savedCharacter);
                PlayerPrefs.SetInt(CpuLevelScreen.PreferenceKey, savedLevel);
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
