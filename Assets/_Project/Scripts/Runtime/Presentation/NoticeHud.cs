using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 試合中のお知らせ（ユーザーの選択、2026-10-14。案 C ＋ 大きな出来事だけ案 B）。
    /// - タイマーの下に積む札（GuidePlate。左に絵、右に見出しと小さな説明、右の端に出来事までの秒）:
    ///   拠点の移動の予告「BANK MOVES」（水色。移るまでの秒）、100 枚のコインの予告「JACKPOT」（虹色。出るまでの秒）と、
    ///   出た直後の「JACKPOT 100」（JackpotChipTicks の間）。札は上から落ちるように出て、終わるとつぶれて消える。重なれば縦に積む。
    /// - 画面を横切る帯（斜めの黒い帯が開き、大きな文字が右から走り抜けて閉じる）: 100 枚のコインの予告の始まり
    ///   「JACKPOT INCOMING」（虹色）と、COIN RUSH の始まり（金色。短いファンファーレ）。
    /// 出来事の時刻は、ティック数とルールだけから決まる（BankRules、FieldCoinRules、MatchRules）。巻き戻しでもずれない。
    /// 拠点の予告の呼び鈴は BankView、100 枚のコインの警報は MatchAtmosphere が鳴らす。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class NoticeHud : MonoBehaviour
    {
        /// <summary>100 枚のコインが出た後に「JACKPOT 100」の札を残すティック（2 秒）。</summary>
        public const int JackpotChipTicks = 120;

        private const int ChipWidth = 300;
        private const int ChipHeight = 46;
        private const float ChipGap = 6f;
        private const float ChipInSeconds = 0.3f;
        private const float ChipOutSeconds = 0.3f;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("COIN RUSH のファンファーレ（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("札を積み始める高さ（ピクセル。タイマーの札と流れのバーの下）")]
        [SerializeField] private float _top = 106f;
        [Tooltip("帯を出す秒数")]
        [SerializeField] private float _bandSeconds = 2.6f;
        [SerializeField] private Color _bankColor = new Color(0.31f, 0.84f, 1f);
        [SerializeField] private Color _rushColor = new Color(1f, 0.8f, 0.2f);

        private readonly List<Chip> _chips = new List<Chip>();
        private readonly List<string> _activeKeys = new List<string>();
        private ISimulationFeed _feed;
        private MatchRules _rules;
        private int _lastTick = -1;
        private GuidePlate _plate;
        private Texture2D _bankIcon;
        private Texture2D _jackpotIcon;
        private GUIStyle _title;
        private GUIStyle _sub;
        private GUIStyle _seconds;
        private GUIStyle _bandTitle;
        private GUIStyle _bandSub;
        private Band _band;

        private sealed class Chip
        {
            public string Key;
            public string Title;
            public string Sub;
            public int Seconds = -1;
            public bool Rainbow;
            public Color Color;
            public bool Jackpot;
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
            _plate?.Dispose();
            if (_bankIcon != null)
            {
                Destroy(_bankIcon);
                Destroy(_jackpotIcon);
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
                Chip chip = Show("bank", "BANK MOVES", "NEW SPOT", false, _bankColor);
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
                Chip chip = Show("jackpot", "JACKPOT", $"{CoinView.RainbowValue} COINS", true, Color.white);
                chip.Seconds = Seconds(spawn + 1 - tick);
            }
            else
            {
                Chip chip = Show("jackpot", $"JACKPOT {CoinView.RainbowValue}", "APPEARED", true, Color.white);
                chip.Seconds = -1;
            }
        }

        // 札を出す（もう出ていれば、見出しと説明だけを変える）。
        private Chip Show(string key, string title, string sub, bool jackpot, Color color)
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
            chip.Sub = sub;
            chip.Jackpot = jackpot;
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
                DrawBand(_band, (Time.time - _band.StartedAt) / _bandSeconds);
            }
            float top = _top;
            if (_rules != null && MatchFlow.PhaseAt(_rules, _feed.Match, _feed.CurrentTick) == MatchPhase.Overtime)
            {
                // アディショナルタイムは、タイマーの下に「BANK = WIN」の行がある（MatchHud）。
                top += 34f;
            }
            float y = top;
            float blend = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            foreach (Chip chip in _chips)
            {
                chip.Y = chip.Y < 0f ? y : Mathf.Lerp(chip.Y, y, blend);
                float squash = chip.Active
                    ? Ease((Time.time - chip.AppearedAt) / ChipInSeconds)
                    : 1f - Mathf.Clamp01((Time.time - chip.LeftAt) / ChipOutSeconds);
                DrawChip(chip, chip.Y, squash);
                if (chip.Active)
                {
                    y += ChipHeight + ChipGap;
                }
            }
        }

        private void DrawChip(Chip chip, float y, float appear)
        {
            if (appear <= 0f)
            {
                return;
            }
            Matrix4x4 matrix = GUI.matrix;
            var rect = new Rect((Screen.width - ChipWidth) * 0.5f, y - (1f - appear) * ChipHeight * 0.6f, ChipWidth, ChipHeight);
            GUIUtility.ScaleAroundPivot(new Vector2(1f, Mathf.Max(0.05f, appear)), new Vector2(rect.center.x, rect.y));
            Texture2D icon = chip.Jackpot ? _jackpotIcon : _bankIcon;
            if (chip.Rainbow)
            {
                _plate.DrawRainbow(rect, Time.time, icon, null, _title, appear);
            }
            else
            {
                _plate.Draw(rect, chip.Color, icon, null, _title, appear);
            }
            Rect right = _plate.RightRect(rect);
            var text = new Rect(right.x + 6f, right.y + 4f, right.width - 56f, right.height * 0.55f);
            GuidePlate.DrawText(text, chip.Title, _title, new Color(1f, 1f, 1f, appear));
            GuidePlate.DrawText(new Rect(text.x, text.yMax - 4f, text.width, right.height * 0.4f), chip.Sub, _sub, new Color(1f, 1f, 1f, 0.7f * appear));
            if (chip.Seconds >= 0)
            {
                GuidePlate.DrawText(new Rect(right.xMax - 50f, right.y, 44f, right.height), chip.Seconds.ToString(), _seconds, new Color(1f, 1f, 1f, appear));
            }
            GUI.matrix = matrix;
        }

        // 画面を横切る斜めの帯。progress は 0〜1（開く → 文字が走り込む → 止まる → 走り去って閉じる）。
        private void DrawBand(Band band, float progress)
        {
            float open = progress < 0.1f ? Ease(progress / 0.1f) : progress > 0.85f ? 1f - Ease((progress - 0.85f) / 0.15f) : 1f;
            if (open <= 0f)
            {
                return;
            }
            float slide = progress < 0.2f ? 1f - Ease(progress / 0.2f) : progress > 0.75f ? -Ease((progress - 0.75f) / 0.17f) : 0f;
            float height = 118f * open;
            float centerY = Screen.height * 0.3f;
            Matrix4x4 matrix = GUI.matrix;
            Color previous = GUI.color;
            GUIUtility.RotateAroundPivot(-3f, new Vector2(Screen.width * 0.5f, centerY));
            var bar = new Rect(-Screen.width * 0.1f, centerY - height * 0.5f, Screen.width * 1.2f, height);
            GUI.color = new Color(0.02f, 0.03f, 0.07f, 0.82f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            Color edge = band.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f, 1f), 0.7f, 1f) : band.Color;
            GUI.color = edge;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width, 5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bar.x, bar.yMax - 5f, bar.width, 5f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.matrix = matrix;

            float alpha = 1f - Mathf.Abs(slide);
            float offset = slide * Screen.width * 0.6f;
            Color titleColor = band.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f + 0.3f, 1f), 0.55f, 1f) : band.Color;
            titleColor.a = alpha;
            GuidePlate.DrawText(new Rect(offset, centerY - 50f, Screen.width, 76f), band.Title, _bandTitle, titleColor);
            GuidePlate.DrawText(new Rect(offset, centerY + 22f, Screen.width, 28f), band.Sub, _bandSub, new Color(1f, 1f, 1f, alpha));
            GUI.color = previous;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private void EnsureResources()
        {
            if (_plate != null)
            {
                return;
            }
            _plate = new GuidePlate(ChipWidth, ChipHeight);
            _bankIcon = GuideIcons.Bank(64, _bankColor);
            _jackpotIcon = GuideIcons.RainbowCoin(64);
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            _sub = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            _seconds = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _bandTitle = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter };
            _bandSub = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            foreach (GUIStyle style in new[] { _title, _sub, _seconds, _bandTitle, _bandSub })
            {
                style.normal.textColor = Color.white;
            }
        }
    }
}
