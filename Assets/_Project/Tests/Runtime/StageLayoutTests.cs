using System.Collections.Generic;
using System.Linq;
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
        };

        // ステージ固有のギミック（どのステージにもあるジャンプ台・上昇気流・加速リング以外）。
        private static readonly Dictionary<string, StageZoneKind[]> UniqueGimmicks = new Dictionary<string, StageZoneKind[]>
        {
            { "Stage_CityDay", new[] { StageZoneKind.SpeedLane, StageZoneKind.Launcher, StageZoneKind.Gust } },
            { "Stage_CityNight", new[] { StageZoneKind.Bumper, StageZoneKind.Gust } },
            { "Stage_Volcano", new[] { StageZoneKind.Hazard, StageZoneKind.Launcher, StageZoneKind.Bumper } },
            { "Stage_SnowSea", new[] { StageZoneKind.Ice, StageZoneKind.SpeedLane, StageZoneKind.Launcher, StageZoneKind.Hazard, StageZoneKind.Blizzard } },
            { "Stage_SpaceStation", new[] { StageZoneKind.LowGravity, StageZoneKind.Warp, StageZoneKind.Launcher } },
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
                SimWorld world = World(new NVector3(zone.Center.X, zone.MinY + CharacterHalf.Y + 0.01f, 0f));
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
            var boxes = Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None);
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

        private SimWorld World(params NVector3[] positions)
        {
            var world = new SimWorld(_terrain, _bounds, _zones);
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
