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
    /// 宇宙ステーションの端のワープゲートで、反対の端へ移る。予告は黄色の斜線と警告の記号で、吹雪は凍る所だけに出す（2026-10-20）。
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
                // 動く足場と扉（2026-10-26）: 見た目がシミュレーションの足場の位置にある（ないときは隠れている）。
                foreach (StagePlatformView platformView in UnityEngine.Object.FindObjectsByType<StagePlatformView>(FindObjectsSortMode.None))
                {
                    StagePlatform platform = platformView.GetComponent<StagePlatformAuthoring>().ToPlatform();
                    int platformIndex = feed.Platforms.ToList().FindIndex(p => p.SameAs(platform));
                    Assert.That(platformIndex, Is.GreaterThanOrEqualTo(0), $"{platformView.name} がシミュレーションの足場にない");
                    bool present = platform.TryGetCenter(feed.CurrentTick, feed.Stage.OpenOf(platformIndex), out System.Numerics.Vector2 center);
                    Assert.That(platformView.Shown, Is.EqualTo(present), $"{platformView.name} の見た目の出し方が、足場があるかと合わない");
                    if (present)
                    {
                        Assert.That(Vector2.Distance(platformView.ShownCenter, new Vector2(center.X, center.Y)), Is.LessThan(1f),
                            $"{platformView.name} の見た目が、足場の位置にない");
                    }
                }
                var glow = UnityEngine.Object.FindFirstObjectByType<AttachableGlow>();
                Assert.That(glow != null && glow.enabled && glow.BoxCount > 0, Is.True, $"{stage.DisplayName} のワイヤーが刺さる足場が光らない");
                Assert.That(glow.Brightness, Is.GreaterThan(0.5f), $"{stage.DisplayName} の足場の光が動いていない");
            }
        }

        [UnityTest]
        public IEnumerator Warnings_AreYellowStripesWithTheSign_AndTheBlizzardWarnsOnlyWhereItFreezes()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            StageDefinition snow = flow.Stages.Stages.First(s => s.SceneName == "Stage_SnowSea");
            flow.StartTraining(snow);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");

            // 予告のある（周期のある）ギミックは、どれも同じ予告の表示（黄色の斜線と警告の記号）を持つ。つららは警告音を鳴らさない。
            StageZoneView[] views = UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None);
            foreach (StageZoneView view in views.Where(v => v.GetComponent<StageZoneAuthoring>().ToZone().Period > 0))
            {
                Assert.That(view.Warning, Is.Not.Null, $"{view.name} に予告の表示がない");
                Assert.That(view.Warning.IconTexture, Is.Not.Null, $"{view.name} に警告の記号がない");
            }
            StageZoneView icicles = views.Single(v => v.name.Contains("Icicles"));
            Assert.That(icicles.WarningSound, Is.False, "つららが警告音を鳴らす");
            StageZoneView blizzardView = views.Single(v => v.GetComponent<StageZoneAuthoring>().Kind == StageZoneKind.Blizzard);
            Assert.That(blizzardView.WarningSound, Is.True, "吹雪の警告音がない");

            // 吹雪の予告は、凍る所（地形の中でなく、風上に地形がない所）だけを塗り、影（洞窟・船の中）には出さない。
            StageZone blizzard = blizzardView.GetComponent<StageZoneAuthoring>().ToZone();
            var errors = new System.Collections.Generic.List<string>();
            Airerage.Core.Physics.BoxTerrain terrain = SceneTerrainBuilder.Build(1 << 0, errors);
            var areas = blizzardView.ExposedAreas;
            Assert.That(areas.Count, Is.GreaterThan(0), "吹雪の予告を塗る所がない");
            bool Painted(float x, float y) => areas.Any(r => r.Contains(new Vector2(x, y)));
            var halfCell = new System.Numerics.Vector3(0.5f, 0.5f, 0.5f);
            int checkedCells = 0;
            for (float y = blizzard.MinY + 0.5f; y < blizzard.MaxY; y += 3f)
            {
                for (float x = blizzard.MinX + 0.5f; x < blizzard.MaxX; x += 3f)
                {
                    var point = new System.Numerics.Vector3(x, y, 0f);
                    bool freezes = !terrain.IsOverlapping(point, halfCell) && !StageZones.Sheltered(point, blizzard.Direction, terrain, blizzard.Limit);
                    Assert.That(Painted(x, y), Is.EqualTo(freezes), $"({x}, {y}) の予告が、凍るかと合わない");
                    checkedCells++;
                }
            }
            Assert.That(checkedCells, Is.GreaterThan(100));
            Assert.That(Painted(-35.5f, -2.5f), Is.False, "洞窟の中に吹雪の予告が出る");
            Assert.That(Painted(45.5f, -0.5f), Is.False, "船の中に吹雪の予告が出る");
            Assert.That(Painted(0.5f, 2.5f), Is.True, "真ん中の氷の上に吹雪の予告が出ない");

            // 縞は、塗る所の世界の位置に描く（ギミックの物体の位置だけずれない）。
            blizzardView.Warning.Show(areas, new[] { new Vector2(0.5f, 2.5f) }, 1f);
            Bounds stripes = blizzardView.Warning.StripeBounds;
            Assert.That(stripes.min.x, Is.EqualTo(areas.Min(r => r.xMin)).Within(0.01f), "縞の左の端がずれている");
            Assert.That(stripes.min.y, Is.EqualTo(areas.Min(r => r.yMin)).Within(0.01f), "縞の下の端がずれている");
            Assert.That(stripes.max.y, Is.EqualTo(areas.Max(r => r.yMax)).Within(0.01f), "縞の上の端がずれている");
            Assert.That(blizzardView.Warning.IconCount, Is.EqualTo(1));
            blizzardView.Warning.Hide();
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
