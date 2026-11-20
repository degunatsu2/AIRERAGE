using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 危険のギミックの粒の演出（2026-11-20 の方針: ギミックを実際の現象・ゲームの演出に寄せる。表示だけ）。同じ物体の
    /// StageZoneView の状態（予告・効いている・柱の位置）を見て、粒（ParticleSystem）の出し方を変える:
    /// 予告の間だけ出す粒（火口の光・湯気・前ぶれの火の粉）、効き始めた瞬間に一度に出す粒（噴き出しの塊）、効いている間に出し続ける粒
    /// （飛び散る塊・火の粉・煙）、効き終わった瞬間に一度に出す粒（床で砕けたつららのかけら）。出し終えた粒は消さずに最後まで飛ばす
    /// （効き終わった後も落ちていく塊と残る煙）。
    /// 柱の位置が周期ごとに変わる危険（溶岩流）は、粒の物体を柱の真ん中へ動かす。
    /// </summary>
    public sealed class HazardEffectRig : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("柱の真ん中へ動かす物体（なければ動かさない）")]
        [SerializeField] private Transform _follow;
        [Tooltip("予告の間だけ出す粒と、その出す量（個/秒）")]
        [SerializeField] private ParticleSystem[] _warning = new ParticleSystem[0];
        [SerializeField] private float[] _warningRates = new float[0];
        [Tooltip("効き始めた瞬間に一度に出す粒と、その数")]
        [SerializeField] private ParticleSystem[] _burst = new ParticleSystem[0];
        [SerializeField] private int[] _burstCounts = new int[0];
        [Tooltip("効いている間に出し続ける粒と、その出す量（個/秒）")]
        [SerializeField] private ParticleSystem[] _active = new ParticleSystem[0];
        [SerializeField] private float[] _activeRates = new float[0];
        [Tooltip("効き終わった瞬間に一度に出す粒と、その数（落ちたつららが床で砕ける）")]
        [SerializeField] private ParticleSystem[] _endBurst = new ParticleSystem[0];
        [SerializeField] private int[] _endBurstCounts = new int[0];

        private bool _wasActive;

        /// <summary>今、効いている粒を出しているか（テスト用）。</summary>
        public bool Erupting { get; private set; }

        /// <summary>今、予告の粒を出しているか（テスト用）。</summary>
        public bool Warning { get; private set; }

        /// <summary>状態を見るギミックの表示（テストと撮影用）。</summary>
        public StageZoneView View => _view;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform follow, ParticleSystem[] warning, float[] warningRates, ParticleSystem[] burst, int[] burstCounts,
            ParticleSystem[] active, float[] activeRates)
        {
            _view = view;
            _follow = follow;
            _warning = warning;
            _warningRates = warningRates;
            _burst = burst;
            _burstCounts = burstCounts;
            _active = active;
            _activeRates = activeRates;
        }

        /// <summary>効き終わった瞬間に一度に出す粒を決める（シーンのビルダー用）。</summary>
        public void ConfigureEndBurst(ParticleSystem[] endBurst, int[] endBurstCounts)
        {
            _endBurst = endBurst;
            _endBurstCounts = endBurstCounts;
        }

        private void Start()
        {
            SetRates(_warning, _warningRates, false);
            SetRates(_active, _activeRates, false);
        }

        private void LateUpdate()
        {
            if (_view == null)
            {
                return;
            }
            bool active = _view.ShowsActive;
            bool warning = _view.ShowsWarning && !active;
            if (_follow != null && (active || warning))
            {
                Vector2 column = _view.ShownColumn;
                if (column.y > column.x)
                {
                    Vector3 p = _follow.position;
                    p.x = (column.x + column.y) * 0.5f;
                    _follow.position = p;
                }
            }
            if (active && !_wasActive)
            {
                Emit(_burst, _burstCounts);
            }
            if (!active && _wasActive)
            {
                Emit(_endBurst, _endBurstCounts);
            }
            _wasActive = active;
            Erupting = active;
            Warning = warning;
            SetRates(_warning, _warningRates, warning);
            SetRates(_active, _activeRates, active);
        }

        private static void Emit(ParticleSystem[] systems, int[] counts)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] != null)
                {
                    systems[i].Emit(i < counts.Length ? counts[i] : 20);
                }
            }
        }

        // 出す量を変える（止めても、出した粒は最後まで飛ぶ）。
        private static void SetRates(ParticleSystem[] systems, float[] rates, bool on)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null)
                {
                    continue;
                }
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = on ? (i < rates.Length ? rates[i] : 10f) : 0f;
                if (on && !system.isPlaying)
                {
                    system.Play();
                }
            }
        }
    }
}
