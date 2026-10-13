using System.Collections.Generic;
using Degunatsu.Core.Common;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 試合の表示（docs/12_match_plan.md T4-1）: 始まる前の演出、上の中央の残り時間、終わったときの「FINISH!」と結果（順位）。
    /// 結果には「Restart」と「Quit」（IMatchControls があれば）。復帰までの残りは、出現位置の周りのゲージで示す（CharacterView）。
    /// 始まる前の演出は「シネマスコープ」（D-46 の C 案。ユーザーの選択、2026-10-13）: 画面の上下に黒い帯が入り、全員の名前が色つきで
    /// 左右から滑り込む。始まると、帯が斜めに切り裂かれるように開く。
    /// 試合の段階と残りは、ティック数と MatchRules から求める（ISimulationFeed）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class MatchHud : MonoBehaviour
    {
        // カウントダウンの中の、演出の区切り（カウントダウン全体に対する割合）。
        private const float BarsInEnd = 0.15f;
        private const float PlatesStart = 0.15f;
        private const float PlateInterval = 0.13f;
        private const float PlateDuration = 0.14f;
        private const float SlashStart = 0.82f;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("結果の画面のボタン（OnlineRunner。IMatchControls。なくてもよい）")]
        [SerializeField] private MonoBehaviour _controls;
        [Tooltip("演出と FINISH! の音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("始まったときに、帯が開く時間（秒）")]
        [SerializeField] private float _openSeconds = 0.5f;
        [Tooltip("「FINISH!」を出してから結果を出すまで（秒）")]
        [SerializeField] private float _finishSeconds = 1.5f;
        [Tooltip("残り時間を目立たせる秒数")]
        [SerializeField] private int _warningSeconds = 10;

        private ISimulationFeed _feed;
        private IMatchControls _matchControls;
        private MatchRules _rules;
        private MatchPhase? _lastPhase;
        private int _platesStarted;
        private float _openAt = -1f;
        private float _finishAt = -1f;
        private float _introProgress = -1f;
        private GUIStyle _center;
        private GUIStyle _timer;
        private GUIStyle _row;
        private GUIStyle _plate;

        /// <summary>画面の中央に出している文字（なければ空。テスト用）。</summary>
        public string CenterText { get; private set; } = string.Empty;

        /// <summary>上の中央の残り時間（時間の制限がなければ空）。</summary>
        public string TimerText { get; private set; } = string.Empty;

        /// <summary>始まる前の演出（黒い帯と名前）を出しているか。</summary>
        public bool ShowingIntro => _introProgress >= 0f;

        /// <summary>始まった直後に、帯が開いているところか。</summary>
        public bool OpeningIntro => _openAt >= 0f && Time.time - _openAt < _openSeconds;

        /// <summary>滑り込み始めた名前の数（テスト用）。</summary>
        public int PlatesShown => _platesStarted;

        /// <summary>結果を出しているか。</summary>
        public bool ShowingResults { get; private set; }

        /// <summary>結果の順位（キャラクターの番号。出していなければ空）。</summary>
        public IReadOnlyList<int> Ranking { get; private set; } = new int[0];

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
                _openAt = -1f;
                _finishAt = -1f;
            }
            CenterText = string.Empty;
            TimerText = string.Empty;
            ShowingResults = false;
            Ranking = new int[0];
            _introProgress = -1f;
            if (rules == null)
            {
                return;
            }

            int tick = _feed.CurrentTick;
            MatchPhase phase = rules.PhaseAt(tick);
            int left = rules.TicksLeftAt(tick);
            switch (phase)
            {
                case MatchPhase.Countdown:
                    _introProgress = rules.CountdownTicks > 0 ? 1f - (float)left / rules.CountdownTicks : 1f;
                    // 名前が滑り込み始めるたびに音を鳴らす。
                    int plates = Mathf.Min(_feed.CharacterCount, PlatesStartedAt(_introProgress));
                    for (; _platesStarted < plates; _platesStarted++)
                    {
                        Play(SoundId.IntroWhoosh);
                    }
                    break;
                case MatchPhase.Playing:
                    if (_lastPhase == MatchPhase.Countdown)
                    {
                        _openAt = Time.time;
                        Play(SoundId.IntroSlash);
                    }
                    if (rules.HasTimeLimit)
                    {
                        int seconds = Seconds(left);
                        TimerText = $"{seconds / 60}:{seconds % 60:00}";
                    }
                    break;
                case MatchPhase.Finished:
                    if (_lastPhase != MatchPhase.Finished)
                    {
                        _finishAt = Time.time;
                        Play(SoundId.Finish);
                    }
                    TimerText = "0:00";
                    if (Time.time - _finishAt < _finishSeconds)
                    {
                        CenterText = "FINISH!";
                    }
                    else
                    {
                        ShowingResults = true;
                        Ranking = MatchResults.Rank(States());
                    }
                    break;
            }
            _lastPhase = phase;
        }

        // 演出の進み progress までに、滑り込み始めた名前の数。
        private static int PlatesStartedAt(float progress)
        {
            if (progress < PlatesStart)
            {
                return 0;
            }
            return 1 + Mathf.FloorToInt((progress - PlatesStart) / PlateInterval);
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
            if (_center == null)
            {
                _center = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _timer = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _row = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
                _plate = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            }
            Color previous = GUI.color;
            Matrix4x4 matrix = GUI.matrix;
            if (ShowingIntro)
            {
                DrawIntro(_introProgress, 0f);
            }
            else if (OpeningIntro)
            {
                DrawIntro(1f, Ease((Time.time - _openAt) / _openSeconds));
            }
            GUI.matrix = matrix;
            if (TimerText.Length > 0)
            {
                bool warning = _rules != null && _rules.PhaseAt(_feed.CurrentTick) == MatchPhase.Playing
                    && _rules.TicksLeftAt(_feed.CurrentTick) <= _warningSeconds * SimTime.TicksPerSecond;
                Shadowed(new Rect(Screen.width * 0.5f - 120f, 8f, 240f, 56f), TimerText, _timer, warning ? new Color(1f, 0.35f, 0.3f) : Color.white);
            }
            if (CenterText.Length > 0)
            {
                Shadowed(new Rect(0f, Screen.height * 0.5f - 90f, Screen.width, 140f), CenterText, _center, new Color(1f, 0.5f, 0.3f));
            }
            if (ShowingResults)
            {
                DrawResults();
            }
            GUI.color = previous;
        }

        // シネマスコープの演出。progress はカウントダウンの進み（0〜1）、open は始まった後に帯が開く進み（0〜1）。
        private void DrawIntro(float progress, float open)
        {
            Matrix4x4 baseMatrix = GUI.matrix;
            float width = Screen.width;
            float height = Screen.height;
            float bar = height * 0.17f;
            float barsIn = Ease(progress / BarsInEnd);

            // 名前（帯の間に、上から順に並べる。偶数番は左から、奇数番は右から滑り込む。開くときは左右へ去る）。
            int count = _feed.CharacterCount;
            const float plateWidth = 440f;
            const float plateHeight = 58f;
            for (int i = 0; i < count; i++)
            {
                float t = Ease((progress - (PlatesStart + i * PlateInterval)) / PlateDuration);
                if (t <= 0f)
                {
                    continue;
                }
                float side = i % 2 == 0 ? -1f : 1f;
                float targetX = (width - plateWidth) * 0.5f;
                float startX = side < 0f ? -plateWidth : width;
                float x = Mathf.Lerp(startX, targetX, t) + side * open * width * 0.6f;
                float y = height * 0.5f + (i - (count - 1) * 0.5f) * (plateHeight + 12f) - plateHeight * 0.5f;
                float alpha = 1f - open;
                Color color = _feed.ColorOf(i);
                GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
                GUI.DrawTexture(new Rect(x, y, plateWidth, plateHeight), Texture2D.whiteTexture);
                GUI.color = new Color(color.r, color.g, color.b, alpha);
                GUI.DrawTexture(new Rect(x, y, 14f, plateHeight), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x, y + plateHeight - 3f, plateWidth, 3f), Texture2D.whiteTexture);
                Color text = Color.Lerp(color, Color.white, 0.4f);
                text.a = alpha;
                GUI.color = text;
                GUI.Label(new Rect(x + 32f, y, plateWidth - 40f, plateHeight), _feed.LabelOf(i), _plate);
            }

            // 切り裂く光（カウントダウンの終わりに、画面を斜めに横切って伸びる）。
            float slash = open > 0f ? 1f : Ease((progress - SlashStart) / (1f - SlashStart));
            if (slash > 0f)
            {
                GUIUtility.RotateAroundPivot(-8f, new Vector2(width * 0.5f, height * 0.5f));
                float length = width * 1.5f * slash;
                GUI.color = new Color(1f, 1f, 1f, 0.9f * (1f - open));
                GUI.DrawTexture(new Rect((width - length) * 0.5f, height * 0.5f - 2f, length, 4f), Texture2D.whiteTexture);
                GUI.matrix = baseMatrix;
            }

            // 上下の黒い帯（入ってくる。開くときは、少し傾きながら上下へ去る）。
            DrawBar(new Rect(-width * 0.25f, -bar + bar * barsIn - open * bar * 1.6f, width * 1.5f, bar), -6f * open, baseMatrix);
            DrawBar(new Rect(-width * 0.25f, height - bar * barsIn + open * bar * 1.6f, width * 1.5f, bar), -6f * open, baseMatrix);

            // 開いた瞬間の白い光。
            if (open > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f * (1f - open));
                GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            }
        }

        private static void DrawBar(Rect rect, float angle, Matrix4x4 baseMatrix)
        {
            if (angle != 0f)
            {
                GUIUtility.RotateAroundPivot(angle, rect.center);
            }
            GUI.color = Color.black;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.matrix = baseMatrix;
        }

        private void DrawResults()
        {
            float width = 640f;
            bool buttons = _matchControls != null;
            float height = 110f + Ranking.Count * 44f + (buttons ? 64f : 0f);
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.Box(area, GUIContent.none);
            Shadowed(new Rect(area.x, area.y + 8f, width, 44f), "RESULT", _timer, new Color(1f, 0.85f, 0.3f));
            int local = _feed.LocalCharacter;
            for (int i = 0; i < Ranking.Count; i++)
            {
                int index = Ranking[i];
                CharacterState state = _feed.GetState(index);
                string mark = index == local ? ">" : " ";
                string text = $"{mark}{i + 1}.  {_feed.LabelOf(index),-8}  {state.Coins.Banked,4} COIN   KO {state.Combat.Knockouts,2}  Fall {state.Combat.Falls,2}";
                Shadowed(new Rect(area.x + 40f, area.y + 60f + i * 44f, width - 60f, 40f), text, _row, i == 0 ? new Color(1f, 0.9f, 0.4f) : Color.white);
            }
            // 順位は確定コインの数（D-25）。同じなら仮に撃破の数（同点のアディショナルタイムは T4-8。D-45）。
            GUI.color = new Color(0.8f, 0.8f, 0.85f);
            float noteY = area.y + 64f + Ranking.Count * 44f;
            GUI.Label(new Rect(area.x + 20f, noteY, width - 40f, 28f), "Ranked by banked coins (ties: KO, then fewer falls)");
            if (!buttons)
            {
                return;
            }
            GUI.color = Color.white;
            var restart = new Rect(area.x + 40f, area.yMax - 56f, 220f, 40f);
            var quit = new Rect(area.xMax - 260f, area.yMax - 56f, 220f, 40f);
            GUI.enabled = _matchControls.CanRestart;
            if (GUI.Button(restart, _matchControls.CanRestart ? "Restart" : "Waiting for the host"))
            {
                _matchControls.Restart();
            }
            GUI.enabled = true;
            if (GUI.Button(quit, "Quit"))
            {
                _matchControls.Quit();
            }
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
