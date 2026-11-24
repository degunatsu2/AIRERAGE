using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ドーナツフライヤー（キッチン。StageTrapKind.DonutFryer。2026-11-24 の要望。表示だけ）。出ているドーナツの位置は Core の仕掛けの道のり
    /// （StageTrap.TryGetDonut。ティック数だけで決まる）で、ここではそれを見せる:
    /// - ドーナツは転がる向きに回り（横の速さ ÷ 半径）、着地の瞬間に少しつぶれて（横に広がり縦に縮む）、跳ねて戻る。
    /// - フライヤーの油は、ドーナツが出る少し前に泡立ち、出るときにドーナツがシュートを滑り出る（油のしずくが少し飛ぶ）。
    /// 当たり（触れると弾く・真ん中がアンカー）は Core（StageTraps）。
    /// </summary>
    public sealed class DonutFryerView : MonoBehaviour
    {
        [Tooltip("ドーナツの見た目（番号ごと。数は StageTrap.DonutSlots 以上）と、油の泡・しずく（なくてもよい）")]
        [SerializeField] private Transform[] _donuts = new Transform[0];
        [Tooltip("次に出るドーナツがシュートを滑ってくる見た目と、シュートの上の端（この仕掛けからの位置。出口は原点）")]
        [SerializeField] private Transform _onChute;
        [SerializeField] private Vector3 _chuteStart = new Vector3(-2.3f, 0.7f, 0f);
        [SerializeField] private ParticleSystem _bubbles;
        [SerializeField] private ParticleSystem _drips;

        /// <summary>出る前に、シュートを滑ってくる長さ（ティック）。</summary>
        public const int ChuteTicks = 30;

        /// <summary>着地でつぶれる強さと、戻るまでの長さ（ティック）。</summary>
        public const float SquashAmount = 0.22f;
        public const int SquashTicks = 9;

        private StageTrapAuthoring _authoring;
        private StageTrap _trap;
        private ISimulationFeed _feed;
        private float _nextFeedSearch;
        private int _lastTick = int.MinValue;
        private float _tickChangedAt;
        private float[] _spin = new float[0];
        private int[] _lastAge = new int[0];

        /// <summary>今見せているドーナツの数（テスト用）。</summary>
        public int Shown { get; private set; }

        /// <summary>次のドーナツがシュートを滑っているか（テスト用）。</summary>
        public bool OnChute => _onChute != null && _onChute.gameObject.activeSelf;

        /// <summary>ドーナツが出た回数（テスト用）。</summary>
        public int Launches { get; private set; }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Transform[] donuts, Transform onChute, Vector3 chuteStart, ParticleSystem bubbles, ParticleSystem drips)
        {
            _donuts = donuts;
            _onChute = onChute;
            _chuteStart = chuteStart;
            _bubbles = bubbles;
            _drips = drips;
        }

        private void Awake()
        {
            _authoring = GetComponent<StageTrapAuthoring>();
            if (_authoring != null)
            {
                _trap = _authoring.ToTrap();
            }
            _spin = new float[_donuts.Length];
            _lastAge = new int[_donuts.Length];
            for (int i = 0; i < _lastAge.Length; i++)
            {
                _lastAge[i] = -1;
            }
        }

        private void LateUpdate()
        {
            if (_trap == null)
            {
                return;
            }
            FindFeed();
            int tick = _feed != null && _feed.CharacterCount > 0 ? _feed.CurrentTick : Mathf.FloorToInt(Time.time * SimTime.TicksPerSecond);
            if (tick != _lastTick)
            {
                _lastTick = tick;
                _tickChangedAt = Time.unscaledTime;
            }
            // ティックの間を滑らかに（次のティックの位置へ寄せる）。
            float between = Mathf.Clamp01((Time.unscaledTime - _tickChangedAt) * SimTime.TicksPerSecond);
            int shown = 0;
            float radius = _trap.HalfSize.X;
            for (int slot = 0; slot < _donuts.Length; slot++)
            {
                Transform donut = _donuts[slot];
                if (donut == null)
                {
                    continue;
                }
                int age = 0;
                System.Numerics.Vector2 now = default;
                System.Numerics.Vector2 velocity = default;
                bool alive = slot < _trap.DonutSlots && _trap.TryGetDonut(tick, slot, out age, out now, out velocity);
                if (!alive)
                {
                    donut.gameObject.SetActive(false);
                    _lastAge[slot] = -1;
                    continue;
                }
                if (_lastAge[slot] < 0 || age < _lastAge[slot])
                {
                    Launches++;
                    if (_drips != null)
                    {
                        _drips.Emit(10);
                    }
                }
                _lastAge[slot] = age;
                Vector3 position = new Vector3(now.X, now.Y, 0f);
                if (_trap.TryGetDonut(tick + 1, slot, out int nextAge, out System.Numerics.Vector2 next, out _) && nextAge == age + 1)
                {
                    position = Vector3.Lerp(position, new Vector3(next.X, next.Y, 0f), between);
                }
                donut.gameObject.SetActive(true);
                donut.position = position;
                // 転がる: 横の速さ ÷ 半径（度/秒）。
                _spin[slot] -= velocity.X / radius * Mathf.Rad2Deg * Time.deltaTime;
                float squash = Squash(age);
                donut.localRotation = Quaternion.Euler(0f, 0f, _spin[slot]);
                donut.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash * 0.5f);
                shown++;
            }
            Shown = shown;
            long shifted = (long)tick + _trap.PhaseTicks;
            int untilNext = shifted < 0 ? int.MaxValue : _trap.Period - (int)(shifted % _trap.Period);
            if (_bubbles != null)
            {
                // ドーナツが出る少し前（周期の最後の 1 秒）は、油が強く泡立つ。
                ParticleSystem.EmissionModule emission = _bubbles.emission;
                emission.rateOverTime = untilNext < SimTime.TicksPerSecond ? 40f : 8f;
            }
            RenderChute(untilNext, between, radius);
        }

        // 出る前の ChuteTicks の間、次のドーナツが油槽の上からシュートを滑って出口へ来る（だんだん速く。転がる）。出た瞬間に本物と入れ替わる。
        private void RenderChute(int untilNext, float between, float radius)
        {
            if (_onChute == null)
            {
                return;
            }
            bool sliding = untilNext <= ChuteTicks && untilNext > 0;
            _onChute.gameObject.SetActive(sliding);
            if (!sliding)
            {
                return;
            }
            float k = Mathf.Clamp01((ChuteTicks - untilNext + between) / ChuteTicks);
            float eased = k * k;
            Vector3 local = Vector3.Lerp(_chuteStart, Vector3.zero, eased);
            _onChute.position = transform.TransformPoint(local);
            float rolled = Vector3.Distance(_chuteStart, local) / Mathf.Max(0.01f, radius) * Mathf.Rad2Deg;
            _onChute.localRotation = Quaternion.Euler(0f, 0f, -rolled);
            _onChute.localScale = Vector3.one;
        }

        // 着地の後の SquashTicks の間のつぶれ（着地の瞬間がいちばん強い）。
        private float Squash(int age)
        {
            var landings = _trap.LandingTicks;
            for (int i = 0; i < landings.Count; i++)
            {
                int since = age - landings[i];
                if (since >= 0 && since < SquashTicks)
                {
                    float t = since / (float)SquashTicks;
                    return SquashAmount * (1f - t) * Mathf.Cos(t * Mathf.PI * 1.5f);
                }
            }
            return 0f;
        }

        private void FindFeed()
        {
            if (_feed is MonoBehaviour gone && gone == null)
            {
                _feed = null;
            }
            if (_feed != null || Time.unscaledTime < _nextFeedSearch)
            {
                return;
            }
            _nextFeedSearch = Time.unscaledTime + 1f;
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is ISimulationFeed feed)
                {
                    _feed = feed;
                    break;
                }
            }
        }
    }
}
