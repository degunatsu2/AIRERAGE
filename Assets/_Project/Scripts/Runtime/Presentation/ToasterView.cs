using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// トースター（キッチンの周期のある発射台。2026-11-20 の方針: ギミックを実際の物に寄せ、判定を見た目に合わせる。表示だけ）。手本は
    /// ポップアップ式のトースター: レバーを下げるとパンを載せた台が沈み、ニクロム線の電熱線が赤く光ってパンを焼く。時間が来ると留め具が
    /// 外れ、ばねで台が跳ね上がってパンが飛び出す（HowStuffWorks・Wikipedia）。
    /// 予告の間（焼いている間）はレバーが下がったまま、溝の奥の電熱線がだんだん赤〜橙に光る。効き始めた瞬間に、2 枚のパンが溝から
    /// 勢いよく跳ね上がり（判定: 溝の上に乗っている人を打ち上げる）、少し戻って溝から頭を出したまま止まる。レバーも跳ね上がる。
    /// 次に焼き始める少し前に、パンとレバーが下がる。粒（パンくずと熱気）は HazardEffectRig。
    /// </summary>
    public sealed class ToasterView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("パン（溝の中で焼いている位置が原点。上へ跳ね上がる）")]
        [SerializeField] private Transform[] _toast = new Transform[0];
        [Tooltip("レバー（下がった位置が原点。上へ戻る）")]
        [SerializeField] private Transform _lever;
        [Tooltip("溝の奥の電熱線（Airerage/Emissive。焼いている間だけ光る）")]
        [SerializeField] private Renderer[] _elements = new Renderer[0];
        [SerializeField] private float _popHeight = 1.25f;
        [SerializeField] private float _restHeight = 0.7f;
        [SerializeField] private float _leverTravel = 0.8f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private MaterialPropertyBlock _block;
        private Vector3[] _toastRest;
        private Vector3 _leverRest;
        private float _poppedAt = -100f;
        private bool _wasActive;
        private float _up;

        /// <summary>パンが出ている高さ（0 なら溝の中。テスト用）。</summary>
        public float ToastHeight => _up;

        /// <summary>電熱線の光の強さ（0〜。テスト用）。</summary>
        public float Glow { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform[] toast, Transform lever, Renderer[] elements)
        {
            _view = view;
            _toast = toast;
            _lever = lever;
            _elements = elements;
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _toastRest = new Vector3[_toast.Length];
            for (int i = 0; i < _toast.Length; i++)
            {
                _toastRest[i] = _toast[i].localPosition;
            }
            if (_lever != null)
            {
                _leverRest = _lever.localPosition;
            }
        }

        private void LateUpdate()
        {
            if (_view == null)
            {
                return;
            }
            bool active = _view.ShowsActive && _view.ActiveElapsedTicks >= 0;
            bool heating = _view.ShowsWarning && !active;
            if (active && !_wasActive)
            {
                _poppedAt = Time.time;
            }
            _wasActive = active;
            float since = Time.time - _poppedAt;
            // パン: 跳ね上がって（0.12 秒）、少し戻って頭を出したまま。焼き始める少し前（予告の 0.4 秒前から）に下がる。
            float target;
            if (heating)
            {
                target = 0f;
            }
            else if (since < 0.12f)
            {
                target = _popHeight * Mathf.Sin(since / 0.12f * Mathf.PI * 0.5f);
            }
            else if (since < 0.5f)
            {
                target = Mathf.Lerp(_popHeight, _restHeight, Mathf.SmoothStep(0f, 1f, (since - 0.12f) / 0.38f));
            }
            else
            {
                int until = _view.TicksUntilActive;
                int warning = _view.CoreZone.Timing.WarningTicks;
                // 予告の 0.4 秒前から下げ始める（レバーを押し下げる）。
                float lower = until > 0 ? Mathf.Clamp01(1f - (until - warning) / 24f) : 0f;
                target = since < 100f ? Mathf.Lerp(_restHeight, 0f, lower) : 0f;
            }
            _up = target;
            for (int i = 0; i < _toast.Length; i++)
            {
                _toast[i].localPosition = _toastRest[i] + Vector3.up * (_up + (i == 1 ? -0.04f : 0f));
            }
            if (_lever != null)
            {
                // レバー: パンと一緒に上がり、下げると溝の下の端へ。
                float leverUp = _restHeight > 0f ? Mathf.Clamp01(_up / _restHeight) : 0f;
                _lever.localPosition = _leverRest + Vector3.up * (_leverTravel * leverUp);
            }
            // 電熱線: 焼いている間だけ、だんだん明るく（予告の長さで 0 → 1。少し揺らぐ）。
            float glow = 0f;
            if (heating)
            {
                int warning = Mathf.Max(1, _view.CoreZone.Timing.WarningTicks);
                glow = Mathf.Clamp01(1f - _view.TicksUntilActive / (float)warning);
                glow = glow * (0.9f + 0.1f * Mathf.Sin(Time.time * 23f));
            }
            Glow = glow;
            foreach (Renderer element in _elements)
            {
                if (element == null)
                {
                    continue;
                }
                element.GetPropertyBlock(_block);
                _block.SetFloat(IntensityId, glow * 4f);
                element.SetPropertyBlock(_block);
            }
        }
    }
}
