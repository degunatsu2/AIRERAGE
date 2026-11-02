using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ホットエリア（ユーザーの要望、2026-11-02。HotAreaRules）の表示。状態を読むだけで、書き換えない。場所と時刻はティック数とルールから求める。
    /// - 予告（出る 10 秒前から）: 出る地点に点滅する橙色の輪。予告が始まった瞬間に、拠点の呼び鈴とは別の高く弾む告知音（SoundId.HotAreaNotice）。
    ///   帯や警報はない（画面のお知らせの札「HOT AREA」は NoticeHud）。
    /// - 出ている間: 中心にフルーツ（ゆっくり回って上下に揺れる）。出た瞬間は、フルーツが小さくから弾んで大きくなり、輪が広がって消える。
    ///   周りのコインは CoinView が描く。
    /// - 誘導表示の札（左にフルーツの絵、右に「HOT AREA」。予告中は「HOT AREA n」）: 画面の外なら画面の端に、画面の中なら地点の上に出し、
    ///   1 秒ごとに光らせる（EdgeGuideLayer の pulse と onScreen。2026-11-02 の要望）。
    /// - だれかがフルーツを取ったら（MatchState.HotFruitTaken が変わった）、その地点で光って「+30%」を出し、音を鳴らす。
    /// 動きは Time.time で決める（巻き戻しとは関係ない）。
    /// </summary>
    public sealed class HotAreaView : MonoBehaviour
    {
        private const int RingSegments = 56;

        /// <summary>出たときの、フルーツが弾んで大きくなり輪が広がる秒数。</summary>
        public const float AppearSeconds = 0.6f;

        [Tooltip("状態を読む相手（SimulationRunner・LocalMatchRunner・OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("フルーツの見た目（シーンでは隠しておく。この表示が出し入れする）")]
        [SerializeField] private Transform _fruit;
        [Tooltip("予告の輪（出た瞬間の広がる輪にも使う）")]
        [SerializeField] private LineRenderer _ring;
        [Tooltip("フルーツを取ったときの光（なくてもよい）")]
        [SerializeField] private BurstEffectView _burst;
        [Tooltip("予告の告知音とフルーツを取った音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("誘導表示（矢印と札）を描く層")]
        [SerializeField] private EdgeGuideLayer _guides;
        [SerializeField] private Color _color = new Color(1f, 0.55f, 0.15f);
        [SerializeField] private float _spinDegreesPerSecond = 70f;
        [SerializeField] private float _bobHeight = 0.18f;
        [Tooltip("予告が始まってから、これだけ遅れて表示を始めたときは告知音を鳴らさない（ティック。途中から見始めたとき）")]
        [SerializeField] private int _noticeSoundWindowTicks = 30;

        private ISimulationFeed _feed;
        private Texture2D _icon;
        private int _shownTaken = -1;
        private MatchRules _rules;
        private int _shownFruitRound;
        private float _fruitAppearedAt = float.NegativeInfinity;
        private Vector3 _fruitCenter;
        private Vector3 _fruitScale = Vector3.one;

        /// <summary>予告している回（なければ 0）と、フルーツを出している回（なければ 0）。テスト用。</summary>
        public int AnnouncedRound { get; private set; }
        public int FruitRound { get; private set; }

        /// <summary>フルーツを取られた回数（表示が見つけた回数）と、予告の告知音を鳴らした回数（テスト用）。</summary>
        public int PickupCount { get; private set; }
        public int NoticeSoundCount { get; private set; }

        /// <summary>出たばかりで、弾んで大きくなっている途中か（テスト用）。</summary>
        public bool Appearing => Time.time - _fruitAppearedAt < AppearSeconds;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _camera == null || _fruit == null || _ring == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）、カメラ、フルーツ、輪のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            _ring.positionCount = RingSegments;
            _ring.loop = true;
            _fruitScale = _fruit.localScale;
            _icon = GuideIcons.Fruit(64);
            if (_guides == null)
            {
                Debug.LogError($"{name}: 誘導表示の層（EdgeGuideLayer）が設定されていません。ホットエリアの場所を示せません", this);
            }
            Hide();
        }

        private void OnDestroy()
        {
            if (_icon != null)
            {
                Destroy(_icon);
            }
        }

        private void Update()
        {
            MatchRules rules = _feed.Rules;
            if (!ReferenceEquals(rules, _rules))
            {
                // 試合が始まった・始め直した。前の試合の「取った」は出さない。
                _rules = rules;
                _shownTaken = _feed.Match.HotFruitTaken;
                _shownFruitRound = 0;
            }
            if (rules == null || _feed.CoinSpawns == null)
            {
                Hide();
                return;
            }
            int tick = _feed.CurrentTick;
            DetectPickup(rules);

            int previousAnnounced = AnnouncedRound;
            AnnouncedRound = HotAreaRules.AnnouncedRound(rules, tick, out int ticksUntil);
            int announcedSpot = AnnouncedRound > 0 ? HotAreaRules.Spot(rules, _feed.CoinSpawns, AnnouncedRound) : -1;
            if (announcedSpot < 0)
            {
                AnnouncedRound = 0;
            }
            if (AnnouncedRound > 0 && AnnouncedRound != previousAnnounced)
            {
                // 予告が始まった（途中から見始めたときは鳴らさない）。
                int since = tick - HotAreaRules.AnnounceTick(rules, AnnouncedRound);
                if (since <= _noticeSoundWindowTicks)
                {
                    NoticeSoundCount++;
                    if (_audio != null)
                    {
                        _audio.Play(SoundId.HotAreaNotice);
                    }
                }
            }
            FruitRound = HotAreaRules.FruitPresent(rules, _feed.Match, tick, out int round) ? round : 0;
            int fruitSpot = FruitRound > 0 ? HotAreaRules.Spot(rules, _feed.CoinSpawns, FruitRound) : -1;
            if (fruitSpot < 0)
            {
                FruitRound = 0;
            }

            float time = Time.time;
            bool ringUsed = false;
            if (FruitRound > 0)
            {
                _fruitCenter = ToWorld(_feed.CoinSpawns[fruitSpot].Position);
                if (FruitRound != _shownFruitRound)
                {
                    // 出た（巻き戻しで出直したときも、同じ回なら出直さない）。
                    _shownFruitRound = FruitRound;
                    _fruitAppearedAt = time;
                }
                float appear = Mathf.Clamp01((time - _fruitAppearedAt) / AppearSeconds);
                _fruit.gameObject.SetActive(true);
                _fruit.position = _fruitCenter + new Vector3(0f, Mathf.Sin(time * 2.2f) * _bobHeight, 0f);
                _fruit.rotation = Quaternion.Euler(0f, time * _spinDegreesPerSecond, Mathf.Sin(time * 1.3f) * 8f);
                _fruit.localScale = _fruitScale * EaseOutBack(appear);
                if (appear < 1f)
                {
                    // 出た瞬間の、広がって消える輪。
                    DrawRing(_fruitCenter, Mathf.Lerp(0.6f, rules.HotAreaOuterRadius + 1.2f, appear), 0.9f * (1f - appear));
                    ringUsed = true;
                }
                if (_guides != null)
                {
                    _guides.Add(_fruitCenter, _color, _icon, "HOT AREA", announce: "hot", pulse: true, onScreen: true, lift: 1.3f);
                }
            }
            else
            {
                _fruit.gameObject.SetActive(false);
            }

            if (AnnouncedRound > 0)
            {
                Vector3 center = ToWorld(_feed.CoinSpawns[announcedSpot].Position);
                int seconds = Mathf.CeilToInt(ticksUntil / (float)SimTime.TicksPerSecond);
                if (!ringUsed)
                {
                    float blink = 0.3f + 0.4f * (0.5f + 0.5f * Mathf.Sin(time * 9f));
                    DrawRing(center, rules.HotAreaOuterRadius + 0.4f, blink);
                    ringUsed = true;
                }
                if (_guides != null)
                {
                    _guides.Add(center, _color, _icon, $"HOT AREA {seconds}", 0.85f, announce: "hotnext", pulse: true, onScreen: true,
                        lift: rules.HotAreaOuterRadius + 0.6f);
                }
            }
            _ring.enabled = ringUsed;
        }

        // フルーツを取った回が増えたら、その地点で光らせて音を鳴らす（巻き戻しで戻ったときは何も出さない）。
        private void DetectPickup(MatchRules rules)
        {
            int taken = _feed.Match.HotFruitTaken;
            if (taken == _shownTaken)
            {
                return;
            }
            bool newer = taken > _shownTaken;
            _shownTaken = taken;
            if (!newer)
            {
                return;
            }
            int spot = HotAreaRules.Spot(rules, _feed.CoinSpawns, taken);
            if (spot < 0)
            {
                return;
            }
            PickupCount++;
            Vector3 center = ToWorld(_feed.CoinSpawns[spot].Position);
            if (_burst != null)
            {
                _burst.Play(center, $"+{rules.HotAreaFruitGauge / CombatGaugeUnitsPerPercent}%");
            }
            if (_audio != null)
            {
                _audio.Play(SoundId.HotFruit);
            }
        }

        // ゲージの 1% あたりの単位（CombatTuning.GaugePerPercent。表示の文字に使う）。
        private const int CombatGaugeUnitsPerPercent = Airerage.Core.Combat.CombatTuning.GaugePerPercent;

        // 0 から少し行き過ぎて 1 に戻る（弾んで大きくなる）。
        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.9f;
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        private void Hide()
        {
            AnnouncedRound = 0;
            FruitRound = 0;
            if (_fruit != null)
            {
                _fruit.gameObject.SetActive(false);
            }
            if (_ring != null)
            {
                _ring.enabled = false;
            }
        }

        private void DrawRing(Vector3 center, float radius, float alpha)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i / (float)RingSegments * Mathf.PI * 2f;
                _ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            Color color = _color;
            color.a = alpha;
            _ring.startColor = _ring.endColor = color;
        }

        private static Vector3 ToWorld(System.Numerics.Vector2 point)
        {
            return new Vector3(point.X, point.Y, 0f);
        }
    }
}
