using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワールドに置かれた物と投射物の表示（docs/13 T5-6、docs/14）。種類ごとの見本を複製して、物の番号ごとに 1 つずつ出す。
    /// - ミラーボール: 回る球と、回りながら色が変わる光の筋。消える前の 1 秒は点滅する（2026-10-16 の要望で大きくした）。
    /// - セーフティボックス: 金庫の箱。上に「X/300」（今の枚数/上限）と、耐久の HP バーを出す。150 枚で金色、300 枚で虹色に光る
    ///   （ユーザーの決定、2026-10-16）。攻撃を受けると白く光って揺れる。周りの円形のゲージで次のチャージまでを示し、チャージした
    ///   瞬間に光って音が鳴る。150 枚に着いたら少し派手な音で強く光り、300 枚に着いたらとても派手な音で光り続ける（チャージは止まり、
    ///   ゲージは消える。ユーザーの要望、2026-10-16）。
    /// - 投射物（コイントス）: 回る小さなコイン。
    /// - 突風（ウインドクロークを解いたとき）: 進む向きに並んだ、弧の形の風の線。
    /// - 風の帯（スカイロケット）: 長方形の枠と、向きへ流れる風の線。消える前の 1 秒は点滅する。
    /// - ワープポータル: 持ち主の色を混ぜた紫の輪と回るらせん。つながっている 2 つの間に薄い線。消える前の 3 秒は点滅する。
    /// - ミサイル: 飛ぶ向きを向いた弾と軌跡。消えたら爆発の光と音。
    /// - コズミックレイ: ポータルの間の太い紫の光と白い芯（当たる瞬間に明るくなる）。撃ち終わって手持ちが確定されたら、
    ///   いちばん古いポータルから拠点へ金色のコインが飛ぶ。
    /// - わたがしの足場（PASTRY CHEF の Q）: 表の向きを向いた、横長のピンクの雲。消える前の 1 秒は点滅する。
    /// - クリーム（E）: 飛んでいる間は小さな玉、張り付いたら面に沿った板（オレンジ・抹茶の色）。消える前の 1 秒は点滅する。
    /// - マカロン（R）: 色の殻とクリームの間の円盤（虹は色が回る）。投げる前は隠し、投げた瞬間に音を鳴らす。消える前に点滅する。
    /// - デジタルスケール（PASTRY CHEF の空中下）: 灰色の量りとボウル。上にグラム数（FATAL の重さを超えたら赤く光る）。
    /// - 雷の波動（SOLDERER のインバータ）: 地形の箱の縁を両回りに進むジグザグの光。
    /// - ボールライトニング（SOLDERER の R）: 脈打つ光の球と、周りのジグザグ。放つ前は隠し、止まった後は消える前に点滅する。
    /// 物が出たとき・セーフティボックスが消えたとき（ばらまき）に音を鳴らす（_audio があれば）。状態を読むだけで、書き換えない。
    /// THIEF の物（煙球・煙・タレントスナッチの弾・置いたギミックと仕掛け。LastKind より後の種類）は ThiefEffectsView が出す。
    /// </summary>
    public sealed class WorldObjectsView : MonoBehaviour
    {
        /// <summary>ここで出す最後の種類（見本はミラーボールからここまで。これより後は ThiefEffectsView）。</summary>
        public const WorldObjectKind LastKind = WorldObjectKind.BallLightning;

        private const float BlinkSeconds = 1f;
        private const float FlashSeconds = 0.2f;
        private const int RaySegments = 16;
        private const int RingPoints = 48;
        private const float RingRadius = 1.25f;

        // ミラーボールの光の筋の長さ（m。2026-10-16 の要望で、球と一緒に 1.6 倍に大きくした）。
        private const float RayOuterRadius = 2.6f;
        private const float RayInnerRadius = 1.05f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color Gold = new Color(1f, 0.8f, 0.2f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("見た目の見本（ミラーボール、セーフティボックス、投射物、突風、風の帯、ワープポータル、ミサイル、ビーム、わたがし、飛ぶクリーム、張り付いたクリーム、マカロン、デジタルスケール、雷の波動、ボールライトニングの順）。シーンでは隠しておく")]
        [SerializeField] private GameObject[] _templates = new GameObject[0];
        [Tooltip("セーフティボックスの枚数を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("物が出た・壊れたときの音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("ミサイルの爆発の光の見本（なくてもよい。隠しておく）")]
        [SerializeField] private BurstEffectView _explosionTemplate;

        private const int TrailLength = 14;
        private const float PortalBlinkSeconds = 3f;
        private const float CoinFlightSeconds = 0.8f;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<CoinFlight> _flights = new List<CoinFlight>();
        private readonly List<BurstEffectView> _explosions = new List<BurstEffectView>();
        private int _nextExplosion;

        private sealed class CoinFlight
        {
            public LineRenderer Line;
            public Vector3 From;
            public Vector3 To;
            public float At;
        }

        /// <summary>今飛んでいる、拠点へのコインの数（テスト用）。</summary>
        public int CoinFlightCount => _flights.Count;

        /// <summary>これまでに出したミサイルの爆発の数（テスト用）。</summary>
        public int ExplosionCount { get; private set; }
        private readonly List<int> _gone = new List<int>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private ISimulationFeed _feed;
        private MaterialPropertyBlock _block;
        private GUIStyle _label;
        private bool _primed;

        private sealed class Shown
        {
            public GameObject Visual;
            public WorldObjectKind Kind;
            public Renderer[] Renderers;
            public LineRenderer Rays;
            public Color BaseColor;
            public float BornAt;
            public int Damage;
            public float FlashAt = -10f;
            public string Label = string.Empty;
            public SafeGlow Glow;
            public Vector3 Anchor;
            public LineRenderer Ring;
            public LineRenderer RingBack;
            public int Coins = -1;
            public float ChargeAt = -10f;
            public float ChargeSeconds = 0.3f;
            public float ChargeStrength;
            public SafeEvent LastEvent;
            public float Health = 1f;
            public float RingProgress = -1f;
            public LineRenderer Outline;
            public LineRenderer[] Streaks = new LineRenderer[0];
            public readonly List<WindShadow> Winds = new List<WindShadow>();
            public LineRenderer RingBorder;
            public LineRenderer RingRim;
            public int Owner;
            public LineRenderer PortalRing;
            public LineRenderer Swirl;
            public LineRenderer Link;
            public LineRenderer Trail;
            public Vector3[] TrailPoints;
            public int TrailCount;
            public LineRenderer BeamCore;
            public LineRenderer BeamGlow;
            public Vector3 BeamStart;
            public int BankedAtSpawn;
            public bool Launched;
            public string Grams = string.Empty;
            public bool Heavy;
            public LineRenderer[] WaveLines;
            public LineRenderer Spark;
        }

        // 雷のジグザグの色。
        private static readonly Color Electric = new Color(1f, 0.95f, 0.4f);
        private static readonly Color ElectricBlue = new Color(0.45f, 0.85f, 1f);

        // 雷の波動の色（青に黄色の縁）と、跳ね方（1 回の跳ねの長さと高さ）、後ろの軌跡の長さ（m）と点の数。
        private static readonly Color WaveBlue = new Color(0.2f, 0.5f, 1f);
        private static readonly Color WaveYellow = new Color(1f, 0.86f, 0.15f);
        private const float WaveHopLength = 2.4f;
        private const float WaveHopHeight = 0.9f;
        private const float WaveTrailLength = 3.4f;
        private const int WavePoints = 20;

        /// <summary>クリームの色（オレンジ・抹茶）。</summary>
        public static Color CreamColor(byte flavor)
        {
            return flavor == (byte)CreamFlavor.Matcha ? new Color(0.5f, 0.75f, 0.32f) : new Color(1f, 0.62f, 0.15f);
        }

        /// <summary>マカロンの色（緑・黄・虹。虹は時間で色が回る）。</summary>
        public static Color MacaronColorOf(byte color, float time)
        {
            switch ((MacaronColor)color)
            {
                case MacaronColor.Yellow: return new Color(1f, 0.88f, 0.3f);
                case MacaronColor.Rainbow: return Color.HSVToRGB(Mathf.Repeat(time * 1.2f, 1f), 0.65f, 1f);
                default: return new Color(0.55f, 0.9f, 0.5f);
            }
        }

        /// <summary>セーフティボックスのチャージの演出の種類（表示用）。</summary>
        public enum SafeEvent
        {
            None,

            /// <summary>ふつうのチャージ（光と小さな音）。</summary>
            Charge,

            /// <summary>150 枚に着いた（強い光と少し派手な音）。</summary>
            Gold,

            /// <summary>上限の 300 枚に着いた（とても派手な音。光り続ける）。</summary>
            Max,
        }

        /// <summary>セーフティボックスの光り方（表示用）。</summary>
        public enum SafeGlow
        {
            None,
            Gold,
            Rainbow,
        }

        /// <summary>表示している物の数（テスト用）。</summary>
        public int ShownCount => _shown.Count;

        /// <summary>番号 id のセーフティボックスの枚数の文字と光り方（テスト用。表示していなければ false）。</summary>
        public bool TryGetSafe(int id, out string label, out SafeGlow glow)
        {
            bool found = _shown.TryGetValue(id, out Shown shown) && shown.Kind == WorldObjectKind.Safe;
            label = found ? shown.Label : string.Empty;
            glow = found ? shown.Glow : SafeGlow.None;
            return found;
        }

        /// <summary>
        /// 番号 id のセーフティボックスの、最後のチャージの演出・耐久の残り（0〜1）・次のチャージまでのゲージ（0〜1。上限なら -1）
        /// （テスト用。表示していなければ false）。
        /// </summary>
        public bool TryGetSafeState(int id, out SafeEvent lastEvent, out float health, out float ringProgress)
        {
            bool found = _shown.TryGetValue(id, out Shown shown) && shown.Kind == WorldObjectKind.Safe;
            lastEvent = found ? shown.LastEvent : SafeEvent.None;
            health = found ? shown.Health : 0f;
            ringProgress = found ? shown.RingProgress : -1f;
            return found;
        }

        /// <summary>チャージで枚数が before から after になったときの演出（増えていなければ None）。</summary>
        public static SafeEvent EventOf(int before, int after, SafeTuning tuning)
        {
            if (after <= before)
            {
                return SafeEvent.None;
            }
            if (before < tuning.MaxCoins && after >= tuning.MaxCoins)
            {
                return SafeEvent.Max;
            }
            return before < tuning.GoldCoins && after >= tuning.GoldCoins ? SafeEvent.Gold : SafeEvent.Charge;
        }

        /// <summary>セーフティボックスの枚数の文字（「X/300」）。</summary>
        public static string SafeLabel(int coins, int max)
        {
            return $"{coins}/{max}";
        }

        /// <summary>枚数に合う光り方（金色の枚数以上で金色、上限で虹色）。</summary>
        public static SafeGlow GlowOf(int coins, SafeTuning tuning)
        {
            return coins >= tuning.MaxCoins ? SafeGlow.Rainbow : coins >= tuning.GoldCoins ? SafeGlow.Gold : SafeGlow.None;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            int kinds = (int)LastKind;
            if (_feed == null || _templates.Length != kinds || System.Array.IndexOf(_templates, null) >= 0)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、{kinds} 種類の見た目の見本が設定されていません", this);
                enabled = false;
                return;
            }
            foreach (GameObject template in _templates)
            {
                template.SetActive(false);
            }
            if (_explosionTemplate != null)
            {
                _explosionTemplate.gameObject.SetActive(false);
            }
            _block = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            WorldObjects objects = _feed.Objects;
            _present.Clear();
            if (objects != null)
            {
                foreach (WorldObject item in objects.Items)
                {
                    if (item.Kind > LastKind)
                    {
                        continue;
                    }
                    _present.Add(item.Id);
                    if (_shown.TryGetValue(item.Id, out Shown changed) && changed.Kind != item.Kind)
                    {
                        // 種類が変わった（飛んでいたクリームが張り付いた）: 見た目を作り直す。
                        Destroy(changed.Visual);
                        _shown.Remove(item.Id);
                    }
                    if (!_shown.TryGetValue(item.Id, out Shown shown))
                    {
                        shown = Create(item);
                        _shown.Add(item.Id, shown);
                        PlaySpawn(item.Kind);
                    }
                    UpdateShown(shown, item);
                }
            }
            _gone.Clear();
            foreach (KeyValuePair<int, Shown> pair in _shown)
            {
                if (!_present.Contains(pair.Key))
                {
                    _gone.Add(pair.Key);
                }
            }
            foreach (int id in _gone)
            {
                Shown shown = _shown[id];
                if (shown.Kind == WorldObjectKind.Safe && _primed && _audio != null)
                {
                    _audio.Play(SoundId.SafeBreak);
                }
                if (shown.Kind == WorldObjectKind.Missile && _primed)
                {
                    Explode(shown.Anchor);
                }
                if (shown.Kind == WorldObjectKind.Beam && _primed && shown.Owner < _feed.CharacterCount
                    && _feed.GetState(shown.Owner).Coins.Banked > shown.BankedAtSpawn)
                {
                    StartCoinFlight(shown.BeamStart);
                }
                Destroy(shown.Visual);
                _shown.Remove(id);
            }
            UpdateCoinFlights();
            // 最初のフレーム（シーンを開いたとき、巻き戻しで物が戻ったとき）には、出た音を鳴らさない。
            _primed = true;
        }

        private Shown Create(in WorldObject item)
        {
            GameObject template = _templates[(int)item.Kind - 1];
            GameObject visual = Instantiate(template, transform);
            visual.name = $"{item.Kind}#{item.Id}";
            visual.SetActive(true);
            var shown = new Shown
            {
                Visual = visual,
                Kind = item.Kind,
                Renderers = visual.GetComponentsInChildren<MeshRenderer>(),
                BornAt = Time.time,
                Damage = item.Damage,
            };
            Transform rays = visual.transform.Find("Rays");
            shown.Rays = rays != null ? rays.GetComponent<LineRenderer>() : null;
            Transform ring = visual.transform.Find("ChargeRing");
            shown.Ring = ring != null ? ring.GetComponent<LineRenderer>() : null;
            Transform ringBack = visual.transform.Find("ChargeRingBack");
            shown.RingBack = ringBack != null ? ringBack.GetComponent<LineRenderer>() : null;
            shown.Owner = item.Owner;
            shown.Swirl = FindLine(visual, "Swirl");
            shown.Link = FindLine(visual, "Link");
            shown.Trail = FindLine(visual, "Trail");
            shown.Spark = FindLine(visual, "Spark");
            shown.BeamCore = FindLine(visual, "Core");
            shown.BeamGlow = FindLine(visual, "Glow");
            shown.PortalRing = item.Kind == WorldObjectKind.Portal ? FindLine(visual, "Ring") : null;
            if (item.Kind == WorldObjectKind.Missile)
            {
                shown.TrailPoints = new Vector3[TrailLength];
            }
            if (item.Kind == WorldObjectKind.Beam)
            {
                shown.BeamStart = new Vector3(item.Position.X, item.Position.Y, 0f);
                shown.BankedAtSpawn = item.Owner < _feed.CharacterCount ? _feed.GetState(item.Owner).Coins.Banked : 0;
            }
            Transform outline = visual.transform.Find("Outline");
            shown.Outline = outline != null ? outline.GetComponent<LineRenderer>() : null;
            var streaks = new List<LineRenderer>();
            for (int i = 0; visual.transform.Find("Streak" + i) is Transform streak; i++)
            {
                streaks.Add(streak.GetComponent<LineRenderer>());
            }
            shown.Streaks = streaks.ToArray();
            if (item.Kind == WorldObjectKind.Gust || item.Kind == WorldObjectKind.WindZone)
            {
                // 風の線には濃い緑の影を付ける（背景と見分けられるように。ユーザーの要望、2026-10-16）。
                if (shown.Outline != null)
                {
                    shown.Winds.Add(new WindShadow(shown.Outline));
                }
                foreach (LineRenderer streak in shown.Streaks)
                {
                    shown.Winds.Add(new WindShadow(streak));
                }
            }
            if (item.Kind == WorldObjectKind.Safe)
            {
                // 置いたときに預けた枚数は、チャージとして数えない。
                shown.Coins = SkillRules.SafeCoins(item);
                if (shown.Ring != null)
                {
                    // 円形のゲージの縁（ほかのゲージと同じ。ユーザーの要望、2026-10-16）。
                    RingGaugeView.CreateFrame(shown.Ring, out shown.RingBorder, out shown.RingRim);
                }
            }
            shown.BaseColor = shown.Renderers.Length > 0 ? shown.Renderers[0].sharedMaterial.GetColor(BaseColorId) : Color.white;
            return shown;
        }

        private void PlaySpawn(WorldObjectKind kind)
        {
            if (!_primed || _audio == null)
            {
                return;
            }
            switch (kind)
            {
                case WorldObjectKind.MirrorBall:
                    _audio.Play(SoundId.MirrorBall, 0.9f);
                    break;
                case WorldObjectKind.Safe:
                    _audio.Play(SoundId.SafePlace);
                    break;
                case WorldObjectKind.Projectile:
                    _audio.Play(SoundId.CoinToss, 0.8f, 0.05f);
                    break;
                case WorldObjectKind.Gust:
                    _audio.Play(SoundId.Gust);
                    break;
                case WorldObjectKind.WindZone:
                    _audio.Play(SoundId.SkyRocket);
                    break;
                case WorldObjectKind.Portal:
                    _audio.Play(SoundId.PortalPlace);
                    break;
                case WorldObjectKind.Missile:
                    _audio.Play(SoundId.MissileFire);
                    break;
                case WorldObjectKind.Beam:
                    _audio.Play(SoundId.CosmicRay);
                    break;
                case WorldObjectKind.CottonCandy:
                    _audio.Play(SoundId.JumpPad, 0.8f, 0.1f);
                    break;
                case WorldObjectKind.CreamShot:
                    _audio.Play(SoundId.Swing, 0.35f, 0.15f);
                    break;
                case WorldObjectKind.ShockWave:
                    // インバータで足場につないで、雷が地面を伝い始めた（SOLDERER）。
                    _audio.Play(SoundId.Thunder, 0.8f, 0.04f);
                    break;
            }
        }

        private static LineRenderer FindLine(GameObject visual, string child)
        {
            Transform found = visual.transform.Find(child);
            return found != null ? found.GetComponent<LineRenderer>() : null;
        }

        // ポータル: 持ち主の色を混ぜた紫。古い方のポータルが、つながっているもう 1 つへの線を描く。
        private void UpdatePortal(Shown shown, in WorldObject portal)
        {
            shown.Visual.transform.localScale = Vector3.one;
            float radius = _feed.GetConfig(portal.Owner).Skills.Portal.Radius;
            Color color = Color.Lerp(SpaceObjectLines.Purple, _feed.ColorOf(portal.Owner), 0.35f);
            float secondsLeft = (portal.ExpireTick - _feed.CurrentTick) / (float)SimTime.TicksPerSecond;
            bool visible = secondsLeft > PortalBlinkSeconds || Mathf.Repeat(Time.time * 6f, 1f) < 0.65f;
            float pop = Mathf.Clamp01((Time.time - shown.BornAt) / 0.25f);
            SpaceObjectLines.DrawPortal(shown.PortalRing, shown.Swirl, shown.Anchor, radius * pop, color, Time.time, visible);
            bool linked = false;
            Vector3 partner = shown.Anchor;
            foreach (WorldObject other in _feed.Objects.Items)
            {
                if (other.Kind == WorldObjectKind.Portal && other.Owner == portal.Owner && other.Id > portal.Id && !other.Ended)
                {
                    linked = true;
                    partner = new Vector3(other.Position.X, other.Position.Y, 0f);
                    break;
                }
            }
            SpaceObjectLines.DrawLink(shown.Link, shown.Anchor, partner, color, Time.time, linked && visible);
        }

        // ミサイル: 飛ぶ向きを向き、通った点を軌跡に残す（ポータルで移ったら軌跡を始め直す）。
        private static void UpdateMissile(Shown shown, in WorldObject missile)
        {
            Transform t = shown.Visual.transform;
            t.localScale = Vector3.one;
            t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(missile.Velocity.Y, missile.Velocity.X) * Mathf.Rad2Deg);
            if (shown.TrailCount > 0 && Vector3.Distance(shown.TrailPoints[shown.TrailCount - 1], shown.Anchor) > 3f)
            {
                shown.TrailCount = 0;
            }
            if (shown.TrailCount == TrailLength)
            {
                System.Array.Copy(shown.TrailPoints, 1, shown.TrailPoints, 0, TrailLength - 1);
                shown.TrailCount--;
            }
            shown.TrailPoints[shown.TrailCount++] = shown.Anchor + new Vector3(0f, 0f, -0.5f);
            SpaceObjectLines.DrawTrail(shown.Trail, shown.TrailPoints, shown.TrailCount, SpaceObjectLines.Purple);
        }

        // ビーム: 当たる瞬間（IntervalTicks ごと）に明るくなる。
        private void UpdateBeam(Shown shown, in WorldObject beam)
        {
            shown.Visual.transform.localScale = Vector3.one;
            BeamTuning tuning = _feed.GetConfig(beam.Owner).Skills.Beam;
            int age = _feed.CurrentTick - beam.SpawnTick;
            float sinceHit = age % tuning.IntervalTicks;
            float flash = Mathf.Clamp01(1f - sinceHit / 4f);
            SpaceObjectLines.DrawBeam(shown.BeamGlow, shown.BeamCore, shown.BeamStart, new Vector3(beam.End.X, beam.End.Y, 0f), tuning.HalfWidth, flash,
                Time.time);
        }

        private void Explode(Vector3 at)
        {
            ExplosionCount++;
            if (_audio != null)
            {
                _audio.Play(SoundId.MissileExplode, 0.9f, 0.04f);
            }
            if (_explosionTemplate == null)
            {
                return;
            }
            // 重なっても途切れないように、いくつかを順に使う。
            if (_explosions.Count < 4)
            {
                BurstEffectView burst = Instantiate(_explosionTemplate, transform);
                burst.gameObject.SetActive(true);
                _explosions.Add(burst);
            }
            _explosions[_nextExplosion % _explosions.Count].Play(at);
            _nextExplosion++;
        }

        // いちばん古いポータル（ビームの始まり）から、今の拠点へコインが飛ぶ。
        private void StartCoinFlight(Vector3 from)
        {
            IReadOnlyList<Airerage.Core.Economy.BankSpot> spots = _feed.BankSpots;
            if (spots == null || spots.Count == 0 || _feed.Rules == null)
            {
                return;
            }
            Vector2 bank = ToUnity(spots[Airerage.Core.Economy.BankRules.SpotAt(_feed.Rules, spots.Count, _feed.CurrentTick)].Position);
            var line = new GameObject("CoinFlight").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.widthMultiplier = 0.3f;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            LineRenderer like = _templates[(int)WorldObjectKind.Beam - 1].GetComponentInChildren<LineRenderer>(true);
            if (like != null)
            {
                line.sharedMaterial = like.sharedMaterial;
            }
            _flights.Add(new CoinFlight { Line = line, From = from, To = new Vector3(bank.x, bank.y, 0f), At = Time.time });
        }

        private static Vector2 ToUnity(System.Numerics.Vector2 v)
        {
            return new Vector2(v.X, v.Y);
        }

        private void UpdateCoinFlights()
        {
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                CoinFlight flight = _flights[i];
                float progress = (Time.time - flight.At) / CoinFlightSeconds;
                if (progress >= 1f)
                {
                    Destroy(flight.Line.gameObject);
                    _flights.RemoveAt(i);
                    continue;
                }
                SpaceObjectLines.DrawCoinFlight(flight.Line, flight.From, flight.To, progress);
            }
        }

        private void UpdateShown(Shown shown, in WorldObject item)
        {
            float age = Time.time - shown.BornAt;
            float pop = Mathf.Clamp01(age / 0.2f);
            Transform t = shown.Visual.transform;
            shown.Anchor = new Vector3(item.Position.X, item.Position.Y, 0f);
            t.position = shown.Anchor;
            t.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, 1f - (1f - pop) * (1f - pop));
            switch (item.Kind)
            {
                case WorldObjectKind.MirrorBall:
                    UpdateMirrorBall(shown, item);
                    break;
                case WorldObjectKind.Safe:
                    UpdateSafe(shown, item);
                    break;
                case WorldObjectKind.Projectile:
                    t.rotation = Quaternion.Euler(0f, Time.time * 720f, 90f);
                    break;
                case WorldObjectKind.Gust:
                    UpdateGust(shown, item);
                    break;
                case WorldObjectKind.WindZone:
                    UpdateWindZone(shown, item);
                    break;
                case WorldObjectKind.Portal:
                    UpdatePortal(shown, item);
                    break;
                case WorldObjectKind.Missile:
                    UpdateMissile(shown, item);
                    break;
                case WorldObjectKind.Beam:
                    UpdateBeam(shown, item);
                    break;
                case WorldObjectKind.CottonCandy:
                {
                    var normal = new Vector3(item.Velocity.X, item.Velocity.Y, 0f);
                    t.rotation = Quaternion.FromToRotation(Vector3.up, normal);
                    // 見本は半分の長さ 1.8m、半分の厚さ 0.4m。
                    t.localScale = Vector3.Scale(t.localScale, new Vector3(item.End.X / 1.8f, item.End.Y / 0.4f, 1f))
                        * (1f + 0.04f * Mathf.Sin(Time.time * 5f));
                    SetVisible(shown, ExpireBlink(item));
                    break;
                }
                case WorldObjectKind.CreamShot:
                    Tint(shown, CreamColor(item.Variant), null);
                    break;
                case WorldObjectKind.CreamPatch:
                    t.localScale = new Vector3(item.End.X * 2f, item.End.Y * 2f, 0.9f) * Mathf.Lerp(0.6f, 1f, pop);
                    Tint(shown, CreamColor(item.Variant), null);
                    SetVisible(shown, ExpireBlink(item));
                    break;
                case WorldObjectKind.Macaron:
                    UpdateMacaron(shown, item);
                    break;
                case WorldObjectKind.Scale:
                {
                    ScaleTuning scale = _feed.GetConfig(item.Owner).Skills.Scale;
                    int grams = PastryRules.ScaleGrams(item, scale, _feed.CurrentTick);
                    shown.Grams = $"{grams}g";
                    shown.Heavy = grams >= scale.FatalGrams;
                    // 見本は当たりの半分の幅 0.6m で作ってある。当たりの大きさに合わせる。
                    t.localScale = Vector3.one * (scale.HalfWidth / 0.6f);
                    break;
                }
                case WorldObjectKind.ShockWave:
                    UpdateShockWave(shown, item);
                    break;
                case WorldObjectKind.BallLightning:
                    UpdateBall(shown, item);
                    break;
            }
            foreach (WindShadow wind in shown.Winds)
            {
                wind.Sync();
            }
        }

        /// <summary>番号 id の物の風の線が、すべて影を持っているか（表示していなければ false。テスト用）。</summary>
        public bool WindsHaveShadows(int id)
        {
            if (!_shown.TryGetValue(id, out Shown shown) || shown.Winds.Count == 0)
            {
                return false;
            }
            foreach (WindShadow wind in shown.Winds)
            {
                if (wind.Main.enabled && !wind.Shadow.enabled)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>番号 id の金庫のチャージのゲージに縁を描いているか（テスト用）。</summary>
        public bool SafeRingHasFrame(int id)
        {
            return _shown.TryGetValue(id, out Shown shown) && shown.RingBorder != null && shown.RingBorder.enabled && shown.RingRim.enabled;
        }

        // 突風: 進む向きに並んだ弧（「)」の形）。生まれてからの時間で薄くなる。
        private static void UpdateGust(Shown shown, in WorldObject gust)
        {
            var velocity = new Vector2(gust.Velocity.X, gust.Velocity.Y);
            Vector2 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector2.right;
            var side = new Vector2(-dir.y, dir.x);
            float fade = Mathf.Clamp01(1f - (Time.time - shown.BornAt) / 0.6f);
            for (int s = 0; s < shown.Streaks.Length; s++)
            {
                LineRenderer line = shown.Streaks[s];
                const int points = 9;
                line.positionCount = points;
                float back = s * 0.7f;
                float bend = 0.5f + 0.15f * s;
                for (int k = 0; k < points; k++)
                {
                    float u = k / (float)(points - 1) * 2f - 1f;
                    Vector2 p = -dir * (back + bend * (1f - u * u)) + side * (u * (1.1f - 0.15f * s));
                    line.SetPosition(k, shown.Anchor + new Vector3(p.x, p.y, -0.6f));
                }
                var color = new Color(0.75f, 1f, 0.55f, 0.8f * fade * (1f - 0.2f * s));
                line.startColor = color;
                line.endColor = color;
            }
        }

        // 風の帯: 置いた点から向きへ伸びる長方形の枠と、向きへ流れる線。
        private void UpdateWindZone(Shown shown, in WorldObject zone)
        {
            Airerage.Core.Skills.WindZoneTuning tuning = _feed.GetConfig(zone.Owner).Skills.WindZone;
            shown.Visual.transform.localScale = Vector3.one;
            var dir = new Vector2(zone.Velocity.X, zone.Velocity.Y);
            var side = new Vector2(-dir.y, dir.x);
            Vector3 origin = shown.Anchor;
            float secondsLeft = (zone.ExpireTick - _feed.CurrentTick) / (float)SimTime.TicksPerSecond;
            bool visible = secondsLeft > BlinkSeconds || Mathf.Repeat(Time.time * 8f, 1f) < 0.6f;
            if (shown.Outline != null)
            {
                shown.Outline.enabled = visible;
                shown.Outline.loop = true;
                shown.Outline.positionCount = 4;
                Vector2 w = side * tuning.HalfWidth;
                Vector2 l = dir * tuning.Length;
                shown.Outline.SetPosition(0, origin + ToWorld(-w));
                shown.Outline.SetPosition(1, origin + ToWorld(-w + l));
                shown.Outline.SetPosition(2, origin + ToWorld(w + l));
                shown.Outline.SetPosition(3, origin + ToWorld(w));
            }
            for (int s = 0; s < shown.Streaks.Length; s++)
            {
                LineRenderer line = shown.Streaks[s];
                line.enabled = visible;
                float lane = (s + 0.5f) / shown.Streaks.Length * 2f - 1f;
                float head = Mathf.Repeat(Time.time * 12f + s * 3.7f, tuning.Length + 2f) - 1f;
                float tail = Mathf.Max(0f, head - 2.2f);
                head = Mathf.Clamp(head, 0f, tuning.Length);
                line.positionCount = 2;
                Vector2 offset = side * (lane * tuning.HalfWidth * 0.85f);
                line.SetPosition(0, origin + ToWorld(offset + dir * tail));
                line.SetPosition(1, origin + ToWorld(offset + dir * head));
            }
        }

        private static Vector3 ToWorld(Vector2 v)
        {
            return new Vector3(v.x, v.y, -0.5f);
        }

        // 消える前の 1 秒は点滅する（見えるなら true）。
        private bool ExpireBlink(in WorldObject item)
        {
            float secondsLeft = (item.ExpireTick - _feed.CurrentTick) / (float)SimTime.TicksPerSecond;
            return secondsLeft > BlinkSeconds || Mathf.Repeat(Time.time * 8f, 1f) < 0.6f;
        }

        private static void SetVisible(Shown shown, bool visible)
        {
            foreach (Renderer renderer in shown.Renderers)
            {
                renderer.enabled = visible;
            }
        }

        // 色を変える（only の名前で始まる部品だけ。null ならすべて）。
        private void Tint(Shown shown, Color color, string only)
        {
            _block.SetColor(BaseColorId, color);
            foreach (Renderer renderer in shown.Renderers)
            {
                if (only == null || renderer.name.StartsWith(only, System.StringComparison.Ordinal))
                {
                    renderer.SetPropertyBlock(_block);
                }
            }
        }

        // 雷の波動: 始まりの点から両回りに、進んだ所までジグザグの光（先ほど明るい）。
        // 雷の波動（ユーザーの要望、2026-10-22: 目立つように、青に黄色の縁で、地面を跳ねるように進む）: 始まりの点から両回りに進む 2 つの
        // 波の先と、その後ろの跳ねた軌跡（縁から外向きに、WaveHopLength ごとに跳ねる弧）。黄色の太い線の上に、青い細い線を重ねる。
        private void UpdateShockWave(Shown shown, in WorldObject wave)
        {
            shown.Visual.transform.position = Vector3.zero;
            shown.Visual.transform.localScale = Vector3.one;
            if (shown.Spark == null)
            {
                return;
            }
            if (shown.WaveLines == null)
            {
                // 線は 4 本（前の縁・前の芯・後ろの縁・後ろの芯）。見本の線（Spark）を複製して使う。
                shown.WaveLines = new LineRenderer[4];
                shown.WaveLines[0] = shown.Spark;
                for (int k = 1; k < shown.WaveLines.Length; k++)
                {
                    shown.WaveLines[k] = Instantiate(shown.Spark, shown.Spark.transform.parent);
                    shown.WaveLines[k].name = "Spark" + k;
                }
            }
            InverterTuning tuning = _feed.GetConfig(wave.Owner).Skills.Inverter;
            float travel = ElectricRules.WaveTravel(wave, tuning, _feed.CurrentTick);
            DrawWave(shown.WaveLines[0], shown.WaveLines[1], wave, travel, 1f);
            DrawWave(shown.WaveLines[2], shown.WaveLines[3], wave, travel, -1f);
        }

        // 波 1 つ（direction の回り）。縁（edge）は黄色で太く、芯（core）は青く細い。先ほど明るく、後ろへ薄くなる。
        private static void DrawWave(LineRenderer edge, LineRenderer core, in WorldObject wave, float travel, float direction)
        {
            var center = new Vector2((wave.Velocity.X + wave.End.X) * 0.5f, (wave.Velocity.Y + wave.End.Y) * 0.5f);
            float length = Mathf.Min(WaveTrailLength, travel);
            edge.positionCount = WavePoints;
            core.positionCount = WavePoints;
            for (int i = 0; i < WavePoints; i++)
            {
                float d = travel - length * i / (WavePoints - 1);
                System.Numerics.Vector2 p = ElectricRules.PointAlong(wave, direction * d);
                System.Numerics.Vector2 ahead = ElectricRules.PointAlong(wave, direction * (d + 0.05f));
                var tangent = new Vector2(ahead.X - p.X, ahead.Y - p.Y);
                var normal = new Vector2(-tangent.y, tangent.x).normalized;
                if (Vector2.Dot(normal, new Vector2(p.X, p.Y) - center) < 0f)
                {
                    normal = -normal;
                }
                float hop = Mathf.Abs(Mathf.Sin(Mathf.PI * d / WaveHopLength)) * WaveHopHeight + 0.12f;
                var point = new Vector2(p.X, p.Y) + normal * hop;
                edge.SetPosition(i, new Vector3(point.x, point.y, -0.68f));
                core.SetPosition(i, new Vector3(point.x, point.y, -0.72f));
            }
            edge.widthMultiplier = 0.34f;
            core.widthMultiplier = 0.15f;
            edge.colorGradient = Fade(WaveYellow);
            core.colorGradient = Fade(WaveBlue);
        }

        private static Gradient Fade(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.3f), 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        // ボールライトニング: 放つまでは隠す。脈打つ光と、周りのジグザグ。止まった後は消える前の 1 秒に点滅する。
        private void UpdateBall(Shown shown, in WorldObject ball)
        {
            bool launched = ball.Stored != 0;
            bool visible = launched && (!ElectricRules.IsResting(ball) || ExpireBlink(ball));
            if (launched && !shown.Launched && _primed && _audio != null)
            {
                _audio.Play(SoundId.MissileFire, 0.6f, 0.1f);
            }
            shown.Launched = launched;
            SetVisible(shown, visible);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 18f + ball.Id);
            // 見本は直径 1m の球。当たりの半径に合わせる（ユーザーの要望で 2 倍にした、2026-10-22）。
            float radiusOfBall = _feed.GetConfig(ball.Owner).Skills.BallLightning.Radius;
            shown.Visual.transform.localScale = Vector3.one * (radiusOfBall * 2f * (0.9f + 0.2f * pulse));
            Tint(shown, Color.Lerp(Electric, Color.white, pulse * 0.6f), null);
            if (shown.Spark == null)
            {
                return;
            }
            shown.Spark.enabled = visible;
            const int points = 9;
            shown.Spark.loop = true;
            shown.Spark.positionCount = points;
            int frame = Mathf.FloorToInt(Time.time * 24f);
            for (int i = 0; i < points; i++)
            {
                float angle = Mathf.PI * 2f * i / points;
                float radius = radiusOfBall * (1.35f + (Mathf.PerlinNoise(i * 1.3f, frame * 0.5f + ball.Id) - 0.5f) * 1.1f);
                shown.Spark.SetPosition(i, shown.Anchor + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -0.7f));
            }
            shown.Spark.startColor = shown.Spark.endColor = new Color(ElectricBlue.r, ElectricBlue.g, ElectricBlue.b, 0.85f);
        }

        // マカロン: 投げるまでは隠す。投げたら転がるように回し、色を塗る。点滅の間は点滅する。
        private void UpdateMacaron(Shown shown, in WorldObject macaron)
        {
            int tick = _feed.CurrentTick;
            bool launched = PastryRules.IsLaunched(macaron, tick);
            if (launched && !shown.Launched && _primed && _audio != null)
            {
                _audio.Play(SoundId.CoinToss, macaron.Variant == (byte)MacaronColor.Rainbow ? 1f : 0.7f, 0.12f);
            }
            shown.Launched = launched;
            TeaPartyTuning tuning = _feed.GetConfig(macaron.Owner).Skills.TeaParty;
            bool blinking = PastryRules.IsBlinking(macaron, tuning, tick);
            SetVisible(shown, launched && (!blinking || Mathf.Repeat(Time.time * 10f, 1f) < 0.5f));
            shown.Visual.transform.rotation = Quaternion.Euler(0f, 0f, -macaron.Position.X / tuning.Radius * Mathf.Rad2Deg);
            // 見本は半径 0.9m（当たりの半径に合わせる）。
            shown.Visual.transform.localScale *= tuning.Radius / 0.9f;
            Tint(shown, MacaronColorOf(macaron.Variant, Time.time), "Shell");
        }

        private void UpdateMirrorBall(Shown shown, in WorldObject ball)
        {
            Transform t = shown.Visual.transform;
            t.rotation = Quaternion.Euler(0f, Time.time * 140f, 0f);
            float secondsLeft = (ball.ExpireTick - _feed.CurrentTick) / (float)SimTime.TicksPerSecond;
            bool visible = secondsLeft > BlinkSeconds || Mathf.Repeat(Time.time * 8f, 1f) < 0.6f;
            foreach (Renderer renderer in shown.Renderers)
            {
                renderer.enabled = visible;
            }
            if (shown.Rays == null)
            {
                return;
            }
            shown.Rays.enabled = visible;
            shown.Rays.positionCount = RaySegments * 2;
            shown.Rays.loop = true;
            float spin = Time.time * 1.6f;
            for (int i = 0; i < RaySegments * 2; i++)
            {
                float angle = spin + Mathf.PI * i / RaySegments;
                float radius = i % 2 == 0 ? RayOuterRadius + 0.4f * Mathf.Sin(Time.time * 6f + i) : RayInnerRadius;
                shown.Rays.SetPosition(i, shown.Anchor + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -0.6f));
            }
            Color color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.5f, 1f), 0.6f, 1f);
            color.a = 0.75f;
            shown.Rays.startColor = color;
            shown.Rays.endColor = color;
        }

        private void UpdateSafe(Shown shown, in WorldObject safe)
        {
            SafeTuning tuning = _feed.GetConfig(safe.Owner).Skills.Safe;
            int coins = SkillRules.SafeCoins(safe);
            shown.Label = SafeLabel(coins, tuning.MaxCoins);
            shown.Glow = GlowOf(coins, tuning);
            shown.Health = Mathf.Clamp01(1f - (float)safe.Damage / tuning.Durability);
            if (safe.Damage > shown.Damage)
            {
                shown.FlashAt = Time.time;
            }
            shown.Damage = safe.Damage;
            UpdateCharge(shown, coins, tuning);
            UpdateRing(shown, safe, tuning);

            Color color = shown.BaseColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            switch (shown.Glow)
            {
                case SafeGlow.Gold:
                    color = Color.Lerp(Gold, Color.white, 0.25f * pulse);
                    break;
                case SafeGlow.Rainbow:
                    color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.8f, 1f), 0.65f, 1f);
                    break;
            }
            // チャージの光（150 枚と 300 枚は強く、長く）。
            float charge = 1f - (Time.time - shown.ChargeAt) / shown.ChargeSeconds;
            if (charge > 0f)
            {
                color = Color.Lerp(color, new Color(1f, 0.95f, 0.6f), Mathf.Clamp01(charge * shown.ChargeStrength));
                shown.Visual.transform.localScale *= 1f + 0.12f * charge * shown.ChargeStrength;
            }
            float flash = 1f - (Time.time - shown.FlashAt) / FlashSeconds;
            if (flash > 0f)
            {
                color = Color.Lerp(color, Color.white, flash);
                shown.Visual.transform.position = shown.Anchor + new Vector3(Mathf.Sin(Time.time * 90f) * 0.08f * flash, 0f, 0f);
            }
            if (shown.Glow == SafeGlow.Rainbow)
            {
                // 上限: 光り続ける（大きく脈打つ）。
                shown.Visual.transform.localScale *= 1.06f + 0.06f * pulse;
            }
            else if (shown.Glow != SafeGlow.None)
            {
                shown.Visual.transform.localScale *= 1f + 0.05f * pulse;
            }
            _block.SetColor(BaseColorId, color);
            foreach (Renderer renderer in shown.Renderers)
            {
                renderer.SetPropertyBlock(_block);
            }
        }

        // チャージで枚数が増えたら、光らせて音を鳴らす（巻き戻しで減ったときは何もしない）。
        private void UpdateCharge(Shown shown, int coins, SafeTuning tuning)
        {
            SafeEvent happened = shown.Coins >= 0 ? EventOf(shown.Coins, coins, tuning) : SafeEvent.None;
            shown.Coins = coins;
            if (happened == SafeEvent.None)
            {
                return;
            }
            shown.LastEvent = happened;
            shown.ChargeAt = Time.time;
            switch (happened)
            {
                case SafeEvent.Max:
                    shown.ChargeSeconds = 0.9f;
                    shown.ChargeStrength = 2.5f;
                    PlaySafe(SoundId.SafeMax);
                    break;
                case SafeEvent.Gold:
                    shown.ChargeSeconds = 0.6f;
                    shown.ChargeStrength = 1.8f;
                    PlaySafe(SoundId.SafeGold);
                    break;
                default:
                    shown.ChargeSeconds = 0.3f;
                    shown.ChargeStrength = 1f;
                    PlaySafe(SoundId.SafeCharge);
                    break;
            }
        }

        private void PlaySafe(SoundId id)
        {
            if (_primed && _audio != null)
            {
                _audio.Play(id, id == SoundId.SafeCharge ? 0.7f : 1f);
            }
        }

        // 次のチャージまでの円形のゲージ（上から時計回りに伸びる）。上限に着いたら出さない。
        private void UpdateRing(Shown shown, in WorldObject safe, SafeTuning tuning)
        {
            bool full = SkillRules.IsSafeFull(safe, tuning);
            shown.RingProgress = full ? -1f : SkillRules.SafeChargeProgress(safe, tuning, _feed.CurrentTick);
            RingGaugeView.DrawFrame(shown.RingBorder, shown.RingRim, shown.Anchor, RingRadius, shown.Ring != null ? shown.Ring.widthMultiplier : 0.12f,
                -0.7f, !full);
            if (shown.RingBack != null)
            {
                shown.RingBack.enabled = !full;
                if (!full)
                {
                    SetArc(shown.RingBack, shown.Anchor, 1f);
                }
            }
            if (shown.Ring == null)
            {
                return;
            }
            shown.Ring.enabled = !full && shown.RingProgress > 0f;
            if (shown.Ring.enabled)
            {
                SetArc(shown.Ring, shown.Anchor, shown.RingProgress);
            }
        }

        private static void SetArc(LineRenderer line, Vector3 center, float fraction)
        {
            int points = Mathf.Max(2, Mathf.CeilToInt(RingPoints * fraction) + 1);
            line.positionCount = points;
            for (int k = 0; k < points; k++)
            {
                float angle = Mathf.PI * 2f * fraction * k / (points - 1);
                line.SetPosition(k, center + new Vector3(Mathf.Sin(angle) * RingRadius, Mathf.Cos(angle) * RingRadius, -0.7f));
            }
        }

        private void OnGUI()
        {
            if (_camera == null || _shown.Count == 0)
            {
                return;
            }
            if (_label == null)
            {
                _label = TextFlash.Style(16, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            foreach (Shown shown in _shown.Values)
            {
                if (shown.Kind == WorldObjectKind.Scale)
                {
                    // デジタルスケールのグラム数（FATAL の重さを超えたら赤く光る）。
                    Vector3 s = _camera.WorldToScreenPoint(shown.Anchor + new Vector3(0f, 1.1f, 0f));
                    if (s.z > 0f)
                    {
                        Color gramsColor = shown.Heavy ? Color.Lerp(new Color(1f, 0.25f, 0.2f), Color.white, 0.3f + 0.3f * Mathf.Sin(Time.time * 20f))
                            : new Color(0.55f, 1f, 0.6f);
                        GuidePlate.DrawText(new Rect(s.x - 60f, Screen.height - s.y - 14f, 120f, 28f), shown.Grams, _label, gramsColor);
                    }
                    continue;
                }
                if (shown.Kind != WorldObjectKind.Safe)
                {
                    continue;
                }
                Vector3 p = _camera.WorldToScreenPoint(shown.Anchor + new Vector3(0f, 1.3f, 0f));
                if (p.z <= 0f)
                {
                    continue;
                }
                var rect = new Rect(p.x - 60f, Screen.height - p.y - 14f, 120f, 28f);
                Color color = shown.Glow == SafeGlow.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 0.8f + 0.3f, 1f), 0.5f, 1f)
                    : shown.Glow == SafeGlow.Gold ? Gold : Color.white;
                GuidePlate.DrawText(rect, shown.Label, _label, color);
                DrawHealth(new Rect(p.x - 40f, rect.yMax + 1f, 80f, 8f), shown.Health);
            }
        }

        // 耐久の HP バー（残りが減ると緑 → 黄 → 赤）。
        private static void DrawHealth(Rect rect, float health)
        {
            Color saved = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), Texture2D.whiteTexture);
            GUI.color = health > 0.5f ? new Color(0.35f, 0.95f, 0.35f) : health > 0.25f ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.25f, 0.2f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * health, rect.height), Texture2D.whiteTexture);
            GUI.color = saved;
        }
    }
}
