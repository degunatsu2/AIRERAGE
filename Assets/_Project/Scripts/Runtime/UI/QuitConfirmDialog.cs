using System;
using UnityEngine;
using UnityEngine.UI;
using Airerage.Runtime.Settings;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ゲームを終了する前の確認（ユーザーの要望、2026-10-20）: 画面の真ん中に「ゲームを終了します。本当によろしいですか？」と
    /// 「はい」「いいえ」。最初は「いいえ」を選ぶ。はいで Confirmed、いいえか戻る操作（Esc・B）で閉じて、開いた側の続き（Show に渡した
    /// onCancel）へ。後ろは暗くし、クリックを通さない。開いた直後の少しの間は決定と戻るを受け付けない（開いたキーで閉じないように）。
    /// 日本語は日本語の字体で書く（GameFonts.Japanese）。
    /// </summary>
    public sealed class QuitConfirmDialog : MonoBehaviour
    {
        private const float InputGuardSeconds = 0.2f;

        [SerializeField] private Text _message;
        [SerializeField] private MenuOption _yes;
        [SerializeField] private MenuOption _no;

        private Action _onCancel;
        private float _shownAt;

        /// <summary>「はい」を選んだ（ゲームを終える）。</summary>
        public event Action Confirmed;

        public bool IsShowing => gameObject.activeSelf;
        public MenuOption Yes => _yes;
        public MenuOption No => _no;
        public string Message => _message != null ? _message.text : string.Empty;

        /// <summary>確認の文（英語と日本語）。</summary>
        public const string MessageEnglish = "QUIT THE GAME. ARE YOU SURE?";
        public const string MessageJapanese = "ゲームを終了します。本当によろしいですか？";

        private void Awake()
        {
            if (_message == null || _yes == null || _no == null)
            {
                Debug.LogError($"{name}: 終了の確認の項目が設定されていません", this);
                enabled = false;
                return;
            }
            _yes.Clicked.AddListener(() =>
            {
                if (Guarded())
                {
                    return;
                }
                Confirmed?.Invoke();
            });
            _no.Clicked.AddListener(Cancel);
            _yes.Cancelled += Cancel;
            _no.Cancelled += Cancel;
            Link(_yes.Button, _no.Button);
            Link(_no.Button, _yes.Button);
        }

        // 左右で「はい」と「いいえ」を行き来する。
        private static void Link(Selectable item, Selectable other)
        {
            item.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = other,
                selectOnRight = other,
            };
        }

        /// <summary>出す（「いいえ」を選ぶ）。onCancel は、いいえ・戻るで閉じたときに呼ぶ。</summary>
        public void Show(Action onCancel)
        {
            _onCancel = onCancel;
            _shownAt = Time.unscaledTime;
            // 言語の設定に合わせる（日本語なら日本語の字体）。
            LocalizedText.Show(_message, MessageEnglish, MessageJapanese);
            foreach (Text text in new[] { _yes.LabelText, _no.LabelText })
            {
                text.font = GameFonts.Switch(text.font, Loc.Japanese);
            }
            _yes.SetLabel(Loc.T("YES", "はい"));
            _no.SetLabel(Loc.T("NO", "いいえ"));
            gameObject.SetActive(true);
            _no.SelectQuietly();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Cancel()
        {
            if (Guarded())
            {
                return;
            }
            Hide();
            Action onCancel = _onCancel;
            _onCancel = null;
            onCancel?.Invoke();
        }

        // 開いた直後か（開いたキーの決定・戻るを受け付けない）。
        private bool Guarded()
        {
            return !gameObject.activeSelf || Time.unscaledTime - _shownAt < InputGuardSeconds;
        }
    }
}
