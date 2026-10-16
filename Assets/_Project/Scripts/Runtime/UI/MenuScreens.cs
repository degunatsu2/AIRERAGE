using System;
using Airerage.Runtime.Flow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューのシーンの画面（docs/15 T6-1。D-54、D-65、D-69）:
    /// - タイトル: 「AIRERAGE」と「PRESS ANY BUTTON」（どれかのキー・ボタンでメインメニューへ。最初の 1 回だけ）。
    /// - メインメニュー: SINGLE（Phase 7 の CPU まで選べない）、LOCAL VS（T6-5 まで選べない）、NETWORK VS、TRAINING、OPTIONS（T6-9 まで
    ///   選べない）、QUIT。キーボード（矢印・Enter・Esc）、ゲームパッド（スティック・十字キー・A・B）、マウスで選ぶ。戻るとタイトル。
    /// 画面の移り変わりは App のシーンの GameFlow に頼む（App のシーンから始めないと、項目を選んでも何もしない）。
    /// </summary>
    public sealed class MenuScreens : MonoBehaviour
    {
        [SerializeField] private GameObject _title;
        [SerializeField] private Text _pressAnyButton;
        [SerializeField] private GameObject _main;
        [SerializeField] private MenuOption _single;
        [SerializeField] private MenuOption _local;
        [SerializeField] private MenuOption _network;
        [SerializeField] private MenuOption _training;
        [SerializeField] private MenuOption _options;
        [SerializeField] private MenuOption _quit;

        private GameFlow _flow;
        private IDisposable _anyButton;
        private float _shownAt;
        private int _selectAtFrame = -1;

        /// <summary>タイトルを出しているか（テスト用）。</summary>
        public bool ShowingTitle => _title != null && _title.activeSelf;

        /// <summary>メインメニューを出しているか（テスト用）。</summary>
        public bool ShowingMain => _main != null && _main.activeSelf;

        public MenuOption Single => _single;
        public MenuOption Local => _local;
        public MenuOption Network => _network;
        public MenuOption Training => _training;
        public MenuOption Options => _options;
        public MenuOption Quit => _quit;

        private void Awake()
        {
            if (_title == null || _main == null || _single == null || _local == null || _network == null || _training == null
                || _options == null || _quit == null)
            {
                Debug.LogError($"{name}: 画面か項目が設定されていません", this);
                enabled = false;
                return;
            }
            _single.SetAvailable(false, "COMING SOON (CPU)");
            _local.SetAvailable(false, "COMING SOON");
            _options.SetAvailable(false, "COMING SOON");
            _network.Clicked.AddListener(() => Run(flow => flow.StartOnline()));
            _training.Clicked.AddListener(() => Run(flow => flow.StartTraining(flow.Stages.Get(0))));
            _quit.Clicked.AddListener(() => Run(flow => flow.QuitGame()));
            foreach (MenuOption item in new[] { _single, _local, _network, _training, _options, _quit })
            {
                item.Cancelled += ShowTitle;
            }
        }

        private void Start()
        {
            _flow = FindFirstObjectByType<GameFlow>();
            if (_flow == null)
            {
                Debug.LogWarning($"{name}: GameFlow がありません（App のシーンから始めてください）。項目を選んでも画面は移りません", this);
            }
            if (_flow != null && _flow.TitlePassed)
            {
                ShowMain();
            }
            else
            {
                ShowTitle();
            }
        }

        private void OnDestroy()
        {
            _anyButton?.Dispose();
            _anyButton = null;
        }

        private void Update()
        {
            if (_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame)
            {
                _selectAtFrame = -1;
                SelectFirst();
            }
            if (ShowingTitle && _pressAnyButton != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime - _shownAt) * 3f);
                _pressAnyButton.color = new Color(1f, 1f, 1f, 0.35f + 0.65f * pulse);
            }
        }

        /// <summary>タイトルを出す（どれかのキー・ボタンでメインメニューへ）。</summary>
        public void ShowTitle()
        {
            _main.SetActive(false);
            _title.SetActive(true);
            _shownAt = Time.unscaledTime;
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            _anyButton?.Dispose();
            _anyButton = InputSystem.onAnyButtonPress.CallOnce(_ => ContinueFromTitle());
        }

        /// <summary>タイトルからメインメニューへ（テスト用にも公開）。</summary>
        public void ContinueFromTitle()
        {
            _anyButton?.Dispose();
            _anyButton = null;
            ShowMain();
        }

        private void ShowMain()
        {
            _title.SetActive(false);
            _main.SetActive(true);
            _flow?.MarkTitlePassed();
            // タイトルを抜けたボタン（Enter や A）で、そのまま最初の項目まで決まらないよう、項目を選ぶのは次のフレーム。
            _selectAtFrame = Time.frameCount + 1;
        }

        private void SelectFirst()
        {
            MenuOption first = Array.Find(new[] { _single, _local, _network, _training, _options, _quit }, item => item.Button.interactable);
            if (EventSystem.current != null && first != null)
            {
                EventSystem.current.SetSelectedGameObject(first.gameObject);
            }
        }

        private void Run(Action<GameFlow> action)
        {
            if (_flow == null || _flow.IsLoading)
            {
                return;
            }
            action(_flow);
        }
    }
}
