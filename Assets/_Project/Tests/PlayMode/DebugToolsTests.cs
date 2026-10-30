using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Airerage.Core.Wire;
using Airerage.Runtime.DebugTools;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// デバッグ機能（docs/06_prototype_plan.md T1-9）とカメラのズームを Sandbox_Wire で動かして確かめる
    /// （キーの代わりにメソッドを呼ぶ）。
    /// </summary>
    public sealed class DebugToolsTests
    {
        [UnityTest]
        public IEnumerator PauseStepToggleTypeAndTrail()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var controls = Object.FindFirstObjectByType<DebugControls>();
            var hud = Object.FindFirstObjectByType<DebugHud>();
            var trail = Object.FindFirstObjectByType<TrailView>();
            Assert.That(controls, Is.Not.Null, "DebugControls がない");
            Assert.That(hud, Is.Not.Null, "DebugHud がない");
            Assert.That(trail, Is.Not.Null, "TrailView がない");

            yield return WaitSeconds(0.5f);
            Assert.That(trail.PointCount, Is.GreaterThan(10), "軌跡が記録されていない");
            Assert.That(hud.Stats.MaxSpeed, Is.GreaterThan(0f), "出現後の落下の速さが計測されていない");

            // トレーニングの表示（右下の 6 つ。ユーザーの要望、2026-10-30）: まだ当てていない・出していないときは「—」。F4 で隠して、もう一度で出す。
            Assert.That(hud.Visible, Is.True);
            string[] values = hud.Values;
            Assert.That(values.Length, Is.EqualTo(DebugHud.Labels.Length));
            Assert.That(values[0], Is.EqualTo("—"), "当てていないのにダメージがある");
            Assert.That(values[2], Does.EndWith("m/s"), "速度に単位がない");
            hud.Toggle();
            Assert.That(hud.Visible, Is.False, "隠せない");
            hud.Toggle();
            Assert.That(hud.Visible, Is.True);

            // 速さ: 1 → 1/2 → 1/4 → 一時停止
            controls.CycleSpeed();
            Assert.That(runner.SpeedScale, Is.EqualTo(0.5f));
            controls.CycleSpeed();
            controls.CycleSpeed();
            Assert.That(runner.SpeedScale, Is.EqualTo(0f));

            int pausedTick = runner.CurrentTick;
            yield return WaitSeconds(0.3f);
            Assert.That(runner.CurrentTick, Is.EqualTo(pausedTick), "一時停止中にティックが進んだ");

            runner.StepOnce();
            Assert.That(runner.CurrentTick, Is.EqualTo(pausedTick + 1), "コマ送りで1ティック進まない");

            controls.CycleSpeed();
            Assert.That(runner.SpeedScale, Is.EqualTo(1f));

            // タイプの切り替え（2回で元に戻す。アセットの値を変えるため）
            WireModelType original = runner.PlayerConfig.Wire.ModelType;
            controls.ToggleWireType();
            Assert.That(runner.PlayerConfig.Wire.ModelType, Is.Not.EqualTo(original));
            controls.ToggleWireType();
            Assert.That(runner.PlayerConfig.Wire.ModelType, Is.EqualTo(original));
        }

        [UnityTest]
        public IEnumerator CameraZoom_StaysWithinLimits_AndMovesTheCamera()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var zoom = Object.FindFirstObjectByType<CameraZoom>();
            var composer = Object.FindFirstObjectByType<CinemachinePositionComposer>();
            Assert.That(zoom, Is.Not.Null, "CameraZoom がない");
            Assert.That(zoom.MinDistance, Is.LessThan(composer.CameraDistance), "前提: 初期の距離から近づける");
            Assert.That(zoom.MaxDistance, Is.GreaterThan(composer.CameraDistance), "前提: 初期の距離から離せる");

            zoom.ZoomIn(1000f);
            Assert.That(zoom.TargetDistance, Is.EqualTo(zoom.MinDistance));
            yield return WaitSeconds(1f);
            Assert.That(composer.CameraDistance, Is.EqualTo(zoom.MinDistance).Within(0.1f), "カメラが近づかない");

            zoom.ZoomIn(-1000f);
            Assert.That(zoom.TargetDistance, Is.EqualTo(zoom.MaxDistance));
            yield return WaitSeconds(1f);
            Assert.That(composer.CameraDistance, Is.EqualTo(zoom.MaxDistance).Within(0.1f), "カメラが離れない");

            // ホイールの押し込み・十字キーの左右で、初期の距離に戻る。
            Assert.That(zoom.DefaultDistance, Is.EqualTo(30f).Within(0.01f), "初期の距離が違う");
            zoom.ResetZoom();
            yield return WaitSeconds(1f);
            Assert.That(composer.CameraDistance, Is.EqualTo(zoom.DefaultDistance).Within(0.1f), "初期の距離に戻らない");
        }

        [UnityTest]
        public IEnumerator Gamepad_SpecialsOnTheShoulders_ZoomAndResetOnTheDpad()
        {
            // D-56（2026-10-15、ユーザー）: LB / RB を特殊攻撃に、ズームを十字キーの上下に、ズームのリセットを十字キーの左右に。
            // 十字キーの検証用の操作はキーボードだけにする。
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var zoom = Object.FindFirstObjectByType<CameraZoom>();
            var asset = (InputActionAsset)typeof(CameraZoom)
                .GetField("_inputActions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(zoom);
            Assert.That(asset, Is.Not.Null, "前提: 入力のアセットがある");

            Assert.That(Paths(asset, "Player", "Special1"), Does.Contain("<Gamepad>/leftShoulder").And.Contain("<Keyboard>/q"));
            Assert.That(Paths(asset, "Player", "Special2"), Does.Contain("<Gamepad>/rightShoulder").And.Contain("<Keyboard>/e"));
            Assert.That(Paths(asset, "Player", "Super"), Does.Contain("<Keyboard>/r"));
            Assert.That(Paths(asset, "Camera", "HoldZoom"), Does.Contain("<Gamepad>/dpad/up").And.Contain("<Gamepad>/dpad/down"));
            Assert.That(Paths(asset, "Camera", "ResetZoom"),
                Does.Contain("<Gamepad>/dpad/left").And.Contain("<Gamepad>/dpad/right").And.Contain("<Mouse>/middleButton"));

            foreach (InputBinding binding in asset.FindActionMap("Camera", true).bindings)
            {
                Assert.That(binding.path, Does.Not.Contain("Shoulder"), $"カメラが肩のボタンをまだ使っている: {binding.action}");
            }
            foreach (InputBinding binding in asset.FindActionMap("Debug", true).bindings)
            {
                Assert.That(binding.path, Does.Not.StartWith("<Gamepad>/dpad"), $"検証用の操作が十字キーをまだ使っている: {binding.action}");
            }
        }

        // アクションの割り当て（合成の部分を含む）のパス。
        private static List<string> Paths(InputActionAsset asset, string map, string action)
        {
            return asset.FindActionMap(map, true).FindAction(action, true).bindings
                .Where(b => !b.isComposite).Select(b => b.path).ToList();
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
