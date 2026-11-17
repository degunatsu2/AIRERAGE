using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 日本語入力（IME）を、文字の入力欄（ネットワーク対戦の名前・アドレス・ポート・参加コード）に入力している間だけ有効にする
    /// （2026-11-16 の指摘: 見出しの左上に白い小さな四角が時たま出る）。
    /// 新しい Input System だけを使う設定では、ゲームが IME を何もしないと、日本語入力が「あ」のままゲームの操作のキー（Q・E・M・C など）を
    /// 押したときに、IME の入力中の窓が画面の左上（位置を教えていないので）に出ていた。入力欄の外では IME を切り、入力欄に入力している間は
    /// IME を有効にして、窓を入力欄の左下に出す。いつも読み込むシーン（App）の EventSystem に置く。
    /// </summary>
    public sealed class ImeGuard : MonoBehaviour
    {
        private Keyboard _keyboard;
        private bool _applied;
        private bool _enabled;
        private Vector2 _cursor = new Vector2(float.NaN, float.NaN);

        /// <summary>今 IME を有効にしているか（テスト用）。</summary>
        public bool ImeEnabled => _enabled;

        private void OnEnable()
        {
            _applied = false;
        }

        private void OnDisable()
        {
            // ほかの場面（エディタの再生の終わりなど）に IME を切ったまま残さない。
            if (_keyboard != null && _keyboard.added)
            {
                _keyboard.SetIMEEnabled(true);
            }
            _keyboard = null;
        }

        private void OnApplicationFocus(bool focused)
        {
            // ほかのアプリから戻ったら、IME の状態を当て直す。
            if (focused)
            {
                _applied = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            InputField field = FocusedField(EventSystem.current);
            bool want = field != null;
            if (!_applied || keyboard != _keyboard || want != _enabled)
            {
                keyboard.SetIMEEnabled(want);
                _keyboard = keyboard;
                _enabled = want;
                _applied = true;
                _cursor = new Vector2(float.NaN, float.NaN);
            }
            if (want)
            {
                Vector2 cursor = CursorFor(field);
                if (cursor != _cursor)
                {
                    keyboard.SetIMECursorPosition(cursor);
                    _cursor = cursor;
                }
            }
        }

        /// <summary>今入力している文字の入力欄（なければ null）。</summary>
        public static InputField FocusedField(EventSystem events)
        {
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null)
            {
                return null;
            }
            InputField field = selected.GetComponent<InputField>();
            return field != null && field.isActiveAndEnabled && field.isFocused ? field : null;
        }

        // IME の窓を出す所（入力欄の左下。画面の左上から右・下へのピクセル）。
        private static Vector2 CursorFor(InputField field)
        {
            var rect = (RectTransform)field.transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Canvas parent = field.GetComponentInParent<Canvas>();
            Canvas canvas = parent != null ? parent.rootCanvas : null;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            return new Vector2(bottomLeft.x, Screen.height - bottomLeft.y);
        }
    }
}
