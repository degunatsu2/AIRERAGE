using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 研究所の気送管（いつも吹く上向きの風 + ZoneLook.Pneumatic。2026-11-21 の方針: 実際の現象に寄せ、判定を見た目に合わせる。表示だけ）。
    /// 手本は病院・工場の気送管（エアシューター）: 送風機が筒（キャリア）の後ろの空気を押し、行き先の近くでは逆向きの空気でゆっくりに
    /// なってから受け口に入る（気送管の特許の説明）。管の中を、両端にフェルトの帯を巻いた透明の筒が勢いよく昇り、上の受け口の手前で
    /// 速さを落としてから消える。下の送風機の羽根は回り続ける。紙くずと塵が空気に乗って昇る（粒はシーンの ParticleSystem）。
    /// 当たり（風）は管の中だけ（StageZone のまま）。
    /// </summary>
    public sealed class PneumaticTubeView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("昇る筒（キャリア）。順に打ち出す")]
        [SerializeField] private Transform[] _carriers = new Transform[0];
        [Tooltip("送風機の羽根（縦の軸で回す）")]
        [SerializeField] private Transform _fan;
        [Tooltip("筒が出る高さと、受け口の高さ（ギミックの物体からの高さ）")]
        [SerializeField] private float _bottom;
        [SerializeField] private float _top = 28f;

        /// <summary>筒の速さ（m/s。実際の気送管はおよそ 7 m/s。人の乗る大きさの管なので速めに）と、打ち出す間隔（秒）。</summary>
        public const float CarrierSpeed = 24f;
        public const float LaunchInterval = 1.7f;

        /// <summary>受け口の手前で速さを落とす長さ（m）。</summary>
        public const float BrakeLength = 4f;

        private float[] _depth = new float[0];

        /// <summary>今、管の中を昇っている筒の数（テスト用）。</summary>
        public int CarriersInFlight { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform[] carriers, Transform fan, float bottom, float top)
        {
            _view = view;
            _carriers = carriers;
            _fan = fan;
            _bottom = bottom;
            _top = top;
        }

        private void Awake()
        {
            // 筒は作ったときの奥行き（遊ぶ面より奥）を保って昇る。
            _depth = new float[_carriers.Length];
            for (int i = 0; i < _carriers.Length; i++)
            {
                _depth[i] = _carriers[i] != null ? _carriers[i].localPosition.z : 0f;
            }
        }

        private void LateUpdate()
        {
            bool blowing = _view == null || _view.ShowsActive;
            if (_fan != null && blowing)
            {
                _fan.Rotate(0f, 1440f * Time.deltaTime, 0f, Space.Self);
            }
            float travel = TravelSeconds(_top - _bottom);
            float cycle = LaunchInterval * Mathf.Max(1, _carriers.Length);
            int inFlight = 0;
            for (int i = 0; i < _carriers.Length; i++)
            {
                Transform carrier = _carriers[i];
                if (carrier == null)
                {
                    continue;
                }
                // 筒ごとに打ち出す時刻をずらす（間隔は少しばらつかせる）。
                float t = Mathf.Repeat(Time.time + i * LaunchInterval + 0.37f * Mathf.Sin(i * 2.1f), cycle);
                bool shown = blowing && t < travel;
                if (carrier.gameObject.activeSelf != shown)
                {
                    carrier.gameObject.SetActive(shown);
                }
                if (!shown)
                {
                    continue;
                }
                inFlight++;
                float height = HeightAt(t, _top - _bottom);
                // 管の中で少し揺れ、ゆっくり回る。
                float sway = 0.18f * Mathf.Sin(Time.time * 7f + i * 1.7f);
                float depth = _depth.Length > i ? _depth[i] : 0f;
                carrier.localPosition = new Vector3(sway, _bottom + height, depth + 0.15f * Mathf.Cos(Time.time * 5f + i));
                carrier.localRotation = Quaternion.Euler(4f * Mathf.Sin(Time.time * 9f + i), Time.time * 90f + i * 40f, 3f * Mathf.Cos(Time.time * 8f + i));
            }
            CarriersInFlight = inFlight;
        }

        /// <summary>筒が下から受け口まで昇るのにかかる秒数（速さ CarrierSpeed で昇り、最後の BrakeLength で止まるまで速さを落とす）。</summary>
        public static float TravelSeconds(float length)
        {
            float brake = Mathf.Min(BrakeLength, length);
            return (length - brake) / CarrierSpeed + 2f * brake / CarrierSpeed;
        }

        /// <summary>打ち出してから t 秒の高さ（0 から length。最後は一様に速さを落として受け口で止まる）。</summary>
        public static float HeightAt(float t, float length)
        {
            float brake = Mathf.Min(BrakeLength, length);
            float cruise = (length - brake) / CarrierSpeed;
            if (t <= cruise)
            {
                return t * CarrierSpeed;
            }
            float b = Mathf.Min(t - cruise, 2f * brake / CarrierSpeed);
            float deceleration = CarrierSpeed * CarrierSpeed / (2f * Mathf.Max(0.01f, brake));
            return length - brake + CarrierSpeed * b - 0.5f * deceleration * b * b;
        }
    }
}
