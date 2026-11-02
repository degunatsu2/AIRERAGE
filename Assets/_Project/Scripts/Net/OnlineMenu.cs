using System.Collections.Generic;
using System.Text;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Airerage.Net
{
    /// <summary>
    /// ネットワーク対戦のメニュー（docs/15 T6-7。ユーザーの要望、2026-10-19）。メニューのシーンに置き、メインメニューの NETWORK VS から開く
    /// （IOnlineMenu）。接続は App のシーンの OnlineConnection が持ち、ここは画面だけを受け持つ。接続の段階に合わせて画面を出す:
    /// - 接続: 名前・参加コード・アドレス・ポートの入力と、HOST ONLINE ／ JOIN WITH CODE（Unity の Relay の参加コード。docs/15 T6-10）、
    ///   HOST (LAN) ／ JOIN (LAN)（IP アドレスとポート）、BACK。参加コードのサービスに問い合わせている間はボタンを押せない。
    /// - 待機所: 1P〜8P の札（名前と準備完了）、参加コード（コピーできる）、READY（もう一度で取り消し）、時間の制限（ホストだけ）、LEAVE。
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
        }

        [SerializeField] private MenuScreens _menu;
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
        [Tooltip("チーム対戦の列の見出し（RED・BLUE・GREEN・YELLOW）")]
        [SerializeField] private GameObject[] _teamHeaders = new GameObject[0];
        [SerializeField] private Text[] _teamHeaderTexts = new Text[0];
        [SerializeField] private MenuOption _ready;
        [SerializeField] private MenuOption _team;
        [SerializeField] private MenuOption _mode;
        [SerializeField] private MenuOption _split;
        [SerializeField] private MenuOption _shuffle;
        [SerializeField] private MenuOption _timeLimit;
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

        // 待機所の札の並べ方（個人戦は 1 列、チーム対戦は 4 色の列。基準の画面 1920×1080 の座標）。
        private const float SlotLeft = 110f;
        private const float SlotTop = -200f;
        private const float LaneWidth = 190f;
        private const float LaneGap = 8f;
        private const float LaneHeaderHeight = 50f;
        private const float LaneSlotHeight = 58f;

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
                || _teamHeaders.Length != Airerage.Core.Match.TeamRules.MaxTeams || _teamHeaderTexts.Length != _teamHeaders.Length)
            {
                Debug.LogError($"{name}: ネットワーク対戦のメニューの部品がそろっていません", this);
                enabled = false;
                return;
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
                    int count = Airerage.Core.Match.TeamRules.SizePatterns(Session.LobbyPlayerCount).Count;
                    _splitIndex = count > 0 ? (_splitIndex + 1) % count : 0;
                }
            });
            _shuffle.Clicked.AddListener(ShuffleTeams);
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
            foreach (MenuOption option in new[] { _ready, _team, _mode, _split, _shuffle, _timeLimit, _copyCode, _leave })
            {
                option.Cancelled += cancel;
            }
            for (int p = 0; p < _slotButtons.Length; p++)
            {
                int player = p;
                _slotButtons[p].Cancelled += cancel;
                _slotButtons[p].Clicked.AddListener(() => MoveToNextTeam(player));
            }
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

        // メインメニューの NETWORK VS へ戻る（接続はしない・もうない）。
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
                _connectMessage.text = "Start from the App scene to play online.";
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
                _connectMessage.text = "PORT MUST BE 0-65535";
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

        private void Show(Panel panel, OnlineSession session)
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
                    _copyCode.gameObject.SetActive(session.JoinCode.Length > 0);
                    _copiedCode = string.Empty;
                    _splitIndex = 0;
                    ShowWaitingOptions(session);
                    Focus(_ready);
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

        // 待機所の右の列の、ホストだけ・チーム対戦だけの項目を出し入れする（選んでいた項目が消えたら READY を選ぶ）。
        private void ShowWaitingOptions(OnlineSession session)
        {
            bool team = session.TeamMode;
            _shownTeamMode = team;
            _shownHost = session.IsHost;
            _team.gameObject.SetActive(team);
            _mode.gameObject.SetActive(session.IsHost);
            _split.gameObject.SetActive(session.IsHost && team);
            _shuffle.gameObject.SetActive(session.IsHost && team);
            _timeLimit.gameObject.SetActive(session.IsHost);
            // 出ている項目だけを上から詰めて並べる（項目の位置は MenuOption が覚えているので、作り直さずに y だけ変える）。
            float y = SlotTop;
            foreach (MenuOption option in new[] { _ready, _team, _mode, _split, _shuffle, _timeLimit, _copyCode, _leave })
            {
                if (!option.gameObject.activeSelf)
                {
                    continue;
                }
                option.SetHome(new Vector2(990f, y));
                y -= ((RectTransform)option.transform).sizeDelta.y + 10f;
            }
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null || !selected.activeInHierarchy)
            {
                Focus(_ready);
            }
        }

        // 札を決定した: チーム対戦なら、その人を次のチームへ（ホストはだれでも、ほかの人は自分だけ。D-78）。
        private void MoveToNextTeam(int player)
        {
            OnlineSession session = Session;
            if (session == null || !session.TeamMode || session.Phase != LobbyPhase.Waiting)
            {
                return;
            }
            IReadOnlyList<LobbySlot> roster = session.Roster;
            if (player >= roster.Count || !roster[player].Connected)
            {
                return;
            }
            int next = NextTeam(roster[player].Team);
            if (session.IsHost)
            {
                session.SetTeamOf(player, next);
            }
            else if (player == session.LocalPlayer)
            {
                session.LocalTeam = next;
            }
        }

        // ホスト: 選んだ人数配分のとおりに、ランダムに分ける（D-78）。
        private void ShuffleTeams()
        {
            OnlineSession session = Session;
            if (session == null || !session.IsHost || !session.TeamMode)
            {
                return;
            }
            List<int[]> patterns = Airerage.Core.Match.TeamRules.SizePatterns(session.LobbyPlayerCount);
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
            if (teamMode != _shownTeamMode || session.IsHost != _shownHost)
            {
                ShowWaitingOptions(session);
            }
            int connected = 0;
            var laneCounts = new int[Airerage.Core.Match.TeamRules.MaxTeams + 1];
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                LobbySlot slot = p < roster.Count ? roster[p] : default;
                connected += slot.Connected ? 1 : 0;
                Color color = p < _colors.Length ? _colors[p] : Color.white;
                bool movable = teamMode && slot.Connected && (session.IsHost || p == session.LocalPlayer);
                if (_slotButtons[p].Button.interactable != movable)
                {
                    _slotButtons[p].Button.interactable = movable;
                }
                bool selected = _slotButtons[p].IsSelected;
                if (teamMode)
                {
                    // チーム対戦: その人のチームの列に、番号の順に並べる（いない番号は出さない）。
                    bool shown = slot.Connected && slot.Team >= 1 && slot.Team <= Airerage.Core.Match.TeamRules.MaxTeams;
                    _slotPlates[p].gameObject.SetActive(shown);
                    if (!shown)
                    {
                        continue;
                    }
                    int lane = slot.Team - 1;
                    int row = laneCounts[slot.Team]++;
                    LayoutSlot(p, new Vector2(SlotLeft + lane * (LaneWidth + LaneGap), SlotTop - LaneHeaderHeight - 8f - row * (LaneSlotHeight + 6f)),
                        new Vector2(LaneWidth, LaneSlotHeight), true);
                    Color teamColor = Airerage.Runtime.Presentation.TeamColors.Of(slot.Team);
                    _slotPlates[p].color = JoinedFill;
                    _slotPlates[p].BorderColor = selected ? Color.white : slot.Ready ? teamColor : new Color(teamColor.r, teamColor.g, teamColor.b, 0.5f);
                    _slotNames[p].text = SlotLabel(session, slot, p, compact: true);
                    _slotNames[p].color = Color.white;
                    _slotStates[p].text = slot.Ready ? "READY!" : string.Empty;
                    _slotStates[p].color = teamColor;
                    continue;
                }
                _slotPlates[p].gameObject.SetActive(true);
                LayoutSlot(p, new Vector2(SlotLeft, SlotTop - p * 72f), new Vector2(780f, 64f), false);
                if (!slot.Connected)
                {
                    _slotPlates[p].color = EmptyFill;
                    _slotPlates[p].BorderColor = EmptyBorder;
                    _slotNames[p].text = "WAITING...";
                    _slotNames[p].color = new Color(1f, 1f, 1f, 0.45f);
                    _slotStates[p].text = string.Empty;
                    continue;
                }
                _slotPlates[p].color = JoinedFill;
                _slotPlates[p].BorderColor = slot.Ready ? color : new Color(color.r, color.g, color.b, 0.5f);
                _slotNames[p].text = SlotLabel(session, slot, p, compact: false);
                _slotNames[p].color = Color.white;
                _slotStates[p].text = slot.Ready ? "READY!" : "NOT READY";
                _slotStates[p].color = slot.Ready ? color : new Color(1f, 1f, 1f, 0.6f);
            }
            // 選んでいた札が消えた（その人が抜けた）ら、READY を選び直す（ゲームパッドで操作できなくならないように）。
            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (current != null && !current.activeInHierarchy && _selectAtFrame < 0)
            {
                Focus(_ready);
            }
            for (int t = 0; t < _teamHeaders.Length; t++)
            {
                _teamHeaders[t].SetActive(teamMode);
                if (teamMode)
                {
                    _teamHeaderTexts[t].text = $"{Airerage.Runtime.Presentation.TeamColors.NameOf(t + 1)}  {laneCounts[t + 1]}";
                }
            }
            _ready.SetLabel(session.LocalReady ? "CANCEL READY" : "READY");
            _team.SetLabel($"MY TEAM: {Airerage.Runtime.Presentation.TeamColors.NameOf(session.LocalTeam)}");
            _mode.SetLabel(teamMode ? "MODE: TEAM (UP TO 8)" : "MODE: FREE FOR ALL (UP TO 4)");
            List<int[]> patterns = Airerage.Core.Match.TeamRules.SizePatterns(connected);
            if (patterns.Count == 0)
            {
                _split.SetLabel("SPLIT: -");
                _shuffle.SetAvailable(false, "2+ PLAYERS");
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
            if (session.JoinCode.Length > 0)
            {
                _waitingInfo.text = session.IsHost
                    ? $"JOIN CODE: {session.JoinCode}   SHARE IT WITH YOUR FRIENDS (NETWORK VS > JOIN WITH CODE)."
                    : $"CONNECTED AS {session.LocalPlayer + 1}P.   JOIN CODE: {session.JoinCode}";
                _copyCode.SetLabel(_copiedCode == session.JoinCode ? "CODE COPIED!" : "COPY JOIN CODE");
            }
            else
            {
                _waitingInfo.text = session.IsHost
                    ? $"HOSTING ON PORT {session.Port}. FRIENDS JOIN WITH YOUR ADDRESS."
                    : $"CONNECTED AS {session.LocalPlayer + 1}P.";
            }
            string problem = session.WaitingProblem;
            if (problem != null)
            {
                _waitingMessage.text = problem;
                _waitingMessage.color = new Color(1f, 0.85f, 0.3f);
            }
            else
            {
                _waitingMessage.color = Color.white;
                _waitingMessage.text = connected < 2
                    ? (teamMode ? $"WAITING FOR PLAYERS (2-{OnlineSession.MaxPlayers})" : $"WAITING FOR PLAYERS (2-{OnlineSession.MaxFreeForAllPlayers})")
                    : teamMode && session.IsHost
                        ? "WHEN EVERYONE IS READY, CHARACTER SELECT STARTS.  SELECT A PLAYER TO MOVE THEM TO THE NEXT TEAM"
                        : "WHEN EVERYONE IS READY, CHARACTER SELECT STARTS";
            }
        }

        // 札の名前の行（名前、ホスト・自分の印）。列の札（compact）は短く。
        private static string SlotLabel(OnlineSession session, LobbySlot slot, int p, bool compact)
        {
            string label = string.IsNullOrEmpty(slot.Name) ? $"P{p + 1}" : slot.Name;
            var marks = new StringBuilder(label);
            if (p == 0)
            {
                marks.Append(compact ? " (H)" : "  (HOST)");
            }
            if (p == session.LocalPlayer)
            {
                marks.Append(compact ? " (YOU)" : "  (YOU)");
            }
            return marks.ToString();
        }

        // 札 p を置く（左上 topLeft、大きさ size。列の札は番号を小さく、名前を上の段、READY を下の段に）。
        private void LayoutSlot(int p, Vector2 topLeft, Vector2 size, bool lane)
        {
            var rect = (RectTransform)_slotPlates[p].transform;
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
                number.sizeDelta = new Vector2(90f, 56f);
                name.anchoredPosition = new Vector2(130f, 0f);
                name.sizeDelta = new Vector2(420f, 56f);
                _slotNames[p].resizeTextMaxSize = 28;
                _slotStates[p].fontSize = 26;
                state.anchoredPosition = new Vector2(-40f, 0f);
                state.sizeDelta = new Vector2(240f, 56f);
            }
        }

        private void RefreshCharacterSelect(OnlineSession session)
        {
            IReadOnlyList<LobbySlot> roster = session.Roster;
            var waiting = new List<string>();
            for (int p = 0; p < OnlineSession.MaxPlayers && p < roster.Count; p++)
            {
                LobbySlot slot = roster[p];
                _characterSelect.SetRemote(p, slot.Connected, slot.Name, slot.Character, slot.Ready);
                if (slot.Connected && !slot.Ready)
                {
                    waiting.Add(string.IsNullOrEmpty(slot.Name) ? $"{p + 1}P" : slot.Name);
                }
            }
            _characterSelect.SetLocalReady(session.LocalReady);
            _characterSelect.SetNetworkMessage(waiting.Count > 0
                ? $"WAITING FOR: {string.Join(", ", waiting)}"
                : "EVERYONE IS READY");
        }
    }
}
