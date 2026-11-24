using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワープゲートの渦（宇宙ステーション。ZoneLook.Wormhole。2026-11-24 の要望: 前の紫の楕円の見た目を精巧に）。表示だけ。
    /// - 楕円の渦の面（Airerage/Wormhole: 奥へ続くトンネルの暗さ、内へ巻き込む渦の腕、吸い込まれていく光の粒、明るい縁）と、
    ///   回りながら揺れる外の輪・中へ縮んで吸い込まれていく輪 3 本（前の見た目を引き継ぐ）。
    /// - 動くゲート（いちばん下の救出のゲート）は、動く向きの反対へ撓む: 真ん中ほど後ろへ遅れて帆のようにふくらみ、後ろ側が尾のように
    ///   伸び、前の縁は少し平たくなる。向きが変わる端では、ばねのように行きすぎて揺れ戻る（2026-11-24 の要望で強く残す）。
    /// - 出口には、外へ吐き出す小さな渦（逆回り）。誰かがワープしたとき（CombatState.WarpCount が増えた）は、入った口と出口に波紋。
    /// 当たりは Core の楕円のワープゲート（StageZone.Round。範囲の箱に内接する楕円）で、見た目の渦の楕円と同じ。
    /// </summary>
    public sealed class WormholeView : MonoBehaviour
    {
        /// <summary>波紋が広がり切るまでの秒数。</summary>
        public const float PulseSeconds = 0.7f;

        /// <summary>撓みが最大になる動く速さ（m/s）と、撓みのばねの強さ・減衰（行きすぎて揺れ戻る）。</summary>
        public const float BendSpeed = 11f;
        public const float BendStiffness = 60f;
        public const float BendDamping = 6.5f;

        private const int RingPoints = 64;

        [Tooltip("渦の面（半径 1 の円盤。ゲートの大きさに拡大する）と、出口の渦（なくてもよい）")]
        [SerializeField] private MeshFilter _disc;
        [SerializeField] private MeshFilter _exitDisc;
        [Tooltip("輪の線の材質（光る線）と色")]
        [SerializeField] private Material _lineMaterial;
        [SerializeField] private Color _color = new Color(0.75f, 0.4f, 1f);
        [Tooltip("渦の半分の大きさ（m。当たりの楕円と同じ）と、出口の渦の半径（m）")]
        [SerializeField] private Vector2 _half = new Vector2(1f, 3f);
        [SerializeField] private float _exitRadius = 0.7f;

        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int BendId = Shader.PropertyToID("_Bend");
        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _exitBlock;
        private StageZoneView _zone;
        private Mesh _mesh;
        private Vector3[] _base = new Vector3[0];
        private Vector3[] _deformed = new Vector3[0];
        private LineRenderer _rim;
        private LineRenderer _rimInner;
        private readonly List<LineRenderer> _swirls = new List<LineRenderer>();
        private LineRenderer _exitRing;
        private Vector3 _lastPosition;
        private bool _hasLast;
        private Vector2 _bend;
        private Vector2 _bendVelocity;
        private float _pulse;
        private float _exitPulse;
        private int[] _warpCounts = new int[0];
        private Vector3[] _lastPositions = new Vector3[0];

        /// <summary>今の撓み（動く向きの反対向き。長さが強さ 0〜1 くらい。テスト用）。</summary>
        public Vector2 Bend => _bend;

        /// <summary>波紋を出した回数（テスト用）。</summary>
        public int Pulses { get; private set; }

        /// <summary>出口で波紋を出した回数（テスト用）。</summary>
        public int ExitPulses { get; private set; }

        /// <summary>渦の半分の大きさ（m）。</summary>
        public Vector2 Half => _half;

        /// <summary>渦の面と、出口の渦（なければ null）。</summary>
        public Renderer Mouth => _disc != null ? _disc.GetComponent<Renderer>() : null;
        public Renderer ExitMouth => _exitDisc != null ? _exitDisc.GetComponent<Renderer>() : null;

        /// <summary>今の外の輪の点（ワールド。テスト用）。</summary>
        public Vector3 RimPoint(int i)
        {
            return _rim != null ? _rim.GetPosition(i) : Vector3.zero;
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(MeshFilter disc, MeshFilter exitDisc, Material lineMaterial, Color color, Vector2 half, float exitRadius)
        {
            _disc = disc;
            _exitDisc = exitDisc;
            _lineMaterial = lineMaterial;
            _color = color;
            _half = half;
            _exitRadius = exitRadius;
        }

        /// <summary>この口に波紋を出す。</summary>
        public void Pulse()
        {
            _pulse = 1f;
            Pulses++;
        }

        private void Awake()
        {
            _zone = GetComponentInParent<StageZoneView>();
            _block = new MaterialPropertyBlock();
            _exitBlock = new MaterialPropertyBlock();
            if (_disc != null && _disc.sharedMesh != null)
            {
                // 撓ませるので、ゲートごとに網の写しを持つ。
                _mesh = Instantiate(_disc.sharedMesh);
                _disc.sharedMesh = _mesh;
                _base = _mesh.vertices;
                _deformed = new Vector3[_base.Length];
            }
            if (_lineMaterial != null)
            {
                _rim = CreateLine("Rim", 0.16f);
                _rimInner = CreateLine("RimInner", 0.06f);
                for (int i = 0; i < 3; i++)
                {
                    _swirls.Add(CreateLine($"Swirl{i + 1}", 0.07f));
                }
                if (_exitDisc != null)
                {
                    _exitRing = CreateLine("ExitRing", 0.07f);
                }
            }
        }

        private LineRenderer CreateLine(string name, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = RingPoints;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void LateUpdate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
            DetectWarps();
            UpdateBend(dt);
            _pulse = Mathf.Max(0f, _pulse - dt / PulseSeconds);
            _exitPulse = Mathf.Max(0f, _exitPulse - dt / PulseSeconds);
            DeformDisc();
            float time = Time.time;
            Vector3 center = transform.position;
            float pulse = _pulse * _pulse * (3f - 2f * _pulse);
            if (_rim != null)
            {
                // 外の輪（回りながら揺れる。前の見た目）と、少し内の細い輪。波紋で外へ広がる。
                DrawRing(_rim, center, 1f + 0.12f * pulse, time * 2f, 0.08f, 1f);
                DrawRing(_rimInner, center, 0.88f, -time * 1.4f, 0.05f, 0.55f + 0.4f * pulse);
                // 中へ縮んで吸い込まれていく輪 3 本。
                for (int i = 0; i < _swirls.Count; i++)
                {
                    float phase = Mathf.Repeat(time * 0.8f + i / (float)_swirls.Count, 1f);
                    float fade = Mathf.Sin(phase * Mathf.PI);
                    DrawRing(_swirls[i], center, 1f - phase * 0.85f, -time * 3f + i, 0.1f, fade * 0.8f);
                }
            }
            if (_exitDisc != null)
            {
                float exitPulse = _exitPulse * _exitPulse * (3f - 2f * _exitPulse);
                _exitDisc.transform.localScale = Vector3.one * _exitRadius * (1f + 0.06f * Mathf.Sin(time * Mathf.PI * 2f) + 0.4f * exitPulse);
                Renderer exitRenderer = _exitDisc.GetComponent<Renderer>();
                exitRenderer.GetPropertyBlock(_exitBlock);
                _exitBlock.SetFloat(PulseId, exitPulse);
                exitRenderer.SetPropertyBlock(_exitBlock);
                if (_exitRing != null)
                {
                    Vector3 exit = _exitDisc.transform.position;
                    float radius = _exitRadius * (1.05f + 0.1f * Mathf.Sin(time * Mathf.PI * 2f) + 0.5f * exitPulse);
                    for (int i = 0; i < RingPoints; i++)
                    {
                        float angle = i / (float)RingPoints * Mathf.PI * 2f;
                        float wobble = 1f + 0.06f * Mathf.Sin(angle * 3f - time * 2f);
                        _exitRing.SetPosition(i, new Vector3(exit.x + Mathf.Cos(angle) * radius * wobble, exit.y + Mathf.Sin(angle) * radius * wobble, exit.z - 0.05f));
                    }
                    SetColor(_exitRing, 0.7f + 0.3f * exitPulse);
                }
            }
            Renderer mouth = Mouth;
            if (mouth != null)
            {
                mouth.GetPropertyBlock(_block);
                _block.SetFloat(PulseId, pulse);
                _block.SetFloat(BendId, _bend.magnitude);
                mouth.SetPropertyBlock(_block);
            }
        }

        // 動く速さから、撓みの目標（動く向きの反対）を決め、ばねで追わせる（向きが変わる端で行きすぎて揺れ戻る）。
        private void UpdateBend(float dt)
        {
            Vector3 position = transform.position;
            Vector2 velocity = _hasLast ? (Vector2)(position - _lastPosition) / dt : Vector2.zero;
            _lastPosition = position;
            _hasLast = true;
            // 瞬間移動（シーンの読み込みなど）は無視する。
            if (velocity.sqrMagnitude > 60f * 60f)
            {
                velocity = Vector2.zero;
            }
            Vector2 target = -Vector2.ClampMagnitude(velocity / BendSpeed, 1f);
            _bendVelocity += (BendStiffness * (target - _bend) - BendDamping * _bendVelocity) * dt;
            _bend += _bendVelocity * dt;
            _bend = Vector2.ClampMagnitude(_bend, 1.6f);
        }

        // 半径 1 の円盤の点 q（ゲートの楕円に拡大する前）を、撓みに合わせて動かす（拡大する前の単位で返す）。
        private Vector2 Deform(Vector2 q)
        {
            float magnitude = _bend.magnitude;
            if (magnitude < 1e-4f)
            {
                return q;
            }
            // 撓む向き（ワールド）を、楕円の単位に直す。
            Vector2 back = _bend / magnitude;
            Vector2 backUnit = new Vector2(back.x / Mathf.Max(_half.x, 0.01f), back.y / Mathf.Max(_half.y, 0.01f));
            float reach = Mathf.Sqrt(back.x * back.x * _half.x * _half.x + back.y * back.y * _half.y * _half.y);
            Vector2 dir = backUnit.normalized;
            float along = -Vector2.Dot(q, dir);
            Vector2 across = q + dir * along;
            float t2 = Mathf.Clamp01(across.sqrMagnitude);
            // 帆のふくらみ（真ん中ほど遅れる）＋ 後ろ側の尾 ＋ 前の縁が少し平たく。
            float shift = 0.55f * (1f - t2) + 0.45f * Mathf.Max(0f, -along) * Mathf.Sqrt(1f - t2) - 0.12f * Mathf.Max(0f, along);
            float meters = shift * magnitude * reach;
            return q + new Vector2(back.x * meters / Mathf.Max(_half.x, 0.01f), back.y * meters / Mathf.Max(_half.y, 0.01f));
        }

        private void DeformDisc()
        {
            if (_mesh == null)
            {
                return;
            }
            for (int i = 0; i < _base.Length; i++)
            {
                Vector2 q = Deform(new Vector2(_base[i].x, _base[i].y));
                _deformed[i] = new Vector3(q.x, q.y, _base[i].z);
            }
            _mesh.vertices = _deformed;
            _mesh.RecalculateBounds();
        }

        // 楕円の輪（scale 倍。rotation で揺れが回る）を、撓ませて描く。
        private void DrawRing(LineRenderer line, Vector3 center, float scale, float rotation, float wobbleAmount, float alpha)
        {
            for (int i = 0; i < RingPoints; i++)
            {
                float angle = i / (float)RingPoints * Mathf.PI * 2f;
                float wobble = 1f + wobbleAmount * Mathf.Sin(angle * 3f + rotation);
                var q = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * scale * wobble;
                Vector2 d = Deform(q);
                line.SetPosition(i, new Vector3(center.x + d.x * _half.x, center.y + d.y * _half.y, center.z - 0.08f));
            }
            SetColor(line, alpha);
        }

        private void SetColor(LineRenderer line, float alpha)
        {
            Color c = _color;
            c.a = Mathf.Clamp01(alpha);
            line.startColor = c;
            line.endColor = c;
        }

        // 誰かがワープした: 直前の位置がこの口の近くなら入った口、今の位置が出口の近くなら出口に波紋。
        private void DetectWarps()
        {
            ISimulationFeed feed = _zone != null ? _zone.Feed : null;
            if (feed == null)
            {
                return;
            }
            int count = feed.CharacterCount;
            if (_warpCounts.Length != count)
            {
                _warpCounts = new int[count];
                _lastPositions = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    CharacterState state = feed.GetState(i);
                    _warpCounts[i] = state.Combat.WarpCount;
                    _lastPositions[i] = new Vector3(state.Position.X, state.Position.Y, 0f);
                }
                return;
            }
            Vector3 center = transform.position;
            float reach = Mathf.Max(_half.x, _half.y) + 2f;
            for (int i = 0; i < count; i++)
            {
                CharacterState state = feed.GetState(i);
                var position = new Vector3(state.Position.X, state.Position.Y, 0f);
                if (state.Combat.WarpCount > _warpCounts[i])
                {
                    if (Near(_lastPositions[i], center, reach))
                    {
                        Pulse();
                    }
                    if (_exitDisc != null && Near(position, _exitDisc.transform.position, _exitRadius + 2f))
                    {
                        _exitPulse = 1f;
                        ExitPulses++;
                    }
                }
                _warpCounts[i] = state.Combat.WarpCount;
                _lastPositions[i] = position;
            }
        }

        private static bool Near(Vector3 a, Vector3 b, float distance)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return dx * dx + dy * dy <= distance * distance;
        }
    }
}
