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
    /// - 待機所: 1P〜4P の札（名前と準備完了）、参加コード（コピーできる）、READY（もう一度で取り消し）、時間の制限（ホストだけ）、LEAVE。
    ///   2 人以上が全員 READY で、
    ///   全員がキャラクター選択へ進む。
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
        [SerializeField] private Text[] _slotNames = new Text[0];
        [SerializeField] private Text[] _slotStates = new Text[0];
        [SerializeField] private MenuOption _ready;
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
                || _slotPlates.Length != OnlineSession.MaxPlayers || _slotNames.Length != OnlineSession.MaxPlayers
                || _slotStates.Length != OnlineSession.MaxPlayers)
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
            foreach (MenuOption option in new[] { _ready, _timeLimit, _copyCode, _leave })
            {
                option.Cancelled += () =>
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
                    _timeLimit.gameObject.SetActive(session.IsHost);
                    _copyCode.gameObject.SetActive(session.JoinCode.Length > 0);
                    _copiedCode = string.Empty;
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

        private void RefreshWaitingRoom(OnlineSession session)
        {
            IReadOnlyList<LobbySlot> roster = session.Roster;
            int connected = 0;
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                LobbySlot slot = p < roster.Count ? roster[p] : default;
                Color color = p < _colors.Length ? _colors[p] : Color.white;
                connected += slot.Connected ? 1 : 0;
                if (!slot.Connected)
                {
                    _slotPlates[p].color = EmptyFill;
                    _slotPlates[p].BorderColor = EmptyBorder;
                    _slotNames[p].text = "WAITING...";
                    _slotNames[p].color = new Color(1f, 1f, 1f, 0.45f);
                    _slotStates[p].text = string.Empty;
                    continue;
                }
                string label = string.IsNullOrEmpty(slot.Name) ? $"P{p + 1}" : slot.Name;
                var marks = new StringBuilder(label);
                if (p == 0)
                {
                    marks.Append("  (HOST)");
                }
                if (p == session.LocalPlayer)
                {
                    marks.Append("  (YOU)");
                }
                _slotPlates[p].color = JoinedFill;
                _slotPlates[p].BorderColor = slot.Ready ? color : new Color(color.r, color.g, color.b, 0.5f);
                _slotNames[p].text = marks.ToString();
                _slotNames[p].color = Color.white;
                _slotStates[p].text = slot.Ready ? "READY!" : "NOT READY";
                _slotStates[p].color = slot.Ready ? color : new Color(1f, 1f, 1f, 0.6f);
            }
            _ready.SetLabel(session.LocalReady ? "CANCEL READY" : "READY");
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
            _waitingMessage.text = connected < 2
                ? "WAITING FOR PLAYERS (2-4)"
                : "WHEN EVERYONE IS READY, CHARACTER SELECT STARTS";
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
