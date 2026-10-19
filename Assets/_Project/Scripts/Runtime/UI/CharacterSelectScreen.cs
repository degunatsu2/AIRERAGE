using System;
using System.Collections.Generic;
using System.Globalization;
using Airerage.Core.Combat;
using Airerage.Core.Common;
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
    /// キャラクター選択（docs/15 T6-3、6.9。格闘ゲームによくあるアイコンを選ぶ形。ユーザーの要望、2026-10-19）。
    /// - 真ん中にキャラクターのアイコン（ロゴ）を並べ（四隅は空ける）、四隅に 1P〜4P の選んでいるキャラクター（ロゴ、名前、ハッシュタグ、
    ///   状態）を出す。アイコンの上には人ごとの色の枠と「1P」などの札（カーソル）。
    /// - 技の説明はふだんは出さず、F1（ゲームパッドは Select）を押すと下からせり出す（もう一度で引っ込む。押した人のカーソルの
    ///   キャラクター）。
    /// - 操作は機器を直接読む（同時に何人もが別のカーソルを動かすため）: 移動は 矢印・WASD ／ 十字キー・左スティック、決定は
    ///   Enter・Space ／ A・Start、戻るは Esc・Backspace ／ B。マウスはアイコンに乗せて選び、クリックで決定（キーボードの人）。
    /// 3 つの使い方:
    /// - Single（トレーニング）: 1P だけ。どの機器でも操作できる。決定で Confirmed、戻るで Cancelled。
    /// - Local（ローカル対戦。T6-5）: 機器ごとに参加（決定）して 1P〜4P。決定で READY、戻るで取り消し、もう一度で抜ける。だれも参加して
    ///   いなければ戻るで Cancelled。時間の制限は T ／ Y。2 人以上で全員 READY なら LocalConfirmed。
    /// - Network（ネットワーク対戦。T6-7）: 自分の番号の枠をこの PC のどの機器でも操作する。ほかの人の枠は SetRemote で出す。カーソルを
    ///   動かすと NetworkHovered、決定・取り消しで NetworkReadyChanged、READY でないときの戻るで Cancelled（抜ける）。
    /// </summary>
    public sealed class CharacterSelectScreen : MonoBehaviour
    {
        public enum SelectMode
        {
            Single,
            Local,
            Network,
        }

        public const int MaxPlayers = 4;

        /// <summary>技の説明に出す技の枠（左から Q、E、R）。</summary>
        public static readonly AttackSlot[] SkillSlots = { AttackSlot.Special1, AttackSlot.Special2, AttackSlot.Super };

        private const float DetailsSeconds = 0.2f;

        [SerializeField] private CharacterRoster _roster;
        [Tooltip("キャラクターのアイコン（一覧と同じ順。左上から横に並べる）")]
        [SerializeField] private CharacterIcon[] _icons = new CharacterIcon[0];
        [Tooltip("アイコンの横の数（上下の移動に使う）")]
        [SerializeField] private int _columns = 4;
        [Tooltip("人ごとのカーソル（枠と札。1P〜4P）")]
        [SerializeField] private RectTransform[] _cursors = new RectTransform[0];
        [SerializeField] private Text[] _cursorTags = new Text[0];
        [Tooltip("四隅の枠（1P 左上、2P 右上、3P 左下、4P 右下）の表示")]
        [SerializeField] private GameObject[] _corners = new GameObject[0];
        [SerializeField] private SlantedPlate[] _cornerPlates = new SlantedPlate[0];
        [SerializeField] private Text[] _cornerDevices = new Text[0];
        [SerializeField] private RawImage[] _cornerLogos = new RawImage[0];
        [SerializeField] private Text[] _cornerNames = new Text[0];
        [SerializeField] private Text[] _cornerTags = new Text[0];
        [SerializeField] private Text[] _cornerStates = new Text[0];
        [Tooltip("下からせり出す技の説明（ロゴ、名前、ハッシュタグ、Q・E・R）")]
        [SerializeField] private RectTransform _details;
        [SerializeField] private RawImage _detailLogo;
        [SerializeField] private Text _detailName;
        [SerializeField] private Text _detailTags;
        [Tooltip("技の名前・説明・クールタイム（SkillSlots と同じ順。Q、E、R）")]
        [SerializeField] private Text[] _skillNames = new Text[0];
        [SerializeField] private Text[] _skillDescriptions = new Text[0];
        [SerializeField] private Text[] _skillCooldowns = new Text[0];
        [SerializeField] private Text _hint;
        [SerializeField] private Text _timeLimit;
        [SerializeField] private Text _message;
        [Tooltip("プレイヤーごとの色（1P〜4P）")]
        [SerializeField] private Color[] _colors = new Color[0];

        private static readonly Color EmptyFill = new Color(0.03f, 0.05f, 0.09f, 0.45f);
        private static readonly Color JoinedFill = new Color(0.03f, 0.05f, 0.09f, 0.85f);
        private static readonly Color EmptyBorder = new Color(1f, 1f, 1f, 0.2f);

        private Slot[] _slots = { new Slot(), new Slot(), new Slot(), new Slot() };
        private int _activeFromFrame = int.MaxValue;
        private bool _timed = true;
        private int _localSlot;
        private string _networkMessage = string.Empty;
        private Font _japanese;
        private int _detailsSlot = -1;
        private float _detailsOpen;

        /// <summary>トレーニング: 決定した（キャラクターの番号）。</summary>
        public event Action<int> Confirmed;

        /// <summary>ローカル対戦: 全員が READY になった（参加した人、時間の制限ありか）。</summary>
        public event Action<IReadOnlyList<LocalPlayerSetup>, bool> LocalConfirmed;

        /// <summary>ネットワーク対戦: 自分のカーソルのキャラクターが変わった。</summary>
        public event Action<int> NetworkHovered;

        /// <summary>ネットワーク対戦: 自分の READY が変わった。</summary>
        public event Action<bool> NetworkReadyChanged;

        /// <summary>戻る（トレーニング・ネットワーク: READY でないとき。ローカル: だれも参加していないとき）。</summary>
        public event Action Cancelled;

        public SelectMode Mode { get; private set; }

        /// <summary>アイコンの数（テスト用）。</summary>
        public int Count => _icons.Length;

        /// <summary>番号 index のアイコン（テスト用）。</summary>
        public CharacterIcon Icon(int index) => _icons[index];

        /// <summary>参加している人数（トレーニングは 1、ネットワークはつながっている人数）。</summary>
        public int JoinedCount
        {
            get
            {
                int count = 0;
                foreach (Slot slot in _slots)
                {
                    count += slot.Joined ? 1 : 0;
                }
                return count;
            }
        }

        /// <summary>時間の制限ありか（ローカル対戦）。</summary>
        public bool Timed => _timed;

        /// <summary>番号 slot（1P が 0）の人のカーソルのキャラクター・READY か・参加しているか。</summary>
        public int CharacterOf(int slot) => _slots[slot].Character;
        public bool IsReady(int slot) => _slots[slot].Ready;
        public bool IsJoined(int slot) => _slots[slot].Joined;

        /// <summary>四隅の枠 slot に出している名前・ハッシュタグ・状態（テスト用）。</summary>
        public string ShownName(int slot) => _cornerNames[slot].text;
        public string ShownTags(int slot) => _cornerTags[slot].text;
        public string ShownState(int slot) => _cornerStates[slot].text;

        /// <summary>技の説明を出しているか（せり出している途中も含む）と、そのキャラクター（テスト用）。</summary>
        public bool DetailsOpen => _detailsSlot >= 0;
        public string ShownDetailName => _detailName.text;
        public string ShownSkillName(int index) => _skillNames[index].text;
        public string ShownSkillDescription(int index) => _skillDescriptions[index].text;
        public string ShownSkillCooldown(int index) => _skillCooldowns[index].text;

        // 1 人分の枠。
        private sealed class Slot
        {
            public bool Joined;
            public bool Remote;
            public InputDevice Key;
            public InputDevice[] Devices = new InputDevice[0];
            public string DeviceName = string.Empty;
            public int Character;
            public bool Ready;
        }

        // 機器のまとまり 1 つの、このフレームの操作。
        private struct Intent
        {
            public bool Confirm;
            public bool Back;
            public bool Details;
            public bool ToggleTime;
            public int Dx;
            public int Dy;

            public bool Any => Confirm || Back || Details || ToggleTime || Dx != 0 || Dy != 0;
        }

        private void Awake()
        {
            int skills = SkillSlots.Length;
            if (_roster == null || _icons.Length != _roster.Count || _columns < 1
                || _cursors.Length != MaxPlayers || _cursorTags.Length != MaxPlayers || _corners.Length != MaxPlayers
                || _cornerPlates.Length != MaxPlayers || _cornerDevices.Length != MaxPlayers || _cornerLogos.Length != MaxPlayers
                || _cornerNames.Length != MaxPlayers || _cornerTags.Length != MaxPlayers || _cornerStates.Length != MaxPlayers
                || _details == null || _detailName == null || _detailTags == null
                || _skillNames.Length != skills || _skillDescriptions.Length != skills || _skillCooldowns.Length != skills)
            {
                Debug.LogError($"{name}: キャラクターの一覧・アイコン（{_icons.Length}）・カーソル・四隅の枠・技の説明の表示がそろっていません", this);
                enabled = false;
                return;
            }
            foreach (CharacterIcon icon in _icons)
            {
                icon.Hovered += OnIconHovered;
                icon.Clicked += OnIconClicked;
            }
            UseJapaneseFont();
        }

        private void OnDestroy()
        {
            if (_japanese != null)
            {
                Destroy(_japanese);
            }
        }

        // ハッシュタグと技の文字（日本語）を、PC の日本語の字体で書く（なければ Unity に入っている字体のまま）。
        private void UseJapaneseFont()
        {
            _japanese = JapaneseFont.Create(32);
            if (_japanese == null)
            {
                return;
            }
            _detailTags.font = _japanese;
            foreach (Text tags in _cornerTags)
            {
                tags.font = _japanese;
            }
            for (int i = 0; i < SkillSlots.Length; i++)
            {
                _skillNames[i].font = _japanese;
                _skillDescriptions[i].font = _japanese;
                _skillCooldowns[i].font = _japanese;
            }
        }

        /// <summary>トレーニング: 1P だけで出す（カーソルは initial のキャラクター）。</summary>
        public void ShowSingle(int initial)
        {
            Open(SelectMode.Single);
            ClearSlots();
            Slot slot = _slots[0];
            slot.Joined = true;
            slot.Character = _roster.Clamp(initial);
            slot.DeviceName = "TRAINING";
            Refresh();
        }

        /// <summary>
        /// ローカル対戦: 出す。reset なら参加をやり直す（メインメニューから）。そうでなければ参加した人とキャラクターを残し、READY だけを
        /// 外す（ステージ選択から戻ったとき）。
        /// </summary>
        public void ShowLocal(bool reset)
        {
            Open(SelectMode.Local);
            if (reset)
            {
                ClearSlots();
                _timed = true;
            }
            foreach (Slot slot in _slots)
            {
                slot.Ready = false;
            }
            Refresh();
        }

        /// <summary>
        /// ローカル対戦: 試合の結果から戻ったときに出す（docs/15 T6-8）。players の人（機器）とキャラクターで参加した状態にし、READY は外す。
        /// 今はつながっていない機器の人は入れない。
        /// </summary>
        public void ShowLocal(IReadOnlyList<LocalPlayerSetup> players, bool timed)
        {
            Open(SelectMode.Local);
            ClearSlots();
            _timed = timed;
            int index = 0;
            foreach (LocalPlayerSetup player in players)
            {
                if (index >= MaxPlayers || player.Devices.Length == 0 || !player.Devices[0].added)
                {
                    continue;
                }
                Slot slot = _slots[index++];
                slot.Joined = true;
                slot.Key = player.Devices[0];
                slot.Devices = player.Devices;
                slot.DeviceName = player.DeviceName;
                slot.Character = _roster.Clamp(player.Character);
                slot.Ready = false;
            }
            Refresh();
        }

        /// <summary>ネットワーク対戦: 自分の番号 localSlot の枠を、この PC の機器で操作する（カーソルは initial のキャラクター）。</summary>
        public void ShowNetwork(int localSlot, int initial)
        {
            Open(SelectMode.Network);
            ClearSlots();
            _localSlot = Mathf.Clamp(localSlot, 0, MaxPlayers - 1);
            Slot slot = _slots[_localSlot];
            slot.Joined = true;
            slot.Character = _roster.Clamp(initial);
            slot.DeviceName = "YOU";
            Refresh();
        }

        /// <summary>ネットワーク対戦: ほかの人の枠（つながっているか、名前、カーソルのキャラクター、READY か）。自分の枠は変えない。</summary>
        public void SetRemote(int slotIndex, bool connected, string playerName, int character, bool ready)
        {
            if (Mode != SelectMode.Network || slotIndex < 0 || slotIndex >= MaxPlayers || slotIndex == _localSlot)
            {
                return;
            }
            Slot slot = _slots[slotIndex];
            int clamped = connected ? _roster.Clamp(character) : 0;
            string deviceName = string.IsNullOrEmpty(playerName) ? $"P{slotIndex + 1}" : playerName;
            if (slot.Joined == connected && slot.Character == clamped && slot.Ready == ready && slot.DeviceName == deviceName)
            {
                return;
            }
            slot.Joined = connected;
            slot.Remote = true;
            slot.Character = clamped;
            slot.Ready = connected && ready;
            slot.DeviceName = deviceName;
            Refresh();
        }

        /// <summary>ネットワーク対戦: 自分の READY をそろえる（ホストが外したときなど。NetworkReadyChanged は出さない）。</summary>
        public void SetLocalReady(bool ready)
        {
            if (Mode != SelectMode.Network || _slots[_localSlot].Ready == ready)
            {
                return;
            }
            _slots[_localSlot].Ready = ready;
            Refresh();
        }

        /// <summary>ネットワーク対戦: 下に出す案内（だれを待っているか、など）。</summary>
        public void SetNetworkMessage(string message)
        {
            message = message ?? string.Empty;
            if (_networkMessage == message)
            {
                return;
            }
            _networkMessage = message;
            Refresh();
        }

        public void Hide()
        {
            _activeFromFrame = int.MaxValue;
            _detailsSlot = -1;
            _detailsOpen = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>技の説明を出すか引っ込める（slot の人のカーソルのキャラクター。同じ人がもう一度押したら引っ込める）。</summary>
        public void ToggleDetails(int slot)
        {
            _detailsSlot = _detailsSlot == slot ? -1 : slot;
            if (_detailsSlot >= 0)
            {
                ShowDetails(_slots[_detailsSlot].Character);
            }
            UiSounds.Find().Play(SoundId.UiMove);
        }

        // 開いたボタンで決まらないよう、受け付けるのは次のフレームから。
        private void Open(SelectMode mode)
        {
            gameObject.SetActive(true);
            Mode = mode;
            _detailsSlot = -1;
            _detailsOpen = 0f;
            _networkMessage = string.Empty;
            _activeFromFrame = Time.frameCount + 1;
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void ClearSlots()
        {
            foreach (Slot slot in _slots)
            {
                slot.Joined = false;
                slot.Remote = false;
                slot.Key = null;
                slot.Devices = new InputDevice[0];
                slot.DeviceName = string.Empty;
                slot.Character = 0;
                slot.Ready = false;
            }
        }

        private void Update()
        {
            UpdateDetailsSlide();
            if (Time.frameCount < _activeFromFrame)
            {
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Handle(keyboard, ReadKeyboard(keyboard));
            }
            foreach (Gamepad gamepad in Gamepad.all)
            {
                Handle(gamepad, ReadGamepad(gamepad));
            }
            if (isActiveAndEnabled && Mode == SelectMode.Local && JoinedCount >= 2 && AllJoinedReady())
            {
                var players = new List<LocalPlayerSetup>();
                foreach (Slot slot in _slots)
                {
                    if (slot.Joined)
                    {
                        players.Add(new LocalPlayerSetup(slot.Devices, slot.DeviceName, slot.Character));
                    }
                }
                LocalConfirmed?.Invoke(players, _timed);
            }
        }

        private static Intent ReadKeyboard(Keyboard k)
        {
            return new Intent
            {
                Confirm = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame,
                Back = k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame,
                Details = k.f1Key.wasPressedThisFrame,
                ToggleTime = k.tKey.wasPressedThisFrame,
                Dx = Pressed(k.aKey, k.leftArrowKey) ? -1 : Pressed(k.dKey, k.rightArrowKey) ? 1 : 0,
                Dy = Pressed(k.wKey, k.upArrowKey) ? -1 : Pressed(k.sKey, k.downArrowKey) ? 1 : 0,
            };
        }

        private static Intent ReadGamepad(Gamepad g)
        {
            return new Intent
            {
                Confirm = g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame,
                Back = g.buttonEast.wasPressedThisFrame,
                Details = g.selectButton.wasPressedThisFrame,
                ToggleTime = g.buttonNorth.wasPressedThisFrame,
                Dx = Pressed(g.dpad.left, g.leftStick.left) ? -1 : Pressed(g.dpad.right, g.leftStick.right) ? 1 : 0,
                Dy = Pressed(g.dpad.up, g.leftStick.up) ? -1 : Pressed(g.dpad.down, g.leftStick.down) ? 1 : 0,
            };
        }

        private static bool Pressed(ButtonControl a, ButtonControl b)
        {
            return a.wasPressedThisFrame || b.wasPressedThisFrame;
        }

        // 機器のまとまり 1 つ（キーボード＋マウス、ゲームパッド）の操作を、その人の枠に効かせる。
        private void Handle(InputDevice key, Intent intent)
        {
            if (!isActiveAndEnabled || !intent.Any)
            {
                return;
            }
            int slotIndex;
            switch (Mode)
            {
                case SelectMode.Single:
                    slotIndex = 0;
                    break;
                case SelectMode.Network:
                    slotIndex = _localSlot;
                    break;
                default:
                    slotIndex = Array.FindIndex(_slots, s => s.Joined && s.Key == key);
                    if (slotIndex < 0)
                    {
                        if (intent.Confirm)
                        {
                            Join(key);
                        }
                        else if (intent.Back && JoinedCount == 0)
                        {
                            Cancelled?.Invoke();
                        }
                        return;
                    }
                    break;
            }
            Slot slot = _slots[slotIndex];
            if (intent.Details)
            {
                ToggleDetails(slotIndex);
            }
            if (intent.ToggleTime && Mode == SelectMode.Local)
            {
                _timed = !_timed;
                UiSounds.Find().Play(SoundId.UiMove);
            }
            if (intent.Back)
            {
                Back(slotIndex);
            }
            else if (intent.Confirm && !slot.Ready)
            {
                Confirm(slotIndex);
            }
            else if ((intent.Dx != 0 || intent.Dy != 0) && !slot.Ready)
            {
                SetCharacter(slotIndex, Move(slot.Character, intent.Dx, intent.Dy));
            }
            Refresh();
        }

        private void Join(InputDevice key)
        {
            int free = Array.FindIndex(_slots, s => !s.Joined);
            if (free < 0)
            {
                return;
            }
            var devices = new List<InputDevice> { key };
            if (key is Keyboard && Mouse.current != null)
            {
                devices.Add(Mouse.current);
            }
            Slot slot = _slots[free];
            slot.Joined = true;
            slot.Key = key;
            slot.Devices = devices.ToArray();
            slot.DeviceName = key is Keyboard ? "KEYBOARD" : $"GAMEPAD {GamepadNumber(key)}";
            // 最初の人は前に選んだキャラクター（トレーニングと同じ）、ほかは番号の順。
            slot.Character = JoinedCount == 1
                ? _roster.Clamp(PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0))
                : free % _roster.Count;
            slot.Ready = false;
            UiSounds.Find().Play(SoundId.UiConfirm);
            Refresh();
        }

        private static int GamepadNumber(InputDevice gamepad)
        {
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i] == gamepad)
                {
                    return i + 1;
                }
            }
            return 1;
        }

        private void Confirm(int slotIndex)
        {
            Slot slot = _slots[slotIndex];
            UiSounds.Find().Play(SoundId.UiConfirm);
            switch (Mode)
            {
                case SelectMode.Single:
                    Confirmed?.Invoke(slot.Character);
                    break;
                case SelectMode.Local:
                    slot.Ready = true;
                    break;
                case SelectMode.Network:
                    slot.Ready = true;
                    NetworkReadyChanged?.Invoke(true);
                    break;
            }
        }

        private void Back(int slotIndex)
        {
            Slot slot = _slots[slotIndex];
            UiSounds.Find().Play(SoundId.UiMove);
            if (slot.Ready)
            {
                slot.Ready = false;
                if (Mode == SelectMode.Network)
                {
                    NetworkReadyChanged?.Invoke(false);
                }
                return;
            }
            if (Mode == SelectMode.Local)
            {
                // 抜ける（後ろの人の番号を詰める。試合の番号と同じになるように）。
                for (int j = slotIndex; j < MaxPlayers - 1; j++)
                {
                    _slots[j] = _slots[j + 1];
                }
                _slots[MaxPlayers - 1] = new Slot();
                _detailsSlot = _detailsSlot == slotIndex ? -1 : _detailsSlot > slotIndex ? _detailsSlot - 1 : _detailsSlot;
                return;
            }
            Cancelled?.Invoke();
        }

        private void SetCharacter(int slotIndex, int character)
        {
            Slot slot = _slots[slotIndex];
            if (slot.Character == character)
            {
                return;
            }
            slot.Character = character;
            UiSounds.Find().Play(SoundId.UiMove);
            if (Mode == SelectMode.Network && slotIndex == _localSlot)
            {
                NetworkHovered?.Invoke(character);
            }
            if (_detailsSlot == slotIndex)
            {
                ShowDetails(character);
            }
        }

        /// <summary>
        /// アイコンの並び（横に columns 個ずつ、count 個）で、index から (dx, dy) だけ動いた先（端は反対側へ回る。最後の行が短いときは、
        /// その行の中で回る）。
        /// </summary>
        public static int MoveInGrid(int index, int dx, int dy, int count, int columns)
        {
            if (count <= 0)
            {
                return 0;
            }
            columns = Mathf.Clamp(columns, 1, count);
            int rows = (count + columns - 1) / columns;
            int row = index / columns;
            int column = index % columns;
            if (dy != 0)
            {
                row = (row + dy + rows) % rows;
                int rowLength = Mathf.Min(columns, count - row * columns);
                column = Mathf.Min(column, rowLength - 1);
            }
            if (dx != 0)
            {
                int rowLength = Mathf.Min(columns, count - row * columns);
                column = (column + dx + rowLength) % rowLength;
            }
            return row * columns + column;
        }

        private int Move(int index, int dx, int dy)
        {
            return MoveInGrid(index, dx, dy, _icons.Length, _columns);
        }

        // マウス: 乗せたアイコンへ、マウスを持つ人のカーソルを動かす。
        private void OnIconHovered(int character)
        {
            int slotIndex = MouseSlot();
            if (slotIndex < 0 || _slots[slotIndex].Ready || Time.frameCount < _activeFromFrame)
            {
                return;
            }
            SetCharacter(slotIndex, character);
            Refresh();
        }

        private void OnIconClicked(int character)
        {
            int slotIndex = MouseSlot();
            if (slotIndex < 0 || _slots[slotIndex].Ready || Time.frameCount < _activeFromFrame)
            {
                return;
            }
            SetCharacter(slotIndex, character);
            Confirm(slotIndex);
            Refresh();
        }

        // マウスで操作する枠（トレーニングは 1P、ネットワークは自分、ローカルはキーボードの人。いなければ -1）。
        private int MouseSlot()
        {
            switch (Mode)
            {
                case SelectMode.Single:
                    return 0;
                case SelectMode.Network:
                    return _localSlot;
                default:
                    return Array.FindIndex(_slots, s => s.Joined && s.Key is Keyboard);
            }
        }

        private bool AllJoinedReady()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Joined && !slot.Ready)
                {
                    return false;
                }
            }
            return true;
        }

        private void Refresh()
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                RefreshCorner(i);
                RefreshCursor(i);
            }
            if (_hint != null)
            {
                _hint.text = Mode == SelectMode.Local
                    ? "ENTER / A: JOIN, READY     ESC / B: BACK     F1 / SELECT: DETAILS     T / Y: TIME LIMIT"
                    : "ENTER / A: OK     ESC / B: BACK     F1 / SELECT: DETAILS";
            }
            if (_timeLimit != null)
            {
                _timeLimit.text = Mode == SelectMode.Local ? $"TIME LIMIT: {(_timed ? "ON" : "OFF")}" : string.Empty;
            }
            if (_message != null)
            {
                _message.text = MessageText();
            }
        }

        private string MessageText()
        {
            switch (Mode)
            {
                case SelectMode.Local:
                    return JoinedCount < 2 ? "2-4 PLAYERS: JOIN WITH ENTER (KEYBOARD) OR A (GAMEPAD)" : string.Empty;
                case SelectMode.Network:
                    return _networkMessage;
                default:
                    return string.Empty;
            }
        }

        private void RefreshCorner(int i)
        {
            Slot slot = _slots[i];
            bool shown = Mode != SelectMode.Single || i == 0;
            _corners[i].SetActive(shown);
            if (!shown)
            {
                return;
            }
            Color color = i < _colors.Length ? _colors[i] : Color.white;
            if (!slot.Joined)
            {
                _cornerPlates[i].color = EmptyFill;
                _cornerPlates[i].BorderColor = EmptyBorder;
                _cornerDevices[i].text = string.Empty;
                _cornerLogos[i].enabled = false;
                _cornerNames[i].text = string.Empty;
                _cornerTags[i].text = string.Empty;
                _cornerStates[i].text = Mode == SelectMode.Local ? "PRESS ENTER / A" : "EMPTY";
                _cornerStates[i].color = new Color(1f, 1f, 1f, 0.5f);
                return;
            }
            CharacterConfigAsset character = _roster.Get(slot.Character);
            _cornerPlates[i].color = JoinedFill;
            _cornerPlates[i].BorderColor = slot.Ready ? color : new Color(color.r, color.g, color.b, 0.55f);
            _cornerDevices[i].text = slot.DeviceName;
            _cornerLogos[i].texture = character.Logo;
            _cornerLogos[i].enabled = character.Logo != null;
            _cornerNames[i].text = character.DisplayName;
            _cornerTags[i].text = TagsText(character.Tags);
            _cornerStates[i].text = slot.Ready ? "READY!" : "SELECTING";
            _cornerStates[i].color = slot.Ready ? color : new Color(1f, 1f, 1f, 0.7f);
        }

        // カーソル: 参加している人のアイコンに、人ごとの色の枠（重なっても見えるよう、番号ごとに少し大きく）と札（アイコンの四隅）。
        private void RefreshCursor(int i)
        {
            Slot slot = _slots[i];
            bool shown = slot.Joined && (Mode != SelectMode.Single || i == 0);
            _cursors[i].gameObject.SetActive(shown);
            if (!shown)
            {
                return;
            }
            RectTransform icon = _icons[slot.Character].Rect;
            RectTransform cursor = _cursors[i];
            cursor.SetParent(icon.parent, false);
            cursor.SetAsLastSibling();
            cursor.anchorMin = icon.anchorMin;
            cursor.anchorMax = icon.anchorMax;
            cursor.pivot = icon.pivot;
            cursor.anchoredPosition = icon.anchoredPosition;
            float grow = 8f + i * 7f;
            cursor.sizeDelta = icon.sizeDelta + new Vector2(grow, grow) * 2f;
            Color color = i < _colors.Length ? _colors[i] : Color.white;
            _cursorTags[i].text = $"{i + 1}P";
            _cursorTags[i].color = color;
            var frame = cursor.GetComponent<SlantedPlate>();
            if (frame != null)
            {
                frame.BorderColor = slot.Ready ? Color.white : color;
            }
        }

        private void UpdateDetailsSlide()
        {
            if (_details == null)
            {
                return;
            }
            float target = _detailsSlot >= 0 ? 1f : 0f;
            float step = DetailsSeconds > 0f ? Time.unscaledDeltaTime / DetailsSeconds : 1f;
            _detailsOpen = Mathf.MoveTowards(_detailsOpen, target, step);
            if (_detailsSlot >= 0)
            {
                ShowDetails(_slots[_detailsSlot].Character);
            }
            float eased = 1f - (1f - _detailsOpen) * (1f - _detailsOpen);
            float height = _details.rect.height;
            _details.anchoredPosition = new Vector2(0f, Mathf.Lerp(-height - 20f, 0f, eased));
            _details.gameObject.SetActive(_detailsOpen > 0f);
            if (_hint != null)
            {
                // 技の説明を出している間は、下の操作の案内を隠す（説明に重なるため。説明の右上に閉じ方を出す）。
                _hint.enabled = _detailsOpen <= 0f;
            }
        }

        private void ShowDetails(int index)
        {
            CharacterConfigAsset character = _roster.Get(index);
            if (character == null)
            {
                return;
            }
            _detailName.text = character.DisplayName;
            _detailTags.text = TagsText(character.Tags);
            if (_detailLogo != null)
            {
                _detailLogo.texture = character.Logo;
                _detailLogo.enabled = character.Logo != null;
            }
            CombatTuning combat = character.Config.Combat;
            for (int i = 0; i < SkillSlots.Length; i++)
            {
                AttackSlot slot = SkillSlots[i];
                SkillText text = character.TextOf(slot);
                _skillNames[i].text = text != null && !string.IsNullOrEmpty(text.Name) ? text.Name : combat.Get(slot).Name;
                _skillDescriptions[i].text = text != null ? text.Description : string.Empty;
                _skillCooldowns[i].text = CooldownText(combat, slot, text != null ? text.CooldownNote : string.Empty);
            }
        }

        /// <summary>ハッシュタグを 1 行に並べる。</summary>
        public static string TagsText(string[] tags)
        {
            return tags == null ? string.Empty : string.Join("  ", tags);
        }

        /// <summary>
        /// 技の説明の下に出す行: 特殊攻撃はクールタイム（数え始めるときの注記と、2 回以上続けて使えるならスタック数）、必殺技は必要なゲージ。
        /// </summary>
        public static string CooldownText(CombatTuning combat, AttackSlot slot, string cooldownNote)
        {
            if (slot == AttackSlot.Super)
            {
                return $"必要ゲージ {GaugeText(combat.SuperGaugeCost)}";
            }
            string text = $"クールタイム {SecondsText(combat.CooldownTicksOf(slot))}";
            if (!string.IsNullOrEmpty(cooldownNote))
            {
                text += $"（{cooldownNote}）";
            }
            int charges = combat.MaxChargesOf(slot);
            if (charges >= 2)
            {
                text += $"  ／  スタック {charges}";
            }
            return text;
        }

        // 秒（端数があれば小数 1 桁）。
        private static string SecondsText(int ticks)
        {
            return ticks % SimTime.TicksPerSecond == 0
                ? $"{ticks / SimTime.TicksPerSecond} 秒"
                : ((float)ticks / SimTime.TicksPerSecond).ToString("0.0", CultureInfo.InvariantCulture) + " 秒";
        }

        // ゲージ（0.1% 単位）を % で（端数があれば小数 1 桁。D-60）。
        private static string GaugeText(int gauge)
        {
            int per = CombatTuning.GaugePerPercent;
            return gauge % per == 0 ? $"{gauge / per}%" : $"{gauge / per}.{gauge % per}%";
        }
    }
}
