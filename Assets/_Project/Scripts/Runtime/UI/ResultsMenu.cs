using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Airerage.Runtime.Settings;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 結果の画面の項目（docs/15 T6-8）: REMATCH（同じ人・同じキャラクター・同じステージで再戦）、CHARACTER SELECT（同じ人のまま
    /// キャラクター選択へ）、MAIN MENU（メニューへ）。結果（MatchHud の順位の表）が出たら、画面の下に横に並べて出す。キーボード（矢印・
    /// Enter）、ゲームパッド（十字キー・スティック・A）、マウスで選べる。ネットワーク対戦では、REMATCH と CHARACTER SELECT はホストだけが
    /// 押せる（ほかの人は「ホストを待つ」）。ネットワーク対戦の 3 つ目の項目は MAIN MENU ではなく BACK ROOM（ホストが全員を待機所へ戻す。
    /// 途中から部屋に入った人がいる間は、REMATCH と CHARACTER SELECT を押せない。2026-11-03 のユーザーの要望）。
    /// ネットワーク対戦では、画面の右上に EXIT（左向きに開いたドアと右向きの矢印の印）も出し、押すと自分だけ部屋を出てメニューへ戻る
    /// （2026-11-03 のユーザーの要望。ホストが押すと部屋がなくなり、ほかの人は「ホストとの接続が切れました」になる）。
    /// 出している間は、カメラの操作（十字キーのズーム）を止める（一時停止と同じ。項目を選ぶとカメラが寄り引きしないように）。
    /// 一時停止のメニューを開いている間は、選んでいる項目を奪わない。
    /// 結果を出している間は、試合中の表示（技の札、右上の表、お知らせ、コンボ、画面の端の札、頭上の数など）を止めて消す
    /// （_hideDuringResults。結果に重なって見えなかった。ユーザーの指摘、2026-10-19）。閉じたら（再戦）戻す。
    /// </summary>
    public sealed class ResultsMenu : MonoBehaviour
    {
        [Tooltip("試合を進める側（LocalMatchRunner か OnlineRunner。IMatchControls）")]
        [SerializeField] private MonoBehaviour _controls;
        [Tooltip("結果を出しているかを知る（MatchHud.ShowingResults）")]
        [SerializeField] private MatchHud _hud;
        [Tooltip("止めるカメラの操作（入力のアセットの Camera。なくてもよい）")]
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private GameObject _panel;
        [SerializeField] private MenuOption _rematch;
        [SerializeField] private MenuOption _characterSelect;
        [SerializeField] private MenuOption _menu;
        [Tooltip("右上の EXIT（ネットワーク対戦だけ出す。自分だけ部屋を出る）")]
        [SerializeField] private MenuOption _exit;
        [Tooltip("EXIT の印（ドアと矢印。絵は起動時にコードで描く）")]
        [SerializeField] private RawImage _exitIcon;
        [SerializeField] private Text _note;
        [Tooltip("結果を出している間は止める、試合中の表示")]
        [SerializeField] private Behaviour[] _hideDuringResults = new Behaviour[0];

        private IMatchControls _target;
        private PauseMenu _pause;
        private InputActionMap _camera;
        private bool _cameraWasEnabled;
        private bool _canRestart;
        private bool _canReturn;
        private bool _canBack;
        private bool _newcomers;
        private bool _tooFew;
        private int _selectAtFrame = -1;
        private Texture2D _exitTexture;
        private readonly System.Collections.Generic.List<Behaviour> _hidden = new System.Collections.Generic.List<Behaviour>();

        /// <summary>出しているか。</summary>
        public bool IsOpen => _panel != null && _panel.activeSelf;

        public MenuOption Rematch => _rematch;
        public MenuOption CharacterSelect => _characterSelect;
        public MenuOption MainMenu => _menu;

        /// <summary>3 つ目の項目（ネットワーク対戦では BACK ROOM、ほかでは MAIN MENU）。</summary>
        public MenuOption BackRoom => _menu;

        /// <summary>右上の EXIT（ネットワーク対戦だけ出す）。</summary>
        public MenuOption Exit => _exit;

        /// <summary>下に出している文（ホストを待つ、など。テスト用）。</summary>
        public string Note => _note != null ? _note.text : string.Empty;

        private void Awake()
        {
            _target = _controls as IMatchControls;
            if (_target == null || _hud == null || _panel == null || _rematch == null || _characterSelect == null || _menu == null)
            {
                Debug.LogError($"{name}: 試合を進める側（IMatchControls）・結果の表示・画面・項目のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            _camera = _inputActions != null ? _inputActions.FindActionMap("Camera", throwIfNotFound: false) : null;
            _rematch.Clicked.AddListener(() =>
            {
                if (_target.CanRestart)
                {
                    _target.Restart();
                }
            });
            _characterSelect.Clicked.AddListener(() =>
            {
                if (_target.CanReturnToCharacterSelect)
                {
                    _target.ReturnToCharacterSelect();
                }
            });
            _menu.Clicked.AddListener(() =>
            {
                if (!_target.HasRoom)
                {
                    _target.Quit();
                }
                else if (_target.CanBackToRoom)
                {
                    _target.BackToRoom();
                }
            });
            if (_exit != null)
            {
                _exit.Clicked.AddListener(() => _target.Quit());
                _exit.gameObject.SetActive(false);
                if (_exitIcon != null)
                {
                    _exitTexture = GuideIcons.Exit(96, Color.white);
                    _exitIcon.texture = _exitTexture;
                }
            }
            _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_exitTexture != null)
            {
                Destroy(_exitTexture);
            }
        }

        private void Start()
        {
            _pause = FindFirstObjectByType<PauseMenu>();
        }

        private void OnDisable()
        {
            if (IsOpen)
            {
                Close();
            }
        }

        private void Update()
        {
            bool show = _hud.ShowingResults;
            if (show && !IsOpen)
            {
                Open();
            }
            else if (!show && IsOpen)
            {
                Close();
            }
            if (!show)
            {
                return;
            }
            RefreshAvailability();
            if (_exitIcon != null && _exit != null && _exit.LabelText != null)
            {
                _exitIcon.color = _exit.LabelText.color;
            }
            bool paused = _pause != null && _pause.IsOpen;
            if (paused || EventSystem.current == null)
            {
                return;
            }
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if ((_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame) || selected == null || !selected.activeInHierarchy
                || (selected.GetComponent<Selectable>() is Selectable current && !current.interactable))
            {
                _selectAtFrame = -1;
                FirstAvailable().SelectQuietly();
            }
        }

        // 遊んでいるモードのイメージカラー（ネットワーク対戦・シングルモード・ローカル対戦）。
        private Color ModeAccent()
        {
            if (_target.HasRoom)
            {
                return MenuAccents.Network;
            }
            Airerage.Runtime.Flow.GameFlow flow = FindFirstObjectByType<Airerage.Runtime.Flow.GameFlow>();
            return flow != null && flow.LocalSingle ? MenuAccents.Single : MenuAccents.Local;
        }

        /// <summary>結果の案内の文字の色（テスト用）。</summary>
        public Color NoteColor => _note != null ? _note.color : Color.clear;

        /// <summary>結果を出している間に止めている表示の数（テスト用）。</summary>
        public int HiddenCount => _hidden.Count;

        private void Open()
        {
            PauseMenu.EnsureEventSystem();
            _panel.SetActive(true);
            foreach (Behaviour hud in _hideDuringResults)
            {
                if (hud != null && hud.enabled)
                {
                    hud.enabled = false;
                    _hidden.Add(hud);
                }
            }
            _canRestart = !_target.CanRestart;
            _canReturn = !_target.CanReturnToCharacterSelect;
            _canBack = !CanBack;
            _menu.SetLabel(_target.HasRoom ? "BACK ROOM" : "MAIN MENU");
            // 案内の文字は、遊んでいるモードのイメージカラー（以前は水色。2026-11-08 のユーザーの要望）。
            AccentTint.Apply(_panel.transform, ModeAccent());
            if (_exit != null)
            {
                // ローカル対戦とシングルモードは 3 つ目の MAIN MENU で抜けられるので、出さない。
                _exit.gameObject.SetActive(_target.HasRoom);
            }
            RefreshAvailability();
            // 試合の最後の操作（押しっぱなしの決定のボタンなど）で、そのまま押さないよう、項目を選ぶのは次のフレーム。
            EventSystem.current.SetSelectedGameObject(null);
            _selectAtFrame = Time.frameCount + 1;
            if (_camera != null)
            {
                _cameraWasEnabled = _camera.enabled;
                _camera.Disable();
            }
        }

        private void Close()
        {
            _panel.SetActive(false);
            foreach (Behaviour hud in _hidden)
            {
                if (hud != null)
                {
                    hud.enabled = true;
                }
            }
            _hidden.Clear();
            _selectAtFrame = -1;
            if (_camera != null && _cameraWasEnabled)
            {
                _camera.Enable();
            }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_panel.transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        // 押せるかが変わったときだけ札を書き直す（ホストが抜けた、など）。
        private void RefreshAvailability()
        {
            bool canRestart = _target.CanRestart;
            bool canReturn = _target.CanReturnToCharacterSelect;
            bool canBack = CanBack;
            bool newcomers = _target.NewcomersWaiting;
            bool tooFew = _target.TooFewPlayers;
            if (canRestart == _canRestart && canReturn == _canReturn && canBack == _canBack && newcomers == _newcomers && tooFew == _tooFew)
            {
                return;
            }
            _tooFew = tooFew;
            _canRestart = canRestart;
            _canReturn = canReturn;
            _canBack = canBack;
            _newcomers = newcomers;
            // 押せない理由は札の中ではなく（長い名前と重なる）、下の文に出す。
            _rematch.SetAvailable(canRestart);
            _characterSelect.SetAvailable(canReturn);
            _menu.SetAvailable(canBack);
            if (_note != null)
            {
                // 再戦できるのに戻れないのは、試合のシーンを直接再生したとき（ゲームの流れがない）。
                _note.text = _target.HasRoom && !canBack
                    ? Loc.T("WAITING FOR THE HOST TO CHOOSE", "ホストが選ぶのを待っています")
                    : tooFew && canBack
                    ? Loc.T("NOT ENOUGH PLAYERS TO PLAY. PLEASE RETURN TO THE WAITING ROOM.", "ゲームプレイ可能な人数に達していません。待機所に戻ってください。")
                    : newcomers && canBack
                    ? Loc.T("A NEW PLAYER JOINED. CHOOSE BACK ROOM SO THEY CAN PLAY", "新しい人が入りました。BACK ROOM で待機所へ戻ると参加できます")
                    : !canRestart && !canReturn
                    ? Loc.T("WAITING FOR THE HOST TO CHOOSE REMATCH OR CHARACTER SELECT", "ホストが再戦かキャラクター選択を選ぶのを待っています")
                    : !canReturn ? Loc.T("CHARACTER SELECT: START FROM THE MAIN MENU (APP SCENE)", "キャラクター選択はメインメニュー（App のシーン）から")
                    : string.Empty;
            }
        }

        // 3 つ目の項目を押せるか（MAIN MENU はいつも、BACK ROOM はホストだけ）。
        private bool CanBack => !_target.HasRoom || _target.CanBackToRoom;

        private MenuOption FirstAvailable()
        {
            if (_rematch.Button.interactable)
            {
                return _rematch;
            }
            return _characterSelect.Button.interactable ? _characterSelect : _menu;
        }
    }
}
