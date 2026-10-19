using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ギミック（StageZoneAuthoring）の動く見た目（表示だけ。シミュレーションには関係しない）。
    /// - ジャンプ台: 向きへ流れる「^」の印。
    /// - 上昇気流: 下から上へ流れる筋。
    /// - 加速リング: 輪と、中心から両方向へ流れる「&lt; &gt;」の印（両方向に効く）。
    /// - スピードレーン: 縁の枠と、向きへ流れる「&gt;」の列。発射台: 明るい枠と、向きへ速く流れる「^」。
    /// - バンパー: 脈打つ二重の枠。氷: 表面の線と、横切るきらめき。低重力: 薄い枠と、ゆっくり昇る粒。
    /// - 風: 向きへ流れる筋。周期のある風は、吹いている間だけ流れ、吹き始める 1 秒前から点滅して知らせる（試合のティック数で決める）。
    /// - ワープゲート: 回る輪と、中へ吸い込まれる輪。出口にも小さな輪。
    /// 線は起動時にこの物体の子として作る。動きは Time.time で決める（巻き戻しとは関係ない。風が吹いているかだけは試合のティック数）。
    /// </summary>
    [RequireComponent(typeof(StageZoneAuthoring))]
    public sealed class StageZoneView : MonoBehaviour
    {
        private const int RingSegments = 40;

        /// <summary>周期のある風が吹き始める何秒前から知らせるか。</summary>
        private const float GustWarningSeconds = 1f;

        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Color _color = new Color(0.3f, 1f, 0.45f);
        [Tooltip("印が流れる周期（秒）")]
        [SerializeField] private float _period = 0.9f;
        [Tooltip("風が吹いている間だけ出す見た目（溶岩の柱、蒸気など。なくてもよい）")]
        [SerializeField] private GameObject _activeVisual;

        private readonly List<LineRenderer> _marks = new List<LineRenderer>();
        private StageZoneAuthoring _zone;
        private LineRenderer _ring;
        private LineRenderer _inner;
        private LineRenderer _exit;
        private Vector2 _direction;
        private StageZone _core;
        private ISimulationFeed _feed;
        private float _nextFeedSearch;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Material lineMaterial, Color color)
        {
            _lineMaterial = lineMaterial;
            _color = color;
        }

        /// <summary>風が吹いている間だけ出す見た目を決める（シーンのビルダー用）。</summary>
        public void ConfigureActiveVisual(GameObject visual)
        {
            _activeVisual = visual;
        }

        /// <summary>今、風が吹いているように見せているか（テスト用。風でなければ常に true）。</summary>
        public bool ShowsActive { get; private set; } = true;

        private void Awake()
        {
            _zone = GetComponent<StageZoneAuthoring>();
            _direction = _zone.Kind == StageZoneKind.Updraft || _zone.Direction == Vector2.zero ? Vector2.up : _zone.Direction.normalized;
            switch (_zone.Kind)
            {
                case StageZoneKind.JumpPad:
                    for (int i = 0; i < 3; i++)
                    {
                        _marks.Add(CreateLine($"Chevron{i + 1}", 3, 0.12f));
                    }
                    break;
                case StageZoneKind.Updraft:
                    int streaks = Mathf.Clamp(Mathf.RoundToInt(_zone.Size.x / 0.8f), 2, 8);
                    for (int i = 0; i < streaks * 2; i++)
                    {
                        _marks.Add(CreateLine($"Streak{i + 1}", 2, 0.07f));
                    }
                    break;
                case StageZoneKind.BoostRing:
                    _ring = CreateLine("Ring", RingSegments, 0.16f);
                    _ring.loop = true;
                    for (int i = 0; i < 4; i++)
                    {
                        _marks.Add(CreateLine($"Arrow{i + 1}", 3, 0.14f));
                    }
                    break;
                case StageZoneKind.SpeedLane:
                    _ring = CreateBox("Edge", 0.06f);
                    int chevrons = Mathf.Clamp(Mathf.RoundToInt(Extent(_direction) / 1.1f), 3, 14);
                    for (int i = 0; i < chevrons; i++)
                    {
                        _marks.Add(CreateLine($"Chevron{i + 1}", 3, 0.1f));
                    }
                    break;
                case StageZoneKind.Launcher:
                    _ring = CreateBox("Frame", 0.12f);
                    for (int i = 0; i < 4; i++)
                    {
                        _marks.Add(CreateLine($"Chevron{i + 1}", 3, 0.16f));
                    }
                    break;
                case StageZoneKind.Bumper:
                    _ring = CreateBox("Outer", 0.14f);
                    _inner = CreateBox("Inner", 0.07f);
                    break;
                case StageZoneKind.Gust:
                    int gustStreaks = Mathf.Clamp(Mathf.RoundToInt(Extent(new Vector2(_direction.y, -_direction.x)) / 0.9f), 2, 10);
                    for (int i = 0; i < gustStreaks * 2; i++)
                    {
                        _marks.Add(CreateLine($"Streak{i + 1}", 2, 0.07f));
                    }
                    break;
                case StageZoneKind.Ice:
                    _ring = CreateLine("Surface", 2, 0.06f);
                    for (int i = 0; i < 3; i++)
                    {
                        _marks.Add(CreateLine($"Glint{i + 1}", 2, 0.08f));
                    }
                    break;
                case StageZoneKind.LowGravity:
                    _ring = CreateBox("Edge", 0.05f);
                    int motes = Mathf.Clamp(Mathf.RoundToInt(_zone.Size.x * _zone.Size.y / 12f), 6, 28);
                    for (int i = 0; i < motes; i++)
                    {
                        _marks.Add(CreateLine($"Mote{i + 1}", 2, 0.12f));
                    }
                    break;
                case StageZoneKind.Warp:
                    _ring = CreateLine("Ring", RingSegments, 0.18f);
                    _ring.loop = true;
                    for (int i = 0; i < 3; i++)
                    {
                        LineRenderer swirl = CreateLine($"Swirl{i + 1}", RingSegments, 0.08f);
                        swirl.loop = true;
                        _marks.Add(swirl);
                    }
                    _exit = CreateLine("Exit", RingSegments, 0.08f);
                    _exit.loop = true;
                    break;
            }
            if (_zone.Kind == StageZoneKind.Gust)
            {
                try
                {
                    _core = _zone.ToZone();
                }
                catch (ArgumentException e)
                {
                    Debug.LogError($"{name}: 風の設定が不正です: {e.Message}", this);
                    enabled = false;
                }
            }
        }

        // 範囲の、向き direction に沿った長さ。
        private float Extent(Vector2 direction)
        {
            return Mathf.Abs(direction.x) * _zone.Size.x + Mathf.Abs(direction.y) * _zone.Size.y;
        }

        private LineRenderer CreateBox(string name, float width)
        {
            LineRenderer line = CreateLine(name, 4, width);
            line.loop = true;
            return line;
        }

        private LineRenderer CreateLine(string name, int points, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void Update()
        {
            Vector3 center = transform.position;
            Vector2 half = _zone.Size * 0.5f;
            float time = Time.time / Mathf.Max(0.05f, _period);
            switch (_zone.Kind)
            {
                case StageZoneKind.JumpPad:
                    RenderPad(center, half, time);
                    break;
                case StageZoneKind.Updraft:
                    RenderUpdraft(center, half, time);
                    break;
                case StageZoneKind.BoostRing:
                    RenderRing(center, half, time);
                    break;
                case StageZoneKind.SpeedLane:
                    RenderLane(center, half, time);
                    break;
                case StageZoneKind.Launcher:
                    RenderLauncher(center, half, time);
                    break;
                case StageZoneKind.Bumper:
                    RenderBumper(center, half, time);
                    break;
                case StageZoneKind.Gust:
                    RenderGust(center, half, time);
                    break;
                case StageZoneKind.Ice:
                    RenderIce(center, half, time);
                    break;
                case StageZoneKind.LowGravity:
                    RenderLowGravity(center, half, time);
                    break;
                case StageZoneKind.Warp:
                    RenderWarp(center, half, time);
                    break;
            }
        }

        // 縁の枠と、向きへ流れる「>」の列。
        private void RenderLane(Vector3 center, Vector2 half, float time)
        {
            Box(_ring, center, half, 1f, 0.55f);
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float length = Extent(_direction);
            float width = Mathf.Min(Extent(side) * 0.8f, 0.8f);
            Vector2 start = new Vector2(center.x, center.y) - _direction * length * 0.5f;
            for (int i = 0; i < _marks.Count; i++)
            {
                float phase = Mathf.Repeat(time * 1.4f + i / (float)_marks.Count, 1f);
                Vector2 tip = start + _direction * (0.3f + phase * (length - 0.3f));
                Chevron(_marks[i], tip, _direction, side, width, Mathf.Min(width * 0.6f, 0.35f), Fade(phase) * 0.9f);
            }
        }

        // 明るい枠と、向きへ速く流れる「^」。
        private void RenderLauncher(Vector3 center, Vector2 half, float time)
        {
            Box(_ring, center, half, 1f + 0.05f * Mathf.Sin(time * Mathf.PI * 4f), 1f);
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float width = Mathf.Min(Mathf.Max(half.x, half.y) * 0.9f, 1.1f);
            Vector2 origin = new Vector2(center.x, center.y);
            for (int i = 0; i < _marks.Count; i++)
            {
                float phase = Mathf.Repeat(time * 2f + i / (float)_marks.Count, 1f);
                Vector2 tip = origin + _direction * (0.4f + phase * 3.5f);
                Chevron(_marks[i], tip, _direction, side, width, 0.5f, Fade(phase));
            }
        }

        // 脈打つ二重の枠。
        private void RenderBumper(Vector3 center, Vector2 half, float time)
        {
            float pulse = Mathf.Sin(time * Mathf.PI * 2f);
            Box(_ring, center, half, 1f + 0.06f * pulse, 1f);
            Box(_inner, center, half, 0.7f - 0.08f * pulse, 0.5f + 0.3f * pulse);
        }

        // 向きへ流れる筋。周期のある風は、吹いている間だけ流れ、吹き始める前に点滅して知らせる。
        private void RenderGust(Vector3 center, Vector2 half, float time)
        {
            float strength = GustStrength(out bool warning);
            ShowsActive = strength >= 1f;
            if (_activeVisual != null && _activeVisual.activeSelf != ShowsActive)
            {
                _activeVisual.SetActive(ShowsActive);
            }
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float length = Extent(_direction);
            float across = Extent(side);
            int columns = _marks.Count / 2;
            var min = new Vector2(center.x - half.x, center.y - half.y);
            var max = new Vector2(center.x + half.x, center.y + half.y);
            for (int i = 0; i < _marks.Count; i++)
            {
                int column = i % columns;
                float offset = ((column + 0.5f) / columns - 0.5f) * across;
                float phase = Mathf.Repeat(time * 0.9f + i * 0.37f, 1f);
                Vector2 basePoint = new Vector2(center.x, center.y) + side * offset;
                float streak = Mathf.Min(1.6f, length * 0.25f);
                Vector2 from = basePoint + _direction * ((phase - 0.5f) * length);
                Vector2 to = from + _direction * streak;
                if (warning)
                {
                    // 止まった短い筋を点滅させる。
                    from = basePoint + _direction * (((i * 0.37f) % 1f - 0.5f) * length);
                    to = from + _direction * 0.4f;
                }
                LineRenderer line = _marks[i];
                line.SetPosition(0, ToWorld(Vector2.Max(min, Vector2.Min(max, from))));
                line.SetPosition(1, ToWorld(Vector2.Max(min, Vector2.Min(max, to))));
                SetColor(line, (warning ? 1f : Fade(phase)) * 0.65f * strength);
            }
        }

        // 風の見た目の強さ（0〜1）。止んでいる間は 0、吹き始める前は点滅する（warning）。
        private float GustStrength(out bool warning)
        {
            warning = false;
            if (_core.Period <= 0)
            {
                return 1f;
            }
            int tick = CurrentTick();
            if (_core.IsActive(tick))
            {
                return 1f;
            }
            int phase = (int)(((long)tick + _core.PhaseTicks) % _core.Period);
            int untilActive = _core.Period - phase;
            if (untilActive <= SimTime.SecondsToTicks(GustWarningSeconds))
            {
                warning = true;
                return (untilActive / 6) % 2 == 0 ? 0.8f : 0.15f;
            }
            return 0f;
        }

        // 試合のティック数（表示する試合が見つからなければ、起動からの時間で代わりにする。メニューの背景など）。
        private int CurrentTick()
        {
            if (_feed == null && Time.unscaledTime >= _nextFeedSearch)
            {
                _nextFeedSearch = Time.unscaledTime + 1f;
                foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (behaviour is ISimulationFeed feed)
                    {
                        _feed = feed;
                        break;
                    }
                }
            }
            if (_feed is MonoBehaviour alive && alive == null)
            {
                // 試合のシーンを閉じた。
                _feed = null;
            }
            return _feed != null && _feed.CharacterCount > 0 ? _feed.CurrentTick : Mathf.FloorToInt(Time.time * SimTime.TicksPerSecond);
        }

        // 表面の線と、横切るきらめき。
        private void RenderIce(Vector3 center, Vector2 half, float time)
        {
            float top = center.y + half.y;
            _ring.SetPosition(0, new Vector3(center.x - half.x, top, 0f));
            _ring.SetPosition(1, new Vector3(center.x + half.x, top, 0f));
            SetColor(_ring, 0.7f);
            for (int i = 0; i < _marks.Count; i++)
            {
                float phase = Mathf.Repeat(time * 0.35f + i * 0.41f, 1f);
                float x = center.x - half.x + phase * half.x * 2f;
                _marks[i].SetPosition(0, new Vector3(x - 0.25f, top - 0.05f, 0f));
                _marks[i].SetPosition(1, new Vector3(x + 0.25f, top + 0.25f, 0f));
                SetColor(_marks[i], Fade(phase));
            }
        }

        // 薄い枠と、ゆっくり昇りながら揺れる粒。
        private void RenderLowGravity(Vector3 center, Vector2 half, float time)
        {
            Box(_ring, center, half, 1f, 0.25f);
            for (int i = 0; i < _marks.Count; i++)
            {
                float u = Mathf.Repeat(i * 0.618f, 1f);
                float phase = Mathf.Repeat(time * 0.12f + i * 0.29f, 1f);
                float x = center.x - half.x + u * half.x * 2f + Mathf.Sin(time + i) * 0.3f;
                float y = center.y - half.y + phase * half.y * 2f;
                _marks[i].SetPosition(0, new Vector3(x, y, 0f));
                _marks[i].SetPosition(1, new Vector3(x, y + 0.12f, 0f));
                SetColor(_marks[i], Fade(phase) * 0.7f);
            }
        }

        // 回る輪と、中へ吸い込まれる輪。出口にも小さな輪。
        private void RenderWarp(Vector3 center, Vector2 half, float time)
        {
            Ellipse(_ring, new Vector2(center.x, center.y), half, time * 2f, 1f, 1f);
            for (int i = 0; i < _marks.Count; i++)
            {
                float phase = Mathf.Repeat(time * 0.8f + i / (float)_marks.Count, 1f);
                Ellipse(_marks[i], new Vector2(center.x, center.y), half, -time * 3f, 1f - phase * 0.85f, Fade(phase) * 0.8f);
            }
            float exitPulse = 0.5f + 0.1f * Mathf.Sin(time * Mathf.PI * 2f);
            Ellipse(_exit, _zone.Target, new Vector2(exitPulse, exitPulse), time, 1f, 0.6f);
        }

        // 楕円（少しゆがませて、回って見えるようにする）。
        private void Ellipse(LineRenderer line, Vector2 center, Vector2 half, float rotation, float scale, float alpha)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                float wobble = 1f + 0.08f * Mathf.Sin(angle * 3f + rotation);
                line.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * half.x * scale * wobble,
                    center.y + Mathf.Sin(angle) * half.y * scale * wobble, 0f));
            }
            SetColor(line, alpha);
        }

        // 範囲の枠（scale 倍の大きさ）。
        private void Box(LineRenderer line, Vector3 center, Vector2 half, float scale, float alpha)
        {
            float x = half.x * scale;
            float y = half.y * scale;
            line.SetPosition(0, new Vector3(center.x - x, center.y - y, 0f));
            line.SetPosition(1, new Vector3(center.x + x, center.y - y, 0f));
            line.SetPosition(2, new Vector3(center.x + x, center.y + y, 0f));
            line.SetPosition(3, new Vector3(center.x - x, center.y + y, 0f));
            SetColor(line, alpha);
        }

        // 台の上から向きへ、3つの「^」が流れて消える。
        private void RenderPad(Vector3 center, Vector2 half, float time)
        {
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float width = Mathf.Min(half.x * 0.8f, 0.9f);
            Vector2 baseCenter = new Vector2(center.x, center.y - half.y);
            for (int i = 0; i < _marks.Count; i++)
            {
                float phase = Mathf.Repeat(time + i / (float)_marks.Count, 1f);
                Vector2 tip = baseCenter + _direction * (0.5f + phase * 2.2f);
                Chevron(_marks[i], tip, _direction, side, width, 0.45f, Fade(phase));
            }
        }

        // 下から上へ流れる筋（間隔をずらして並べる）。
        private void RenderUpdraft(Vector3 center, Vector2 half, float time)
        {
            int columns = _marks.Count / 2;
            float height = half.y * 2f;
            for (int i = 0; i < _marks.Count; i++)
            {
                int column = i % columns;
                float x = center.x - half.x + (column + 0.5f) / columns * half.x * 2f;
                float phase = Mathf.Repeat(time * 0.6f + i * 0.37f, 1f);
                float y = center.y - half.y + phase * height;
                float length = Mathf.Min(1.4f, height * 0.25f);
                LineRenderer line = _marks[i];
                line.SetPosition(0, new Vector3(x, y, 0f));
                line.SetPosition(1, new Vector3(x, Mathf.Min(y + length, center.y + half.y), 0f));
                SetColor(line, Fade(phase) * 0.6f);
            }
        }

        // 輪（範囲に収まる楕円）と、中心から両方向へ流れる「<」「>」。輪は少し脈打つ。
        private void RenderRing(Vector3 center, Vector2 half, float time)
        {
            float pulse = 1f + 0.06f * Mathf.Sin(time * Mathf.PI * 2f);
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                _ring.SetPosition(i, new Vector3(center.x + Mathf.Cos(angle) * half.x * pulse, center.y + Mathf.Sin(angle) * half.y * pulse, 0f));
            }
            SetColor(_ring, 0.9f);

            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float reach = Mathf.Min(half.x, half.y) * 0.8f;
            for (int i = 0; i < _marks.Count; i++)
            {
                // 偶数番は向きへ、奇数番は逆へ、中心から流れる。
                Vector2 forward = i % 2 == 0 ? _direction : -_direction;
                float phase = Mathf.Repeat(time * 1.5f + (i / 2) * 0.5f, 1f);
                Vector2 tip = new Vector2(center.x, center.y) + forward * Mathf.Lerp(reach * 0.3f, reach, phase);
                Chevron(_marks[i], tip, forward, side, reach * 0.6f, reach * 0.45f, Fade(phase));
            }
        }

        private void Chevron(LineRenderer line, Vector2 tip, Vector2 forward, Vector2 side, float width, float depth, float alpha)
        {
            Vector2 back = tip - forward * depth;
            line.SetPosition(0, ToWorld(back + side * width * 0.5f));
            line.SetPosition(1, ToWorld(tip));
            line.SetPosition(2, ToWorld(back - side * width * 0.5f));
            SetColor(line, alpha);
        }

        private void SetColor(LineRenderer line, float alpha)
        {
            Color color = _color;
            color.a *= Mathf.Clamp01(alpha);
            line.startColor = line.endColor = color;
        }

        // 流れ始めと終わりで薄くする。
        private static float Fade(float phase)
        {
            return Mathf.Clamp01(Mathf.Min(phase * 4f, (1f - phase) * 3f));
        }

        private static Vector3 ToWorld(Vector2 point)
        {
            return new Vector3(point.x, point.y, 0f);
        }
    }
}
