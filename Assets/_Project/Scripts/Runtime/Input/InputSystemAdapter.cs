using System;
using Degunatsu.Core.Input;
using UnityEngine.InputSystem;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.Runtime.Input
{
    /// <summary>
    /// Input System を読み、ティックごとの PlayerInput を作る（docs/03_architecture.md 4.5）。
    /// Input System を直接読むのはここだけで、シミュレーションは PlayerInput だけを見る。
    /// アクションは DegunatsuControls.inputactions の "Player" マップ（Move / Jump / Wire / Reset）。
    /// </summary>
    public sealed class InputSystemAdapter : IDisposable
    {
        private const string MapName = "Player";

        private readonly InputActionMap _map;
        private readonly InputAction _move;
        private readonly InputAction _jump;
        private readonly InputAction _wire;
        private readonly InputAction _reset;

        // 前のティックからの間に押されたボタン。ティックの間に押して離しても取りこぼさないようにする。
        private InputButtons _pressedSinceLastTick;

        public InputSystemAdapter(InputActionAsset actions)
        {
            if (actions == null)
            {
                throw new ArgumentNullException(nameof(actions));
            }
            _map = actions.FindActionMap(MapName, throwIfNotFound: true);
            _move = _map.FindAction("Move", throwIfNotFound: true);
            _jump = _map.FindAction("Jump", throwIfNotFound: true);
            _wire = _map.FindAction("Wire", throwIfNotFound: true);
            _reset = _map.FindAction("Reset", throwIfNotFound: true);
        }

        /// <summary>このフレームで位置のリセットが押されたか（Poll で更新する）。</summary>
        public bool ResetRequested { get; private set; }

        public void Enable()
        {
            _map.Enable();
        }

        public void Disable()
        {
            _map.Disable();
        }

        /// <summary>毎フレーム、ティックを進める前に呼ぶ。</summary>
        public void Poll()
        {
            if (_jump.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Jump;
            }
            if (_wire.WasPressedThisFrame())
            {
                _pressedSinceLastTick |= InputButtons.Wire;
            }
            ResetRequested = _reset.WasPressedThisFrame();
        }

        /// <summary>1ティック分の入力を作る。前のティックから押されたボタンは、離していても押している扱いにする。</summary>
        public SimInput ConsumeTick()
        {
            InputButtons buttons = _pressedSinceLastTick;
            if (_jump.IsPressed())
            {
                buttons |= InputButtons.Jump;
            }
            if (_wire.IsPressed())
            {
                buttons |= InputButtons.Wire;
            }
            _pressedSinceLastTick = InputButtons.None;

            UnityEngine.Vector2 move = _move.ReadValue<UnityEngine.Vector2>();
            return SimInput.FromAxes(move.x, move.y, buttons);
        }

        public void Dispose()
        {
            _map.Disable();
        }
    }
}
