using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
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
    /// - 風: 向きへ流れる筋。周期のある風は、吹いている間だけ流れ、吹き始める 1 秒前（予告の長さがあればその長さ）から予告する。
    /// - 周期のある発射台（間欠泉）: 噴いている間だけ噴き出しの見た目を出し、噴く前は予告する。噴いた瞬間に音。
    /// - 予告はどれも、当たる範囲に黄色の斜線と「三角形の中にエクスクラメーションマーク」の記号（WarningOverlay。ユーザーの要望、2026-10-20）。
    /// - ワープゲート: 回る輪と、中へ吸い込まれる輪。出口にも小さな輪。
    /// - 危険（溶岩流・つらら）: 予告の間は当たる柱の全体を予告して警告音（つららは鳴らさない）、噴いている間は柱の見た目と昇る（落ちる）筋。
    /// - 吹雪: 予告の間は凍る所（影にならない所）だけを予告し、画面の上に BLIZZARD の文字と警告音。吹いている間は白い幕と、向きへ流れる
    ///   雪の筋。
    /// 動くギミックは、見た目の子（_moving）を試合のティック数で動かす（この物体は動かさない。ギミックの範囲はこの物体の位置から決まる）。
    /// 線は起動時にこの物体の子として作る。流れる印の動きは Time.time で決める（巻き戻しとは関係ない）。周期・動き・ランダムな位置は、
    /// シミュレーションと同じ試合のティック数と試合の種で決める（表示する試合がなければ、起動からの時間と種 0。メニューの背景など）。
    /// </summary>
    [RequireComponent(typeof(StageZoneAuthoring))]
    public sealed class StageZoneView : MonoBehaviour
    {
        private const int RingSegments = 40;

        /// <summary>周期のある風が吹き始める何秒前から知らせるか（予告の長さを決めていないとき）。</summary>
        private const float GustWarningSeconds = 1f;

        /// <summary>予告の点滅の速さ（1 秒あたり）。</summary>
        private const float WarningBlinkPerSecond = 4f;

        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Color _color = new Color(0.3f, 1f, 0.45f);
        [Tooltip("印が流れる周期（秒）")]
        [SerializeField] private float _period = 0.9f;
        [Tooltip("効いている間だけ出す見た目（溶岩の柱、蒸気、吹雪の幕など。なくてもよい）")]
        [SerializeField] private GameObject _activeVisual;
        [Tooltip("予告の間に警告音を鳴らすか（危険と吹雪。つららは鳴らさない。ユーザーの要望、2026-10-20）")]
        [SerializeField] private bool _warningSound = true;
        [Tooltip("動くギミックで、一緒に動かす見た目の親（なくてもよい）")]
        [SerializeField] private Transform _moving;

        private readonly List<LineRenderer> _marks = new List<LineRenderer>();
        private StageZoneAuthoring _zone;
        private LineRenderer _ring;
        private LineRenderer _inner;
        private LineRenderer _exit;
        private Vector2 _direction;
        private StageZone _core;
        private ISimulationFeed _feed;
        private GameAudio _audio;
        private float _nextFeedSearch;
        private int _lastTick = int.MinValue;
        private float _lastTickChangedAt;
        private int _warnedCycle = int.MinValue;
        private int _soundedCycle = int.MinValue;
        private int _blizzardCountdown;
        private GUIStyle _bannerStyle;
        private WarningOverlay _overlay;
        private readonly List<Rect> _warningAreas = new List<Rect>();
        private readonly List<Vector2> _warningIcons = new List<Vector2>();
        private List<Rect> _exposedAreas;
        private List<Vector2> _exposedIcons;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Material lineMaterial, Color color)
        {
            _lineMaterial = lineMaterial;
            _color = color;
        }

        /// <summary>効いている間だけ出す見た目を決める（シーンのビルダー用）。</summary>
        public void ConfigureActiveVisual(GameObject visual)
        {
            _activeVisual = visual;
        }

        /// <summary>予告の間に警告音を鳴らすかを決める（シーンのビルダー用）。</summary>
        public void ConfigureWarningSound(bool sound)
        {
            _warningSound = sound;
        }

        /// <summary>予告の表示（周期のあるギミックだけ。テスト用）。</summary>
        public WarningOverlay Warning => _overlay;

        /// <summary>予告の間に警告音を鳴らすか（テスト用）。</summary>
        public bool WarningSound => _warningSound;

        /// <summary>動くギミックで、一緒に動かす見た目の親を決める（シーンのビルダー用）。</summary>
        public void ConfigureMoving(Transform moving)
        {
            _moving = moving;
        }

        /// <summary>今、効いているように見せているか（テスト用。周期のないギミックでは常に true）。</summary>
        public bool ShowsActive { get; private set; } = true;

        /// <summary>今、予告を見せているか（テスト用）。</summary>
        public bool ShowsWarning { get; private set; }

        /// <summary>見た目の今の中心（動くギミックは動いた位置。テスト用）。</summary>
        public Vector3 ShownCenter { get; private set; }

        /// <summary>危険の柱の今の横の範囲（予告か噴いている間。テスト用）。</summary>
        public Vector2 ShownColumn { get; private set; }

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
                    _ring = CreateBox("Edge", 0.12f);
                    _inner = CreateBox("InnerEdge", 0.05f);
                    int motes = Mathf.Clamp(Mathf.RoundToInt(_zone.Size.x * _zone.Size.y / 6f), 10, 48);
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
                case StageZoneKind.Hazard:
                    _ring = CreateBox("Outline", 0.14f);
                    for (int i = 0; i < 8; i++)
                    {
                        _marks.Add(CreateLine($"Streak{i + 1}", 2, 0.16f));
                    }
                    break;
                case StageZoneKind.Blizzard:
                    _ring = CreateBox("Outline", 0.18f);
                    int flakes = Mathf.Clamp(Mathf.RoundToInt(_zone.Size.x * _zone.Size.y / 40f), 12, 60);
                    for (int i = 0; i < flakes; i++)
                    {
                        _marks.Add(CreateLine($"Snow{i + 1}", 2, 0.1f));
                    }
                    break;
            }
            try
            {
                _core = _zone.ToZone();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{name}: ギミックの設定が不正です: {e.Message}", this);
                enabled = false;
                return;
            }
            if (_core.Period > 0 && _lineMaterial != null)
            {
                // 予告の表示（黄色の斜線と警告の記号。吹雪はキャラクターの前に、ほかはギミックの見た目の前に出す）。
                var overlay = new GameObject("Warning");
                overlay.transform.SetParent(transform, false);
                _overlay = overlay.AddComponent<WarningOverlay>();
                _overlay.Initialize(_lineMaterial, _zone.Kind == StageZoneKind.Blizzard ? -2.6f : -0.7f);
            }
        }

        // 予告の表示を出す（ShowsWarning も合わせる）。
        private void ShowWarning(bool warning)
        {
            ShowsWarning = warning;
            if (_overlay == null)
            {
                return;
            }
            if (!warning)
            {
                _overlay.Hide();
                return;
            }
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * 1.6f));
            _overlay.Show(_warningAreas, _warningIcons, pulse);
        }

        // 範囲全体を予告する（記号は真ん中）。
        private void WarnWholeZone(Vector3 center, Vector2 half)
        {
            _warningAreas.Clear();
            _warningIcons.Clear();
            _warningAreas.Add(new Rect(center.x - half.x, center.y - half.y, half.x * 2f, half.y * 2f));
            _warningIcons.Add(new Vector2(center.x, center.y));
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
            int tick = CurrentTick();
            Vector3 center = transform.position;
            if (_core.Moves)
            {
                System.Numerics.Vector2 travel = _core.Travel;
                var offset = new Vector3(travel.X, travel.Y, 0f) * _core.OffsetFraction(tick);
                center += offset;
                if (_moving != null)
                {
                    _moving.localPosition = offset;
                }
            }
            ShownCenter = center;
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
                    RenderLauncher(center, half, time, tick);
                    break;
                case StageZoneKind.Bumper:
                    RenderBumper(center, half, time);
                    break;
                case StageZoneKind.Gust:
                    RenderGust(center, half, time, tick);
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
                case StageZoneKind.Hazard:
                    RenderHazard(center, half, time, tick);
                    break;
                case StageZoneKind.Blizzard:
                    RenderBlizzard(center, half, time, tick);
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

        // 明るい枠と、向きへ速く流れる「^」。周期のある発射台（間欠泉）は、噴いている間だけ流れ、噴く前は枠を点滅させる。
        private void RenderLauncher(Vector3 center, Vector2 half, float time, int tick)
        {
            float strength = PeriodicStrength(tick, out bool warning, out int cycle);
            SetActiveVisual(strength >= 1f);
            if (strength >= 1f && _core.Period > 0 && cycle != _soundedCycle)
            {
                _soundedCycle = cycle;
                PlayInMatch(SoundId.Geyser, 0.7f);
            }
            if (warning)
            {
                WarnWholeZone(center, half);
            }
            ShowWarning(warning);
            float frameAlpha = _core.Period <= 0 ? 1f : 0.25f + 0.75f * strength;
            Box(_ring, center, half, 1f + 0.05f * Mathf.Sin(time * Mathf.PI * 4f), frameAlpha);
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            float width = Mathf.Min(Mathf.Max(half.x, half.y) * 0.9f, 1.1f);
            Vector2 origin = new Vector2(center.x, center.y);
            float speed = _core.Period > 0 ? 4f : 2f;
            for (int i = 0; i < _marks.Count; i++)
            {
                float phase = Mathf.Repeat(time * speed + i / (float)_marks.Count, 1f);
                Vector2 tip = origin + _direction * (0.4f + phase * 3.5f);
                Chevron(_marks[i], tip, _direction, side, width, 0.5f, Fade(phase) * (warning ? 0f : strength));
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
        private void RenderGust(Vector3 center, Vector2 half, float time, int tick)
        {
            float strength = PeriodicStrength(tick, out bool warning, out _);
            SetActiveVisual(strength >= 1f);
            if (warning)
            {
                WarnWholeZone(center, half);
            }
            ShowWarning(warning);
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

        // 周期のあるギミックの見た目の強さ（0〜1）。効いている間は 1、止んでいる間は 0、効き始める前は点滅する（warning）。
        // cycle は効いている周期の番号（予告の間は次の周期の番号）。
        private float PeriodicStrength(int tick, out bool warning, out int cycle)
        {
            warning = false;
            if (_core.Period <= 0)
            {
                cycle = 0;
                return 1f;
            }
            _core.Cycle(tick, CurrentSeed(), out int index, out int start, out int next);
            if (index >= 0 && tick - start < _core.ActiveTicks)
            {
                cycle = index;
                return 1f;
            }
            cycle = index + 1;
            int untilActive = next - tick;
            int warningTicks = _core.Timing.WarningTicks > 0 ? _core.Timing.WarningTicks : SimTime.SecondsToTicks(GustWarningSeconds);
            if (untilActive <= warningTicks)
            {
                // 予告の間は、流れる印を止めて、黄色の斜線と警告の記号を出す（ShowWarning）。
                warning = true;
                return 0f;
            }
            return 0f;
        }

        // 予告の点滅（表示の時間で決める）。
        private static bool Blink()
        {
            return Mathf.Repeat(Time.unscaledTime * WarningBlinkPerSecond, 1f) < 0.55f;
        }

        private void SetActiveVisual(bool active)
        {
            ShowsActive = active;
            if (_activeVisual != null && _activeVisual.activeSelf != active)
            {
                _activeVisual.SetActive(active);
            }
        }


        // 予告の間は当たる柱の全体に黄色の斜線と警告の記号（予告の始まりに警告音。つららは鳴らさない）、噴いている間は柱の見た目と、
        // 向きへ流れる太い筋。
        private void RenderHazard(Vector3 center, Vector2 half, float time, int tick)
        {
            float strength = PeriodicStrength(tick, out bool warning, out int cycle);
            bool active = strength >= 1f;
            SetActiveVisual(active);
            if (warning && cycle != _warnedCycle)
            {
                _warnedCycle = cycle;
                if (_warningSound)
                {
                    PlayInMatch(SoundId.HazardWarning, 0.75f);
                }
            }
            if (!active && !warning)
            {
                ShowWarning(false);
                SetColor(_ring, 0f);
                foreach (LineRenderer line in _marks)
                {
                    SetColor(line, 0f);
                }
                return;
            }
            _core.HazardColumn(cycle, CurrentSeed(), out float minX, out float maxX);
            ShownColumn = new Vector2(minX, maxX);
            float columnCenter = (minX + maxX) * 0.5f;
            var columnHalf = new Vector2((maxX - minX) * 0.5f, half.y);
            var column = new Vector3(columnCenter, center.y, 0f);
            PlaceOnColumn(_activeVisual, columnCenter, maxX - minX);
            if (warning)
            {
                WarnWholeZone(column, columnHalf);
            }
            ShowWarning(warning);
            Box(_ring, column, columnHalf, 1f, warning ? 0f : 0.9f);
            // 上向きなら下から上へ（溶岩流）、下向きなら上から下へ（つらら）流れる。
            Vector2 flow = _direction;
            float height = half.y * 2f;
            for (int i = 0; i < _marks.Count; i++)
            {
                float u = (i + 0.5f) / _marks.Count;
                float phase = Mathf.Repeat(time * 2.2f + i * 0.37f, 1f);
                float x = minX + u * (maxX - minX);
                float y = flow.y >= 0f ? center.y - half.y + phase * height : center.y + half.y - phase * height;
                var from = new Vector2(x, y);
                Vector2 to = from + flow * Mathf.Min(2.4f, half.y * 0.4f);
                _marks[i].SetPosition(0, ToWorld(new Vector2(from.x, Mathf.Clamp(from.y, center.y - half.y, center.y + half.y))));
                _marks[i].SetPosition(1, ToWorld(new Vector2(to.x, Mathf.Clamp(to.y, center.y - half.y, center.y + half.y))));
                SetColor(_marks[i], active ? Fade(phase) : 0f);
            }
        }

        // 柱の見た目（幅 1 の板として作ってある）を、柱の横の位置と幅に合わせる。
        private void PlaceOnColumn(GameObject visual, float centerX, float width)
        {
            if (visual == null)
            {
                return;
            }
            Transform t = visual.transform;
            Vector3 position = t.position;
            position.x = centerX;
            t.position = position;
            Vector3 scale = t.localScale;
            scale.x = width;
            t.localScale = scale;
        }

        // 予告の間は、凍る所（影にならない所）だけに黄色の斜線と警告の記号（安全な所には出さない。ユーザーの要望、2026-10-20）。
        // 吹いている間は白い幕と、向きへ流れる雪の筋。
        private void RenderBlizzard(Vector3 center, Vector2 half, float time, int tick)
        {
            float strength = PeriodicStrength(tick, out bool warning, out int cycle);
            bool active = strength >= 1f;
            SetActiveVisual(active);
            _blizzardCountdown = 0;
            if (warning)
            {
                _core.Cycle(tick, CurrentSeed(), out _, out _, out int next);
                _blizzardCountdown = Mathf.Max(1, Mathf.CeilToInt((next - tick) / (float)SimTime.TicksPerSecond));
                if (cycle != _warnedCycle)
                {
                    _warnedCycle = cycle;
                    if (_warningSound)
                    {
                        PlayInMatch(SoundId.BlizzardWarning, 0.8f);
                    }
                }
                ComputeExposedAreas();
                _warningAreas.Clear();
                _warningAreas.AddRange(_exposedAreas);
                _warningIcons.Clear();
                _warningIcons.AddRange(_exposedIcons);
            }
            ShowWarning(warning);
            Box(_ring, center, half, 1f, active ? 0.5f : 0f);
            Vector2 side = new Vector2(_direction.y, -_direction.x);
            for (int i = 0; i < _marks.Count; i++)
            {
                float u = Mathf.Repeat(i * 0.618f, 1f);
                float phase = Mathf.Repeat(time * 1.6f + i * 0.29f, 1f);
                // 範囲を斜めに横切る雪（風下へ流れながら少し落ちる）。
                Vector2 start = new Vector2(center.x, center.y) - _direction * (half.x + half.y) * 0.5f;
                Vector2 point = start + _direction * phase * (half.x + half.y) + side * (u - 0.5f) * (half.x + half.y) * 1.2f;
                point.y -= phase * 2f;
                point = Vector2.Max(new Vector2(center.x - half.x, center.y - half.y), Vector2.Min(new Vector2(center.x + half.x, center.y + half.y), point));
                _marks[i].SetPosition(0, ToWorld(point));
                _marks[i].SetPosition(1, ToWorld(point + _direction * 1.4f + Vector2.down * 0.2f));
                SetColor(_marks[i], active ? Fade(phase) : 0f);
            }
        }

        /// <summary>吹雪の範囲を区切る升目の大きさ（m）と、警告の記号を置く間隔（m）。</summary>
        private const float ExposureCell = 1f;
        private const float ExposureIconSpacing = 16f;

        // 吹雪で凍る所（範囲の中で、地形の中でなく、風上に地形がない所。StageZones.Sheltered と同じ決め方）を、横に続く升目を
        // まとめた長方形にする（1 回だけ。地形はシーンの箱から作る）。
        private void ComputeExposedAreas()
        {
            if (_exposedAreas != null)
            {
                return;
            }
            _exposedAreas = new List<Rect>();
            _exposedIcons = new List<Vector2>();
            var errors = new List<string>();
            BoxTerrain terrain = SceneTerrainBuilder.Build(1 << 0, errors);
            var halfCell = new System.Numerics.Vector3(ExposureCell * 0.5f, ExposureCell * 0.5f, 0.5f);
            float limit = _core.Limit > 0f ? _core.Limit : StageZones.BlizzardShelterDistance;
            int columns = Mathf.Max(1, Mathf.FloorToInt((_core.MaxX - _core.MinX) / ExposureCell));
            int rows = Mathf.Max(1, Mathf.FloorToInt((_core.MaxY - _core.MinY) / ExposureCell));
            int iconStep = Mathf.Max(1, Mathf.RoundToInt(ExposureIconSpacing / ExposureCell));
            for (int row = 0; row < rows; row++)
            {
                float y = _core.MinY + (row + 0.5f) * ExposureCell;
                int runStart = -1;
                for (int column = 0; column <= columns; column++)
                {
                    bool exposed = false;
                    if (column < columns)
                    {
                        var point = new System.Numerics.Vector3(_core.MinX + (column + 0.5f) * ExposureCell, y, 0f);
                        exposed = !terrain.IsOverlapping(point, halfCell) && !StageZones.Sheltered(point, _core.Direction, terrain, limit);
                        if (exposed && row % iconStep == iconStep / 2 && column % iconStep == iconStep / 2)
                        {
                            _exposedIcons.Add(new Vector2(point.X, point.Y));
                        }
                    }
                    if (exposed && runStart < 0)
                    {
                        runStart = column;
                    }
                    else if (!exposed && runStart >= 0)
                    {
                        _exposedAreas.Add(new Rect(_core.MinX + runStart * ExposureCell, y - ExposureCell * 0.5f,
                            (column - runStart) * ExposureCell, ExposureCell));
                        runStart = -1;
                    }
                }
            }
            foreach (string error in errors)
            {
                Debug.LogWarning($"{name}: 吹雪の予告の範囲を作るときに除いた地形: {error}", this);
            }
        }

        /// <summary>吹雪で凍る所の長方形（テスト用。まだなければ作る）。</summary>
        public IReadOnlyList<Rect> ExposedAreas
        {
            get
            {
                ComputeExposedAreas();
                return _exposedAreas;
            }
        }

        // 吹雪の予告の文字（画面の上）。表示する試合が進んでいる間だけ。
        private void OnGUI()
        {
            if (_zone == null || _zone.Kind != StageZoneKind.Blizzard || _blizzardCountdown <= 0 || !MatchRunning())
            {
                return;
            }
            if (_bannerStyle == null)
            {
                _bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(Screen.height * 0.04f),
                    fontStyle = FontStyle.Bold,
                };
            }
            string text = $"BLIZZARD IN {_blizzardCountdown} — TAKE COVER!";
            var rect = new Rect(0f, Screen.height * 0.2f, Screen.width, Screen.height * 0.08f);
            Color color = GUI.color;
            // 帯と、文字の左右に警告の記号（予告の表示と同じ黄色。ユーザーの要望、2026-10-20）。
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            float iconSize = rect.height * 0.9f;
            Vector2 textSize = _bannerStyle.CalcSize(new GUIContent(text));
            if (_overlay != null && _overlay.IconTexture != null)
            {
                GUI.color = Color.white;
                float y = rect.y + (rect.height - iconSize) * 0.5f;
                GUI.DrawTexture(new Rect(rect.center.x - textSize.x * 0.5f - iconSize * 1.2f, y, iconSize, iconSize), _overlay.IconTexture);
                GUI.DrawTexture(new Rect(rect.center.x + textSize.x * 0.5f + iconSize * 0.2f, y, iconSize, iconSize), _overlay.IconTexture);
            }
            GUI.color = Blink() ? new Color(1f, 0.88f, 0.1f) : new Color(1f, 0.7f, 0.05f);
            GUI.Label(rect, text, _bannerStyle);
            GUI.color = color;
        }

        // 表示する試合があり、ティックが進んでいるか（試合が終わって止まっている間は予告の文字と音を出さない）。
        private bool MatchRunning()
        {
            return _feed != null && _feed.CharacterCount > 0 && Time.unscaledTime - _lastTickChangedAt < 0.5f;
        }

        // 試合の中でだけ音を鳴らす（メニューの背景では鳴らさない）。
        private void PlayInMatch(SoundId id, float volume)
        {
            if (!MatchRunning())
            {
                return;
            }
            if (_audio == null)
            {
                _audio = FindFirstObjectByType<GameAudio>();
            }
            if (_audio != null)
            {
                _audio.Play(id, volume);
            }
        }

        private int CurrentSeed()
        {
            return _feed != null && _feed.CharacterCount > 0 ? _feed.Rules.Seed : 0;
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
                _audio = null;
            }
            int tick = _feed != null && _feed.CharacterCount > 0 ? _feed.CurrentTick : Mathf.FloorToInt(Time.time * SimTime.TicksPerSecond);
            if (tick != _lastTick)
            {
                _lastTick = tick;
                _lastTickChangedAt = Time.unscaledTime;
            }
            return tick;
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

        // 脈打つ二重の枠と、ゆっくり昇りながら揺れる粒（見やすく。ユーザーの要望、2026-10-19）。
        private void RenderLowGravity(Vector3 center, Vector2 half, float time)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * Mathf.PI * 0.8f);
            Box(_ring, center, half, 1f, 0.6f + 0.3f * pulse);
            Box(_inner, center, new Vector2(Mathf.Max(0.1f, half.x - 0.35f), Mathf.Max(0.1f, half.y - 0.35f)), 1f, 0.25f + 0.25f * (1f - pulse));
            for (int i = 0; i < _marks.Count; i++)
            {
                float u = Mathf.Repeat(i * 0.618f, 1f);
                float phase = Mathf.Repeat(time * 0.12f + i * 0.29f, 1f);
                float x = center.x - half.x + u * half.x * 2f + Mathf.Sin(time + i) * 0.3f;
                float y = center.y - half.y + phase * half.y * 2f;
                _marks[i].SetPosition(0, new Vector3(x, y, 0f));
                _marks[i].SetPosition(1, new Vector3(x, y + 0.14f, 0f));
                SetColor(_marks[i], Fade(phase));
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
