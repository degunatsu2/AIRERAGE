using System;
using System.Collections;
using System.Linq;
using Airerage.Core.Simulation;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// ステージの仕掛けと悪魔の手の見た目（ユーザーの要望、2026-10-26）: 研究所の赤外線に触れると予告が出てミサイルが飛び、競技場の
    /// ピッチャーマシンがボールを打ち出し、恐怖の館の悪魔の手に握られると指と渦が出て、吸い込まれて見えなくなってから牢屋に現れる。
    /// </summary>
    public sealed class StageTrapViewTests
    {
        [UnityTest]
        public IEnumerator Lab_TouchingTheInfrared_ShowsTheWarning_ThenFiresTheMissile()
        {
            yield return StartTraining("Stage_Lab");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            StageTrapView view = UnityEngine.Object.FindObjectsByType<StageTrapView>(FindObjectsSortMode.None)
                .Single(v => v.name == "InfraredWest");
            StageTrap trap = view.GetComponent<StageTrapAuthoring>().ToTrap();
            yield return WaitSeconds(0.5f);
            // 実験室の屋根の上（赤外線の真ん中）に立たせる。
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(trap.Muzzle.X + trap.Range * 0.5f, 7.6f + 0.91f, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            yield return WaitFor(() => view.ShowsWarning, 2f, "赤外線に触れても予告が出ない");
            yield return WaitFor(() => view.ShowsProjectile || runner.PlayerState.Combat.HazardHitCount > 0, 2f, "予告の後にミサイルが飛ばない");
            yield return WaitFor(() => runner.PlayerState.Combat.HazardHitCount > 0, 2f, "ミサイルが当たらない");
            Assert.That(runner.PlayerState.Velocity.Length(), Is.GreaterThan(20f), "ミサイルでとても吹き飛ばない");
        }

        [UnityTest]
        public IEnumerator Stadium_PitchingMachine_ThrowsABall()
        {
            yield return StartTraining("Stage_Stadium");
            StageTrapView[] views = UnityEngine.Object.FindObjectsByType<StageTrapView>(FindObjectsSortMode.None);
            Assert.That(views.Length, Is.EqualTo(2), "ピッチャーマシンが 2 つない");
            // 最初のボールは 5 秒後、次は 15 秒後（10 秒ずらし）。
            yield return WaitFor(() => views.Any(v => v.ShowsProjectile), 8f, "ボールが出ない");
            StageTrapView throwing = views.First(v => v.ShowsProjectile);
            Vector3 first = throwing.transform.Find("Ball").position;
            yield return WaitSeconds(0.3f);
            Assert.That(Vector3.Distance(throwing.transform.Find("Ball").position, first), Is.GreaterThan(1f), "ボールが飛ばない");
        }

        [UnityTest]
        public IEnumerator Manor_DemonHand_GripsAndSucksYouIn_ThenYouAppearInThePrison()
        {
            yield return StartTraining("Stage_Manor");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            yield return WaitSeconds(0.5f);
            // 墓場の地面（左の手の通り道）に立たせる。
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(-41f, 0.91f, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            yield return WaitFor(() => runner.PlayerState.Combat.IsCaptured, 18f, "悪魔の手に握られない");
            // 見た目は表示のフレームで変わる（握られた次のフレーム）。
            CharacterView view = null;
            yield return WaitFor(() => (view = UnityEngine.Object.FindObjectsByType<CharacterView>(FindObjectsSortMode.None)
                .FirstOrDefault(v => v.CaptureEffect != null && v.CaptureEffect.Showing)) != null, 0.5f, "握られた見た目が出ない");
            yield return WaitFor(() => UnityEngine.Object.FindObjectsByType<DemonHandView>(FindObjectsSortMode.None).Any(h => h.Gripping), 0.5f,
                "手が握る形にならない");
            yield return WaitFor(() => view.HiddenForCapture || !runner.PlayerState.Combat.IsCaptured, 2f, "渦に吸い込まれて見えなくならない");
            yield return WaitFor(() => !runner.PlayerState.Combat.IsCaptured, 3f, "牢屋へ移らない");
            Assert.That(runner.PlayerState.Position.Y, Is.GreaterThan(25f), "牢屋に現れない");
            Assert.That(view.HiddenForCapture, Is.False, "牢屋に現れても見えない");
            Assert.That(view.CaptureEffect.Showing, Is.False, "牢屋に現れても握られた見た目が残る");
        }

        private static IEnumerator StartTraining(string scene)
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            StageDefinition stage = flow.Stages.Stages.First(s => s.SceneName == scene);
            flow.StartTraining(stage);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
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
