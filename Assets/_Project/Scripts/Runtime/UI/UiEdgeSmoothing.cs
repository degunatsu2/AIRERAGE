using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 自前の多角形で描く UI（斜めの札・白い背景・ロゴなど）の、ふちのギザギザを消す（2026-11-16 のユーザーの要望）。
    /// 描いた三角形のうち、ほかの三角形と共有していない辺（図形の外周）の外側に、画面の 1 ピクセル幅で透明へ溶ける帯を足す。
    /// 斜めの辺もピクセルの段にならず、滑らかに見える（アンチエイリアス。描画の設定の MSAA に頼らないので、どの画面でも効く）。
    /// </summary>
    public static class UiEdgeSmoothing
    {
        /// <summary>帯の幅（画面のピクセル）。</summary>
        public const float FringePixels = 1f;

        // 辺の端の点を、この細かさでまとめて同じ点とみなす（ローカルの座標の 1/256）。
        private const float Quantum = 256f;

        // UI の作り直しはメインのスレッドだけで行うので、作業用の入れ物は使い回す（毎回の割り当てを避ける）。
        private static readonly List<UIVertex> Stream = new List<UIVertex>(256);
        private static readonly Dictionary<(long, long), int> Edges = new Dictionary<(long, long), int>(256);

        /// <summary>graphic の描いた形の外周に、画面の 1 ピクセル幅のぼかしの帯を足す。</summary>
        public static void Apply(VertexHelper vh, Graphic graphic)
        {
            float pixel = LocalPixel(graphic);
            if (pixel > 0f)
            {
                Apply(vh, pixel * FringePixels);
            }
        }

        /// <summary>vh の形の外周に、幅 width（ローカルの座標）の、外へ透明になる帯を足す。</summary>
        public static void Apply(VertexHelper vh, float width)
        {
            if (vh.currentIndexCount == 0 || width <= 0f)
            {
                return;
            }
            Stream.Clear();
            Edges.Clear();
            vh.GetUIVertexStream(Stream);
            int count = Stream.Count - Stream.Count % 3;
            for (int t = 0; t < count; t += 3)
            {
                if (Degenerate(Stream[t].position, Stream[t + 1].position, Stream[t + 2].position))
                {
                    continue;
                }
                for (int e = 0; e < 3; e++)
                {
                    (long, long) key = Key(Stream[t + e].position, Stream[t + (e + 1) % 3].position);
                    Edges.TryGetValue(key, out int seen);
                    Edges[key] = seen + 1;
                }
            }
            // 描く順は、三角形ごとに「三角形 → その外周の帯」。帯をまとめて最後に足すと、後ろに隠れるはずの形（タイトルロゴの枠の後ろを
            // 通るワイヤーなど）の帯が手前に出て、細い線に見えた。
            vh.Clear();
            for (int t = 0; t < count; t += 3)
            {
                int start = vh.currentVertCount;
                vh.AddVert(Stream[t]);
                vh.AddVert(Stream[t + 1]);
                vh.AddVert(Stream[t + 2]);
                vh.AddTriangle(start, start + 1, start + 2);
                if (Degenerate(Stream[t].position, Stream[t + 1].position, Stream[t + 2].position))
                {
                    continue;
                }
                for (int e = 0; e < 3; e++)
                {
                    UIVertex a = Stream[t + e];
                    UIVertex b = Stream[t + (e + 1) % 3];
                    if (Edges[Key(a.position, b.position)] != 1)
                    {
                        continue;
                    }
                    Vector3 c = Stream[t + (e + 2) % 3].position;
                    AddFringe(vh, a, b, c, width);
                }
            }
            Stream.Clear();
            Edges.Clear();
        }

        /// <summary>graphic のローカルの座標で、画面の 1 ピクセルの長さ。</summary>
        public static float LocalPixel(Graphic graphic)
        {
            Canvas canvas = graphic.canvas;
            if (canvas == null)
            {
                return 1f;
            }
            Canvas root = canvas.rootCanvas;
            float rootScale = Mathf.Abs(root.transform.lossyScale.x);
            float scale = Mathf.Abs(graphic.transform.lossyScale.x);
            if (rootScale <= 0f || scale <= 0f || root.scaleFactor <= 0f)
            {
                return 0f;
            }
            // ローカルの 1 単位 = (自分の拡大 ÷ キャンバスの拡大) × キャンバスの倍率 ピクセル。
            return 1f / (scale / rootScale * root.scaleFactor);
        }

        // 辺 a-b の、三角形の残りの点 c と反対の側へ、幅 width の帯（内側は元の色、外側は透明）。
        private static void AddFringe(VertexHelper vh, UIVertex a, UIVertex b, Vector3 c, float width)
        {
            Vector2 edge = b.position - a.position;
            float length = edge.magnitude;
            if (length <= 0.0001f)
            {
                return;
            }
            var normal = new Vector2(-edge.y, edge.x) / length;
            if (Vector2.Dot(normal, (Vector2)(c - a.position)) > 0f)
            {
                normal = -normal;
            }
            Vector3 offset = normal * width;
            UIVertex outerA = a;
            UIVertex outerB = b;
            outerA.position += offset;
            outerB.position += offset;
            outerA.color = Clear(a.color);
            outerB.color = Clear(b.color);
            int start = vh.currentVertCount;
            vh.AddVert(a);
            vh.AddVert(b);
            vh.AddVert(outerB);
            vh.AddVert(outerA);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static Color32 Clear(Color32 color)
        {
            color.a = 0;
            return color;
        }

        private static bool Degenerate(Vector3 a, Vector3 b, Vector3 c)
        {
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            return Mathf.Abs(area) < 0.0001f;
        }

        // 向きを問わない辺の鍵（端の点を細かさ Quantum でまとめる）。
        private static (long, long) Key(Vector3 a, Vector3 b)
        {
            long pa = Pack(a);
            long pb = Pack(b);
            return pa < pb ? (pa, pb) : (pb, pa);
        }

        private static long Pack(Vector3 p)
        {
            long x = Mathf.RoundToInt(p.x * Quantum);
            long y = Mathf.RoundToInt(p.y * Quantum);
            return (x << 32) ^ (y & 0xffffffffL);
        }
    }
}
