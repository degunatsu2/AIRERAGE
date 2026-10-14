using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 虹色に光るコイン（100 枚。docs/12_match_plan.md 3.5）の光（表示だけ）: コインの色が虹色に移り変わり、周りに虹色の輪が
    /// 脈打ち、外へ広がって消える輪を繰り返す。大きさはこの物体の大きさ（CoinView が出たときに大きくする）に合わせる。
    /// 動きは Time.time で決める（巻き戻しとは関係ない）。
    /// </summary>
    public sealed class RainbowCoinGlow : MonoBehaviour
    {
        private const int RingSegments = 48;
        private const int GradientKeys = 8;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("色を変えるコインの面")]
        [SerializeField] private Renderer[] _faces = new Renderer[0];
        [Tooltip("周りの輪（脈打つ）")]
        [SerializeField] private LineRenderer _halo;
        [Tooltip("外へ広がって消える輪")]
        [SerializeField] private LineRenderer _wave;
        [SerializeField] private float _haloRadius = 0.85f;
        [SerializeField] private float _waveRadius = 2.2f;
        [Tooltip("色がひと回りする秒数")]
        [SerializeField] private float _hueSeconds = 1.6f;
        [Tooltip("広がる輪の周期（秒）")]
        [SerializeField] private float _waveSeconds = 1.1f;

        private readonly GradientColorKey[] _colorKeys = new GradientColorKey[GradientKeys];
        private readonly GradientAlphaKey[] _alphaKeys = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        private readonly Gradient _gradient = new Gradient();
        private MaterialPropertyBlock _block;

        /// <summary>今のコインの色（テスト用）。</summary>
        public Color CurrentColor { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Renderer[] faces, LineRenderer halo, LineRenderer wave)
        {
            _faces = faces;
            _halo = halo;
            _wave = wave;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            foreach (LineRenderer line in new[] { _halo, _wave })
            {
                if (line != null)
                {
                    line.positionCount = RingSegments;
                    line.loop = true;
                    line.useWorldSpace = true;
                }
            }
        }

        private void LateUpdate()
        {
            float hue = Mathf.Repeat(Time.time / Mathf.Max(0.1f, _hueSeconds), 1f);
            CurrentColor = Color.HSVToRGB(hue, 0.75f, 1f);
            foreach (Renderer face in _faces)
            {
                face.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, CurrentColor);
                face.SetPropertyBlock(_block);
            }

            float scale = transform.localScale.x;
            Vector3 center = transform.position;
            if (_halo != null)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 7f);
                Ring(_halo, center, _haloRadius * pulse * scale, hue, 1f);
            }
            if (_wave != null)
            {
                float phase = Mathf.Repeat(Time.time / Mathf.Max(0.1f, _waveSeconds), 1f);
                Ring(_wave, center, Mathf.Lerp(_haloRadius, _waveRadius, phase) * scale, hue + 0.5f, (1f - phase) * 0.8f);
            }
        }

        // 輪を描き、色を一周の虹にする（hue だけ回す）。
        private void Ring(LineRenderer line, Vector3 center, float radius, float hue, float alpha)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            for (int k = 0; k < GradientKeys; k++)
            {
                float t = k / (float)(GradientKeys - 1);
                _colorKeys[k] = new GradientColorKey(Color.HSVToRGB(Mathf.Repeat(hue + t, 1f), 0.7f, 1f), t);
            }
            _alphaKeys[0].alpha = alpha;
            _alphaKeys[1].alpha = alpha;
            _gradient.SetKeys(_colorKeys, _alphaKeys);
            line.colorGradient = _gradient;
        }
    }
}
