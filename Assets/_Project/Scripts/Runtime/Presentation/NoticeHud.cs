using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 試合中のお知らせ（ユーザーの選択、2026-10-14。案 C ＋ 大きな出来事だけ案 B。告知の並べ方は 2026-10-30 の要望）。
    /// - タイマーの右の告知（札）: 札が文字ごとタイマーの台形の後ろから右へせり出し、終わると同じように左へ動いてタイマーの中へ引っ込む
    ///   （札の本来の左の斜めの縁より左は切り落とすので、タイマーの側にははみ出さない。2026-10-30 の要望）。タイムラインの
    ///   ように、新しい告知がいちばん上（タイマーの右の上の端）に出て、前からの告知は下へずれる。札は平行四辺形で、左の縁をタイマーの
    ///   斜めの縁に合わせる（誘導表示の札と紛らわしくないように絵はなく、縁の色で種類を分ける）。告知はこれからもすべてここに出す。
    ///   - キルログ: 「撃破した人（緑の文字）・赤い衝撃波の印 → 撃破された人（赤い文字）・倒れた人の印」（例: CPU2💥→CPU3🤸）。
    ///     最後にダメージを与えた人がいなければ（自分で落ちた）、撃破した人の名前は出さない。
    ///   - 拠点の移動の予告「BANK MOVES」（水色。移るまでの秒）、100 枚のコインの予告「JACKPOT」（虹色。出るまでの秒）と、出た後の
    ///     「JACKPOT 100」。
    ///   - 試合から抜けた人（ネットワーク対戦。docs/15 T6-10）: 「P3 LEFT」（灰色）。
    ///   秒を数える告知（拠点の移動・100 枚のコインの予告）は、その時が来るまで出す。ほかの告知は NoticeSeconds 秒で引っ込む。
    ///   画面が狭くて右の表と重なるときは札を小さくし、それでも入らなければタイマーの下に出す。
    /// - 画面を横切る帯（BannerStrip）: 100 枚のコインの予告の始まり「JACKPOT INCOMING」（虹色）と、COIN RUSH の始まり
    ///   （金色。短いファンファーレ）。
    /// 拠点と 100 枚のコインの時刻は、ティック数とルールだけから決まる（BankRules、FieldCoinRules、MatchRules）。巻き戻しでもずれない。
    /// 撃破は、撃破された数（CombatState.Falls）が増えた人と、撃破の数（Knockouts）が増えた人から見つける（同じフレームに複数なら、
    /// 前のフレームの最後に攻撃した人 LastAttacker で組にする）。
    /// 拠点の予告の呼び鈴は BankView、100 枚のコインの警報は MatchAtmosphere が鳴らす。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class NoticeHud : MonoBehaviour
    {
        /// <summary>告知を出しておく秒数（秒を数える告知を除く。ユーザーの要望、2026-10-30）。</summary>
        public const float NoticeSeconds = 5f;

        /// <summary>100 枚のコインが出た後に「JACKPOT 100」の札を残すティック（NoticeSeconds 秒）。</summary>
        public const int JackpotChipTicks = (int)(NoticeSeconds * SimTime.TicksPerSecond);

        public const int ChipHeight = 36;
        public const float ChipGap = 4f;
        public const float ChipTop = 4f;

        /// <summary>秒を数える告知の札の幅（ピクセル）。</summary>
        public const int CountdownChipWidth = 260;

        /// <summary>右上の全員の表（ScoreboardHud）と王冠の分として、画面の右に空けておく幅（ピクセル）。</summary>
        public const float ScoreboardReserve = 370f;

        /// <summary>札を小さくするときの下限（これより小さくしないと入らなければ、タイマーの下に出す）。</summary>
        public const float MinChipScale = 0.65f;

        /// <summary>タイマーの右の縁に沿って並ぶ札の数（倍率が 1 のとき。それより下の札は、流れのバーの右に置く）。</summary>
        public const int BesideTimerSlots = 2;

        /// <summary>札の左右の斜めの縁が、上から下へ左に寄る量（ピクセル。タイマーの台形の右の縁と同じ傾き）。</summary>
        public const float ChipSlant = ChipHeight * TimerPlate.EdgeSlope;

        // 同時に出す札の数の上限（超えたら、時間で消える古い札から引っ込める）。
        private const int MaxChips = 6;
        // せり出す・引っ込む時間と、その前後に場所を空ける・詰める時間（ほかの札とすれ違って重ならないように）。
        private const float SlideSeconds = 0.3f;
        private const float SpaceSeconds = 0.15f;

        // せり出す途中の札を、本来の左の斜めの縁で切り落とすための横の切り分けの数（縁が斜めなので、細い帯ごとに切る）。
        private const int SlideStrips = 6;
        private const float IconSize = 24f;
        private const float ArrowSize = 18f;
        private const float KillPadding = 10f;
        private const float KillGap = 6f;

        private static readonly Color KillerColor = new Color(0.45f, 1f, 0.45f);
        private static readonly Color VictimColor = new Color(1f, 0.36f, 0.32f);
        private static readonly Color KillFrameColor = new Color(0.78f, 0.8f, 0.86f);
        private static readonly Color LeftColor = new Color(0.7f, 0.72f, 0.78f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("COIN RUSH のファンファーレ（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("画面が狭くてタイマーの右に札が入らないとき、札を積み始める高さ（ピクセル。タイマーの札と流れのバーの下）")]
        [SerializeField] private float _fallbackTop = 106f;
        [Tooltip("帯を出す秒数")]
        [SerializeField] private float _bandSeconds = 2.6f;
        [SerializeField] private Color _bankColor = new Color(0.31f, 0.84f, 1f);
        [SerializeField] private Color _rushColor = new Color(1f, 0.8f, 0.2f);

        // 出した順（新しい札が後ろ）。
        private readonly List<Chip> _chips = new List<Chip>();
        private readonly List<string> _activeKeys = new List<string>();
        private readonly List<Rect> _rects = new List<Rect>();
        private readonly List<float> _widths = new List<float>();
        private readonly List<float> _presences = new List<float>();
        private readonly Dictionary<int, Texture2D[]> _shapes = new Dictionary<int, Texture2D[]>();
        private ISimulationFeed _feed;
        private MatchRules _rules;
        private int _lastTick = -1;
        private GUIStyle _title;
        private GUIStyle _seconds;
        private GUIStyle _name;
        private Texture2D _burstIcon;
        private Texture2D _fallenIcon;
        private Texture2D _arrowIcon;
        private readonly BannerStrip _strip = new BannerStrip();
        private Band _band;
        private int _timedSerial;

        // キャラクターごとの、前のフレームの様子（撃破と、試合から抜けたかを見つける）。
        private int[] _falls = new int[0];
        private int[] _knockouts = new int[0];
        private int[] _lastAttacker = new int[0];
        private bool[] _left = new bool[0];

        private sealed class Chip
        {
            public string Key;
            public string Title;
            public int Seconds = -1;
            public bool Rainbow;
            public Color Color;
            public float AppearedAt;
            public float LeftAt = -1f;

            // 時間で消える札（キルログ・抜けた人）が引っ込む時刻（秒を数える札は -1。出来事が終わったら引っ込む）。
            public float ExpiresAt = -1f;

            // キルログ（撃破した人の名前は、いなければ空）。
            public bool Kill;
            public string Killer;
            public string Victim;
            public float Width;

            public bool Active => LeftAt < 0f;
        }

        private sealed class Band
        {
            public string Title;
            public string Sub;
            public bool Rainbow;
            public Color Color;
            public float StartedAt;
        }

        /// <summary>キルログの 1 行（撃破した人の名前は、いなければ空）。</summary>
        public struct KillEntry
        {
            public string Killer;
            public string Victim;
        }

        /// <summary>今出している札の見出し（上から。新しい順。キルログは「撃破した人 > 撃破された人」。テスト用）。</summary>
        public IReadOnlyList<string> ChipTitles
        {
            get
            {
                var titles = new List<string>();
                for (int i = _chips.Count - 1; i >= 0; i--)
                {
                    if (_chips[i].Active)
                    {
                        titles.Add(_chips[i].Title);
                    }
                }
                return titles;
            }
        }

        /// <summary>今出しているキルログ（上から。新しい順。テスト用）。</summary>
        public IReadOnlyList<KillEntry> KillLog
        {
            get
            {
                var entries = new List<KillEntry>();
                for (int i = _chips.Count - 1; i >= 0; i--)
                {
                    if (_chips[i].Active && _chips[i].Kill)
                    {
                        entries.Add(new KillEntry { Killer = _chips[i].Killer, Victim = _chips[i].Victim });
                    }
                }
                return entries;
            }
        }

        /// <summary>これまでに出したキルログの数（テスト用）。</summary>
        public int KillsShown { get; private set; }

        /// <summary>今出している帯の文字（なければ空。テスト用）。</summary>
        public string BandTitle => _band != null && Time.time - _band.StartedAt < _bandSeconds ? _band.Title : string.Empty;

        /// <summary>これまでに出した帯の数（テスト用）。</summary>
        public int BandsShown { get; private set; }

        /// <summary>札をタイマーの右に出しているか（画面が狭ければ、タイマーの下。テスト用）。</summary>
        public bool ChipsBesideTheTimer { get; private set; } = true;

        /// <summary>抜けた人の札を出した回数（テスト用）。</summary>
        public int LeftChipsShown { get; private set; }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            foreach (Texture2D[] shape in _shapes.Values)
            {
                Destroy(shape[0]);
                Destroy(shape[1]);
            }
            _shapes.Clear();
            if (_burstIcon != null)
            {
                Destroy(_burstIcon);
                Destroy(_fallenIcon);
                Destroy(_arrowIcon);
            }
        }

        private void Update()
        {
            MatchRules rules = _feed.Rules;
            if (!ReferenceEquals(rules, _rules))
            {
                // 試合が始まった、始め直した、または抜けた（オンライン）。途中から見始めても、前の出来事の帯と札は出さない。
                _rules = rules;
                _chips.Clear();
                _band = null;
                _lastTick = rules != null ? _feed.CurrentTick : -1;
                ResetWatch();
            }
            _activeKeys.Clear();
            if (rules != null)
            {
                int tick = _feed.CurrentTick;
                MatchPhase phase = MatchFlow.PhaseAt(rules, _feed.Match, tick);
                DetectBands(rules, phase, tick);
                if (phase == MatchPhase.Playing || phase == MatchPhase.Overtime)
                {
                    UpdateBankChip(rules, tick);
                    UpdateJackpotChip(rules, tick);
                }
                WatchCharacters();
                _lastTick = tick;
            }
            float now = Time.time;
            int active = 0;
            foreach (Chip chip in _chips)
            {
                if (!chip.Active)
                {
                    continue;
                }
                // 出来事が終わった札と、時間が来た札は、タイマーの中へ引っ込める。
                bool over = chip.ExpiresAt >= 0f ? now >= chip.ExpiresAt : !_activeKeys.Contains(chip.Key);
                if (over)
                {
                    chip.LeftAt = now;
                }
                else
                {
                    active++;
                }
            }
            // 多すぎれば、時間で消える古い札から引っ込める。
            for (int i = 0; i < _chips.Count && active > MaxChips; i++)
            {
                if (_chips[i].Active && _chips[i].ExpiresAt >= 0f)
                {
                    _chips[i].LeftAt = now;
                    active--;
                }
            }
            _chips.RemoveAll(c => !c.Active && now - c.LeftAt > SlideSeconds + SpaceSeconds);
        }

        // 前のフレームから今のティックまでの間に始まった出来事の帯を出す。
        private void DetectBands(MatchRules rules, MatchPhase phase, int tick)
        {
            int announce = FieldCoinRules.JackpotAnnounceTick(rules);
            if (announce >= 0 && _lastTick < announce && tick >= announce
                && FieldCoinRules.JackpotSpot(rules, _feed.CoinSpawns) >= 0)
            {
                StartBand("JACKPOT INCOMING", $"{CoinView.RainbowValue} COINS IN {Seconds(rules.JackpotNoticeTicks)} SECONDS", true, Color.white);
            }
            if (rules.HasTimeLimit && rules.LateGameTicks > 0 && phase == MatchPhase.Playing)
            {
                int rush = rules.CountdownTicks + rules.DurationTicks - rules.LateGameTicks;
                if (_lastTick < rush && tick >= rush)
                {
                    StartBand("COIN RUSH", $"LAST {Seconds(rules.LateGameTicks)} SECONDS  -  MORE COINS", false, _rushColor);
                    if (_audio != null)
                    {
                        _audio.Play(SoundId.CoinRush);
                    }
                }
            }
        }

        private void StartBand(string title, string sub, bool rainbow, Color color)
        {
            _band = new Band { Title = title, Sub = sub, Rainbow = rainbow, Color = color, StartedAt = Time.time };
            BandsShown++;
        }

        private void UpdateBankChip(MatchRules rules, int tick)
        {
            IReadOnlyList<BankSpot> spots = _feed.BankSpots;
            if (spots == null || spots.Count < 2)
            {
                return;
            }
            if (BankRules.NextSpotAt(rules, spots.Count, tick, out int ticksUntilMove) >= 0 && ticksUntilMove <= rules.BankNoticeTicks)
            {
                Chip chip = Show("bank", "BANK MOVES", false, _bankColor);
                chip.Seconds = Seconds(ticksUntilMove);
            }
        }

        private void UpdateJackpotChip(MatchRules rules, int tick)
        {
            int announce = FieldCoinRules.JackpotAnnounceTick(rules);
            int spawn = FieldCoinRules.JackpotSpawnTick(rules);
            if (announce < 0 || tick < announce || tick > spawn + JackpotChipTicks
                || FieldCoinRules.JackpotSpot(rules, _feed.CoinSpawns) < 0)
            {
                return;
            }
            if (tick <= spawn)
            {
                Chip chip = Show("jackpot", "JACKPOT", true, Color.white);
                chip.Seconds = Seconds(spawn + 1 - tick);
            }
            else
            {
                Chip chip = Show("jackpot", $"JACKPOT {CoinView.RainbowValue}", true, Color.white);
                chip.Seconds = -1;
            }
        }

        // 前のフレームの様子を、今の様子で取り直す（試合が変わった・人数が変わったとき。前からの撃破や抜けた人は出さない）。
        private void ResetWatch()
        {
            int count = _feed.Rules != null ? _feed.CharacterCount : 0;
            _falls = new int[count];
            _knockouts = new int[count];
            _lastAttacker = new int[count];
            _left = new bool[count];
            for (int i = 0; i < count; i++)
            {
                CombatState c = _feed.GetState(i).Combat;
                _falls[i] = c.Falls;
                _knockouts[i] = c.Knockouts;
                _lastAttacker[i] = c.HasLastAttacker ? c.LastAttacker : -1;
                _left[i] = c.HasLeft;
            }
        }

        // 撃破（キルログ）と、試合から抜けた人の札。
        private void WatchCharacters()
        {
            int count = _feed.CharacterCount;
            if (_falls.Length != count)
            {
                ResetWatch();
                return;
            }
            // このフレームに撃破の数が増えた分（撃破した人の候補）。
            var credits = new int[count];
            for (int i = 0; i < count; i++)
            {
                credits[i] = Mathf.Max(0, _feed.GetState(i).Combat.Knockouts - _knockouts[i]);
            }
            for (int i = 0; i < count; i++)
            {
                CombatState c = _feed.GetState(i).Combat;
                for (int fall = _falls[i]; fall < c.Falls; fall++)
                {
                    int killer = Claim(credits, _lastAttacker[i], i);
                    AddKill(killer >= 0 ? _feed.LabelOf(killer) : string.Empty, _feed.LabelOf(i));
                }
                if (c.HasLeft && !_left[i])
                {
                    LeftChipsShown++;
                    Chip chip = AddTimed("left" + i, $"{_feed.LabelOf(i)} LEFT", LeftColor);
                    chip.Seconds = -1;
                }
                _left[i] = c.HasLeft;
            }
            for (int i = 0; i < count; i++)
            {
                CombatState c = _feed.GetState(i).Combat;
                _falls[i] = c.Falls;
                _knockouts[i] = c.Knockouts;
                _lastAttacker[i] = c.HasLastAttacker ? c.LastAttacker : -1;
            }
        }

        // 撃破された人 victim の手柄の持ち主（前のフレームの最後に攻撃した人を優先。撃破の数が増えていなければ、増えたほかの人。いなければ -1）。
        private static int Claim(int[] credits, int lastAttacker, int victim)
        {
            if (lastAttacker >= 0 && lastAttacker < credits.Length && credits[lastAttacker] > 0)
            {
                credits[lastAttacker]--;
                return lastAttacker;
            }
            for (int j = 0; j < credits.Length; j++)
            {
                if (j != victim && credits[j] > 0)
                {
                    credits[j]--;
                    return j;
                }
            }
            return -1;
        }

        private void AddKill(string killer, string victim)
        {
            Chip chip = AddTimed("ko" + _timedSerial, killer.Length > 0 ? $"{killer} > {victim}" : $"> {victim}", KillFrameColor);
            chip.Kill = true;
            chip.Killer = killer;
            chip.Victim = victim;
            KillsShown++;
        }

        // 時間で消える札を、新しくいちばん上に出す。
        private Chip AddTimed(string key, string title, Color color)
        {
            _timedSerial++;
            var chip = new Chip { Key = key, Title = title, Color = color, AppearedAt = Time.time, ExpiresAt = Time.time + NoticeSeconds };
            _chips.Add(chip);
            return chip;
        }

        // 出来事が続いている間の札を出す（もう出ていれば、見出しだけを変える。新しく出すときは、いちばん上に）。
        private Chip Show(string key, string title, bool jackpot, Color color)
        {
            _activeKeys.Add(key);
            Chip chip = null;
            foreach (Chip c in _chips)
            {
                if (c.Key == key && c.Active)
                {
                    chip = c;
                    break;
                }
            }
            if (chip == null)
            {
                chip = new Chip { Key = key, AppearedAt = Time.time };
                _chips.Add(chip);
            }
            chip.Title = title;
            chip.Rainbow = jackpot;
            chip.Color = color;
            return chip;
        }

        // ティックを、切り上げた秒にする。
        private static int Seconds(int ticks)
        {
            return (ticks + SimTime.TicksPerSecond - 1) / SimTime.TicksPerSecond;
        }

        /// <summary>
        /// 札の置き場所（画面の座標。左上の角）を求める。widths は札の幅、presences は札の出ている割合（0〜1。出る途中・引っ込む途中は
        /// その割合だけ場所を取る）で、どちらも上から（新しい順）。rects に札の場所（大きさは scale を掛けた値）を入れ、札の大きさの倍率を返す。
        /// beside は、タイマーの右に入ったか（入らなければ、タイマーの下の fallbackTop から中央に積む）。
        /// タイマーの右では、札の左の斜めの縁をタイマーの右の縁に沿わせる。タイマーより下の札は、流れのバーにかからない所に置く。
        /// </summary>
        public static float Place(IReadOnlyList<float> widths, IReadOnlyList<float> presences, float screenWidth, float fallbackTop,
            List<Rect> rects, out bool beside)
        {
            rects.Clear();
            float centerX = screenWidth * 0.5f;
            float widest = 0f;
            float widestBelow = 0f;
            for (int i = 0; i < widths.Count; i++)
            {
                widest = Mathf.Max(widest, widths[i]);
                if (i >= BesideTimerSlots)
                {
                    widestBelow = Mathf.Max(widestBelow, widths[i]);
                }
            }
            // 右の表に重ならない倍率（タイマーより下の札は、流れのバーの右から始まるので、そこからの幅でも確かめる）。
            float right = screenWidth - ScoreboardReserve;
            float start = TimerPlate.RightEdgeAt(centerX, ChipTop) + ChipGap - ChipSlant;
            float scale = widest > 0f ? Mathf.Min(1f, (right - start) / widest) : 1f;
            if (widestBelow > 0f)
            {
                scale = Mathf.Min(scale, (right - (centerX + TimerPlate.TimelineWidth * 0.5f + ChipGap)) / widestBelow);
            }
            beside = scale >= MinChipScale;
            if (!beside)
            {
                scale = widest > 0f ? Mathf.Min(1f, (screenWidth - 16f) / widest) : 1f;
            }
            float y = beside ? ChipTop : fallbackTop;
            for (int i = 0; i < widths.Count; i++)
            {
                float width = widths[i] * scale;
                float height = ChipHeight * scale;
                float x;
                if (beside)
                {
                    // 札の左上の角が、タイマーの右の縁から ChipGap だけ右になる。
                    x = TimerPlate.RightEdgeAt(centerX, y) + ChipGap - ChipSlant * scale;
                    if (y + height > TimerPlate.Height + 2f)
                    {
                        x = Mathf.Max(x, centerX + TimerPlate.TimelineWidth * 0.5f + ChipGap);
                    }
                }
                else
                {
                    x = (screenWidth - width) * 0.5f;
                }
                rects.Add(new Rect(x, y, width, height));
                y += (ChipHeight + ChipGap) * scale * Mathf.Clamp01(presences[i]);
            }
            return scale;
        }

        private void OnGUI()
        {
            if (_chips.Count == 0 && BandTitle.Length == 0)
            {
                return;
            }
            EnsureResources();
            if (BandTitle.Length > 0)
            {
                _strip.Draw(_band.Title, _band.Sub, _band.Color, _band.Rainbow, (Time.time - _band.StartedAt) / _bandSeconds, Screen.height * 0.3f);
            }
            if (_chips.Count == 0)
            {
                return;
            }

            // 上から（新しい順）に、幅と出ている割合を並べる。
            float now = Time.time;
            _widths.Clear();
            _presences.Clear();
            for (int i = _chips.Count - 1; i >= 0; i--)
            {
                Chip chip = _chips[i];
                if (chip.Width <= 0f)
                {
                    // 札の形の絵と同じ幅（10 ピクセルごとに切り上げ）にする（狭いと、右上の斜めの角が切れる。ユーザーの指摘、2026-10-30）。
                    chip.Width = ShapeWidth(chip.Kill ? KillWidth(chip) : CountdownChipWidth);
                }
                _widths.Add(chip.Width);
                _presences.Add(Space(chip, now));
            }
            float top = _fallbackTop;
            if (_rules != null && MatchFlow.PhaseAt(_rules, _feed.Match, _feed.CurrentTick) == MatchPhase.Overtime)
            {
                // アディショナルタイムは、タイマーの下に「BANK = WIN」の行がある（MatchHud）。
                top += 34f;
            }
            float scale = Place(_widths, _presences, Screen.width, top, _rects, out bool beside);
            ChipsBesideTheTimer = beside;
            Matrix4x4 matrix = GUI.matrix;
            for (int k = 0; k < _rects.Count; k++)
            {
                Chip chip = _chips[_chips.Count - 1 - k];
                Rect rect = _rects[k];
                GUI.matrix = matrix * Matrix4x4.TRS(new Vector3(rect.x, rect.y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
                DrawChip(chip, Extend(chip, now));
            }
            GUI.matrix = matrix;
        }

        // 札のせり出している割合（出るときは、場所を空けてから 0 → 1。引っ込むときは 1 → 0 の後に場所を詰める）。
        private static float Extend(Chip chip, float now)
        {
            return chip.Active
                ? Ease((now - chip.AppearedAt - SpaceSeconds) / SlideSeconds)
                : 1f - Ease((now - chip.LeftAt) / SlideSeconds);
        }

        // 札が取っている場所の割合（出るときは先に空け、引っ込むときは引っ込みきってから詰める）。
        private static float Space(Chip chip, float now)
        {
            return chip.Active
                ? Ease((now - chip.AppearedAt) / SpaceSeconds)
                : 1f - Ease((now - chip.LeftAt - SlideSeconds) / SpaceSeconds);
        }

        /// <summary>
        /// せり出している割合 extend（0〜1）の札の、本来の場所からの横のずれ（札の左上が原点の座標。0 で本来の場所、-幅 でタイマーの後ろに
        /// 隠れきる）。
        /// </summary>
        public static float SlideOffset(float width, float extend)
        {
            return -(1f - Mathf.Clamp01(extend)) * width;
        }

        // 札を、文字ごとタイマーの後ろから右へせり出す形で描く（ユーザーの要望、2026-10-30: その場に現れたり消えたりしない）。札を左へずらして描き、
        // 本来の左の斜めの縁（タイマーの右の縁に沿う線）より左は切り落とす。座標は、札の本来の場所の左上が原点。
        private void DrawChip(Chip chip, float extend)
        {
            if (extend <= 0f)
            {
                return;
            }
            if (extend >= 1f)
            {
                DrawChipBody(chip);
                return;
            }
            float offset = SlideOffset(chip.Width, extend);
            float strip = ChipHeight / (float)SlideStrips;
            for (int k = 0; k < SlideStrips; k++)
            {
                float top = k * strip;
                // 帯の下の端での本来の左の縁（斜めの縁は下ほど左なので、帯の中では最も左。本来の場所の札は切れない）。
                float left = ChipSlant * (1f - (top + strip) / ChipHeight);
                GUI.BeginGroup(new Rect(left, top, chip.Width - left, strip));
                GUI.BeginGroup(new Rect(offset - left, -top, chip.Width, ChipHeight));
                DrawChipBody(chip);
                GUI.EndGroup();
                GUI.EndGroup();
            }
        }

        // 札の中身と縁と文字（座標は札の左上が原点）。
        private void DrawChipBody(Chip chip)
        {
            Texture2D[] shape = Shape(Mathf.RoundToInt(chip.Width));
            Color previous = GUI.color;
            GUI.BeginGroup(new Rect(0f, 0f, chip.Width, ChipHeight));
            var local = new Rect(0f, 0f, shape[0].width, ChipHeight);
            GUI.color = new Color(0.03f, 0.05f, 0.09f, 0.82f);
            GUI.DrawTexture(local, shape[0]);
            GUI.color = chip.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.6f, 1f), 0.65f, 1f) : chip.Color;
            GUI.DrawTexture(local, shape[1]);
            GUI.color = previous;
            if (chip.Kill)
            {
                DrawKill(chip);
            }
            else
            {
                GuidePlate.DrawText(new Rect(ChipSlant + 12f, 0f, chip.Width - ChipSlant - 64f, ChipHeight), chip.Title, _title, Color.white);
                if (chip.Seconds >= 0)
                {
                    GuidePlate.DrawText(new Rect(chip.Width - ChipSlant - 48f, 0f, 40f, ChipHeight), chip.Seconds.ToString(), _seconds, Color.white);
                }
            }
            GUI.EndGroup();
        }

        // キルログの中身: 撃破した人（緑）・衝撃波 → 撃破された人（赤）・倒れた人。
        private void DrawKill(Chip chip)
        {
            float x = ChipSlant + KillPadding;
            float mid = ChipHeight * 0.5f;
            if (chip.Killer.Length > 0)
            {
                float killer = _name.CalcSize(new GUIContent(chip.Killer)).x;
                GuidePlate.DrawText(new Rect(x, 0f, killer + 4f, ChipHeight), chip.Killer, _name, KillerColor);
                x += killer + KillGap * 0.5f;
            }
            GUI.DrawTexture(new Rect(x, mid - IconSize * 0.5f, IconSize, IconSize), _burstIcon);
            x += IconSize + KillGap * 0.5f;
            GUI.DrawTexture(new Rect(x, mid - ArrowSize * 0.5f, ArrowSize, ArrowSize), _arrowIcon);
            x += ArrowSize + KillGap;
            float victim = _name.CalcSize(new GUIContent(chip.Victim)).x;
            GuidePlate.DrawText(new Rect(x, 0f, victim + 4f, ChipHeight), chip.Victim, _name, VictimColor);
            x += victim + KillGap * 0.5f;
            GUI.DrawTexture(new Rect(x, mid - IconSize * 0.5f, IconSize, IconSize), _fallenIcon);
        }

        // キルログの札の幅（中身の幅と、左右の余白・斜めの縁）。
        private float KillWidth(Chip chip)
        {
            float width = ChipSlant + KillPadding;
            if (chip.Killer.Length > 0)
            {
                width += _name.CalcSize(new GUIContent(chip.Killer)).x + KillGap * 0.5f;
            }
            width += IconSize + KillGap * 0.5f + ArrowSize + KillGap;
            width += _name.CalcSize(new GUIContent(chip.Victim)).x + KillGap * 0.5f + IconSize;
            return width + KillPadding + ChipSlant;
        }

        /// <summary>札の幅（中身の幅を 10 ピクセルごとに切り上げる。札の形の絵はこの幅ごとに作って使い回す）。</summary>
        public static float ShapeWidth(float width)
        {
            int pixels = Mathf.CeilToInt(width - 0.01f);
            return (pixels + 9) / 10 * 10;
        }

        // 幅 width の札の形（中身と縁）。width は ShapeWidth で切り上げた幅。
        private Texture2D[] Shape(int width)
        {
            int key = (int)ShapeWidth(width);
            if (!_shapes.TryGetValue(key, out Texture2D[] shape))
            {
                shape = new[]
                {
                    TimerPlate.Create($"NoticeChipFill{key}", key, ChipHeight, (x, y) => InsideChip(x, y, key, 0f) ? 1f : 0f),
                    TimerPlate.Create($"NoticeChipFrame{key}", key, ChipHeight,
                        (x, y) => InsideChip(x, y, key, 0f) && !InsideChip(x, y, key, 2.5f) ? 1f : 0f),
                };
                _shapes.Add(key, shape);
            }
            return shape;
        }

        // 札の形（平行四辺形）の中か。inset は内側へ縮める量（ピクセル）。
        private static bool InsideChip(int x, int yFromTop, int width, float inset)
        {
            float px = x + 0.5f;
            float py = yFromTop + 0.5f;
            if (py < inset || py > ChipHeight - inset)
            {
                return false;
            }
            float left = ChipSlant * (1f - py / ChipHeight);
            return px >= left + inset * 1.2f && px <= left + width - ChipSlant - inset * 1.2f;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private void EnsureResources()
        {
            if (_title != null)
            {
                return;
            }
            // 折り返さない（狭いと 2 行になって見切れていた。ユーザーの指摘、2026-10-14）。
            _title = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip });
            _seconds = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false });
            _name = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) });
            _title.normal.textColor = Color.white;
            _seconds.normal.textColor = Color.white;
            _name.normal.textColor = Color.white;
            _burstIcon = GuideIcons.Burst(48);
            _fallenIcon = GuideIcons.Fallen(48, Color.Lerp(VictimColor, Color.white, 0.25f));
            _arrowIcon = GuideIcons.Arrow(36, new Color(0.92f, 0.94f, 1f));
        }
    }
}
