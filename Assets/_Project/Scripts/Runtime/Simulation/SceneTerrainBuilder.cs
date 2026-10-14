using System.Collections.Generic;
using System.Text;
using Airerage.Core.Physics;
using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// シーンの地形から Core の BoxTerrain を作る（docs/03_architecture.md 4.2）。
    /// 地形は terrainMask の層にある BoxCollider で、回転させず、Z = 0 の面を横切るように置く。
    /// 条件に合わないコライダーは除き、理由を errors に追加する（黙って無視しない）。
    /// </summary>
    public static class SceneTerrainBuilder
    {
        // これ以下の回転（度）は回転していないとみなす。
        private const float RotationToleranceDeg = 0.01f;

        public static BoxTerrain Build(LayerMask terrainMask, ICollection<string> errors)
        {
            var boxes = new List<(Bounds bounds, bool attachable)>();
            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Collider collider in colliders)
            {
                if (!collider.enabled || collider.isTrigger || (terrainMask.value & (1 << collider.gameObject.layer)) == 0)
                {
                    continue;
                }
                if (TryGetTerrainBox(collider, out Bounds bounds, out string error))
                {
                    boxes.Add((bounds, collider.TryGetComponent(out WireAttachable _)));
                }
                else
                {
                    errors.Add($"地形として使えないコライダーを除いた: {PathOf(collider.transform)}（{error}）");
                }
            }

            // 見つかった順はシーンによって変わるので、並べ替えて決定的にする（押し出しの計算順が結果に影響するため）。
            boxes.Sort((a, b) => CompareBounds(a.bounds, b.bounds));

            var terrain = new BoxTerrain();
            foreach ((Bounds bounds, bool attachable) in boxes)
            {
                terrain.AddBox(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y, attachable);
            }

            var anchors = new List<Vector3>();
            foreach (WireAnchorPoint anchor in Object.FindObjectsByType<WireAnchorPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                anchors.Add(anchor.transform.position);
            }
            anchors.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            foreach (Vector3 anchor in anchors)
            {
                terrain.AddAnchor(anchor.x, anchor.y);
            }
            return terrain;
        }

        private static bool TryGetTerrainBox(Collider collider, out Bounds bounds, out string error)
        {
            bounds = default;
            if (!(collider is BoxCollider))
            {
                error = $"{collider.GetType().Name} は使えない。BoxCollider だけを使う";
                return false;
            }
            if (Quaternion.Angle(collider.transform.rotation, Quaternion.identity) > RotationToleranceDeg)
            {
                error = $"回転している（{collider.transform.rotation.eulerAngles}）。地形の箱は回転させない";
                return false;
            }
            bounds = collider.bounds;
            if (!(bounds.size.x > 0f) || !(bounds.size.y > 0f))
            {
                error = $"大きさが 0（{bounds.size}）";
                return false;
            }
            if (!(bounds.min.z < 0f && bounds.max.z > 0f))
            {
                error = $"Z = 0 の面を横切っていない（Z の範囲 {bounds.min.z}〜{bounds.max.z}）";
                return false;
            }
            error = null;
            return true;
        }

        private static int CompareBounds(Bounds a, Bounds b)
        {
            int c = a.min.x.CompareTo(b.min.x);
            if (c == 0)
            {
                c = a.min.y.CompareTo(b.min.y);
            }
            if (c == 0)
            {
                c = a.max.x.CompareTo(b.max.x);
            }
            return c != 0 ? c : a.max.y.CompareTo(b.max.y);
        }

        private static string PathOf(Transform transform)
        {
            var path = new StringBuilder(transform.name);
            for (Transform t = transform.parent; t != null; t = t.parent)
            {
                path.Insert(0, t.name + "/");
            }
            return path.ToString();
        }
    }
}
