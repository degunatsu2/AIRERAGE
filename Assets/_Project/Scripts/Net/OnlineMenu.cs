using System.Collections.Generic;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Airerage.Net
{
    /// <summary>
    /// ネットワーク対戦のメニュー（docs/15 T6-7。ユーザーの要望、2026-10-19）。メニューのシーンに置き、メインメニューの NETWORK から開く
    /// （IOnlineMenu）。接続は App のシーンの OnlineConnection が持ち、ここは画面だけを受け持つ。接続の段階に合わせて画面を出す:
    /// - 接続: 名前・参加コード・アドレス・ポートの入力と、HOST ONLINE ／ JOIN WITH CODE（Unity の Relay の参加コード。docs/15 T6-10）、
    ///   HOST (LAN) ／ JOIN (LAN)（IP アドレスとポート）、BACK。参加コードのサービスに問い合わせている間はボタンを押せない。
    /// - 待機所: 1P〜8P の札（名前と準備完了。ホストと自分は名前の前の印で示す）、参加コード（コピーできる）、READY（もう一度で取り消し）、時間の制限（ホストだけ）、LEAVE。
    ///   2 人以上が全員 READY で、全員がキャラクター選択へ進む。チーム対戦（docs/19 T9-5。D-78〜D-79）: ホストが MODE で個人戦（4 人まで）と
    ///   チーム対戦（8 人まで）を切り替える。チーム対戦では札を 4 色の列（RED・BLUE・GREEN・YELLOW）に並べ、各自が MY TEAM で自分の
    ///   チームを選ぶ。ホストは札を選んで決定すると、その人を次のチームへ移せる。SPLIT で人数配分を選び、RANDOM SPLIT でその配分のとおりに
    ///   ランダムに分ける。チームが 2 つに満たないと進めない（個人戦で 5 人以上も）。
    /// - キャラクター選択: アイコンを選ぶ画面（CharacterSelectScreen のネットワーク対戦の使い方）。自分の枠をこの PC の機器で動かし、
    ///   ほかの人の枠は届いた値で出す。全員が決定でステージ選択へ。READY でないときの戻るで抜ける。
    /// - ステージ選択: ホストが選ぶ（カーソルを全員へ送る）。ほかの人はホストのカーソルを見る。ホストの決定で全員が読み込んで始まる。
    /// 切れたら接続の画面に戻り、理由を出す。
    /// </summary>
    public sealed class OnlineMenu : MonoBehaviour, IOnlineMenu
    {
        private enum Panel
        {
            None,
            Connect,
            Waiting,
            CharacterSelect,
            StageSelect,
            Loading,

            // アイテムを選ぶ画面（待機所に入ったまま。docs/20_item_plan.md）。
            Items,
        }

        [SerializeField] private MenuScreens _menu;
        [Tooltip("画面の切り替えの演出（D-87。なければすぐ切り替える）")]
        [SerializeField] private ScreenTransition _transition;
        [SerializeField] private CharacterSelectScreen _characterSelect;
        [SerializeField] private StageSelectScreen _stageSelect;
        [SerializeField] private StageCatalog _stages;
        [Header("接続")]
        [SerializeField] private GameObject _connectPanel;
        [SerializeField] private InputField _name;
        [SerializeField] private InputField _code;
        [SerializeField] private InputField _address;
        [SerializeField] private InputField _port;
        [SerializeField] private MenuOption _hostOnline;
        [SerializeField] private MenuOption _joinOnline;
        [SerializeField] private MenuOption _host;
        [SerializeField] private MenuOption _join;
        [SerializeField] private MenuOption _back;
        [SerializeField] private Text _connectMessage;
        [Header("待機所")]
        [SerializeField] private GameObject _waitingPanel;
        [SerializeField] private Text _waitingInfo;
        [SerializeField] private SlantedPlate[] _slotPlates = new SlantedPlate[0];
        [SerializeField] private PlateButton[] _slotButtons = new PlateButton[0];
        [SerializeField] private Text[] _slotNumbers = new Text[0];
        [SerializeField] private Text[] _slotNames = new Text[0];
        [SerializeField] private Text[] _slotStates = new Text[0];
        [Tooltip("札の名前の前の印: ホスト（金色の王冠）と自分（水色の人の形）。文字の「(HOST)」「(YOU)」の代わり（2026-11-03 の要望）")]
        [SerializeField] private RawImage[] _slotHostIcons = new RawImage[0];
        [SerializeField] private RawImage[] _slotYouIcons = new RawImage[0];
        [Tooltip("列の見出し（チーム対戦は RED・BLUE・GREEN・YELLOW。個人戦は 1 つ目を PLAYERS に。決定でそこに入る）")]
        [SerializeField] private GameObject[] _teamHeaders = new GameObject[0];
        [SerializeField] private SlantedPlate[] _teamHeaderPlates = new SlantedPlate[0];
        [SerializeField] private PlateButton[] _teamHeaderButtons = new PlateButton[0];
        [SerializeField] private Text[] _teamHeaderTexts = new Text[0];
        [Tooltip("観戦の待機所の見出し（右の端の列。決定でそこへ移る）")]
        [SerializeField] private PlateButton _spectatorHeader;
        [SerializeField] private Text _spectatorHeaderText;
        [Tooltip("参加する人の札と見出しを並べる、縦にスクロールする場所の中身と、そのスクロール")]
        [SerializeField] private RectTransform _slotContent;
        [SerializeField] private ScrollRect _slotScroll;
        [SerializeField] private MenuOption _ready;
        [SerializeField] private MenuOption _team;
        [SerializeField] private MenuOption _mode;
        [SerializeField] private MenuOption _split;
        [SerializeField] private MenuOption _shuffle;
        [SerializeField] private MenuOption _timeLimit;
        [Tooltip("アイテムのルール（ホストが決める。docs/20_item_plan.md）: モード、初期アイテム数・初期アイテム選択（プリアイテム）、ドロップ頻度・ドロップアイテム選択（アイテムドロップ）")]
        [SerializeField] private MenuOption _itemMode;
        [SerializeField] private MenuOption _itemCount;
        [SerializeField] private MenuOption _itemPick;
        [SerializeField] private MenuOption _dropRate;
        [SerializeField] private MenuOption _dropPool;
        [Tooltip("アイテムを選ぶ画面（メニューの画面のものを使う）")]
        [SerializeField] private ItemSelectScreen _itemSelect;
        [SerializeField] private MenuOption _copyCode;
        [SerializeField] private MenuOption _leave;
        [SerializeField] private Text _waitingMessage;
        [Tooltip("プレイヤーごとの色（1P〜4P）")]
        [SerializeField] private Color[] _colors = new Color[0];
        [SerializeField] private GameObject _loadingPanel;

        private static readonly Color EmptyFill = new Color(0.03f, 0.05f, 0.09f, 0.45f);
        private static readonly Color JoinedFill = new Color(0.03f, 0.05f, 0.09f, 0.85f);
        private static readonly Color EmptyBorder = new Color(1f, 1f, 1f, 0.2f);

        private OnlineConnection _connection;
        private bool _open;
        private Panel _shown = Panel.None;
        private string _lastStatus = string.Empty;
        private int _selectAtFrame = -1;
        private MenuOption _focus;
        private string _copiedCode = string.Empty;
        private int _splitIndex;
        private bool _shownTeamMode;
        private bool _shownHost;
        private bool _shownSpectator;
        private bool _shownLate;
        private LobbyPhase _shownPhase;
        private Airerage.Core.Items.ItemMode _shownItemMode;
        // アイテムを選ぶ画面を開いているか（ドロップするアイテムか、初期アイテムか）と、閉じた後に選ぶ項目。
        private bool _itemsOpen;
        private bool _itemsPool;
        private MenuOption _focusAfterItems;

        // 待機所の札の並べ方（基準の画面 1920×1080）。参加する人の札と見出しはスクロールの中身の座標（左上が 0）、観戦の列は待機所の座標。
        private const float SlotTop = -200f;

        // 待機所の右の列の項目の高さと間隔（多いときは OptionArea に収まるように詰める）。
        private const float OptionHeight = 64f;
        private const float OptionStep = 74f;
        private const float OptionArea = 590f;
        private const float RowWidth = 780f;
        private const float HeaderHeight = 46f;
        private const float RowHeight = 58f;
        private const float RowGap = 8f;
        private const float TeamGap = 14f;
        private const float LaneHeaderHeight = 50f;
        private const float LaneSlotHeight = 58f;
        private const float SpectatorLaneLeft = 1600f;
        private const float SpectatorLaneWidth = 280f;
        private static readonly Color PlayersColor = new Color(0.35f, 0.85f, 1f);
        private static readonly Color SpectatorColor = new Color(0.75f, 0.75f, 0.8f);
        private static readonly Color YouColor = new Color(0.35f, 0.85f, 1f);
        private Texture2D _hostTexture;
        private Texture2D _youTexture;

        /// <summary>開いているか（テスト用）。</summary>
        public bool IsOpen => _open;

        /// <summary>今出している画面（テスト用）。</summary>
        public string ShownPanel => _shown.ToString();

        public bool IsConnected => Connection != null && Connection.IsConnected;

        // App のシーンの接続（メインメニューの Start から先に聞かれることがあるので、使うときに探す）。
        private OnlineConnection Connection => _connection != null ? _connection : _connection = FindFirstObjectByType<OnlineConnection>();

        public MenuOption HostButton => _host;
        public MenuOption JoinButton => _join;
        public MenuOption HostOnlineButton => _hostOnline;
        public MenuOption JoinOnlineButton => _joinOnline;
        public MenuOption CopyCodeButton => _copyCode;
        public InputField CodeField => _code;

        /// <summary>今の接続の画面の文（テスト用）。</summary>
        public string ConnectMessage => _connectMessage != null ? _connectMessage.text : string.Empty;

        /// <summary>待機所の上の文（参加コードなど。テスト用）。</summary>
        public string WaitingInfo => _waitingInfo != null ? _waitingInfo.text : string.Empty;
        public MenuOption ReadyButton => _ready;
        public MenuOption TeamButton => _team;
        public MenuOption ModeButton => _mode;
        public MenuOption SplitButton => _split;
        public MenuOption ShuffleButton => _shuffle;
        public MenuOption ItemModeButton => _itemMode;
        public MenuOption ItemCountButton => _itemCount;
        public MenuOption ItemPickButton => _itemPick;
        public MenuOption DropRateButton => _dropRate;
        public MenuOption DropPoolButton => _dropPool;

        /// <summary>列の見出し（t 番目。決定でそのチームに入る。個人戦は 0 番が PLAYERS。テスト用）と、観戦の待機所の見出し。</summary>
        public PlateButton TeamHeader(int t) => _teamHeaderButtons[t];
        public PlateButton SpectatorHeader => _spectatorHeader;

        /// <summary>待機所の p 番の札（チーム対戦で選んで決定すると次のチームへ。テスト用）。</summary>
        public PlateButton SlotButton(int p) => _slotButtons[p];

        /// <summary>待機所の p 番の札の左上の位置（基準の画面の座標。テスト用）。</summary>
        public Vector2 SlotPosition(int p) => ((RectTransform)_slotPlates[p].transform).anchoredPosition;

        /// <summary>待機所の下の文（進めない理由など。テスト用）。</summary>
        public string WaitingMessage => _waitingMessage != null ? _waitingMessage.text : string.Empty;
        public InputField NameField => _name;
        public InputField AddressField => _address;
        public InputField PortField => _port;

        private OnlineSession Session => Connection != null ? Connection.Session : null;

        private void Awake()
        {
            if (_menu == null || _characterSelect == null || _stageSelect == null || _stages == null || _connectPanel == null
                || _name == null || _code == null || _address == null || _port == null || _hostOnline == null || _joinOnline == null
                || _host == null || _join == null || _back == null
                || _waitingPanel == null || _ready == null || _timeLimit == null || _copyCode == null || _leave == null
                || _team == null || _mode == null || _split == null || _shuffle == null
                || _slotPlates.Length != OnlineSession.MaxPlayers || _slotNames.Length != OnlineSession.MaxPlayers
                || _slotStates.Length != OnlineSession.MaxPlayers || _slotButtons.Length != OnlineSession.MaxPlayers
                || _slotNumbers.Length != OnlineSession.MaxPlayers
                || _slotHostIcons.Length != OnlineSession.MaxPlayers || _slotYouIcons.Length != OnlineSession.MaxPlayers
                || _teamHeaders.Length != Airerage.Core.Match.TeamRules.MaxTeams || _teamHeaderTexts.Length != _teamHeaders.Length
                || _teamHeaderPlates.Length != _teamHeaders.Length || _teamHeaderButtons.Length != _teamHeaders.Length
                || _spectatorHeader == null || _spectatorHeaderText == null || _slotContent == null || _slotScroll == null
                || _itemMode == null || _itemCount == null || _itemPick == null || _dropRate == null || _dropPool == null || _itemSelect == null)
            {
                Debug.LogError($"{name}: ネットワーク対戦のメニューの部品がそろっていません", this);
                enabled = false;
                return;
            }
            // ホスト・自分の印の絵（コードで描く）。
            _hostTexture = Airerage.Runtime.Presentation.GuideIcons.Crown(64);
            _youTexture = Airerage.Runtime.Presentation.GuideIcons.Person(64, YouColor);
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                _slotHostIcons[p].texture = _hostTexture;
                _slotYouIcons[p].texture = _youTexture;
                _slotHostIcons[p].gameObject.SetActive(false);
                _slotYouIcons[p].gameObject.SetActive(false);
            }
            // 参加コードは英数字の大文字で入れる（小文字で打っても大文字にする）。
            _code.onValidateInput = (text, index, c) => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : '\0';
            _hostOnline.Clicked.AddListener(HostOnline);
            _joinOnline.Clicked.AddListener(JoinOnline);
            _host.Clicked.AddListener(Host);
            _join.Clicked.AddListener(Join);
            _back.Clicked.AddListener(Close);
            foreach (MenuOption option in new[] { _hostOnline, _joinOnline, _host, _join, _back })
            {
                option.Cancelled += Close;
            }
            _ready.Clicked.AddListener(() =>
            {
                if (Session != null)
                {
                    Session.LocalReady = !Session.LocalReady;
                }
            });
            _timeLimit.Clicked.AddListener(() =>
            {
                if (Connection != null)
                {
                    Connection.Timed = !Connection.Timed;
                }
            });
            _copyCode.Clicked.AddListener(() =>
            {
                if (Session != null && Session.JoinCode.Length > 0)
                {
                    GUIUtility.systemCopyBuffer = Session.JoinCode;
                    _copiedCode = Session.JoinCode;
                }
            });
            _leave.Clicked.AddListener(Leave);
            _team.Clicked.AddListener(() =>
            {
                if (Session != null && Session.TeamMode)
                {
                    Session.LocalTeam = NextTeam(Session.LocalTeam);
                }
            });
            _mode.Clicked.AddListener(() =>
            {
                if (Session != null && Session.IsHost)
                {
                    Session.TeamMode = !Session.TeamMode;
                }
            });
            _split.Clicked.AddListener(() =>
            {
                if (Session != null && Session.IsHost)
                {
                    int count = Airerage.Core.Match.TeamRules.SizePatterns(Session.LobbyParticipantCount).Count;
                    _splitIndex = count > 0 ? (_splitIndex + 1) % count : 0;
                }
            });
            _shuffle.Clicked.AddListener(ShuffleTeams);
            WireItems();
            System.Action cancel = () =>
            {
                // 戻る操作: READY なら取り消し、そうでなければ抜ける。
                if (Session != null && Session.LocalReady)
                {
                    Session.LocalReady = false;
                }
                else
                {
                    Leave();
                }
            };
            foreach (MenuOption option in WaitingOptions())
            {
                option.Cancelled += cancel;
            }
            for (int p = 0; p < _slotButtons.Length; p++)
            {
                int player = p;
                _slotButtons[p].Cancelled += cancel;
                _slotButtons[p].Clicked.AddListener(() => MoveToNextTeam(player));
            }
            for (int t = 0; t < _teamHeaderButtons.Length; t++)
            {
                int team = t + 1;
                _teamHeaderButtons[t].Cancelled += cancel;
                _teamHeaderButtons[t].Clicked.AddListener(() => JoinFromHeader(team));
            }
            _spectatorHeader.Cancelled += cancel;
            _spectatorHeader.Clicked.AddListener(() =>
            {
                if (Session != null && Session.Phase == LobbyPhase.Waiting)
                {
                    Session.LocalSpectator = true;
                }
            });
            _characterSelect.NetworkHovered += character =>
            {
                if (_open && Session != null)
                {
                    Session.LocalCharacter = character;
                    PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, character);
                }
            };
            _characterSelect.NetworkReadyChanged += ready =>
            {
                if (_open && Session != null)
                {
                    Session.LocalReady = ready;
                }
            };
            _characterSelect.Cancelled += () =>
            {
                if (_open && _characterSelect.Mode == CharacterSelectScreen.SelectMode.Network)
                {
                    Leave();
                }
            };
            _stageSelect.HighlightedIndexChanged += index =>
            {
                if (_open && Session != null && Session.IsHost)
                {
                    Session.StageCursor = index;
                }
            };
            _stageSelect.Confirmed += stage =>
            {
                if (_open && _stageSelect.NetworkMode && Session != null && Session.IsHost && Session.State == OnlineSession.SessionState.Lobby)
                {
                    Connection.StartMatch(_stages.IndexOf(stage));
                }
            };
            _stageSelect.Cancelled += () =>
            {
                if (_open && _stageSelect.NetworkMode && Session != null && Session.IsHost)
                {
                    Session.BackToCharacterSelect();
                }
            };
            HidePanels();
        }

        // 待機所の右の列の項目（上からの順）。
        private MenuOption[] WaitingOptions()
        {
            return new[] { _ready, _team, _mode, _split, _shuffle, _itemMode, _itemCount, _itemPick, _dropRate, _dropPool, _timeLimit, _copyCode, _leave };
        }

        // アイテムの項目と、アイテムを選ぶ画面の操作をつなぐ（docs/20_item_plan.md）。
        private void WireItems()
        {
            _itemMode.Clicked.AddListener(() =>
            {
                if (Session != null && Session.IsHost)
                {
                    LobbyItems items = Session.Items;
                    Session.Items = items.WithMode((Airerage.Core.Items.ItemMode)(((int)items.Mode + 1) % 3));
                }
            });
            _itemCount.Clicked.AddListener(() =>
            {
                if (Session != null && Session.IsHost)
                {
                    LobbyItems items = Session.Items;
                    Session.Items = items.WithStartCount(items.StartCount % Airerage.Core.Items.ItemCatalog.BagSlots + 1);
                }
            });
            _dropRate.Clicked.AddListener(() =>
            {
                if (Session != null && Session.IsHost)
                {
                    LobbyItems items = Session.Items;
                    Session.Items = items.WithDropRate((items.DropRate + 1) % 3);
                }
            });
            _itemPick.Clicked.AddListener(() => OpenItems(false));
            _dropPool.Clicked.AddListener(() =>
            {
                if (Session != null && Session.IsHost)
                {
                    OpenItems(true);
                }
            });
            _itemSelect.PicksChanged += picks =>
            {
                if (!_itemsOpen || Session == null)
                {
                    return;
                }
                Session.LocalStartItems = picks;
                // 次の初めの値にもする（オプションの START ITEMS と同じ設定）。
                ItemSettings saved = ItemSettings.Load();
                saved.StartItems = new Airerage.Core.Items.ItemId[Airerage.Core.Items.ItemCatalog.BagSlots];
                System.Array.Copy(picks, saved.StartItems, Mathf.Min(picks.Length, saved.StartItems.Length));
                saved.Save();
            };
            _itemSelect.PoolChanged += pool =>
            {
                if (!_itemsOpen || Session == null || !Session.IsHost)
                {
                    return;
                }
                Session.Items = Session.Items.WithPool(pool);
                ItemSettings saved = ItemSettings.Load();
                saved.DropPool = pool;
                saved.Save();
            };
            _itemSelect.Closed += () =>
            {
                if (!_itemsOpen)
                {
                    return;
                }
                CloseItems();
                // 待機所を出し直して、開いた項目を選ぶ。
                _shown = Panel.None;
            };
        }

        // アイテムを選ぶ画面を開く（待機所に入ったまま。状態は「アイテム選択中……」）。
        private void OpenItems(bool pool)
        {
            OnlineSession session = Session;
            if (session == null || session.Phase != LobbyPhase.Waiting)
            {
                return;
            }
            _itemsOpen = true;
            _itemsPool = pool;
            _focusAfterItems = pool ? _dropPool : _itemPick;
            session.LocalItemSelecting = true;
        }

        private void CloseItems()
        {
            _itemsOpen = false;
            if (Session != null)
            {
                Session.LocalItemSelecting = false;
            }
        }

        private void OnDestroy()
        {
            if (_hostTexture != null)
            {
                Destroy(_hostTexture);
            }
            if (_youTexture != null)
            {
                Destroy(_youTexture);
            }
        }

        private void Start()
        {
            _name.characterLimit = OnlineSession.MaxNameLength;
            _name.text = PlayerPrefs.GetString(OnlineConnection.PlayerNameKey, string.Empty);
            _address.text = PlayerPrefs.GetString(OnlineConnection.AddressKey, "127.0.0.1");
            _port.text = PlayerPrefs.GetInt(OnlineConnection.PortKey, OnlineConnection.DefaultPort).ToString();
        }

        /// <summary>開く（つながっていなければ接続の画面、つながっていれば今の段階の画面）。</summary>
        public void Open()
        {
            _open = true;
            _shown = Panel.None;
            _menu.HideMenus();
            UpdatePanels();
        }

        // メインメニューの NETWORK へ戻る（接続はしない・もうない）。
        private void Close()
        {
            Connection?.Leave();
            _open = false;
            HidePanels();
            _shown = Panel.None;
            _menu.ShowMainFromOnline();
        }

        /// <summary>参加コードでホストになる（Relay。docs/15 T6-10。テスト用にも公開）。</summary>
        public async void HostOnline()
        {
            if (!CanConnect())
            {
                return;
            }
            SavePreferences();
            if (!await Connection.HostOnline(_name.text, PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0)))
            {
                _lastStatus = Connection.Message;
            }
        }

        /// <summary>入力欄の参加コードのホストに参加する（Relay。docs/15 T6-10。テスト用にも公開）。</summary>
        public async void JoinOnline()
        {
            if (!CanConnect())
            {
                return;
            }
            SavePreferences();
            if (!await Connection.JoinOnline(_code.text, _name.text, PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0)))
            {
                _lastStatus = Connection.Message;
            }
        }

        // 接続を始められるか（App のシーンから始めていて、参加コードのサービスに問い合わせていない）。
        private bool CanConnect()
        {
            if (Connection == null)
            {
                _connectMessage.text = Loc.T("Start from the App scene to play online.", "ネットワーク対戦は App のシーンから始めてください");
                return false;
            }
            return !Connection.Busy;
        }

        /// <summary>ホストとして待ち受ける（ポートは入力欄。テスト用にも公開）。</summary>
        public void Host()
        {
            if (!TryReadPort(out ushort port))
            {
                return;
            }
            SavePreferences();
            if (!Connection.Host(port, _name.text, PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0)))
            {
                _connectMessage.text = Connection.Message;
            }
        }

        /// <summary>入力欄のアドレスとポートのホストに参加する（テスト用にも公開）。</summary>
        public void Join()
        {
            if (!TryReadPort(out ushort port))
            {
                return;
            }
            SavePreferences();
            if (!Connection.Join(_address.text.Trim(), port, _name.text, PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0)))
            {
                _connectMessage.text = Connection.Message;
            }
        }

        // 接続を閉じて、接続の画面へ。
        private void Leave()
        {
            _lastStatus = string.Empty;
            Connection?.Leave();
        }

        private bool TryReadPort(out ushort port)
        {
            if (!CanConnect())
            {
                port = 0;
                return false;
            }
            if (!ushort.TryParse(_port.text.Trim(), out port))
            {
                _connectMessage.text = Loc.T("PORT MUST BE 0-65535", "ポートは 0〜65535 で入れてください");
                return false;
            }
            return true;
        }

        private void SavePreferences()
        {
            PlayerPrefs.SetString(OnlineConnection.PlayerNameKey, OnlineSession.SanitizeName(_name.text));
            PlayerPrefs.SetString(OnlineConnection.AddressKey, _address.text.Trim());
            if (ushort.TryParse(_port.text.Trim(), out ushort port))
            {
                PlayerPrefs.SetInt(OnlineConnection.PortKey, port);
            }
        }

        private void Update()
        {
            if (!_open)
            {
                return;
            }
            if (_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame)
            {
                _selectAtFrame = -1;
                if (_focus != null && _focus.isActiveAndEnabled)
                {
                    _focus.SelectQuietly();
                }
            }
            UpdatePanels();
        }

        private void UpdatePanels()
        {
            OnlineSession session = Session;
            if (session != null && session.State == OnlineSession.SessionState.Ended)
            {
                // 切れた（ホストが抜けた、満員、など）。理由を残して接続の画面へ。
                _lastStatus = session.Status;
                Connection.Leave();
                session = null;
            }
            Panel want = Desired(session);
            if (_itemsOpen)
            {
                if (want == Panel.Waiting && session != null && session.Phase == LobbyPhase.Waiting && !session.LocalLate)
                {
                    want = Panel.Items;
                }
                else
                {
                    // 待機所の段階でなくなった（全員の準備完了など）: アイテムを選ぶ画面を閉じる。
                    CloseItems();
                }
            }
            if (want != _shown)
            {
                Show(want, session);
            }
            switch (_shown)
            {
                case Panel.Connect:
                    bool busy = Connection != null && Connection.Busy;
                    _connectMessage.text = busy ? Connection.Message : session != null ? session.Status : _lastStatus;
                    foreach (MenuOption option in new[] { _hostOnline, _joinOnline, _host, _join })
                    {
                        if (option.Button.interactable == busy)
                        {
                            option.SetAvailable(!busy);
                        }
                    }
                    break;
                case Panel.Waiting:
                    RefreshWaitingRoom(session);
                    break;
                case Panel.CharacterSelect:
                    RefreshCharacterSelect(session);
                    break;
                case Panel.StageSelect:
                    if (!session.IsHost)
                    {
                        _stageSelect.ShowRemoteCursor(session.StageCursor);
                    }
                    break;
            }
        }

        private void OnGUI()
        {
            // キャラクター選択・ステージ選択・待機所の間に、途中から人が入った知らせ（左下の黄色い文字。2026-11-03）。
            if (_open && Session != null)
            {
                JoinNoticeHud.Draw(Session.JoinNotices);
            }
        }

        private static Panel Desired(OnlineSession session)
        {
            if (session == null || session.State == OnlineSession.SessionState.Connecting)
            {
                return Panel.Connect;
            }
            if (session.State != OnlineSession.SessionState.Lobby)
            {
                return Panel.Loading;
            }
            if (session.LocalSpectator || session.LocalLate)
            {
                // 観戦の人と、途中から入って待っている人は、キャラクター選択とステージ選択の間も待機所で待つ（docs/19）。
                return Panel.Waiting;
            }
            switch (session.Phase)
            {
                case LobbyPhase.CharacterSelect:
                    return Panel.CharacterSelect;
                case LobbyPhase.StageSelect:
                    return Panel.StageSelect;
                default:
                    return Panel.Waiting;
            }
        }

        // 画面の切り替え（D-87。2026-11-07）: 待機所からキャラクター選択へは斜めの札とアイコンのめくれ、ほかは風。前の段階へ戻るときは
        // 向きが逆。メインメニューから開いたとき（前の画面がない）は、メインメニューの切り替えの中で出す。
        private void Show(Panel panel, OnlineSession session)
        {
            Panel previous = _shown;
            if (_transition != null && previous != Panel.None && previous != panel && !_transition.InSwap)
            {
                TransitionKind kind = previous == Panel.Waiting && panel == Panel.CharacterSelect ? TransitionKind.SlantCascade : TransitionKind.Gust;
                int direction = Order(panel) >= Order(previous) ? 1 : -1;
                // 斜めの札の 1 枚目は NETWORK のイメージカラー（メインメニューの NETWORK の項目の色。2026-11-07）。
                Color? accent = _menu != null && _menu.Network != null ? _menu.Network.Accent : (Color?)null;
                _transition.Run(kind, direction, null, () => ShowNow(panel, session), "CHARACTER SELECT", accent);
                return;
            }
            ShowNow(panel, session);
        }

        // 画面の流れの順（進むか戻るかを決める）。
        private static int Order(Panel panel)
        {
            switch (panel)
            {
                case Panel.Connect:
                    return 0;
                case Panel.Waiting:
                    return 1;
                case Panel.Items:
                    return 2;
                case Panel.CharacterSelect:
                    return 3;
                case Panel.StageSelect:
                    return 4;
                case Panel.Loading:
                    return 5;
                default:
                    return 0;
            }
        }

        private void ShowNow(Panel panel, OnlineSession session)
        {
            HidePanels();
            _shown = panel;
            switch (panel)
            {
                case Panel.Connect:
                    _connectPanel.SetActive(true);
                    Focus(session != null ? _back : _hostOnline);
                    break;
                case Panel.Waiting:
                    _waitingPanel.SetActive(true);
                    _copiedCode = string.Empty;
                    _splitIndex = 0;
                    ShowWaitingOptions(session);
                    MenuOption back = _focusAfterItems;
                    _focusAfterItems = null;
                    Focus(back != null && back.gameObject.activeSelf ? back : _ready.gameObject.activeSelf ? _ready : _leave);
                    break;
                case Panel.Items:
                    if (_itemsPool)
                    {
                        _itemSelect.ShowPool(session.Items.Pool);
                    }
                    else
                    {
                        _itemSelect.ShowPick(session.Items.StartCount, session.LocalStartItems);
                    }
                    break;
                case Panel.CharacterSelect:
                    _characterSelect.ShowNetwork(session.LocalPlayer, session.LocalCharacter);
                    break;
                case Panel.StageSelect:
                    _stageSelect.ShowNetwork(session.IsHost, session.StageCursor, session.IsHost ? session.CommonStagePool : ulong.MaxValue);
                    break;
                case Panel.Loading:
                    if (_loadingPanel != null)
                    {
                        _loadingPanel.SetActive(true);
                    }
                    break;
            }
        }

        private void HidePanels()
        {
            _connectPanel.SetActive(false);
            _waitingPanel.SetActive(false);
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
            if (_characterSelect.Mode == CharacterSelectScreen.SelectMode.Network)
            {
                _characterSelect.Hide();
            }
            if (_stageSelect.NetworkMode)
            {
                _stageSelect.Hide();
            }
            if (_itemSelect.IsShowing && !_menu.ShowingItemSelectFromOptions)
            {
                _itemSelect.Hide();
            }
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        // 開いたボタンでそのまま決まらないよう、項目を選ぶのは次のフレーム。
        private void Focus(MenuOption option)
        {
            _focus = option;
            _selectAtFrame = Time.frameCount + 1;
        }

        // 待機所の右の列の、ホストだけ・チーム対戦だけ・参加する人だけの項目を出し入れする（選んでいた項目が消えたら READY を選ぶ）。
        // 待機所の段階でなければ（観戦の人が、キャラクター選択の間に見ている）、LEAVE だけ。
        private void ShowWaitingOptions(OnlineSession session)
        {
            bool team = session.TeamMode;
            bool waiting = session.Phase == LobbyPhase.Waiting && !session.LocalLate;
            bool spectator = session.LocalSpectator;
            _shownLate = session.LocalLate;
            _shownTeamMode = team;
            _shownHost = session.IsHost;
            _shownSpectator = spectator;
            _shownPhase = session.Phase;
            Airerage.Core.Items.ItemMode itemMode = session.Items.Mode;
            _shownItemMode = itemMode;
            _ready.gameObject.SetActive(waiting && !spectator);
            // アイテムのルール（docs/20_item_plan.md）: モードはだれでも見える（決めるのはホスト）。プリアイテムなら初期アイテム数と
            // 初期アイテム選択、アイテムドロップならドロップ頻度とドロップアイテム選択。
            _itemMode.gameObject.SetActive(waiting);
            _itemCount.gameObject.SetActive(waiting && itemMode == Airerage.Core.Items.ItemMode.PreItem);
            _itemPick.gameObject.SetActive(waiting && itemMode == Airerage.Core.Items.ItemMode.PreItem && !spectator);
            _dropRate.gameObject.SetActive(waiting && itemMode == Airerage.Core.Items.ItemMode.ItemDrop);
            _dropPool.gameObject.SetActive(waiting && itemMode == Airerage.Core.Items.ItemMode.ItemDrop);
            _team.gameObject.SetActive(waiting && team);
            _mode.gameObject.SetActive(waiting && session.IsHost);
            _split.gameObject.SetActive(waiting && session.IsHost && team);
            _shuffle.gameObject.SetActive(waiting && session.IsHost && team);
            _timeLimit.gameObject.SetActive(waiting && session.IsHost);
            _copyCode.gameObject.SetActive(session.JoinCode.Length > 0);
            // 出ている項目だけを上から詰めて並べる（項目の位置は MenuOption が覚えているので、作り直さずに y だけ変える）。項目が多いときは、
            // 下の文に重ならないように、項目を低くして詰める（アイテムの項目を足して、最大 13。2026-11-05）。
            int shown = 0;
            foreach (MenuOption option in WaitingOptions())
            {
                shown += option.gameObject.activeSelf ? 1 : 0;
            }
            float step = Mathf.Min(OptionStep, OptionArea / Mathf.Max(1, shown));
            float height = Mathf.Min(OptionHeight, step - 8f);
            float y = SlotTop;
            foreach (MenuOption option in WaitingOptions())
            {
                if (!option.gameObject.activeSelf)
                {
                    continue;
                }
                var rect = (RectTransform)option.transform;
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
                option.SetHome(new Vector2(990f, y));
                y -= step;
            }
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null || !selected.activeInHierarchy)
            {
                Focus(_ready.gameObject.activeSelf ? _ready : _leave);
            }
        }

        // 札を決定した: その人を次の席へ（ホストはだれでも、ほかの人は自分だけ。D-78）。チーム対戦は RED → BLUE → GREEN → YELLOW →
        // 観戦 → RED、個人戦は参加 ↔ 観戦（ホスト自身も観戦にできる。2026-11-03）。
        private void MoveToNextTeam(int player)
        {
            OnlineSession session = Session;
            if (session == null || session.Phase != LobbyPhase.Waiting)
            {
                return;
            }
            IReadOnlyList<LobbySlot> roster = session.Roster;
            if (player >= roster.Count || !roster[player].Connected || (!session.IsHost && player != session.LocalPlayer))
            {
                return;
            }
            LobbySlot slot = roster[player];
            if (!session.TeamMode)
            {
                SetSpectator(session, player, !slot.Spectator);
                return;
            }
            if (slot.Spectator)
            {
                SetTeam(session, player, 1);
            }
            else if (slot.Team >= Airerage.Core.Match.TeamRules.MaxTeams)
            {
                SetSpectator(session, player, true);
            }
            else
            {
                SetTeam(session, player, NextTeam(slot.Team));
            }
        }

        // 見出しを決定した: 自分がそのチームに入る（個人戦の PLAYERS なら、観戦から参加へ）。
        private void JoinFromHeader(int team)
        {
            OnlineSession session = Session;
            if (session == null || session.Phase != LobbyPhase.Waiting)
            {
                return;
            }
            if (session.TeamMode)
            {
                session.LocalTeam = team;
            }
            else if (team == 1)
            {
                session.LocalSpectator = false;
            }
        }

        private static void SetTeam(OnlineSession session, int player, int team)
        {
            if (session.IsHost)
            {
                session.SetTeamOf(player, team);
            }
            else
            {
                session.LocalTeam = team;
            }
        }

        private static void SetSpectator(OnlineSession session, int player, bool spectate)
        {
            if (session.IsHost)
            {
                session.SetSpectatorOf(player, spectate);
            }
            else
            {
                session.LocalSpectator = spectate;
            }
        }

        // ホスト: 選んだ人数配分のとおりに、ランダムに分ける（D-78。観戦の人は分けない）。
        private void ShuffleTeams()
        {
            OnlineSession session = Session;
            if (session == null || !session.IsHost || !session.TeamMode)
            {
                return;
            }
            List<int[]> patterns = Airerage.Core.Match.TeamRules.SizePatterns(session.LobbyParticipantCount);
            if (patterns.Count == 0)
            {
                return;
            }
            // 分け方の種は、押すたびに変わればよい（結果はロビーのメッセージで全員へ送るので、全員で同じ値を使う必要はない）。
            session.SplitTeams(patterns[_splitIndex % patterns.Count], System.Environment.TickCount ^ Time.frameCount);
        }

        private static int NextTeam(int team)
        {
            return team % Airerage.Core.Match.TeamRules.MaxTeams + 1;
        }

        private void RefreshWaitingRoom(OnlineSession session)
        {
            IReadOnlyList<LobbySlot> roster = session.Roster;
            bool teamMode = session.TeamMode;
            bool late = session.LocalLate;
            bool waiting = session.Phase == LobbyPhase.Waiting && !late;
            bool running = session.MatchRunning;
            if (teamMode != _shownTeamMode || session.IsHost != _shownHost || session.LocalSpectator != _shownSpectator
                || session.Phase != _shownPhase || late != _shownLate || session.Items.Mode != _shownItemMode)
            {
                ShowWaitingOptions(session);
            }
            int players = 0;
            int spectators = 0;
            var teamMembers = new List<int>[Airerage.Core.Match.TeamRules.MaxTeams + 1];
            for (int t = 0; t < teamMembers.Length; t++)
            {
                teamMembers[t] = new List<int>();
            }
            var playerSlots = new List<int>();
            var emptySlots = new List<int>();
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                LobbySlot slot = p < roster.Count ? roster[p] : default;
                bool movable = waiting && slot.Connected && !slot.Late && (session.IsHost || p == session.LocalPlayer);
                if (_slotButtons[p].Button.interactable != movable)
                {
                    _slotButtons[p].Button.interactable = movable;
                }
                if (!slot.Connected)
                {
                    _slotPlates[p].gameObject.SetActive(false);
                    emptySlots.Add(p);
                    continue;
                }
                _slotPlates[p].gameObject.SetActive(true);
                if (slot.Spectator)
                {
                    // 観戦の待機所（右の端の列。スクロールの外）。
                    LayoutSlot(p, (RectTransform)_waitingPanel.transform,
                        new Vector2(SpectatorLaneLeft, SlotTop - LaneHeaderHeight - 8f - spectators * (LaneSlotHeight + 6f)),
                        new Vector2(SpectatorLaneWidth, LaneSlotHeight), true);
                    spectators++;
                    StyleSlot(session, slot, p, SpectatorColor, compact: true);
                    continue;
                }
                players += slot.Late ? 0 : 1;
                if (teamMode)
                {
                    teamMembers[Mathf.Clamp(slot.Team, 1, Airerage.Core.Match.TeamRules.MaxTeams)].Add(p);
                }
                else
                {
                    playerSlots.Add(p);
                }
            }

            // 参加する人の札と見出しを、スクロールの中身に上から並べる。
            float y = 0f;
            var layoutWaiting = new List<int>();
            if (teamMode)
            {
                // チーム対戦: RED・BLUE・GREEN・YELLOW を縦に。人が入ったチームは、その人数分だけ下へ広がる（2026-11-03 の要望）。
                for (int t = 1; t <= Airerage.Core.Match.TeamRules.MaxTeams; t++)
                {
                    PlaceHeader(t - 1, y, Airerage.Runtime.Presentation.TeamColors.Of(t),
                        $"{Airerage.Runtime.Presentation.TeamColors.NameOf(t)}  {teamMembers[t].Count}", true);
                    y -= HeaderHeight + RowGap;
                    foreach (int p in teamMembers[t])
                    {
                        LayoutSlot(p, _slotContent, new Vector2(0f, y), new Vector2(RowWidth, RowHeight), false);
                        StyleSlot(session, roster[p], p, Airerage.Runtime.Presentation.TeamColors.Of(t), compact: false);
                        y -= RowHeight + RowGap;
                    }
                    y -= TeamGap;
                }
            }
            else
            {
                // 個人戦: PLAYERS の見出しの下に 1 列。4 人の枠まで、空いた番号の札で「WAITING...」を出す。
                PlaceHeader(0, y, PlayersColor, $"PLAYERS  {players} / {OnlineSession.MaxFreeForAllPlayers}", false);
                for (int t = 1; t < _teamHeaders.Length; t++)
                {
                    _teamHeaders[t].SetActive(false);
                }
                y -= HeaderHeight + RowGap;
                foreach (int p in playerSlots)
                {
                    LayoutSlot(p, _slotContent, new Vector2(0f, y), new Vector2(RowWidth, RowHeight), false);
                    StyleSlot(session, roster[p], p, p < _colors.Length ? _colors[p] : Color.white, compact: false);
                    y -= RowHeight + RowGap;
                }
                for (int i = 0; i < emptySlots.Count && playerSlots.Count + i < OnlineSession.MaxFreeForAllPlayers; i++)
                {
                    int p = emptySlots[i];
                    _slotPlates[p].gameObject.SetActive(true);
                    LayoutSlot(p, _slotContent, new Vector2(0f, y), new Vector2(RowWidth, RowHeight), false);
                    _slotPlates[p].color = EmptyFill;
                    _slotPlates[p].BorderColor = EmptyBorder;
                    _slotNames[p].text = Loc.T("WAITING...", "待機中……");
                    _slotNames[p].color = new Color(1f, 1f, 1f, 0.45f);
                    PlaceMarks(p, false, false, false);
                    _slotStates[p].text = string.Empty;
                    y -= RowHeight + RowGap;
                }
            }
            // 中身の高さ（はみ出したらスクロールできる）と、選んでいる札・見出しを見える所へ。
            _slotContent.sizeDelta = new Vector2(_slotContent.sizeDelta.x, -y);
            KeepSelectedVisible();

            // 選んでいた札が消えた（その人が抜けた）ら、READY を選び直す（ゲームパッドで操作できなくならないように）。
            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if ((current == null || !current.activeInHierarchy) && _selectAtFrame < 0)
            {
                Focus(_ready.gameObject.activeSelf ? _ready : _leave);
            }
            RefreshHeaderButtons(session, teamMode, waiting, spectators);
            _ready.SetLabel(session.LocalReady ? "CANCEL READY" : "READY");
            _team.SetLabel(session.LocalSpectator ? "MY TEAM: -" : $"MY TEAM: {Airerage.Runtime.Presentation.TeamColors.NameOf(session.LocalTeam)}");
            _mode.SetLabel(teamMode ? "MODE: TEAM" : "MODE: FREE FOR ALL");
            List<int[]> patterns = Airerage.Core.Match.TeamRules.SizePatterns(players);
            if (patterns.Count == 0)
            {
                _split.SetLabel("SPLIT: -");
                if (_shuffle.Button.interactable)
                {
                    _shuffle.SetAvailable(false, "2+ PLAYERS");
                }
            }
            else
            {
                int[] sizes = patterns[_splitIndex % patterns.Count];
                _split.SetLabel($"SPLIT: {string.Join(" VS ", sizes)}");
                if (!_shuffle.Button.interactable)
                {
                    _shuffle.SetAvailable(true);
                }
            }
            _timeLimit.SetLabel($"TIME LIMIT: {(Connection.Timed ? "ON" : "OFF")}");
            RefreshItemOptions(session);
            if (session.JoinCode.Length > 0)
            {
                _waitingInfo.text = session.IsHost
                    ? Loc.T($"JOIN CODE: {session.JoinCode}   SHARE IT WITH YOUR FRIENDS (NETWORK > JOIN WITH CODE).",
                        $"参加コード: {session.JoinCode}　友だちに伝えてください（NETWORK > JOIN WITH CODE で入力）")
                    : Loc.T($"CONNECTED AS {session.LocalPlayer + 1}P.   JOIN CODE: {session.JoinCode}",
                        $"{session.LocalPlayer + 1}P として参加中　参加コード: {session.JoinCode}");
                _copyCode.SetLabel(_copiedCode == session.JoinCode ? "CODE COPIED!" : "COPY JOIN CODE");
            }
            else
            {
                _waitingInfo.text = session.IsHost
                    ? Loc.T($"HOSTING ON PORT {session.Port}. FRIENDS JOIN WITH YOUR ADDRESS.",
                        $"ポート {session.Port} で待ち受け中　友だちはあなたのアドレスで参加できます")
                    : Loc.T($"CONNECTED AS {session.LocalPlayer + 1}P.", $"{session.LocalPlayer + 1}P として参加中");
            }
            _waitingMessage.text = ComposeWaitingMessage(session, teamMode, waiting, running, players, out bool problem);
            _waitingMessage.color = problem ? new Color(1f, 0.85f, 0.3f) : Color.white;
        }

        // アイテムの項目の文字と、ホストだけが選べる項目（ほかの人は見るだけ）。
        private void RefreshItemOptions(OnlineSession session)
        {
            LobbyItems items = session.Items;
            _itemMode.SetLabel(Loc.T($"ITEMS: {ItemSettings.ModeLabel(items.Mode)}", $"アイテム: {ItemSettings.ModeLabel(items.Mode)}"));
            _itemCount.SetLabel(Loc.T($"START ITEM COUNT: {items.StartCount}", $"初期アイテム数: {items.StartCount}"));
            int picked = Mathf.Min(session.LocalStartItems.Count, items.StartCount);
            _itemPick.SetLabel(Loc.T($"SELECT START ITEMS ({picked}/{items.StartCount})", $"初期アイテム選択（{picked}/{items.StartCount}）"));
            _dropRate.SetLabel(Loc.T($"DROP RATE: {ItemSettings.RateLabel(items.DropRate)}", $"ドロップ頻度: {ItemSettings.RateLabel(items.DropRate)}"));
            int pool = Airerage.Core.Items.ItemCatalog.CountIn(items.Pool);
            _dropPool.SetLabel(Loc.T($"SELECT DROP ITEMS ({pool}/{Airerage.Core.Items.ItemCatalog.Count})",
                $"ドロップアイテム選択（{pool}/{Airerage.Core.Items.ItemCatalog.Count}）"));
            foreach (MenuOption hostOnly in new[] { _itemMode, _itemCount, _dropRate, _dropPool })
            {
                if (hostOnly.Button.interactable != session.IsHost)
                {
                    hostOnly.SetAvailable(session.IsHost, "HOST");
                }
            }
        }

        // 待機所の下の文（進めない理由、待っている人数、観戦・途中から入った人の案内）。
        private static string ComposeWaitingMessage(OnlineSession session, bool teamMode, bool waiting, bool running, int players, out bool problem)
        {
            problem = false;
            if (session.LocalLate)
            {
                return running
                    ? Loc.T("A MATCH IS IN PROGRESS. WHEN EVERYONE IS BACK IN THE ROOM, SELECT A TEAM (OR PLAYERS) TO JOIN",
                        "対戦中です。全員が待機所に戻ったら、チーム（または PLAYERS）を選ぶと参加できます")
                    : Loc.T("THE PLAYERS ARE CHOOSING. WHEN EVERYONE IS BACK IN THE ROOM, SELECT A TEAM (OR PLAYERS) TO JOIN",
                        "選択中です。全員が待機所に戻ったら、チーム（または PLAYERS）を選ぶと参加できます");
            }
            if (!waiting)
            {
                // 観戦の人が、キャラクター選択・ステージ選択の間に見ている。
                return session.Phase == LobbyPhase.CharacterSelect
                    ? Loc.T("SPECTATING: THE PLAYERS ARE CHOOSING CHARACTERS", "観戦中: プレイヤーがキャラクターを選んでいます")
                    : Loc.T("SPECTATING: THE HOST IS CHOOSING THE STAGE", "観戦中: ホストがステージを選んでいます");
            }
            string reason = session.WaitingProblem;
            if (reason != null)
            {
                problem = true;
                return reason.StartsWith("FREE FOR ALL")
                    ? Loc.T(reason, $"個人戦は {OnlineSession.MaxFreeForAllPlayers} 人までです。MODE でチーム対戦にしてください")
                    : Loc.T(reason, "2 つ以上のチームに分かれてください");
            }
            if (players < 2)
            {
                int max = teamMode ? OnlineSession.MaxPlayers : OnlineSession.MaxFreeForAllPlayers;
                return Loc.T($"WAITING FOR PLAYERS (2-{max})", $"プレイヤーを待っています（2〜{max} 人）");
            }
            if (session.LocalSpectator)
            {
                return Loc.T("YOU ARE SPECTATING. SELECT A TEAM (OR PLAYERS) TO PLAY", "観戦中です。チーム（または PLAYERS）を選ぶと参加できます");
            }
            return teamMode && session.IsHost
                ? Loc.T("WHEN EVERYONE IS READY, CHARACTER SELECT STARTS.  SELECT A PLAYER TO MOVE THEM",
                    "全員が READY でキャラクター選択へ進みます。プレイヤーを選ぶと別のチームへ移せます")
                : Loc.T("WHEN EVERYONE IS READY, CHARACTER SELECT STARTS.  SELECT A TEAM TO JOIN IT",
                    "全員が READY でキャラクター選択へ進みます。チームを選ぶとそのチームに入ります");
        }

        // 札 p の色と文字（名前の行、状態の行）。状態は、試合中なら「対戦中……」、選択中なら「選択中……」、途中から入った人は「待機中」、
        // 観戦の人は「観戦」、待機所では READY（2026-11-03 の要望）。
        private void StyleSlot(OnlineSession session, LobbySlot slot, int p, Color color, bool compact)
        {
            bool selected = _slotButtons[p].IsSelected;
            _slotPlates[p].color = JoinedFill;
            _slotNames[p].text = SlotLabel(slot, p);
            _slotNames[p].color = Color.white;
            PlaceMarks(p, p == session.HostSlot, p == session.LocalPlayer, compact);
            string state;
            bool bright;
            if (slot.Late)
            {
                state = Loc.T("WAITING", "待機中");
                bright = false;
            }
            else if (slot.Spectator)
            {
                state = session.MatchRunning ? Loc.T("WATCHING...", "観戦中……") : Loc.T("WATCHING", "観戦");
                bright = false;
            }
            else if (session.MatchRunning)
            {
                state = Loc.T("IN MATCH...", "対戦中……");
                bright = true;
            }
            else if (session.Phase != LobbyPhase.Waiting)
            {
                state = Loc.T("SELECTING...", "選択中……");
                bright = true;
            }
            else if (slot.ItemSelecting)
            {
                // アイテムを選ぶ画面を開いている（待機所に入ったまま。docs/20_item_plan.md）。
                state = Loc.T("CHOOSING ITEMS...", "アイテム選択中……");
                bright = true;
            }
            else
            {
                state = slot.Ready ? "READY!" : compact ? string.Empty : "NOT READY";
                bright = slot.Ready;
            }
            _slotPlates[p].BorderColor = selected ? Color.white : bright ? color : new Color(color.r, color.g, color.b, 0.5f);
            _slotStates[p].text = state;
            _slotStates[p].color = bright ? color : new Color(1f, 1f, 1f, 0.6f);
        }

        // 見出し index（スクロールの中身の y に置く）の色・文字・大きさ。
        private void PlaceHeader(int index, float y, Color color, string text, bool shown)
        {
            _teamHeaders[index].SetActive(true);
            var rect = (RectTransform)_teamHeaders[index].transform;
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(RowWidth, HeaderHeight);
            bool selected = _teamHeaderButtons[index].IsSelected;
            _teamHeaderPlates[index].color = new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, selected ? 1f : 0.9f);
            _teamHeaderPlates[index].BorderColor = selected ? Color.white : color;
            _teamHeaderTexts[index].color = color;
            ((RectTransform)_teamHeaderTexts[index].transform).sizeDelta = new Vector2(RowWidth - 20f, HeaderHeight - 4f);
            _teamHeaderTexts[index].text = text;
        }

        // 見出しを選べるか: 自分が入れるチーム・PLAYERS（今いる所は選べない）と、観戦の列（待機所の段階だけ）。
        private void RefreshHeaderButtons(OnlineSession session, bool teamMode, bool waiting, int spectators)
        {
            for (int t = 0; t < _teamHeaders.Length; t++)
            {
                bool here = teamMode ? !session.LocalSpectator && session.LocalTeam == t + 1 : !session.LocalSpectator;
                bool usable = waiting && !here && (teamMode || t == 0);
                if (_teamHeaderButtons[t].Button.interactable != usable)
                {
                    _teamHeaderButtons[t].Button.interactable = usable;
                }
            }
            bool spectatorSelected = _spectatorHeader.IsSelected;
            var spectatorPlate = _spectatorHeader.GetComponent<SlantedPlate>();
            if (spectatorPlate != null)
            {
                spectatorPlate.BorderColor = spectatorSelected ? Color.white : SpectatorColor;
            }
            _spectatorHeaderText.text = $"SPECTATORS  {spectators}";
            bool canWatch = waiting && !session.LocalSpectator;
            if (_spectatorHeader.Button.interactable != canWatch)
            {
                _spectatorHeader.Button.interactable = canWatch;
            }
        }

        // 選んでいる札・見出しがスクロールの中身にあれば、見える所までスクロールする（ゲームパッドで下のチームへ動いたとき）。
        private void KeepSelectedVisible()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null || selected.transform.parent != _slotContent)
            {
                return;
            }
            var item = (RectTransform)selected.transform;
            float view = ((RectTransform)_slotScroll.transform).rect.height;
            float top = -item.anchoredPosition.y;
            float bottom = top + item.rect.height;
            Vector2 position = _slotContent.anchoredPosition;
            if (top < position.y)
            {
                position.y = top;
            }
            else if (bottom > position.y + view)
            {
                position.y = bottom - view;
            }
            _slotContent.anchoredPosition = position;
        }

        // 札の名前の行（名前。ホスト・自分は文字ではなく、名前の前の印 PlaceMarks で示す）。
        private static string SlotLabel(LobbySlot slot, int p)
        {
            return string.IsNullOrEmpty(slot.Name) ? $"P{p + 1}" : slot.Name;
        }

        /// <summary>札 p の名前の前に出しているホストの印・自分の印（テスト用）。</summary>
        public bool HostMarkShown(int p) => _slotHostIcons[p].gameObject.activeSelf;
        public bool YouMarkShown(int p) => _slotYouIcons[p].gameObject.activeSelf;

        // 札 p の名前の前にホストの印（王冠）・自分の印（人の形）を並べ、その分だけ名前を右へずらす（LayoutSlot の後に呼ぶ。
        // 「(HOST)」「(YOU)」の文字が札を圧迫していた。2026-11-03 のユーザーの要望）。
        private void PlaceMarks(int p, bool host, bool you, bool lane)
        {
            float size = lane ? 22f : 32f;
            float gap = lane ? 4f : 6f;
            var name = (RectTransform)_slotNames[p].transform;
            float x = name.anchoredPosition.x;
            float y = name.anchoredPosition.y;
            foreach ((RawImage icon, bool shown) in new[] { (_slotHostIcons[p], host), (_slotYouIcons[p], you) })
            {
                if (icon.gameObject.activeSelf != shown)
                {
                    icon.gameObject.SetActive(shown);
                }
                if (!shown)
                {
                    continue;
                }
                var rect = (RectTransform)icon.transform;
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(size, size);
                x += size + gap;
            }
            float shift = x - name.anchoredPosition.x;
            if (shift > 0f)
            {
                name.anchoredPosition = new Vector2(x, y);
                name.sizeDelta = new Vector2(Mathf.Max(40f, name.sizeDelta.x - shift), name.sizeDelta.y);
            }
        }

        // 札 p を parent の中の topLeft に、大きさ size で置く（列の札 lane は番号を小さく、名前を上の段、状態を下の段に）。
        private void LayoutSlot(int p, RectTransform parent, Vector2 topLeft, Vector2 size, bool lane)
        {
            var rect = (RectTransform)_slotPlates[p].transform;
            if (rect.parent != parent)
            {
                rect.SetParent(parent, false);
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            var number = (RectTransform)_slotNumbers[p].transform;
            var name = (RectTransform)_slotNames[p].transform;
            var state = (RectTransform)_slotStates[p].transform;
            if (lane)
            {
                _slotNumbers[p].fontSize = 24;
                number.anchoredPosition = new Vector2(16f, 0f);
                number.sizeDelta = new Vector2(44f, 40f);
                name.anchoredPosition = new Vector2(60f, 9f);
                name.sizeDelta = new Vector2(size.x - 72f, 30f);
                _slotNames[p].resizeTextMaxSize = 20;
                _slotStates[p].fontSize = 16;
                state.anchoredPosition = new Vector2(-14f, -14f);
                state.sizeDelta = new Vector2(size.x - 72f, 22f);
            }
            else
            {
                _slotNumbers[p].fontSize = 34;
                number.anchoredPosition = new Vector2(34f, 0f);
                number.sizeDelta = new Vector2(90f, 54f);
                name.anchoredPosition = new Vector2(130f, 0f);
                name.sizeDelta = new Vector2(420f, 54f);
                _slotNames[p].resizeTextMaxSize = 28;
                _slotStates[p].fontSize = 26;
                state.anchoredPosition = new Vector2(-40f, 0f);
                state.sizeDelta = new Vector2(240f, 54f);
            }
        }

        private void RefreshCharacterSelect(OnlineSession session)
        {
            IReadOnlyList<LobbySlot> roster = session.Roster;
            var waiting = new List<string>();
            for (int p = 0; p < OnlineSession.MaxPlayers && p < roster.Count; p++)
            {
                LobbySlot slot = roster[p];
                // 観戦の人は、キャラクター選択の枠に出さない。
                _characterSelect.SetRemote(p, slot.Plays, slot.Name, slot.Character, slot.Ready);
                if (slot.Plays && !slot.Ready)
                {
                    waiting.Add(string.IsNullOrEmpty(slot.Name) ? $"{p + 1}P" : slot.Name);
                }
            }
            _characterSelect.SetLocalReady(session.LocalReady);
            _characterSelect.SetNetworkMessage(waiting.Count > 0
                ? Loc.T($"WAITING FOR: {string.Join(", ", waiting)}", $"決定を待っています: {string.Join("、", waiting)}")
                : Loc.T("EVERYONE IS READY", "全員が決定しました"));
        }
    }
}
