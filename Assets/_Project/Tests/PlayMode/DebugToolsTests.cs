using System.Collections;
using Airerage.Core.Wire;
using Airerage.Runtime.DebugTools;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
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
