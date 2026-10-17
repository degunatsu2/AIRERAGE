using System;
using System.Collections.Generic;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// ローカル対戦の参加とキャラクター選択（docs/15 T6-5、T6-3。D-67）。4 人分の枠を横に並べ、機器ごとに操作する:
    /// キーボード＋マウスで 1 人、ゲームパッド 1 つで 1 人。
    /// - 参加: Enter・Space（キーボード）、A・Start（ゲームパッド）。参加した順に P1〜P4。
    /// - キャラクター: 左右（A・D・矢印、十字キー・左スティック）。決定（参加と同じボタン）で READY。
    /// - 戻る: Esc・Backspace、B。READY を取り消し、もう一度で抜ける。だれも参加していなければメインメニューへ（Cancelled）。
    /// - 時間の制限: T（キーボード）、Y（ゲームパッド）で あり／なし を切り替える（参加している人だけ）。
    /// 2 人以上が参加して全員が READY になったら、Confirmed（参加した人と時間の制限）でステージ選択へ進む。
    /// メニューの EventSystem とは別に、機器を直接読む（同時に何人もが別の枠を操作するため）。
    /// </summary>
    public sealed class LocalJoinScreen : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        [SerializeField] private CharacterRoster _roster;
        [Tooltip("枠ごとの札・番号・機器・ロゴ・名前・案内（P1〜P4 の順）")]
        [SerializeField] private SlantedPlate[] _plates = new SlantedPlate[0];
        [SerializeField] private Text[] _numbers = new Text[0];
        [SerializeField] private Text[] _devices = new Text[0];
        [SerializeField] private RawImage[] _logos = new RawImage[0];
        [SerializeField] private Text[] _names = new Text[0];
        [SerializeField] private Text[] _states = new Text[0];
        [SerializeField] private Text _timeLimit;
        [SerializeField] private Text _message;
        [Tooltip("プレイヤーごとの色（P1〜P4）")]
        [SerializeField] private Color[] _colors = new Color[0];

        private static readonly Color EmptyFill = new Color(0.03f, 0.05f, 0.09f, 0.55f);
        private static readonly Color JoinedFill = new Color(0.03f, 0.05f, 0.09f, 0.82f);
        private static readonly Color EmptyBorder = new Color(1f, 1f, 1f, 0.25f);

        private readonly List<Entry> _entries = new List<Entry>();
        private int _activeFromFrame = int.MaxValue;
        private bool _timed = true;

        /// <summary>全員が READY になった（参加した人、時間の制限ありか）。</summary>
        public event Action<IReadOnlyList<LocalPlayerSetup>, bool> Confirmed;

        /// <summary>だれも参加していないときに戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>参加している人数（テスト用）。</summary>
        public int JoinedCount => _entries.Count;

        /// <summary>時間の制限ありか。</summary>
        public bool Timed => _timed;

        /// <summary>番号 index（P1 が 0）の人のキャラクター・READY か（テスト用）。</summary>
        public int CharacterOf(int index) => _entries[index].Character;
        public bool IsReady(int index) => _entries[index].Ready;

        /// <summary>枠 index に出している名前と案内（テスト用）。</summary>
        public string ShownName(int index) => _names[index].text;
        public string ShownState(int index) => _states[index].text;

        // 参加した 1 人（機器のまとまり 1 つ）。
        private sealed class Entry
        {
            public InputDevice Key;
            public InputDevice[] Devices;
            public string DeviceName;
            public int Character;
            public bool Ready;
        }

        private void Awake()
        {
            if (_roster == null || _roster.Count == 0 || _plates.Length != MaxPlayers || _numbers.Length != MaxPlayers
                || _devices.Length != MaxPlayers || _logos.Length != MaxPlayers || _names.Length != MaxPlayers || _states.Length != MaxPlayers)
            {
                Debug.LogError($"{name}: キャラクターの一覧がないか、{MaxPlayers} 人分の枠の表示がそろっていません", this);
                enabled = false;
            }
        }

        /// <summary>
        /// 出す。reset なら参加をやり直す（メインメニューから）。そうでなければ参加した人とキャラクターを残し、READY だけを外す（ステージ選択
        /// から戻ったとき）。開いたボタンで参加しないよう、受け付けるのは次のフレームから。
        /// </summary>
        public void Show(bool reset)
        {
            gameObject.SetActive(true);
            if (reset)
            {
                _entries.Clear();
                _timed = true;
            }
            foreach (Entry entry in _entries)
            {
                entry.Ready = false;
            }
            _activeFromFrame = Time.frameCount + 1;
            Refresh();
        }

        public void Hide()
        {
            _activeFromFrame = int.MaxValue;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Time.frameCount < _activeFromFrame)
            {
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Handle(keyboard,
                    keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame,
                    keyboard.escapeKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame,
                    Pressed(keyboard.aKey, keyboard.leftArrowKey) ? -1 : Pressed(keyboard.dKey, keyboard.rightArrowKey) ? 1 : 0,
                    keyboard.tKey.wasPressedThisFrame);
            }
            foreach (Gamepad gamepad in Gamepad.all)
            {
                Handle(gamepad,
                    gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame,
                    gamepad.buttonEast.wasPressedThisFrame,
                    Pressed(gamepad.dpad.left, gamepad.leftStick.left) ? -1 : Pressed(gamepad.dpad.right, gamepad.leftStick.right) ? 1 : 0,
                    gamepad.buttonNorth.wasPressedThisFrame);
            }
            if (isActiveAndEnabled && _entries.Count >= 2 && _entries.TrueForAll(e => e.Ready))
            {
                var players = _entries.ConvertAll(e => new LocalPlayerSetup(e.Devices, e.DeviceName, e.Character));
                Confirmed?.Invoke(players, _timed);
            }
        }

        private static bool Pressed(ButtonControl a, ButtonControl b)
        {
            return a.wasPressedThisFrame || b.wasPressedThisFrame;
        }

        // 機器のまとまり 1 つ（キーボード＋マウス、ゲームパッド）の、このフレームの操作。
        private void Handle(InputDevice key, bool confirm, bool back, int step, bool toggleTime)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            Entry entry = _entries.Find(e => e.Key == key);
            if (entry == null)
            {
                if (confirm && _entries.Count < MaxPlayers)
                {
                    _entries.Add(NewEntry(key));
                    UiSounds.Find().Play(SoundId.UiConfirm);
                    Refresh();
                }
                else if (back && _entries.Count == 0)
                {
                    Cancelled?.Invoke();
                }
                return;
            }
            if (toggleTime)
            {
                _timed = !_timed;
                UiSounds.Find().Play(SoundId.UiMove);
            }
            if (back)
            {
                if (entry.Ready)
                {
                    entry.Ready = false;
                }
                else
                {
                    _entries.Remove(entry);
                }
                UiSounds.Find().Play(SoundId.UiMove);
            }
            else if (confirm && !entry.Ready)
            {
                entry.Ready = true;
                UiSounds.Find().Play(SoundId.UiConfirm);
            }
            else if (step != 0 && !entry.Ready)
            {
                entry.Character = (entry.Character + step + _roster.Count) % _roster.Count;
                UiSounds.Find().Play(SoundId.UiMove);
            }
            Refresh();
        }

        // 新しく参加した人: キーボードならマウスも使う。最初の人は前に選んだキャラクター（トレーニングと同じ）、ほかは番号の順。
        private Entry NewEntry(InputDevice key)
        {
            var devices = new List<InputDevice> { key };
            if (key is Keyboard && Mouse.current != null)
            {
                devices.Add(Mouse.current);
            }
            int character = _entries.Count == 0
                ? _roster.Clamp(PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0))
                : _entries.Count % _roster.Count;
            string deviceName = key is Keyboard ? "KEYBOARD" : $"GAMEPAD {IndexOf(key) + 1}";
            return new Entry { Key = key, Devices = devices.ToArray(), DeviceName = deviceName, Character = character };
        }

        private static int IndexOf(InputDevice gamepad)
        {
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i] == gamepad)
                {
                    return i;
                }
            }
            return 0;
        }

        private void Refresh()
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                Color color = i < _colors.Length ? _colors[i] : Color.white;
                _numbers[i].text = $"P{i + 1}";
                _numbers[i].color = color;
                if (i >= _entries.Count)
                {
                    _plates[i].color = EmptyFill;
                    _plates[i].BorderColor = EmptyBorder;
                    _devices[i].text = string.Empty;
                    _logos[i].enabled = false;
                    _names[i].text = string.Empty;
                    _states[i].text = "JOIN\nENTER / A";
                    _states[i].color = new Color(1f, 1f, 1f, 0.6f);
                    continue;
                }
                Entry entry = _entries[i];
                CharacterConfigAsset character = _roster.Get(entry.Character);
                _plates[i].color = JoinedFill;
                _plates[i].BorderColor = entry.Ready ? color : new Color(color.r, color.g, color.b, 0.6f);
                _devices[i].text = entry.DeviceName;
                _logos[i].texture = character.Logo;
                _logos[i].enabled = character.Logo != null;
                _names[i].text = character.DisplayName;
                bool keyboard = entry.Key is Keyboard;
                _states[i].text = entry.Ready
                    ? "READY!"
                    : keyboard ? "< A / D >\nENTER: READY" : "< D-PAD >\nA: READY";
                _states[i].color = entry.Ready ? color : Color.white;
            }
            if (_timeLimit != null)
            {
                _timeLimit.text = $"TIME LIMIT: {(_timed ? "ON" : "OFF")}   (T / Y)";
            }
            if (_message != null)
            {
                _message.text = _entries.Count < 2
                    ? "2-4 PLAYERS.  JOIN WITH ENTER (KEYBOARD) OR A (GAMEPAD)"
                    : _entries.TrueForAll(e => e.Ready) ? string.Empty : "WHEN EVERYONE IS READY, CHOOSE THE STAGE";
            }
        }
    }
}
