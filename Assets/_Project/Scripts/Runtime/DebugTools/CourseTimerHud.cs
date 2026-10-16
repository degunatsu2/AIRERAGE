using System.Collections.Generic;
using System.Text;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Data;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// タイム計測コースの計測と表示（デバッグ用。画面の左上に IMGUI で出す）。
    /// シーンの CourseZone（スタート1つ、ゴール複数）を集め、SimulationRunner のティックごとに計測する。
    /// </summary>
    public sealed class CourseTimerHud : MonoBehaviour
    {
        // ベストタイムの表の1列の幅（文字数）。
        private const int ColumnWidth = 9;

        [SerializeField] private SimulationRunner _runner;

        private CourseTimer _timer;
        private readonly StringBuilder _text = new StringBuilder();
        private GUIStyle _style;

        /// <summary>計測の状態（テスト用）。シーンにゾーンがなければ null。</summary>
        public CourseTimer Timer => _timer;

        private void Awake()
        {
            if (_runner == null)
            {
                Debug.LogError($"{name}: SimulationRunner が設定されていません", this);
                enabled = false;
                return;
            }

            Rect? start = null;
            var goals = new List<CourseTimer.Goal>();
            foreach (CourseZone zone in FindObjectsByType<CourseZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (zone.Kind == CourseZone.ZoneKind.Goal)
                {
                    goals.Add(new CourseTimer.Goal(zone.Course, zone.Area));
                }
                else if (start == null)
                {
                    start = zone.Area;
                }
                else
                {
                    Debug.LogError($"{name}: スタートの範囲が2つ以上あります（{zone.name}）。最初の1つだけを使います", zone);
                }
            }
            if (start == null || goals.Count == 0)
            {
                Debug.LogError($"{name}: スタートかゴールの範囲（CourseZone）がシーンにありません", this);
                enabled = false;
                return;
            }
            goals.Sort((a, b) => string.CompareOrdinal(a.Course, b.Course));
            _timer = new CourseTimer(start.Value, goals);
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.PlayerTicked += OnPlayerTicked;
                _runner.PlayerReset += OnPlayerReset;
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.PlayerTicked -= OnPlayerTicked;
                _runner.PlayerReset -= OnPlayerReset;
            }
        }

        private void OnPlayerTicked(CharacterState state)
        {
            if (_timer == null)
            {
                return;
            }
            CharacterConfig config = _runner.PlayerConfig;
            var center = new Vector2(state.Position.X, state.Position.Y);
            var half = new Vector2(config.Movement.HalfWidth, config.Movement.HalfHeight);
            _timer.Step(center, half, config.Wire.ModelType, _runner.PlayerCharacter.DisplayName);
        }

        private void OnPlayerReset()
        {
            _timer?.Abort();
        }

        private void OnGUI()
        {
            if (_timer == null)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16 };
            }

            _text.Clear();
            _text.Append("Course timer  (wire type ").Append(WireTypeLabels.Short(_runner.PlayerConfig.Wire.ModelType)).Append(")\n");
            switch (_timer.Phase)
            {
                case CourseTimer.TimerPhase.Waiting:
                    _text.Append("Go to START (green)\n");
                    break;
                case CourseTimer.TimerPhase.Ready:
                    _text.Append("Ready: leave START to begin\n");
                    break;
                case CourseTimer.TimerPhase.Running:
                    _text.Append("Running: ").Append(Seconds(_timer.RunningTicks)).Append('\n');
                    break;
            }
            if (_timer.LastRecord is CourseTimer.Record last)
            {
                _text.Append("Last: ").Append(last.Course).Append(' ').Append(Seconds(last.Ticks))
                    .Append(" (").Append(ShortName(last.Character)).Append(", ").Append(WireTypeLabels.Short(last.WireType)).Append(")\n");
            }
            // ベストは、今のワイヤーのタイプで、キャラクターごと（docs/13 T5-7 の比較）。
            WireModelType wireType = _runner.PlayerConfig.Wire.ModelType;
            IReadOnlyList<string> characters = Characters();
            _text.Append("Best      ");
            foreach (string character in characters)
            {
                _text.Append(ShortName(character).PadLeft(ColumnWidth));
            }
            _text.Append('\n');
            foreach (CourseTimer.Goal goal in _timer.Goals)
            {
                _text.Append(goal.Course.PadRight(10));
                foreach (string character in characters)
                {
                    _text.Append(Best(goal.Course, wireType, character).PadLeft(ColumnWidth));
                }
                _text.Append('\n');
            }
            // 行数: 見出し、状態、前回、ベストの見出し、コースごとの行
            float width = 140f + 95f * characters.Count;
            GUI.Box(new Rect(10f, 10f, width, 12f + 22f * (_timer.Goals.Count + 4)), _text.ToString(), _style);
        }

        // 列に並べるキャラクター（F3 の一覧の順。一覧がなければ今のキャラクターだけ）。
        private IReadOnlyList<string> Characters()
        {
            var names = new List<string>();
            if (_runner.Roster != null)
            {
                foreach (CharacterConfigAsset character in _runner.Roster.Characters)
                {
                    if (character != null)
                    {
                        names.Add(character.DisplayName);
                    }
                }
            }
            if (names.Count == 0)
            {
                names.Add(_runner.PlayerCharacter.DisplayName);
            }
            return names;
        }

        // 列の見出し（名前の最初の語）。
        private static string ShortName(string character)
        {
            if (string.IsNullOrEmpty(character))
            {
                return "-";
            }
            int space = character.IndexOf(' ');
            return space > 0 ? character.Substring(0, space) : character;
        }

        private string Best(string course, WireModelType type, string character)
        {
            return _timer.TryGetBest(course, type, character, out int ticks) ? Seconds(ticks) : "--";
        }

        private static string Seconds(int ticks)
        {
            return SimTime.TicksToSeconds(ticks).ToString("0.00") + "s";
        }

    }
}
