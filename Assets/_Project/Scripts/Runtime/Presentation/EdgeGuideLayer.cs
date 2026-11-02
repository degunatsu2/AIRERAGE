using System.Collections.Generic;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 画面の外にある物の誘導表示（画面の端の矢印と札。GuidePlate）を、まとめて描く（ユーザーの要望、2026-10-14）。
    /// 拠点、100 枚のコイン、撃破ドロップのかたまり、ほかのプレイヤーなど、表示ごとの部品が毎フレームの Update で Add を呼び、
    /// ここが OnGUI でまとめて描く。札が重なるときは、画面の端に沿ってずらす。
    /// 毎フレーム、ほかの部品の Update より先に（DefaultExecutionOrder）前のフレームの分を消す。
    /// 知らせたい誘導表示（撃破ドロップのかたまり、拠点の移動。Add の announce）は、出てきたときに軽く目立たせる（ユーザーの要望、2026-10-30）:
    /// 札が少し大きく出て縮み、白く光り、札の周りに縁の色の枠が広がって消える（AppearSeconds）。同じ種類の前のフレームの札と画面の上で近ければ
    /// 同じ札とみなし（かたまりのコインが拾われて少し動いた、カメラが動いた）、出直さない。拠点が移ると札が離れた所に出るので、出直す。
    /// 重要な出来事の札（ホットエリア・拠点の移動・100 枚のコイン・WANTED の人。Add の pulse）は、出てきた後も PulseSeconds ごとに白く光って
    /// 枠が広がる（目につかせるため。ユーザーの要望、2026-11-02）。
    /// 画面の中の物にも札を出せる（Add の onScreen。ホットエリアと 100 枚のコイン）: 物の上に下向きの矢印と札を出す（画面の外のときの札を
    /// そのまま出し続ける形）。
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

        /// <summary>出てきたときに目立たせる時間（秒）【仮】。</summary>
        public const float AppearSeconds = 0.6f;

        /// <summary>重要な出来事の札が、出てきた後にくり返し光る間隔（秒）。</summary>
        public const float PulseSeconds = 1f;

        // 前のフレームの同じ種類の札と同じとみなす、画面の上の距離（ピクセル）。
        private const float SameGuideDistance = 160f;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Entry> _previous = new List<Entry>();
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

            // 出てきたときに目立たせる種類（null なら目立たせない）と、出てきた時刻。
            public string Announce;
            public float AppearedAt;

            // 出てきた後も PulseSeconds ごとに光るか。画面の中の物の上に出した札か。
            public bool Pulse;
            public bool OnScreen;
        }

        /// <summary>このフレームに描く誘導表示の数（テスト用）。</summary>
        public int Count => _entries.Count;

        /// <summary>このフレームに描く誘導表示のうち、出てきたばかりで目立たせているものの数（テスト用）。</summary>
        public int AppearingCount
        {
            get
            {
                int count = 0;
                foreach (Entry entry in _entries)
                {
                    count += Appear(entry) < 1f ? 1 : 0;
                }
                return count;
            }
        }

        /// <summary>このフレームに描く誘導表示のうち、くり返し光る札の数と、画面の中の物の上に出した札の数（テスト用）。</summary>
        public int PulsingCount
        {
            get
            {
                int count = 0;
                foreach (Entry entry in _entries)
                {
                    count += entry.Pulse ? 1 : 0;
                }
                return count;
            }
        }

        public int OnScreenCount
        {
            get
            {
                int count = 0;
                foreach (Entry entry in _entries)
                {
                    count += entry.OnScreen ? 1 : 0;
                }
                return count;
            }
        }

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
        /// を出し、true を返す。画面の中なら false（onScreen なら、world から lift（m）だけ上に下向きの矢印と札を出す）。
        /// announce を渡すと、その種類の札が新しく出てきたときに目立たせ、pulse なら出てきた後も PulseSeconds ごとに光らせる。
        /// </summary>
        public bool Add(Vector3 world, Color frame, Texture2D icon, string text, float alpha = 1f, bool rainbow = false, string announce = null,
            bool pulse = false, bool onScreen = false, float lift = 0f)
        {
            if (_camera == null)
            {
                return false;
            }
            bool offscreen = ScreenEdgeMarker.Locate(_camera, world, _edgeMargin, out Vector2 edge, out float angle);
            if (!offscreen)
            {
                if (!onScreen)
                {
                    return false;
                }
                // 画面の中: 物の上に、下向きの矢印（その上に札）。
                Vector3 p = _camera.WorldToScreenPoint(world + Vector3.up * lift);
                if (p.z <= 0f)
                {
                    return false;
                }
                edge = new Vector2(p.x, Screen.height - p.y - _arrowSize * 0.5f);
                angle = 90f;
            }
            _entries.Add(new Entry
            {
                Edge = edge, Angle = angle, Frame = frame, Rainbow = rainbow, Icon = icon, Text = text, Alpha = alpha,
                Announce = announce, AppearedAt = announce != null ? AppearedAt(announce, edge) : float.NegativeInfinity,
                Pulse = pulse && announce != null, OnScreen = !offscreen,
            });
            return offscreen;
        }

        // 種類 announce の札が出てきた時刻（前のフレームに、同じ種類の札が近くにあれば、その時刻を引き継ぐ。なければ今）。
        private float AppearedAt(string announce, Vector2 edge)
        {
            int best = -1;
            float bestDistance = SameGuideDistance;
            for (int i = 0; i < _previous.Count; i++)
            {
                if (_previous[i].Announce != announce)
                {
                    continue;
                }
                float distance = Vector2.Distance(_previous[i].Edge, edge);
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (best < 0)
            {
                return Time.time;
            }
            float appearedAt = _previous[best].AppearedAt;
            // 同じ札を 2 つに引き継がない。
            _previous.RemoveAt(best);
            return appearedAt;
        }

        // 出てきてからの進み（0〜1。目立たせない札と、目立たせ終わった札は 1）。
        private static float Appear(Entry entry)
        {
            return entry.Announce == null ? 1f : Mathf.Clamp01((Time.time - entry.AppearedAt) / AppearSeconds);
        }

        // 白く光って枠が広がる動きの進み（0〜1。光っていなければ 1）。出てきたとき（Appear）と、くり返し光る札は PulseSeconds ごと。
        private static float Flash(Entry entry)
        {
            float appear = Appear(entry);
            if (appear < 1f || !entry.Pulse)
            {
                return appear;
            }
            float cycle = Mathf.Repeat(Time.time - entry.AppearedAt, PulseSeconds);
            return Mathf.Clamp01(cycle / AppearSeconds);
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
            _previous.Clear();
            _previous.AddRange(_entries);
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
                _text = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip });
            }
            Layout();
            float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 6f);
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                Color arrowColor = entry.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.6f, 1f), 0.65f, 1f) : entry.Frame;
                arrowColor.a *= entry.Alpha;
                float appear = Appear(entry);
                // 出てきたばかりの札は、矢印も少し大きく。
                float pop = 1f + 0.35f * (1f - Ease(appear));
                ScreenEdgeMarker.DrawArrow(_arrow, entry.Edge, entry.Angle, _arrowSize * pulse * pop, arrowColor);
                Matrix4x4 matrix = GUI.matrix;
                if (appear < 1f)
                {
                    // 少し大きく出て縮む（0.25 秒）。
                    float scale = 1f + 0.25f * (1f - Ease(appear / 0.4f));
                    GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), _rects[i].center);
                }
                if (entry.Rainbow)
                {
                    _plate.DrawRainbow(_rects[i], Time.time, entry.Icon, entry.Text, _text, entry.Alpha);
                }
                else
                {
                    _plate.Draw(_rects[i], entry.Frame, entry.Icon, entry.Text, _text, entry.Alpha);
                }
                float flash = Flash(entry);
                if (flash < 1f)
                {
                    // 白く光ってから、本来の札に戻る（くり返しの光は少し弱く）。
                    float strength = appear < 1f ? 0.75f : 0.55f;
                    _plate.DrawFill(_rects[i], new Color(1f, 1f, 1f, strength * Mathf.Clamp01(1f - flash / 0.5f) * entry.Alpha));
                }
                GUI.matrix = matrix;
                if (flash < 1f)
                {
                    DrawSpreadingFrame(_rects[i], entry.Rainbow ? Color.white : entry.Frame, flash, entry.Alpha);
                }
            }
        }

        // 札の周りに広がって消える枠（縁の色）。
        private static void DrawSpreadingFrame(Rect rect, Color color, float progress, float alpha)
        {
            float spread = 6f + 34f * Ease(progress);
            float thickness = Mathf.Lerp(4f, 1f, progress);
            var outer = new Rect(rect.x - spread, rect.y - spread, rect.width + spread * 2f, rect.height + spread * 2f);
            Color previous = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, (1f - progress) * 0.9f * alpha);
            GUI.DrawTexture(new Rect(outer.x, outer.y, outer.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(outer.x, outer.yMax - thickness, outer.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(outer.x, outer.y, thickness, outer.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(outer.xMax - thickness, outer.y, thickness, outer.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
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
