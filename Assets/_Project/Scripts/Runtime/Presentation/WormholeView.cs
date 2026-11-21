using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワームホールの口（宇宙ステーションのワープゲート。2026-11-21 のギミックの精巧化 第 5 弾。表示だけ）。口の見た目は Airerage/Wormhole の
    /// 板が描く（重力レンズ・口の中の向こう側の景色・縁の光）。ここでは:
    /// - 口の中に映す「向こう側」の位置（対になった口か、出口の小さな口）を、毎フレーム材質へ渡す（救出のゲートは動くので）。
    /// - 誰かがワープしたとき（CombatState.WarpCount が増えた）、入った口と出た口に波紋を出す（_Pulse）。
    /// 当たりは Core の丸いワープゲート（StageZone.Round）で、口の半径と同じ。
    /// </summary>
    public sealed class WormholeView : MonoBehaviour
    {
        /// <summary>波紋が広がり切るまでの秒数。</summary>
        public const float PulseSeconds = 0.7f;

        [Tooltip("この口の板（Airerage/Wormhole）と、口の半径（m）")]
        [SerializeField] private Renderer _mouth;
        [SerializeField] private float _radius = 3f;
        [Tooltip("口の中に映す向こう側（対の口か出口の口）。対の口なら、その口の WormholeView が向こうの波紋を出す")]
        [SerializeField] private Transform _other;
        [Tooltip("出口の小さな口（救出のゲートの出口。なければ使わない）と、その半径")]
        [SerializeField] private Renderer _exitMouth;
        [SerializeField] private float _exitRadius = 1.2f;

        private static readonly int OtherCenterId = Shader.PropertyToID("_OtherCenter");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");

        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _exitBlock;
        private StageZoneView _zone;
        private int[] _warpCounts = new int[0];
        private Vector3[] _lastPositions = new Vector3[0];
        private float _pulse;
        private float _exitPulse;

        /// <summary>波紋を出した回数（テスト用）。</summary>
        public int Pulses { get; private set; }

        /// <summary>出口の口で波紋を出した回数（テスト用）。</summary>
        public int ExitPulses { get; private set; }

        /// <summary>口の半径（m）。</summary>
        public float Radius => _radius;

        /// <summary>この口の板。</summary>
        public Renderer Mouth => _mouth;

        /// <summary>出口の小さな口（なければ null）。</summary>
        public Renderer ExitMouth => _exitMouth;

        /// <summary>口の中に映す向こう側（テスト用）。</summary>
        public Transform Other => _other;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer mouth, float radius, Transform other, Renderer exitMouth, float exitRadius)
        {
            _mouth = mouth;
            _radius = radius;
            _other = other;
            _exitMouth = exitMouth;
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
        }

        private void LateUpdate()
        {
            DetectWarps();
            float fade = Time.deltaTime / PulseSeconds;
            _pulse = Mathf.Max(0f, _pulse - fade);
            _exitPulse = Mathf.Max(0f, _exitPulse - fade);
            if (_mouth != null)
            {
                _mouth.GetPropertyBlock(_block);
                Vector3 other = _other != null ? _other.position : _mouth.transform.position;
                _block.SetVector(OtherCenterId, other);
                _block.SetFloat(PulseId, Ease(_pulse));
                _mouth.SetPropertyBlock(_block);
            }
            if (_exitMouth != null && _mouth != null)
            {
                _exitMouth.GetPropertyBlock(_exitBlock);
                _exitBlock.SetVector(OtherCenterId, _mouth.transform.position);
                _exitBlock.SetFloat(PulseId, Ease(_exitPulse));
                _exitMouth.SetPropertyBlock(_exitBlock);
            }
        }

        // 波紋は出た瞬間がいちばん強く、広がりながら消える（1 → 0 を緩やかに）。
        private static float Ease(float t)
        {
            return t * t * (3f - 2f * t);
        }

        // 誰かがワープした: 直前の位置がこの口の近くなら入った口、今の位置がこの口（か出口の口）の近くなら出た口。
        private void DetectWarps()
        {
            ISimulationFeed feed = _zone != null ? _zone.Feed : null;
            if (feed == null || _mouth == null)
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
            Vector3 center = _mouth.transform.position;
            for (int i = 0; i < count; i++)
            {
                CharacterState state = feed.GetState(i);
                var position = new Vector3(state.Position.X, state.Position.Y, 0f);
                if (state.Combat.WarpCount > _warpCounts[i])
                {
                    if (Near(_lastPositions[i], center, _radius + 2f) || Near(position, center, _radius + 2f))
                    {
                        Pulse();
                    }
                    if (_exitMouth != null && Near(position, _exitMouth.transform.position, _exitRadius + 2f))
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
