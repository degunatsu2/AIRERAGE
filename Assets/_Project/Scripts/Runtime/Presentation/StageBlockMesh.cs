using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 面取りした箱の網（D-97 の V1。2026-11-17 のユーザーの決定）: 6 つの面を縁から bevel だけ内側に縮め、12 本の辺を 45° の細い面で、
    /// 8 つの角を三角の面でつなぐ。面ごとに頂点を分けて、面取りの面がはっきり光を受けるようにする（角の明るい線）。外形の大きさは size の
    /// まま（地形の当たりの箱と同じ）。シェーダー（Airerage/StageSurface）に渡す値: uv0 = 中心からの位置（m。x, y）、uv1 = (横の半分の長さ,
    /// 縦の半分の長さ, 明滅の位相, ワイヤーが刺さる足場なら 1)。
    /// </summary>
    public static class StageBlockMesh
    {
        /// <summary>面取りの幅の上限（いちばん短い辺の半分に対する割合）。</summary>
        public const float MaxBevelRatio = 0.45f;

        private static readonly List<Vector3> Vertices = new List<Vector3>(96);
        private static readonly List<Vector3> Normals = new List<Vector3>(96);
        private static readonly List<Vector2> Local = new List<Vector2>(96);
        private static readonly List<Vector4> Block = new List<Vector4>(96);
        private static readonly List<int> Triangles = new List<int>(132);

        /// <summary>size の箱を、幅 bevel で面取りした網を mesh に入れる（前の中身は消す）。</summary>
        public static void Fill(Mesh mesh, Vector3 size, float bevel, float phase, bool attachable)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(bevel, 0f, MaxBevelRatio * Mathf.Min(h.x, Mathf.Min(h.y, h.z)));
            var block = new Vector4(h.x, h.y, phase, attachable ? 1f : 0f);
            Vertices.Clear();
            Normals.Clear();
            Local.Clear();
            Block.Clear();
            Triangles.Clear();

            // 6 つの面（縁から b だけ内側）。
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3;
                int v = (axis + 2) % 3;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 normal = Vector3.zero;
                    normal[axis] = s;
                    var quad = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = s * h[axis];
                        p[u] = (i == 0 || i == 3 ? -1f : 1f) * (h[u] - b);
                        p[v] = (i < 2 ? -1f : 1f) * (h[v] - b);
                        quad[i] = p;
                    }
                    AddPolygon(quad, normal, block);
                }
            }
            if (b > 0f)
            {
                // 12 本の辺（2 つの軸の向きの面の間。残りの軸の向きに伸びる 45° の面）。
                for (int a = 0; a < 3; a++)
                {
                    int c = (a + 1) % 3;
                    int t = (a + 2) % 3;
                    for (int sa = -1; sa <= 1; sa += 2)
                    {
                        for (int sc = -1; sc <= 1; sc += 2)
                        {
                            Vector3 normal = Vector3.zero;
                            normal[a] = sa;
                            normal[c] = sc;
                            Vector3 q1 = Vector3.zero;
                            q1[a] = sa * h[a];
                            q1[c] = sc * (h[c] - b);
                            Vector3 q2 = Vector3.zero;
                            q2[a] = sa * (h[a] - b);
                            q2[c] = sc * h[c];
                            Vector3 low = Vector3.zero;
                            low[t] = -(h[t] - b);
                            Vector3 high = Vector3.zero;
                            high[t] = h[t] - b;
                            AddPolygon(new[] { q1 + low, q1 + high, q2 + high, q2 + low }, normal.normalized, block);
                        }
                    }
                }
                // 8 つの角（3 つの面取りの面の間の三角）。
                for (int corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3((corner & 1) != 0 ? 1f : -1f, (corner & 2) != 0 ? 1f : -1f, (corner & 4) != 0 ? 1f : -1f);
                    var p0 = new Vector3(sign.x * h.x, sign.y * (h.y - b), sign.z * (h.z - b));
                    var p1 = new Vector3(sign.x * (h.x - b), sign.y * h.y, sign.z * (h.z - b));
                    var p2 = new Vector3(sign.x * (h.x - b), sign.y * (h.y - b), sign.z * h.z);
                    AddPolygon(new[] { p0, p1, p2 }, sign.normalized, block);
                }
            }

            mesh.Clear();
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Local);
            mesh.SetUVs(1, Block);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
        }

        // 凸の多角形を扇形の三角形で足す。表の向き（Unity は時計回り）が normal に合うように並べ替える。
        private static void AddPolygon(Vector3[] points, Vector3 normal, Vector4 block)
        {
            int start = Vertices.Count;
            foreach (Vector3 p in points)
            {
                Vertices.Add(p);
                Normals.Add(normal);
                Local.Add(new Vector2(p.x, p.y));
                Block.Add(block);
            }
            bool flip = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f;
            for (int i = 1; i + 1 < points.Length; i++)
            {
                Triangles.Add(start);
                Triangles.Add(start + (flip ? i + 1 : i));
                Triangles.Add(start + (flip ? i : i + 1));
            }
        }
    }
}
