using System.Collections.Generic;
using Degunatsu.Core.Physics;
using Degunatsu.Runtime.DebugTools;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NVector3 = System.Numerics.Vector3;

namespace Degunatsu.Runtime.Tests
{
    /// <summary>
    /// 生成した検証用シーン（Sandbox_Wire）の配置を検査する。地形の配置を変えたときに、置き方の誤りを見つけるため。
    /// </summary>
    public sealed class SandboxWireSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Sandbox_Wire.unity";

        // キャラクターの箱の半サイズ（DefaultCharacter の仮の値と同じ）。
        private static readonly NVector3 CharacterHalf = new NVector3(0.4f, 0.9f, 0.4f);

        private Scene _scene;
        private BoxTerrain _terrain;
        private List<string> _errors;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Physics.SyncTransforms();
            _errors = new List<string>();
            _terrain = SceneTerrainBuilder.Build(1 << 0, _errors);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void Terrain_HasOnlySupportedColliders()
        {
            Assert.That(_errors, Is.Empty);
            Assert.That(_terrain.Boxes.Count, Is.GreaterThan(10));
        }

        [Test]
        public void SpawnPoint_IsNotInsideTerrain()
        {
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var spawn = (Transform)new SerializedObject(runner).FindProperty("_spawnPoint").objectReferenceValue;
            Assert.That(spawn, Is.Not.Null);
            Assert.That(_terrain.IsOverlapping(spawn.position.ToNumerics(), CharacterHalf), Is.False,
                $"出現位置 {spawn.position} が地形に埋まっている");
        }

        [Test]
        public void CourseZones_OneStartAndNamedGoals_NotInsideTerrain()
        {
            int starts = 0;
            var goals = new HashSet<string>();
            foreach (CourseZone zone in Object.FindObjectsByType<CourseZone>(FindObjectsSortMode.None))
            {
                if (zone.Kind == CourseZone.ZoneKind.Start)
                {
                    starts++;
                }
                else
                {
                    Assert.That(zone.Course, Is.Not.Empty, $"{zone.name}: ゴールにコース名がない");
                    Assert.That(goals.Add(zone.Course), Is.True, $"コース名 {zone.Course} が重複している");
                }

                Rect area = zone.Area;
                var center = new NVector3(area.center.x, area.center.y, 0f);
                var half = new NVector3(area.width * 0.5f, area.height * 0.5f, 0f);
                Assert.That(_terrain.IsOverlapping(center, half), Is.False, $"{zone.name} の範囲 {area} が地形に重なっている");
            }
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(goals.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void AnchorPoints_AreNotInsideTerrain()
        {
            Assert.That(_terrain.AnchorPoints.Count, Is.GreaterThan(0));
            foreach (NVector3 anchor in _terrain.AnchorPoints)
            {
                Assert.That(_terrain.IsOverlapping(anchor, new NVector3(0.01f, 0.01f, 0f)), Is.False,
                    $"アンカーポイント {anchor} が地形に埋まっている");
            }
        }
    }
}
