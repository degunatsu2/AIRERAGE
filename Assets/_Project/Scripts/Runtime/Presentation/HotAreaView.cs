using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ホットエリア（ユーザーの要望、2026-11-02。HotAreaRules）の表示。状態を読むだけで、書き換えない。場所と時刻はティック数とルールから求める。
    /// - 予告（出る 10 秒前から）: 出る地点に点滅する橙色の輪と「HOT AREA n」。拠点の移動の予告と同じく控えめに（帯や警報の音はない。
    ///   画面のお知らせの札「HOT AREA」は NoticeHud）。
    /// - 出ている間: 中心にフルーツ（ゆっくり回って上下に揺れる）。周りのコインは CoinView が描く。
    /// - 画面の外にあるときは、画面の端の矢印と誘導表示の札（左にフルーツの絵、右に「HOT AREA」）。出たときと予告が出たときは札を軽く
    ///   目立たせる（EdgeGuideLayer の announce）。
    /// - だれかがフルーツを取ったら（MatchState.HotFruitTaken が変わった）、その地点で光って「+30%」を出し、音を鳴らす。
    /// 動きは Time.time で決める（巻き戻しとは関係ない）。
    /// </summary>
    public sealed class HotAreaView : MonoBehaviour
    {
        private const int RingSegments = 56;

        [Tooltip("状態を読む相手（SimulationRunner・LocalMatchRunner・OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("フルーツの見た目（シーンでは隠しておく。この表示が出し入れする）")]
        [SerializeField] private Transform _fruit;
        [Tooltip("予告の輪")]
        [SerializeField] private LineRenderer _ring;
        [Tooltip("フルーツを取ったときの光（なくてもよい）")]
        [SerializeField] private BurstEffectView _burst;
        [Tooltip("フルーツを取った音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("画面の外のときの誘導表示（矢印と札）を描く層")]
        [SerializeField] private EdgeGuideLayer _guides;
        [SerializeField] private Color _color = new Color(1f, 0.55f, 0.15f);
        [SerializeField] private float _spinDegreesPerSecond = 70f;
        [SerializeField] private float _bobHeight = 0.18f;

        private ISimulationFeed _feed;
        private Texture2D _icon;
        private GUIStyle _label;
        private Vector3 _center;
        private int _seconds;
        private bool _offscreen;
        private int _shownTaken = -1;
        private MatchRules _rules;

        /// <summary>予告している回（なければ 0）と、フルーツを出している回（なければ 0）。テスト用。</summary>
        public int AnnouncedRound { get; private set; }
        public int FruitRound { get; private set; }

        /// <summary>フルーツを取られた回数（表示が見つけた回数。テスト用）。</summary>
        public int PickupCount { get; private set; }

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
            _icon = GuideIcons.Fruit(64);
            if (_guides == null)
            {
                Debug.LogError($"{name}: 誘導表示の層（EdgeGuideLayer）が設定されていません。画面の外のホットエリアを示せません", this);
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
            }
            if (rules == null || _feed.CoinSpawns == null)
            {
                Hide();
                return;
            }
            int tick = _feed.CurrentTick;
            DetectPickup(rules);

            AnnouncedRound = HotAreaRules.AnnouncedRound(rules, tick, out int ticksUntil);
            int announcedSpot = AnnouncedRound > 0 ? HotAreaRules.Spot(rules, _feed.CoinSpawns, AnnouncedRound) : -1;
            if (announcedSpot < 0)
            {
                AnnouncedRound = 0;
            }
            FruitRound = HotAreaRules.FruitPresent(rules, _feed.Match, tick, out int round) ? round : 0;
            int fruitSpot = FruitRound > 0 ? HotAreaRules.Spot(rules, _feed.CoinSpawns, FruitRound) : -1;
            if (fruitSpot < 0)
            {
                FruitRound = 0;
            }

            float time = Time.time;
            _offscreen = false;
            if (FruitRound > 0)
            {
                Vector3 center = ToWorld(_feed.CoinSpawns[fruitSpot].Position);
                _fruit.gameObject.SetActive(true);
                _fruit.position = center + new Vector3(0f, Mathf.Sin(time * 2.2f) * _bobHeight, 0f);
                _fruit.rotation = Quaternion.Euler(0f, time * _spinDegreesPerSecond, Mathf.Sin(time * 1.3f) * 8f);
                _offscreen |= _guides != null && _guides.Add(center, _color, _icon, "HOT AREA", announce: "hot");
            }
            else
            {
                _fruit.gameObject.SetActive(false);
            }

            if (AnnouncedRound > 0)
            {
                _center = ToWorld(_feed.CoinSpawns[announcedSpot].Position);
                _seconds = Mathf.CeilToInt(ticksUntil / (float)SimTime.TicksPerSecond);
                float blink = 0.3f + 0.4f * (0.5f + 0.5f * Mathf.Sin(time * 9f));
                DrawRing(_center, rules.HotAreaOuterRadius + 0.4f, blink);
                _ring.enabled = true;
                _offscreen = _guides != null && _guides.Add(_center, _color, _icon, $"HOT AREA {_seconds}", 0.8f, announce: "hotnext");
            }
            else
            {
                _ring.enabled = false;
            }
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

        private void OnGUI()
        {
            if (AnnouncedRound <= 0 || _offscreen)
            {
                return;
            }
            if (_label == null)
            {
                _label = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            }
            float radius = _rules != null ? _rules.HotAreaOuterRadius + 0.4f : 2.8f;
            Vector3 p = _camera.WorldToScreenPoint(_center + new Vector3(0f, radius + 0.6f, 0f));
            if (p.z <= 0f)
            {
                return;
            }
            var rect = new Rect(p.x - 110f, Screen.height - p.y - 16f, 220f, 30f);
            Color previous = GUI.color;
            Color color = _color;
            color.a = 0.85f;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), $"HOT AREA {_seconds}", _label);
            GUI.color = color;
            GUI.Label(rect, $"HOT AREA {_seconds}", _label);
            GUI.color = previous;
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
