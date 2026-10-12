using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーを線で表示する（プロトタイプ用）。フックが飛んでいる間はフックまで、接続中は接続点まで線を引き、接続点に印を置く。
    /// ロープが地形の角に巻き付いているときは、角（支点）で折れ曲がった線にする（D-33）。
    /// 外れたフックを回収している間は、外れた位置からキャラクターへ戻ってくるフックまで線を引く。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WireView : MonoBehaviour
    {
        [SerializeField] private Transform _anchorMarker;

        private LineRenderer _line;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            Hide();
        }

        public void Render(in CharacterState previous, in CharacterState current, float alpha, WireTuning tuning)
        {
            WireState wire = current.Wire;
            if (wire.Phase != WirePhase.Firing && wire.Phase != WirePhase.Attached && wire.Phase != WirePhase.Retracting)
            {
                Hide();
                return;
            }

            Vector3 from = Vector3.Lerp(previous.Position.ToUnity(), current.Position.ToUnity(), alpha);
            Vector3 to;
            if (wire.Phase == WirePhase.Attached)
            {
                to = wire.Anchor.ToUnity();
            }
            else if (wire.Phase == WirePhase.Retracting)
            {
                // 外れた位置（HookPosition）から、回収の時間をかけてキャラクターへ戻す。
                float progress = tuning.MissRetractTicks > 0 ? Mathf.Clamp01((wire.PhaseTicks + alpha) / tuning.MissRetractTicks) : 1f;
                to = Vector3.Lerp(wire.HookPosition.ToUnity(), from, progress);
            }
            else
            {
                // 直前のティックもフックが飛んでいれば補間する（射出した直後は現在の位置）。
                to = previous.Wire.Phase == WirePhase.Firing
                    ? Vector3.Lerp(previous.Wire.HookPosition.ToUnity(), wire.HookPosition.ToUnity(), alpha)
                    : wire.HookPosition.ToUnity();
            }

            _line.enabled = true;
            if (wire.Phase == WirePhase.Attached && wire.Pivots.Count > 0)
            {
                // キャラクター → 最後の支点 → … → 最初の支点 → 接続点。
                int count = wire.Pivots.Count;
                _line.positionCount = count + 2;
                _line.SetPosition(0, from);
                for (int i = 0; i < count; i++)
                {
                    _line.SetPosition(i + 1, wire.Pivots[count - 1 - i].ToUnity());
                }
                _line.SetPosition(count + 1, to);
            }
            else
            {
                _line.positionCount = 2;
                _line.SetPosition(0, from);
                _line.SetPosition(1, to);
            }
            if (_anchorMarker != null)
            {
                _anchorMarker.gameObject.SetActive(wire.Phase == WirePhase.Attached);
                _anchorMarker.position = to;
            }
        }

        private void Hide()
        {
            _line.enabled = false;
            if (_anchorMarker != null)
            {
                _anchorMarker.gameObject.SetActive(false);
            }
        }
    }
}
