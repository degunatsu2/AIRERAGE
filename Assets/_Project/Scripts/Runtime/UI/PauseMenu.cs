using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 一時停止のメニュー（docs/15 T6-1。オフラインの試合: トレーニング、検証用のシーン、ローカル対戦）。Esc かゲームパッドの Start
    /// （入力の Pause。ローカル対戦ではだれの機器でも）で開き、シミュレーションを止める（IPauseTarget）。項目: RESUME（続ける）、
    /// RESET POSITION（出現位置に戻す。Backspace と同じ。対戦では出さない）、BACK TO MENU（App のシーンから始めたときだけ）。もう一度 Pause か、戻る操作で閉じる（RESUME と同じ）。Esc は Pause でも UI の戻る操作でもあるので、
    /// 1 回の押下で開け閉めは 1 回だけにする（戻る操作で閉じた同じフレームに、Pause で開き直さない。ユーザーの指摘、2026-10-17）。
    /// 検証用のシーンを直接再生したときは EventSystem がないので、開くときに作る。
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [Tooltip("止める実行（SimulationRunner か LocalMatchRunner。IPauseTarget）")]
        [SerializeField] private MonoBehaviour _runner;
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private GameObject _panel;
        [SerializeField] private MenuOption _resume;
        [SerializeField] private MenuOption _reset;
        [SerializeField] private MenuOption _menu;

        private InputAction _pause;
        private InputActionMap _camera;
        private bool _cameraWasEnabled;
        private IPauseTarget _target;
        private GameFlow _flow;
        private float _speedBefore = 1f;
        private int _selectAtFrame = -1;
        private int _toggledAtFrame = -1;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _panel != null && _panel.activeSelf;

        public MenuOption Resume => _resume;
        public MenuOption ResetPosition => _reset;
        public MenuOption BackToMenu => _menu;

        private void Awake()
        {
            _target = _runner as IPauseTarget;
            if (_target == null || _inputActions == null || _panel == null || _resume == null || _reset == null || _menu == null)
            {
                Debug.LogError($"{name}: 実行（IPauseTarget）・入力・画面・項目のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            _pause = _inputActions.FindAction("Player/Pause", throwIfNotFound: true);
            _camera = _inputActions.FindActionMap("Camera", throwIfNotFound: true);
            _resume.Clicked.AddListener(Close);
            _reset.Clicked.AddListener(() =>
            {
                _target.ResetPlayer();
                Close();
            });
            _menu.Clicked.AddListener(() =>
            {
                if (_flow != null && !_flow.IsLoading)
                {
                    Close();
                    _flow.ShowMenu();
                }
            });
            foreach (MenuOption item in new[] { _resume, _reset, _menu })
            {
                item.Cancelled += Close;
            }
            _panel.SetActive(false);
        }

        private void Start()
        {
            _flow = FindFirstObjectByType<GameFlow>();
            _menu.gameObject.SetActive(_flow != null);
            _reset.gameObject.SetActive(_target.CanResetPosition);
        }

        private void OnEnable()
        {
            _pause?.Enable();
        }

        private void OnDisable()
        {
            _pause?.Disable();
        }

        private void Update()
        {
            if (_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame && IsOpen)
            {
                _selectAtFrame = -1;
                _resume.SelectQuietly();
            }
            if (_pause.WasPressedThisFrame() && _toggledAtFrame != Time.frameCount)
            {
                if (IsOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }
        }

        /// <summary>開いて、シミュレーションを止める。</summary>
        public void Open()
        {
            if (IsOpen)
            {
                return;
            }
            EnsureEventSystem();
            _toggledAtFrame = Time.frameCount;
            _speedBefore = _target.SpeedScale;
            _target.SpeedScale = 0f;
            // 開いている間は、ゲームの入力（キャラクターの操作、位置のリセット、カメラのズーム）を受け付けない（十字キーで項目を
            // 選ぶとカメラが寄り引きしていた。ユーザーの指摘、2026-10-19）。
            _target.InputBlocked = true;
            _cameraWasEnabled = _camera.enabled;
            _camera.Disable();
            UiSounds.Find().Play(SoundId.UiPause);
            _panel.SetActive(true);
            // 開いたボタン（Esc は戻る操作でもある）で、そのまま閉じないよう、項目を選ぶのは次のフレーム。
            EventSystem.current.SetSelectedGameObject(null);
            _selectAtFrame = Time.frameCount + 1;
        }

        /// <summary>閉じて、シミュレーションを続ける。</summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            _panel.SetActive(false);
            _selectAtFrame = -1;
            _toggledAtFrame = Time.frameCount;
            _target.SpeedScale = _speedBefore > 0f ? _speedBefore : 1f;
            _target.InputBlocked = false;
            if (_cameraWasEnabled)
            {
                _camera.Enable();
            }
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
