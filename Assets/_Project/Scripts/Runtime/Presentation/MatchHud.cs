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
    /// 名前、下の帯へプレイヤーの札（斜めの札。プレイヤーの色）が滑り込み、上の帯の右で Loading のゲージがグリッチとともに増える
    /// （100% で Complete を 2 回点滅）。名前と札が去ると、集中線とともにカメラが突進し、白い光で帯が外れて、試合中の表示（タイマー・
    /// 右上の札・技の札など）がフェードインし（HudFade.Reveal）、「ON YOUR MARKS」→「GET READY……」→ 試合が始まった瞬間に「ROLL!」。
    /// 文字の下に、始まりまでのゲージ（緑 → 黄 → 赤）。2026-11-18 の要望。時間割は MatchIntroTimeline（カウントダウンの長さに合わせて縮む）。
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
        private const int SoundSet = 2;
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
        private GUIStyle _loading;
        private Texture2D _gaugeGradient;
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

        /// <summary>今のラウンドコール（「ON YOUR MARKS」「GET READY……」「ROLL!」。出していなければ空。テスト用）。</summary>
        public string RoundCall { get; private set; } = string.Empty;

        /// <summary>ラウンドコールの下の、始まりまでのゲージ（0〜1。始まった後の ROLL! では 1。テスト用）。</summary>
        public float StartGauge { get; private set; }

        /// <summary>上の帯の右の Loading の数（0〜100。出していなければ -1）と、Complete を出しているか（テスト用）。</summary>
        public int LoadingPercent => ShowingIntro && _introTime < MatchIntroTimeline.Flash ? MatchIntroTimeline.LoadingPercent(_introTime) : -1;
        public bool LoadingComplete => ShowingIntro && _introTime < MatchIntroTimeline.Flash && MatchIntroTimeline.CompleteVisible(_introTime);

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
            if (_gaugeGradient != null)
            {
                Destroy(_gaugeGradient);
            }
        }

        private void OnDisable()
        {
            // 試合中の表示を隠したまま外れない（メニューへ戻ったときなど）。
            HudFade.SetReveal(1f);
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
            StartGauge = 0f;
            _introTime = -1f;
            // 試合中の表示の出し具合（始まりの演出の間だけ 0 から上げる。ほかは 1）。
            float reveal = 1f;
            if (rules == null)
            {
                HudFade.SetReveal(reveal);
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
                    // 札が滑り込み始めた数（音は鳴らさない。札の音と、白い光の切り裂く高い音は消した。2026-11-18 の要望）。
                    // 集中線の突進の風を切る音と、ON YOUR MARKS・GET READY の短い音は鳴らす。
                    _platesStarted = Mathf.Min(_feed.CharacterCount, PlatesStartedAt(_introTime));
                    PlayOnce(SoundDive, MatchIntroTimeline.AerialEnd, SoundId.UiWhoosh);
                    PlayOnce(SoundReady, MatchIntroTimeline.ReadyStart, SoundId.CountdownTick);
                    PlayOnce(SoundSet, MatchIntroTimeline.SetStart, SoundId.CountdownTick);
                    RoundCall = MatchIntroTimeline.CallAt(_introTime);
                    StartGauge = MatchIntroTimeline.StartGauge(_introTime);
                    reveal = MatchIntroTimeline.HudReveal(_introTime);
                    if (rules.HasTimeLimit)
                    {
                        // 試合中の表示と一緒にタイマーも出す（始まるまでは試合の長さのまま）。
                        int full = Seconds(rules.DurationTicks);
                        TimerText = $"{full / 60}:{full % 60:00}";
                        TimerLabel = "TIME";
                        TimerDigits = TimerText;
                    }
                    break;
                case MatchPhase.Playing:
                    if (_lastPhase == MatchPhase.Countdown)
                    {
                        _goAt = Time.time;
                        Play(SoundId.SuperStart);
                    }
                    if (_goAt >= 0f && Time.time - _goAt < MatchIntroTimeline.GoSeconds)
                    {
                        RoundCall = MatchIntroTimeline.CallGo;
                        StartGauge = 1f;
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
            HudFade.SetReveal(reveal);
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

            // 上の帯の右: Loading のゲージ（グリッチ。100% で Complete を 2 回点滅。去るときは名前と一緒に上へ）。
            DrawLoading(t, width - height * 0.06f, bar * 0.2f - labelsOut * bar - barShift, bar, 1f - labelsOut);

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

        // ラウンドコール（ON YOUR MARKS → GET READY…… → ROLL!。2026-11-18 の要望）。画面の真ん中に大きく。
        // 2026-11-26 の要望（全体に色が暗い・縁をぼかして発光させる・ほかのゲームの戦闘開始の演出を参考に文字の出方を）: 前は白い文字を
        // ステージの色の太く濃い縁で 16 重に囲んでいて、暗い色のステージでは全体が沈んでいた。文字の芯は純白のまま、縁はステージの色を
        // 明るくした柔らかい発光（ぼかした 3 重の輪。ROLL! は橙）に。格闘ゲームの開始の文字のように、1 文字ずつ大きく右から打ち付けて入り
        // （着いた瞬間に白く光る）、入るときに横一文字の光の筋が走る。ROLL! は大きく出て縮みながら消える。長い文字は画面の幅に収める。
        // 文字の下に、始まりまでのゲージ。
        // 2026-11-26 の 2 回目の指摘（文字の中が灰色・縁をもっとぼかして鮮やかに・1 文字ずつ入る文字の上半分が出ない）: 演出の文字の
        // 色を白に（既定のラベルの色が灰色だった）、縁を 6 重のぼかした光に、拡大しても切れない枠（はみ出しても切らない）に。
        private void DrawRoundCall()
        {
            EnsureIntroStyles();
            Matrix4x4 baseMatrix = GUI.matrix;
            bool go = RoundCall == MatchIntroTimeline.CallGo;
            float realPerDesign = _rules != null && _rules.CountdownTicks > 0
                ? _rules.CountdownTicks / (MatchIntroTimeline.DesignSeconds * Airerage.Core.Common.SimTime.TicksPerSecond) : 1f;
            float callStart = RoundCall == MatchIntroTimeline.CallSet ? MatchIntroTimeline.SetStart : MatchIntroTimeline.ReadyStart;
            float since = go ? Time.time - _goAt : (_introTime - callStart) * realPerDesign;
            float alpha = go ? 1f - Mathf.Clamp01((since - MatchIntroTimeline.GoSeconds * 0.6f) / (MatchIntroTimeline.GoSeconds * 0.4f)) : 1f;
            if (alpha <= 0f)
            {
                return;
            }
            float width = _call.CalcSize(new GUIContent(RoundCall)).x;
            float fit = Mathf.Min(1f, Screen.width * 0.86f / Mathf.Max(1f, width));
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.44f);
            float pop = go ? Mathf.Lerp(1.5f, 1f, Ease(since / 0.18f)) : 1f;
            float scale = pop * fit;
            Color glow = go ? new Color(1f, 0.62f, 0.2f) : Glowing(_stageColor);
            DrawCallStreak(center, since, alpha, glow, fit);
            if (scale > 0.01f)
            {
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), center);
                DrawCallLetters(center, since, alpha, glow, go);
                GUI.matrix = baseMatrix;
            }
            DrawStartGauge(center.y + _call.fontSize * 0.55f * fit + Screen.height * 0.025f, alpha, go, glow);
        }

        /// <summary>1 文字が打ち付けて入るまでの時間（秒）と、次の文字までの間（秒）。</summary>
        public const float CallLetterSeconds = 0.16f;
        public const float CallLetterStagger = 0.028f;

        /// <summary>縁を光らせる色: ステージの色を明るく鮮やかに（暗い色のステージでも沈まないように。テスト用に公開）。
        /// 2026-11-26 の要望（縁をもっと鮮やかに）で、白へ寄せるのをやめ、彩度を上げた。</summary>
        public static Color Glowing(Color stage)
        {
            Color.RGBToHSV(stage, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp(s * 1.15f + 0.12f, 0.55f, 1f), 1f);
        }

        // ぼかした発光の縁の輪（文字の太さの基準に対する半径・不透明度）。内側ほど濃く、外ほど薄く広い（重ねるとぼかした光になる）。
        // 2026-11-26 の要望（もう少しぼかして発光させる）で、3 重から 6 重に広げた。下見は Python で同じ式から描いた。
        private static readonly (float Radius, float Alpha, int Count)[] GlowRings =
        {
            (0.45f, 0.6f, 16), (1.1f, 0.3f, 16), (2f, 0.16f, 16), (3.2f, 0.09f, 12), (4.8f, 0.05f, 12), (7f, 0.03f, 12),
        };

        // 文字の縁の発光（GlowRings の写し）を描く。芯は描かない。
        private static void DrawGlow(Rect rect, string text, GUIStyle style, Color glow, float alpha, float thick)
        {
            for (int ring = 0; ring < GlowRings.Length; ring++)
            {
                var spec = GlowRings[ring];
                float radius = thick * spec.Radius;
                GUI.color = new Color(glow.r, glow.g, glow.b, spec.Alpha * alpha);
                for (int k = 0; k < spec.Count; k++)
                {
                    float angle = (k + (ring % 2) * 0.5f) * Mathf.PI * 2f / spec.Count;
                    GUI.Label(new Rect(rect.x + Mathf.Cos(angle) * radius, rect.y + Mathf.Sin(angle) * radius, rect.width, rect.height), text, style);
                }
            }
        }

        // 文字を 1 文字ずつ: 大きく右から打ち付けて入り、着いた瞬間に白く光る。縁はぼかした光（3 重の輪）。ROLL! は 1 度に出す。
        private void DrawCallLetters(Vector2 center, float since, float alpha, Color glow, bool go)
        {
            string text = RoundCall;
            float total = _call.CalcSize(new GUIContent(text)).x;
            float x = center.x - total * 0.5f;
            // 1 文字ずつ 2.1 倍から縮んで入るので、拡大しても収まる高さの枠にする（前は 0.2 で、拡大した文字の上が切れていた。2026-11-26）。
            float height = Screen.height * 0.5f;
            float thick = Mathf.Max(3f, Screen.height * 0.0055f);
            // 全部の文字が着いて光も引いたら、1 回で描く（写しの数を文字の数で割る）。
            float settled = (text.Length - 1) * CallLetterStagger + CallLetterSeconds + 0.12f;
            if (go || since >= settled)
            {
                var whole = new Rect(center.x - total * 0.5f, center.y - height * 0.5f, total, height);
                float flashWhole = go ? Mathf.Clamp01(1f - since / 0.2f) : 0f;
                DrawGlow(whole, text, _call, glow, alpha, thick);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(whole, text, _call);
                if (flashWhole > 0f)
                {
                    DrawGlow(whole, text, _call, Color.white, flashWhole * 0.6f * alpha, thick * 0.6f);
                }
                return;
            }
            for (int i = 0; i < text.Length; i++)
            {
                string letter = text[i].ToString();
                float w = _call.CalcSize(new GUIContent(letter)).x;
                float local = go ? since : since - i * CallLetterStagger;
                float u = Mathf.Clamp01(local / CallLetterSeconds);
                if (text[i] == ' ' || u <= 0f)
                {
                    x += w;
                    continue;
                }
                float eased = Ease(u);
                float letterScale = go ? 1f : Mathf.Lerp(2.1f, 1f, eased);
                float slide = go ? 0f : (1f - eased) * Screen.height * 0.09f;
                float a = alpha * Mathf.Clamp01(u * 2.2f);
                // 着いた瞬間の白い光（0.12 秒で引く）。
                float flash = go ? Mathf.Clamp01(1f - since / 0.2f) : u >= 1f ? Mathf.Clamp01(1f - (local - CallLetterSeconds) / 0.12f) : 0f;
                var rect = new Rect(x + slide, center.y - height * 0.5f, w, height);
                Matrix4x4 saved = GUI.matrix;
                // 拡大の中心は、外側の拡大（長い文字を画面に収める fit）をかけた後の画面の位置で渡す。
                Vector3 pivot = saved.MultiplyPoint3x4(new Vector3(rect.center.x, rect.center.y, 0f));
                GUIUtility.ScaleAroundPivot(new Vector2(letterScale, letterScale), pivot);
                // ぼかした発光の縁（6 重の輪）。
                DrawGlow(rect, letter, _call, glow, a, thick);
                // 芯: 本来の白。
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Label(rect, letter, _call);
                if (flash > 0f)
                {
                    // 着いた瞬間の白い光（縁の内側を白く光らせる）。
                    DrawGlow(rect, letter, _call, Color.white, flash * 0.6f * a, thick * 0.6f);
                }
                GUI.matrix = saved;
                x += w;
            }
        }

        // 文字が入るときに走る、横一文字の光の筋（細い白い芯と、縁の色の柔らかい広がり）。
        private void DrawCallStreak(Vector2 center, float since, float alpha, Color glow, float fit)
        {
            const float Life = 0.45f;
            if (since < 0f || since > Life)
            {
                return;
            }
            float u = since / Life;
            float fade = (1f - u) * alpha;
            float reach = Screen.width * Mathf.Lerp(0.25f, 0.62f, Ease(Mathf.Clamp01(u * 2.5f)));
            float y = center.y + _call.fontSize * 0.08f * fit;
            for (int layer = 3; layer >= 0; layer--)
            {
                float h = Mathf.Max(1.5f, Screen.height * (0.002f + layer * 0.006f));
                float a = fade * (layer == 0 ? 0.9f : 0.32f / layer);
                GUI.color = layer == 0 ? new Color(1f, 1f, 1f, a) : new Color(glow.r, glow.g, glow.b, a);
                GUI.DrawTexture(new Rect(center.x - reach, y - h * 0.5f, reach * 2f, h), Texture2D.whiteTexture);
            }
        }

        // 始まりまでのゲージ（左が緑、真ん中が黄、右が赤のグラデーション。ON YOUR MARKS で空、試合の始まりで満ちる。2026-11-18 の要望）。
        // 2026-11-26 の要望（暗い・縁をぼかして発光させる）: 溝は 6 割の黒から、縁の色を少し含んだ半透明の暗い色に。縁は硬い白い線をやめ、
        // 外ほど薄い光の重ね（ぼかした発光）に。満ちている所の上下に同じ色のにじみと上の照り、先に白い光、GET READY に替わる所に小さな
        // 区切り。ROLL! で白く光る。
        private static readonly (float Grow, float Alpha)[] GaugeGlow =
        {
            (0.5f, 0.55f), (1.2f, 0.3f), (2.2f, 0.17f), (3.6f, 0.1f), (5.4f, 0.06f), (8f, 0.035f),
        };

        private void DrawStartGauge(float y, float alpha, bool go, Color glow)
        {
            float width = Mathf.Min(Screen.width * 0.5f, Screen.height * 0.85f);
            float height = Mathf.Max(10f, Screen.height * 0.014f);
            var track = new Rect(Screen.width * 0.5f - width * 0.5f, y, width, height);
            float border = Mathf.Max(2f, height * 0.18f);
            // ぼかした発光の縁（外ほど薄く広い 6 重。2026-11-26 の要望でもっとぼかした）。
            for (int ring = GaugeGlow.Length - 1; ring >= 0; ring--)
            {
                float grow = border * GaugeGlow[ring].Grow;
                GUI.color = new Color(glow.r, glow.g, glow.b, GaugeGlow[ring].Alpha * alpha);
                GUI.DrawTexture(new Rect(track.x - grow, track.y - grow, track.width + grow * 2f, track.height + grow * 2f), Texture2D.whiteTexture);
            }
            // 溝（縁の色を含んだ半透明の暗い色。暗く沈まないように、前より縁の色を多く・薄く）。
            Color groove = Color.Lerp(new Color(0.05f, 0.06f, 0.1f), glow, 0.3f);
            GUI.color = new Color(groove.r, groove.g, groove.b, 0.55f * alpha);
            GUI.DrawTexture(track, Texture2D.whiteTexture);
            float fill = Mathf.Clamp01(StartGauge);
            if (fill > 0f)
            {
                // 満ちている所のにじみ（上下に柔らかく）と、本体のグラデーションと、上の縁の照り。
                var filled = new Rect(track.x, track.y, track.width * fill, track.height);
                GUI.color = new Color(1f, 1f, 1f, 0.28f * alpha);
                GUI.DrawTextureWithTexCoords(new Rect(filled.x, filled.y - height * 0.45f, filled.width, filled.height + height * 0.9f), GaugeGradient(),
                    new Rect(0f, 0f, fill, 1f));
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTextureWithTexCoords(filled, GaugeGradient(), new Rect(0f, 0f, fill, 1f));
                GUI.color = new Color(1f, 1f, 1f, 0.35f * alpha);
                GUI.DrawTexture(new Rect(filled.x, filled.y, filled.width, Mathf.Max(1.5f, height * 0.22f)), Texture2D.whiteTexture);
            }
            float split = (MatchIntroTimeline.SetStart - MatchIntroTimeline.ReadyStart) / (MatchIntroTimeline.DesignSeconds - MatchIntroTimeline.ReadyStart);
            GUI.color = new Color(1f, 1f, 1f, 0.6f * alpha);
            GUI.DrawTexture(new Rect(track.x + track.width * split - 1f, track.y, 2f, track.height), Texture2D.whiteTexture);
            if (fill > 0f && fill < 1f)
            {
                // 先の白い光（芯と、柔らかい光の輪）。
                float tip = track.x + track.width * fill;
                for (int ring = 2; ring >= 0; ring--)
                {
                    float grow = border * (0.5f + ring * 1.2f);
                    GUI.color = new Color(1f, 1f, 1f, (ring == 0 ? 0.95f : 0.3f / ring) * alpha);
                    GUI.DrawTexture(new Rect(tip - 2f - grow, track.y - border - grow, 4f + grow * 2f, track.height + (border + grow) * 2f),
                        Texture2D.whiteTexture);
                }
            }
            if (go)
            {
                float flash = 1f - Mathf.Clamp01((Time.time - _goAt) / 0.35f);
                GUI.color = new Color(1f, 1f, 1f, flash * alpha);
                GUI.DrawTexture(new Rect(track.x - border, track.y - border, track.width + border * 2f, track.height + border * 2f), Texture2D.whiteTexture);
            }
        }

        // 始まりまでのゲージの色（緑 → 黄 → 赤）。
        private Texture2D GaugeGradient()
        {
            if (_gaugeGradient != null)
            {
                return _gaugeGradient;
            }
            _gaugeGradient = new Texture2D(256, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var green = new Color(0.2f, 0.95f, 0.35f);
            var yellow = new Color(1f, 0.88f, 0.15f);
            var red = new Color(1f, 0.2f, 0.18f);
            for (int x = 0; x < 256; x++)
            {
                float k = x / 255f;
                _gaugeGradient.SetPixel(x, 0, k < 0.5f ? Color.Lerp(green, yellow, k * 2f) : Color.Lerp(yellow, red, (k - 0.5f) * 2f));
            }
            _gaugeGradient.Apply();
            return _gaugeGradient;
        }

        // 上の帯の右の Loading（2026-11-18 の要望）: 「Loading……  37%」とゲージを右寄せで。数は少し止まっては跳び、跳んだ直後とときどき
        // グリッチ（赤と水色のずれ・横にずれる切れ目・ノイズの小さな四角）。100% で「Complete」に替わり、2 回点滅する（ゲージは満ちたまま光る）。
        private void DrawLoading(float t, float right, float top, float bar, float alpha)
        {
            float from = MatchIntroTimeline.LoadingStart - 0.15f;
            if (t < from || alpha <= 0f)
            {
                return;
            }
            alpha *= Mathf.Clamp01((t - from) / 0.15f);
            int percent = MatchIntroTimeline.LoadingPercent(t);
            bool complete = t >= MatchIntroTimeline.LoadingEnd;
            int bucket = Mathf.FloorToInt(t * 24f);
            bool glitch = MatchIntroTimeline.LoadingJumped(t) || Noise(bucket, 7) < 0.1f;
            float textHeight = bar * 0.36f;
            var textRect = new Rect(right - Screen.width * 0.5f, top, Screen.width * 0.5f, textHeight);
            string text = complete ? (MatchIntroTimeline.CompleteVisible(t) ? "Complete" : string.Empty) : $"Loading……  {percent}%";
            Color main = complete ? Color.Lerp(_stageColor, Color.white, 0.45f) : Color.white;
            if (text.Length > 0)
            {
                DrawGlitchText(textRect, text, main, alpha, glitch && !complete || (complete && t < MatchIntroTimeline.LoadingEnd + 0.08f), bucket);
            }
            float gaugeWidth = Mathf.Min(Screen.width * 0.24f, bar * 3.4f);
            float gaugeHeight = Mathf.Max(5f, bar * 0.07f);
            var track = new Rect(right - gaugeWidth, top + textHeight + bar * 0.05f, gaugeWidth, gaugeHeight);
            GUI.color = new Color(1f, 1f, 1f, 0.18f * alpha);
            GUI.DrawTexture(track, Texture2D.whiteTexture);
            float fill = percent / 100f;
            float jitter = glitch ? (Noise(bucket, 3) - 0.5f) * bar * 0.12f : 0f;
            GUI.color = new Color(_stageColor.r, _stageColor.g, _stageColor.b, alpha);
            GUI.DrawTexture(new Rect(track.x + jitter, track.y, track.width * fill, track.height), Texture2D.whiteTexture);
            if (fill > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(new Rect(track.x + track.width * fill - 3f + jitter, track.y - 2f, 3f, track.height + 4f), Texture2D.whiteTexture);
            }
            if (complete)
            {
                // 満ちたゲージが Complete と一緒に光る。
                GUI.color = new Color(1f, 1f, 1f, (MatchIntroTimeline.CompleteVisible(t) ? 0.55f : 0.1f) * alpha);
                GUI.DrawTexture(track, Texture2D.whiteTexture);
            }
            if (glitch)
            {
                // ノイズの小さな四角（文字とゲージのまわり）。
                for (int i = 0; i < 6; i++)
                {
                    float w = Mathf.Lerp(4f, bar * 0.6f, Noise(bucket, 20 + i));
                    float h = Mathf.Lerp(2f, bar * 0.05f, Noise(bucket, 40 + i));
                    float x = right - gaugeWidth * 1.15f + Noise(bucket, 60 + i) * gaugeWidth * 1.15f;
                    float y = top + Noise(bucket, 80 + i) * (track.yMax - top);
                    float pick = Noise(bucket, 100 + i);
                    Color c = pick < 0.33f ? new Color(1f, 0.15f, 0.4f) : pick < 0.66f ? new Color(0.1f, 0.95f, 1f) : Color.white;
                    GUI.color = new Color(c.r, c.g, c.b, 0.6f * alpha);
                    GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
                }
            }
        }

        // グリッチの文字: 赤と水色を左右にずらして重ね、文字を横に 3 つに切ってそれぞれ横へずらす。glitch でなければふつうに描く。
        private void DrawGlitchText(Rect rect, string text, Color color, float alpha, bool glitch, int bucket)
        {
            if (!glitch)
            {
                GUI.color = new Color(color.r, color.g, color.b, alpha);
                GUI.Label(rect, text, _loading);
                return;
            }
            float split = Mathf.Max(2f, rect.height * 0.07f);
            GUI.color = new Color(1f, 0.15f, 0.4f, 0.75f * alpha);
            GUI.Label(new Rect(rect.x + split, rect.y, rect.width, rect.height), text, _loading);
            GUI.color = new Color(0.1f, 0.95f, 1f, 0.75f * alpha);
            GUI.Label(new Rect(rect.x - split, rect.y, rect.width, rect.height), text, _loading);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            const int slices = 3;
            for (int i = 0; i < slices; i++)
            {
                float y0 = rect.height * i / slices;
                float dx = (Noise(bucket, 10 + i) - 0.5f) * rect.height * 0.7f;
                GUI.BeginGroup(new Rect(rect.x, rect.y + y0, rect.width, rect.height / slices + 1f));
                GUI.Label(new Rect(dx, -y0, rect.width, rect.height), text, _loading);
                GUI.EndGroup();
            }
        }

        // 0〜1 の決まった乱れ（表示だけ。グリッチの形）。
        private static float Noise(int a, int b)
        {
            return Mathf.Repeat(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f, 1f);
        }

        private void EnsureIntroStyles()
        {
            int h = Screen.height;
            if (_call != null && _call.fontSize == Mathf.RoundToInt(h * 0.12f))
            {
                return;
            }
            // 文字そのものの色は白にする（既定のラベルの色は灰色で、GUI.color と掛け合わさって文字の中が灰色に、縁の光もくすんでいた。
            // 2026-11-26 の指摘）。ラウンドコールは 1 文字ずつ大きく拡大して入るので、枠からはみ出しても切らない（Overflow・折り返しなし）。
            _call = Plain(new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBlack, fontSize = Mathf.RoundToInt(h * 0.12f), alignment = TextAnchor.MiddleCenter });
            _stageTitle = Plain(new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBlack, fontSize = Mathf.RoundToInt(h * 0.058f), alignment = TextAnchor.MiddleLeft });
            _stageSub = Plain(new GUIStyle(GUI.skin.label) { font = GameFonts.JapaneseBold, fontSize = Mathf.RoundToInt(h * 0.024f), alignment = TextAnchor.UpperLeft });
            _plateName = Plain(new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBlack, fontSize = Mathf.RoundToInt(h * 0.03f), alignment = TextAnchor.MiddleCenter });
            _plateSub = Plain(new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBold, fontSize = Mathf.RoundToInt(h * 0.016f), alignment = TextAnchor.MiddleCenter });
            _loading = Plain(new GUIStyle(GUI.skin.label) { font = GameFonts.LatinBold, fontSize = Mathf.RoundToInt(h * 0.026f), alignment = TextAnchor.MiddleRight });
        }

        // 演出の文字の共通の設定: 色は白（GUI.color で塗る）、枠で切らない、折り返さない。
        private static GUIStyle Plain(GUIStyle style)
        {
            style.normal.textColor = Color.white;
            style.clipping = TextClipping.Overflow;
            style.wordWrap = false;
            return style;
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
