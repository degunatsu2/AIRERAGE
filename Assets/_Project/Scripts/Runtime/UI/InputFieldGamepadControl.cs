using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの入力欄（名前・アドレス・ポート）をゲームパッドでも使えるようにする（ユーザーの指摘、2026-10-19）。
    /// - 選んだだけでは文字の入力を始めない（入力を始めると、ゲームパッドのカーソルが上下に動かなくなっていた）。決定（Enter ／ A）か
    ///   クリックで入力を始める。
    /// - 入力中にゲームパッドの A・B・十字キー・スティックを押すと、入力をやめる（そのあとはふつうにカーソルで項目を移れる）。
    ///   キーボードの Enter ／ Esc でもやめられる（InputField がもともとすること）。
    /// </summary>
    [RequireComponent(typeof(InputField))]
    public sealed class InputFieldGamepadControl : MonoBehaviour
    {
        private const float StickThreshold = 0.5f;

        private InputField _field;

        private void Awake()
        {
            _field = GetComponent<InputField>();
            _field.shouldActivateOnSelect = false;
        }

        private void Update()
        {
            if (!_field.isFocused)
            {
                return;
            }
            foreach (Gamepad gamepad in Gamepad.all)
            {
                if (gamepad.buttonSouth.wasPressedThisFrame || gamepad.buttonEast.wasPressedThisFrame
                    || gamepad.dpad.up.wasPressedThisFrame || gamepad.dpad.down.wasPressedThisFrame
                    || Mathf.Abs(gamepad.leftStick.ReadValue().y) > StickThreshold)
                {
                    _field.DeactivateInputField();
                    return;
                }
            }
        }
    }
}
