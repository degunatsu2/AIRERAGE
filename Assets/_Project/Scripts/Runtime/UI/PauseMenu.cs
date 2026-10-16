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
    /// 一時停止のメニュー（docs/15 T6-1。オフラインの試合: トレーニングと検証用のシーン）。Esc かゲームパッドの Start（入力の Pause）で
    /// 開き、シミュレーションを止める。項目: RESUME（続ける）、RESET POSITION（出現位置に戻す。Backspace と同じ）、BACK TO MENU（App の
    /// シーンから始めたときだけ）。もう一度 Pause か、戻る操作で閉じる。
    /// 検証用のシーンを直接再生したときは EventSystem がないので、開くときに作る。
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private GameObject _panel;
        [SerializeField] private MenuOption _resume;
        [SerializeField] private MenuOption _reset;
        [SerializeField] private MenuOption _menu;

        private InputAction _pause;
        private GameFlow _flow;
        private float _speedBefore = 1f;
        private int _selectAtFrame = -1;

        /// <summary>開いているか。</summary>
        public bool IsOpen => _panel != null && _panel.activeSelf;

        public MenuOption Resume => _resume;
        public MenuOption ResetPosition => _reset;
        public MenuOption BackToMenu => _menu;

        private void Awake()
        {
            if (_runner == null || _inputActions == null || _panel == null || _resume == null || _reset == null || _menu == null)
            {
                Debug.LogError($"{name}: 実行・入力・画面・項目のどれかが設定されていません", this);
                enabled = false;
                return;
            }
            _pause = _inputActions.FindAction("Player/Pause", throwIfNotFound: true);
            _resume.Clicked.AddListener(Close);
            _reset.Clicked.AddListener(() =>
            {
                _runner.ResetPlayer();
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
            if (_pause.WasPressedThisFrame())
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
            _speedBefore = _runner.SpeedScale;
            _runner.SpeedScale = 0f;
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
            _runner.SpeedScale = _speedBefore > 0f ? _speedBefore : 1f;
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
