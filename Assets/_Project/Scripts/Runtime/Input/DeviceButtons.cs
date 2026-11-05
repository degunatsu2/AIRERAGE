using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Airerage.Runtime.Input
{
    /// <summary>
    /// 1 つの操作（InputAction）の、ゲームパッドの割り当てとそれ以外（キーボードとマウス）の割り当てを分けて読む（ゲームパッドの
    /// アイテムの操作は ITEM MENU のボタンを押している間だけ効くため。ユーザーの要望、2026-11-05。docs/20_item_plan.md）。
    /// </summary>
    public static class DeviceButtons
    {
        /// <summary>action の、gamepad（true ならゲームパッド、false ならそれ以外）の割り当てのどれかを押しているか。</summary>
        public static bool IsPressed(InputAction action, bool gamepad)
        {
            if (action == null || !action.enabled)
            {
                return false;
            }
            foreach (InputControl control in action.controls)
            {
                if (control is ButtonControl button && (control.device is Gamepad) == gamepad && button.isPressed)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>action の、gamepad の割り当てのどれかをこのフレームで押したか。</summary>
        public static bool WasPressedThisFrame(InputAction action, bool gamepad)
        {
            if (action == null || !action.enabled)
            {
                return false;
            }
            foreach (InputControl control in action.controls)
            {
                if (control is ButtonControl button && (control.device is Gamepad) == gamepad && button.wasPressedThisFrame)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
