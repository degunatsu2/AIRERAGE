using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 埠頭の荷役の網（跳ね返す物 + ZoneLook.Net。2026-11-21 の方針: 実際の現象に寄せ、判定を見た目に合わせる。表示だけ）。
    /// 手本は港の荷役の網（カーゴネット）: 四隅の輪を 4 本足の吊り具でクレーンの鉤に掛けて吊る。吊り具の下に枠（スプレッダー）を入れると
    /// 網が張ったまま広がる。落ちてきた物を受けた網は、受けた所が深く沈んでから跳ね返し、数回揺れて収まる（落下物を受ける網と同じ）。
    /// - 網目（縦横の綱）は、乗った所を中心に沈み、ばねのように戻る。いつも自分の重さで少したるんでいる。
    /// - 跳ね返したら、吊り具ごと少し揺れる。
    /// 当たり（跳ね返す物）は網の所だけ（StageZone のまま）。誰が乗ったかは表示する試合の状態を見る（StageZoneView.Feed）。
    /// </summary>
    public sealed class CargoNetView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("網の横（X）に通る綱と、奥行き（Z）に通る綱")]
        [SerializeField] private LineRenderer[] _alongX = new LineRenderer[0];
        [SerializeField] private LineRenderer[] _alongZ = new LineRenderer[0];
        [Tooltip("網の半分の大きさ（横・奥行き。m）と、網の高さ（ギミックの物体から）")]
        [SerializeField] private Vector2 _half = new Vector2(1.6f, 1.1f);
        [SerializeField] private float _height;
        [Tooltip("揺らす吊り具の全体（鉤の所が軸）")]
        [SerializeField] private Transform _rig;

        /// <summary>自分の重さのたるみ（m）、ばねの強さと減衰。</summary>
        public const float RestSag = 0.28f;
        public const float Stiffness = 150f;
        public const float Damping = 7f;

        private float _sag;
        private float _sagVelocity;
        private float _impactX;
        private float _sway;
        private float _swayVelocity;
        private Vector3[] _lastVelocity = new Vector3[0];
        private bool[] _inside = new bool[0];

        /// <summary>今の沈み（m。たるみは除く。テスト用）。</summary>
        public float Sag => _sag;

        /// <summary>網が受けた回数（テスト用）。</summary>
        public int Catches { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, LineRenderer[] alongX, LineRenderer[] alongZ, Vector2 half, float height, Transform rig)
        {
            _view = view;
            _alongX = alongX;
            _alongZ = alongZ;
            _half = half;
            _height = height;
            _rig = rig;
        }

        /// <summary>網が受ける（沈み始める）。x は網の真ん中からの横の位置、speed は落ちてきた速さ（m/s）。</summary>
        public void Catch(float x, float speed)
        {
            _impactX = Mathf.Clamp(x, -_half.x * 0.8f, _half.x * 0.8f);
            _sagVelocity += Mathf.Clamp(speed * 0.2f, 1.2f, 4f);
            _swayVelocity += Mathf.Clamp(x, -1f, 1f) * 12f;
            Catches++;
        }

        private void LateUpdate()
        {
            DetectCatches();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
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
                Shape(line, true);
            }
            foreach (LineRenderer line in _alongZ)
            {
                Shape(line, false);
            }
        }

        // 綱の点を、網の面の高さから沈みの形の分だけ下げる。網の綱は作るときの位置（横か奥行きの位置）を保つ。
        private void Shape(LineRenderer line, bool alongX)
        {
            if (line == null)
            {
                return;
            }
            int count = line.positionCount;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = line.GetPosition(i);
                float x = p.x;
                float z = p.z;
                p.y = _height - Depth(x, z);
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
            ISimulationFeed feed = _view != null ? _view.Feed : null;
            if (feed == null)
            {
                return;
            }
            if (_inside.Length != feed.CharacterCount)
            {
                _inside = new bool[feed.CharacterCount];
                _lastVelocity = new Vector3[feed.CharacterCount];
            }
            Vector3 center = transform.position;
            Vector2 zoneHalf = _view.GetComponent<StageZoneAuthoring>() != null ? _view.GetComponent<StageZoneAuthoring>().Size * 0.5f : _half;
            for (int i = 0; i < _inside.Length; i++)
            {
                CharacterState state = feed.GetState(i);
                System.Numerics.Vector3 extents = feed.GetConfig(i).Movement.HalfExtents;
                var position = new Vector2(state.Position.X, state.Position.Y);
                bool inside = Mathf.Abs(position.x - center.x) < zoneHalf.x + extents.X && Mathf.Abs(position.y - center.y) < zoneHalf.y + extents.Y + 0.6f;
                // 上から落ちてきて、網の所で上向きに跳ね返った（表示のフレームがティックより粗くても、向きが変わったことで分かる）。
                bool bounced = _lastVelocity[i].y < -1f && state.Velocity.Y > 1f;
                if (inside && bounced && position.y > center.y)
                {
                    Catch(position.x - center.x, -_lastVelocity[i].y);
                }
                _inside[i] = inside;
                _lastVelocity[i] = new Vector3(state.Velocity.X, state.Velocity.Y, 0f);
            }
        }
    }
}
