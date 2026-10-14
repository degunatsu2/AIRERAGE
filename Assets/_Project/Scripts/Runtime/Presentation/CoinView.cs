using System.Collections.Generic;
using Degunatsu.Core.Economy;
using Degunatsu.Core.Match;
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
        /// <summary>20 枚: 赤いコイン（5 枚から 20 枚にした。ユーザーの要望、2026-10-14）。</summary>
        Red,
        /// <summary>100 枚: 虹色に光るコイン。</summary>
        Rainbow,
    }

    /// <summary>
    /// フィールドのコインの表示（docs/12_match_plan.md T4-4）。枚数ごとに見た目を変える（CoinLook）: 1 枚は金色、3 枚は金色が
    /// ピラミッド型に 3 枚、20 枚は赤、100 枚は虹色に光る。コインは回りながら、少し上下に揺れる。出たときは小さくから大きくなる。
    /// ドロップしたコイン（寿命がある。T4-6）は、消える前の DropBlinkTicks の間点滅する。
    /// 赤いコインの出現地点は、拾われてから次に出るまでを円形のゲージで示す（ユーザーの要望、2026-10-14）。
    /// 100 枚のコインの予告の間は、出る地点に小さな虹色のコインを脈打たせる。
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
        [Tooltip("赤いコインの出現地点の、次に出るまでのゲージの見本（シーンでは隠しておく。なくてもよい）")]
        [SerializeField] private RingGaugeView _respawnGauge;
        [Tooltip("ドロップしたコインが消える前に点滅する時間（ティック）")]
        [SerializeField] private int _dropBlinkTicks = 120;
        [Tooltip("被弾ドロップのコインが、落とした所から飛んでいく時間（秒。拾えない時間より短く）")]
        [SerializeField] private float _flySeconds = 0.35f;

        private static readonly int LookCount = System.Enum.GetValues(typeof(CoinLook)).Length;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private readonly Dictionary<int, RingGaugeView> _gauges = new Dictionary<int, RingGaugeView>();
        private readonly HashSet<int> _occupied = new HashSet<int>();
        private Stack<Shown>[] _pools;
        private ISimulationFeed _feed;
        private Shown _preview;

        /// <summary>表示しているコインの数（テスト用）。</summary>
        public int ShownCount => _shown.Count;

        private sealed class Shown
        {
            public GameObject Visual;
            public CoinLook Look;
            public Transform[] Faces;
            public Renderer[] Renderers;
            public float BornAt;
            public bool Flies;
            public Vector3 FlyFrom;
        }

        /// <summary>次に出るまでのゲージを出している出現地点の数（テスト用）。</summary>
        public int RespawnGaugeCount { get; private set; }

        /// <summary>100 枚のコインの予告の、小さな虹色のコインを出しているか（テスト用）。</summary>
        public bool PreviewShowing => _preview != null && _preview.Visual.activeSelf;

        /// <summary>点滅して隠れているコインの数（テスト用）。</summary>
        public int BlinkHiddenCount { get; private set; }

        /// <summary>赤いコインにする枚数。</summary>
        public const int RedValue = 20;

        /// <summary>枚数に合う見た目（100 枚以上は虹色、20 枚以上は赤、3 枚以上はピラミッド、それより少なければ金色）。</summary>
        public static CoinLook LookOf(int value)
        {
            if (value >= RainbowValue)
            {
                return CoinLook.Rainbow;
            }
            if (value >= RedValue)
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
            if (_respawnGauge != null)
            {
                _respawnGauge.gameObject.SetActive(false);
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
                HidePreview();
                HideGauges();
                return;
            }

            float time = Time.time;
            int tick = _feed.CurrentTick;
            int hidden = 0;
            _occupied.Clear();
            foreach (FieldCoin coin in field.Coins)
            {
                _occupied.Add(coin.Source);
                if (!_shown.TryGetValue(coin.Id, out Shown shown))
                {
                    shown = Take(LookOf(coin.Value));
                    shown.BornAt = time;
                    // 被弾ドロップは、落とした位置（当たった人の位置）から、それぞれの場所へ飛んでいくように見せる。
                    shown.Flies = coin.Origin == CoinEventKind.HitDrop;
                    shown.FlyFrom = shown.Flies ? BatchCenter(field, coin) : Vector3.zero;
                    _shown.Add(coin.Id, shown);
                }
                float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - shown.BornAt) / _popSeconds));
                float bob = Mathf.Sin(time * 2.5f + coin.Id) * _bobHeight;
                Transform t = shown.Visual.transform;
                var target = new Vector3(coin.Position.X, coin.Position.Y + bob, 0f);
                if (shown.Flies)
                {
                    float fly = Mathf.Clamp01((time - shown.BornAt) / Mathf.Max(0.01f, _flySeconds));
                    target = Vector3.Lerp(shown.FlyFrom, target, 1f - (1f - fly) * (1f - fly));
                }
                t.position = target;
                t.localScale = Vector3.one * pop;
                // 面をカメラに向けたまま、縦の軸で回す（ピラミッドの 3 枚はそろえて回す）。
                Quaternion spin = Quaternion.Euler(0f, time * _spinDegreesPerSecond + coin.Id * 37f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                foreach (Transform face in shown.Faces)
                {
                    face.localRotation = spin;
                }
                // 寿命のあるコイン（ドロップ）は、消える前に点滅する。
                bool visible = coin.ExpireTick - tick > _dropBlinkTicks || Mathf.Repeat(time * 8f, 1f) < 0.6f;
                hidden += visible ? 0 : 1;
                foreach (Renderer face in shown.Renderers)
                {
                    face.enabled = visible;
                }
            }
            BlinkHiddenCount = hidden;
            UpdatePreview(time);
            UpdateGauges(field, tick);
        }

        // 被弾ドロップを落とした位置: 同じときに落ちたコイン（同じ拾えるティック）の真ん中から、並べ方の平均のずれを引く。
        private static Vector3 BatchCenter(CoinField field, FieldCoin coin)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (FieldCoin other in field.Coins)
            {
                if (other.Origin == coin.Origin && other.PickableFromTick == coin.PickableFromTick)
                {
                    sum += new Vector2(other.Position.X, other.Position.Y);
                    count++;
                }
            }
            sum /= Mathf.Max(1, count);
            System.Numerics.Vector2 offset = CoinDropRules.MeanHitOffset(count);
            return new Vector3(sum.x - offset.X, sum.y - offset.Y, 0f);
        }

        // 100 枚のコインの予告の間、出る地点に小さな虹色のコインを脈打たせる。
        private void UpdatePreview(float time)
        {
            MatchRules rules = _feed.Rules;
            int tick = _feed.CurrentTick;
            int announce = rules != null ? FieldCoinRules.JackpotAnnounceTick(rules) : -1;
            int spot = rules != null ? FieldCoinRules.JackpotSpot(rules, _feed.CoinSpawns) : -1;
            if (announce < 0 || spot < 0 || tick < announce || tick > FieldCoinRules.JackpotSpawnTick(rules))
            {
                HidePreview();
                return;
            }
            if (_preview == null)
            {
                _preview = Take(CoinLook.Rainbow);
            }
            _preview.Visual.SetActive(true);
            System.Numerics.Vector2 position = _feed.CoinSpawns[spot].Position;
            Transform t = _preview.Visual.transform;
            t.position = new Vector3(position.X, position.Y, 0f);
            t.localScale = Vector3.one * (0.4f + 0.1f * Mathf.Sin(time * 6f));
            Quaternion spin = Quaternion.Euler(0f, time * _spinDegreesPerSecond * 2f, 0f) * Quaternion.Euler(90f, 0f, 0f);
            foreach (Transform face in _preview.Faces)
            {
                face.localRotation = spin;
            }
        }

        private void HidePreview()
        {
            if (_preview != null)
            {
                _preview.Visual.SetActive(false);
            }
        }

        // 赤いコインの出現地点: コインがなく、まだ次に出せないなら、次に出るまでの残りをゲージで示す。
        private void UpdateGauges(CoinField field, int tick)
        {
            IReadOnlyList<CoinSpawnPoint> spawns = _feed.CoinSpawns;
            MatchRules rules = _feed.Rules;
            int shown = 0;
            for (int i = 0; spawns != null && rules != null && _respawnGauge != null && i < spawns.Count; i++)
            {
                CoinSpawnPoint point = spawns[i];
                bool red = point.Kind == CoinSpawnKind.Regular && LookOf(point.Value) == CoinLook.Red;
                int ready = field.SpawnReadyTick(i);
                bool waiting = red && !_occupied.Contains(i) && tick < ready;
                if (!waiting)
                {
                    if (_gauges.TryGetValue(i, out RingGaugeView idle))
                    {
                        idle.Hide();
                    }
                    continue;
                }
                if (!_gauges.TryGetValue(i, out RingGaugeView gauge))
                {
                    gauge = Instantiate(_respawnGauge, transform);
                    gauge.name = $"RespawnGauge{i}";
                    gauge.gameObject.SetActive(true);
                    _gauges.Add(i, gauge);
                }
                float fraction = (ready - tick) / (float)Mathf.Max(1, rules.CoinSpawnIntervalTicks);
                gauge.Show(new Vector3(point.Position.X, point.Position.Y, 0f), fraction);
                shown++;
            }
            RespawnGaugeCount = shown;
        }

        private void HideGauges()
        {
            foreach (RingGaugeView gauge in _gauges.Values)
            {
                gauge.Hide();
            }
            RespawnGaugeCount = 0;
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
                Transform[] faces = FacesOf(visual.transform);
                var renderers = new Renderer[faces.Length];
                for (int i = 0; i < faces.Length; i++)
                {
                    renderers[i] = faces[i].GetComponent<Renderer>();
                }
                shown = new Shown { Visual = visual, Look = look, Faces = faces, Renderers = renderers };
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
