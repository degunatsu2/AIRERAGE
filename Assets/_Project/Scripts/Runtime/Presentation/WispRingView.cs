using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 恐怖の館の鬼火の輪（加速リング + ZoneLook.Wisp。2026-11-21 の方針: 汎用のギミックの見た目を変えるときは、効果と配置も見た目に合わせる。
    /// 表示だけ）。鬼火（ウィル・オ・ウィスプ）は、沼や墓地の地面から数メートルの所に浮かぶ小さな青白い火の玉で、煙を出さず、ゆらゆら漂い、
    /// 人が近づくと離れていくと伝えられる（記録と伝承のまとめ）。
    /// - 青白い鬼火が輪の形に連なってゆっくり回り、一つずつ明るさと大きさが揺らぐ（Airerage/Wisp の板）。
    /// - 輪の中から、くぐる向きへ鬼火の粒がこぼれて流れる（粒はシーンの ParticleSystem）。
    /// - 誰かがくぐると、鬼火がぱっと明るくなって輪が広がり、粒が散る。
    /// 輪は漂う（StageZone の動き。判定も一緒に動く）。当たり（加速）は輪の中だけ（StageZone のまま）。
    /// </summary>
    public sealed class WispRingView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("輪に連なる鬼火（Airerage/Wisp の板）")]
        [SerializeField] private Transform[] _wisps = new Transform[0];
        [Tooltip("輪の半径（横・縦。m）")]
        [SerializeField] private Vector2 _radii = new Vector2(1.4f, 1.7f);
        [Tooltip("くぐったときに散る粒")]
        [SerializeField] private ParticleSystem _burst;

        /// <summary>回る速さ（ラジアン/秒）。</summary>
        public const float OrbitSpeed = 0.9f;

        private int[] _passes = new int[0];
        private float _flare;
        private float[] _baseSize = new float[0];
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;

        /// <summary>くぐられて明るくなっているか（テスト用）。</summary>
        public bool Flaring => _flare > 0.05f;

        /// <summary>くぐった回数（この輪で数えた分。テスト用）。</summary>
        public int PassesSeen { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform[] wisps, Vector2 radii, ParticleSystem burst)
        {
            _view = view;
            _wisps = wisps;
            _radii = radii;
            _burst = burst;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _baseSize = new float[_wisps.Length];
            for (int i = 0; i < _wisps.Length; i++)
            {
                _baseSize[i] = _wisps[i] != null ? _wisps[i].localScale.x : 1f;
            }
        }

        private void LateUpdate()
        {
            DetectPasses();
            _flare = Mathf.MoveTowards(_flare, 0f, Time.deltaTime * 1.6f);
            float spread = 1f + 0.18f * _flare;
            int count = _wisps.Length;
            for (int i = 0; i < count; i++)
            {
                Transform wisp = _wisps[i];
                if (wisp == null)
                {
                    continue;
                }
                float angle = i * Mathf.PI * 2f / Mathf.Max(1, count) + Time.time * OrbitSpeed;
                // 輪の線から少し外れて漂い、奥行きも少し揺れる。
                float drift = 0.12f * Mathf.Sin(Time.time * 1.3f + i * 2.3f);
                float rx = (_radii.x + drift) * spread;
                float ry = (_radii.y + drift) * spread;
                wisp.localPosition = new Vector3(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry + 0.1f * Mathf.Sin(Time.time * 2.1f + i),
                    0.25f * Mathf.Sin(angle * 2f + Time.time));
                float flicker = Mathf.PerlinNoise(Time.time * 2.4f, i * 1.37f);
                float size = _baseSize.Length > i ? _baseSize[i] : 1f;
                wisp.localScale = Vector3.one * size * (0.75f + 0.5f * flicker + 0.6f * _flare);
                var renderer = wisp.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.GetPropertyBlock(_block);
                    float glow = 0.65f + 0.6f * flicker + 1.6f * _flare;
                    _block.SetColor(ColorId, new Color(0.9f * glow, 1.7f * glow, 1.4f * glow, 1f));
                    renderer.SetPropertyBlock(_block);
                }
            }
        }

        // 誰かが輪をくぐったか（加速リングを通った回数が増え、この輪の近くにいる）。
        private void DetectPasses()
        {
            ISimulationFeed feed = _view != null ? _view.Feed : null;
            if (feed == null)
            {
                return;
            }
            if (_passes.Length != feed.CharacterCount)
            {
                _passes = new int[feed.CharacterCount];
                for (int i = 0; i < _passes.Length; i++)
                {
                    _passes[i] = feed.GetState(i).BoostRingCount;
                }
                return;
            }
            Vector3 center = _view.ShownCenter;
            for (int i = 0; i < _passes.Length; i++)
            {
                CharacterState state = feed.GetState(i);
                if (state.BoostRingCount > _passes[i])
                {
                    var position = new Vector3(state.Position.X, state.Position.Y, 0f);
                    if (Vector3.Distance(position, center) < Mathf.Max(_radii.x, _radii.y) + 2.5f)
                    {
                        _flare = 1f;
                        PassesSeen++;
                        if (_burst != null)
                        {
                            _burst.transform.position = new Vector3(position.x, position.y, -0.2f);
                            _burst.Emit(18);
                        }
                    }
                }
                _passes[i] = state.BoostRingCount;
            }
        }
    }
}
