using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 落雷の見た目（2026-11-20 の方針: ギミックを実際の現象に寄せる。表示だけで、当たりは StageZoneView の危険の柱のまま）。手本は
    /// NOAA・NWS の雷の解説と、ゲームの落雷の演出の分解:
    /// - 予告の間: 落ちる地点の地面から、短い光の筋（お迎え放電）が細かく明滅しながら上へ伸びる。
    /// - 落ちた瞬間: 枝分かれした稲妻（最初の一撃だけ枝がある）がいちばん明るく光り、同じ道を 2 回の再放電がたどって明滅する
    ///   （3 回目は下の方で別の道へそれる）。芯は青白、まわりに紫のにじみ。地面では閃光の光、火花、煙、赤く焼けた跡が残って冷える。
    /// 稲妻の本線は危険の柱（ShownColumn）の幅の中を通す（判定と見た目を合わせる）。
    /// </summary>
    public sealed class LightningStrikeView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("光る線の材質（Airerage/GlowLine）")]
        [SerializeField] private Material _lineMaterial;
        [Tooltip("稲妻の上の端と、落ちる地面（ワールドの y）")]
        [SerializeField] private float _top = 26f;
        [SerializeField] private float _ground;
        [Tooltip("稲妻の奥行き（ワールドの z。キャラクターより少し奥）")]
        [SerializeField] private float _depth = 0.3f;
        [Tooltip("地面の閃光の光")]
        [SerializeField] private Light _flash;
        [Tooltip("赤く焼けた跡（地面に寝かせた板）")]
        [SerializeField] private Renderer _scorch;
        [Tooltip("落ちた所で一度に出す火花と煙（一撃ごと）")]
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField] private ParticleSystem _smoke;
        [Tooltip("空の光（奥の大きな板。雲の中の放電で空が一瞬明るくなる）")]
        [SerializeField] private Renderer _skyFlash;

        private const int MainPoints = 34;
        private const int BranchCount = 4;
        private const int BranchPoints = 9;
        private const int StreamerCount = 3;
        // 一撃（最初の放電と 2 回の再放電）の始まり（秒）と明るさ。
        private static readonly float[] StrokeTimes = { 0f, 0.1f, 0.22f };
        private static readonly float[] StrokePeaks = { 1f, 0.75f, 0.6f };
        private static readonly Color CoreColor = new Color(0.86f, 0.9f, 1f);
        private static readonly Color HaloColor = new Color(0.55f, 0.42f, 1f);
        private static readonly Color StreamerColor = new Color(0.75f, 0.7f, 1f);

        private LineRenderer _mainCore;
        private LineRenderer _mainHalo;
        private readonly List<LineRenderer> _branchCore = new List<LineRenderer>();
        private readonly List<LineRenderer> _branchHalo = new List<LineRenderer>();
        private readonly List<LineRenderer> _streamers = new List<LineRenderer>();
        private readonly Vector3[] _main = new Vector3[MainPoints];
        private readonly Vector3[] _fork = new Vector3[MainPoints];
        private readonly Vector3[][] _branches = new Vector3[BranchCount][];
        private MaterialPropertyBlock _scorchBlock;
        private float _strikeTime = -100f;
        private int _emittedStrokes;
        private bool _wasActive;
        private bool _linesOn = true;
        private float _scorchAlpha = -1f;
        private float _skyAlpha = -1f;
        private float _nextStreamerFlicker;

        /// <summary>今の稲妻の明るさ（0〜。テスト用）。</summary>
        public float Brightness { get; private set; }

        /// <summary>今の稲妻の本線の点（テスト用。ワールド座標）。</summary>
        public IReadOnlyList<Vector3> MainChannel => _main;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Material lineMaterial, float top, float ground, float depth, Light flash, Renderer scorch,
            ParticleSystem sparks, ParticleSystem smoke, Renderer skyFlash)
        {
            _skyFlash = skyFlash;
            _view = view;
            _lineMaterial = lineMaterial;
            _top = top;
            _ground = ground;
            _depth = depth;
            _flash = flash;
            _scorch = scorch;
            _sparks = sparks;
            _smoke = smoke;
        }

        private void Awake()
        {
            _mainHalo = CreateLine("MainHalo", MainPoints);
            _mainCore = CreateLine("MainCore", MainPoints);
            for (int i = 0; i < BranchCount; i++)
            {
                _branches[i] = new Vector3[BranchPoints];
                _branchHalo.Add(CreateLine($"BranchHalo{i + 1}", BranchPoints));
                _branchCore.Add(CreateLine($"BranchCore{i + 1}", BranchPoints));
            }
            for (int i = 0; i < StreamerCount; i++)
            {
                _streamers.Add(CreateLine($"Streamer{i + 1}", 2));
            }
            _scorchBlock = new MaterialPropertyBlock();
            SetAll(0f);
            if (_flash != null)
            {
                _flash.intensity = 0f;
            }
            SetScorch(0f);
            SetGlow(_skyFlash, ref _skyAlpha, new Color(0.55f, 0.5f, 0.85f), 0f);
        }

        private LineRenderer CreateLine(string name, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.numCapVertices = 2;
            line.numCornerVertices = 1;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private void LateUpdate()
        {
            if (_view == null)
            {
                return;
            }
            bool active = _view.ShowsActive && _view.ActiveElapsedTicks >= 0;
            Vector2 column = _view.ShownColumn;
            float center = (column.x + column.y) * 0.5f;
            float halfWidth = Mathf.Max(0.2f, (column.y - column.x) * 0.5f);
            if (active && !_wasActive)
            {
                Strike(center, halfWidth);
            }
            _wasActive = active;
            float t = Time.time - _strikeTime;
            RenderStrike(t);
            RenderStreamers(_view.ShowsWarning && !active, center, halfWidth);
        }

        // 新しい落雷: 稲妻の道（本線・枝・3 回目にそれる下の道）を決め、地面の物を落ちる所へ動かす。
        private void Strike(float center, float halfWidth)
        {
            _strikeTime = Time.time;
            _emittedStrokes = 0;
            var random = new System.Random(Mathf.RoundToInt(center * 97f) ^ Time.frameCount);
            // 本線: 上から下へ、横へよろめきながら、柱の幅の中（端から少し内側）に収める。
            float limit = halfWidth * 0.8f;
            float x = center + Range(random, -limit, limit) * 0.5f;
            for (int i = 0; i < MainPoints; i++)
            {
                float u = i / (float)(MainPoints - 1);
                float y = Mathf.Lerp(_top, _ground, u);
                if (i > 0)
                {
                    x += Range(random, -1f, 1f) * 0.55f;
                    x = Mathf.Clamp(x, center - limit, center + limit);
                }
                _main[i] = new Vector3(i == MainPoints - 1 ? Mathf.Clamp(x, center - limit * 0.5f, center + limit * 0.5f) : x, y, _depth);
            }
            // 3 回目の再放電は、下の 3 割で別の道へそれる（稲妻が地面の近くで枝分かれして見える）。
            int forkStart = Mathf.RoundToInt(MainPoints * 0.7f);
            System.Array.Copy(_main, _fork, MainPoints);
            float fx = _main[forkStart].x;
            for (int i = forkStart + 1; i < MainPoints; i++)
            {
                fx = Mathf.Clamp(fx + Range(random, -1f, 1f) * 0.6f, center - limit, center + limit);
                _fork[i] = new Vector3(fx, _main[i].y, _depth);
            }
            // 枝: 本線の上 7 割から外へ斜め下へ伸びて、空中で消える（地面には届かない）。
            for (int b = 0; b < BranchCount; b++)
            {
                int from = random.Next(2, Mathf.RoundToInt(MainPoints * 0.7f));
                float side = random.Next(2) == 0 ? -1f : 1f;
                float length = Range(random, 2.5f, 6f);
                float angle = Range(random, 25f, 60f) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Sin(angle) * side, -Mathf.Cos(angle));
                Vector3 p = _main[from];
                for (int k = 0; k < BranchPoints; k++)
                {
                    _branches[b][k] = p;
                    float step = length / (BranchPoints - 1);
                    var jitter = new Vector2(-direction.y, direction.x) * Range(random, -1f, 1f) * step * 0.7f;
                    p += new Vector3(direction.x * step + jitter.x, direction.y * step + jitter.y, 0f);
                }
            }
            Vector3 ground = new Vector3(_main[MainPoints - 1].x, _ground, 0f);
            foreach (Component c in new Component[] { _flash, _scorch, _sparks, _smoke })
            {
                if (c != null)
                {
                    Vector3 position = c.transform.position;
                    position.x = ground.x;
                    c.transform.position = position;
                }
            }
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        // 一撃ごとの明るさ（すぐ光って速く消える）と、通り道の残光。
        private void RenderStrike(float t)
        {
            float flash = 0f;
            int stroke = -1;
            for (int i = 0; i < StrokeTimes.Length; i++)
            {
                if (t >= StrokeTimes[i])
                {
                    flash += StrokePeaks[i] * Mathf.Exp(-(t - StrokeTimes[i]) / 0.035f);
                    stroke = i;
                }
            }
            float afterglow = t >= 0f ? 0.22f * Mathf.Exp(-t / 0.25f) : 0f;
            float brightness = Mathf.Min(1.3f, flash) + afterglow;
            Brightness = brightness;
            // 一撃ごとに、地面に火花と煙。
            while (stroke >= 0 && _emittedStrokes <= stroke && _emittedStrokes < StrokeTimes.Length && t < 1f)
            {
                if (_sparks != null)
                {
                    _sparks.Emit(_emittedStrokes == 0 ? 60 : 22);
                }
                if (_smoke != null)
                {
                    _smoke.Emit(_emittedStrokes == 0 ? 10 : 3);
                }
                _emittedStrokes++;
            }
            if (brightness < 0.01f)
            {
                if (_linesOn)
                {
                    SetAll(0f);
                    _linesOn = false;
                    if (_flash != null)
                    {
                        _flash.intensity = 0f;
                    }
                    SetGlow(_skyFlash, ref _skyAlpha, new Color(0.55f, 0.5f, 0.85f), 0f);
                }
                SetScorch(t < 3f ? 1f - t / 3f : 0f);
                return;
            }
            _linesOn = true;
            Vector3[] path = stroke >= 2 ? _fork : _main;
            SetLine(_mainCore, path, 0.24f, 0.14f, CoreColor, brightness);
            SetLine(_mainHalo, path, 1.7f, 0.9f, HaloColor, brightness * 0.5f);
            SetGlow(_skyFlash, ref _skyAlpha, new Color(0.55f, 0.5f, 0.85f), Mathf.Clamp01(flash) * 0.5f);
            // 枝は最初の一撃だけ（再放電は枝分かれしない）。
            float branch = t >= 0f ? Mathf.Exp(-t / 0.05f) : 0f;
            for (int b = 0; b < BranchCount; b++)
            {
                SetLine(_branchCore[b], _branches[b], 0.12f, 0.02f, CoreColor, branch);
                SetLine(_branchHalo[b], _branches[b], 0.9f, 0.2f, HaloColor, branch * 0.4f);
            }
            if (_flash != null)
            {
                _flash.intensity = brightness * 10f;
            }
            SetScorch(Mathf.Clamp01(1f - t / 3f));
        }

        private static void SetLine(LineRenderer line, Vector3[] points, float startWidth, float endWidth, Color color, float brightness)
        {
            if (brightness < 0.01f)
            {
                line.enabled = false;
                return;
            }
            line.enabled = true;
            line.SetPositions(points);
            line.startWidth = startWidth;
            line.endWidth = endWidth;
            Color c = color * Mathf.Min(brightness, 1.3f);
            c.a = Mathf.Clamp01(brightness);
            line.startColor = line.endColor = c;
        }

        // 予告: 落ちる地点の地面から伸びる短い光の筋（お迎え放電）。細かく明滅する。
        private void RenderStreamers(bool warning, float center, float halfWidth)
        {
            if (!warning)
            {
                foreach (LineRenderer line in _streamers)
                {
                    line.enabled = false;
                }
                return;
            }
            if (Time.time < _nextStreamerFlicker)
            {
                return;
            }
            _nextStreamerFlicker = Time.time + 0.07f;
            for (int i = 0; i < _streamers.Count; i++)
            {
                LineRenderer line = _streamers[i];
                bool on = Random.value < 0.6f;
                line.enabled = on;
                if (!on)
                {
                    continue;
                }
                float x = center + (i - 1) * halfWidth * 0.6f + Random.Range(-0.15f, 0.15f);
                float length = Random.Range(0.4f, 1.6f);
                line.SetPosition(0, new Vector3(x, _ground, _depth));
                line.SetPosition(1, new Vector3(x + Random.Range(-0.25f, 0.25f), _ground + length, _depth));
                line.startWidth = 0.08f;
                line.endWidth = 0.01f;
                Color c = StreamerColor;
                c.a = Random.Range(0.4f, 0.8f);
                line.startColor = line.endColor = c;
            }
        }

        private void SetAll(float alpha)
        {
            foreach (LineRenderer line in GetComponentsInChildren<LineRenderer>(true))
            {
                line.enabled = alpha > 0f;
            }
        }

        private void SetScorch(float alpha)
        {
            SetGlow(_scorch, ref _scorchAlpha, new Color(1f, 0.42f, 0.14f), alpha);
        }

        // 縁のぼけた光の板（Airerage/SoftParticle の _Tint）の濃さ。
        private void SetGlow(Renderer glow, ref float current, Color color, float alpha)
        {
            if (glow == null || Mathf.Abs(alpha - current) < 0.004f)
            {
                return;
            }
            current = alpha;
            glow.enabled = alpha > 0.01f;
            glow.GetPropertyBlock(_scorchBlock);
            color.a = alpha;
            _scorchBlock.SetColor("_Tint", color);
            glow.SetPropertyBlock(_scorchBlock);
        }
    }
}
