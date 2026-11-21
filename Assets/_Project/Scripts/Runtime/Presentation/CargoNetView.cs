using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 埠頭の荷役の網（動く足場 StagePlatformKind.Hoist の見た目。2026-11-21 の要望。表示だけ）。網そのものの位置（吊り具が伸びて下がる・
    /// バンジーのように跳ね返る・ゆっくり戻る）は Core の状態で、StagePlatformView がこの物体（足場の見た目）を動かす。ここでは:
    /// - トロリーから鉤のブロックへ伸びるワイヤーを、網の今の高さに合わせて伸び縮みさせる。
    /// - 網目（縦横の綱）を、乗った所を中心に沈め、ばねのように戻す。いつも自分の重さで少したるんでいる。手本は港の荷役の網（カーゴネット）と、
    ///   落ちてきた物を受けて深く沈んでから跳ね返す網。
    /// - 跳ね返したときと、網が速く下がったり跳ね返ったりしたときに、吊り具ごと少し揺らす。
    /// 誰が乗ったかは表示する試合の状態を見る（StagePlatformView.Feed）。
    /// </summary>
    public sealed class CargoNetView : MonoBehaviour
    {
        [Tooltip("網の横（X）に通る綱と、奥行き（Z）に通る綱")]
        [SerializeField] private LineRenderer[] _alongX = new LineRenderer[0];
        [SerializeField] private LineRenderer[] _alongZ = new LineRenderer[0];
        [Tooltip("網の半分の大きさ（横・奥行き。m）と、網の高さ（網の物体から）")]
        [SerializeField] private Vector2 _half = new Vector2(1.6f, 1.1f);
        [SerializeField] private float _height;
        [Tooltip("揺らす吊り具の全体（鉤の所が軸）")]
        [SerializeField] private Transform _rig;
        [Tooltip("トロリーから鉤のブロックへのワイヤー（ワールドの位置）と、トロリー（動かない）")]
        [SerializeField] private LineRenderer _cable;
        [SerializeField] private Transform _trolley;

        /// <summary>自分の重さのたるみ（m）、ばねの強さと減衰。</summary>
        public const float RestSag = 0.28f;
        public const float Stiffness = 150f;
        public const float Damping = 7f;

        private StagePlatformView _platform;
        private float _sag;
        private float _sagVelocity;
        private float _impactX;
        private float _sway;
        private float _swayVelocity;
        private float _lastY;
        private float _lastSpeed;
        private Vector3[] _lastVelocity = new Vector3[0];

        /// <summary>今の沈み（m。たるみは除く。テスト用）。</summary>
        public float Sag => _sag;

        /// <summary>網が受けた回数（テスト用）。</summary>
        public int Catches { get; private set; }

        /// <summary>トロリーから鉤のブロックへのワイヤーの長さ（m。テスト用）。</summary>
        public float CableLength => _cable != null ? Vector3.Distance(_cable.GetPosition(0), _cable.GetPosition(1)) : 0f;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(LineRenderer[] alongX, LineRenderer[] alongZ, Vector2 half, float height, Transform rig, LineRenderer cable, Transform trolley)
        {
            _alongX = alongX;
            _alongZ = alongZ;
            _half = half;
            _height = height;
            _rig = rig;
            _cable = cable;
            _trolley = trolley;
        }

        /// <summary>網が受ける（沈み始める）。x は網の真ん中からの横の位置、speed は落ちてきた速さ（m/s）。</summary>
        public void Catch(float x, float speed)
        {
            _impactX = Mathf.Clamp(x, -_half.x * 0.8f, _half.x * 0.8f);
            _sagVelocity += Mathf.Clamp(speed * 0.2f, 1.2f, 4f);
            _swayVelocity += Mathf.Clamp(x, -1f, 1f) * 12f;
            Catches++;
        }

        private void Awake()
        {
            _platform = GetComponentInParent<StagePlatformView>();
            _lastY = transform.position.y;
        }

        private void LateUpdate()
        {
            DetectCatches();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            // 網が速く下がる・跳ね返ると、吊り具が少し揺れる（速さの変わり方で、網目が慣性で遅れて沈む・浮く）。
            float y = transform.position.y;
            float speed = dt > 0f ? (y - _lastY) / dt : 0f;
            _sagVelocity += Mathf.Clamp((speed - _lastSpeed) * 0.04f, -0.6f, 0.6f);
            _lastY = y;
            _lastSpeed = speed;
            _sagVelocity += (-Stiffness * _sag - Damping * _sagVelocity) * dt;
            _sag += _sagVelocity * dt;
            _swayVelocity += (-30f * _sway - 2.2f * _swayVelocity) * dt;
            _sway += _swayVelocity * dt;
            if (_rig != null)
            {
                _rig.localRotation = Quaternion.Euler(0f, 0f, _sway);
            }
            foreach (LineRenderer line in _alongX)
            {
                Shape(line);
            }
            foreach (LineRenderer line in _alongZ)
            {
                Shape(line);
            }
            if (_cable != null && _trolley != null && _rig != null)
            {
                // ワイヤー: トロリーの下から、鉤のブロックの上まで（網が下がった分だけ伸びる）。
                _cable.SetPosition(0, _trolley.position + Vector3.down * 0.25f);
                _cable.SetPosition(1, _rig.TransformPoint(new Vector3(0f, 1.1f, 0f)));
            }
        }

        // 綱の点を、網の面の高さから沈みの形の分だけ下げる。網の綱は作るときの位置（横か奥行きの位置）を保つ。
        private void Shape(LineRenderer line)
        {
            if (line == null)
            {
                return;
            }
            int count = line.positionCount;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = line.GetPosition(i);
                p.y = _height - Depth(p.x, p.z);
                line.SetPosition(i, p);
            }
        }

        /// <summary>網の (x, z) の所の沈み（m。下向きが正）: 縁ほど浅く、受けた所ほど深い。</summary>
        public float Depth(float x, float z)
        {
            float u = Mathf.Clamp01(1f - (x / _half.x) * (x / _half.x));
            float v = Mathf.Clamp01(1f - (z / _half.y) * (z / _half.y));
            float edge = u * v;
            float d = x - _impactX;
            float local = Mathf.Exp(-d * d / 1.1f);
            return edge * (RestSag + Mathf.Max(-0.25f, _sag) * (0.35f + 0.65f * local));
        }

        // 誰かが上から網に乗って跳ね返されたか。跳ね返される直前の下向きの速さで沈む。
        private void DetectCatches()
        {
            ISimulationFeed feed = _platform != null ? _platform.Feed : null;
            if (feed == null)
            {
                return;
            }
            if (_lastVelocity.Length != feed.CharacterCount)
            {
                _lastVelocity = new Vector3[feed.CharacterCount];
            }
            Vector3 center = transform.position;
            Vector2 boxHalf = _platform.Platform != null
                ? new Vector2(_platform.Platform.HalfSize.X, _platform.Platform.HalfSize.Y)
                : _half;
            for (int i = 0; i < _lastVelocity.Length; i++)
            {
                CharacterState state = feed.GetState(i);
                System.Numerics.Vector3 extents = feed.GetConfig(i).Movement.HalfExtents;
                var position = new Vector2(state.Position.X, state.Position.Y);
                bool inside = Mathf.Abs(position.x - center.x) < boxHalf.x + extents.X && Mathf.Abs(position.y - center.y) < boxHalf.y + extents.Y + 0.6f;
                // 上から落ちてきて、網の所で上向きに跳ね返った（表示のフレームがティックより粗くても、向きが変わったことで分かる）。
                bool bounced = _lastVelocity[i].y < -1f && state.Velocity.Y > 1f;
                if (inside && bounced && position.y > center.y)
                {
                    Catch(position.x - center.x, -_lastVelocity[i].y);
                }
                _lastVelocity[i] = new Vector3(state.Velocity.X, state.Velocity.Y, 0f);
            }
        }
    }
}
