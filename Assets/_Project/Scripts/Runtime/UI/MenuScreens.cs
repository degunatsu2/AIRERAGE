using System;
using System.Collections.Generic;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューのシーンの画面（docs/15 T6-1。D-54、D-65、D-69）:
    /// - タイトル: 「AIRERAGE」と「PRESS ANY BUTTON」（どれかのキー・ボタンでメインメニューへ。最初の 1 回だけ）。Esc では終了の確認
    ///   （QuitConfirmDialog。ユーザーの要望、2026-10-20）を出す。メインメニューの QUIT も、終了の確認で「はい」を選んだら終える。
    /// - メインメニュー: SINGLE、LOCAL、NETWORK、TRAINING、OPTIONS、QUIT（LOCAL ・ NETWORK は 2026-11-07 まで LOCAL VS ・ NETWORK VS）。キーボード（矢印・Enter・Esc）、ゲームパッド（スティック・十字キー・A・B）、マウスで選ぶ。戻るとタイトル。
    /// - TRAINING → キャラクター選択（T6-3）→ ステージ選択（T6-4）→ トレーニング。戻る操作で 1 つ前の画面へ。選んだキャラクターは
    ///   覚えておき（PlayerPrefs。オンラインの待機所と同じ）、トレーニングの試合の仕組みがそのキャラクターで始める。
    /// - キャラクター選択はアイコンを選ぶ形（CharacterSelectScreen。トレーニングは 1P だけ、ローカル対戦は機器ごとに参加。docs/15 6.9）。
    /// - NETWORK → ネットワーク対戦のメニュー（IOnlineMenu。Net のアセンブリにある。ホスト・参加 → 待機所 → キャラクター選択 →
    ///   ステージ選択 → 試合。docs/15 T6-7）。つながったまま試合から戻ったときは、最初からネットワーク対戦のメニューを開く。
    /// - LOCAL → 参加とキャラクター選択（T6-5）→ ステージ選択 → ローカル対戦。ステージ選択から戻ると、参加した人は
    ///   そのまま（READY だけ外す）。
    /// - OPTIONS → オプションの画面（OptionsScreen。音量・画面・キーの割り当て。docs/15 T6-9）。閉じるとメインメニューの OPTIONS へ。
    ///   RANDOM STAGES → ステージ選択の画面で、RANDOM で選ぶステージのチェックを付け外し（閉じるとオプションへ）。
    ///   ITEMS の START ITEMS ／ DROP ITEMS → アイテムを選ぶ画面（ItemSelectScreen。選ぶたびに ItemSettings に保存し、閉じるとオプションへ。
    ///   docs/20_item_plan.md）。
    /// - SINGLE（docs/16 T7-2。D-72【仮】）→ キャラクター選択（トレーニングと同じ 1 人の画面）→ CPU の強さ（CpuLevelScreen）→ ステージ選択 →
    ///   自分＋CPU 3 体のローカル対戦（CPU のキャラクターはランダム。なるべく自分と違うキャラクター）。自分はキーボード＋マウスとどの
    ///   ゲームパッドでも操作できる。
    /// 画面の移り変わりは App のシーンの GameFlow に頼む（App のシーンから始めないと、試合へは移らない）。
    /// 流す曲（Music）は出している画面で決まる: キャラクター選択・CPU の強さ・ステージ選択は選択の曲、ほかはタイトルの曲（docs/18 6 章）。
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
        [SerializeField] private OptionsScreen _optionsScreen;
        [SerializeField] private QuitConfirmDialog _quitDialog;
        [SerializeField] private CpuLevelScreen _cpuLevel;
        [Tooltip("アイテムを選ぶ画面（オプションの START ITEMS ／ DROP ITEMS と、ネットワーク対戦の待機所。なくてもよい）")]
        [SerializeField] private ItemSelectScreen _itemSelect;
        [Tooltip("ネットワーク対戦のメニュー（IOnlineMenu。なければ、NETWORK は今のオンラインの検証用のシーンを開く）")]
        [SerializeField] private MonoBehaviour _online;
        [Tooltip("画面の切り替えの演出（D-87。なければすぐ切り替える）")]
        [SerializeField] private ScreenTransition _transition;

        private GameFlow _flow;
        private IDisposable _anyButton;
        private float _shownAt;
        private int _selectAtFrame = -1;
        private MenuOption _focus;
        // ステージ選択の後に始めるもの（トレーニング・ローカル対戦・シングルモード）と、ローカル対戦に参加した人、シングルモードの CPU の強さ。
        private enum PlayMode
        {
            Training,
            Local,
            Single,
        }

        private PlayMode _mode;
        private IReadOnlyList<LocalPlayerSetup> _localPlayers;
        private bool _localTimed = true;
        private Airerage.Core.Ai.CpuLevel _level = Airerage.Core.Ai.CpuLevel.Normal;
        private bool _started;
        // アイテムを選ぶ画面を、オプションから開いているか。
        private bool _itemsFromOptions;

        /// <summary>タイトルを出しているか（テスト用）。</summary>
        public bool ShowingTitle => _title != null && _title.activeSelf;

        /// <summary>
        /// 今の画面で流す曲（App のシーンの MusicPlayer が見る）。キャラクター選択・CPU の強さ・ステージ選択（オプションの RANDOM STAGES は
        /// 除く）は選択の曲、ほかはタイトルの曲。最初の画面を出すまでは None（読み込んだ直後に、違う曲が一瞬鳴り始めないように）。
        /// </summary>
        public MusicPlayer.MenuTrack Music
        {
            get
            {
                if (!_started)
                {
                    return MusicPlayer.MenuTrack.None;
                }
                bool selecting = ShowingCharacterSelect || ShowingCpuLevel || (ShowingStageSelect && !_stageSelect.PoolMode);
                return selecting ? MusicPlayer.MenuTrack.Select : MusicPlayer.MenuTrack.Title;
            }
        }

        /// <summary>メインメニューを出しているか（テスト用）。</summary>
        public bool ShowingMain => _main != null && _main.activeSelf;

        /// <summary>キャラクター選択・ステージ選択を出しているか（テスト用）。</summary>
        public bool ShowingCharacterSelect => _characterSelect != null && _characterSelect.gameObject.activeSelf;
        public bool ShowingStageSelect => _stageSelect != null && _stageSelect.gameObject.activeSelf;

        /// <summary>CPU の強さの画面を出しているか（テスト用）。</summary>
        public bool ShowingCpuLevel => _cpuLevel != null && _cpuLevel.IsShowing;

        public CpuLevelScreen CpuLevel => _cpuLevel;

        /// <summary>アイテムを選ぶ画面（なければ null）。</summary>
        public ItemSelectScreen ItemSelect => _itemSelect;

        /// <summary>アイテムを選ぶ画面を、オプションから開いて出しているか（ネットワーク対戦のメニューは、このときは画面を隠さない）。</summary>
        public bool ShowingItemSelectFromOptions => _itemsFromOptions && ShowingItemSelect;

        /// <summary>アイテムを選ぶ画面を出しているか（テスト用）。</summary>
        public bool ShowingItemSelect => _itemSelect != null && _itemSelect.IsShowing;

        /// <summary>オプションの画面を出しているか（テスト用）。</summary>
        public bool ShowingOptions => _optionsScreen != null && _optionsScreen.IsShowing;

        public OptionsScreen OptionsScreen => _optionsScreen;

        /// <summary>終了の確認を出しているか（テスト用）。</summary>
        public bool ShowingQuitDialog => _quitDialog != null && _quitDialog.IsShowing;

        public QuitConfirmDialog QuitDialog => _quitDialog;

        public CharacterSelectScreen CharacterSelect => _characterSelect;
        public StageSelectScreen StageSelect => _stageSelect;

        public MenuOption Single => _single;
        public MenuOption Local => _local;
        public MenuOption Network => _network;
        public MenuOption Training => _training;
        public MenuOption Options => _options;
        public MenuOption Quit => _quit;

        private void Awake()
        {
            if (_title == null || _main == null || _single == null || _local == null || _network == null || _training == null
                || _options == null || _quit == null || _characterSelect == null || _stageSelect == null || _optionsScreen == null
                || _quitDialog == null || _cpuLevel == null)
            {
                Debug.LogError($"{name}: 画面か項目が設定されていません", this);
                enabled = false;
                return;
            }
            // 画面の切り替えの演出（D-87。ユーザーの決定、2026-11-07）: キャラクター選択へは斜めの札とアイコンのめくれ（SlantCascade）、
            // ほかは風（Gust。戻るときは向きが逆）。タイトルからはスカイダイブ（ContinueFromTitle）。
            _single.Clicked.AddListener(() => Go(TransitionKind.SlantCascade, 1, _single, () =>
            {
                _mode = PlayMode.Single;
                ShowCharacterSelect();
            }, CharacterSelectLabel));
            _cpuLevel.Confirmed += level => Go(TransitionKind.Gust, 1, null, () =>
            {
                _level = level;
                ShowStageSelect();
            });
            _cpuLevel.Cancelled += () => Go(TransitionKind.Gust, -1, null, ShowCharacterSelect);
            _options.Clicked.AddListener(() => Go(TransitionKind.Gust, 1, _options, () =>
            {
                HideAll();
                _optionsScreen.Show();
            }));
            _optionsScreen.Closed += () => Go(TransitionKind.Gust, -1, null, () => ShowMain(_options));
            // RANDOM で選ぶステージ（ステージ選択と同じ画面。閉じたらオプションの RANDOM STAGES へ戻る。ユーザーの要望、2026-10-26）。
            _optionsScreen.RandomStagesRequested += () => Go(TransitionKind.Gust, 1, null, () =>
            {
                HideAll();
                _stageSelect.ShowRandomPool();
            });
            _stageSelect.PoolClosed += () => Go(TransitionKind.Gust, -1, null, () =>
            {
                HideAll();
                _optionsScreen.ShowAtRandomStages();
            });
            if (_itemSelect != null)
            {
                // 初期アイテムとドロップするアイテム（オプションの ITEMS。ユーザーの要望、2026-11-05）。
                _optionsScreen.StartItemsRequested += () => Go(TransitionKind.Gust, 1, null, () =>
                {
                    HideAll();
                    Airerage.Runtime.Settings.ItemSettings items = Airerage.Runtime.Settings.ItemSettings.Load();
                    _itemsFromOptions = true;
                    _itemSelect.ShowPick(items.StartCount, items.StartItems);
                });
                _optionsScreen.DropItemsRequested += () => Go(TransitionKind.Gust, 1, null, () =>
                {
                    HideAll();
                    _itemsFromOptions = true;
                    _itemSelect.ShowPool(Airerage.Runtime.Settings.ItemSettings.Load().DropPool);
                });
                _itemSelect.PicksChanged += picks =>
                {
                    if (_itemsFromOptions)
                    {
                        Airerage.Runtime.Settings.ItemSettings items = Airerage.Runtime.Settings.ItemSettings.Load();
                        items.StartItems = new Airerage.Core.Items.ItemId[Airerage.Core.Items.ItemCatalog.BagSlots];
                        System.Array.Copy(picks, items.StartItems, Mathf.Min(picks.Length, items.StartItems.Length));
                        items.Save();
                    }
                };
                _itemSelect.PoolChanged += pool =>
                {
                    if (_itemsFromOptions)
                    {
                        Airerage.Runtime.Settings.ItemSettings items = Airerage.Runtime.Settings.ItemSettings.Load();
                        items.DropPool = pool;
                        items.Save();
                    }
                };
                _itemSelect.Closed += () =>
                {
                    if (!_itemsFromOptions)
                    {
                        return;
                    }
                    bool start = _itemSelect.LastMode == ItemSelectScreen.SelectMode.Pick;
                    Go(TransitionKind.Gust, -1, null, () =>
                    {
                        _itemsFromOptions = false;
                        HideAll();
                        _optionsScreen.ShowAtItems(start);
                    });
                };
            }
            _network.Clicked.AddListener(() =>
            {
                if (Online != null)
                {
                    Go(TransitionKind.Gust, 1, _network, () =>
                    {
                        HideAll();
                        Online.Open();
                    });
                }
                else
                {
                    Run(flow => flow.StartOnline());
                }
            });
            _training.Clicked.AddListener(() => Go(TransitionKind.SlantCascade, 1, _training, () =>
            {
                _mode = PlayMode.Training;
                ShowCharacterSelect();
            }, CharacterSelectLabel));
            _local.Clicked.AddListener(() => Go(TransitionKind.SlantCascade, 1, _local, () =>
            {
                _mode = PlayMode.Local;
                ShowLocalJoin(true);
            }, CharacterSelectLabel));
            _characterSelect.LocalConfirmed += (players, timed) => Go(TransitionKind.Gust, 1, null, () =>
            {
                _localPlayers = players;
                _localTimed = timed;
                ShowStageSelect();
            });
            _characterSelect.Confirmed += index => Go(TransitionKind.Gust, 1, null, () =>
            {
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, index);
                if (_mode == PlayMode.Single)
                {
                    ShowCpuLevel();
                }
                else
                {
                    ShowStageSelect();
                }
            });
            _characterSelect.Cancelled += () =>
            {
                // ネットワーク対戦のキャラクター選択の「戻る」は、ネットワーク対戦のメニューが受け持つ。
                if (_characterSelect.Mode == CharacterSelectScreen.SelectMode.Single)
                {
                    Go(TransitionKind.Gust, -1, null, () => ShowMain(_mode == PlayMode.Single ? _single : _training));
                }
                else if (_characterSelect.Mode == CharacterSelectScreen.SelectMode.Local)
                {
                    Go(TransitionKind.Gust, -1, null, () => ShowMain(_local));
                }
            };
            // カーソルのあるステージをメニューの背景に映す（ユーザーの要望、2026-10-19）。
            _stageSelect.HighlightedStageChanged += stage => _flow?.PreviewStage(stage);
            _stageSelect.Confirmed += stage => Run(flow =>
            {
                // ネットワーク対戦のステージ選択は、ネットワーク対戦のメニューが受け持つ。
                if (_stageSelect.NetworkMode)
                {
                    return;
                }
                switch (_mode)
                {
                    case PlayMode.Local:
                        flow.StartLocal(stage, _localPlayers, _localTimed);
                        break;
                    case PlayMode.Single:
                        flow.StartLocal(stage, SinglePlayers(PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0)), true, single: true);
                        break;
                    default:
                        flow.StartTraining(stage);
                        break;
                }
            });
            _stageSelect.Cancelled += () =>
            {
                if (_stageSelect.NetworkMode)
                {
                    return;
                }
                Go(TransitionKind.Gust, -1, null, () =>
                {
                    switch (_mode)
                    {
                        case PlayMode.Local:
                            ShowLocalJoin(false);
                            break;
                        case PlayMode.Single:
                            ShowCpuLevel();
                            break;
                        default:
                            ShowCharacterSelect();
                            break;
                    }
                });
            };
            _quit.Clicked.AddListener(() => _quitDialog.Show(() => ShowMain(_quit)));
            _quitDialog.Confirmed += () => Run(flow => flow.QuitGame());
            foreach (MenuOption item in new[] { _single, _local, _network, _training, _options, _quit })
            {
                item.Cancelled += () => Go(TransitionKind.Gust, -1, null, ShowTitle);
            }
        }

        private void Start()
        {
            _flow = FindFirstObjectByType<GameFlow>();
            if (_flow == null)
            {
                Debug.LogWarning($"{name}: GameFlow がありません（App のシーンから始めてください）。項目を選んでも画面は移りません", this);
            }
            bool selectReturn = _flow != null && _flow.ConsumeLocalSelectReturn();
            if (Online != null && Online.IsConnected)
            {
                // つながったまま試合から戻った（ネットワーク対戦のメニューが、今の段階の画面を出す）。
                HideAll();
                _flow?.MarkTitlePassed();
                Online.Open();
            }
            else if (selectReturn && _flow.LocalSingle)
            {
                // シングルモードの結果の「キャラクター選択へ」（docs/16 T7-2）。
                _flow.MarkTitlePassed();
                _mode = PlayMode.Single;
                ShowCharacterSelect();
            }
            else if (selectReturn)
            {
                // ローカル対戦の結果の「キャラクター選択へ」（同じ人とキャラクターで。docs/15 T6-8）。
                _flow.MarkTitlePassed();
                _mode = PlayMode.Local;
                HideAll();
                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                _characterSelect.ShowLocal(_flow.LocalPlayers, _flow.LocalTimed);
            }
            else if (_flow != null && _flow.TitlePassed)
            {
                ShowMain();
            }
            else
            {
                ShowTitle();
            }
            _started = true;
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
            _anyButton = InputSystem.onAnyButtonPress.CallOnce(OnTitleButton);
        }

        // タイトルで押したボタン: Esc なら終了の確認（いいえでタイトルへ戻る）、ほかはメインメニューへ。
        private void OnTitleButton(InputControl control)
        {
            if (control is KeyControl key && key.keyCode == Key.Escape)
            {
                _anyButton?.Dispose();
                _anyButton = null;
                _quitDialog.Show(ShowTitle);
                return;
            }
            ContinueFromTitle();
        }

        /// <summary>タイトルからメインメニューへ（テスト用にも公開）。</summary>
        public void ContinueFromTitle()
        {
            _anyButton?.Dispose();
            _anyButton = null;
            UiSounds.Find().Play(SoundId.UiTitleStart);
            Go(TransitionKind.SkyDive, 1, null, () => ShowMain());
        }

        // 画面の切り替えの見出し（キャラクター選択へ入るときの、斜めの札の文字）。
        private const string CharacterSelectLabel = "CHARACTER SELECT";

        /// <summary>画面の切り替えの演出（なければ null）。</summary>
        public ScreenTransition Transition => _transition;

        // swap（画面を入れ替える処理）を、kind の演出つきで行う（演出がなければすぐ）。origin は決定した項目。
        private void Go(TransitionKind kind, int direction, MenuOption origin, Action swap, string label = null)
        {
            if (_transition != null)
            {
                _transition.Run(kind, direction, origin != null ? (RectTransform)origin.transform : null, swap, label);
            }
            else
            {
                swap();
            }
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
            _characterSelect.ShowSingle(PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0));
        }

        private IOnlineMenu Online => _online as IOnlineMenu;

        /// <summary>ネットワーク対戦のメニューから、メインメニューの NETWORK へ戻る。</summary>
        public void ShowMainFromOnline()
        {
            ShowMain(_network);
        }

        /// <summary>ほかの画面（ネットワーク対戦のメニュー）が使う前に、メニューの画面を全部隠す。</summary>
        public void HideMenus()
        {
            HideAll();
        }

        private void ShowLocalJoin(bool reset)
        {
            HideAll();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
            _characterSelect.ShowLocal(reset);
        }

        private void ShowCpuLevel()
        {
            HideAll();
            _cpuLevel.Show();
        }

        /// <summary>
        /// シングルモードの参加者（docs/16 T7-2）: 自分（キーボード＋マウスとすべてのゲームパッド。キャラクター character）と CPU 3 体
        /// （今の強さ。キャラクターはランダムで、なるべく自分とも互いとも違うもの）。
        /// </summary>
        public List<LocalPlayerSetup> SinglePlayers(int character)
        {
            var devices = new List<InputDevice>();
            if (Keyboard.current != null)
            {
                devices.Add(Keyboard.current);
            }
            if (Mouse.current != null)
            {
                devices.Add(Mouse.current);
            }
            devices.AddRange(Gamepad.all);
            var players = new List<LocalPlayerSetup> { new LocalPlayerSetup(devices.ToArray(), "KEYBOARD / GAMEPAD", character) };
            int count = Math.Max(1, _characterSelect.Count);
            var pool = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (i != character)
                {
                    pool.Add(i);
                }
            }
            for (int k = 0; k < 3; k++)
            {
                if (pool.Count == 0)
                {
                    pool.Add(UnityEngine.Random.Range(0, count));
                }
                int pick = UnityEngine.Random.Range(0, pool.Count);
                players.Add(LocalPlayerSetup.Cpu(pool[pick], _level));
                pool.RemoveAt(pick);
            }
            return players;
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
            _cpuLevel.Hide();
            _optionsScreen.Hide();
            _quitDialog.Hide();
            if (_itemSelect != null && _itemsFromOptions)
            {
                _itemSelect.Hide();
            }
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
