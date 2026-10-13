using System.Collections.Generic;
using Degunatsu.Core.Economy;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// フィールドのコインの表示（docs/12_match_plan.md T4-4）。金色のコインが回りながら、少し上下に揺れる。枚数が多いほど大きい。
    /// 出たときは小さくから大きくなる。状態（CoinField）を読むだけで、書き換えない。見た目は _template を複製して作る。
    /// </summary>
    public sealed class CoinView : MonoBehaviour
    {
        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("コイン1つの見た目（シーンでは隠しておく。複製して使う）")]
        [SerializeField] private GameObject _template;
        [SerializeField] private float _spinDegreesPerSecond = 160f;
        [SerializeField] private float _bobHeight = 0.12f;
        [SerializeField] private float _popSeconds = 0.25f;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly Stack<GameObject> _pool = new Stack<GameObject>();
        private readonly List<int> _gone = new List<int>();
        private ISimulationFeed _feed;
        private Vector3 _templateScale;

        /// <summary>表示しているコインの数（テスト用）。</summary>
        public int ShownCount => _shown.Count;

        private sealed class Shown
        {
            public GameObject Visual;
            public float BornAt;
            public float Scale;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _template == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、コインの見た目が設定されていません", this);
                enabled = false;
                return;
            }
            _templateScale = _template.transform.localScale;
            _template.SetActive(false);
        }

        private void Update()
        {
            CoinField field = _feed.Coins;
            _gone.Clear();
            foreach (int id in _shown.Keys)
            {
                if (field == null || field.IndexOf(id) < 0)
                {
                    _gone.Add(id);
                }
            }
            foreach (int id in _gone)
            {
                Shown shown = _shown[id];
                shown.Visual.SetActive(false);
                _pool.Push(shown.Visual);
                _shown.Remove(id);
            }
            if (field == null)
            {
                return;
            }

            float time = Time.time;
            foreach (FieldCoin coin in field.Coins)
            {
                if (!_shown.TryGetValue(coin.Id, out Shown shown))
                {
                    shown = new Shown { Visual = Take(), BornAt = time, Scale = ScaleOf(coin.Value) };
                    _shown.Add(coin.Id, shown);
                }
                float pop = Mathf.Clamp01((time - shown.BornAt) / _popSeconds);
                float bob = Mathf.Sin(time * 2.5f + coin.Id) * _bobHeight;
                Transform t = shown.Visual.transform;
                t.position = new Vector3(coin.Position.X, coin.Position.Y + bob, 0f);
                // 面をカメラに向けたまま、縦の軸で回す。
                t.rotation = Quaternion.Euler(0f, time * _spinDegreesPerSecond + coin.Id * 37f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                t.localScale = _templateScale * (shown.Scale * Mathf.SmoothStep(0f, 1f, pop));
            }
        }

        private GameObject Take()
        {
            GameObject visual = _pool.Count > 0 ? _pool.Pop() : Instantiate(_template, transform);
            visual.SetActive(true);
            return visual;
        }

        // 枚数が多いほど大きく（5 枚で 1 倍、20 枚で約 1.5 倍）。
        private static float ScaleOf(int value)
        {
            return Mathf.Clamp(1f + 0.25f * Mathf.Log(Mathf.Max(1, value) / 5f, 2f), 0.7f, 1.8f);
        }
    }
}
