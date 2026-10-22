using System;
using System.Collections;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
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
    /// ローカル対戦（docs/15 T6-5。D-67）: メインメニューの LOCAL VS → 機器ごとの参加とキャラクター選択（アイコンを選ぶ形）（キーボードとゲームパッド）→
    /// ステージ選択 → 試合。人ごとの機器でそのキャラクターだけが動き、全員が入るカメラ、人ごとの技の札、一時停止（RESET POSITION なし）、
    /// メニューへ戻る。機器は仮想のキーボードとゲームパッドを足して、状態の出来事を送る。
    /// </summary>
    public sealed class LocalVsTests
    {
        [UnityTest]
        public IEnumerator LocalVs_JoinsByDevice_ChoosesTheStage_AndEachDeviceMovesItsOwnCharacter()
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
                Assert.That(menu.Local.Button.interactable, Is.True, "LOCAL VS を選べない");
                menu.Local.Button.onClick.Invoke();
                yield return null;
                Assert.That(menu.ShowingCharacterSelect, Is.True, "LOCAL VS でキャラクター選択を出さない");
                CharacterSelectScreen join = menu.CharacterSelect;
                Assert.That(join.Mode, Is.EqualTo(CharacterSelectScreen.SelectMode.Local));

                // 機器ごとに参加する（キーボードが P1、ゲームパッドが P2）。
                yield return Tap(keyboard, Key.Enter);
                Assert.That(join.JoinedCount, Is.EqualTo(1), "Enter で参加しない");
                yield return Press(gamepad, GamepadButton.South);
                Assert.That(join.JoinedCount, Is.EqualTo(2), "ゲームパッドの A で参加しない");
                int before = join.CharacterOf(1);
                yield return Press(gamepad, GamepadButton.DpadRight);
                int character = join.CharacterOf(1);
                Assert.That(character, Is.Not.EqualTo(before), "十字キーでキャラクターが変わらない");
                Assert.That(join.ShownState(1), Is.EqualTo("SELECTING"), "2P の枠に状態を出さない");
                yield return Press(gamepad, GamepadButton.Select);
                Assert.That(join.DetailsOpen, Is.True, "ゲームパッドの Select で技の説明を出さない");
                yield return Press(gamepad, GamepadButton.Select);
                Assert.That(join.DetailsOpen, Is.False);
                Assert.That(join.CharacterOf(0), Is.Not.EqualTo(-1));
                yield return Press(gamepad, GamepadButton.North);
                Assert.That(join.Timed, Is.False, "Y で時間の制限が変わらない");
                yield return Press(gamepad, GamepadButton.North);
                Assert.That(join.Timed, Is.True);

                // 1 人だけ READY ではまだ進まない。全員 READY でステージ選択へ。
                yield return Tap(keyboard, Key.Enter);
                Assert.That(join.IsReady(0), Is.True, "Enter で READY にならない");
                Assert.That(menu.ShowingCharacterSelect, Is.True, "全員が READY になる前に進んだ");
                yield return Press(gamepad, GamepadButton.South);
                yield return null;
                Assert.That(menu.ShowingStageSelect, Is.True, "全員が READY でステージ選択に進まない");

                // ステージ選択から戻ると、参加した人はそのまま（READY だけ外す）。
                menu.StageSelect.Option(0).OnCancel(null);
                yield return null;
                Assert.That(menu.ShowingCharacterSelect, Is.True, "ステージ選択からキャラクター選択へ戻らない");
                Assert.That(join.JoinedCount, Is.EqualTo(2), "戻ったら参加した人が消えた");
                Assert.That(join.IsReady(0) || join.IsReady(1), Is.False, "戻ったのに READY のまま");
                Assert.That(join.CharacterOf(1), Is.EqualTo(character), "戻ったらキャラクターが変わった");
                yield return Tap(keyboard, Key.Enter);
                yield return Press(gamepad, GamepadButton.South);
                yield return null;
                Assert.That(menu.ShowingStageSelect, Is.True);
                menu.StageSelect.Option(0).Button.onClick.Invoke();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Local && !flow.IsLoading, 10f, "ローカル対戦にならない");
                Assert.That(flow.LoadedScenes, Is.EqualTo(new[] { "Stage_Athletic", "Match_Local" }), "ステージに試合の仕組みを重ねていない");

                var runner = UnityEngine.Object.FindFirstObjectByType<LocalMatchRunner>();
                Assert.That(runner, Is.Not.Null);
                Assert.That(runner.enabled, Is.True, "試合の仕組みが動いていない");
                Assert.That(runner.CharacterCount, Is.EqualTo(2), "2 人で始まらない");
                Assert.That(runner.Players[1].Character, Is.EqualTo(character), "選んだキャラクターで始まらない");
                Assert.That(runner.World.Rules.DurationTicks, Is.GreaterThan(0), "時間の制限がない");

                // 頭上のプレイヤーの番号の札（ローカル対戦だけ。2026-10-22）: プレイヤーの色の枠と番号を、キャラクターの頭の上に出す。
                var tags = UnityEngine.Object.FindFirstObjectByType<PlayerTagHud>();
                Assert.That(tags, Is.Not.Null, "頭上の番号の札がない");
                yield return null;
                Camera view = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
                for (int p = 0; p < runner.CharacterCount; p++)
                {
                    Assert.That(tags.TryGetTag(p, out Rect rect, out string label, out Color color), Is.True, $"P{p + 1} の札を出さない");
                    Assert.That(label, Is.EqualTo(runner.LabelOf(p)), "番号が違う");
                    Assert.That(color, Is.EqualTo(runner.ColorOf(p)), "枠がプレイヤーの色でない");
                    Vector3 body = view.WorldToScreenPoint(runner.GetState(p).Position.ToUnity());
                    Assert.That(rect.yMax, Is.LessThan(Screen.height - body.y), "札が頭の上にない");
                    Assert.That(rect.center.x, Is.EqualTo(body.x).Within(rect.width), "札がキャラクターの上にない");
                }

                // カウントダウンの後、それぞれの機器でそのキャラクターだけが動く。
                int playing = runner.World.Rules.CountdownTicks + 10;
                yield return WaitFor(() => runner.CurrentTick > playing && runner.GetState(0).Grounded && runner.GetState(1).Grounded, 8f,
                    "試合が始まらないか、地面に立たない");
                float p1 = runner.GetState(0).Position.X;
                float p2 = runner.GetState(1).Position.X;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                yield return WaitSeconds(0.4f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Assert.That(runner.GetState(0).Position.X - p1, Is.GreaterThan(1f), "キーボードで P1 が動かない");
                Assert.That(Mathf.Abs(runner.GetState(1).Position.X - p2), Is.LessThan(0.1f), "キーボードで P2 も動いた");

                // 離した後の滑りが止まってから、ゲームパッドを試す。
                yield return WaitFor(() => Mathf.Abs(runner.GetState(0).Velocity.X) < 0.01f, 3f, "P1 が止まらない");
                p1 = runner.GetState(0).Position.X;
                p2 = runner.GetState(1).Position.X;
                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(-1f, 0f) });
                yield return WaitSeconds(0.4f);
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
                yield return null;
                Assert.That(runner.GetState(1).Position.X - p2, Is.LessThan(-1f), "ゲームパッドで P2 が動かない");
                Assert.That(Mathf.Abs(runner.GetState(0).Position.X - p1), Is.LessThan(0.1f), "ゲームパッドで P1 も動いた");

                // 全員が入るカメラと、人ごとの技の札（参加した 2 人分だけ）。
                var camera = UnityEngine.Object.FindFirstObjectByType<SharedCamera>();
                Assert.That(camera, Is.Not.Null, "全員が入るカメラがない");
                Assert.That(camera.Distance, Is.GreaterThanOrEqualTo(30f - 0.01f));
                int shown = 0;
                foreach (SkillHud hud in UnityEngine.Object.FindObjectsByType<SkillHud>(FindObjectsSortMode.None))
                {
                    shown += hud.SlotOf(0).Shown ? 1 : 0;
                }
                Assert.That(shown, Is.EqualTo(2), "技の札が参加した人数分ない");

                // 一時停止: Esc で開いて止まり（RESET POSITION はない）、もう一度 Esc で閉じる。
                var pause = UnityEngine.Object.FindFirstObjectByType<PauseMenu>();
                yield return Tap(keyboard, Key.Escape);
                Assert.That(pause.IsOpen, Is.True, "Esc で一時停止しない");
                Assert.That(runner.SpeedScale, Is.Zero);
                Assert.That(pause.ResetPosition.gameObject.activeSelf, Is.False, "対戦なのに RESET POSITION がある");
                yield return null;
                yield return Tap(keyboard, Key.Escape);
                Assert.That(pause.IsOpen, Is.False, "もう一度 Esc で閉じない");
                Assert.That(runner.SpeedScale, Is.EqualTo(1f));

                // 結果の Restart で始め直し、Quit でメニューへ。
                runner.Restart();
                Assert.That(runner.CurrentTick, Is.LessThan(5), "Restart で始め直さない");
                runner.Quit();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューに戻らない");
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

        [Test]
        public void SharedCamera_PullsBack_ToFitEveryone()
        {
            // 縦の画角 40 度、16:9。横に 40m 離れた 2 人（余白なし）は、横で決まる。
            float distance = SharedCamera.RequiredDistance(new Vector2(20f, 0f), 40f, 16f / 9f);
            float tan = Mathf.Tan(20f * Mathf.Deg2Rad);
            Assert.That(distance, Is.EqualTo(20f / (tan * 16f / 9f)).Within(1e-3f));
            // 縦に離れているときは縦で決まる。
            Assert.That(SharedCamera.RequiredDistance(new Vector2(1f, 10f), 40f, 16f / 9f), Is.EqualTo(10f / tan).Within(1e-3f));
        }

        // キーを押して、次のフレームで離す。
        private static IEnumerator Tap(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        // ゲームパッドのボタンを押して、次のフレームで離す。
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
