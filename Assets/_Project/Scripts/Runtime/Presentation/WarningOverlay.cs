using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ギミックの予告の表示（表示だけ。ユーザーの要望、2026-10-20）: 当たる範囲を黄色の斜線（黄色と暗い色の縞。明るいステージでも暗い
    /// ステージでも見える）で塗り、「三角形の中にエクスクラメーションマーク」の記号を置く。どちらもゆっくり明滅する。
    /// 縞は世界の位置で決まる模様なので、範囲の大きさによらず同じ幅で、隣り合う範囲どうしでもつながる。絵はその場で描く（音声ファイルと
    /// 同じく、画像のファイルを足さない）。StageZoneView が作って使う。
    /// </summary>
    public sealed class WarningOverlay : MonoBehaviour
    {
        /// <summary>縞 1 組の幅（m）。</summary>
        private const float StripePeriod = 1.6f;

        /// <summary>記号の大きさ（m）。</summary>
        public const float IconSize = 2.6f;

        private static readonly Color Yellow = new Color(1f, 0.84f, 0.05f, 0.85f);
        private static readonly Color Dark = new Color(0.08f, 0.07f, 0.02f, 0.45f);

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<GameObject> _icons = new List<GameObject>();
        private Mesh _mesh;
        private Material _stripeMaterial;
        private Material _iconMaterial;
        private Texture2D _stripeTexture;
        private Texture2D _iconTexture;
        private MeshRenderer _stripes;
        private float _z;

        /// <summary>出しているか（テスト用）。</summary>
        public bool IsShowing => _stripes != null && _stripes.enabled;

        /// <summary>今の縞の範囲の数と記号の数（テスト用）。</summary>
        public int AreaCount { get; private set; }
        public int IconCount { get; private set; }

        /// <summary>警告の記号の絵（画面の文字の横にも使う）。</summary>
        public Texture2D IconTexture => _iconTexture;

        /// <summary>縞の世界での範囲（テスト用）。</summary>
        public Bounds StripeBounds => _stripes != null ? _stripes.bounds : default;

        /// <summary>作る。material は Sprites/Default（半透明）の材質（複製して使う）。z は手前の位置（世界の z）。</summary>
        public void Initialize(Material material, float z)
        {
            _z = z;
            _stripeTexture = CreateStripes();
            _iconTexture = CreateIcon();
            _stripeMaterial = new Material(material) { mainTexture = _stripeTexture };
            _iconMaterial = new Material(material) { mainTexture = _iconTexture };
            _mesh = new Mesh { name = "WarningStripes" };
            _mesh.MarkDynamic();
            var stripes = new GameObject("WarningStripes");
            stripes.transform.SetParent(transform, false);
            stripes.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _stripes = stripes.AddComponent<MeshRenderer>();
            _stripes.sharedMaterial = _stripeMaterial;
            _stripes.shadowCastingMode = ShadowCastingMode.Off;
            _stripes.receiveShadows = false;
            _stripes.enabled = false;
        }

        private void OnDestroy()
        {
            Destroy(_mesh);
            Destroy(_stripeMaterial);
            Destroy(_iconMaterial);
            Destroy(_stripeTexture);
            Destroy(_iconTexture);
        }

        /// <summary>範囲 areas（世界の xy の長方形）を縞で塗り、icons の位置に記号を置く。alpha は明滅の強さ（0〜1）。</summary>
        public void Show(IReadOnlyList<Rect> areas, IReadOnlyList<Vector2> icons, float alpha)
        {
            if (_mesh == null)
            {
                return;
            }
            _vertices.Clear();
            _uvs.Clear();
            _triangles.Clear();
            foreach (Rect area in areas)
            {
                AddQuad(area);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            AreaCount = areas.Count;
            var tint = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            _stripeMaterial.color = tint;
            _iconMaterial.color = new Color(1f, 1f, 1f, Mathf.Clamp01(0.4f + alpha));
            _stripes.enabled = areas.Count > 0;
            while (_icons.Count < icons.Count)
            {
                _icons.Add(CreateIconQuad());
            }
            for (int i = 0; i < _icons.Count; i++)
            {
                bool shown = i < icons.Count;
                if (shown)
                {
                    _icons[i].transform.position = new Vector3(icons[i].x, icons[i].y, _z - 0.05f);
                }
                if (_icons[i].activeSelf != shown)
                {
                    _icons[i].SetActive(shown);
                }
            }
            IconCount = icons.Count;
        }

        public void Hide()
        {
            if (_stripes != null)
            {
                _stripes.enabled = false;
            }
            foreach (GameObject icon in _icons)
            {
                if (icon.activeSelf)
                {
                    icon.SetActive(false);
                }
            }
            AreaCount = 0;
            IconCount = 0;
        }

        // 長方形 1 つ（範囲は世界の位置なので、この物体の中の位置に直す。UV は世界の位置。縞の模様が範囲によらずつながる）。
        private void AddQuad(Rect area)
        {
            int start = _vertices.Count;
            Transform stripes = _stripes.transform;
            _vertices.Add(stripes.InverseTransformPoint(new Vector3(area.xMin, area.yMin, _z)));
            _vertices.Add(stripes.InverseTransformPoint(new Vector3(area.xMax, area.yMin, _z)));
            _vertices.Add(stripes.InverseTransformPoint(new Vector3(area.xMax, area.yMax, _z)));
            _vertices.Add(stripes.InverseTransformPoint(new Vector3(area.xMin, area.yMax, _z)));
            _uvs.Add(new Vector2(area.xMin, area.yMin) / StripePeriod);
            _uvs.Add(new Vector2(area.xMax, area.yMin) / StripePeriod);
            _uvs.Add(new Vector2(area.xMax, area.yMax) / StripePeriod);
            _uvs.Add(new Vector2(area.xMin, area.yMax) / StripePeriod);
            _triangles.Add(start);
            _triangles.Add(start + 2);
            _triangles.Add(start + 1);
            _triangles.Add(start);
            _triangles.Add(start + 3);
            _triangles.Add(start + 2);
        }

        private GameObject CreateIconQuad()
        {
            var go = new GameObject("WarningIcon");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(IconSize, IconSize, 1f);
            var mesh = new Mesh { name = "WarningIcon" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _iconMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // 斜めの縞（黄色と暗い色。繰り返して貼る）。
        private static Texture2D CreateStripes()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = (x + y) % size < size / 2 ? Yellow : Dark;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // 黄色の三角形（黒い縁）の中に、黒い「!」。
        private static Texture2D CreateIcon()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            var apex = new Vector2(64f, 122f);
            var left = new Vector2(4f, 10f);
            var right = new Vector2(124f, 10f);
            var innerApex = new Vector2(64f, 104f);
            var innerLeft = new Vector2(20f, 20f);
            var innerRight = new Vector2(108f, 20f);
            var black = new Color(0.05f, 0.05f, 0.05f, 1f);
            var yellow = new Color(1f, 0.84f, 0.05f, 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    Color color = Color.clear;
                    if (InTriangle(p, apex, left, right))
                    {
                        color = InTriangle(p, innerApex, innerLeft, innerRight) ? yellow : black;
                        bool bar = Mathf.Abs(p.x - 64f) <= 6f && p.y >= 46f && p.y <= 90f;
                        bool dot = (p - new Vector2(64f, 33f)).sqrMagnitude <= 49f;
                        if (bar || dot)
                        {
                            color = black;
                        }
                    }
                    pixels[y * size + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b);
            float d2 = Cross(p, b, c);
            float d3 = Cross(p, c, a);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }
    }
}
