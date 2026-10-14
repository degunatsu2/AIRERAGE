using System.Collections.Generic;
using Airerage.Core.Physics;
using Airerage.Core.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// シーンのコライダーから BoxTerrain を正しく作れることを確かめる。
    /// </summary>
    public sealed class SceneTerrainBuilderTests
    {
        private const float Tolerance = 1e-5f;

        private ColliderWorld _world;
        private List<string> _errors;

        [SetUp]
        public void SetUp()
        {
            _world = new ColliderWorld();
            _errors = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void Build_FromPlaygroundColliders_MatchesReferenceTerrain()
        {
            BoxTerrain reference = TestArenas.Playground();
            _world.Dispose();
            _world = ColliderWorld.From(reference);

            BoxTerrain built = _world.Build(_errors);

            Assert.That(_errors, Is.Empty);
            Assert.That(built.Boxes.Count, Is.EqualTo(reference.Boxes.Count));
            foreach (BoxTerrain.Box expected in reference.Boxes)
            {
                Assert.That(ContainsBox(built, expected), Is.True,
                    $"min={expected.Min} max={expected.Max} attachable={expected.Attachable} の箱がない");
            }
            Assert.That(built.AnchorPoints.Count, Is.EqualTo(reference.AnchorPoints.Count));
            foreach (System.Numerics.Vector3 anchor in reference.AnchorPoints)
            {
                Assert.That(built.AnchorPoints, Has.Some.EqualTo(anchor));
            }
        }

        [Test]
        public void Build_UsesWorldBounds_WithParentScaleAndColliderCenter()
        {
            GameObject parent = _world.Create("Parent");
            parent.transform.position = new Vector3(10f, 2f, 0f);
            parent.transform.localScale = new Vector3(2f, 3f, 1f);
            GameObject child = _world.Create("Child");
            child.transform.SetParent(parent.transform, false);
            var box = child.AddComponent<BoxCollider>();
            box.center = new Vector3(1f, 0f, 0f);
            box.size = new Vector3(1f, 1f, 4f);

            BoxTerrain built = _world.Build(_errors);

            // 中心 = (10 + 1 * 2, 2), 大きさ = (1 * 2, 1 * 3)
            Assert.That(_errors, Is.Empty);
            Assert.That(built.Boxes.Count, Is.EqualTo(1));
            Assert.That(built.Boxes[0].Min.X, Is.EqualTo(11f).Within(Tolerance));
            Assert.That(built.Boxes[0].Max.X, Is.EqualTo(13f).Within(Tolerance));
            Assert.That(built.Boxes[0].Min.Y, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(built.Boxes[0].Max.Y, Is.EqualTo(3.5f).Within(Tolerance));
        }

        [Test]
        public void Build_ReportsAndSkipsRotatedBox()
        {
            GameObject go = _world.AddBox(Vector3.zero, new Vector3(2f, 1f, ColliderWorld.Depth));
            go.transform.rotation = Quaternion.Euler(0f, 0f, 30f);

            BoxTerrain built = _world.Build(_errors);

            Assert.That(built.Boxes, Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(1));
            StringAssert.Contains("回転", _errors[0]);
        }

        [Test]
        public void Build_ReportsAndSkipsNonBoxCollider()
        {
            GameObject go = _world.Create("Sphere");
            go.AddComponent<SphereCollider>();

            BoxTerrain built = _world.Build(_errors);

            Assert.That(built.Boxes, Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(1));
            StringAssert.Contains("SphereCollider", _errors[0]);
        }

        [Test]
        public void Build_ReportsAndSkipsBoxNotCrossingGameplayPlane()
        {
            _world.AddBox(new Vector3(0f, 0f, 5f), new Vector3(2f, 1f, 2f)); // Z の範囲 4〜6

            BoxTerrain built = _world.Build(_errors);

            Assert.That(built.Boxes, Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(1));
            StringAssert.Contains("Z = 0", _errors[0]);
        }

        [Test]
        public void Build_IgnoresTriggersAndOtherLayers()
        {
            GameObject trigger = _world.AddBox(Vector3.zero, new Vector3(2f, 1f, ColliderWorld.Depth));
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            GameObject otherLayer = _world.AddBox(new Vector3(5f, 0f, 0f), new Vector3(2f, 1f, ColliderWorld.Depth));
            otherLayer.layer = 0;

            BoxTerrain built = _world.Build(_errors);

            Assert.That(built.Boxes, Is.Empty);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void Build_OrdersBoxesDeterministically()
        {
            _world.AddBox(new Vector3(5f, 0f, 0f), new Vector3(1f, 1f, ColliderWorld.Depth));
            _world.AddBox(new Vector3(-5f, 0f, 0f), new Vector3(1f, 1f, ColliderWorld.Depth));
            _world.AddBox(new Vector3(0f, 3f, 0f), new Vector3(1f, 1f, ColliderWorld.Depth));

            BoxTerrain built = _world.Build(_errors);

            Assert.That(built.Boxes.Count, Is.EqualTo(3));
            Assert.That(built.Boxes[0].Min.X, Is.LessThan(built.Boxes[1].Min.X));
            Assert.That(built.Boxes[1].Min.X, Is.LessThan(built.Boxes[2].Min.X));
        }

        private static bool ContainsBox(BoxTerrain terrain, BoxTerrain.Box expected)
        {
            foreach (BoxTerrain.Box box in terrain.Boxes)
            {
                if (System.Numerics.Vector2.Distance(box.Min, expected.Min) <= Tolerance
                    && System.Numerics.Vector2.Distance(box.Max, expected.Max) <= Tolerance
                    && box.Attachable == expected.Attachable)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
