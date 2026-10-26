using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// ステージのシーン（docs/15 T6-2、6.11）の配置を検査する。ステージを作り直したときに、置き方の誤り（地形に埋まった出現位置や
    /// コイン、床から浮いたジャンプ台、拠点の中のコイン、どこにも降りられない発射台）を見つけるため。遊び心地は確かめられない。
    /// </summary>
    public sealed class StageLayoutTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Stages/StageCatalog.asset";

        // ステージ選択に並ぶ順（StageCatalog と同じ）。
        private static readonly string[] Stages =
        {
            "Stage_Athletic", "Stage_CityDay", "Stage_CityNight", "Stage_Volcano", "Stage_SnowSea", "Stage_SpaceStation",
            "Stage_Lab", "Stage_Kitchen", "Stage_Manor", "Stage_Stadium", "Stage_Wharf",
        };

        // コインの総量（出現地点の価値の合計）の下限。2026-10-21 に総量を 2 倍にした（ユーザーの要望。その前の総量の 2 倍）。
        private static readonly Dictionary<string, int> MinimumCoinTotals = new Dictionary<string, int>
        {
            { "Stage_Athletic", 366 }, { "Stage_CityDay", 356 }, { "Stage_CityNight", 354 }, { "Stage_Volcano", 360 }, { "Stage_SnowSea", 362 },
            { "Stage_SpaceStation", 278 },
            // 2026-10-26 に加えたステージ（ユーザーの案）。ほかのステージと同じくらい。
            { "Stage_Lab", 340 }, { "Stage_Kitchen", 340 }, { "Stage_Manor", 340 }, { "Stage_Stadium", 340 }, { "Stage_Wharf", 340 },
        };

        // ステージ固有のギミック（どのステージにもあるジャンプ台・上昇気流・加速リング以外）。
        private static readonly Dictionary<string, StageZoneKind[]> UniqueGimmicks = new Dictionary<string, StageZoneKind[]>
        {
            { "Stage_CityDay", new[] { StageZoneKind.SpeedLane, StageZoneKind.Launcher, StageZoneKind.Gust } },
            { "Stage_CityNight", new[] { StageZoneKind.Bumper, StageZoneKind.Gust } },
            { "Stage_Volcano", new[] { StageZoneKind.Hazard, StageZoneKind.Launcher, StageZoneKind.Bumper } },
            { "Stage_SnowSea", new[] { StageZoneKind.Ice, StageZoneKind.SpeedLane, StageZoneKind.Launcher, StageZoneKind.Hazard, StageZoneKind.Blizzard, StageZoneKind.Water } },
            { "Stage_SpaceStation", new[] { StageZoneKind.LowGravity, StageZoneKind.Warp, StageZoneKind.Launcher } },
            { "Stage_Lab", new[] { StageZoneKind.Water, StageZoneKind.Gust, StageZoneKind.Warp, StageZoneKind.LowGravity, StageZoneKind.Hazard } },
            { "Stage_Kitchen", new[] { StageZoneKind.Hazard, StageZoneKind.Launcher, StageZoneKind.Water, StageZoneKind.Ice, StageZoneKind.SpeedLane } },
            { "Stage_Manor", new[] { StageZoneKind.Water, StageZoneKind.Hazard, StageZoneKind.Gust, StageZoneKind.Warp, StageZoneKind.Bumper } },
            { "Stage_Stadium", new[] { StageZoneKind.Gust, StageZoneKind.SpeedLane, StageZoneKind.Launcher, StageZoneKind.Bumper } },
            { "Stage_Wharf", new[] { StageZoneKind.Water, StageZoneKind.Gust, StageZoneKind.SpeedLane, StageZoneKind.Bumper } },
        };

        private static readonly NVector3 CharacterHalf = new NVector3(0.4f, 0.9f, 0.4f);
        private static readonly NVector3 CoinHalf = new NVector3(0.3f, 0.3f, 0.3f);

        private Scene _scene;
        private BoxTerrain _terrain;
        private StageBounds _bounds;
        private List<StageZone> _zones;
        private List<CoinSpawnPoint> _coins;
        private List<BankSpot> _banks;
        private List<NVector3> _spawns;
        private List<StagePlatform> _platforms;
        private List<string> _errors;

        private void Open(string stage)
        {
            string[] found = AssetDatabase.FindAssets($"{stage} t:Scene");
            Assert.That(found.Length, Is.EqualTo(1), $"{stage} のシーンがない");
            _scene = EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(found[0]), OpenSceneMode.Additive);
            Physics.SyncTransforms();
            _errors = new List<string>();
            _terrain = SceneTerrainBuilder.Build(1 << 0, _errors);
            StageBounds? bounds = KnockoutBounds.Find(_errors);
            Assert.That(bounds.HasValue, Is.True, "撃破の範囲がない");
            _bounds = bounds.Value;
            _zones = StageZoneAuthoring.Collect(_errors);
            _coins = CoinSpawnAuthoring.Collect(_errors);
            _banks = BankSpotAuthoring.Collect(_errors);
            _spawns = SpawnPointAuthoring.Collect().Select(p => p.ToNumerics()).ToList();
            _platforms = StagePlatformAuthoring.Collect(_errors);
            Assert.That(_errors, Is.Empty, "ステージの読み込みで除いたものがある");
        }

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void Catalog_ListsEveryStage_InOrder()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath);
            Assert.That(catalog.Stages.Select(s => s.SceneName).ToArray(), Is.EqualTo(Stages), "ステージの一覧とこのテストの一覧が違う");
        }

        [TestCaseSource(nameof(Stages))]
        public void Spawns_AreInsideTheStage_StandingOnTheGround_AwayFromMovingGimmicks(string stage)
        {
            Open(stage);
            Assert.That(_spawns.Count, Is.EqualTo(4), "出現位置が 4 人分ない");
            foreach (NVector3 spawn in _spawns)
            {
                Assert.That(_bounds.Contains(spawn), Is.True, $"出現位置 {spawn} がステージの外");
                Assert.That(_terrain.IsOverlapping(spawn, CharacterHalf), Is.False, $"出現位置 {spawn} が地形に埋まっている");
                Assert.That(_terrain.Raycast(spawn, -NVector3.UnitY, CharacterHalf.Y + 0.5f, out _), Is.True, $"出現位置 {spawn} の下に地面がない");
                foreach (StageZone zone in _zones)
                {
                    bool moves = zone.Kind == StageZoneKind.JumpPad || zone.Kind == StageZoneKind.Launcher || zone.Kind == StageZoneKind.SpeedLane
                        || zone.Kind == StageZoneKind.Bumper || zone.Kind == StageZoneKind.Warp;
                    Assert.That(moves && zone.Overlaps(spawn, new NVector2(CharacterHalf.X, CharacterHalf.Y + 0.2f)), Is.False,
                        $"出現位置 {spawn} が {zone} の上（始まってすぐ動いてしまう）");
                }
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void CoinsJackpotsAndBanks_AreInsideTheStage_AndNoCoinIsInsideABank(string stage)
        {
            Open(stage);
            Assert.That(_coins.Count(c => c.Kind == CoinSpawnKind.Regular), Is.GreaterThanOrEqualTo(20), "コインの出現地点が少ない");
            Assert.That(_coins.Count(c => c.Kind == CoinSpawnKind.Jackpot), Is.GreaterThanOrEqualTo(3), "100 枚のコインの候補が少ない");
            Assert.That(_banks.Count, Is.GreaterThanOrEqualTo(5), "拠点の候補が少ない");
            foreach (CoinSpawnPoint coin in _coins)
            {
                var position = new NVector3(coin.Position.X, coin.Position.Y, 0f);
                Assert.That(_bounds.Contains(position), Is.True, $"コイン {coin.Position} がステージの外");
                Assert.That(_terrain.IsOverlapping(position, CoinHalf), Is.False, $"コイン {coin.Position} が地形に埋まっている");
            }
            float radius = new Airerage.Core.Match.MatchRules().BankRadius;
            foreach (BankSpot bank in _banks)
            {
                Assert.That(_bounds.Contains(new NVector3(bank.Position.X, bank.Position.Y, 0f)), Is.True, $"拠点 {bank.Position} がステージの外");
                foreach (CoinSpawnPoint coin in _coins)
                {
                    Assert.That(NVector2.Distance(coin.Position, bank.Position), Is.GreaterThan(radius),
                        $"コイン {coin.Position} が拠点 {bank.Position} の中（拾うとすぐ確定してしまう）");
                }
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void FloorGimmicks_SitOnTheFloor_AndWarpExitsAreOpen(string stage)
        {
            Open(stage);
            foreach (StageZone zone in _zones)
            {
                var center = new NVector3((zone.MinX + zone.MaxX) * 0.5f, zone.MinY, 0f);
                // 周期のある発射台（壁の間欠泉）は床の上に置かない。
                if (zone.Kind == StageZoneKind.JumpPad || zone.Kind == StageZoneKind.SpeedLane || (zone.Kind == StageZoneKind.Launcher && zone.Period == 0)
                    || zone.Kind == StageZoneKind.Ice)
                {
                    Assert.That(_terrain.Raycast(center + new NVector3(0f, 0.05f, 0f), -NVector3.UnitY, 0.1f, out _), Is.True,
                        $"{zone} の下に床がない");
                    Assert.That(_terrain.IsOverlapping(new NVector3(center.X, zone.MinY + 0.1f, 0f), new NVector3((zone.MaxX - zone.MinX) * 0.5f - 0.01f, 0.05f, 0.1f)),
                        Is.False, $"{zone} が地形に埋まっている");
                }
                if (zone.Kind == StageZoneKind.Warp)
                {
                    var exit = new NVector3(zone.Target.X, zone.Target.Y, 0f);
                    Assert.That(_bounds.Contains(exit), Is.True, $"ワープゲートの出口 {zone.Target} がステージの外");
                    Assert.That(_terrain.IsOverlapping(exit, CharacterHalf), Is.False, $"ワープゲートの出口 {zone.Target} が地形に埋まっている");
                }
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void Players_StandStill_AtTheSpawns_ForThreeSeconds(string stage)
        {
            Open(stage);
            SimWorld world = World(_spawns.ToArray());
            Step(world, 180, default);
            for (int i = 0; i < world.CharacterCount; i++)
            {
                CharacterState state = world.GetState(i);
                Assert.That(state.Combat.Falls, Is.EqualTo(0), $"{i + 1} 人目が出現位置から落ちた");
                Assert.That(state.Grounded, Is.True, $"{i + 1} 人目が出現位置に立っていない（{state.Position}）");
                Assert.That(state.Velocity.Length(), Is.LessThan(0.5f), $"{i + 1} 人目が出現位置で動き続けている");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void PadsLaunchersAndBumpers_SendThePlayerFlying(string stage)
        {
            Open(stage);
            foreach (StageZone zone in _zones.Where(z => z.Kind == StageZoneKind.JumpPad || (z.Kind == StageZoneKind.Launcher && z.Period == 0)
                || z.Kind == StageZoneKind.Bumper))
            {
                NVector3 start = zone.Kind == StageZoneKind.Bumper
                    ? new NVector3(zone.Center.X, zone.Center.Y, 0f)
                    : new NVector3(zone.Center.X, zone.MinY + CharacterHalf.Y + 0.01f, 0f);
                SimWorld world = World(start);
                Step(world, 1, default);
                Assert.That(world.GetState(0).JumpPadCount, Is.EqualTo(1), $"{zone} で飛ばない");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void EveryLauncher_LandsThePlayerOnTheStage(string stage)
        {
            Open(stage);
            foreach (StageZone zone in _zones.Where(z => z.Kind == StageZoneKind.Launcher && z.Period == 0))
            {
                // 飛ぶ向きへ入れ続ける（プレイヤーがふつうにすること）。床に降りるか、床の上のジャンプ台に乗ったら（続けて飛ぶ）降りたとみなす。
                // 危険（溶岩流など）は時刻とランダムな場所で当たるかが変わるので除き、飛ぶ道だけを見る。
                SimWorld world = World(_zones.Where(z => z.Kind != StageZoneKind.Hazard).ToList(),
                    new NVector3(zone.Center.X, zone.MinY + CharacterHalf.Y + 0.01f, 0f));
                PlayerInput input = PlayerInput.FromAxes(zone.Direction.X > 0f ? 1f : -1f, 0f, InputButtons.None);
                Step(world, 1, input);
                Assert.That(world.GetState(0).Grounded, Is.False, $"前提: {zone} で飛んだ");
                var path = new System.Text.StringBuilder();
                for (int i = 0; i < 240 && !Landed(world.GetState(0)); i++)
                {
                    Step(world, 1, input);
                    if (i % 10 == 0)
                    {
                        NVector3 p = world.GetState(0).Position;
                        path.Append($" ({p.X:F1}, {p.Y:F1})");
                    }
                }
                CharacterState state = world.GetState(0);
                Assert.That(state.Combat.Falls, Is.EqualTo(0), $"{zone} で飛ぶと撃破される:{path}");
                Assert.That(Landed(state), Is.True, $"{zone} で飛んだ先に降りる所がない:{path}");
                Assert.That(NVector3.Distance(state.Position, new NVector3(zone.Center.X, zone.Center.Y, 0f)), Is.GreaterThan(6f),
                    $"{zone} で飛んでも、ほとんど同じ所に戻る（{state.Position}）");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void PeriodicLaunchers_FireOnlyWhileActive(string stage)
        {
            Open(stage);
            foreach (StageZone zone in _zones.Where(z => z.Kind == StageZoneKind.Launcher && z.Period > 0))
            {
                int active = Enumerable.Range(0, zone.Period * 2).First(t => zone.IsActive(t));
                int quiet = Enumerable.Range(0, zone.Period * 2).First(t => !zone.IsActive(t));
                Assert.That(Fire(zone, active).JumpPadCount, Is.EqualTo(1), $"{zone} が噴いている間に飛ばない");
                Assert.That(Fire(zone, quiet).JumpPadCount, Is.EqualTo(0), $"{zone} が止んでいる間に飛ぶ");
                Assert.That(zone.Timing.WarningTicks, Is.GreaterThan(0), $"{zone} に予告がない");
            }
        }

        // 範囲の真ん中にいる人に、ティック tick の発射台を効かせる。
        private static CharacterState Fire(StageZone zone, int tick)
        {
            var state = new CharacterState { Position = new NVector3(zone.Center.X, zone.Center.Y, 0f) };
            StageZones.Apply(ref state, state.Position, new CharacterConfig(), new[] { zone }, tick);
            return state;
        }

        [TestCaseSource(nameof(Stages))]
        public void HazardsAndBlizzards_WarnBeforeTheyStrike(string stage)
        {
            Open(stage);
            foreach (StageZone zone in _zones.Where(z => z.Kind == StageZoneKind.Hazard || z.Kind == StageZoneKind.Blizzard))
            {
                Assert.That(zone.Timing.WarningTicks, Is.GreaterThanOrEqualTo(zone.Kind == StageZoneKind.Blizzard ? 5 * 60 : 60),
                    $"{zone} の予告が短い");
                Assert.That(_bounds.Contains(new NVector3(zone.Center.X, zone.Center.Y, 0f)), Is.True, $"{zone} がステージの外");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void SideKnockoutLines_AreWellOutsideTheTerrain_AndTheCoins(string stage)
        {
            Open(stage);
            // 左右の撃破の線は、いちばん外の地形から 10m 以上、コインから 6m 以上離す（ユーザーの要望、2026-10-19）。
            // 陸上競技場のトラックは、撃破の線の外まで地続き（下へ落ちず、左右へ押し出して撃破する。ユーザーの案、2026-10-26）。
            var boxes = Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)
                .Where(b => !(stage == "Stage_Stadium" && b.name == "Track")).ToArray();
            float left = boxes.Min(b => b.bounds.min.x);
            float right = boxes.Max(b => b.bounds.max.x);
            Assert.That(left - _bounds.MinX, Is.GreaterThanOrEqualTo(10f), "左の撃破の線が地形に近い");
            Assert.That(_bounds.MaxX - right, Is.GreaterThanOrEqualTo(10f), "右の撃破の線が地形に近い");
            foreach (CoinSpawnPoint coin in _coins)
            {
                Assert.That(coin.Position.X - _bounds.MinX, Is.GreaterThanOrEqualTo(6f), $"コイン {coin.Position} が左の撃破の線に近い");
                Assert.That(_bounds.MaxX - coin.Position.X, Is.GreaterThanOrEqualTo(6f), $"コイン {coin.Position} が右の撃破の線に近い");
            }
        }

        [Test]
        public void Volcano_LavaFlowsReachTheObsidianTier_AndGeysersEruptTogether()
        {
            Open("Stage_Volcano");
            // 溶岩流は 25 秒ごとに 3 秒、幅 6m で黒曜石の段（y = 13）まで噴く（ユーザーの要望、2026-10-20）。
            StageZone[] lava = _zones.Where(z => z.Kind == StageZoneKind.Hazard).ToArray();
            Assert.That(lava.Length, Is.EqualTo(2), "溶岩流が左右にない");
            foreach (StageZone zone in lava)
            {
                Assert.That(zone.MaxY, Is.GreaterThanOrEqualTo(13f), $"{zone} が黒曜石の段まで届かない");
                Assert.That(zone.Width, Is.EqualTo(6f), $"{zone} の太さが違う");
                Assert.That(zone.Period, Is.EqualTo(25 * SimTime.TicksPerSecond), $"{zone} の間隔が違う");
                Assert.That(zone.Timing.ActiveTicks, Is.EqualTo(3 * SimTime.TicksPerSecond), $"{zone} の長さが違う");
            }
            // 壁の間欠泉は 4 つが一斉に、20 秒ごとに 3.6 秒噴く。
            StageZone[] geysers = _zones.Where(z => z.Kind == StageZoneKind.Launcher && z.Period > 0).ToArray();
            Assert.That(geysers.Length, Is.EqualTo(4), "壁の間欠泉が 4 つない");
            Assert.That(geysers.All(g => g.Period == 20 * SimTime.TicksPerSecond && g.Timing.ActiveTicks == 216), Is.True, "間欠泉の間隔か長さが違う");
            for (int tick = 0; tick < geysers[0].Period * 3; tick += 7)
            {
                Assert.That(geysers.Select(g => g.IsActive(tick, 1234)).Distinct().Count(), Is.EqualTo(1), $"ティック {tick} で一斉に噴いていない");
            }
        }

        [TestCaseSource(nameof(Stages))]
        public void CoinTotals_AreDoubled_AndCoinsDoNotStack(string stage)
        {
            Open(stage);
            CoinSpawnPoint[] regular = _coins.Where(c => c.Kind == CoinSpawnKind.Regular).ToArray();
            Assert.That(regular.Sum(c => c.Value), Is.GreaterThanOrEqualTo(MinimumCoinTotals[stage]), "コインの総量が 2 倍になっていない");
            for (int i = 0; i < regular.Length; i++)
            {
                for (int j = i + 1; j < regular.Length; j++)
                {
                    Assert.That(NVector2.Distance(regular[i].Position, regular[j].Position), Is.GreaterThanOrEqualTo(1.4f),
                        $"コイン {regular[i].Position} と {regular[j].Position} が重なっている");
                }
            }
        }

        [Test]
        public void Hideouts_HaveMoreCoins()
        {
            // 隠れ家にしやすい所（雪原の下の洞窟、ビル街の真ん中のビルの下）には、20 枚 2 つを含めて多めに置く（ユーザーの要望、2026-10-21）。
            (string Stage, Rect Area)[] hideouts =
            {
                ("Stage_SnowSea", Rect.MinMaxRect(-46.5f, -3f, -24f, 2f)),
                ("Stage_CityDay", Rect.MinMaxRect(-9f, -10f, 9f, -4f)),
                ("Stage_CityNight", Rect.MinMaxRect(-9f, -10f, 9f, -4f)),
            };
            foreach (var hideout in hideouts)
            {
                Open(hideout.Stage);
                CoinSpawnPoint[] inside = _coins.Where(c => c.Kind == CoinSpawnKind.Regular
                    && hideout.Area.Contains(new Vector2(c.Position.X, c.Position.Y))).ToArray();
                Assert.That(inside.Count(c => c.Value >= 20), Is.GreaterThanOrEqualTo(2), $"{hideout.Stage} の隠れ家の 20 枚が少ない");
                Assert.That(inside.Sum(c => c.Value), Is.GreaterThanOrEqualTo(50), $"{hideout.Stage} の隠れ家のコインが少ない");
                EditorSceneManager.CloseScene(_scene, true);
                _scene = default;
            }
        }

        [Test]
        public void SnowSea_ShipBow_IsAFloor()
        {
            Open("Stage_SnowSea");
            // 船の中の床の左の端（船首）にも立てる（見た目だけで判定がなかった。ユーザーの指摘、2026-10-21）。
            Assert.That(_terrain.IsOverlapping(new NVector3(36f, -1.6f, 0f), new NVector3(0.4f, 0.05f, 0.4f)), Is.True, "船首に足場の判定がない");
            SimWorld world = World(new NVector3(36f, -1.5f + CharacterHalf.Y + 0.01f, 0f));
            Step(world, 60, default);
            Assert.That(world.GetState(0).Grounded, Is.True, "船首に立てない");
            Assert.That(world.GetState(0).Position.Y, Is.GreaterThan(-1.5f), "船首から落ちた");
        }

        [TestCaseSource(nameof(Stages))]
        public void Decor_DoesNotShareAVisibleFaceWithTheTerrain(string stage)
        {
            Open(stage);
            // 地形の箱と同じ向きの面が同じ位置にある飾りの箱は、カメラが動くとちらつく（z-fighting。雪原の船の白い部分。2026-10-21）。
            Bounds[] terrain = Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Select(b => b.bounds).ToArray();
            var problems = new List<string>();
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (renderer.GetComponent<Collider>() != null || filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "Cube"
                    || renderer.transform.rotation != Quaternion.identity || !renderer.enabled)
                {
                    continue;
                }
                Bounds decor = renderer.bounds;
                foreach (Bounds box in terrain)
                {
                    if (SharesFace(decor, box))
                    {
                        problems.Add($"{renderer.name} {decor.center}");
                        break;
                    }
                }
            }
            Assert.That(problems, Is.Empty, "地形と同じ位置に同じ向きの面がある飾り（ちらつく）");
        }

        // a と b に、同じ向きで同じ位置の面があり、その面が見える大きさで重なっているか（数 cm の細い重なりは見えないので数えない）。
        // カメラは手前（-Z の側）にいるので、奥向き（+Z）の面は見えない。
        private static bool SharesFace(Bounds a, Bounds b)
        {
            const float tolerance = 0.002f;
            const float visibleOverlap = 0.05f;
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;
                float overlapU = Mathf.Min(a.max[u], b.max[u]) - Mathf.Max(a.min[u], b.min[u]);
                float overlapV = Mathf.Min(a.max[v], b.max[v]) - Mathf.Max(a.min[v], b.min[v]);
                if (overlapU <= visibleOverlap || overlapV <= visibleOverlap)
                {
                    continue;
                }
                bool sameMax = axis != 2 && Mathf.Abs(a.max[axis] - b.max[axis]) < tolerance;
                if (sameMax || Mathf.Abs(a.min[axis] - b.min[axis]) < tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void SnowSea_Blizzard_HasShelters_InTheCaveAndTheShip()
        {
            Open("Stage_SnowSea");
            StageZone blizzard = _zones.Single(z => z.Kind == StageZoneKind.Blizzard);
            bool Sheltered(float x, float y) => StageZones.Sheltered(new NVector3(x, y, 0f), blizzard.Direction, _terrain, blizzard.Limit);
            Assert.That(Sheltered(-36f, -2.1f), Is.True, "洞窟の中が吹雪の影にならない");
            Assert.That(Sheltered(-26f, -2.1f), Is.True, "洞窟の入り口が吹雪の影にならない");
            Assert.That(Sheltered(45f, -0.6f), Is.True, "船の中が吹雪の影にならない");
            Assert.That(Sheltered(0f, 1.9f), Is.False, "真ん中の氷の上が吹雪の影になる（隠れなくてよくなる）");
            Assert.That(Sheltered(-43f, 12.9f), Is.False, "尾根の上が吹雪の影になる");
            // 洞窟の中の拠点の候補と、船の中を通り抜けられる。
            Assert.That(_banks.Any(b => b.Position.X < -24f && b.Position.Y < 2f), Is.True, "洞窟の中に拠点の候補がない");
            Assert.That(_terrain.IsOverlapping(new NVector3(42f, 0.2f, 0f), CharacterHalf), Is.False, "船の中を通れない");
            Assert.That(_terrain.IsOverlapping(new NVector3(-40f, -2f, 0f), CharacterHalf), Is.False, "洞窟の中を通れない");
            Assert.That(_terrain.IsOverlapping(new NVector3(-47.4f, -1f, 0f), new NVector3(0.2f, 0.5f, 0.2f)), Is.True, "洞窟が左の端を貫いている");
        }

        [Test]
        public void Cities_HaveTheHollowWithABank_UnderTheCenterBuilding()
        {
            foreach (string stage in new[] { "Stage_CityDay", "Stage_CityNight" })
            {
                Open(stage);
                Assert.That(_terrain.IsOverlapping(new NVector3(0f, -9.05f, 0f), CharacterHalf), Is.False, $"{stage}: 真ん中のビルの中に立てない");
                Assert.That(_terrain.Raycast(new NVector3(0f, -9.05f, 0f), -NVector3.UnitY, 1f, out _), Is.True, $"{stage}: くりぬいた中に床がない");
                Assert.That(_terrain.IsOverlapping(new NVector3(-9.5f, -8f, 0f), CharacterHalf), Is.False, $"{stage}: くりぬいた中から左の谷へ出られない");
                Assert.That(_terrain.IsOverlapping(new NVector3(9.5f, -8f, 0f), CharacterHalf), Is.False, $"{stage}: くりぬいた中から右の谷へ出られない");
                Assert.That(_banks.Any(b => NVector2.Distance(b.Position, new NVector2(0f, -7.5f)) < 0.5f), Is.True, $"{stage}: くりぬいた中に拠点の候補がない");
                EditorSceneManager.CloseScene(_scene, true);
                _scene = default;
            }
        }

        // 床に立っているか、床の上のジャンプ台に乗った。
        private bool Landed(CharacterState state)
        {
            var half = new NVector2(CharacterHalf.X, CharacterHalf.Y);
            return state.Grounded || _zones.Any(z => z.Kind == StageZoneKind.JumpPad && z.Overlaps(state.Position, half));
        }

        [TestCaseSource(nameof(NewStages))]
        public void NewStage_HasItsOwnGimmicks_AndTheCommonOnes(string stage)
        {
            Open(stage);
            foreach (StageZoneKind kind in UniqueGimmicks[stage])
            {
                Assert.That(_zones.Any(z => z.Kind == kind), Is.True, $"{stage} に固有のギミック {kind} がない");
            }
            Assert.That(_zones.Any(z => z.Kind == StageZoneKind.JumpPad), Is.True, "ジャンプ台がない");
            Assert.That(_zones.Any(z => z.Kind == StageZoneKind.BoostRing), Is.True, "加速リングがない");
            Assert.That(Object.FindObjectsByType<Airerage.Runtime.Presentation.StageZoneView>(FindObjectsSortMode.None).Length, Is.EqualTo(_zones.Count),
                "見た目のないギミックがある");
        }

        private static IEnumerable<string> NewStages()
        {
            return UniqueGimmicks.Keys;
        }

        [Test]
        public void SnowSeaAndWharf_HaveASeaToFloatIn_DownToTheKnockoutLine()
        {
            // 海の判定（ユーザーの要望、2026-10-26）: 落ちた海で浮けて、底が撃破の線。
            foreach (string stage in new[] { "Stage_SnowSea", "Stage_Wharf" })
            {
                Open(stage);
                StageZone sea = _zones.Single(z => z.Kind == StageZoneKind.Water);
                Assert.That(sea.MinY, Is.EqualTo(_bounds.MinY).Within(1e-3f), $"{stage} の海の底が撃破の線でない");
                Assert.That(sea.MinX, Is.LessThanOrEqualTo(_bounds.MinX + 1f), $"{stage} の海が左の端まで続かない");
                Assert.That(sea.MaxX, Is.GreaterThanOrEqualTo(_bounds.MaxX - 1f), $"{stage} の海が右の端まで続かない");
                // 海の上から落ちると、浮いている（すぐには撃破されない）。
                SimWorld world = World(new NVector3(_bounds.MaxX - 4f, sea.MaxY + 3f, 0f));
                Step(world, 150, default);
                Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(0), $"{stage} の海に落ちてすぐ撃破された");
                Assert.That(world.GetState(0).Combat.WaterTicksLeft, Is.GreaterThan(0), $"{stage} の海に浮かない");
                EditorSceneManager.CloseScene(_scene, true);
                _scene = default;
            }
        }

        [Test]
        public void Lab_Doors_OpenWithAWire_AndLeadIntoTheVaults()
        {
            Open("Stage_Lab");
            StagePlatform[] doors = _platforms.Where(p => p.IsDoor).ToArray();
            Assert.That(doors.Length, Is.EqualTo(2), "実験室の扉が 2 つない");
            // 扉の外から、扉にワイヤーをつなぐと開く。
            StagePlatform door = doors.OrderBy(d => d.Origin.X).First();
            var start = new NVector3(door.Origin.X + 4f, CharacterHalf.Y + 0.01f, 0f);
            SimWorld world = World(start);
            var aim = NVector2.Normalize(new NVector2(-4f, door.Origin.Y - start.Y + 0.5f));
            for (int t = 0; t < 60; t++)
            {
                Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(aim));
            }
            int index = world.Platforms.ToList().FindIndex(p => p.SameAs(door));
            Assert.That(world.Stage.OpenOf(index), Is.EqualTo(StagePlatform.DoorScale), "ワイヤーをつないでも扉が開ききらない");
            // 扉の中（実験室）に、20 枚のコインと拠点の候補がある。
            Assert.That(_coins.Count(c => c.Value == 20 && c.Position.X < door.Origin.X && c.Position.X > door.Origin.X - 16f && c.Position.Y < 7f),
                Is.GreaterThanOrEqualTo(2), "実験室の中に 20 枚のコインが少ない");
            Assert.That(_banks.Any(b => b.Position.X < door.Origin.X && b.Position.X > door.Origin.X - 16f && b.Position.Y < 7f), Is.True,
                "実験室の中に拠点の候補がない");
        }

        [Test]
        public void Kitchen_RollCakes_CrossTheStage_FromOutsideToOutside()
        {
            Open("Stage_Kitchen");
            StagePlatform[] cakes = _platforms.Where(p => !p.IsDoor).ToArray();
            Assert.That(cakes.Length, Is.EqualTo(2), "ロールケーキが 2 つない");
            foreach (StagePlatform cake in cakes)
            {
                Assert.That(cake.Attachable, Is.True, "ロールケーキにワイヤーがつながらない");
                Assert.That(cake.TryGetCenter(0, 0, out NVector2 from), Is.True);
                Assert.That(cake.TryGetCenter(cake.ActiveTo - 1, 0, out NVector2 to), Is.True);
                Assert.That(System.MathF.Abs(from.X), Is.GreaterThan(_bounds.MaxX), "ロールケーキがステージの外から出ない");
                Assert.That(System.MathF.Abs(to.X), Is.GreaterThan(_bounds.MaxX), "ロールケーキがステージの外へ去らない");
                Assert.That(System.MathF.Sign(from.X), Is.Not.EqualTo(System.MathF.Sign(to.X)), "ロールケーキが反対の外へ去らない");
                Assert.That(cake.TryGetCenter(cake.ActiveTo + 10, 0, out _), Is.False, "去った後もロールケーキがある");
            }
            // 2 つは反対の側から同時に出る（交差する）。
            cakes[0].TryGetCenter(0, 0, out NVector2 a);
            cakes[1].TryGetCenter(0, 0, out NVector2 b);
            Assert.That(System.MathF.Sign(a.X), Is.Not.EqualTo(System.MathF.Sign(b.X)), "ロールケーキが左右から 1 つずつ出ない");
            // オーブンの火柱は 40 秒ごと。
            StageZone fire = _zones.Single(z => z.Kind == StageZoneKind.Hazard);
            Assert.That(fire.Period, Is.EqualTo(40 * Airerage.Core.Common.SimTime.TicksPerSecond), "火柱が 40 秒ごとでない");
        }

        [Test]
        public void Wharf_Container_IsHollow_AndMovesEveryTwentySeconds()
        {
            Open("Stage_Wharf");
            StagePlatform[] plates = _platforms.Where(p => p.Period == 60 * Airerage.Core.Common.SimTime.TicksPerSecond).ToArray();
            Assert.That(plates.Length, Is.EqualTo(2), "コンテナの屋根と床がない");
            plates[0].TryGetCenter(0, 0, out NVector2 roof);
            plates[1].TryGetCenter(0, 0, out NVector2 floor);
            float inside = System.MathF.Abs(roof.Y - floor.Y) - plates[0].HalfSize.Y - plates[1].HalfSize.Y;
            Assert.That(inside, Is.GreaterThan(CharacterHalf.Y * 2f + 0.2f), "コンテナの中に入れない");
            // 20 秒ごとに別の場所へ運ばれる。
            int ticks = 20 * Airerage.Core.Common.SimTime.TicksPerSecond;
            plates[1].TryGetCenter(ticks - 1, 0, out NVector2 first);
            plates[1].TryGetCenter(ticks * 2 - 1, 0, out NVector2 second);
            plates[1].TryGetCenter(ticks * 3 - 1, 0, out NVector2 third);
            Assert.That(NVector2.Distance(first, second), Is.GreaterThan(5f), "20 秒で運ばれない");
            Assert.That(NVector2.Distance(second, third), Is.GreaterThan(5f), "20 秒で運ばれない");
            // ボラードがアンカーポイント（岸壁の上の低い所）。
            Assert.That(_terrain.AnchorPoints.Count(p => p.Y < 3f), Is.GreaterThanOrEqualTo(4), "ボラードのアンカーポイントが少ない");
        }

        [Test]
        public void Stadium_HasNoBottomToFallFrom_OnlyTheSidesAndTheTop()
        {
            Open("Stage_Stadium");
            // トラックは左右の撃破の線の外まで続き、下の撃破の線には落ちない（ユーザーの案）。
            Assert.That(_terrain.Raycast(new NVector3(_bounds.MinX + 0.5f, 2f, 0f), -NVector3.UnitY, 5f, out _), Is.True, "左の撃破の線の手前に床がない");
            Assert.That(_terrain.Raycast(new NVector3(_bounds.MaxX - 0.5f, 2f, 0f), -NVector3.UnitY, 5f, out _), Is.True, "右の撃破の線の手前に床がない");
            // トラックを歩いて右の撃破の線を越えると撃破される。
            SimWorld world = World(new List<StageZone>(), new NVector3(_bounds.MaxX - 3f, CharacterHalf.Y + 0.01f, 0f));
            Step(world, 90, PlayerInput.FromAxes(1f, 0f, InputButtons.None));
            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "右の撃破の線を越えても撃破されない");
        }

        private SimWorld World(params NVector3[] positions)
        {
            return World(_zones, positions);
        }

        private SimWorld World(IReadOnlyList<StageZone> zones, params NVector3[] positions)
        {
            var world = new SimWorld(_terrain, _bounds, zones, null, null, null, _platforms);
            foreach (NVector3 position in positions)
            {
                world.AddCharacter(new CharacterConfig(), position, 1);
            }
            return world;
        }

        private static void Step(SimWorld world, int ticks, PlayerInput input)
        {
            var inputs = Enumerable.Repeat(input, world.CharacterCount).ToArray();
            for (int i = 0; i < ticks; i++)
            {
                world.Step(inputs);
            }
        }
    }
}
