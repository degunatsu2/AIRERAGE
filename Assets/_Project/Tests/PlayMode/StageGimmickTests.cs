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

        // ビル街の昼の上空の風は、同じ範囲に左向きと右向きが交互に吹く。予告の間は、吹く向きへ流れる黄色の「>」で向きを知らせる
        // （2026-11-18 の要望）。風の筋を線で描かないステージでも出し、吹き始めたら消す。
        [UnityTest]
        public IEnumerator CityDay_GustWarning_ShowsWhichWayTheWindWillBlow()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            StageDefinition city = flow.Stages.Stages.First(s => s.SceneName == "Stage_CityDay");
            flow.StartTraining(city);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");

            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            StageZoneView[] winds = UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None)
                .Where(v => v.name.StartsWith("BuildingWind")).ToArray();
            Assert.That(winds.Length, Is.EqualTo(2), "上空の左向きと右向きの風がない");
            Assert.That(winds.All(v => !v.WindLines), Is.True, "ビル街の昼は風のシェーダーで風を描く前提");
            foreach (Vector2 expected in new[] { Vector2.left, Vector2.right })
            {
                StageZoneView view = winds.Single(v => Vector2.Dot(v.WarningArrowDirection, expected) > 0.99f);
                // 試合の始まりの数ティックの短い予告は除く（すぐ吹き始めて、次のフレームには消えている）。同じフレームで確かめる。
                yield return WaitFor(() => view.ShowsWarning && runner.CurrentTick > 60, 14f, $"{view.name} の予告が出ない");
                Assert.That(view.WarningArrows, Is.GreaterThanOrEqualTo(4),
                    $"{view.name} の予告に向きの矢印がない（矢印の線 {view.GetComponentsInChildren<LineRenderer>(true).Count(l => l.name.StartsWith("WarningArrow"))} 本、"
                    + $"予告 {view.ShowsWarning}、ティック {runner.CurrentTick}）");
                LineRenderer arrow = view.GetComponentsInChildren<LineRenderer>().First(l => l.name == "WarningArrow1" && l.enabled);
                Vector3 back = arrow.GetPosition(0);
                Vector3 tip = arrow.GetPosition(1);
                Assert.That(Mathf.Sign(tip.x - back.x), Is.EqualTo(Mathf.Sign(expected.x)), $"{view.name} の矢印が吹く向きを指していない");
                Assert.That(arrow.startColor.r > 0.9f && arrow.startColor.g > 0.7f && arrow.startColor.b < 0.3f, Is.True, $"{view.name} の矢印が黄色でない");
                Vector2 before = view.FirstWarningArrowTip;
                yield return WaitSeconds(0.2f);
                if (view.ShowsWarning)
                {
                    float moved = Vector2.Dot(view.FirstWarningArrowTip - before, expected);
                    // 端で折り返した直後でなければ、吹く向きへ進んでいる。
                    Assert.That(moved > 0f || Mathf.Abs(moved) > 20f, Is.True, $"{view.name} の矢印が吹く向きへ流れない（{moved}）");
                }
                yield return WaitFor(() => !view.ShowsWarning, 6f, $"{view.name} の予告が終わらない");
                Assert.That(view.WarningArrows, Is.EqualTo(0), $"{view.name} が吹き始めても矢印が残る");
            }
        }

        /// <summary>
        /// 研究所のレーザーの網（2026-11-21 の指摘）: 効いている間の光線は、両端の発光装置の間（判定の範囲）だけに出る
        /// （柱の幅への引き伸ばしで、ステージの端から端まで伸びていた）。光線は細いまま。
        /// </summary>
        [UnityTest]
        public IEnumerator Lab_LaserBeams_RunOnlyBetweenTheEmitters()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Lab"));
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            StageZoneView view = UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None).Single(v => v.name == "LaserGrid");
            StageZone zone = view.GetComponent<StageZoneAuthoring>().ToZone();
            // 速めるのは効き始める少し前まで（4 倍だと重いフレーム 1 つで 1.2 秒の効いている間を飛び越えることがある）。
            Time.timeScale = 4f;
            try
            {
                yield return WaitFor(() => !view.ShowsActive && view.TicksUntilActive > 0 && view.TicksUntilActive < 90, 30f, "レーザーの網が効き始める前にならない");
            }
            finally
            {
                Time.timeScale = 1f;
            }
            yield return WaitFor(() => view.ShowsActive && view.ActiveElapsedTicks >= 0 && view.ActiveElapsedTicks < 40, 5f, "レーザーの網が効き始めない");
            yield return null;
            LineRenderer[] beams = view.GetComponentsInChildren<LineRenderer>().Where(l => l.name.StartsWith("Beam") && l.enabled).ToArray();
            Assert.That(beams.Length, Is.EqualTo(3), "効いている間の光線が 3 本ない");
            Camera camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null, "カメラがない");
            foreach (LineRenderer beam in beams)
            {
                Vector3 from = beam.transform.TransformPoint(beam.GetPosition(0));
                Vector3 to = beam.transform.TransformPoint(beam.GetPosition(1));
                Assert.That(Mathf.Min(from.x, to.x), Is.EqualTo(zone.MinX).Within(0.05f), $"{beam.name} の左の端が発光装置の所にない（{from} → {to}）");
                Assert.That(Mathf.Max(from.x, to.x), Is.EqualTo(zone.MaxX).Within(0.05f), $"{beam.name} の右の端が発光装置の所にない（{from} → {to}）");
                // 描かれる形（線の網をワールドの座標で作る。画面を描かないバッチモードでは Renderer.bounds が当てにならない）。
                var mesh = new Mesh();
                beam.BakeMesh(mesh, camera, true);
                Bounds bounds = mesh.bounds;
                UnityEngine.Object.Destroy(mesh);
                Assert.That(bounds.min.x, Is.GreaterThan(zone.MinX - 0.5f), $"{beam.name} の描かれる所が範囲の左へはみ出る（{bounds}）");
                Assert.That(bounds.max.x, Is.LessThan(zone.MaxX + 0.5f), $"{beam.name} の描かれる所が範囲の右へはみ出る（{bounds}）");
                Assert.That(bounds.size.y, Is.LessThan(0.5f), $"{beam.name} が太くなっている（{bounds}）");
            }
        }

        /// <summary>
        /// 埠頭の荷役の網（2026-11-21 の要望）: 上から落ちてきた人を跳ね返して網目が沈み（CargoNetView）、ワイヤーをつないでぶら下がると
        /// 網が下がってワイヤー（吊り具）が伸び、外すと戻り始める（動く足場 StagePlatformKind.Hoist）。
        /// </summary>
        [UnityTest]
        public IEnumerator Wharf_CargoNet_Bounces_AndLowersWhileAWireHangsOnIt()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Wharf"));
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            StagePlatformView view = UnityEngine.Object.FindObjectsByType<StagePlatformView>(FindObjectsSortMode.None).Single(v => v.name == "NetWest");
            StagePlatform platform = view.GetComponent<StagePlatformAuthoring>().ToPlatform();
            CargoNetView net = view.GetComponentInChildren<CargoNetView>();
            yield return WaitSeconds(0.5f);
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(platform.Origin.X + 0.6f, platform.Origin.Y + platform.HalfSize.Y + 2.5f, 0f);
            state.Velocity = new System.Numerics.Vector3(0f, -14f, 0f);
            state.Grounded = false;
            runner.SetPlayerState(state);
            yield return WaitFor(() => net.Catches > 0, 3f, $"網が受けない（{runner.PlayerState.Position}・{runner.PlayerState.Velocity}）");
            Assert.That(runner.PlayerState.Velocity.Y, Is.GreaterThan(5f), "網が跳ね返さない");

            // 網の真ん中のアンカーにワイヤーをつないだ状態にする（ぶら下がる）。網が下がり、ワイヤーが伸びる。
            float restCable = net.CableLength;
            int index = ((ISimulationFeed)runner).Platforms.ToList().FindIndex(p => p.SameAs(platform));
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            CharacterState hanger = runner.PlayerState;
            hanger.Position = new System.Numerics.Vector3(platform.Origin.X, platform.Origin.Y - 5f, 0f);
            hanger.Velocity = System.Numerics.Vector3.Zero;
            hanger.Wire.Phase = Airerage.Core.Wire.WirePhase.Attached;
            hanger.Wire.Anchor = new System.Numerics.Vector3(platform.Origin.X, platform.Origin.Y, 0f);
            hanger.Wire.AnchorBody = (byte)(index + 1);
            hanger.Wire.RopeLength = 5f;
            hanger.Wire.PhaseTicks = 0;
            runner.SetPlayerState(hanger);
            // ワイヤーのボタンを押し続ける（離すと外れる）。
            runner.InputOverride = new Repeat(Airerage.Core.Input.PlayerInput.FromAxes(0f, 0f, Airerage.Core.Input.InputButtons.Wire)
                .WithAim(System.Numerics.Vector2.UnitY));
            try
            {
                yield return WaitFor(() => ((ISimulationFeed)runner).Stage.OpenOf(index) >= StagePlatform.HoistScale, 3f,
                    $"ぶら下がっても網が下がらない（{((ISimulationFeed)runner).Stage.OpenOf(index)}・{runner.PlayerState.Wire.Phase}）");
                yield return null;
                Assert.That(view.ShownCenter.y, Is.LessThan(platform.Origin.Y - platform.OpenHeight * 0.8f), "網の見た目が下がらない");
                Assert.That(net.CableLength, Is.GreaterThan(restCable + platform.OpenHeight * 0.8f), "吊り具（ワイヤー）が伸びない");
            }
            finally
            {
                runner.InputOverride = null;
            }
        }

        private sealed class Repeat : ITickInputSource
        {
            private readonly Airerage.Core.Input.PlayerInput _input;

            public Repeat(Airerage.Core.Input.PlayerInput input)
            {
                _input = input;
            }

            public bool TryGetNext(out Airerage.Core.Input.PlayerInput input)
            {
                input = _input;
                return true;
            }
        }

        /// <summary>
        /// 恐怖の館の鬼火の輪（2026-11-21 の要望）: 飛び回る輪をくぐると 30 枚のコインをもらい、鬼火がその人の周りを回る（WispRingView）。
        /// </summary>
        [UnityTest]
        public IEnumerator Manor_WispRing_GivesCoins_AndCirclesWhoPassesThroughIt()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Manor"));
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            ISimulationFeed feed = runner;
            WispRingView view = UnityEngine.Object.FindFirstObjectByType<WispRingView>();
            StageTrap wisp = view.GetComponent<StageTrapAuthoring>().ToTrap();
            yield return WaitFor(() => feed.Rules.HasTimeLimit ? feed.CurrentTick > feed.Rules.CountdownTicks + 5 : feed.CurrentTick > 5, 10f, "試合が始まらない");
            Assert.That(view.ShowsRing, Is.True, "輪が出ていない");
            int before = runner.PlayerState.Coins.Held;
            bool taken = false;
            float limit = Time.realtimeSinceStartup + 3f;
            while (!taken && Time.realtimeSinceStartup < limit)
            {
                // 輪の次のティックの位置に置き続ける。
                System.Numerics.Vector2 next = wisp.WispAt(feed.CurrentTick + 1, feed.Rules.Seed);
                CharacterState state = runner.PlayerState;
                state.Position = new System.Numerics.Vector3(next.X, next.Y, 0f);
                state.Velocity = System.Numerics.Vector3.Zero;
                runner.SetPlayerState(state);
                yield return null;
                taken = runner.PlayerState.Coins.Held >= before + 30;
            }
            Assert.That(taken, Is.True, "輪をくぐっても 30 枚もらえない");
            yield return null;
            yield return null;
            Assert.That(view.Circling, Is.True, "鬼火がくぐった人の周りを回らない");
            Assert.That(view.ShowsRing, Is.False, "くぐった後も輪が出ている");
        }

        /// <summary>
        /// 恐怖の館のかぼちゃの投石機（2026-11-21）: かごの中の人は、腕が跳ね上がる瞬間に放られ、腕は横木まで振り上がる（CatapultView）。
        /// </summary>
        [UnityTest]
        public IEnumerator Manor_Catapult_FlingsWhoIsInTheBucket_AsTheArmSwingsUp()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == "Stage_Manor"));
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading, 10f, "トレーニングにならない");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            StageZoneView view = UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None).Single(v => v.name == "PumpkinLauncher");
            StageZone zone = view.GetComponent<StageZoneAuthoring>().ToZone();
            CatapultView catapult = UnityEngine.Object.FindFirstObjectByType<CatapultView>();
            // 予告（引き絞り）の間にかごへ入れる。
            yield return WaitFor(() => view.ShowsWarning, 6f, "投石機が引き絞らない");
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(zone.Center.X, zone.MinY + 0.95f, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            yield return WaitFor(() => runner.PlayerState.Velocity.Y > 20f, 3f, $"かごの中の人が放られない（{runner.PlayerState.Position}）");
            Assert.That(runner.PlayerState.Velocity.X, Is.LessThan(-5f), "館の方（左上）へ放られない");
            yield return WaitFor(() => catapult.ArmAngle > 90f, 1f, $"腕が横木まで振り上がらない（{catapult.ArmAngle}）");
            Assert.That(catapult.Throwing, Is.True, "かごのかぼちゃが飛んでいかない");
        }

        [UnityTest]
        public IEnumerator SpaceStation_EdgeWormholes_Wrap_TheCoilGunShootsWhereYouAim_AndTheCabinDrifts()
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

            // ワームホールの渦（2026-11-24: 前の紫の楕円の見た目を精巧に）: くぐると入った口と出口の渦に波紋が出る。
            WormholeView[] mouths = UnityEngine.Object.FindObjectsByType<WormholeView>(FindObjectsSortMode.None);
            WormholeView west = mouths.Single(m => m.GetComponentInParent<StageZoneView>().name == "WarpGateWest");
            WormholeView rescue = mouths.Single(m => m.GetComponentInParent<StageZoneView>().name == "RescueGate");
            Assert.That(rescue.ExitMouth, Is.Not.Null, "救出のゲートの出口に渦がない");
            yield return WaitFor(() => west.Pulses > 0 && west.ExitPulses > 0, 1f, $"くぐった口と出口に波紋が出ない（口 {west.Pulses}・出口 {west.ExitPulses}）");
            // 動く救出のゲートは、動く向きと逆へ撓む（前の見た目でユーザーが気に入っていたところ）。
            float bent = 0f;
            yield return WaitFor(() => (bent = Mathf.Max(bent, rescue.Bend.magnitude)) > 0.3f, 6f, $"動くゲートが撓まない（{rescue.Bend}）");

            // コイルガン（2026-11-24）: 触れると中に収まって 1 秒ため、ためている間は照準で向きを変えられ、たまったらその向きへ撃ち出す。
            yield return WaitSeconds(0.6f);
            CoilAcceleratorView coil = UnityEngine.Object.FindObjectsByType<CoilAcceleratorView>(FindObjectsSortMode.None)
                .Single(c => c.GetComponentInParent<StageZoneView>().name == "CoilGunCore");
            StageZone gun = coil.GetComponentInParent<StageZoneAuthoring>().ToZone();
            var aim = new Vector2(-0.6f, 0.8f);
            runner.InputOverride = new HoldAim(aim);
            CharacterState into = runner.PlayerState;
            into.Position = new System.Numerics.Vector3(gun.MinX - 0.6f, gun.Center.Y, 0f);
            into.Velocity = new System.Numerics.Vector3(4f, 0f, 0f);
            runner.SetPlayerState(into);
            CoilDischargeView discharge = coil.GetComponent<CoilDischargeView>();
            Assert.That(discharge, Is.Not.Null, "コイルガンのためと発射の演出がない");
            yield return WaitFor(() => runner.PlayerState.Combat.CaptureTicks > 0, 2f, "コイルに収まらない");
            yield return WaitFor(() => coil.ShowsTrajectory && coil.Charge > 0.3f, 1f, "ためている間に軌道の線が出ない");
            // ためている間は放電し、砲口に光の玉が育つ。満ちる直前は合図を出す（2026-11-24 の要望）。
            yield return WaitFor(() => discharge.ActiveArcs > 0 && discharge.OrbSize > 0.5f, 1f, $"ためている間に放電しない（弧 {discharge.ActiveArcs}・玉 {discharge.OrbSize}）");
            yield return WaitFor(() => discharge.Ready, 1f, "満ちる直前の合図が出ない");
            Assert.That(Mathf.DeltaAngle(coil.Angle, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg), Is.InRange(-15f, 15f), $"砲身が照準へ回らない（{coil.Angle}）");
            Vector2 launched = Vector2.zero;
            yield return WaitFor(() =>
            {
                CharacterState now = runner.PlayerState;
                if (now.Combat.CaptureTicks > 0)
                {
                    return false;
                }
                launched = new Vector2(now.Velocity.X, now.Velocity.Y);
                return true;
            }, 2f, "1 秒たっても撃ち出されない");
            runner.InputOverride = null;
            Assert.That(Vector2.Dot(launched, aim), Is.GreaterThan(18f), $"照準の向きへ撃ち出されない（{launched}）");
            // 表示は LateUpdate で撃ち出しを見つけるので、砲口がはじけてから数える（いちばん前のコイルは撃ち出す瞬間に通電する）。
            yield return WaitFor(() => coil.Shots > 0, 0.5f, "砲口がはじけない");
            yield return WaitFor(() => discharge.Flashes > 0, 0.5f, "砲口の閃光が出ない");
            Assert.That(coil.Firings, Is.GreaterThanOrEqualTo(3), $"コイルが順に満ちない（{coil.Firings} 回）");

            // 宇宙船の中: 人が通ると、漂う物が押される。
            MicrogravityView cabin = UnityEngine.Object.FindObjectsByType<MicrogravityView>(FindObjectsSortMode.None)
                .Single(c => c.GetComponentInParent<StageZoneView>().name == "ZeroGravityWest");
            Assert.That(cabin.Count, Is.GreaterThanOrEqualTo(8), "漂う物が少ない");
            StageZone zeroG = cabin.GetComponentInParent<StageZoneAuthoring>().ToZone();
            for (int pass = 0; pass < 3 && cabin.Pushes == 0; pass++)
            {
                CharacterState drifter = runner.PlayerState;
                drifter.Position = new System.Numerics.Vector3(zeroG.MinX + 0.8f, zeroG.MinY + 1.6f + pass * 2f, 0f);
                drifter.Velocity = new System.Numerics.Vector3(9f, 0f, 0f);
                runner.SetPlayerState(drifter);
                yield return WaitSeconds(1.4f);
            }
            Assert.That(cabin.Pushes, Is.GreaterThan(0), "宇宙船の中を通っても、漂う物が押されない");
        }

        // 照準を同じ向きに入れ続ける（コイルガンの向きを決める）。
        private sealed class HoldAim : ITickInputSource
        {
            private readonly sbyte _x;
            private readonly sbyte _y;

            public HoldAim(Vector2 aim)
            {
                _x = (sbyte)Mathf.RoundToInt(aim.x * Airerage.Core.Input.PlayerInput.AxisMax);
                _y = (sbyte)Mathf.RoundToInt(aim.y * Airerage.Core.Input.PlayerInput.AxisMax);
            }

            public bool TryGetNext(out Airerage.Core.Input.PlayerInput input)
            {
                input = new Airerage.Core.Input.PlayerInput(0, 0, Airerage.Core.Input.InputButtons.None, _x, _y);
                return true;
            }
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
