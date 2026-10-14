using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 画面の外にある物の誘導表示（画面の端の矢印と札。GuidePlate）を、まとめて描く（ユーザーの要望、2026-10-14）。
    /// 拠点、100 枚のコイン、撃破ドロップのかたまり、ほかのプレイヤーなど、表示ごとの部品が毎フレームの Update で Add を呼び、
    /// ここが OnGUI でまとめて描く。札が重なるときは、画面の端に沿ってずらす。
    /// 毎フレーム、ほかの部品の Update より先に（DefaultExecutionOrder）前のフレームの分を消す。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class EdgeGuideLayer : MonoBehaviour
    {
        [Tooltip("位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("矢印を置く、画面の端からの距離（ピクセル）")]
        [SerializeField] private float _edgeMargin = 56f;
        [SerializeField] private int _plateWidth = 180;
        [SerializeField] private int _plateHeight = 48;
        [SerializeField] private float _arrowSize = 40f;
        [Tooltip("札どうしの間（ピクセル）")]
        [SerializeField] private float _gap = 6f;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Rect> _rects = new List<Rect>();
        private GuidePlate _plate;
        private Texture2D _arrow;
        private GUIStyle _text;

        private struct Entry
        {
            public Vector2 Edge;
            public float Angle;
            public Color Frame;
            public bool Rainbow;
            public Texture2D Icon;
            public string Text;
            public float Alpha;
        }

        /// <summary>このフレームに描く誘導表示の数（テスト用）。</summary>
        public int Count => _entries.Count;

        /// <summary>このフレームに描く誘導表示の文字（テスト用）。</summary>
        public IEnumerable<string> Texts
        {
            get
            {
                foreach (Entry entry in _entries)
                {
                    yield return entry.Text;
                }
            }
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Camera camera)
        {
            _camera = camera;
        }

        /// <summary>
        /// world が画面の外なら、このフレームに誘導表示（矢印と札。左に icon、右に text、縁と斜めの線は frame の色。rainbow なら虹色）
        /// を出し、true を返す。画面の中なら何もせず false。
        /// </summary>
        public bool Add(Vector3 world, Color frame, Texture2D icon, string text, float alpha = 1f, bool rainbow = false)
        {
            if (_camera == null || !ScreenEdgeMarker.Locate(_camera, world, _edgeMargin, out Vector2 edge, out float angle))
            {
                return false;
            }
            _entries.Add(new Entry { Edge = edge, Angle = angle, Frame = frame, Rainbow = rainbow, Icon = icon, Text = text, Alpha = alpha });
            return true;
        }

        private void Awake()
        {
            if (_camera == null)
            {
                Debug.LogError($"{name}: カメラが設定されていません", this);
            }
        }

        private void Update()
        {
            _entries.Clear();
        }

        private void OnDestroy()
        {
            _plate?.Dispose();
            if (_arrow != null)
            {
                Destroy(_arrow);
            }
        }

        private void OnGUI()
        {
            if (_entries.Count == 0)
            {
                return;
            }
            if (_plate == null)
            {
                _plate = new GuidePlate(_plateWidth, _plateHeight);
                _arrow = ScreenEdgeMarker.CreateArrowTexture(64);
                _text = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            }
            Layout();
            float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 6f);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                Color arrowColor = entry.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.6f, 1f), 0.65f, 1f) : entry.Frame;
                arrowColor.a *= entry.Alpha;
                ScreenEdgeMarker.DrawArrow(_arrow, entry.Edge, entry.Angle, _arrowSize * pulse, arrowColor);
                if (entry.Rainbow)
                {
                    _plate.DrawRainbow(_rects[i], Time.time, entry.Icon, entry.Text, _text, entry.Alpha);
                }
                else
                {
                    _plate.Draw(_rects[i], entry.Frame, entry.Icon, entry.Text, _text, entry.Alpha);
                }
            }
        }

        // 札を矢印の内側に置き、重なるものは画面の端に沿ってずらす（左右の端なら上下へ、上下の端なら左右へ）。
        private void Layout()
        {
            _rects.Clear();
            foreach (Entry entry in _entries)
            {
                _rects.Add(_plate.PlaceAtEdge(entry.Edge, entry.Angle, _arrowSize, 8f));
            }
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int i = 0; i < _rects.Count; i++)
                {
                    for (int j = i + 1; j < _rects.Count; j++)
                    {
                        Rect a = _rects[i];
                        Rect b = _rects[j];
                        if (!a.Overlaps(b))
                        {
                            continue;
                        }
                        bool sideEdge = Mathf.Abs(Mathf.Cos(_entries[j].Angle * Mathf.Deg2Rad)) > 0.5f;
                        if (sideEdge)
                        {
                            b.y = b.center.y >= a.center.y ? a.yMax + _gap : a.y - _gap - b.height;
                        }
                        else
                        {
                            b.x = b.center.x >= a.center.x ? a.xMax + _gap : a.x - _gap - b.width;
                        }
                        b.x = Mathf.Clamp(b.x, 8f, Screen.width - 8f - b.width);
                        b.y = Mathf.Clamp(b.y, 8f, Screen.height - 8f - b.height);
                        _rects[j] = b;
                        moved = true;
                    }
                }
                if (!moved)
                {
                    break;
                }
            }
        }
    }
}
