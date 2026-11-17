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
    /// IME の切り替えは 2 つの入口の両方で行う: Input System の Keyboard と、古い入力の UnityEngine.Input.imeCompositionMode（uGUI の入力欄が使い、
    /// 入力を終えると「自動」に戻す）。片方だけだと、エディタのゲームビューで IME が切れず、見出しの上に入力中の窓が残った
    /// （2026-11-17 の指摘。見出しの上の四角は、画面にいる間は消えず、別の画面へ移ると消える）。
    /// 切り替えるのは状態が変わったときだけ（毎フレーム書かない）。ほかから書き換えられたら 1 秒以上空けて合わせ直す。ほかのアプリから
    /// 戻った直後の 0.6 秒は触らない（OnApplicationFocus）。
    /// 2 回目の指摘（2026-11-17: ウインドウを戻すと同時に Esc を押すと止まる。エディタでもビルドでも）: ほかのアプリから戻っても当て直さない。
    /// 合わせ直しも入力欄がある画面だけ（試合中は IME に一度も触らない）。IME の切り替えの途中かどうかは Busy（止まったときの見張り
    /// HangWatchdog が読む）。
    /// </summary>
    public sealed class ImeGuard : MonoBehaviour
    {
        /// <summary>ほかのアプリから戻ってから、IME に触らずに待つ秒数。</summary>
        public const float SettleSeconds = 0.6f;

        /// <summary>古い入力の IME の設定がほかから書き換えられたとき、合わせ直す間隔の下限（秒）。</summary>
        public const float RetrySeconds = 1f;

        private Keyboard _keyboard;
        private bool _applied;
        private bool _enabled;
        private bool _unfocused;
        private float _quietUntil = float.NegativeInfinity;
        private float _legacySetAt = float.NegativeInfinity;
        private Vector2 _cursor = new Vector2(float.NaN, float.NaN);
        private volatile string _busy;

        /// <summary>今 IME を有効にしているか（テスト用）。</summary>
        public bool ImeEnabled => _enabled;

        /// <summary>古い入力の IME の設定を書いた回数（テスト用。毎フレーム書き続けていないかを確かめる）。</summary>
        public int LegacyWrites { get; private set; }

        /// <summary>今している IME の操作（していなければ null。止まったときの見張りがほかのスレッドから読む）。</summary>
        public string Busy => _busy;

        /// <summary>ほかのアプリから戻った直後で、IME に触らずに待っているか（テスト用）。</summary>
        public bool Settling => _unfocused || Time.unscaledTime < _quietUntil;

        /// <summary>古い入力の IME の設定のうち、今の状態で求めるもの（入力中は有効、ほかは無効）。</summary>
        public static IMECompositionMode LegacyModeFor(bool typing)
        {
            return typing ? IMECompositionMode.On : IMECompositionMode.Off;
        }

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
            UnityEngine.Input.imeCompositionMode = IMECompositionMode.Auto;
        }

        /// <summary>
        /// ほかのアプリへ移った・戻った（2026-11-17 の指摘: 試合中にウインドウを戻すと、たまにゲームが止まる）。ウインドウが切り替わる
        /// 途中に IME を切り替えると、Windows の日本語入力とエディタ（ゲームビューを出したときに IME の設定を戻す）とで切り替え合い、
        /// 止まることがある。離れている間と戻ってから少しの間は IME に触らず、落ち着いてから 1 回だけ当て直す。
        /// </summary>
        public void OnApplicationFocus(bool focused)
        {
            _unfocused = !focused;
            if (focused)
            {
                // 当て直さない（戻ったときに IME に触ると止まることがある。入力欄の出入りのときだけ切り替える）。
                _quietUntil = Time.unscaledTime + SettleSeconds;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || Settling)
            {
                return;
            }
            InputField field = FocusedField(EventSystem.current);
            bool want = field != null;
            IMECompositionMode legacy = LegacyModeFor(want);
            if (!_applied || keyboard != _keyboard || want != _enabled)
            {
                // 入力を始めた・終えた（と最初の 1 回）: 両方の入口を 1 回ずつ切り替える。
                _busy = want ? "Keyboard.SetIMEEnabled(true)" : "Keyboard.SetIMEEnabled(false)";
                keyboard.SetIMEEnabled(want);
                _busy = null;
                SetLegacy(legacy);
                _keyboard = keyboard;
                _enabled = want;
                _applied = true;
                _cursor = new Vector2(float.NaN, float.NaN);
            }
            else if (UnityEngine.Input.imeCompositionMode != legacy && Time.unscaledTime - _legacySetAt >= RetrySeconds)
            {
                // ほか（uGUI の入力欄・エディタ）が古い入力の設定を書き換えた。毎フレーム書き合わないよう、間を空けて合わせ直す。
                // 入力欄がある画面（ネットワークの画面など）だけ。試合中は触らない。
                _legacySetAt = Time.unscaledTime;
                if (FindAnyObjectByType<InputField>() != null)
                {
                    SetLegacy(legacy);
                }
            }
            if (want)
            {
                Vector2 cursor = CursorFor(field);
                if (cursor != _cursor)
                {
                    _busy = "Keyboard.SetIMECursorPosition";
                    keyboard.SetIMECursorPosition(cursor);
                    _busy = null;
                    _cursor = cursor;
                }
            }
        }

        private void SetLegacy(IMECompositionMode mode)
        {
            if (UnityEngine.Input.imeCompositionMode != mode)
            {
                _busy = $"Input.imeCompositionMode = {mode}";
                UnityEngine.Input.imeCompositionMode = mode;
                _busy = null;
                LegacyWrites++;
            }
            _legacySetAt = Time.unscaledTime;
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
