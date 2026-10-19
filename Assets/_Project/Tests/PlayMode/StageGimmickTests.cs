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
    /// 追加のステージ（docs/15 6.11）: どのステージもトレーニングで始まり（エラーなし、全員が出現位置に立つ）、ギミックの見た目が動く。
    /// 周期のあるギミック（風・間欠泉・危険・吹雪）の見た目（効いている間だけ出す柱など）が、試合のティック数と種で決まる効いているかと合う。
    /// 動くギミックの見た目が、試合のティック数で決まる位置にある。ワイヤーが刺さる足場が光る。
    /// 宇宙ステーションの端のワープゲートで、反対の端へ移る。
    /// </summary>
    public sealed class StageGimmickTests
    {
        [UnityTest]
        public IEnumerator EveryStage_StartsTraining_WithWorkingGimmickViews()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            for (int index = 1; index < flow.Stages.Count; index++)
            {
                StageDefinition stage = flow.Stages.Get(index);
                flow.StartTraining(stage);
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, $"{stage.DisplayName} のトレーニングにならない");
                Assert.That(flow.LoadedScenes[0], Is.EqualTo(stage.SceneName));
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(stage.SceneName), $"{stage.DisplayName} の光と空でない");
                var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
                Assert.That(runner.enabled, Is.True, $"{stage.DisplayName} で試合の仕組みが動いていない");
                ISimulationFeed feed = runner;
                yield return WaitSeconds(1.5f);
                for (int i = 0; i < feed.CharacterCount; i++)
                {
                    Assert.That(feed.GetState(i).Grounded, Is.True, $"{stage.DisplayName} の {i + 1} 人目が立っていない（{feed.GetState(i).Position}）");
                    Assert.That(feed.GetState(i).Combat.Falls, Is.EqualTo(0), $"{stage.DisplayName} の {i + 1} 人目が落ちた");
                }

                StageZoneView[] views = UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None);
                Assert.That(views.Length, Is.EqualTo(UnityEngine.Object.FindObjectsByType<StageZoneAuthoring>(FindObjectsSortMode.None).Length),
                    $"{stage.DisplayName} の見た目のないギミックがある");
                Assert.That(views.All(v => v.enabled && v.transform.childCount > 0), Is.True, $"{stage.DisplayName} のギミックの見た目が動いていない");
                int seed = feed.Rules.Seed;
                foreach (StageZoneView view in views)
                {
                    StageZone zone = view.GetComponent<StageZoneAuthoring>().ToZone();
                    int tick = feed.CurrentTick;
                    if (zone.Moves)
                    {
                        Vector3 expected = view.transform.position + new Vector3(zone.Travel.X, zone.Travel.Y, 0f) * zone.OffsetFraction(tick);
                        Assert.That(Vector3.Distance(view.ShownCenter, expected), Is.LessThan(1f), $"{view.name} の見た目が、動く位置にない（ティック {tick}）");
                    }
                    if (zone.Period <= 0)
                    {
                        continue;
                    }
                    if (zone.IsActive(tick - 3, seed) == zone.IsActive(tick + 3, seed))
                    {
                        Assert.That(view.ShowsActive, Is.EqualTo(zone.IsActive(tick, seed)), $"{view.name} の見た目が、効いているかと合わない（ティック {tick}）");
                    }
                }
                var glow = UnityEngine.Object.FindFirstObjectByType<AttachableGlow>();
                Assert.That(glow != null && glow.enabled && glow.BoxCount > 0, Is.True, $"{stage.DisplayName} のワイヤーが刺さる足場が光らない");
                Assert.That(glow.Brightness, Is.GreaterThan(0.5f), $"{stage.DisplayName} の足場の光が動いていない");
            }
        }

        [UnityTest]
        public IEnumerator SpaceStation_EdgeGate_WrapsThePlayerToTheOtherSide()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            StageDefinition space = flow.Stages.Stages.First(s => s.SceneName == "Stage_SpaceStation");
            flow.StartTraining(space);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            StageZone gate = UnityEngine.Object.FindObjectsByType<StageZoneAuthoring>(FindObjectsSortMode.None).Select(a => a.ToZone())
                .First(z => z.Kind == StageZoneKind.Warp && z.Target.X > 0f);
            yield return WaitSeconds(0.5f);

            // 西の端のゲートの手前から、左へ飛ばす。
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(gate.MaxX + 1.5f, gate.Center.Y, 0f);
            state.Velocity = new System.Numerics.Vector3(-14f, 2f, 0f);
            runner.SetPlayerState(state);
            yield return WaitFor(() => runner.PlayerState.Combat.WarpCount > 0, 2f, "端のゲートで移らない");
            CharacterState after = runner.PlayerState;
            Assert.That(after.Position.X, Is.GreaterThan(35f), "反対の端へ移らない");
            Assert.That(after.Velocity.X, Is.LessThan(0f), "向きを保たない");
            Assert.That(after.Combat.Falls, Is.EqualTo(0));
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
