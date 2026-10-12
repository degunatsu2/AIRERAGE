using System;
using System.Collections.Generic;
using Degunatsu.Core.Physics;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Tests
{
    /// <summary>
    /// テスト用に、シーンへ地形のコライダーやアンカーポイントを置く。テストの終わりに Dispose で消す。
    /// シーンにある他の物と混ざらないよう、テスト専用の層に置く。
    /// </summary>
    public sealed class ColliderWorld : IDisposable
    {
        /// <summary>テストで地形に使う層（名前のない層）。</summary>
        public const int TerrainLayer = 31;

        /// <summary>地形の奥行き。キャラクターの奥行き（半サイズ 0.4m）より十分大きくする。</summary>
        public const float Depth = 10f;

        private readonly List<GameObject> _objects = new List<GameObject>();

        public static LayerMask TerrainMask => 1 << TerrainLayer;

        /// <summary>BoxTerrain と同じ地形をコライダーで置く。</summary>
        public static ColliderWorld From(BoxTerrain reference)
        {
            var world = new ColliderWorld();
            foreach (BoxTerrain.Box box in reference.Boxes)
            {
                var center = new Vector3((box.Min.X + box.Max.X) * 0.5f, (box.Min.Y + box.Max.Y) * 0.5f, 0f);
                var size = new Vector3(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y, Depth);
                world.AddBox(center, size, box.Attachable);
            }
            foreach (System.Numerics.Vector3 anchor in reference.AnchorPoints)
            {
                world.AddAnchor(new Vector3(anchor.X, anchor.Y, 0f));
            }
            return world;
        }

        public GameObject AddBox(Vector3 center, Vector3 size, bool attachable = false)
        {
            var go = Create("TestTerrain");
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            if (attachable)
            {
                go.AddComponent<WireAttachable>();
            }
            return go;
        }

        public GameObject AddAnchor(Vector3 position)
        {
            var go = Create("TestAnchor");
            go.transform.position = position;
            go.AddComponent<WireAnchorPoint>();
            return go;
        }

        public GameObject Create(string name)
        {
            var go = new GameObject(name) { layer = TerrainLayer };
            _objects.Add(go);
            return go;
        }

        /// <summary>置いたものから BoxTerrain を作る。</summary>
        public BoxTerrain Build(List<string> errors)
        {
            Physics.SyncTransforms();
            return SceneTerrainBuilder.Build(TerrainMask, errors);
        }

        public void Dispose()
        {
            foreach (GameObject go in _objects)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            _objects.Clear();
        }
    }
}
