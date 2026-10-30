using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// トレーニングの表示（ユーザーの要望、2026-10-30: 前の表示はほかの UI に重なり、内容が多すぎた）。画面の右下に、大きな日本語で 6 つだけ出す:
    /// 最後に当てた攻撃のダメージと吹っ飛ばし力（吹き飛ぶ速さ。m/s）、自分の今の速度（m/s）、最後に出した技の発生・持続・後隙（フレーム。
    /// 技の性能表 docs/17 と同じ数え方。長押しで変わる技は出したときの長さ）。F4（DebugControls の ToggleTrainingInfo）で出す・隠すを切り替える。
    /// キャラクターと重なったら透かす（HudOcclusion）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        private const float Width = 330f;
        private const float RowHeight = 38f;
        private const float Padding = 14f;
        private const float HintHeight = 24f;
        // 画面の右下からの余白（下は、版の表示 VersionLabel の上）。
        private const float RightMargin = 16f;
        private const float BottomMargin = 30f;

        /// <summary>表示する項目の名前（上から）。</summary>
        public static readonly string[] Labels = { "ダメージ", "吹っ飛ばし力", "速度", "発生", "持続", "後隙" };

        [SerializeField] private SimulationRunner _runner;

        private readonly MotionStats _stats = new MotionStats();
        private readonly string[] _values = new string[6];
        private readonly HudOcclusion.Link _occlusion = new HudOcclusion.Link();
        private GUIStyle _label;
        private GUIStyle _value;
        private GUIStyle _hint;

        // 最後に出した技（_hasLastAttack が false なら、まだ出していない）と、出したときの長押しの長さ。
        private bool _hasLastAttack;
        private AttackSlot _lastAttack;
        private int _lastAttackTicks;
        private int _lastCharge;

        /// <summary>計測（テスト用）。</summary>
        public MotionStats Stats => _stats;

        /// <summary>表示を出しているか（F4 で切り替える）。</summary>
        public bool Visible { get; private set; } = true;

        /// <summary>今の項目の値（Labels と同じ順。テスト用）。</summary>
        public string[] Values
        {
            get
            {
                UpdateValues();
                return (string[])_values.Clone();
            }
        }

        /// <summary>表示を出す・隠すを切り替える。</summary>
        public void Toggle()
        {
            Visible = !Visible;
        }

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
            if (state.Action.Kind == ActionKind.Attack)
            {
                // 新しく出した技なら（違う技か、経過が戻った）、長押しの長さを数え直す。
                if (!_hasLastAttack || state.Action.Attack != _lastAttack || state.Action.Ticks < _lastAttackTicks)
                {
                    _lastCharge = 0;
                }
                _hasLastAttack = true;
                _lastAttack = state.Action.Attack;
                _lastAttackTicks = state.Action.Ticks;
                _lastCharge = Mathf.Max(_lastCharge, state.Action.Charge);
            }
        }

        private void OnPlayerReset()
        {
            _stats.Reset();
            _hasLastAttack = false;
            _lastCharge = 0;
        }

        /// <summary>
        /// 技 attack を長押しの長さ charge で出したときの、発生（判定が最初に出るフレーム。出したフレームを 1）・持続（判定が出ているフレームの数）・
        /// 後隙（最後の判定が消えてから動けるまで）。技の性能表（docs/17）と同じ数え方。判定がなく投射物などを出す技の発生は物が出るフレームで、
        /// 持続と後隙は -1（「—」）。走る間や押している間ずっと続く判定は、持続と後隙が -1。
        /// </summary>
        public static void FramesOf(AttackData attack, int charge, out int startup, out int active, out int recovery)
        {
            int total = attack.TotalTicksFor(charge);
            int first = int.MaxValue;
            int end = 0;
            bool open = attack.Run;
            bool any = false;
            foreach (AttackHit hit in attack.Hits)
            {
                if (!attack.Uses(hit.Charge, charge))
                {
                    continue;
                }
                any = true;
                first = Mathf.Min(first, hit.StartTick);
                end = Mathf.Max(end, hit.StartTick + hit.ActiveTicks);
                open |= hit.UntilEnd;
            }
            if (!any)
            {
                startup = attack.Effect != SkillEffect.None ? attack.EffectTick + 1 : -1;
                active = -1;
                recovery = -1;
                return;
            }
            startup = first + 1;
            if (open)
            {
                active = -1;
                recovery = -1;
                return;
            }
            // 持続は、どれかの判定が出ているフレームの数（判定が重なっていても 1 回と数える）。
            active = 0;
            for (int t = first; t < end; t++)
            {
                foreach (AttackHit hit in attack.Hits)
                {
                    if (attack.Uses(hit.Charge, charge) && t >= hit.StartTick && t < hit.StartTick + hit.ActiveTicks)
                    {
                        active++;
                        break;
                    }
                }
            }
            recovery = Mathf.Max(0, total - end);
        }

        private void UpdateValues()
        {
            if (_runner.LastPlayerHit.HasValue)
            {
                HitRecord hit = _runner.LastPlayerHit.Value;
                _values[0] = hit.Damage.ToString();
                _values[1] = hit.Knockback.Length().ToString("0.0");
            }
            else
            {
                _values[0] = "—";
                _values[1] = "—";
            }
            _values[2] = _stats.CurrentSpeed.ToString("0.0") + " m/s";
            if (_hasLastAttack)
            {
                FramesOf(_runner.PlayerConfig.Combat.Get(_lastAttack), _lastCharge, out int startup, out int active, out int recovery);
                _values[3] = Frames(startup);
                _values[4] = Frames(active);
                _values[5] = Frames(recovery);
            }
            else
            {
                _values[3] = "—";
                _values[4] = "—";
                _values[5] = "—";
            }
        }

        private static string Frames(int frames)
        {
            return frames >= 0 ? frames + "F" : "—";
        }

        private void OnGUI()
        {
            if (!_runner.enabled || !Visible)
            {
                return;
            }
            if (_label == null)
            {
                _label = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft }, true);
                _value = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight }, true);
                _hint = GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleRight }, true);
            }
            UpdateValues();
            float height = Padding * 2f + RowHeight * Labels.Length + HintHeight;
            var panel = new Rect(Screen.width - RightMargin - Width, Screen.height - BottomMargin - height, Width, height);
            HudFade.Begin(_occlusion.AlphaFor(this, 0, panel));
            GUI.color = HudFade.Apply(new Color(0.03f, 0.05f, 0.09f, 0.78f));
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = HudFade.Apply(new Color(1f, 1f, 1f, 0.35f));
            GUI.DrawTexture(new Rect(panel.x, panel.y, 3f, panel.height), Texture2D.whiteTexture);
            for (int i = 0; i < Labels.Length; i++)
            {
                var row = new Rect(panel.x + Padding, panel.y + Padding + RowHeight * i, Width - Padding * 2f, RowHeight);
                GuidePlate.DrawText(row, Labels[i], _label, new Color(0.75f, 0.8f, 0.9f));
                GuidePlate.DrawText(row, _values[i], _value, Color.white);
            }
            var hint = new Rect(panel.x + Padding, panel.yMax - Padding - HintHeight, Width - Padding * 2f, HintHeight);
            GuidePlate.DrawText(hint, "F4: 表示の切り替え", _hint, new Color(1f, 1f, 1f, 0.6f));
            HudFade.End();
        }
    }
}
