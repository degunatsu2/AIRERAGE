using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 宇宙船の中の無重力（StageZoneKind.LowGravity の見た目。2026-11-21 のギミックの精巧化 第 5 弾。表示だけ）。手本は国際宇宙ステーション
    /// （ISS）と天宮の船内の映像: 固定していない物（工具・ペン・手順書・食品の袋・荷物の袋）はゆっくり漂いながら回り、壁に当たると
    /// 跳ね返る。水は表面張力で揺れる玉になり、凸レンズのように後ろの景色を逆さに映す（天宮の授業の水の玉。Airerage/WaterDrop）。
    /// 換気の風でゆっくり流れる。人が近くを通ると押されて飛んでいく（押した勢いのまま、ゆっくり止まらない）。
    /// 漂う物の位置は表示だけで決める（巻き戻しとも関係ない）。
    /// </summary>
    public sealed class MicrogravityView : MonoBehaviour
    {
        [Tooltip("漂う物（工具・袋・ペンなど）と、水の玉")]
        [SerializeField] private Transform[] _items = new Transform[0];
        [SerializeField] private Transform[] _drops = new Transform[0];
        [Tooltip("漂える範囲（中心からの半分の大きさ。m。物の大きさの分は内側へ）と、奥行きの範囲（z）")]
        [SerializeField] private Vector2 _half = new Vector2(7f, 3f);
        [SerializeField] private Vector2 _depth = new Vector2(0.7f, 1.4f);

        /// <summary>壁で跳ね返るときに残る速さの割合。</summary>
        public const float Restitution = 0.55f;

        /// <summary>換気の流れの速さ（m/s）と、流れに寄っていく速さ（1 秒あたり）。</summary>
        public const float FlowSpeed = 0.12f;
        public const float FlowFollow = 0.08f;

        /// <summary>人が押す範囲（m）と、押す強さ（人の速さに掛ける）。</summary>
        public const float PushRadius = 1.3f;
        public const float PushShare = 0.35f;

        /// <summary>漂う物（水の玉のほか）のおよその半径（m。壁で跳ね返る所と、人に押される範囲に使う）。</summary>
        public const float ItemRadius = 0.4f;

        /// <summary>押されて飛んでいく速さの上限（m/s）。</summary>
        public const float MaxSpeed = 5f;

        private Vector3[] _velocity = new Vector3[0];
        private Vector3[] _spin = new Vector3[0];
        private float[] _size = new float[0];
        private float[] _wobble = new float[0];
        private float[] _wobblePhase = new float[0];
        private Vector3[] _dropScale = new Vector3[0];
        private Transform[] _all = new Transform[0];
        private StageZoneView _zone;

        /// <summary>人に押された回数（テスト用）。</summary>
        public int Pushes { get; private set; }

        /// <summary>漂う物の数（水の玉を含む。テスト用）。</summary>
        public int Count => _all.Length;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Transform[] items, Transform[] drops, Vector2 half, Vector2 depth)
        {
            _items = items;
            _drops = drops;
            _half = half;
            _depth = depth;
        }

        /// <summary>i 番目の物の今の速さ（m/s。テスト用）。</summary>
        public Vector3 VelocityOf(int i)
        {
            return _velocity[i];
        }

        private void Awake()
        {
            _zone = GetComponentInParent<StageZoneView>();
            _all = new Transform[_items.Length + _drops.Length];
            _items.CopyTo(_all, 0);
            _drops.CopyTo(_all, _items.Length);
            int n = _all.Length;
            _velocity = new Vector3[n];
            _spin = new Vector3[n];
            _size = new float[n];
            _wobble = new float[n];
            _wobblePhase = new float[n];
            _dropScale = new Vector3[n];
            var random = new System.Random(n * 7919 + Mathf.RoundToInt(transform.position.x * 10f));
            for (int i = 0; i < n; i++)
            {
                if (_all[i] == null)
                {
                    continue;
                }
                // ゆっくり漂い、ゆっくり回る（ISS の映像の工具やペンは 1 秒に数 cm〜十数 cm、1 回転に数秒〜十数秒）。
                bool drop = i >= _items.Length;
                _velocity[i] = new Vector3(Range(random, -0.12f, 0.12f), Range(random, -0.08f, 0.08f), 0f);
                _spin[i] = drop ? Vector3.zero : new Vector3(Range(random, -40f, 40f), Range(random, -30f, 30f), Range(random, -35f, 35f));
                Vector3 scale = _all[i].localScale;
                // 水の玉は球の大きさ、ほかの物（中身を子に持つ空の物体）はおよその半径。
                _size[i] = drop ? Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * 0.5f : ItemRadius;
                _wobble[i] = drop ? 0.04f : 0f;
                _wobblePhase[i] = Range(random, 0f, Mathf.PI * 2f);
                _dropScale[i] = scale;
            }
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f)
            {
                return;
            }
            Push();
            float time = Time.time;
            for (int i = 0; i < _all.Length; i++)
            {
                Transform item = _all[i];
                if (item == null)
                {
                    continue;
                }
                Vector3 p = item.localPosition;
                // 換気の流れ（ゆっくり回る大きな渦）に少しずつ寄る。
                Vector3 flow = new Vector3(-p.y / Mathf.Max(_half.y, 0.1f), p.x / Mathf.Max(_half.x, 0.1f), 0f) * FlowSpeed;
                _velocity[i] += (flow - _velocity[i]) * Mathf.Min(1f, FlowFollow * dt);
                p += _velocity[i] * dt;
                float limitX = Mathf.Max(0f, _half.x - _size[i]);
                float limitY = Mathf.Max(0f, _half.y - _size[i]);
                if (Mathf.Abs(p.x) > limitX)
                {
                    p.x = Mathf.Sign(p.x) * limitX;
                    _velocity[i].x = -_velocity[i].x * Restitution;
                    _spin[i] += new Vector3(0f, 0f, _velocity[i].y * 60f);
                    _wobble[i] = Mathf.Max(_wobble[i], i >= _items.Length ? 0.12f : 0f);
                }
                if (Mathf.Abs(p.y) > limitY)
                {
                    p.y = Mathf.Sign(p.y) * limitY;
                    _velocity[i].y = -_velocity[i].y * Restitution;
                    _spin[i] += new Vector3(0f, 0f, -_velocity[i].x * 60f);
                    _wobble[i] = Mathf.Max(_wobble[i], i >= _items.Length ? 0.12f : 0f);
                }
                p.z = Mathf.Clamp(p.z, _depth.x, _depth.y);
                item.localPosition = p;
                if (i < _items.Length)
                {
                    item.localRotation = Quaternion.Euler(_spin[i] * dt) * item.localRotation;
                }
                else
                {
                    // 水の玉は表面張力で揺れる（押されたり壁に当たったりすると大きく、だんだん小さく）。
                    _wobble[i] = Mathf.Max(0.04f, _wobble[i] - dt * 0.15f);
                    float w = Mathf.Sin(time * 7f + _wobblePhase[i]) * _wobble[i];
                    item.localScale = Vector3.Scale(_dropScale[i], new Vector3(1f + w, 1f - w, 1f + w * 0.5f));
                }
            }
        }

        // 近くを通った人が、物を押す（人の速さの一部を物に渡す。無重力なので押された物は止まらずに飛んでいく）。
        private void Push()
        {
            ISimulationFeed feed = _zone != null ? _zone.Feed : null;
            if (feed == null)
            {
                return;
            }
            Vector3 origin = transform.position;
            for (int c = 0; c < feed.CharacterCount; c++)
            {
                CharacterState state = feed.GetState(c);
                var position = new Vector2(state.Position.X - origin.x, state.Position.Y - origin.y);
                if (Mathf.Abs(position.x) > _half.x + PushRadius || Mathf.Abs(position.y) > _half.y + PushRadius)
                {
                    continue;
                }
                var velocity = new Vector2(state.Velocity.X, state.Velocity.Y);
                for (int i = 0; i < _all.Length; i++)
                {
                    if (_all[i] == null)
                    {
                        continue;
                    }
                    Vector3 local = _all[i].localPosition;
                    Vector2 away = new Vector2(local.x, local.y) - position;
                    float distance = away.magnitude;
                    float reach = PushRadius + _size[i];
                    if (distance > reach)
                    {
                        continue;
                    }
                    Vector2 normal = distance > 1e-3f ? away / distance : Vector2.up;
                    // 人の速さのうち物へ向かう分と、押しのける分（近いほど強い）。
                    float toward = Mathf.Max(0f, Vector2.Dot(velocity, normal));
                    float shove = (1f - distance / reach) * 1.2f;
                    Vector2 kick = normal * (toward * PushShare + shove) * Time.deltaTime * 8f;
                    if (kick.sqrMagnitude < 1e-6f)
                    {
                        continue;
                    }
                    _velocity[i] = Vector3.ClampMagnitude(_velocity[i] + new Vector3(kick.x, kick.y, 0f), MaxSpeed);
                    _spin[i] += new Vector3(kick.y * 90f, -kick.x * 90f, (kick.x - kick.y) * 120f);
                    if (i >= _items.Length)
                    {
                        _wobble[i] = Mathf.Max(_wobble[i], 0.16f);
                    }
                    Pushes++;
                }
            }
        }
    }
}
