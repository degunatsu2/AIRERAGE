using System.Text;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// デバッグ表示（docs/06_prototype_plan.md T1-9）。画面の右上に IMGUI で、キャラクターとワイヤーの状態、
    /// 入力、計測（速さ・最高速・解除時の速さ）を出す。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        private const float Width = 420f;

        [SerializeField] private SimulationRunner _runner;

        private readonly MotionStats _stats = new MotionStats();
        private readonly StringBuilder _text = new StringBuilder();
        private GUIStyle _style;

        /// <summary>計測（テスト用）。</summary>
        public MotionStats Stats => _stats;

        private void Awake()
        {
            if (_runner == null)
            {
                Debug.LogError($"{name}: SimulationRunner が設定されていません", this);
                enabled = false;
            }
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
            _stats.Step(state);
        }

        private void OnPlayerReset()
        {
            _stats.Reset();
        }

        private void OnGUI()
        {
            if (!_runner.enabled)
            {
                return;
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            }

            CharacterState s = _runner.PlayerState;
            WireTuning tuning = _runner.PlayerConfig.Wire;
            SimInput input = _runner.LastInput;
            WireState w = s.Wire;

            _text.Clear();
            _text.Append("Debug  Tab: type  F1: speed  F2: step\n");
            _text.Append("Type ").Append(WireTypeLabels.Long(tuning.ModelType))
                .Append("   Speed ").Append(_runner.SpeedScale > 0f ? "x" + _runner.SpeedScale.ToString("0.##") : "PAUSED")
                .Append("   Tick ").Append(_runner.CurrentTick).Append('\n');
            _text.Append("Input ").Append(_runner.UsingPointer ? "mouse" : "pad").Append("  move (")
                .Append(input.Move.X.ToString("0.00")).Append(", ").Append(input.Move.Y.ToString("0.00")).Append(')');
            if (input.HasAim)
            {
                _text.Append("  aim (").Append(input.Aim.X.ToString("0.00")).Append(", ").Append(input.Aim.Y.ToString("0.00")).Append(')');
            }
            _text.Append("  [").Append(input.Buttons).Append("]\n");
            _text.Append("Pos (").Append(s.Position.X.ToString("0.00")).Append(", ").Append(s.Position.Y.ToString("0.00"))
                .Append(")  Vel (").Append(s.Velocity.X.ToString("0.0")).Append(", ").Append(s.Velocity.Y.ToString("0.0")).Append(")\n");
            _text.Append("Speed ").Append(_stats.CurrentSpeed.ToString("0.0")).Append("  Max ").Append(_stats.MaxSpeed.ToString("0.0"))
                .Append("  Exit ").Append(_stats.LastExitSpeed.HasValue ? _stats.LastExitSpeed.Value.ToString("0.0") : "--").Append(" m/s\n");
            _text.Append(s.Grounded ? "Grounded" : "Air").Append("  Facing ").Append(s.Facing > 0 ? "R" : "L")
                .Append("  Air jumps used ").Append(s.AirJumpsUsed).Append('\n');
            _text.Append("Wire ").Append(w.Phase).Append(" (").Append(w.PhaseTicks).Append("t)");
            if (w.Phase == WirePhase.Attached)
            {
                _text.Append("  Rope ").Append(w.RopeLength.ToString("0.00")).Append(" m");
            }
            if (w.Phase == WirePhase.Cooldown)
            {
                _text.Append("  Cooldown ").Append(w.CooldownTicksLeft).Append('t');
            }
            _text.Append('\n');
            _text.Append("Last release ").Append(w.LastReleaseReason).Append("  Last miss ").Append(w.LastMissReason).Append('\n');
            AppendAction("Action ", s);
            int lines = 10;
            if (_runner.DummyCount > 0)
            {
                CharacterState dummy = _runner.GetDummyState(0);
                _text.Append('\n');
                AppendAction("Dummy ", dummy);
                _text.Append("  Combo ").Append(dummy.Combat.ComboCount).Append(" (dmg ").Append(dummy.Combat.ComboDamage).Append(')');
                lines++;
            }
            GUI.Box(new Rect(Screen.width - Width - 10f, 10f, Width, 12f + 21f * lines), _text.ToString(), _style);
        }

        // アクション状態の1行（攻撃なら種類と段階、のけぞりなら残り。ヒットストップ中ならその残り）。
        private void AppendAction(string label, in CharacterState s)
        {
            _text.Append(label).Append(s.Action.Kind);
            if (s.Action.Kind == ActionKind.Attack)
            {
                AttackData attack = _runner.PlayerConfig.Combat.Get(s.Action.Attack);
                _text.Append(' ').Append(s.Action.Attack).Append(' ').Append(attack.PhaseAt(s.Action.Ticks))
                    .Append(" (").Append(s.Action.Ticks + 1).Append('/').Append(attack.TotalTicks).Append("t)");
            }
            else if (s.Action.Kind == ActionKind.Hitstun)
            {
                _text.Append(" (").Append(s.Action.Ticks).Append('/').Append(s.Action.Duration).Append("t)");
            }
            if (s.Combat.HitstopTicksLeft > 0)
            {
                _text.Append("  Hitstop ").Append(s.Combat.HitstopTicksLeft).Append('t');
            }
        }
    }
}
