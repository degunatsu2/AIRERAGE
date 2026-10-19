using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 結果の画面の項目（docs/15 T6-8）: REMATCH（同じ人・同じキャラクター・同じステージで再戦）、CHARACTER SELECT（同じ人のまま
    /// キャラクター選択へ）、MAIN MENU（メニューへ）。結果（MatchHud の順位の表）が出たら、画面の下に横に並べて出す。キーボード（矢印・
    /// Enter）、ゲームパッド（十字キー・スティック・A）、マウスで選べる。ネットワーク対戦では、REMATCH と CHARACTER SELECT はホストだけが
    /// 押せる（ほかの人は「ホストを待つ」）。
    /// 出している間は、カメラの操作（十字キーのズーム）を止める（一時停止と同じ。項目を選ぶとカメラが寄り引きしないように）。
    /// 一時停止のメニューを開いている間は、選んでいる項目を奪わない。
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
        [SerializeField] private Text _note;

        private IMatchControls _target;
        private PauseMenu _pause;
        private InputActionMap _camera;
        private bool _cameraWasEnabled;
        private bool _canRestart;
        private bool _canReturn;
        private int _selectAtFrame = -1;

        /// <summary>出しているか。</summary>
        public bool IsOpen => _panel != null && _panel.activeSelf;

        public MenuOption Rematch => _rematch;
        public MenuOption CharacterSelect => _characterSelect;
        public MenuOption MainMenu => _menu;

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
            _menu.Clicked.AddListener(() => _target.Quit());
            _panel.SetActive(false);
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

        private void Open()
        {
            PauseMenu.EnsureEventSystem();
            _panel.SetActive(true);
            _canRestart = !_target.CanRestart;
            _canReturn = !_target.CanReturnToCharacterSelect;
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
            if (canRestart == _canRestart && canReturn == _canReturn)
            {
                return;
            }
            _canRestart = canRestart;
            _canReturn = canReturn;
            // 押せない理由は札の中ではなく（長い名前と重なる）、下の文に出す。
            _rematch.SetAvailable(canRestart);
            _characterSelect.SetAvailable(canReturn);
            if (_note != null)
            {
                // 再戦できるのに戻れないのは、試合のシーンを直接再生したとき（ゲームの流れがない）。
                _note.text = !canRestart && !canReturn ? "WAITING FOR THE HOST TO CHOOSE REMATCH OR CHARACTER SELECT"
                    : !canReturn ? "CHARACTER SELECT: START FROM THE MAIN MENU (APP SCENE)" : string.Empty;
            }
        }

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
