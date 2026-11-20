using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 研究所のレーザーの網（周期のある危険。2026-11-20 の方針: 実際の現象に寄せ、判定を見た目に合わせる。表示だけ）。レーザーの光は、
    /// きれいな空気の中では横からは見えず、煙・霧・ちりのような小さな粒が光を横へ散らす所だけが見える（チンダル現象。粒が多ければ
    /// 続いた線に、少なければ光る点の並びに見える。ブリタニカ・Wikipedia）。
    /// 両端の発光装置（制御甲板の両端の柱の下に吊った箱と赤いレンズ）の間に、効いている間は細い赤い光線が判定の高さに 3 本走り、
    /// わずかにちらつく（光線そのものは StageZoneView が効いている間だけ出す物）。光線の上では空気中のちりがきらめく（粒は HazardEffectRig）。
    /// 予告の間は、真ん中の 1 本が弱い照準の光線として途切れ途切れに走り、レンズが点滅する。
    /// </summary>
    public sealed class LaserGridView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("効いている間の光線")]
        [SerializeField] private LineRenderer[] _beams = new LineRenderer[0];
        [Tooltip("予告の間の照準の光線")]
        [SerializeField] private LineRenderer _aim;
        [Tooltip("発光装置のレンズ（Airerage/Emissive）")]
        [SerializeField] private Renderer[] _lenses = new Renderer[0];

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private MaterialPropertyBlock _block;

        /// <summary>照準の光線を出しているか（テスト用）。</summary>
        public bool Aiming => _aim != null && _aim.enabled;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, LineRenderer[] beams, LineRenderer aim, Renderer[] lenses)
        {
            _view = view;
            _beams = beams;
            _aim = aim;
            _lenses = lenses;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_aim != null)
            {
                _aim.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_view == null)
            {
                return;
            }
            bool active = _view.ShowsActive && _view.ActiveElapsedTicks >= 0;
            bool warning = _view.ShowsWarning && !active;
            if (active)
            {
                // 光線: 太さがわずかに揺らぐ（空気のゆらぎ）。
                for (int i = 0; i < _beams.Length; i++)
                {
                    float width = 0.07f + 0.02f * Mathf.PerlinNoise(Time.time * 17f, i * 3.1f);
                    _beams[i].startWidth = width;
                    _beams[i].endWidth = width;
                }
            }
            if (_aim != null)
            {
                // 照準の光線: 途切れ途切れに（効き始めが近いほど長く点く）。
                bool on = warning && Mathf.PerlinNoise(Time.time * 11f, 0.5f) > 0.35f;
                if (_aim.enabled != on)
                {
                    _aim.enabled = on;
                }
            }
            float lens = active ? 5f : warning ? (Mathf.Repeat(Time.time * 4f, 1f) < 0.5f ? 4f : 0.8f) : 0.8f;
            foreach (Renderer renderer in _lenses)
            {
                if (renderer == null)
                {
                    continue;
                }
                renderer.GetPropertyBlock(_block);
                _block.SetFloat(IntensityId, lens);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
