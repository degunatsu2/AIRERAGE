using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// セーフティボックス（RISK TAKER の E）を長押ししている人の頭上に、預けて減る手持ちのコインの数（「-45」）を出す
    /// （ユーザーの案、2026-10-15。docs/14 1.3）。だれのものでも出す（ほかのプレイヤーにも見える）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class HoldDepositHud : MonoBehaviour
    {
        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("頭上の位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;

        private ISimulationFeed _feed;
        private GUIStyle _style;

        /// <summary>キャラクター index が長押しで預ける枚数（長押ししていなければ -1。表示とテストに使う）。</summary>
        public int DepositOf(int index)
        {
            if (_feed == null || index < 0 || index >= _feed.CharacterCount)
            {
                return -1;
            }
            return DepositOf(_feed.GetState(index), _feed.GetConfig(index));
        }

        /// <summary>state が長押しで預ける枚数（手持ちと上限まで。長押ししていない・金庫の技でなければ -1）。</summary>
        public static int DepositOf(in CharacterState state, CharacterConfig config)
        {
            if (!state.Combat.Holding || config.Combat.Get(state.Combat.HoldSlot).Effect != SkillEffect.Safe)
            {
                return -1;
            }
            SafeTuning safe = config.Skills.Safe;
            return Mathf.Min(Mathf.Min(state.Coins.Held, safe.DepositFor(state.Combat.HoldTicks)), safe.MaxCoins);
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void OnGUI()
        {
            if (_camera == null)
            {
                return;
            }
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                int deposit = DepositOf(i);
                if (deposit < 0)
                {
                    continue;
                }
                if (_style == null)
                {
                    _style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                    _style.normal.textColor = Color.white;
                }
                CharacterState state = _feed.GetState(i);
                float top = state.Position.Y + _feed.GetConfig(i).Movement.HalfHeight + 1.2f;
                Vector3 p = _camera.WorldToScreenPoint(new Vector3(state.Position.X, top, 0f));
                if (p.z <= 0f)
                {
                    continue;
                }
                var rect = new Rect(p.x - 60f, Screen.height - p.y - 16f, 120f, 32f);
                GuidePlate.DrawText(rect, $"-{deposit}", _style, new Color(1f, 0.82f, 0.25f));
            }
        }
    }
}
