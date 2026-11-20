using Airerage.Core.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 落ちてくるつらら（2026-11-20 の方針: ギミックを実際の現象に寄せ、判定を見た目に合わせる。表示だけ）。天井には並んだつららが
    /// 下がっている。予告の間は、落ちる柱の所のつららが小刻みに揺れ（ひびが入る）、氷のかけらとしずくが落ちる（粒は HazardEffectRig）。
    /// 効いている間は、そのつららが天井から外れて、判定の帯（Core の StageZone.FallBand）と同じ動きで、だんだん速く落ちる。床に着いた
    /// 瞬間に砕け（粒は HazardEffectRig の終わりの粒）、天井のつららは少しずつ伸びて元に戻る。
    /// </summary>
    public sealed class IcicleFallView : MonoBehaviour
    {
        [Tooltip("状態を見るギミックの表示")]
        [SerializeField] private StageZoneView _view;
        [Tooltip("天井に下がっているつらら（根元が天井。下へ伸びる）")]
        [SerializeField] private Transform[] _hanging = new Transform[0];
        [Tooltip("落ちていくつらら（根元を原点に下へ伸びる。落ちていない間は隠す）")]
        [SerializeField] private Transform _falling;
        [Tooltip("天井のつららが落ちてから元の長さに戻るまで（秒）")]
        [SerializeField] private float _regrowSeconds = 2.5f;

        private Vector3[] _scales;
        private float[] _regrowStart;
        private int _current = -1;
        private bool _wasActive;
        private float _warningStart = -1f;

        /// <summary>今、落ちていく途中か（テスト用）。</summary>
        public bool Falling => _falling != null && _falling.gameObject.activeSelf;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneView view, Transform[] hanging, Transform falling)
        {
            _view = view;
            _hanging = hanging;
            _falling = falling;
        }

        private void Awake()
        {
            _scales = new Vector3[_hanging.Length];
            _regrowStart = new float[_hanging.Length];
            for (int i = 0; i < _hanging.Length; i++)
            {
                _scales[i] = _hanging[i].localScale;
                _regrowStart[i] = -100f;
            }
            if (_falling != null)
            {
                _falling.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            if (_view == null || _hanging.Length == 0)
            {
                return;
            }
            bool active = _view.ShowsActive && _view.ActiveElapsedTicks >= 0;
            bool warning = _view.ShowsWarning && !active;
            Vector2 column = _view.ShownColumn;
            if ((warning || active) && column.y > column.x && (_current < 0 || (!_wasActive && _warningStart < 0f)))
            {
                _current = Nearest((column.x + column.y) * 0.5f);
            }
            if (warning)
            {
                if (_warningStart < 0f)
                {
                    _warningStart = Time.time;
                }
                Shake(_current, Time.time - _warningStart);
            }
            else
            {
                _warningStart = -1f;
            }
            if (active && _current >= 0)
            {
                Transform icicle = _hanging[_current];
                icicle.localRotation = Quaternion.identity;
                icicle.localScale = Vector3.zero;
                _regrowStart[_current] = float.MaxValue;
                StageZone zone = _view.CoreZone;
                zone.FallBand(_view.ActiveElapsedTicks, out _, out float top);
                _falling.gameObject.SetActive(true);
                _falling.position = new Vector3(icicle.position.x, top, icicle.position.z);
                _falling.localScale = _scales[_current];
            }
            if (!active && _wasActive)
            {
                // 床に着いて砕けた: 落ちていたつららを隠し、天井のつららを伸ばし始める。
                _falling.gameObject.SetActive(false);
                if (_current >= 0)
                {
                    _regrowStart[_current] = Time.time + 0.6f;
                }
                _current = -1;
            }
            _wasActive = active;
            Regrow();
        }

        private int Nearest(float x)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _hanging.Length; i++)
            {
                float distance = Mathf.Abs(_hanging[i].position.x - x);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        // ひびが入って小刻みに揺れる（落ちる直前ほど強く）。
        private void Shake(int index, float elapsed)
        {
            if (index < 0)
            {
                return;
            }
            float strength = Mathf.Lerp(0.6f, 3.5f, Mathf.Clamp01(elapsed / 1.2f));
            _hanging[index].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 55f) * strength);
        }

        // 落ちた後の天井のつららを、少しずつ元の長さへ。
        private void Regrow()
        {
            for (int i = 0; i < _hanging.Length; i++)
            {
                if (_regrowStart[i] < -50f || i == _current)
                {
                    continue;
                }
                float t = (Time.time - _regrowStart[i]) / _regrowSeconds;
                if (t < 0f)
                {
                    _hanging[i].localScale = Vector3.zero;
                    continue;
                }
                float grow = Mathf.Clamp01(t);
                _hanging[i].localScale = new Vector3(_scales[i].x * Mathf.Lerp(0.4f, 1f, grow), _scales[i].y * grow, _scales[i].z * Mathf.Lerp(0.4f, 1f, grow));
                if (grow >= 1f)
                {
                    _regrowStart[i] = -100f;
                    _hanging[i].localScale = _scales[i];
                }
            }
        }
    }
}
