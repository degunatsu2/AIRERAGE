using System;
using System.Collections;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// メニューの演出（ユーザーの決定、2026-11-07）:
    /// - 画面の切り替え（D-87）: タイトル → メインはスカイダイブ（カメラの突進と速さの線）、メイン → キャラクター選択は斜めの札と
    ///   アイコンのめくれ、戻るときは逆向きの風。画面はすぐ入れ替わり、演出は写しで動き、終わると写しは残らない。押すと早送り。
    /// - ボタンの演出（D-88）: 導火線の縁・行きすぎと残像・裏の札・文字のデコード・背景の連動・決定の衝撃・アイコン・周りが引く。
    /// - 試合へ入るときは黒い帯が閉じ、読み込みが済むと開く（T10）。
    /// - 効果音: カーソルはダイヤルのカチッ、決定は新しい音（音声ファイル）。
    /// </summary>
    public sealed class MenuMotionTests
    {
        [UnityTest]
        public IEnumerator Menu_PlaysTransitions_AndButtonEffects_ThenLeavesNoCopies()
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
                var flow = Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
                var menu = Object.FindFirstObjectByType<MenuScreens>();
                ScreenTransition transition = menu.Transition;
                Assert.That(transition, Is.Not.Null, "画面の切り替えの演出がない");
                var camera = Object.FindFirstObjectByType<MenuCameraDrift>();
                var sounds = Object.FindFirstObjectByType<UiSounds>();

                // タイトル → メイン: スカイダイブ。画面はすぐ入れ替わり、写しが動き、カメラが前へ突進する。
                menu.ContinueFromTitle();
                Assert.That(menu.ShowingMain, Is.True, "タイトルの決定ですぐにメインメニューへ入れ替わらない");
                yield return null;
                yield return null;
                yield return null;
                Assert.That(transition.Running, Is.True, "演出が動いていない");
                Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.SkyDive));
                Assert.That(transition.GhostCount, Is.GreaterThan(0), "画面の写しを動かしていない");
                Assert.That(camera.Diving, Is.True, "背景のカメラが前へ突進しない");
                Assert.That(GameObject.Find("SpeedLines"), Is.Not.Null, "速さの線がない");
                yield return WaitFor(() => !transition.Running, 3f, "スカイダイブが終わらない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "演出が終わっても写しが残っている");
                Assert.That(menu.GetComponentInChildren<MainMenuBackdrop>(), Is.Not.Null);

                // ボタンの演出: 選ぶと導火線の縁・裏の札・文字のデコード・背景の名前・アイコン、ほかの項目は暗く下がる。カーソルの音。
                var backdrop = Object.FindFirstObjectByType<MainMenuBackdrop>();
                Assert.That(backdrop.Word, Is.EqualTo("SINGLE"), "最初の項目の名前を背景に出さない（B7）");
                EventSystem.current.SetSelectedGameObject(menu.Training.gameObject);
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiMove), "カーソルの音が鳴らない");
                Assert.That(menu.Training.Plate.FuseRunning, Is.True, "選んでも縁に光が走らない（B1）");
                Assert.That(menu.Training.Decoding, Is.True, "選んでも名前がばらばらに変わらない（B5）");
                Assert.That(menu.Training.GetComponentInChildren<MenuIcon>().Selected, Is.True, "絵が動かない（B12）");
                yield return WaitSeconds(0.45f);
                Assert.That(menu.Training.Decoding, Is.False, "名前が決まらない（B5）");
                Assert.That(menu.Training.Label, Is.EqualTo("TRAINING"));
                Assert.That(menu.Training.LabelText.text, Is.EqualTo("TRAINING"), "ばらばらにした名前が元に戻らない");
                Assert.That(menu.Training.Plate.Back, Is.GreaterThan(0.9f), "裏の札が出ない（B3）");
                Assert.That(menu.Single.IsReceded, Is.True, "ほかの項目が暗く下がらない（B13）");
                Assert.That(menu.Single.Plate.Back, Is.LessThan(0.1f), "選んでいない項目の裏の札が残っている");
                Assert.That(backdrop.Word, Is.EqualTo("TRAINING"), "背景の名前が変わらない（B7）");
                Assert.That(backdrop.Description, Is.Not.Empty, "背景に説明を出さない（B7）");

                // メイン → キャラクター選択: 決定の衝撃と、斜めの札とアイコンのめくれ。
                menu.Training.Button.onClick.Invoke();
                Assert.That(sounds.History[sounds.History.Count - 1], Is.EqualTo(SoundId.UiConfirm), "決定の音が鳴らない");
                Assert.That(menu.ShowingCharacterSelect, Is.True, "決定ですぐにキャラクター選択へ入れ替わらない");
                Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.SlantCascade));
                Assert.That(menu.Training.Burst, Is.Not.Null, "決定しても輪と火花が出ない（B10）");
                yield return null;
                yield return null;
                yield return null;
                Assert.That(GameObject.Find("Band"), Is.Not.Null, "斜めの札がない");
                yield return WaitFor(() => !transition.Running, 3f, "キャラクター選択への切り替えが終わらない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "演出が終わっても写しが残っている");
                var group = menu.CharacterSelect.GetComponent<CanvasGroup>();
                Assert.That(group == null || group.alpha >= 0.999f, Is.True, "演出が終わってもキャラクター選択が透明のまま");

                // 戻る: 逆向きの風。
                yield return Tap(keyboard, Key.Escape);
                Assert.That(menu.ShowingMain, Is.True, "キャラクター選択から戻れない");
                Assert.That(transition.LastKind, Is.EqualTo(TransitionKind.Gust));
                Assert.That(transition.LastDirection, Is.EqualTo(-1), "戻るときに向きが逆にならない");
                yield return WaitFor(() => !transition.Running, 3f, "戻る切り替えが終わらない");

                // 動いている途中に押すと、最後まで早送りする（押した入力は画面がそのまま受け取る）。
                menu.Training.Button.onClick.Invoke();
                yield return null;
                yield return null;
                yield return null;
                Assert.That(transition.Running, Is.True);
                yield return Tap(keyboard, Key.LeftShift);
                yield return null;
                Assert.That(transition.Running, Is.False, "押しても早送りしない");
                yield return null;
                Assert.That(CopiesLeft(), Is.Zero, "早送りしても写しが残っている");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.editorInputBehaviorInPlayMode = savedBehavior;
                InputSystem.settings.backgroundBehavior = savedBackground;
            }
        }

        [UnityTest]
        public IEnumerator Training_ClosesTheCurtain_ThenOpensIt_AfterLoading()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var curtain = Object.FindFirstObjectByType<ScreenCurtain>();
            Assert.That(curtain, Is.Not.Null, "試合へ入るときの黒い帯がない");
            flow.StartTraining(flow.Stages.Get(0));
            yield return null;
            Assert.That(curtain.Showing, Is.True, "試合へ入るときに黒い帯を出さない");
            Assert.That(NavigationGate.Current.Held, Is.True, "帯を閉じている間にメニューの決定を止めない");
            yield return WaitSeconds(0.3f);
            Assert.That(flow.LoadedScenes, Does.Contain("Menu"), "帯が閉じる前にメニューを外した");
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 15f, "トレーニングにならない");
            yield return WaitFor(() => !curtain.Showing, 2f, "読み込みが済んでも黒い帯が開かない");
            Assert.That(NavigationGate.Current.Held, Is.False, "帯が開いてもメニューの決定を止めたまま");
            Assert.That(EventSystem.current.sendNavigationEvents, Is.True);
        }

        [UnityTest]
        public IEnumerator UiSounds_UseTheNewCursorAndConfirmFiles()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var sounds = Object.FindFirstObjectByType<UiSounds>();
            Assert.That(sounds, Is.Not.Null);
            Assert.That(sounds.ClipOf(SoundId.UiMove).name, Is.EqualTo("UiCursor"), "カーソルの音がダイヤルのカチッでない");
            Assert.That(sounds.ClipOf(SoundId.UiConfirm).name, Is.EqualTo("UiConfirm"), "決定の音が新しい音でない");
            Assert.That(sounds.ClipOf(SoundId.UiTitleStart).name, Is.EqualTo("TitleStart"));
            Assert.That(sounds.ClipOf(SoundId.UiCharacterDecide).name, Is.EqualTo("CharacterDecide"));
            Assert.That(sounds.ClipOf(SoundId.UiMove).length, Is.LessThan(0.1f), "カーソルの音が長い");
        }

        // 演出の写し（名前の終わりが「(Transition)」）の数。
        private static int CopiesLeft()
        {
            int count = 0;
            foreach (RectTransform rect in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rect.name.EndsWith("(Transition)", StringComparison.Ordinal))
                {
                    count++;
                }
            }
            return count;
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
