using System.Collections.Generic;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.Tests
{
    /// <summary>
    /// 自前の多角形で描く UI のふちのぼかし（UiEdgeSmoothing。2026-11-16 のユーザーの要望: 斜め線のギザギザが目立つ）:
    /// 形の外周（ほかの三角形と共有していない辺）だけに、外へ透明になる帯を足す。内側の辺には足さない。
    /// </summary>
    public sealed class UiEdgeSmoothingTests
    {
        [Test]
        public void Triangle_GetsAFringeOnEachEdge_FadingOutward()
        {
            var vh = new VertexHelper();
            Color32 color = new Color32(255, 200, 100, 255);
            vh.AddVert(new Vector3(0f, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(100f, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(30f, 80f), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
            UiEdgeSmoothing.Apply(vh, 1f);
            Assert.That(vh.currentVertCount, Is.EqualTo(3 + 3 * 4), "3 つの辺に帯がない");
            Assert.That(vh.currentIndexCount, Is.EqualTo(3 + 3 * 6));
            UIVertex first = default;
            vh.PopulateUIVertex(ref first, 0);
            Assert.That(first.position, Is.EqualTo(Vector3.zero), "元の三角形が先に描かれない");
            var centroid = new Vector3(130f / 3f, 80f / 3f);
            for (int i = 3; i < vh.currentVertCount; i += 4)
            {
                UIVertex inner = default;
                UIVertex outer = default;
                vh.PopulateUIVertex(ref inner, i);
                vh.PopulateUIVertex(ref outer, i + 3);
                Assert.That(inner.color.a, Is.EqualTo(255), "帯の内側が元の濃さでない");
                Assert.That(outer.color.a, Is.EqualTo(0), "帯の外側が透明でない");
                Assert.That((outer.position - centroid).magnitude, Is.GreaterThan((inner.position - centroid).magnitude), "帯が外へ向いていない");
                Assert.That((outer.position - inner.position).magnitude, Is.EqualTo(1f).Within(0.001f), "帯の幅が 1 でない");
            }
        }

        [Test]
        public void Quad_GetsFringesOnlyOnTheOutline()
        {
            var vh = new VertexHelper();
            Color32 color = Color.white;
            // 斜めの札と同じ平行四辺形（2 つの三角形。真ん中の対角線は内側の辺）。
            vh.AddVert(new Vector3(0f, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(100f, 0f), color, Vector2.zero);
            vh.AddVert(new Vector3(130f, 50f), color, Vector2.zero);
            vh.AddVert(new Vector3(30f, 50f), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
            UiEdgeSmoothing.Apply(vh, 1f);
            // 三角形ごとに描き直すので、元の頂点は 2 × 3。帯は外周の 4 つの辺だけ（対角線には足さない）。
            Assert.That(vh.currentVertCount, Is.EqualTo(2 * 3 + 4 * 4), "外周の 4 つの辺だけに帯を足していない（対角線にも足した）");
        }

        [Test]
        public void Fringes_AreDrawnRightAfterTheirShape_SoLaterShapesCoverThem()
        {
            // 後ろの形（三角形 1）の帯は、手前の形（三角形 2）より先に描く（手前の形の上に後ろの形の帯が出ない）。
            var vh = new VertexHelper();
            Color32 back = new Color32(0, 200, 255, 255);
            Color32 front = Color.white;
            vh.AddVert(new Vector3(0f, 0f), back, Vector2.zero);
            vh.AddVert(new Vector3(100f, 0f), back, Vector2.zero);
            vh.AddVert(new Vector3(50f, 10f), back, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddVert(new Vector3(40f, -20f), front, Vector2.zero);
            vh.AddVert(new Vector3(60f, -20f), front, Vector2.zero);
            vh.AddVert(new Vector3(50f, 30f), front, Vector2.zero);
            vh.AddTriangle(3, 4, 5);
            UiEdgeSmoothing.Apply(vh, 1f);
            var stream = new List<UIVertex>();
            vh.GetUIVertexStream(stream);
            int lastBack = -1;
            int firstFront = -1;
            for (int i = 0; i < stream.Count; i++)
            {
                Color32 c = stream[i].color;
                if (c.g == back.g && c.b == back.b && c.r == back.r)
                {
                    lastBack = i;
                }
                if (firstFront < 0 && c.r == 255 && c.g == 255)
                {
                    firstFront = i;
                }
            }
            Assert.That(lastBack, Is.LessThan(firstFront), "後ろの形の帯が手前の形より後に描かれる");
        }

        [Test]
        public void LocalPixel_FollowsTheCanvasAndTheGraphicScale()
        {
            var root = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var child = new GameObject("Plate", typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                child.transform.localScale = new Vector3(2f, 2f, 1f);
                var plate = child.AddComponent<SlantedPlate>();
                float expected = 1f / (2f * canvas.scaleFactor);
                Assert.That(UiEdgeSmoothing.LocalPixel(plate), Is.EqualTo(expected).Within(0.0001f), "2 倍に拡大した札の 1 ピクセルが半分の長さでない");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ImeGuard_FindsNoFieldWithoutAnEventSystem()
        {
            Assert.That(ImeGuard.FocusedField(null), Is.Null);
        }
    }
}
