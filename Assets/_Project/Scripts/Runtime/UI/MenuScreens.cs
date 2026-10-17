using System;
using System.Collections.Generic;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
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
    /// - メインメニュー: SINGLE（Phase 7 の CPU まで選べない）、LOCAL VS、NETWORK VS、TRAINING、OPTIONS（T6-9 まで選べない）、QUIT。キーボード（矢印・Enter・Esc）、ゲームパッド（スティック・十字キー・A・B）、マウスで選ぶ。戻るとタイトル。
    /// - TRAINING → キャラクター選択（T6-3）→ ステージ選択（T6-4）→ トレーニング。戻る操作で 1 つ前の画面へ。選んだキャラクターは
    ///   覚えておき（PlayerPrefs。オンラインの待機所と同じ）、トレーニングの試合の仕組みがそのキャラクターで始める。
    /// - LOCAL VS → 参加とキャラクター選択（LocalJoinScreen。T6-5）→ ステージ選択 → ローカル対戦。ステージ選択から戻ると、参加した人は
    ///   そのまま（READY だけ外す）。
    /// 画面の移り変わりは App のシーンの GameFlow に頼む（App のシーンから始めないと、試合へは移らない）。
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
        [SerializeField] private CharacterSelectScreen _characterSelect;
        [SerializeField] private StageSelectScreen _stageSelect;
        [SerializeField] private LocalJoinScreen _localJoin;

        private GameFlow _flow;
        private IDisposable _anyButton;
        private float _shownAt;
        private int _selectAtFrame = -1;
        private MenuOption _focus;
        // ステージ選択の後に始めるもの（トレーニングかローカル対戦）と、ローカル対戦に参加した人。
        private bool _localMode;
        private IReadOnlyList<LocalPlayerSetup> _localPlayers;
        private bool _localTimed = true;

        /// <summary>タイトルを出しているか（テスト用）。</summary>
        public bool ShowingTitle => _title != null && _title.activeSelf;

        /// <summary>メインメニューを出しているか（テスト用）。</summary>
        public bool ShowingMain => _main != null && _main.activeSelf;

        /// <summary>キャラクター選択・ステージ選択を出しているか（テスト用）。</summary>
        public bool ShowingCharacterSelect => _characterSelect != null && _characterSelect.gameObject.activeSelf;
        public bool ShowingStageSelect => _stageSelect != null && _stageSelect.gameObject.activeSelf;
        public bool ShowingLocalJoin => _localJoin != null && _localJoin.gameObject.activeSelf;

        public CharacterSelectScreen CharacterSelect => _characterSelect;
        public StageSelectScreen StageSelect => _stageSelect;
        public LocalJoinScreen LocalJoin => _localJoin;

        public MenuOption Single => _single;
        public MenuOption Local => _local;
        public MenuOption Network => _network;
        public MenuOption Training => _training;
        public MenuOption Options => _options;
        public MenuOption Quit => _quit;

        private void Awake()
        {
            if (_title == null || _main == null || _single == null || _local == null || _network == null || _training == null
                || _options == null || _quit == null || _characterSelect == null || _stageSelect == null || _localJoin == null)
            {
                Debug.LogError($"{name}: 画面か項目が設定されていません", this);
                enabled = false;
                return;
            }
            _single.SetAvailable(false, "COMING SOON (CPU)");
            _options.SetAvailable(false, "COMING SOON");
            _network.Clicked.AddListener(() => Run(flow => flow.StartOnline()));
            _training.Clicked.AddListener(() =>
            {
                _localMode = false;
                ShowCharacterSelect();
            });
            _local.Clicked.AddListener(() =>
            {
                _localMode = true;
                ShowLocalJoin(true);
            });
            _localJoin.Confirmed += (players, timed) =>
            {
                _localPlayers = players;
                _localTimed = timed;
                ShowStageSelect();
            };
            _localJoin.Cancelled += () => ShowMain(_local);
            _characterSelect.Confirmed += index =>
            {
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, index);
                ShowStageSelect();
            };
            _characterSelect.Cancelled += () => ShowMain(_training);
            _stageSelect.Confirmed += stage => Run(flow =>
            {
                if (_localMode)
                {
                    flow.StartLocal(stage, _localPlayers, _localTimed);
                }
                else
                {
                    flow.StartTraining(stage);
                }
            });
            _stageSelect.Cancelled += () =>
            {
                if (_localMode)
                {
                    ShowLocalJoin(false);
                }
                else
                {
                    ShowCharacterSelect();
                }
            };
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
            HideAll();
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
            UiSounds.Find().Play(SoundId.UiConfirm);
            ShowMain();
        }

        // メインメニューを出す（focus の項目、なければ使える最初の項目を選ぶ）。
        private void ShowMain(MenuOption focus = null)
        {
            HideAll();
            _main.SetActive(true);
            _flow?.MarkTitlePassed();
            _focus = focus;
            // タイトルを抜けたボタン（Enter や A）で、そのまま最初の項目まで決まらないよう、項目を選ぶのは次のフレーム。
            _selectAtFrame = Time.frameCount + 1;
        }

        private void ShowCharacterSelect()
        {
            HideAll();
            _characterSelect.Show(PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0));
        }

        private void ShowLocalJoin(bool reset)
        {
            HideAll();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            _localJoin.Show(reset);
        }

        private void ShowStageSelect()
        {
            HideAll();
            _stageSelect.Show();
        }

        private void HideAll()
        {
            _title.SetActive(false);
            _main.SetActive(false);
            _characterSelect.Hide();
            _stageSelect.Hide();
            _localJoin.Hide();
            _selectAtFrame = -1;
        }

        private void SelectFirst()
        {
            MenuOption first = _focus != null && _focus.Button.interactable
                ? _focus
                : Array.Find(new[] { _single, _local, _network, _training, _options, _quit }, item => item.Button.interactable);
            _focus = null;
            if (first != null)
            {
                first.SelectQuietly();
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
