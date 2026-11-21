using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 電磁コイルの加速器（宇宙ステーションの加速リング StageZoneKind.BoostRing の見た目。2026-11-21 のギミックの精巧化 第 5 弾。表示だけ）。
    /// 手本は多段のコイルガン（コイルの電磁石を順に通電して、通る物を引き寄せて加速するリニアモーター）: センサーが通る物の位置を見て、
    /// 物がコイルの手前にある間だけそのコイルに通電し、コイルの真ん中に来たら切る（切らないと引き戻して遅くなる）。
    /// - 誰も通っていない間は、コイルの内側の光る輪が順に淡く光る（待機の点検）。
    /// - 人が通るとき、通る向き（両方向）の手前のコイルから、人の位置に合わせて順に強く光り、通電した瞬間に火花を散らす。
    /// 当たりは Core の加速リングの範囲（コイルの並ぶ長さ × 内径）で、通っている向きに速さを Power 以上にする。
    /// </summary>
    public sealed class CoilAcceleratorView : MonoBehaviour
    {
        [Tooltip("コイルの内側の光る輪（向きの順）と、火花（コイルごと。なくてもよい）")]
        [SerializeField] private Renderer[] _liners = new Renderer[0];
        [SerializeField] private ParticleSystem[] _sparks = new ParticleSystem[0];
        [Tooltip("コイルの位置（範囲の中心から加速の向きに沿った距離。m）と、コイルの内径の半分（m）")]
        [SerializeField] private float[] _stations = new float[0];
        [SerializeField] private float _boreRadius = 1.2f;
        [Tooltip("加速の向き（ワールド。長さ 1）と、光の色（Airerage/Emissive の _BaseColor。強さは光り方で変える）")]
        [SerializeField] private Vector2 _axis = Vector2.right;
        [SerializeField] private Color _color = new Color(0.35f, 0.85f, 1f);

        /// <summary>通電する範囲（コイルの手前、m）。この範囲に人がいる間だけ強く光る。</summary>
        public const float EnergizeReach = 1.4f;

        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private MaterialPropertyBlock _block;
        private StageZoneView _zone;
        private bool[] _energized = new bool[0];
        private float[] _glow = new float[0];

        /// <summary>通電した回数（コイルごとの合計。テスト用）。</summary>
        public int Firings { get; private set; }

        /// <summary>i 番目のコイルの今の光の強さ（0〜1。テスト用）。</summary>
        public float GlowOf(int i)
        {
            return _glow[i];
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer[] liners, ParticleSystem[] sparks, float[] stations, float boreRadius, Vector2 axis, Color color)
        {
            _liners = liners;
            _sparks = sparks;
            _stations = stations;
            _boreRadius = boreRadius;
            _axis = axis.sqrMagnitude > 0f ? axis.normalized : Vector2.right;
            _color = color;
        }

        private void Awake()
        {
            _zone = GetComponentInParent<StageZoneView>();
            _block = new MaterialPropertyBlock();
            _energized = new bool[_stations.Length];
            _glow = new float[_stations.Length];
        }

        private void LateUpdate()
        {
            int n = _stations.Length;
            if (_energized.Length != n)
            {
                _energized = new bool[n];
                _glow = new float[n];
            }
            var drive = new float[n];
            ISimulationFeed feed = _zone != null ? _zone.Feed : null;
            Vector2 center = transform.position;
            if (feed != null)
            {
                Vector2 side = new Vector2(-_axis.y, _axis.x);
                for (int c = 0; c < feed.CharacterCount; c++)
                {
                    CharacterState state = feed.GetState(c);
                    var offset = new Vector2(state.Position.X, state.Position.Y) - center;
                    float along = Vector2.Dot(offset, _axis);
                    float across = Mathf.Abs(Vector2.Dot(offset, side));
                    if (across > _boreRadius + 0.6f)
                    {
                        continue;
                    }
                    // 通る向き（両方向）。止まっている人には通電しない。
                    float speed = Vector2.Dot(new Vector2(state.Velocity.X, state.Velocity.Y), _axis);
                    if (Mathf.Abs(speed) < 0.5f)
                    {
                        continue;
                    }
                    float sign = Mathf.Sign(speed);
                    for (int i = 0; i < n; i++)
                    {
                        // コイルの手前 EnergizeReach の間だけ通電し、真ん中を過ぎたら切る。
                        float ahead = (_stations[i] - along) * sign;
                        if (ahead >= 0f && ahead <= EnergizeReach)
                        {
                            drive[i] = Mathf.Max(drive[i], 1f - ahead / EnergizeReach * 0.4f);
                        }
                    }
                }
            }
            float time = Time.time;
            for (int i = 0; i < n; i++)
            {
                bool on = drive[i] > 0f;
                if (on && !_energized[i])
                {
                    Firings++;
                    if (i < _sparks.Length && _sparks[i] != null)
                    {
                        _sparks[i].Emit(18);
                    }
                }
                _energized[i] = on;
                // 待機: 1.6 秒ごとに、内側の輪が順に淡く光る（点検の光）。
                float idle = 0.12f + 0.18f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(time * 3.9f - i * 1.1f)), 6f);
                float target = on ? 0.6f + 0.4f * drive[i] : idle;
                // 通電は一瞬で、切れると少し残光。
                _glow[i] = target > _glow[i] ? target : Mathf.MoveTowards(_glow[i], target, Time.deltaTime * 3f);
                if (i < _liners.Length && _liners[i] != null)
                {
                    _liners[i].GetPropertyBlock(_block);
                    _block.SetColor(ColorId, _color);
                    _block.SetFloat(IntensityId, 0.2f + 3.2f * _glow[i]);
                    _liners[i].SetPropertyBlock(_block);
                }
            }
        }
    }
}
