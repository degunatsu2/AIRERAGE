using System.Collections.Generic;
using Degunatsu.Core.Economy;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>コインの見た目の種類（枚数で決める。docs/12_match_plan.md 3.5）。</summary>
    public enum CoinLook
    {
        /// <summary>1 枚: 金色のコイン。</summary>
        Single,
        /// <summary>3 枚: 金色のコインがピラミッド型に 3 枚。</summary>
        Triple,
        /// <summary>5 枚: 赤いコイン。</summary>
        Red,
        /// <summary>100 枚: 虹色に光るコイン。</summary>
        Rainbow,
    }

    /// <summary>
    /// フィールドのコインの表示（docs/12_match_plan.md T4-4）。枚数ごとに見た目を変える（CoinLook）: 1 枚は金色、3 枚は金色が
    /// ピラミッド型に 3 枚、5 枚は赤、100 枚は虹色に光る。コインは回りながら、少し上下に揺れる。出たときは小さくから大きくなる。
    /// 状態（CoinField）を読むだけで、書き換えない。見た目は種類ごとの見本を複製して作る。見本の子のうちメッシュを持つもの
    /// （コインの面）を、それぞれの縦の軸で回す。
    /// </summary>
    public sealed class CoinView : MonoBehaviour
    {
        /// <summary>虹色のコインにする枚数（残り 1 分に出るコイン。docs/12 3.5）。</summary>
        public const int RainbowValue = 100;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("見た目の見本（CoinLook の順: 1 枚、3 枚、5 枚、100 枚）。シーンでは隠しておく")]
        [SerializeField] private GameObject[] _templates = new GameObject[0];
        [SerializeField] private float _spinDegreesPerSecond = 160f;
        [SerializeField] private float _bobHeight = 0.12f;
        [SerializeField] private float _popSeconds = 0.25f;

        private static readonly int LookCount = System.Enum.GetValues(typeof(CoinLook)).Length;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private Stack<Shown>[] _pools;
        private ISimulationFeed _feed;

        /// <summary>表示しているコインの数（テスト用）。</summary>
        public int ShownCount => _shown.Count;

        private sealed class Shown
        {
            public GameObject Visual;
            public CoinLook Look;
            public Transform[] Faces;
            public float BornAt;
        }

        /// <summary>枚数に合う見た目（100 枚以上は虹色、5 枚以上は赤、3 枚以上はピラミッド、それより少なければ金色）。</summary>
        public static CoinLook LookOf(int value)
        {
            if (value >= RainbowValue)
            {
                return CoinLook.Rainbow;
            }
            if (value >= 5)
            {
                return CoinLook.Red;
            }
            return value >= 3 ? CoinLook.Triple : CoinLook.Single;
        }

        /// <summary>表示しているコインの見た目（テスト用）。表示していなければ false。</summary>
        public bool TryGetLook(int coinId, out CoinLook look)
        {
            bool found = _shown.TryGetValue(coinId, out Shown shown);
            look = found ? shown.Look : default;
            return found;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _templates.Length != LookCount || System.Array.IndexOf(_templates, null) >= 0)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、{LookCount} 種類の見た目の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _pools = new Stack<Shown>[LookCount];
            for (int i = 0; i < LookCount; i++)
            {
                _pools[i] = new Stack<Shown>();
                _templates[i].SetActive(false);
            }
        }

        private void Update()
        {
            CoinField field = _feed.Coins;
            _gone.Clear();
            foreach (KeyValuePair<int, Shown> pair in _shown)
            {
                if (field == null || field.IndexOf(pair.Key) < 0)
                {
                    _gone.Add(pair.Key);
                }
            }
            foreach (int id in _gone)
            {
                Shown shown = _shown[id];
                shown.Visual.SetActive(false);
                _pools[(int)shown.Look].Push(shown);
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
                    shown = Take(LookOf(coin.Value));
                    shown.BornAt = time;
                    _shown.Add(coin.Id, shown);
                }
                float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - shown.BornAt) / _popSeconds));
                float bob = Mathf.Sin(time * 2.5f + coin.Id) * _bobHeight;
                Transform t = shown.Visual.transform;
                t.position = new Vector3(coin.Position.X, coin.Position.Y + bob, 0f);
                t.localScale = Vector3.one * pop;
                // 面をカメラに向けたまま、縦の軸で回す（ピラミッドの 3 枚はそろえて回す）。
                Quaternion spin = Quaternion.Euler(0f, time * _spinDegreesPerSecond + coin.Id * 37f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                foreach (Transform face in shown.Faces)
                {
                    face.localRotation = spin;
                }
            }
        }

        private Shown Take(CoinLook look)
        {
            Stack<Shown> pool = _pools[(int)look];
            Shown shown;
            if (pool.Count > 0)
            {
                shown = pool.Pop();
            }
            else
            {
                GameObject visual = Instantiate(_templates[(int)look], transform);
                shown = new Shown { Visual = visual, Look = look, Faces = FacesOf(visual.transform) };
            }
            shown.Visual.SetActive(true);
            return shown;
        }

        // 見本の子のうち、メッシュを持つもの（コインの面）。
        private static Transform[] FacesOf(Transform root)
        {
            var faces = new List<Transform>();
            foreach (Transform child in root)
            {
                if (child.GetComponent<MeshFilter>() != null)
                {
                    faces.Add(child);
                }
            }
            return faces.ToArray();
        }
    }
}
