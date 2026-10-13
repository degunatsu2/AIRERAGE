using System.Collections.Generic;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Common;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 試合の表示（docs/12_match_plan.md T4-1、T4-2）: 始まる前の「3」「2」「1」と「GO!」、上の中央の残り時間、
    /// 終わったときの「FINISH!」と結果（順位）、自分が撃破されたときの「RESPAWN n」。
    /// 試合の段階と残りは、ティック数と MatchRules から求める（ISimulationFeed）。状態を読むだけで、書き換えない。
    /// 時間の制限がないとき（検証用のシーン）は、復帰までの秒数だけを出す。
    /// </summary>
    public sealed class MatchHud : MonoBehaviour
    {
        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("カウントダウン、GO!、FINISH! の音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("「GO!」を出す時間（秒）")]
        [SerializeField] private float _goSeconds = 1f;
        [Tooltip("「FINISH!」を出してから結果を出すまで（秒）")]
        [SerializeField] private float _finishSeconds = 1.5f;
        [Tooltip("残り時間を目立たせる秒数")]
        [SerializeField] private int _warningSeconds = 10;

        private ISimulationFeed _feed;
        private MatchRules _rules;
        private MatchPhase? _lastPhase;
        private int _lastCountdown = -1;
        private float _goAt = -1f;
        private float _finishAt = -1f;
        private GUIStyle _center;
        private GUIStyle _timer;
        private GUIStyle _row;

        /// <summary>画面の中央に出している文字（なければ空。テスト用）。</summary>
        public string CenterText { get; private set; } = string.Empty;

        /// <summary>上の中央の残り時間（時間の制限がなければ空）。</summary>
        public string TimerText { get; private set; } = string.Empty;

        /// <summary>結果を出しているか。</summary>
        public bool ShowingResults { get; private set; }

        /// <summary>結果の順位（キャラクターの番号。出していなければ空）。</summary>
        public IReadOnlyList<int> Ranking { get; private set; } = new int[0];

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void Update()
        {
            MatchRules rules = _feed.Rules;
            if (!ReferenceEquals(rules, _rules))
            {
                // 試合が始まった、または終わって抜けた（オンライン）。表示を初めからにする。
                _rules = rules;
                _lastPhase = null;
                _lastCountdown = -1;
                _goAt = -1f;
                _finishAt = -1f;
            }
            CenterText = string.Empty;
            TimerText = string.Empty;
            ShowingResults = false;
            Ranking = new int[0];
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
                    int count = Seconds(left);
                    CenterText = count.ToString();
                    if (count != _lastCountdown)
                    {
                        _lastCountdown = count;
                        Play(SoundId.CountdownBeep);
                    }
                    break;
                case MatchPhase.Playing:
                    if (_lastPhase == MatchPhase.Countdown)
                    {
                        _goAt = Time.time;
                        Play(SoundId.Go);
                    }
                    if (_goAt >= 0f && Time.time - _goAt < _goSeconds)
                    {
                        CenterText = "GO!";
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

            if (CenterText.Length == 0 && phase != MatchPhase.Finished)
            {
                int local = _feed.LocalCharacter;
                if (local >= 0 && local < _feed.CharacterCount)
                {
                    CombatState combat = _feed.GetState(local).Combat;
                    if (combat.IsWaitingToRespawn)
                    {
                        CenterText = $"RESPAWN {Seconds(combat.RespawnTicksLeft)}";
                    }
                }
            }
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

        // 残りのティックを、切り上げた秒にする（「3」「2」「1」、残り時間の表示）。
        private static int Seconds(int ticks)
        {
            return (ticks + SimTime.TicksPerSecond - 1) / SimTime.TicksPerSecond;
        }

        private void OnGUI()
        {
            if (_center == null)
            {
                _center = new GUIStyle(GUI.skin.label) { fontSize = 96, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _timer = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _row = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            }
            Color previous = GUI.color;
            if (TimerText.Length > 0)
            {
                bool warning = _rules != null && _rules.PhaseAt(_feed.CurrentTick) == MatchPhase.Playing
                    && _rules.TicksLeftAt(_feed.CurrentTick) <= _warningSeconds * SimTime.TicksPerSecond;
                Shadowed(new Rect(Screen.width * 0.5f - 120f, 8f, 240f, 56f), TimerText, _timer, warning ? new Color(1f, 0.35f, 0.3f) : Color.white);
            }
            if (CenterText.Length > 0)
            {
                Color color = CenterText == "GO!" ? new Color(1f, 0.9f, 0.3f)
                    : CenterText == "FINISH!" ? new Color(1f, 0.5f, 0.3f) : Color.white;
                Shadowed(new Rect(0f, Screen.height * 0.5f - 90f, Screen.width, 140f), CenterText, _center, color);
            }
            if (ShowingResults)
            {
                DrawResults();
            }
            GUI.color = previous;
        }

        private void DrawResults()
        {
            float width = 560f;
            float height = 110f + Ranking.Count * 44f;
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.Box(area, GUIContent.none);
            Shadowed(new Rect(area.x, area.y + 8f, width, 44f), "RESULT", _timer, new Color(1f, 0.85f, 0.3f));
            int local = _feed.LocalCharacter;
            for (int i = 0; i < Ranking.Count; i++)
            {
                int index = Ranking[i];
                CombatState combat = _feed.GetState(index).Combat;
                string mark = index == local ? ">" : " ";
                string text = $"{mark}{i + 1}.  {_feed.LabelOf(index),-8}  KO {combat.Knockouts,2}   Fall {combat.Falls,2}";
                Shadowed(new Rect(area.x + 40f, area.y + 60f + i * 44f, width - 60f, 40f), text, _row, i == 0 ? new Color(1f, 0.9f, 0.4f) : Color.white);
            }
            // 順位は撃破の数で仮に決めている（コインができたら、コインの数で決める。docs/12）。
            GUI.color = new Color(0.8f, 0.8f, 0.85f);
            GUI.Label(new Rect(area.x + 20f, area.yMax - 34f, width - 40f, 28f), "Ranked by KO, then fewer falls (until coins are added)");
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
