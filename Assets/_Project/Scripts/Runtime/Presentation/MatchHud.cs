using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;
using Airerage.Runtime.Settings;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 試合の表示（docs/12_match_plan.md T4-1）: 始まる前の演出、上の中央の残り時間、終わったときの「FINISH!」と結果（順位）。
    /// 結果には「Restart」と「Quit」（IMatchControls があれば）。復帰までの残りは、出現位置の周りのゲージで示す（CharacterView）。
    /// 始まる前の演出（D-98。2026-11-17 のユーザーの決定: I1 + I2、最後に集中線とズームからラウンドコール）: ゲート（ScreenCurtain）が開いた
    /// ときには、もう上下に黒い帯（シネマスコープ）が付いている。カメラが空撮でステージを回る間（MatchIntroCamera）に、上の帯へステージの
    /// 名前、下の帯へプレイヤーの札（斜めの札。プレイヤーの色）が滑り込む。名前と札が去ると、集中線とともにカメラが突進し、白い光で帯が
    /// 外れて、「READY」→ 試合が始まった瞬間に「GO!」。時間割は MatchIntroTimeline（カウントダウンの長さに合わせて縮む）。
    /// 残り時間は、上の辺に付いた台形の札と、その下の試合の流れのバー（TimerPlate。ユーザーの選択、2026-10-14）。残り 10 秒は
    /// 赤く脈打ち、1 秒ごとに短い音。アディショナルタイムは橙色。お知らせ（拠点の移動、100 枚のコイン、ホットエリア）は NoticeHud。
    /// アディショナルタイムの始まり（OVERTIME）と試合の終わり（FINISH! / 名前 WINS!）は、お知らせと同じ画面を横切る帯（BannerStrip。
    /// ユーザーの要望、2026-10-14）。
    /// 試合の段階と残りは、ティック数と MatchRules から求める（ISimulationFeed）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class MatchHud : MonoBehaviour
    {
        // 演出の音を鳴らしたか（1 試合に 1 回ずつ）。
        private const int SoundDive = 1;
        private const int SoundFlash = 2;
        private const int SoundReady = 4;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("結果の画面のボタン（OnlineRunner。IMatchControls。なくてもよい）")]
        [SerializeField] private MonoBehaviour _controls;
        [Tooltip("演出と FINISH! の音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("「FINISH!」を出してから結果を出すまで（秒）")]
        [SerializeField] private float _finishSeconds = 2f;
        [Tooltip("残り時間を目立たせる秒数")]
        [SerializeField] private int _warningSeconds = 10;
        [Tooltip("「OVERTIME」を大きく出す秒数")]
        [SerializeField] private float _bannerSeconds = 2f;

        private ISimulationFeed _feed;
        private IMatchControls _matchControls;
        private MatchRules _rules;
        private MatchPhase? _lastPhase;
        private int _platesStarted;
        private float _goAt = -1f;
        private float _finishAt = -1f;
        private float _introTime = -1f;
        private int _introSounds;
        private readonly MatchIntroTimeline.TickClock _clock = new MatchIntroTimeline.TickClock();
        private string _stageName = string.Empty;
        private string _stageNameJa = string.Empty;
        private Color _stageColor = new Color(0.35f, 0.85f, 1f);
        private GUIStyle _call;
        private GUIStyle _stageTitle;
        private GUIStyle _stageSub;
        private GUIStyle _plateName;
        private GUIStyle _plateSub;
        private float _overtimeAt = -1f;
        private int _lastWarningSecond = -1;
        private readonly List<float> _bankMoves = new List<float>();
        private TimerPlate _timerPlate;
        private readonly HudOcclusion.Link _occlusion = new HudOcclusion.Link();
        private readonly BannerStrip _strip = new BannerStrip();
        private string _centerSub = string.Empty;
        private Color _centerColor = Color.white;
        private float _centerProgress;
        private GUIStyle _timer;
        private GUIStyle _plate;
        private GUIStyle _small;
        private PlayerStatusPlate _resultPlate;
        private GUIStyle _teamHeader;
        private GUIStyle _teamBank;

        /// <summary>画面の中央に出している文字（なければ空。テスト用）。</summary>
        public string CenterText { get; private set; } = string.Empty;

        /// <summary>上の中央の残り時間（時間の制限がなければ空）。</summary>
        public string TimerText { get; private set; } = string.Empty;

        /// <summary>タイマーの札の見出し（TIME / OVERTIME）と数字（テスト用）。</summary>
        public string TimerLabel { get; private set; } = string.Empty;
        public string TimerDigits { get; private set; } = string.Empty;

        /// <summary>残り 10 秒の、1 秒ごとの音を鳴らした数（テスト用）。</summary>
        public int CountdownTicksPlayed { get; private set; }

        /// <summary>始まる前の演出（黒い帯と名前）を出しているか。</summary>
        public bool ShowingIntro => _introTime >= 0f;

        /// <summary>突進の白い光で、帯が外れているところか。</summary>
        public bool OpeningIntro => _introTime >= MatchIntroTimeline.Flash && _introTime < MatchIntroTimeline.Flash + 0.3f;

        /// <summary>演出の基準の秒（MatchIntroTimeline。カウントダウンでなければ -1。テスト用）。</summary>
        public float IntroTime => _introTime;

        /// <summary>今のラウンドコール（「READY」「GO!」。出していなければ空。テスト用）。</summary>
        public string RoundCall { get; private set; } = string.Empty;

        /// <summary>上の帯に出すステージの名前（テスト用）。</summary>
        public string StageName => _stageName;

        /// <summary>滑り込み始めた名前の数（テスト用）。</summary>
        public int PlatesShown => _platesStarted;

        /// <summary>結果を出しているか。</summary>
        public bool ShowingResults { get; private set; }

        /// <summary>結果の順位（キャラクターの番号。出していなければ空）。</summary>
        public IReadOnlyList<int> Ranking { get; private set; } = new int[0];

        /// <summary>アディショナルタイムに、拠点に着けば勝てる人の名前（「BANK = WIN」の行。テスト用）。</summary>
        public string ChallengersText { get; private set; } = string.Empty;

        private void OnDestroy()
        {
            _resultPlate?.Dispose();
            _timerPlate?.Dispose();
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            _matchControls = _controls as IMatchControls;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
            if (_controls != null && _matchControls == null)
            {
                Debug.LogError($"{name}: 結果の画面のボタンの相手が IMatchControls ではありません（{_controls.GetType().Name}）", this);
            }
        }

        private void Update()
        {
            MatchRules rules = _feed.Rules;
            if (!ReferenceEquals(rules, _rules))
            {
                // 試合が始まった、始め直した、または抜けた（オンライン）。表示を初めからにする。
                _rules = rules;
                _lastPhase = null;
                _platesStarted = 0;
                _goAt = -1f;
                _introSounds = 0;
                FindStage();
                _finishAt = -1f;
                _overtimeAt = -1f;
                _lastWarningSecond = -1;
            }
            CenterText = string.Empty;
            _centerSub = string.Empty;
            TimerText = string.Empty;
            TimerLabel = string.Empty;
            TimerDigits = string.Empty;
            ShowingResults = false;
            Ranking = new int[0];
            ChallengersText = string.Empty;
            RoundCall = string.Empty;
            _introTime = -1f;
            if (rules == null)
            {
                return;
            }

            int tick = _feed.CurrentTick;
            MatchState match = _feed.Match;
            MatchPhase phase = MatchFlow.PhaseAt(rules, match, tick);
            int left = MatchFlow.TicksLeftAt(rules, match, tick);
            switch (phase)
            {
                case MatchPhase.Countdown:
                    _introTime = MatchIntroTimeline.DesignTime(rules, tick, _clock.Between(tick));
                    // 札が滑り込み始めるたびに音。集中線の突進・白い光・READY にも音。
                    int plates = Mathf.Min(_feed.CharacterCount, PlatesStartedAt(_introTime));
                    for (; _platesStarted < plates; _platesStarted++)
                    {
                        Play(SoundId.IntroWhoosh);
                    }
                    PlayOnce(SoundDive, MatchIntroTimeline.AerialEnd, SoundId.UiWhoosh);
                    PlayOnce(SoundFlash, MatchIntroTimeline.Flash, SoundId.IntroSlash);
                    PlayOnce(SoundReady, MatchIntroTimeline.ReadyStart, SoundId.CountdownTick);
                    if (_introTime >= MatchIntroTimeline.ReadyStart)
                    {
                        RoundCall = "READY";
                    }
                    break;
                case MatchPhase.Playing:
                    if (_lastPhase == MatchPhase.Countdown)
                    {
                        _goAt = Time.time;
                        // カウントダウンが短く白い光の前に始まったとき（テストなど）も、帯が外れる音は鳴らす。
                        if ((_introSounds & SoundFlash) == 0)
                        {
                            _introSounds |= SoundFlash;
                            Play(SoundId.IntroSlash);
                        }
                        Play(SoundId.SuperStart);
                    }
                    if (_goAt >= 0f && Time.time - _goAt < MatchIntroTimeline.GoSeconds)
                    {
                        RoundCall = "GO!";
                    }
                    if (rules.HasTimeLimit)
                    {
                        int seconds = Seconds(left);
                        TimerText = $"{seconds / 60}:{seconds % 60:00}";
                        TimerLabel = "TIME";
                        TimerDigits = TimerText;
                        // 残り 10 秒は、1 秒ごとに短い音。
                        if (seconds <= _warningSeconds && seconds > 0 && seconds != _lastWarningSecond)
                        {
                            _lastWarningSecond = seconds;
                            CountdownTicksPlayed++;
                            Play(SoundId.CountdownTick);
                        }
                    }
                    break;
                case MatchPhase.Overtime:
                    if (_lastPhase != MatchPhase.Overtime)
                    {
                        _overtimeAt = Time.time;
                        Play(SoundId.Overtime);
                    }
                    TimerText = $"OVERTIME {Seconds(left)}";
                    TimerLabel = "OVERTIME";
                    TimerDigits = Seconds(left).ToString();
                    if (Time.time - _overtimeAt < _bannerSeconds)
                    {
                        CenterText = "OVERTIME";
                        _centerSub = Loc.T("REACH THE BANK TO WIN", "拠点にたどり着いた人の勝ち");
                        _centerColor = new Color(1f, 0.6f, 0.15f);
                        _centerProgress = (Time.time - _overtimeAt) / _bannerSeconds;
                    }
                    ChallengersText = Challengers();
                    break;
                case MatchPhase.Finished:
                    if (_lastPhase != MatchPhase.Finished)
                    {
                        _finishAt = Time.time;
                        Play(SoundId.Finish);
                    }
                    TimerText = "0:00";
                    TimerLabel = "TIME";
                    TimerDigits = TimerText;
                    if (Time.time - _finishAt < _finishSeconds)
                    {
                        // チーム対戦は勝ったチームの名前（RED WINS! など。T9-6）。
                        int winnerTeam = match.Winner >= 0 ? _feed.GetState(match.Winner).Combat.Team : 0;
                        CenterText = match.Winner < 0 ? "FINISH!"
                            : winnerTeam != 0 ? $"{TeamColors.NameOf(winnerTeam)} WINS!" : $"{_feed.LabelOf(match.Winner)} WINS!";
                        _centerSub = match.Winner >= 0 ? Loc.T("REACHED THE BANK IN OVERTIME", "アディショナルタイムに拠点へ到達") : "TIME UP";
                        _centerColor = match.Winner >= 0 ? _feed.ColorOf(match.Winner) : new Color(1f, 0.85f, 0.3f);
                        _centerProgress = (Time.time - _finishAt) / _finishSeconds;
                    }
                    else
                    {
                        ShowingResults = true;
                        Ranking = MatchResults.Rank(States(), match.Winner);
                    }
                    break;
            }
            _lastPhase = phase;
        }

        // アディショナルタイムに、拠点に着けば勝てる人（確定＋所持が、ほかの人の確定の最大以上）の名前。
        private string Challengers()
        {
            CharacterState[] states = States();
            var names = new List<string>();
            for (int i = 0; i < states.Length; i++)
            {
                if (MatchFlow.CanWinAtTheBank(states, i))
                {
                    names.Add(_feed.LabelOf(i));
                }
            }
            return string.Join("  ", names);
        }

        // 基準の秒 t までに、滑り込み始めた札の数。
        private static int PlatesStartedAt(float t)
        {
            if (t < MatchIntroTimeline.PlatesStart)
            {
                return 0;
            }
            return 1 + Mathf.FloorToInt((t - MatchIntroTimeline.PlatesStart) / MatchIntroTimeline.PlateInterval);
        }

        // 演出の区切り at を過ぎたら、1 試合に 1 回だけ音を鳴らす。
        private void PlayOnce(int flag, float at, SoundId id)
        {
            if (_introTime >= at && (_introSounds & flag) == 0)
            {
                _introSounds |= flag;
                Play(id);
            }
        }

        // 上の帯に出すステージの名前と色（ゲームの流れの行き先。検証用のシーンはシーンの名前）。
        private void FindStage()
        {
            var flow = FindFirstObjectByType<Airerage.Runtime.Flow.GameFlow>();
            // CurrentStage は読み込みが済んでから入るので、試合のシーンが動き出したときには行き先（TargetStage）を見る（GameAudio と同じ）。
            Airerage.Runtime.Flow.StageDefinition stage = flow != null ? (flow.TargetStage != null ? flow.TargetStage : flow.CurrentStage) : null;
            if (stage != null)
            {
                _stageName = stage.DisplayName;
                _stageNameJa = stage.NameJa;
                _stageColor = stage.Color;
                return;
            }
            _stageName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Replace("Stage_", string.Empty).ToUpperInvariant();
            _stageNameJa = string.Empty;
        }

        private CharacterState[] States()
        {
            var states = new CharacterState[_feed.CharacterCount];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = _feed.GetState(i);
            }
            return states;
        }

        private void Play(SoundId id)
        {
            if (_audio != null)
            {
                _audio.Play(id);
            }
        }

        // 残りのティックを、切り上げた秒にする（残り時間の表示）。
        private static int Seconds(int ticks)
        {
            return (ticks + SimTime.TicksPerSecond - 1) / SimTime.TicksPerSecond;
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private void OnGUI()
        {
            if (_timer == null)
            {
                _timer = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
                _plate = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft });
                _small = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            }
            Color previous = GUI.color;
            Matrix4x4 matrix = GUI.matrix;
            if (ShowingIntro)
            {
                DrawIntro(_introTime);
            }
            if (RoundCall.Length > 0)
            {
                DrawRoundCall();
            }
            GUI.matrix = matrix;
            if (TimerText.Length > 0 && !ShowingResults)
            {
                DrawTimer();
            }
            if (CenterText.Length > 0)
            {
                _strip.Draw(CenterText, _centerSub, _centerColor, false, _centerProgress, Screen.height * 0.42f);
            }
            if (ShowingResults)
            {
                DrawResults();
            }
            GUI.color = previous;
        }

        // 上の中央の台形の札と、試合の流れのバー（TimerPlate）。その下に、アディショナルタイムに拠点に着けば勝てる人。
        private void DrawTimer()
        {
            if (_timerPlate == null)
            {
                _timerPlate = new TimerPlate();
            }
            MatchState match = _feed.Match;
            int tick = _feed.CurrentTick;
            MatchPhase phase = MatchFlow.PhaseAt(_rules, match, tick);
            int left = MatchFlow.TicksLeftAt(_rules, match, tick);
            bool warning = phase == MatchPhase.Playing && left <= _warningSeconds * SimTime.TicksPerSecond;
            Color color = phase == MatchPhase.Overtime ? new Color(1f, 0.6f, 0.15f) : warning ? new Color(1f, 0.35f, 0.3f) : Color.white;
            float scale = warning ? 1f + 0.08f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI)) : 1f;
            float remaining = phase == MatchPhase.Playing && _rules.DurationTicks > 0 ? (float)left / _rules.DurationTicks : phase == MatchPhase.Overtime ? 0f : -1f;
            TimerPlate.Timeline timeline = TimerPlate.TimelineOf(_rules, tick, _bankMoves);
            // キャラクターと重なったら、タイマーと流れのバーを透かす（HudOcclusion）。
            float wide = Mathf.Max(TimerPlate.Width, TimerPlate.TimelineWidth);
            var area = new Rect(Screen.width * 0.5f - wide * 0.5f, 0f, wide, TimerPlate.Height + 22f);
            HudFade.Begin(_occlusion.AlphaFor(this, 0, area));
            float bottom = _timerPlate.Draw(Screen.width * 0.5f, TimerLabel, TimerDigits, color, remaining, scale, timeline);
            HudFade.End();
            if (ChallengersText.Length > 0)
            {
                Shadowed(new Rect(Screen.width * 0.5f - 300f, bottom + 4f, 600f, 30f), $"BANK = WIN:  {ChallengersText}", _small, new Color(1f, 0.85f, 0.4f));
            }
        }

        // 始まる前の演出（基準の秒 t。MatchIntroTimeline）: シネマスコープの帯、上の帯のステージの名前、下の帯のプレイヤーの札、集中線、
        // 突進の終わりの白い光。帯はゲートが開いたときから付いていて、白い光で上下へ外れる。
        private void DrawIntro(float t)
        {
            EnsureIntroStyles();
            Matrix4x4 baseMatrix = GUI.matrix;
            float width = Screen.width;
            float height = Screen.height;
            float bar = height * 0.13f;
            float off = Ease((t - MatchIntroTimeline.Flash) / 0.28f);
            float labelsOut = Ease((t - MatchIntroTimeline.LabelsOut) / 0.3f);

            // 集中線（突進の間。画面の外側から真ん中へ向かう光の線。だんだん濃く）。
            float dive = Mathf.Clamp01((t - MatchIntroTimeline.AerialEnd) / (MatchIntroTimeline.Flash - MatchIntroTimeline.AerialEnd));
            if (t >= MatchIntroTimeline.AerialEnd && t < MatchIntroTimeline.Flash + 0.15f)
            {
                DrawSpeedLines(dive, t, baseMatrix);
            }

            // 上下の黒い帯。
            float barShift = off * bar * 1.4f;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, -barShift, width, bar), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, height - bar + barShift, width, bar), Texture2D.whiteTexture);
            // 帯の内側の縁にステージの色の細い線。
            Color edge = _stageColor;
            edge.a = 1f - off;
            GUI.color = edge;
            GUI.DrawTexture(new Rect(0f, bar - 3f - barShift, width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, height - bar + barShift, width, 3f), Texture2D.whiteTexture);

            // 上の帯: ステージの名前（左から滑り込み、去るときは上へ）。
            float nameIn = Ease((t - MatchIntroTimeline.NameIn) / 0.45f);
            if (nameIn > 0f && labelsOut < 1f)
            {
                float x = Mathf.Lerp(-width * 0.5f, height * 0.06f, nameIn);
                float y = bar * 0.12f - labelsOut * bar - barShift;
                float alpha = 1f - labelsOut;
                // ステージの色の菱形の印。
                GUIUtility.RotateAroundPivot(45f, new Vector2(x + bar * 0.12f, y + bar * 0.34f));
                GUI.color = new Color(_stageColor.r, _stageColor.g, _stageColor.b, alpha);
                GUI.DrawTexture(new Rect(x + bar * 0.04f, y + bar * 0.26f, bar * 0.16f, bar * 0.16f), Texture2D.whiteTexture);
                GUI.matrix = baseMatrix;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(x + bar * 0.32f, y, width, bar * 0.6f), _stageName, _stageTitle);
                if (_stageNameJa.Length > 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.8f * alpha);
                    GUI.Label(new Rect(x + bar * 0.34f, y + bar * 0.56f, width, bar * 0.3f), _stageNameJa, _stageSub);
                }
            }

            // 下の帯: プレイヤーの札（右から順に滑り込み、去るときは下へ）。
            int count = _feed.CharacterCount;
            if (count > 0 && labelsOut < 1f)
            {
                float gap = 14f;
                float plateHeight = bar * 0.62f;
                float plateWidth = Mathf.Min(340f, (width - 80f - gap * (count - 1)) / count);
                float total = plateWidth * count + gap * (count - 1);
                float left = (width - total) * 0.5f;
                float y = height - bar + (bar - plateHeight) * 0.5f + labelsOut * bar + barShift;
                for (int i = 0; i < count; i++)
                {
                    float k = Ease((t - (MatchIntroTimeline.PlatesStart + i * MatchIntroTimeline.PlateInterval)) / MatchIntroTimeline.PlateSlide);
                    if (k <= 0f)
                    {
                        continue;
                    }
                    float target = left + i * (plateWidth + gap);
                    float x = Mathf.Lerp(width + 40f + i * 60f, target, k);
                    CharacterConfigAsset character = _feed.CharacterOf(i);
                    DrawPlate(new Rect(x, y, plateWidth, plateHeight), _feed.ColorOf(i), _feed.LabelOf(i),
                        character != null ? character.DisplayName : string.Empty, 1f - labelsOut, baseMatrix);
                }
            }

            // 突進の終わりの白い光（帯が外れるのを隠す）。
            if (t >= MatchIntroTimeline.Flash)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.85f * (1f - Ease((t - MatchIntroTimeline.Flash) / 0.4f)));
                GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            }
            GUI.matrix = baseMatrix;
        }

        // 斜めの札（裏の札つき。メニューの札と同じ形）: プレイヤーの色の面に、名前とキャラクターの名前。
        private void DrawPlate(Rect rect, Color color, string label, string sub, float alpha, Matrix4x4 baseMatrix)
        {
            // 横へずらす斜め（下ほど左へ）。札の真ん中を中心に。
            Matrix4x4 shear = Matrix4x4.identity;
            shear.m01 = -0.32f;
            Vector3 center = rect.center;
            GUI.matrix = baseMatrix * Matrix4x4.Translate(center) * shear * Matrix4x4.Translate(-center);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(rect.x + 6f, rect.y + 5f, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.9f * alpha);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), Texture2D.whiteTexture);
            GUI.matrix = baseMatrix;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.04f, rect.width, rect.height * 0.62f), label, _plateName);
            if (sub.Length > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.85f * alpha);
                GUI.Label(new Rect(rect.x, rect.y + rect.height * 0.6f, rect.width, rect.height * 0.36f), sub, _plateSub);
            }
        }

        // 集中線: 画面の外側から真ん中へ向かう細い光の線（タイトル → メインメニューのスカイダイブの速さの線と同じ考え方）。
        private void DrawSpeedLines(float strength, float t, Matrix4x4 baseMatrix)
        {
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float reach = new Vector2(Screen.width, Screen.height).magnitude * 0.55f;
            const int lines = 56;
            for (int i = 0; i < lines; i++)
            {
                // 線ごとの決まった乱れ（毎フレーム同じ並びで、外へ流れる）。
                float seed = Mathf.Repeat(Mathf.Sin(i * 12.9898f) * 43758.5453f, 1f);
                float angle = i * 360f / lines + seed * 5f;
                float flow = Mathf.Repeat(t * 3.2f + seed, 1f);
                float inner = reach * Mathf.Lerp(0.45f, 0.2f, strength) + flow * reach * 0.25f;
                float length = reach * (0.35f + seed * 0.4f) * (0.5f + strength * 0.5f);
                float thickness = 1.5f + seed * 3f * strength;
                GUIUtility.RotateAroundPivot(angle, center);
                GUI.color = new Color(1f, 1f, 1f, (0.25f + seed * 0.5f) * strength * (1f - flow * 0.5f));
                GUI.DrawTexture(new Rect(center.x + inner, center.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
                GUI.matrix = baseMatrix;
            }
        }

        // ラウンドコール（READY → GO!）。画面の真ん中に大きく、ステージの色の縁取り。READY は弾んで出て、GO! は大きく出て縮みながら消える。
        private void DrawRoundCall()
        {
            EnsureIntroStyles();
            Matrix4x4 baseMatrix = GUI.matrix;
            bool go = RoundCall == "GO!";
            float age = go ? Time.time - _goAt : (_introTime - MatchIntroTimeline.ReadyStart) * (_rules != null && _rules.CountdownTicks > 0
                ? _rules.CountdownTicks / (MatchIntroTimeline.DesignSeconds * Airerage.Core.Common.SimTime.TicksPerSecond) : 1f);
            float scale = go ? Mathf.Lerp(1.5f, 1f, Ease(age / 0.18f)) : UiEase.OutBack(Mathf.Clamp01(age / 0.25f));
            float alpha = go ? 1f - Mathf.Clamp01((age - MatchIntroTimeline.GoSeconds * 0.6f) / (MatchIntroTimeline.GoSeconds * 0.4f)) : 1f;
            if (scale <= 0.01f || alpha <= 0f)
            {
                return;
            }
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.46f);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), center);
            var rect = new Rect(center.x - Screen.width * 0.5f, center.y - Screen.height * 0.12f, Screen.width, Screen.height * 0.24f);
            Color outline = go ? new Color(1f, 0.55f, 0.15f, alpha) : new Color(_stageColor.r, _stageColor.g, _stageColor.b, alpha);
            float thick = Mathf.Max(3f, Screen.height * 0.006f);
            GUI.color = outline;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                GUI.Label(new Rect(rect.x + Mathf.Cos(a) * thick, rect.y + Mathf.Sin(a) * thick, rect.width, rect.height), RoundCall, _call);
            }
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, RoundCall, _call);
            GUI.matrix = baseMatrix;
        }

        private void EnsureIntroStyles()
        {
            int h = Screen.height;
            if (_call != null && _call.fontSize == Mathf.RoundToInt(h * 0.16f))
            {
                return;
            }
            _call = new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBlack, fontSize = Mathf.RoundToInt(h * 0.16f), alignment = TextAnchor.MiddleCenter };
            _stageTitle = new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBlack, fontSize = Mathf.RoundToInt(h * 0.058f), alignment = TextAnchor.MiddleLeft };
            _stageSub = new GUIStyle(GUI.skin.label) { font = GameFonts.JapaneseBold, fontSize = Mathf.RoundToInt(h * 0.024f), alignment = TextAnchor.UpperLeft };
            _plateName = new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBlack, fontSize = Mathf.RoundToInt(h * 0.03f), alignment = TextAnchor.MiddleCenter };
            _plateSub = new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBold, fontSize = Mathf.RoundToInt(h * 0.016f), alignment = TextAnchor.MiddleCenter };
        }

        // 結果: 右上の表と同じ札（PlayerStatusPlate）を、大きくして順位の順に並べる。順位は札の順位の四角（1 位 金・2 位 銀・3 位 銅）、
        // 首位に王冠。数は短くせずに正確に書く。
        private void DrawResults()
        {
            const float rowGap = 10f;
            if (_resultPlate == null)
            {
                _resultPlate = new PlayerStatusPlate(1.3f);
            }
            int plateWidth = _resultPlate.Width;
            int plateHeight = _resultPlate.Height;
            float width = 640f;
            bool buttons = _matchControls != null;
            CharacterState[] states = States();
            bool teams = TeamRules.IsTeamMatch(states);
            // チーム対戦は、チームごとに見出し（順位・チームの名前・チームの BANK の合計）を出し、その下にチームの人の札（T9-6）。
            int headers = teams ? CountTeams(states) : 0;
            const float headerHeight = 34f;
            float rows = Ranking.Count * (plateHeight + rowGap) + headers * (headerHeight + rowGap);
            float height = 82f + rows + (buttons ? 64f : 0f);
            var area = new Rect((Screen.width - width) * 0.5f, Mathf.Max(4f, (Screen.height - height) * 0.5f), width, height);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            Shadowed(new Rect(area.x, area.y + 8f, width, 44f), "RESULT", _timer, new Color(1f, 0.85f, 0.3f));
            float plateX = area.x + (width - plateWidth) * 0.5f + 30f;
            float y = area.y + 60f;
            int lastTeam = -1;
            int teamRank = 0;
            for (int i = 0; i < Ranking.Count; i++)
            {
                int index = Ranking[i];
                int team = states[index].Combat.Team;
                if (teams && team != lastTeam)
                {
                    teamRank++;
                    DrawTeamResultHeader(new Rect(plateX - 70f, y, plateWidth + 70f, headerHeight), teamRank, team, TeamRules.ScoreOf(states, index));
                    y += headerHeight + rowGap;
                }
                lastTeam = team;
                bool crown = ScoreboardHud.IsLeader(_feed, index);
                // 個人戦は並びの順（アディショナルタイムで決まった勝者が 1 位）、チーム対戦はチームの順位。
                int rank = teams ? teamRank : i + 1;
                bool scored = TeamRules.ScoreOf(states, index) > 0;
                _resultPlate.Draw(new Rect(plateX, y, plateWidth, plateHeight), rank, scored, _feed.LabelOf(index), _feed.ColorOf(index),
                    states[index], crown, default, new Color(1f, 0.85f, 0.2f), ScoreboardHud.LogoOf(_feed, index), abbreviate: false);
                y += plateHeight + rowGap;
            }
            // 順位の決め方（確定コインの数。D-25）の説明の行は出さない（ユーザーの要望、2026-10-30）。
            if (!buttons)
            {
                return;
            }
            GUI.color = Color.white;
            var restart = new Rect(area.x + 40f, area.yMax - 56f, 220f, 40f);
            var quit = new Rect(area.xMax - 260f, area.yMax - 56f, 220f, 40f);
            GUI.enabled = _matchControls.CanRestart;
            if (GUI.Button(restart, _matchControls.CanRestart ? "Restart" : Loc.T("Waiting for the host", "ホストを待っています")))
            {
                UiSounds.Find().Play(SoundId.UiConfirm);
                _matchControls.Restart();
            }
            GUI.enabled = true;
            if (GUI.Button(quit, "Quit"))
            {
                UiSounds.Find().Play(SoundId.UiConfirm);
                _matchControls.Quit();
            }
        }

        // チーム対戦の結果のチームの見出し: チームの色の帯に、順位・チームの名前・チームの BANK の合計。
        private void DrawTeamResultHeader(Rect rect, int rank, int team, int bank)
        {
            if (_teamHeader == null)
            {
                _teamHeader = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft });
                _teamBank = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight });
            }
            Color color = TeamColors.Of(team);
            GUI.color = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, 0.9f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 8f, rect.height), Texture2D.whiteTexture);
            Color rankColor = rank == 1 ? new Color(1f, 0.85f, 0.3f) : Color.white;
            Shadowed(new Rect(rect.x + 18f, rect.y, rect.width, rect.height), $"{rank}  {TeamColors.NameOf(team)}", _teamHeader, rankColor);
            Shadowed(new Rect(rect.x, rect.y, rect.width - 14f, rect.height), $"BANK {bank}", _teamBank, new Color(1f, 0.85f, 0.2f));
            GUI.color = Color.white;
        }

        // 試合にいるチームの数（チームなしは数えない）。
        private static int CountTeams(CharacterState[] states)
        {
            int mask = 0;
            foreach (CharacterState state in states)
            {
                if (state.Combat.Team > 0 && state.Combat.Team < 31)
                {
                    mask |= 1 << state.Combat.Team;
                }
            }
            int count = 0;
            for (; mask != 0; mask &= mask - 1)
            {
                count++;
            }
            return count;
        }

        private static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 3f, rect.y + 3f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }
    }
}
