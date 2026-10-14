using System.Collections.Generic;
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
    /// 線は起動時にこの物体の子として作る。動きは Time.time で決める（巻き戻しとは関係ない）。
    /// </summary>
    [RequireComponent(typeof(StageZoneAuthoring))]
    public sealed class StageZoneView : MonoBehaviour
    {
        private const int RingSegments = 40;

        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Color _color = new Color(0.3f, 1f, 0.45f);
        [Tooltip("印が流れる周期（秒）")]
        [SerializeField] private float _period = 0.9f;

        private readonly List<LineRenderer> _marks = new List<LineRenderer>();
        private StageZoneAuthoring _zone;
        private LineRenderer _ring;
        private Vector2 _direction;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Material lineMaterial, Color color)
        {
            _lineMaterial = lineMaterial;
            _color = color;
        }

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
            }
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
            }
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
