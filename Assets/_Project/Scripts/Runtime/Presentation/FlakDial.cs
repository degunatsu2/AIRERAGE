using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// JUGGERNAUT のフラックの弾道の強さのダイアル（ユーザーの要望、2026-10-29）。歯車の縁の円盤の面に、段階の目（弱・中・強・弩）を
    /// 60° おきに並べ、上の動かない指針の下に来た目だけを大きく、段階の色で光らせる（ほかは小さく灰色）。段階が変わると、ダイアルが
    /// ばねのように少し行き過ぎてから止まる回り方で、次の目を指針の下へ回す（上で右隣の強い目へ）。
    /// 見た目だけ（段階は状態から読む）。円盤と指針は頂点の色で塗るメッシュ（材質は Sprites-Default）、目は文字（TextMesh）。
    /// </summary>
    public sealed class FlakDial
    {
        /// <summary>隣の目との角度（度）。</summary>
        public const float StepDegrees = 60f;

        // 大きさ（円盤の半径を 1 とした割合）。
        private const int Teeth = 24;
        private const float FaceRadius = 0.8f;
        private const float RootRadius = 0.88f;
        private const float LabelRadius = 0.48f;
        private const float LabelHeight = 0.42f;
        private const float SmallLabel = 0.6f;
        private const int CircleSegments = 40;
        // 回り方（ばね。少し行き過ぎてから止まる）。
        private const float Stiffness = 520f;
        private const float DampingRatio = 0.55f;
        private const float BumpSeconds = 0.15f;

        private static readonly Color FaceCenter = new Color(0.14f, 0.15f, 0.16f, 0.95f);
        private static readonly Color FaceEdge = new Color(0.05f, 0.05f, 0.06f, 0.95f);
        private static readonly Color GearInner = new Color(0.2f, 0.21f, 0.23f, 1f);
        private static readonly Color GearOuter = new Color(0.55f, 0.57f, 0.6f, 1f);
        private static readonly Color Tick = new Color(0.75f, 0.77f, 0.8f, 0.9f);
        private static readonly Color Hub = new Color(0.4f, 0.42f, 0.45f, 1f);
        private static readonly Color DimLabel = new Color(0.62f, 0.64f, 0.68f, 0.85f);
        private static readonly Color Outline = new Color(0f, 0f, 0f, 0.85f);

        private readonly GameObject _root;
        private readonly Transform _wheel;
        private readonly Transform _pointer;
        private readonly Mesh _wheelMesh;
        private readonly Mesh _overlay;
        private readonly TextMesh[] _labels;
        private readonly Color[] _colors;
        private readonly float _labelSize;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _vertexColors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        private bool _shown;
        private int _level = -1;
        private float _offset;
        private float _velocity;
        private float _changedAt = -10f;
        private Color _overlayColor;
        private float _overlayBump = -1f;

        /// <summary>
        /// parent の子にダイアルを作る。labels は段階の目の文字、colors はその色（同じ数）。目の文字は labelTemplate（隠した見本）を写す。
        /// </summary>
        public FlakDial(Transform parent, string name, Material material, TextMesh labelTemplate, string[] labels, Color[] colors)
        {
            _colors = colors;
            _root = new GameObject(name);
            _root.transform.SetParent(parent, false);

            var wheel = new GameObject("Wheel");
            wheel.transform.SetParent(_root.transform, false);
            _wheel = wheel.transform;
            _wheelMesh = new Mesh { name = name + "Wheel" };
            BuildWheel(_wheelMesh, labels.Length);
            AddRenderer(wheel, _wheelMesh, material, 3);

            var overlay = new GameObject("Pointer");
            overlay.transform.SetParent(_root.transform, false);
            _pointer = overlay.transform;
            _overlay = new Mesh { name = name + "Pointer" };
            _overlay.MarkDynamic();
            AddRenderer(overlay, _overlay, material, 5);

            _labels = new TextMesh[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                TextMesh label = Object.Instantiate(labelTemplate, _root.transform);
                label.name = $"Mark{i}";
                label.gameObject.SetActive(true);
                label.text = labels[i];
                label.anchor = TextAnchor.MiddleCenter;
                label.GetComponent<MeshRenderer>().sortingOrder = 6;
                _labels[i] = label;
            }
            // TextMesh の 1 行の高さは、およそ characterSize × fontSize / 10（m）。目の大きさは transform の拡大で変える。
            _labelSize = 10f / Mathf.Max(1, labelTemplate.fontSize);
            _root.SetActive(false);
        }

        /// <summary>出しているか。</summary>
        public bool Visible => _root.activeSelf;

        /// <summary>円盤の半径と、指針の大きさ（円盤と同じ割合で大きくする。テスト用）。</summary>
        public float Radius => _wheel.localScale.x;
        public float PointerRadius => _pointer.localScale.x;

        /// <summary>指針の下に回す段階（最後に Show で渡したもの）。</summary>
        public int Level => _level;

        /// <summary>指針の下に回す目の文字。</summary>
        public string SelectedLabel => _level >= 0 && _level < _labels.Length ? _labels[_level].text : string.Empty;

        /// <summary>今のダイアルの回り（度。時計回りが正）と、止まる回り（テスト用）。</summary>
        public float Offset => _offset;
        public float TargetOffset => -_level * StepDegrees;

        /// <summary>目 index の今の大きさ（指針の下で 1。テスト用）。</summary>
        public float LabelScale(int index) => Emphasis(index) * (1f - SmallLabel) + SmallLabel;

        /// <summary>
        /// center を中心に、半径 radius（m）のダイアルで段階 level を指す。毎フレーム呼ぶ。出し始めはその段階のまま出し、段階が変わったら
        /// 回して切り替える。
        /// </summary>
        public void Show(Vector3 center, float radius, int level)
        {
            level = Mathf.Clamp(level, 0, _labels.Length - 1);
            if (!_shown)
            {
                _shown = true;
                _root.SetActive(true);
                _level = level;
                _offset = TargetOffset;
                _velocity = 0f;
            }
            else if (level != _level)
            {
                _level = level;
                _changedAt = Time.time;
            }
            Spin(Time.deltaTime);

            _root.transform.position = center;
            _wheel.localScale = Vector3.one * radius;
            _pointer.localScale = Vector3.one * radius;
            // 回り（時計回りが正）を、z 軸の回転（反時計回りが正）にする。
            _wheel.localRotation = Quaternion.Euler(0f, 0f, -_offset);
            float bump = 1f + 0.3f * Mathf.Clamp01(1f - (Time.time - _changedAt) / BumpSeconds);
            UpdatePointer(_colors[_level], bump);
            for (int i = 0; i < _labels.Length; i++)
            {
                float angle = (i * StepDegrees + _offset) * Mathf.Deg2Rad;
                Transform t = _labels[i].transform;
                t.localPosition = new Vector3(Mathf.Sin(angle) * radius * LabelRadius, Mathf.Cos(angle) * radius * LabelRadius, -0.03f);
                t.localScale = Vector3.one * (radius * LabelHeight * LabelScale(i) * _labelSize);
                _labels[i].color = Color.Lerp(DimLabel, _colors[i], Emphasis(i));
            }
        }

        public void Hide()
        {
            _shown = false;
            if (_root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>作ったメッシュを片付ける（入れ物は親と一緒に消える）。</summary>
        public void Destroy()
        {
            Object.Destroy(_wheelMesh);
            Object.Destroy(_overlay);
        }

        // 指針からの角度で、目をどれだけ強調するか（指針の下で 1、隣の目の所で 0）。
        private float Emphasis(int index)
        {
            float degrees = Mathf.Abs(index * StepDegrees + _offset);
            return Mathf.Clamp01(1f - degrees / StepDegrees);
        }

        // ばねで止まる回りへ近づける（少し行き過ぎる）。
        private void Spin(float dt)
        {
            float damping = 2f * DampingRatio * Mathf.Sqrt(Stiffness);
            // 細かく刻んで、フレームの間隔が長くても暴れないようにする。
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.005f));
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                float accel = Stiffness * (TargetOffset - _offset) - damping * _velocity;
                _velocity += accel * h;
                _offset += _velocity * h;
            }
            if (Mathf.Abs(TargetOffset - _offset) < 0.05f && Mathf.Abs(_velocity) < 0.5f)
            {
                _offset = TargetOffset;
                _velocity = 0f;
            }
        }

        // 動かない部分（半径 1。大きさは入れ物の拡大で合わせる）: 指針（上の三角）と、指針の下の目の後ろの光。色が変わるか、跳ねている
        // 間だけ作り直す。
        private void UpdatePointer(Color color, float bump)
        {
            if (color == _overlayColor && Mathf.Approximately(bump, _overlayBump))
            {
                return;
            }
            _overlayColor = color;
            _overlayBump = bump;
            Begin();
            Color glow = new Color(color.r, color.g, color.b, 0.35f);
            Color clear = new Color(color.r, color.g, color.b, 0f);
            AddDisc(new Vector3(0f, LabelRadius, -0.02f), 0.3f, glow, clear);
            float w = 0.16f * bump;
            const float tip = 1.02f;
            float top = tip + 0.24f * bump;
            // 黒い縁の上に色の三角。
            AddTriangle(new Vector3(0f, tip - 0.05f, -0.02f), new Vector3(-w - 0.04f, top + 0.03f, -0.02f),
                new Vector3(w + 0.04f, top + 0.03f, -0.02f), Outline);
            AddTriangle(new Vector3(0f, tip, -0.03f), new Vector3(-w, top, -0.03f), new Vector3(w, top, -0.03f), color);
            Finish(_overlay);
        }

        // 回る部分（半径 1）: 面、目の印、歯車の縁、軸。
        private void BuildWheel(Mesh mesh, int marks)
        {
            Begin();
            AddDisc(Vector3.zero, FaceRadius, FaceCenter, FaceEdge);
            for (int i = 0; i < marks; i++)
            {
                float a = i * StepDegrees * Mathf.Deg2Rad;
                Vector3 along = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                Vector3 side = new Vector3(along.y, -along.x, 0f) * 0.025f;
                AddQuad(along * 0.68f - side, along * 0.68f + side, along * 0.78f + side, along * 0.78f - side, Tick, Tick, -0.005f);
            }
            float tooth = Mathf.PI * 2f / Teeth;
            for (int t = 0; t < Teeth; t++)
            {
                float a = t * tooth;
                // 歯の形: 根元 → 立ち上がり → 先 → 下がり → 次の歯の根元。
                float[] at = { a, a + tooth * 0.2f, a + tooth * 0.5f, a + tooth * 0.7f, a + tooth };
                float[] r = { RootRadius, 1f, 1f, RootRadius, RootRadius };
                for (int k = 0; k < 4; k++)
                {
                    Vector3 i0 = Polar(FaceRadius - 0.02f, at[k]);
                    Vector3 i1 = Polar(FaceRadius - 0.02f, at[k + 1]);
                    Vector3 o0 = Polar(r[k], at[k]);
                    Vector3 o1 = Polar(r[k + 1], at[k + 1]);
                    AddQuad(i0, i1, o1, o0, GearInner, Color.Lerp(GearInner, GearOuter, (r[k] + r[k + 1]) * 0.5f >= 0.99f ? 1f : 0.6f), 0.002f);
                }
            }
            AddDisc(new Vector3(0f, 0f, -0.006f), 0.13f, Hub, Hub * 0.8f);
            Finish(mesh);
        }

        private static Vector3 Polar(float radius, float angle)
        {
            return new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, 0f);
        }

        private static void AddRenderer(GameObject go, Mesh mesh, Material material, int sortingOrder)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void Begin()
        {
            _vertices.Clear();
            _vertexColors.Clear();
            _triangles.Clear();
        }

        private void Finish(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetColors(_vertexColors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
        }

        private void AddDisc(Vector3 center, float radius, Color inner, Color outer)
        {
            int baseIndex = _vertices.Count;
            _vertices.Add(center);
            _vertexColors.Add(inner);
            for (int k = 0; k < CircleSegments; k++)
            {
                float a = Mathf.PI * 2f * k / CircleSegments;
                _vertices.Add(center + new Vector3(Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, 0f));
                _vertexColors.Add(outer);
            }
            for (int k = 0; k < CircleSegments; k++)
            {
                _triangles.Add(baseIndex);
                _triangles.Add(baseIndex + 1 + k);
                _triangles.Add(baseIndex + 1 + (k + 1) % CircleSegments);
            }
        }

        private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color inner, Color outer, float z)
        {
            int baseIndex = _vertices.Count;
            var offset = new Vector3(0f, 0f, z);
            _vertices.Add(a + offset);
            _vertices.Add(b + offset);
            _vertices.Add(c + offset);
            _vertices.Add(d + offset);
            _vertexColors.Add(inner);
            _vertexColors.Add(inner);
            _vertexColors.Add(outer);
            _vertexColors.Add(outer);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 2);
            _triangles.Add(baseIndex + 3);
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            int baseIndex = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertexColors.Add(color);
            _vertexColors.Add(color);
            _vertexColors.Add(color);
            _triangles.Add(baseIndex);
            _triangles.Add(baseIndex + 1);
            _triangles.Add(baseIndex + 2);
        }
    }
}
