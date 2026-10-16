using System;
using System.Collections;
using Airerage.Core.Simulation;
using Airerage.Net;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
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
            try
            {
                // 前に選んだキャラクター（オンラインの待機所と同じ）で、トレーニングが始まる。
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, 3);
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                Assert.That(flow, Is.Not.Null, "App のシーンに GameFlow がない");
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                Assert.That(flow.LoadedScenes, Is.EquivalentTo(new[] { "Stage_Athletic", "Menu" }), "メニューの背景にステージがない");
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Stage_Athletic"), "光と空がステージのものでない");

                var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
                Assert.That(menu.ShowingTitle, Is.True, "最初にタイトルを出さない");
                menu.ContinueFromTitle();
                yield return null;
                yield return null;
                Assert.That(menu.ShowingMain, Is.True, "タイトルの後にメインメニューを出さない");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.Network.gameObject), "使える最初の項目を選んでいない");
                Assert.That(menu.Single.Button.interactable, Is.False, "SINGLE は Phase 7 まで選べない");
                Assert.That(menu.Local.Button.interactable, Is.False, "LOCAL VS は T6-5 まで選べない");
                Assert.That(menu.Options.Button.interactable, Is.False, "OPTIONS は T6-9 まで選べない");
                Assert.That(menu.Training.Button.interactable && menu.Quit.Button.interactable, Is.True);

                menu.Training.Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
                Assert.That(flow.LoadedScenes, Is.EqualTo(new[] { "Stage_Athletic", "Match_Training" }), "ステージを先に、試合の仕組みを後に読み込んでいない");
                Assert.That(UnityEngine.Object.FindFirstObjectByType<MenuScreens>(), Is.Null, "メニューが残っている");
                var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
                Assert.That(runner, Is.Not.Null);
                Assert.That(runner.enabled, Is.True, "試合の仕組みが動いていない（ステージの物が見つからない）");
                ISimulationFeed feed = runner;
                Assert.That(feed.CharacterCount, Is.EqualTo(4), "自分とダミー 3 体でない");
                Assert.That(runner.PlayerCharacter.DisplayName, Is.EqualTo(CharacterPresets.SpaceTravelerName), "前に選んだキャラクターで始まらない");
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
            }
        }

        [UnityTest]
        public IEnumerator NetworkVs_OpensTheOnlineScene_AndQuitGoesBackToTheMenu()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
            menu.ContinueFromTitle();
            yield return null;
            menu.Network.Button.onClick.Invoke();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Online && !flow.IsLoading, 10f, "ネットワーク対戦にならない");
            var online = UnityEngine.Object.FindFirstObjectByType<OnlineRunner>();
            Assert.That(online, Is.Not.Null, "オンラインのシーンがない");
            Assert.That(online.enabled, Is.True);

            // 結果の Quit（と、つなぐ前の「Back to menu」）は、ゲームを終えずにメニューへ戻る。
            online.Quit();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューに戻らない");
            Assert.That(UnityEngine.Object.FindFirstObjectByType<OnlineRunner>(), Is.Null, "オンラインのシーンが残っている");
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
            Assert.That(pause.BackToMenu.gameObject.activeSelf, Is.False, "App から始めていないのに BACK TO MENU がある");
            pause.ResetPosition.Button.onClick.Invoke();
            yield return null;
            Assert.That(pause.IsOpen, Is.False, "RESET POSITION の後に閉じない");
            Assert.That(runner.SpeedScale, Is.EqualTo(1f), "閉じた後に動き出さない");
            Assert.That(Mathf.Abs(runner.PlayerState.Position.X - spawn.x), Is.LessThan(0.5f), "出現位置に戻らない");
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
