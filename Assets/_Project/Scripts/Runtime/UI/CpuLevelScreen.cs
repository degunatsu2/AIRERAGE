using System;
using Airerage.Core.Ai;
using UnityEngine;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// シングルモードの CPU の強さを選ぶ画面（docs/16 T7-2。D-71【仮】）: EASY・NORMAL・HARD の札。前に選んだ強さから始める
    /// （PlayerPrefs）。決定で Confirmed、戻る操作で Cancelled。
    /// </summary>
    public sealed class CpuLevelScreen : MonoBehaviour
    {
        /// <summary>前に選んだ強さを覚えておく PlayerPrefs のキー。</summary>
        public const string PreferenceKey = "Airerage.CpuLevel";

        [Tooltip("強さの札（EASY・NORMAL・HARD の順）")]
        [SerializeField] private MenuOption[] _options = new MenuOption[0];

        /// <summary>強さを決めた。</summary>
        public event Action<CpuLevel> Confirmed;

        /// <summary>戻る操作をした。</summary>
        public event Action Cancelled;

        public bool IsShowing => gameObject.activeSelf;

        /// <summary>強さ level の札（テスト用）。</summary>
        public MenuOption Option(CpuLevel level) => _options[(int)level];

        private void Awake()
        {
            if (_options.Length != 3)
            {
                Debug.LogError($"{name}: 強さの札が 3 つ（EASY・NORMAL・HARD）ではありません（{_options.Length}）", this);
                enabled = false;
                return;
            }
            for (int i = 0; i < _options.Length; i++)
            {
                var level = (CpuLevel)i;
                _options[i].Clicked.AddListener(() =>
                {
                    PlayerPrefs.SetInt(PreferenceKey, (int)level);
                    Confirmed?.Invoke(level);
                });
                _options[i].Cancelled += () => Cancelled?.Invoke();
            }
        }

        /// <summary>出す（前に選んだ強さの札を選んだ状態で）。</summary>
        public void Show()
        {
            gameObject.SetActive(true);
            int saved = Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey, (int)CpuLevel.Normal), 0, _options.Length - 1);
            _options[saved].SelectQuietly();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
