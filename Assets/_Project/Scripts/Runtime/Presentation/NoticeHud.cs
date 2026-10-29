using System.Collections.Generic;
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
    /// 試合中のお知らせ（ユーザーの選択、2026-10-14。案 C ＋ 大きな出来事だけ案 B）。
    /// - タイマーの右に伸びる札（見出しと、右の端に出来事までの秒。プレイの邪魔にならないように、画面の上の端に。誘導表示の札と
    ///   紛らわしくないように絵はなく、左の縁をタイマーの台形の斜めの縁に合わせた平行四辺形。縁の色で種類を分ける。
    ///   ユーザーの指摘、2026-10-14）: 拠点の移動の予告「BANK MOVES」（水色。移るまでの秒）、100 枚のコインの予告
    ///   「JACKPOT」（虹色。出るまでの秒）と、出た直後の「JACKPOT 100」（JackpotChipTicks の間）。札はタイマーの後ろから右へ
    ///   伸びるように出て、終わると縮んで消える。重なれば縦に積む。画面が狭くて右の表と重なるときは、タイマーの下に出す。
    /// - 画面を横切る帯（BannerStrip）: 100 枚のコインの予告の始まり「JACKPOT INCOMING」（虹色）と、COIN RUSH の始まり
    ///   （金色。短いファンファーレ）。
    /// - 試合から抜けた人（ネットワーク対戦。docs/15 T6-10）: 「P3 LEFT」の札（灰色。LeftChipSeconds 秒）。
    /// 出来事の時刻は、ティック数とルールだけから決まる（BankRules、FieldCoinRules、MatchRules）。巻き戻しでもずれない。
    /// 拠点の予告の呼び鈴は BankView、100 枚のコインの警報は MatchAtmosphere が鳴らす。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class NoticeHud : MonoBehaviour
    {
        /// <summary>100 枚のコインが出た後に「JACKPOT 100」の札を残すティック（2 秒）。</summary>
        public const int JackpotChipTicks = 120;

        /// <summary>抜けた人の札を出しておく秒数。</summary>
        public const float LeftChipSeconds = 4f;

        private const int ChipWidth = 260;
        private const int ChipHeight = 36;
        private const float ChipGap = 4f;
        private const float ChipTop = 4f;

        // 右上の全員の表（ScoreboardHud）と王冠の分として、画面の右に空けておく幅（ピクセル）。
        private const float ScoreboardReserve = 370f;
        private const float ChipInSeconds = 0.3f;
        private const float ChipOutSeconds = 0.3f;

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

        private readonly List<Chip> _chips = new List<Chip>();
        private readonly List<string> _activeKeys = new List<string>();
        private ISimulationFeed _feed;
        private MatchRules _rules;
        private int _lastTick = -1;
        private Texture2D _chipFill;
        private Texture2D _chipFrame;
        private GUIStyle _title;
        private GUIStyle _seconds;
        private readonly BannerStrip _strip = new BannerStrip();
        private Band _band;

        // キャラクターごとの、試合から抜けたか（前のフレーム）と、抜けた時刻（表示の時間）。
        private bool[] _left = new bool[0];
        private float[] _leftAt = new float[0];

        private sealed class Chip
        {
            public string Key;
            public string Title;
            public int Seconds = -1;
            public bool Rainbow;
            public Color Color;
            public float AppearedAt;
            public float LeftAt = -1f;
            public float Y = -1f;

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

        /// <summary>今出している札の見出し（上から。テスト用）。</summary>
        public IReadOnlyList<string> ChipTitles
        {
            get
            {
                var titles = new List<string>();
                foreach (Chip chip in _chips)
                {
                    if (chip.Active)
                    {
                        titles.Add(chip.Title);
                    }
                }
                return titles;
            }
        }

        /// <summary>今出している帯の文字（なければ空。テスト用）。</summary>
        public string BandTitle => _band != null && Time.time - _band.StartedAt < _bandSeconds ? _band.Title : string.Empty;

        /// <summary>これまでに出した帯の数（テスト用）。</summary>
        public int BandsShown { get; private set; }

        /// <summary>札をタイマーの右に出しているか（画面が狭ければ、タイマーの下。テスト用）。</summary>
        public bool ChipsBesideTheTimer { get; private set; } = true;

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
            if (_chipFill != null)
            {
                Destroy(_chipFill);
                Destroy(_chipFrame);
            }
        }

        private void Update()
        {
            MatchRules rules = _feed.Rules;
            if (!ReferenceEquals(rules, _rules))
            {
                // 試合が始まった、始め直した、または抜けた（オンライン）。途中から見始めても、前の出来事の帯は出さない。
                _rules = rules;
                _chips.Clear();
                _band = null;
                _lastTick = rules != null ? _feed.CurrentTick : -1;
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
                UpdateLeftChips();
                _lastTick = tick;
            }
            // 出来事が終わった札は、つぶれるように消す。
            foreach (Chip chip in _chips)
            {
                if (chip.Active && !_activeKeys.Contains(chip.Key))
                {
                    chip.LeftAt = Time.time;
                }
            }
            _chips.RemoveAll(c => !c.Active && Time.time - c.LeftAt > ChipOutSeconds);
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

        // 試合から抜けた人の札（抜けた瞬間から LeftChipSeconds 秒）。人数が変わったら、今の様子から数え直す（前から抜けていた人は出さない）。
        private void UpdateLeftChips()
        {
            int count = _feed.CharacterCount;
            if (_left.Length != count)
            {
                _left = new bool[count];
                _leftAt = new float[count];
                for (int i = 0; i < count; i++)
                {
                    _left[i] = _feed.GetState(i).Combat.HasLeft;
                    _leftAt[i] = float.NegativeInfinity;
                }
                return;
            }
            for (int i = 0; i < count; i++)
            {
                bool left = _feed.GetState(i).Combat.HasLeft;
                if (left && !_left[i])
                {
                    _leftAt[i] = Time.time;
                    LeftChipsShown++;
                }
                _left[i] = left;
                if (left && Time.time - _leftAt[i] < LeftChipSeconds)
                {
                    Chip chip = Show("left" + i, $"P{i + 1} LEFT", false, new Color(0.7f, 0.72f, 0.78f));
                    chip.Seconds = -1;
                }
            }
        }

        /// <summary>抜けた人の札を出した回数（テスト用）。</summary>
        public int LeftChipsShown { get; private set; }

        // 札を出す（もう出ていれば、見出しだけを変える）。
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

            // タイマーの右に、上の端から積む（札の左の斜めの縁を、タイマーの台形の右の縁に沿わせる）。右の表と重なるほど画面が
            // 狭ければ、タイマーの下に積む。
            float centerX = Screen.width * 0.5f;
            ChipsBesideTheTimer = BesideX(centerX, ChipTop) + ChipWidth <= Screen.width - ScoreboardReserve;
            float top = ChipsBesideTheTimer ? ChipTop : _fallbackTop;
            if (!ChipsBesideTheTimer && _rules != null && MatchFlow.PhaseAt(_rules, _feed.Match, _feed.CurrentTick) == MatchPhase.Overtime)
            {
                // アディショナルタイムは、タイマーの下に「BANK = WIN」の行がある（MatchHud）。
                top += 34f;
            }
            float y = top;
            float blend = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            foreach (Chip chip in _chips)
            {
                chip.Y = chip.Y < 0f ? y : Mathf.Lerp(chip.Y, y, blend);
                float extend = chip.Active
                    ? Ease((Time.time - chip.AppearedAt) / ChipInSeconds)
                    : 1f - Mathf.Clamp01((Time.time - chip.LeftAt) / ChipOutSeconds);
                float x = ChipsBesideTheTimer ? BesideX(centerX, chip.Y) : (Screen.width - ChipWidth) * 0.5f;
                DrawChip(chip, new Rect(x, chip.Y, ChipWidth, ChipHeight), extend);
                if (chip.Active)
                {
                    y += ChipHeight + ChipGap;
                }
            }
        }

        // 上の端が y の札の左の端の x（札の左上の角が、タイマーの右の縁から ChipGap だけ右になる）。
        private static float BesideX(float centerX, float y)
        {
            return TimerPlate.RightEdgeAt(centerX, y) + ChipGap - ChipSlant;
        }

        // 札の左右の斜めの縁が、上から下へ左に寄る量（ピクセル。タイマーの台形の右の縁と同じ傾き）。
        private const float ChipSlant = ChipHeight * TimerPlate.EdgeSlope;

        // 札を、左の端から右へ extend（0〜1）の割合まで伸ばして描く（見えている部分だけを切り出す）。
        private void DrawChip(Chip chip, Rect rect, float extend)
        {
            if (extend <= 0f)
            {
                return;
            }
            Color previous = GUI.color;
            GUI.BeginGroup(new Rect(rect.x, rect.y, rect.width * extend, rect.height));
            var local = new Rect(0f, 0f, rect.width, rect.height);
            GUI.color = new Color(0.03f, 0.05f, 0.09f, 0.82f);
            GUI.DrawTexture(local, _chipFill);
            GUI.color = chip.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.6f, 1f), 0.65f, 1f) : chip.Color;
            GUI.DrawTexture(local, _chipFrame);
            GUI.color = previous;
            GuidePlate.DrawText(new Rect(ChipSlant + 12f, 0f, rect.width - ChipSlant - 64f, rect.height), chip.Title, _title, Color.white);
            if (chip.Seconds >= 0)
            {
                GuidePlate.DrawText(new Rect(rect.width - ChipSlant - 48f, 0f, 40f, rect.height), chip.Seconds.ToString(), _seconds, Color.white);
            }
            GUI.EndGroup();
        }

        // 札の形（平行四辺形）の中か。inset は内側へ縮める量（ピクセル）。
        private static bool InsideChip(int x, int yFromTop, float inset)
        {
            float px = x + 0.5f;
            float py = yFromTop + 0.5f;
            if (py < inset || py > ChipHeight - inset)
            {
                return false;
            }
            float left = ChipSlant * (1f - py / ChipHeight);
            return px >= left + inset * 1.2f && px <= left + ChipWidth - ChipSlant - inset * 1.2f;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private void EnsureResources()
        {
            if (_chipFill != null)
            {
                return;
            }
            _chipFill = TimerPlate.Create("NoticeChipFill", ChipWidth, ChipHeight, (x, y) => InsideChip(x, y, 0f) ? 1f : 0f);
            _chipFrame = TimerPlate.Create("NoticeChipFrame", ChipWidth, ChipHeight,
                (x, y) => InsideChip(x, y, 0f) && !InsideChip(x, y, 2.5f) ? 1f : 0f);
            // 折り返さない（狭いと 2 行になって見切れていた。ユーザーの指摘、2026-10-14）。
            _title = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip });
            _seconds = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false });
            _title.normal.textColor = Color.white;
            _seconds.normal.textColor = Color.white;
        }
    }
}
