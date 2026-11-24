using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// コイルガン（宇宙ステーションの StageZoneKind.CoilGun の見た目。2026-11-24 の要望。表示だけ）。手本は多段のコイルガン（コイルの
    /// 電磁石に順に電気を流して、中の物を撃ち出すリニアモーター。段ごとにコンデンサーがある）。
    /// - 誰も入っていない間は、コイルの内側の光る輪が順に淡く光る（待機）。砲身はコイルの向き（ギミックの向き）へゆっくり戻る。
    /// - 人が収まると、砲身がその人の照準（CombatState.SkillAim。StageZones.CoilDirection）の向きへ回り、ため具合
    ///   （StageZones.CoilCharge）に合わせて、後ろのコイルから順に光が満ちていく（1 つ満ちるたびに火花）。満ちきる直前は全部が白く明滅する。
    ///   撃ち出す先の軌道（重力の放物線）を点線で見せる。
    /// - 撃ち出した瞬間に、砲口から火花と光をはじけさせ、砲身が少し下がる（反動）。
    /// 当たり（触れると収まる範囲）は Core のギミックの範囲（コイルの並ぶ長さ × 内径）。
    /// </summary>
    public sealed class CoilAcceleratorView : MonoBehaviour
    {
        [Tooltip("コイルの内側の光る輪（後ろから砲口への順）と、火花（コイルごと）")]
        [SerializeField] private Renderer[] _liners = new Renderer[0];
        [SerializeField] private ParticleSystem[] _sparks = new ParticleSystem[0];
        [Tooltip("砲口の火花（撃ち出した瞬間）と、撃ち出す先の軌道の点線")]
        [SerializeField] private ParticleSystem _muzzle;
        [SerializeField] private LineRenderer _trajectory;
        [Tooltip("砲口の位置（砲身の向きに沿った、中心からの距離。m）")]
        [SerializeField] private float _muzzleDistance = 1.9f;
        [Tooltip("ギミックの向き（ワールド。長さ 1）と、撃ち出す速さ（m/s）")]
        [SerializeField] private Vector2 _axis = Vector2.right;
        [SerializeField] private float _power = 26f;
        [Tooltip("光の色（Airerage/Emissive の _BaseColor。強さは光り方で変える）")]
        [SerializeField] private Color _color = new Color(0.35f, 0.85f, 1f);

        /// <summary>砲身が照準の向きへ回る速さ（度/秒）。</summary>
        public const float TurnDegreesPerSecond = 900f;

        /// <summary>軌道の点線を見せる長さ（秒）と点の数。</summary>
        public const float TrajectorySeconds = 0.9f;
        public const int TrajectoryPoints = 24;

        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private MaterialPropertyBlock _block;
        private StageZoneView _zone;
        private int _zoneIndex = -1;
        private float[] _glow = new float[0];
        private bool[] _full = new bool[0];
        private float _angle;
        private float _recoil;
        private int[] _lastCapture = new int[0];
        private readonly List<int> _occupants = new List<int>();

        /// <summary>今の砲身の向き（度。ワールドの右が 0。テスト用）。</summary>
        public float Angle => _angle;

        /// <summary>中にいる人のため具合（0〜1。いなければ 0。テスト用）。</summary>
        public float Charge { get; private set; }

        /// <summary>撃ち出した回数（テスト用）。</summary>
        public int Shots { get; private set; }

        /// <summary>コイルが満ちた回数（コイルごとの合計。テスト用）。</summary>
        public int Firings { get; private set; }

        /// <summary>i 番目のコイルの今の光の強さ（0〜1。テスト用）。</summary>
        public float GlowOf(int i)
        {
            return _glow[i];
        }

        /// <summary>軌道の点線を見せているか（テスト用）。</summary>
        public bool ShowsTrajectory => _trajectory != null && _trajectory.enabled;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer[] liners, ParticleSystem[] sparks, ParticleSystem muzzle, LineRenderer trajectory, float muzzleDistance, Vector2 axis,
            float power, Color color)
        {
            _liners = liners;
            _sparks = sparks;
            _muzzle = muzzle;
            _trajectory = trajectory;
            _muzzleDistance = muzzleDistance;
            _axis = axis.sqrMagnitude > 0f ? axis.normalized : Vector2.right;
            _power = power;
            _color = color;
        }

        private void Awake()
        {
            _zone = GetComponentInParent<StageZoneView>();
            _block = new MaterialPropertyBlock();
            _glow = new float[_liners.Length];
            _full = new bool[_liners.Length];
            _angle = Mathf.Atan2(_axis.y, _axis.x) * Mathf.Rad2Deg;
            if (_trajectory != null)
            {
                _trajectory.positionCount = TrajectoryPoints;
                _trajectory.enabled = false;
            }
        }

        private void LateUpdate()
        {
            ISimulationFeed feed = _zone != null ? _zone.Feed : null;
            float charge = 0f;
            Vector2 aim = _axis;
            float gravity = 30f;
            FindOccupants(feed);
            if (_occupants.Count > 0)
            {
                CharacterState state = feed.GetState(_occupants[0]);
                charge = StageZones.CoilCharge(state);
                System.Numerics.Vector2 direction = StageZones.CoilDirection(state.Combat.SkillAim, new System.Numerics.Vector2(_axis.x, _axis.y));
                aim = new Vector2(direction.X, direction.Y);
                gravity = feed.GetConfig(_occupants[0]).Movement.Gravity;
            }
            Charge = charge;
            float dt = Time.deltaTime;
            // 砲身の向き: 中に人がいれば照準へ速く、いなければギミックの向きへゆっくり戻る。
            float target = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
            float turn = _occupants.Count > 0 ? TurnDegreesPerSecond : 120f;
            _angle = Mathf.MoveTowardsAngle(_angle, target, turn * dt);
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 2.5f);
            transform.localRotation = Quaternion.Euler(0f, 0f, _angle);
            transform.localPosition = -(Vector3)(Direction(_angle) * _recoil * 0.35f);
            RenderCoils(charge, dt);
            RenderTrajectory(charge, aim, gravity);
        }

        private static Vector2 Direction(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        // このコイルに収まっている人を探し、収まり終わった（撃ち出された）人がいれば、砲口をはじけさせる。
        private void FindOccupants(ISimulationFeed feed)
        {
            _occupants.Clear();
            if (feed == null || feed.Zones == null)
            {
                return;
            }
            if (_zoneIndex < 0 || _zoneIndex >= feed.Zones.Count || !feed.Zones[_zoneIndex].Equals(_zone.CoreZone))
            {
                _zoneIndex = -1;
                for (int i = 0; i < feed.Zones.Count; i++)
                {
                    if (feed.Zones[i].Equals(_zone.CoreZone))
                    {
                        _zoneIndex = i;
                        break;
                    }
                }
            }
            if (_lastCapture.Length != feed.CharacterCount)
            {
                _lastCapture = new int[feed.CharacterCount];
            }
            for (int c = 0; c < feed.CharacterCount; c++)
            {
                CharacterState state = feed.GetState(c);
                bool inside = state.Combat.CaptureTicks > 0 && state.Combat.CaptureZone == _zoneIndex;
                if (inside)
                {
                    _occupants.Add(c);
                }
                else if (_lastCapture[c] > 0 && state.Combat.CaptureZone == _zoneIndex)
                {
                    Fire();
                }
                _lastCapture[c] = inside ? state.Combat.CaptureTicks : 0;
            }
        }

        // 撃ち出した: 砲口の火花と光、砲身の反動。
        private void Fire()
        {
            Shots++;
            _recoil = 1f;
            if (_muzzle != null)
            {
                _muzzle.transform.localPosition = new Vector3(_muzzleDistance, 0f, 0f);
                _muzzle.Emit(60);
            }
            for (int i = 0; i < _glow.Length; i++)
            {
                _glow[i] = 1.4f;
                _full[i] = false;
            }
        }

        // ため具合 charge（0〜1）に合わせて、後ろのコイルから順に光を満たす。誰もいなければ待機の淡い光。
        private void RenderCoils(float charge, float dt)
        {
            int n = _liners.Length;
            float time = Time.time;
            for (int i = 0; i < n; i++)
            {
                float target;
                if (charge > 0f)
                {
                    float start = i / (float)n;
                    float fill = Mathf.Clamp01((charge - start) * n);
                    target = 0.15f + 0.85f * fill;
                    if (fill >= 1f && !_full[i])
                    {
                        _full[i] = true;
                        Firings++;
                        if (i < _sparks.Length && _sparks[i] != null)
                        {
                            _sparks[i].Emit(16 + i * 6);
                        }
                    }
                    // 満ちきる直前（最後の 15%）は、全部が白く速く明滅する。
                    if (charge > 0.85f)
                    {
                        target += 0.35f * Mathf.Abs(Mathf.Sin(time * 38f));
                    }
                }
                else
                {
                    _full[i] = false;
                    target = 0.12f + 0.18f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * 3.9f - i * 1.1f)), 6f);
                }
                _glow[i] = target > _glow[i] ? Mathf.MoveTowards(_glow[i], target, dt * 8f) : Mathf.MoveTowards(_glow[i], target, dt * 2.5f);
                if (i < _liners.Length && _liners[i] != null)
                {
                    _liners[i].GetPropertyBlock(_block);
                    Color c = Color.Lerp(_color, Color.white, Mathf.Clamp01(_glow[i] - 0.8f) * 2f);
                    _block.SetColor(ColorId, c);
                    _block.SetFloat(IntensityId, 0.2f + 3.6f * _glow[i]);
                    _liners[i].SetPropertyBlock(_block);
                }
            }
        }

        // 撃ち出す先の軌道（砲口から、照準の向きに速さ Power で出た物の重力の放物線）を点線で。ためるほど濃く、遠くまで。
        private void RenderTrajectory(float charge, Vector2 aim, float gravity)
        {
            if (_trajectory == null)
            {
                return;
            }
            _trajectory.enabled = charge > 0f;
            if (charge <= 0f)
            {
                return;
            }
            Vector3 origin = transform.parent != null ? transform.parent.position : transform.position;
            Vector2 start = new Vector2(origin.x, origin.y) + aim * _muzzleDistance;
            Vector2 velocity = aim * _power;
            float seconds = TrajectorySeconds * (0.4f + 0.6f * charge);
            for (int i = 0; i < TrajectoryPoints; i++)
            {
                float t = seconds * i / (TrajectoryPoints - 1);
                var p = new Vector3(start.x + velocity.x * t, start.y + velocity.y * t - 0.5f * gravity * t * t, -0.4f);
                _trajectory.SetPosition(i, p);
            }
            Color color = Color.Lerp(_color, Color.white, charge * 0.5f);
            color.a = 0.25f + 0.55f * charge;
            Color end = color;
            end.a = 0f;
            _trajectory.startColor = color;
            _trajectory.endColor = end;
        }
    }
}
