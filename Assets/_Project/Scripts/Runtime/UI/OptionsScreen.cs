using System;
using System.Collections.Generic;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// オプションの画面（docs/15 T6-9。D-68）。いちばん上の PAGE の項目で、左右（か決定）でページを切り替える:
    /// - GENERAL: 全体・BGM・効果音の音量（0〜100%、10% ずつ）、表示（FULLSCREEN ／ WINDOWED）、解像度（この画面で使える大きさ）、
    ///   垂直同期（ON ／ OFF）、言語（LANGUAGE: 日本語 ／ ENGLISH。今はステージ選択の説明だけを切り替える。初めは日本語。
    ///   ユーザーの要望、2026-10-27）、RESET TO DEFAULT。項目を選んで左右で変える（値の左半分・右半分のクリックでも左右。OptionArrow。
    ///   決定でも次の値へ）。変えたらすぐに当てて保存する。RANDOM STAGES で、ステージ選択の RANDOM で選ぶステージのチェックを付け外し
    ///   する画面へ（RandomStagesRequested。ステージ選択と同じ画面。ユーザーの要望、2026-10-26）。
    /// 変えた結果の案内（左下）は 3 秒で消え、ページを切り替えても消える。
    /// - CONTROLS: 変えられる操作（BindingSettings）ごとに、キーボードのキー（マウスの左・右・サイドのボタンも）とゲームパッドのボタン。項目を決定すると、次に押した
    ///   キー（ボタン）に変わる（キーボードは Esc、ゲームパッドは Start でやめる）。ほかの操作と同じキーなら入れ替える。RESET CONTROLS で
    ///   元に戻す。
    /// BACK か戻る操作（Esc・B）で閉じる（Closed）。設定は GameSettings で PlayerPrefs に保存し、キーの割り当ては試合の入力を
    /// 作るときに当たる（InputSystemAdapter）。
    /// </summary>
    public sealed class OptionsScreen : MonoBehaviour
    {
        /// <summary>GENERAL のページの項目（_generalRows と同じ順）。</summary>
        public enum Row
        {
            Master,
            Bgm,
            Se,
            Display,
            Resolution,
            VSync,
            Language,
        }

        /// <summary>LANGUAGE の値の文字。</summary>
        public const string JapaneseLabel = "日本語";
        public const string EnglishLabel = "ENGLISH";

        private const float NavigationRestoreDelay = 0.2f;

        /// <summary>変えた結果の案内を出しておく長さ（秒。ユーザーの要望、2026-10-20）。</summary>
        private const float PromptSeconds = 3f;

        [Tooltip("キーの割り当てを変える入力のアセット（試合で使うものと同じ）")]
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private MenuOption _page;
        [SerializeField] private Text _pageValue;
        [SerializeField] private GameObject _generalPage;
        [SerializeField] private GameObject _controlsPage;
        [Tooltip("GENERAL の項目（Row の順）と、その値の文字")]
        [SerializeField] private MenuOption[] _generalRows = new MenuOption[0];
        [SerializeField] private Text[] _generalValues = new Text[0];
        [SerializeField] private MenuOption _resetGeneral;
        [Tooltip("RANDOM で選ぶステージを決める画面へ（なくてもよい）")]
        [SerializeField] private MenuOption _randomStages;
        [Tooltip("CONTROLS の項目（BindingSettings.Actions の順）。キーボードとゲームパッド")]
        [SerializeField] private MenuOption[] _keyCells = new MenuOption[0];
        [SerializeField] private MenuOption[] _padCells = new MenuOption[0];
        [SerializeField] private MenuOption _resetControls;
        [SerializeField] private MenuOption _back;
        [Tooltip("キーの割り当てを変えている間の案内と、変えた結果")]
        [SerializeField] private Text _prompt;

        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        private GameSettings _settings;
        private int _resolutionIndex;
        private bool _controls;
        private InputActionMap _map;
        private InputActionRebindingExtensions.RebindingOperation _rebind;
        private float _navigationRestoreAt = -1f;
        private float _promptHideAt = -1f;
        private bool _wired;
        private Font _japanese;
        private Font _latin;

        /// <summary>閉じた（設定は保存してある）。</summary>
        public event Action Closed;

        /// <summary>RANDOM STAGES を決定した（設定を保存して隠してある。呼んだ側がステージ選択を RANDOM STAGES として出す）。</summary>
        public event Action RandomStagesRequested;

        /// <summary>出しているか（テスト用）。</summary>
        public bool IsShowing => gameObject.activeSelf;

        /// <summary>CONTROLS のページを出しているか（テスト用）。</summary>
        public bool ShowingControls => _controls;

        /// <summary>キーの割り当てを変えている途中か（テスト用）。</summary>
        public bool IsRebinding => _rebind != null;

        /// <summary>今の設定（テスト用）。</summary>
        public GameSettings Settings => _settings;

        /// <summary>キーの割り当てを変える入力のアセット（テスト用）。</summary>
        public InputActionAsset InputActions => _inputActions;

        public MenuOption Page => _page;
        public MenuOption Back => _back;
        public MenuOption ResetGeneral => _resetGeneral;
        public MenuOption RandomStages => _randomStages;
        public MenuOption ResetControls => _resetControls;
        public MenuOption GeneralRow(Row row) => _generalRows[(int)row];
        public string GeneralValue(Row row) => _generalValues[(int)row].text;
        public MenuOption KeyCell(int action) => _keyCells[action];
        public MenuOption PadCell(int action) => _padCells[action];
        public string PromptText => _prompt != null ? _prompt.text : string.Empty;

        private void Awake()
        {
            Initialize();
        }

        // 項目がそろっているかを調べ、操作をつなぐ（Show からも呼ぶ。エディタで画像を撮るときは Awake が呼ばれない）。
        private bool Initialize()
        {
            if (_map != null)
            {
                return true;
            }
            int rows = Enum.GetValues(typeof(Row)).Length;
            int actions = BindingSettings.Actions.Length;
            if (_inputActions == null || _page == null || _pageValue == null || _generalPage == null || _controlsPage == null
                || _generalRows.Length != rows || _generalValues.Length != rows || _resetGeneral == null || _keyCells.Length != actions
                || _padCells.Length != actions || _resetControls == null || _back == null)
            {
                Debug.LogError($"{name}: オプションの画面の項目が設定されていません", this);
                enabled = false;
                return false;
            }
            _map = _inputActions.FindActionMap(BindingSettings.MapName, throwIfNotFound: true);
            Wire();
            return true;
        }

        // 項目の操作をつなぐ（1 回だけ）。
        private void Wire()
        {
            if (_wired)
            {
                return;
            }
            _wired = true;
            Stepper(_page).Stepped += _ =>
            {
                UiSounds.Find().Play(SoundId.UiMove);
                SetPage(!_controls);
            };
            _page.Clicked.AddListener(() => SetPage(!_controls));
            for (int i = 0; i < _generalRows.Length; i++)
            {
                var row = (Row)i;
                Stepper(_generalRows[i]).Stepped += delta => Step(row, delta);
                // 決定でも次の値へ（決定の音は項目が鳴らす）。
                _generalRows[i].Clicked.AddListener(() => Step(row, 1, wrap: true, quiet: true));
            }
            _resetGeneral.Clicked.AddListener(ResetGeneralSettings);
            if (_randomStages != null)
            {
                _randomStages.Clicked.AddListener(() =>
                {
                    CancelRebind();
                    _settings?.Save();
                    gameObject.SetActive(false);
                    RandomStagesRequested?.Invoke();
                });
            }
            for (int i = 0; i < _keyCells.Length; i++)
            {
                int action = i;
                _keyCells[i].Clicked.AddListener(() => BeginRebind(action, BindingSettings.Column.Keyboard));
                _padCells[i].Clicked.AddListener(() => BeginRebind(action, BindingSettings.Column.Gamepad));
            }
            _resetControls.Clicked.AddListener(ResetBindings);
            _back.Clicked.AddListener(Close);
            foreach (MenuOption item in AllItems())
            {
                item.Cancelled += () =>
                {
                    if (_rebind == null)
                    {
                        Close();
                    }
                };
            }
        }

        private static OptionStepper Stepper(MenuOption option)
        {
            return option.TryGetComponent(out OptionStepper stepper) ? stepper : option.gameObject.AddComponent<OptionStepper>();
        }

        private IEnumerable<MenuOption> AllItems()
        {
            yield return _page;
            foreach (MenuOption row in _generalRows)
            {
                yield return row;
            }
            yield return _resetGeneral;
            if (_randomStages != null)
            {
                yield return _randomStages;
            }
            foreach (MenuOption cell in _keyCells)
            {
                yield return cell;
            }
            foreach (MenuOption cell in _padCells)
            {
                yield return cell;
            }
            yield return _resetControls;
            yield return _back;
        }

        /// <summary>出して、RANDOM STAGES の項目を選ぶ（RANDOM STAGES の画面から戻ったとき）。</summary>
        public void ShowAtRandomStages()
        {
            Show();
            if (_randomStages != null && IsShowing)
            {
                _randomStages.SelectQuietly();
            }
        }

        /// <summary>出す（GENERAL のページから。保存した設定を読む）。</summary>
        public void Show()
        {
            if (!Initialize())
            {
                return;
            }
            gameObject.SetActive(true);
            _settings = GameSettings.Load();
            BindingSettings.Apply(_inputActions, _settings.BindingOverrides);
            BuildResolutions();
            SetPrompt(string.Empty);
            SetPage(false);
        }

        /// <summary>隠す（保存はしない。閉じるときは Close）。</summary>
        public void Hide()
        {
            CancelRebind();
            gameObject.SetActive(false);
        }

        /// <summary>保存して閉じる。</summary>
        public void Close()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }
            CancelRebind();
            _settings?.Save();
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void OnDisable()
        {
            CancelRebind();
            RestoreNavigation();
        }

        private void Update()
        {
            if (_navigationRestoreAt >= 0f && Time.unscaledTime >= _navigationRestoreAt)
            {
                RestoreNavigation();
            }
            if (_promptHideAt >= 0f && Time.unscaledTime >= _promptHideAt && _rebind == null)
            {
                SetPrompt(string.Empty);
            }
        }

        // ページを切り替える（ページの項目を選んだまま）。
        private void SetPage(bool controls)
        {
            _controls = controls;
            _generalPage.SetActive(!controls);
            _controlsPage.SetActive(controls);
            _pageValue.text = controls ? "CONTROLS" : "GENERAL";
            SetPrompt(string.Empty);
            BuildNavigation();
            RefreshGeneral();
            RefreshBindings();
            _page.SelectQuietly();
        }

        // 上下・左右の移動先（ページごと。値を変える項目の左右は OptionStepper が受け持つので、移動先を決めない）。
        private void BuildNavigation()
        {
            var column = new List<Selectable> { _page.Button };
            if (_controls)
            {
                for (int i = 0; i < _keyCells.Length; i++)
                {
                    Link(_keyCells[i].Button, i == 0 ? _page.Button : _keyCells[i - 1].Button,
                        i + 1 < _keyCells.Length ? _keyCells[i + 1].Button : _resetControls.Button, null, _padCells[i].Button);
                    Link(_padCells[i].Button, i == 0 ? _page.Button : _padCells[i - 1].Button,
                        i + 1 < _padCells.Length ? _padCells[i + 1].Button : _resetControls.Button, _keyCells[i].Button, null);
                }
                Link(_page.Button, _back.Button, _keyCells[0].Button, null, null);
                Link(_resetControls.Button, _keyCells[_keyCells.Length - 1].Button, _back.Button, null, null);
                Link(_back.Button, _resetControls.Button, _page.Button, null, null);
                return;
            }
            foreach (MenuOption row in _generalRows)
            {
                column.Add(row.Button);
            }
            if (_randomStages != null)
            {
                column.Add(_randomStages.Button);
            }
            column.Add(_resetGeneral.Button);
            column.Add(_back.Button);
            for (int i = 0; i < column.Count; i++)
            {
                Link(column[i], column[(i + column.Count - 1) % column.Count], column[(i + 1) % column.Count], null, null);
            }
        }

        private static void Link(Selectable item, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            item.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right,
            };
        }

        /// <summary>GENERAL の項目 row の値を delta だけ変える（wrap なら端から反対の端へ回る）。すぐに当てて保存する。</summary>
        public void Step(Row row, int delta, bool wrap = false, bool quiet = false)
        {
            if (_settings == null || delta == 0)
            {
                return;
            }
            switch (row)
            {
                case Row.Master:
                    _settings.MasterVolume = StepVolume(_settings.MasterVolume, delta, wrap);
                    break;
                case Row.Bgm:
                    _settings.BgmVolume = StepVolume(_settings.BgmVolume, delta, wrap);
                    break;
                case Row.Se:
                    _settings.SeVolume = StepVolume(_settings.SeVolume, delta, wrap);
                    break;
                case Row.Display:
                    _settings.Fullscreen = !_settings.Fullscreen;
                    break;
                case Row.Resolution:
                    if (_resolutions.Count == 0)
                    {
                        return;
                    }
                    _resolutionIndex = wrap
                        ? (_resolutionIndex + delta + _resolutions.Count) % _resolutions.Count
                        : Mathf.Clamp(_resolutionIndex + delta, 0, _resolutions.Count - 1);
                    _settings.Width = _resolutions[_resolutionIndex].x;
                    _settings.Height = _resolutions[_resolutionIndex].y;
                    break;
                case Row.VSync:
                    _settings.VSync = !_settings.VSync;
                    break;
                case Row.Language:
                    _settings.Language = _settings.Language == GameLanguage.Japanese ? GameLanguage.English : GameLanguage.Japanese;
                    break;
            }
            if (!quiet)
            {
                UiSounds.Find().Play(SoundId.UiMove);
            }
            ApplyAndSave(row == Row.Display || row == Row.Resolution || row == Row.VSync);
        }

        private static int StepVolume(int volume, int delta, bool wrap)
        {
            int next = volume + delta;
            if (wrap)
            {
                return (next % (GameSettings.MaxVolume + 1) + GameSettings.MaxVolume + 1) % (GameSettings.MaxVolume + 1);
            }
            return Mathf.Clamp(next, 0, GameSettings.MaxVolume);
        }

        private void ResetGeneralSettings()
        {
            string bindings = _settings.BindingOverrides;
            _settings = new GameSettings { BindingOverrides = bindings };
            BuildResolutions();
            ApplyAndSave(true);
            SetPrompt("GENERAL SETTINGS RESET");
        }

        // 今の設定を当てて保存する（音量は鳴っている BGM と効果音にもすぐ当てる）。
        private void ApplyAndSave(bool screen)
        {
            _settings.ApplyAudio();
            foreach (GameAudio audio in FindObjectsByType<GameAudio>(FindObjectsSortMode.None))
            {
                audio.ApplySettings(_settings);
            }
            foreach (UiSounds sounds in FindObjectsByType<UiSounds>(FindObjectsSortMode.None))
            {
                sounds.ApplySettings(_settings);
            }
            foreach (MusicPlayer music in FindObjectsByType<MusicPlayer>(FindObjectsSortMode.None))
            {
                music.ApplySettings(_settings);
            }
            if (screen)
            {
                _settings.ApplyScreen();
            }
            _settings.Save();
            RefreshGeneral();
        }

        // 解像度の候補（この画面で使える大きさ。今の大きさも入れる）。
        private void BuildResolutions()
        {
            _resolutions.Clear();
            foreach (Resolution resolution in Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (!_resolutions.Contains(size))
                {
                    _resolutions.Add(size);
                }
            }
            var current = _settings.HasScreen ? new Vector2Int(_settings.Width, _settings.Height) : new Vector2Int(Screen.width, Screen.height);
            if (!_resolutions.Contains(current))
            {
                _resolutions.Add(current);
            }
            _resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            _resolutionIndex = _resolutions.IndexOf(current);
        }

        private void RefreshGeneral()
        {
            if (_settings == null)
            {
                return;
            }
            _generalValues[(int)Row.Master].text = VolumeText(_settings.MasterVolume);
            _generalValues[(int)Row.Bgm].text = VolumeText(_settings.BgmVolume);
            _generalValues[(int)Row.Se].text = VolumeText(_settings.SeVolume);
            _generalValues[(int)Row.Display].text = _settings.Fullscreen ? "FULLSCREEN" : "WINDOWED";
            Vector2Int size = _resolutionIndex >= 0 && _resolutionIndex < _resolutions.Count ? _resolutions[_resolutionIndex] : Vector2Int.zero;
            _generalValues[(int)Row.Resolution].text = $"{size.x} x {size.y}";
            _generalValues[(int)Row.VSync].text = _settings.VSync ? "ON" : "OFF";
            ShowLanguage(_settings.Language);
        }

        // 言語の値（「日本語」は PC の日本語の字体で書く。PC になければ Unity に入っている字体のまま）。
        private void ShowLanguage(GameLanguage language)
        {
            Text value = _generalValues[(int)Row.Language];
            if (_latin == null)
            {
                _latin = value.font;
            }
            bool ja = language == GameLanguage.Japanese;
            if (ja && _japanese == null)
            {
                _japanese = JapaneseFont.Create(32);
            }
            value.font = ja && _japanese != null ? _japanese : _latin;
            value.text = ja ? JapaneseLabel : EnglishLabel;
        }

        private void OnDestroy()
        {
            if (_japanese != null)
            {
                Destroy(_japanese);
            }
        }

        private static string VolumeText(int volume)
        {
            return $"{volume * 100 / GameSettings.MaxVolume}%";
        }

        private void RefreshBindings()
        {
            for (int i = 0; i < BindingSettings.Actions.Length; i++)
            {
                InputAction action = _map.FindAction(BindingSettings.Actions[i], throwIfNotFound: true);
                _keyCells[i].SetLabel(BindingSettings.DisplayName(action, BindingSettings.BindingIndex(action, BindingSettings.Column.Keyboard)));
                _padCells[i].SetLabel(BindingSettings.DisplayName(action, BindingSettings.BindingIndex(action, BindingSettings.Column.Gamepad)));
            }
        }

        // 操作 actionIndex の、列 column の割り当てを変え始める（次に押したキー・ボタンで決まる）。
        private void BeginRebind(int actionIndex, BindingSettings.Column column)
        {
            if (_rebind != null || _navigationRestoreAt >= 0f)
            {
                return;
            }
            InputAction action = _map.FindAction(BindingSettings.Actions[actionIndex], throwIfNotFound: true);
            int binding = BindingSettings.BindingIndex(action, column);
            if (binding < 0)
            {
                SetPrompt($"{BindingSettings.Labels[actionIndex]} HAS NO {column.ToString().ToUpperInvariant()} BINDING");
                return;
            }
            string previous = action.bindings[binding].effectivePath;
            MenuOption cell = column == BindingSettings.Column.Keyboard ? _keyCells[actionIndex] : _padCells[actionIndex];
            cell.SetLabel("...");
            SetPrompt(column == BindingSettings.Column.Keyboard
                ? $"PRESS A KEY OR MOUSE BUTTON FOR {BindingSettings.Labels[actionIndex]}   (ESC: CANCEL)"
                : $"PRESS A BUTTON FOR {BindingSettings.Labels[actionIndex]}   (START: CANCEL)", false);
            // 変えている間は、メニューの移動・決定・戻るを止める（押したキーで項目が動かないように）。操作の組も止める。
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = false;
            }
            bool wasEnabled = _map.enabled;
            _map.Disable();
            _rebind = BindingSettings.StartRebind(action, binding, column, changed =>
            {
                _rebind = null;
                if (wasEnabled)
                {
                    _map.Enable();
                }
                string message;
                if (changed)
                {
                    string swapped = BindingSettings.SwapDuplicate(_map, action, column, previous);
                    message = $"{BindingSettings.Labels[actionIndex]}: {BindingSettings.Readable(action.bindings[binding].effectivePath)}";
                    if (swapped != null)
                    {
                        int other = Array.IndexOf(BindingSettings.Actions, swapped);
                        message += $"   ({BindingSettings.Labels[other]} → {BindingSettings.Readable(previous)})";
                    }
                    SaveBindings();
                }
                else
                {
                    message = "CANCELLED";
                }
                RefreshBindings();
                SetPrompt(message);
                // 押したキーが離れるまで、少し待ってからメニューの操作を戻す。
                _navigationRestoreAt = Time.unscaledTime + NavigationRestoreDelay;
            });
        }

        private void CancelRebind()
        {
            if (_rebind != null)
            {
                InputActionRebindingExtensions.RebindingOperation operation = _rebind;
                _rebind = null;
                operation.Cancel();
            }
        }

        private void RestoreNavigation()
        {
            _navigationRestoreAt = -1f;
            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = true;
            }
        }

        private void ResetBindings()
        {
            _inputActions.RemoveAllBindingOverrides();
            SaveBindings();
            RefreshBindings();
            SetPrompt("CONTROLS RESET");
        }

        private void SaveBindings()
        {
            _settings.BindingOverrides = _inputActions.SaveBindingOverridesAsJson();
            _settings.Save();
        }

        // 案内を出す（timed なら PromptSeconds で消す。変えている間の案内は消さない）。
        private void SetPrompt(string text, bool timed = true)
        {
            _promptHideAt = timed && !string.IsNullOrEmpty(text) ? Time.unscaledTime + PromptSeconds : -1f;
            if (_prompt != null)
            {
                _prompt.text = text;
            }
        }
    }
}
