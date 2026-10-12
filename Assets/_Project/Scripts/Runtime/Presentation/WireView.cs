using Degunatsu.Core.Simulation;
using Degunatsu.Core.Wire;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// ワイヤーを線で表示する（プロトタイプ用）。フックが飛んでいる間はフックまで、接続中は接続点まで線を引き、接続点に印を置く。
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

        public void Render(in CharacterState previous, in CharacterState current, float alpha)
        {
            WireState wire = current.Wire;
            if (wire.Phase != WirePhase.Firing && wire.Phase != WirePhase.Attached)
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
            else
            {
                // 直前のティックもフックが飛んでいれば補間する（射出した直後は現在の位置）。
                to = previous.Wire.Phase == WirePhase.Firing
                    ? Vector3.Lerp(previous.Wire.HookPosition.ToUnity(), wire.HookPosition.ToUnity(), alpha)
                    : wire.HookPosition.ToUnity();
            }

            _line.enabled = true;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
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
