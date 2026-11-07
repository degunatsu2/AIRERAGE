using System;
using System.Collections.Generic;
using System.Globalization;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Settings;
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

        /// <summary>枠の数（ネットワーク対戦のチーム対戦は 8 人まで。docs/19 T9-4）。</summary>
        public const int MaxPlayers = 8;

        /// <summary>ローカル対戦で機器ごとに参加できる人数（D-85。CPU は別）。</summary>
        public const int MaxLocalPlayers = 4;

        /// <summary>5 人以上のときの枠の縮め方（四隅に 2 枚ずつ重ねて並べる。5P〜8P の枠は、この大きさで 1P〜4P の内側に置く）。</summary>
        public const float CompactCornerScale = 0.6f;

        /// <summary>技の説明に出す技の枠（左から Q、E、R）。</summary>
        public static readonly AttackSlot[] SkillSlots = { AttackSlot.Special1, AttackSlot.Special2, AttackSlot.Super };

        private const float DetailsSeconds = 0.2f;

        /// <summary>
        /// トレーニング・シングルモードで決めてから次の画面へ進むまでの秒数（決定の演出を見せる。D-89。その間にもう一度決定を押すと
        /// すぐ進み、戻るを押すと取り消す）。
        /// </summary>
        public const float SingleHoldSeconds = 0.55f;

        /// <summary>ローカル対戦で全員が READY になってから次の画面へ進むまでの秒数（全員そろった帯を見せる。D-89 の C17）。</summary>
        public const float AllReadyHoldSeconds = 1f;

        /// <summary>RANDOM のルーレットで、カーソルがアイコンを移る回数（D-89 の C18）。</summary>
        public const int RollHops = 12;

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

        /// <summary>アイコンの並びの下の知らせ（テスト用）。</summary>
        public string ShownMessage => _message != null ? _message.text : string.Empty;
        [Tooltip("プレイヤーごとの色（1P〜4P）")]
        [SerializeField] private Color[] _colors = new Color[0];

        private static readonly Color EmptyFill = new Color(0.03f, 0.05f, 0.09f, 0.45f);
        private static readonly Color JoinedFill = new Color(0.03f, 0.05f, 0.09f, 0.85f);
        private static readonly Color EmptyBorder = new Color(1f, 1f, 1f, 0.2f);

        private Slot[] _slots = NewSlots();
        private int _activeFromFrame = int.MaxValue;
        private bool _timed = true;

        // ローカル対戦のチーム対戦と CPU（docs/19 T9-6。D-85: 人は 4 人まで、残りを CPU で、個人戦は合わせて 4 人・チーム対戦は 8 人まで）。
        private bool _teamMode;
        private int _cpuCount;
        private int _localSlot;
        private string _networkMessage = string.Empty;
        private GameLanguage _language = GameLanguage.Japanese;
        private int _detailsSlot = -1;
        private float _detailsOpen;
        // 決定の演出の待ち（トレーニング・シングルモードの決定、ローカル対戦の全員 READY。D-89）。
        private float _confirmAt = -1f;
        private float _allReadySince = -1f;
        // アイコンの置き場所（演出で動かしても、カーソルはここに置く）と、四隅の枠に出している名前（文字の演出の途中でも本来の名前）。
        private Vector2[] _iconHomes = new Vector2[0];
        private readonly string[] _shownNames = new string[MaxPlayers];

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
        public int Count => _roster != null ? _roster.Count : 0;

        /// <summary>RANDOM のアイコンの番号（キャラクターの番号の後ろ。決めたときにランダムなキャラクターに変わる）。</summary>
        public int RandomIndex => Count;

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

        /// <summary>ローカル対戦をチーム対戦で始めるか（M ／ X で切り替える）。</summary>
        public bool TeamMode => _teamMode;

        /// <summary>ローカル対戦に入れる CPU の数（C ／ LB で 0 から上限まで回す）。</summary>
        public int CpuCount => _cpuCount;

        /// <summary>ローカル対戦の人と CPU を合わせた上限（個人戦 4 人、チーム対戦 8 人）。</summary>
        public int LocalCapacity => _teamMode ? MaxPlayers : MaxLocalPlayers;

        /// <summary>参加した人 slotIndex のチーム（チーム対戦のときだけ意味がある）。</summary>
        public int TeamOf(int slotIndex) => slotIndex >= 0 && slotIndex < _slots.Length ? _slots[slotIndex].Team : 0;

        /// <summary>CPU のチーム（チーム対戦で、人数の少ないチームから順に入れる。テスト用にも公開）。</summary>
        public byte[] CpuTeams()
        {
            var teams = new byte[_cpuCount];
            if (!_teamMode)
            {
                return teams;
            }
            var counts = new int[TeamColorsCount + 1];
            bool[] used = new bool[TeamColorsCount + 1];
            foreach (Slot slot in _slots)
            {
                if (slot.Joined)
                {
                    counts[slot.Team]++;
                    used[slot.Team] = true;
                }
            }
            // 人のチームが 1 つだけなら、CPU は別のチーム（RED なら BLUE、ほかなら RED）から入れる（2 チーム以上にする）。
            int distinct = 0;
            for (int t = 1; t <= TeamColorsCount; t++)
            {
                distinct += used[t] ? 1 : 0;
            }
            if (distinct < 2)
            {
                used[1] = true;
                used[2] = true;
            }
            for (int c = 0; c < teams.Length; c++)
            {
                int best = -1;
                for (int t = 1; t <= TeamColorsCount; t++)
                {
                    if (used[t] && (best < 0 || counts[t] < counts[best]))
                    {
                        best = t;
                    }
                }
                teams[c] = (byte)best;
                counts[best]++;
            }
            return teams;
        }

        private const int TeamColorsCount = 4;

        // ローカル対戦を始められるか（人が 1 人以上、人と CPU で 2 人以上、チーム対戦なら 2 チーム以上）。
        private bool CanStartLocal
        {
            get
            {
                int humans = JoinedCount;
                return humans >= 1 && humans + _cpuCount >= 2 && LocalProblem() == null;
            }
        }

        // ローカル対戦で始められない理由（なければ null）。
        private string LocalProblem()
        {
            if (!_teamMode)
            {
                return null;
            }
            int mask = 0;
            foreach (Slot slot in _slots)
            {
                if (slot.Joined)
                {
                    mask |= 1 << slot.Team;
                }
            }
            foreach (byte team in CpuTeams())
            {
                mask |= 1 << team;
            }
            int teams = 0;
            for (; mask != 0; mask &= mask - 1)
            {
                teams++;
            }
            return teams >= 2 ? null : Loc.T("MAKE AT LEAST 2 TEAMS", "2 つ以上のチームに分かれてください");
        }

        // ローカル対戦の試合に出る人（参加した人の順、その後に CPU。CPU のキャラクターはランダム、強さは CPU LEVEL の画面の値）。
        private List<LocalPlayerSetup> LocalPlayers()
        {
            var players = new List<LocalPlayerSetup>();
            foreach (Slot slot in _slots)
            {
                if (slot.Joined)
                {
                    players.Add(new LocalPlayerSetup(slot.Devices, slot.DeviceName, slot.Character) { Team = _teamMode ? slot.Team : (byte)0 });
                }
            }
            var level = (Airerage.Core.Ai.CpuLevel)Mathf.Clamp(PlayerPrefs.GetInt(CpuLevelScreen.PreferenceKey, (int)Airerage.Core.Ai.CpuLevel.Normal),
                0, Enum.GetValues(typeof(Airerage.Core.Ai.CpuLevel)).Length - 1);
            byte[] teams = CpuTeams();
            for (int c = 0; c < _cpuCount; c++)
            {
                LocalPlayerSetup cpu = LocalPlayerSetup.Cpu(UnityEngine.Random.Range(0, Count), level);
                cpu.Team = teams[c];
                players.Add(cpu);
            }
            return players;
        }

        // ローカル対戦の設定（だれの機器でも）: M ／ X で個人戦とチーム対戦、C ／ LB で CPU の数、Q ／ RB で自分のチーム（チーム対戦のとき）。
        private void HandleLocalSettings(int slotIndex, Intent intent)
        {
            if (intent.ToggleMode)
            {
                _teamMode = !_teamMode;
                if (_teamMode)
                {
                    // チーム対戦にしたら、参加した順に RED と BLUE に分ける（各自が Q ／ RB で変えられる）。
                    int n = 0;
                    foreach (Slot slot in _slots)
                    {
                        if (slot.Joined)
                        {
                            slot.Team = (byte)(n++ % 2 + 1);
                        }
                    }
                }
                ClampCpus();
                UiSounds.Find().Play(SoundId.UiMove);
            }
            if (intent.CycleCpu)
            {
                int max = Mathf.Max(0, LocalCapacity - JoinedCount);
                _cpuCount = max == 0 ? 0 : (_cpuCount + 1) % (max + 1);
                UiSounds.Find().Play(SoundId.UiMove);
            }
            if (intent.CycleTeam && _teamMode && slotIndex >= 0)
            {
                Slot slot = _slots[slotIndex];
                slot.Team = (byte)(slot.Team % TeamColorsCount + 1);
                UiSounds.Find().Play(SoundId.UiMove);
            }
        }

        // CPU の数を、上限（人と合わせて個人戦 4 人・チーム対戦 8 人）に収める。
        private void ClampCpus()
        {
            _cpuCount = Mathf.Clamp(_cpuCount, 0, Mathf.Max(0, LocalCapacity - JoinedCount));
        }

        /// <summary>番号 slot（1P が 0）の人のカーソルのキャラクター・READY か・参加しているか。</summary>
        public int CharacterOf(int slot) => _slots[slot].Character;
        public bool IsReady(int slot) => _slots[slot].Ready;
        public bool IsJoined(int slot) => _slots[slot].Joined;

        /// <summary>四隅の枠 slot に出している名前・ハッシュタグ・状態（テスト用）。</summary>
        public string ShownName(int slot) => _shownNames[slot] ?? _cornerNames[slot].text;
        public string ShownTags(int slot) => _cornerTags[slot].text;
        public string ShownState(int slot) => _cornerStates[slot].text;

        // ---- 演出（CharacterSelectMotion。D-89）が読む値 ----------------------------------------------------------------------

        /// <summary>アイコンの数（キャラクター＋RANDOM）。</summary>
        public int IconCount => _icons.Length;

        /// <summary>index のアイコンの置き場所（演出でずらしても変わらない）。</summary>
        public Vector2 IconHome(int index) => index < _iconHomes.Length ? _iconHomes[index] : _icons[index].Rect.anchoredPosition;

        /// <summary>index のアイコンのキャラクター（RANDOM なら null）。</summary>
        public CharacterConfigAsset CharacterAt(int index) => _roster != null && index < _roster.Count ? _roster.Get(index) : null;

        /// <summary>slot の人のカーソルが今いるアイコン（RANDOM のルーレットの途中は、ルーレットが今いるアイコン）。</summary>
        public int CursorIndex(int slot) => _slots[slot].Rolling ? _slots[slot].RollShown : _slots[slot].Character;

        /// <summary>slot の人が RANDOM のルーレットの途中か。</summary>
        public bool IsRolling(int slot) => _slots[slot].Rolling;

        /// <summary>slot の人のカーソルを出しているか。</summary>
        public bool CursorShown(int slot) => _cursors[slot].gameObject.activeSelf;

        /// <summary>slot の人の四隅の枠を出しているか。</summary>
        public bool CornerShown(int slot) => _corners[slot].activeSelf;

        public RectTransform CursorRect(int slot) => _cursors[slot];
        public RectTransform CornerRect(int slot) => (RectTransform)_corners[slot].transform;
        public SlantedPlate CornerPlate(int slot) => _cornerPlates[slot];
        public RawImage CornerLogo(int slot) => _cornerLogos[slot];
        public Text CornerName(int slot) => _cornerNames[slot];
        public Text CornerTags(int slot) => _cornerTags[slot];

        /// <summary>この画面のイメージカラー（入ってきた項目の色。2026-11-08）。</summary>
        public Color Accent { get; private set; } = MenuAccents.Default;

        /// <summary>
        /// この画面のイメージカラーを accent にする（以前は水色だった文字と線: 時間の制限、ハッシュタグ、技の説明の Q・E・R と見出しの線、
        /// RANDOM の「?」。入ってきた項目 SINGLE・LOCAL・NETWORK・TRAINING の色。2026-11-08 のユーザーの要望）。
        /// </summary>
        public void SetAccent(Color accent)
        {
            Accent = accent;
            AccentTint.Apply(this, accent);
        }

        /// <summary>slot の人の色（ローカル対戦のチーム対戦ならチームの色）。</summary>
        public Color PlayerColor(int slot)
        {
            if (Mode == SelectMode.Local && _teamMode && _slots[slot].Joined)
            {
                return Airerage.Runtime.Presentation.TeamColors.Of(_slots[slot].Team);
            }
            return slot < _colors.Length ? _colors[slot] : Color.white;
        }

        /// <summary>
        /// 全員そろったか（ローカル対戦・ネットワーク対戦で、参加している人が全員 READY。ローカル対戦は始められる人数のとき。D-89 の C17）。
        /// </summary>
        public bool AllReady
        {
            get
            {
                if (Mode == SelectMode.Single || JoinedCount == 0)
                {
                    return false;
                }
                return AllJoinedReady() && (Mode != SelectMode.Local || CanStartLocal);
            }
        }

        /// <summary>技の説明を出しているか（せり出している途中も含む）と、そのキャラクター（テスト用）。</summary>
        public bool DetailsOpen => _detailsSlot >= 0;
        public string ShownDetailName => _detailName.text;
        public string ShownSkillName(int index) => _skillNames[index].text;
        public string ShownSkillDescription(int index) => DescriptionOf(_skillDescriptions[index]);
        public string ShownSkillCooldown(int index) => _skillCooldowns[index].text;

        // 折り返しの決まりで出した文の、改行を入れる前の文（なければ出している文）。
        private static string DescriptionOf(Text text)
        {
            LineBreakText wrapper = text.GetComponent<LineBreakText>();
            return wrapper != null ? wrapper.Value : text.text;
        }

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

            // RANDOM のルーレット（D-89 の C18）: 回っている途中か、行き先、今いるアイコン、移った回数、次に移る時刻。
            public bool Rolling;
            public int RollResult;
            public int RollShown;
            public int RollHop;
            public float RollNextAt;

            // チーム対戦のチーム（ローカル対戦。1〜4。T9-6）。
            public byte Team = 1;
        }

        // 機器のまとまり 1 つの、このフレームの操作。
        private struct Intent
        {
            public bool Confirm;
            public bool Back;
            public bool Details;
            public bool ToggleTime;
            public bool ToggleMode;
            public bool CycleTeam;
            public bool CycleCpu;
            public int Dx;
            public int Dy;

            public bool Any => Confirm || Back || Details || ToggleTime || ToggleMode || CycleTeam || CycleCpu || Dx != 0 || Dy != 0;
        }

        private void Awake()
        {
            int skills = SkillSlots.Length;
            if (_roster == null || _icons.Length != _roster.Count + 1 || _columns < 1
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
            _iconHomes = new Vector2[_icons.Length];
            for (int i = 0; i < _icons.Length; i++)
            {
                _iconHomes[i] = _icons[i].Rect.anchoredPosition;
            }
            UseJapaneseFont();
        }

        // ハッシュタグと技の名前・クールタイムを、日本語なら日本語の字体（M PLUS 1）で、英語なら英字の字体（Orbitron）で書く（太さは
        // そのまま。言語はオプションの LANGUAGE。2026-10-27）。技の説明は文なので、どちらの言語でも M PLUS 1（ビルダーが設定する）。
        private void UseJapaneseFont()
        {
            _language = GameSettings.LoadLanguage();
            bool ja = _language == GameLanguage.Japanese;
            _detailTags.font = GameFonts.Switch(_detailTags.font, ja);
            foreach (Text tags in _cornerTags)
            {
                tags.font = GameFonts.Switch(tags.font, ja);
            }
            for (int i = 0; i < SkillSlots.Length; i++)
            {
                _skillNames[i].font = GameFonts.Switch(_skillNames[i].font, ja);
                _skillCooldowns[i].font = GameFonts.Switch(_skillCooldowns[i].font, ja);
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
                _teamMode = false;
                _cpuCount = 0;
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
            _teamMode = false;
            _cpuCount = 0;
            int index = 0;
            foreach (LocalPlayerSetup player in players)
            {
                // 前の試合のチーム対戦と CPU の数を引き継ぐ。
                _teamMode |= player.Team != 0;
                if (player.IsCpu)
                {
                    _cpuCount++;
                    continue;
                }
                if (index >= MaxLocalPlayers || player.Devices.Length == 0 || !player.Devices[0].added)
                {
                    continue;
                }
                Slot slot = _slots[index++];
                slot.Joined = true;
                slot.Key = player.Devices[0];
                slot.Devices = player.Devices;
                slot.DeviceName = player.DeviceName;
                slot.Character = _roster.Clamp(player.Character);
                slot.Team = player.Team != 0 ? player.Team : (byte)1;
                slot.Ready = false;
            }
            ClampCpus();
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
            UseJapaneseFont();
            _detailsSlot = -1;
            _detailsOpen = 0f;
            _networkMessage = string.Empty;
            _confirmAt = -1f;
            _allReadySince = -1f;
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
                slot.Rolling = false;
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
            UpdateRolls();
            if (_confirmAt >= 0f && Time.unscaledTime >= _confirmAt)
            {
                FireSingle();
            }
            // ローカル対戦: 全員が READY になったら、全員そろった帯を見せてから進む（その間に戻ると取り消し。D-89 の C17）。
            if (isActiveAndEnabled && Mode == SelectMode.Local && CanStartLocal && AllJoinedReady())
            {
                if (_allReadySince < 0f)
                {
                    _allReadySince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _allReadySince >= AllReadyHoldSeconds)
                {
                    _allReadySince = -1f;
                    LocalConfirmed?.Invoke(LocalPlayers(), _timed);
                }
            }
            else
            {
                _allReadySince = -1f;
            }
        }

        // RANDOM のルーレット（D-89 の C18）: カーソルがアイコンの上を速く回ってから、だんだん遅くなって、先に決めたキャラクターで止まる。
        private void UpdateRolls()
        {
            float now = Time.unscaledTime;
            bool changed = false;
            for (int i = 0; i < MaxPlayers; i++)
            {
                Slot slot = _slots[i];
                if (!slot.Rolling || now < slot.RollNextAt)
                {
                    continue;
                }
                slot.RollHop++;
                changed = true;
                if (slot.RollHop > RollHops)
                {
                    slot.Rolling = false;
                    SetCharacter(i, slot.RollResult, quiet: true);
                    Lock(i);
                    continue;
                }
                if (slot.RollHop == RollHops)
                {
                    slot.RollShown = slot.RollResult;
                }
                else
                {
                    int next = UnityEngine.Random.Range(0, Count - 1);
                    slot.RollShown = next >= slot.RollShown ? next + 1 : next;
                }
                float k = (float)slot.RollHop / RollHops;
                slot.RollNextAt = now + 0.05f + 0.19f * k * k;
                UiSounds.Find().Play(SoundId.UiMove);
            }
            if (changed)
            {
                Refresh();
            }
        }

        // トレーニング・シングルモード: 決定の演出を見せた後で（または、もう一度決定を押したら）次の画面へ。
        private void FireSingle()
        {
            _confirmAt = -1f;
            if (Mode == SelectMode.Single && _slots[0].Ready)
            {
                Confirmed?.Invoke(_slots[0].Character);
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
                ToggleMode = k.mKey.wasPressedThisFrame,
                CycleTeam = k.qKey.wasPressedThisFrame,
                CycleCpu = k.cKey.wasPressedThisFrame,
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
                ToggleMode = g.buttonWest.wasPressedThisFrame,
                CycleTeam = g.rightShoulder.wasPressedThisFrame,
                CycleCpu = g.leftShoulder.wasPressedThisFrame,
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
            if (Mode == SelectMode.Local)
            {
                HandleLocalSettings(slotIndex, intent);
            }
            if (intent.Back)
            {
                Back(slotIndex);
            }
            else if (slot.Rolling)
            {
                // ルーレットの途中は、戻る（取り消し）だけ。
            }
            else if (intent.Confirm && !slot.Ready)
            {
                Confirm(slotIndex);
            }
            else if (intent.Confirm && Mode == SelectMode.Single && _confirmAt >= 0f)
            {
                // 決定の演出の途中にもう一度押したら、すぐ進む。
                FireSingle();
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
            if (free < 0 || free >= MaxLocalPlayers)
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
            // チーム対戦なら、人数の少ないほう（RED と BLUE）に入る。参加して人と CPU が上限を超えたら CPU を減らす。
            int red = 0;
            int blue = 0;
            foreach (Slot other in _slots)
            {
                if (other.Joined && other != slot)
                {
                    red += other.Team == 1 ? 1 : 0;
                    blue += other.Team == 2 ? 1 : 0;
                }
            }
            slot.Team = (byte)(red <= blue ? 1 : 2);
            ClampCpus();
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
            if (slot.Rolling)
            {
                return;
            }
            if (slot.Character == RandomIndex && Count > 1)
            {
                // RANDOM: 行き先をここで決めて、ルーレットを回す（D-89 の C18）。止まったら決める（ネットワーク対戦では、止まってから
                // 決めたキャラクターをほかの人へ知らせ、準備完了にする）。
                slot.Rolling = true;
                slot.RollResult = UnityEngine.Random.Range(0, Count);
                slot.RollHop = 0;
                slot.RollShown = RandomIndex;
                slot.RollNextAt = Time.unscaledTime;
                UiSounds.Find().Play(SoundId.UiConfirm);
                return;
            }
            if (slot.Character == RandomIndex)
            {
                SetCharacter(slotIndex, UnityEngine.Random.Range(0, Count), quiet: true);
            }
            Lock(slotIndex);
        }

        // 決める（READY）。トレーニング・シングルモードは、決定の演出を見せてから次の画面へ（SingleHoldSeconds）。
        private void Lock(int slotIndex)
        {
            Slot slot = _slots[slotIndex];
            UiSounds.Find().Play(SoundId.UiCharacterDecide);
            slot.Ready = true;
            switch (Mode)
            {
                case SelectMode.Single:
                    _confirmAt = Time.unscaledTime + SingleHoldSeconds;
                    break;
                case SelectMode.Network:
                    NetworkReadyChanged?.Invoke(true);
                    break;
            }
            Refresh();
        }

        private void Back(int slotIndex)
        {
            Slot slot = _slots[slotIndex];
            UiSounds.Find().Play(SoundId.UiMove);
            if (slot.Rolling)
            {
                // ルーレットを止める（RANDOM のまま）。
                slot.Rolling = false;
                return;
            }
            if (slot.Ready)
            {
                slot.Ready = false;
                if (Mode == SelectMode.Single)
                {
                    _confirmAt = -1f;
                }
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

        private void SetCharacter(int slotIndex, int character, bool quiet = false)
        {
            Slot slot = _slots[slotIndex];
            if (slot.Character == character)
            {
                return;
            }
            slot.Character = character;
            if (!quiet)
            {
                UiSounds.Find().Play(SoundId.UiMove);
            }
            // RANDOM にいる間は、ほかの人には前のキャラクターのまま見せる（決めたときに知らせる）。
            if (Mode == SelectMode.Network && slotIndex == _localSlot && character != RandomIndex)
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
            if (slotIndex < 0 || _slots[slotIndex].Ready || _slots[slotIndex].Rolling || Time.frameCount < _activeFromFrame)
            {
                return;
            }
            SetCharacter(slotIndex, character);
            Refresh();
        }

        private void OnIconClicked(int character)
        {
            int slotIndex = MouseSlot();
            if (slotIndex < 0 || _slots[slotIndex].Ready || _slots[slotIndex].Rolling || Time.frameCount < _activeFromFrame)
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
            bool compact = Compact;
            for (int i = 0; i < MaxPlayers; i++)
            {
                _corners[i].transform.localScale = Vector3.one * (compact ? CompactCornerScale : 1f);
                RefreshCorner(i);
                RefreshCursor(i);
            }
            if (_hint != null)
            {
                if (Mode == SelectMode.Local)
                {
                    LocalizedText.Show(_hint,
                        "ENTER / A: JOIN, READY     ESC / B: BACK     F1 / SELECT: DETAILS     T / Y: TIME LIMIT\n"
                        + "M / X: MODE (FREE FOR ALL / TEAM)     C / LB: CPU     Q / RB: MY TEAM",
                        "Enter ／ A: 参加・決定　　Esc ／ B: 戻る　　F1 ／ SELECT: 技の説明　　T ／ Y: 制限時間\n"
                        + "M ／ X: 個人戦 ／ チーム対戦　　C ／ LB: CPU の数　　Q ／ RB: 自分のチーム");
                }
                else
                {
                    LocalizedText.Show(_hint, "ENTER / A: OK     ESC / B: BACK     F1 / SELECT: DETAILS",
                        "Enter ／ A: 決定　　Esc ／ B: 戻る　　F1 ／ SELECT: 技の説明");
                }
            }
            if (_timeLimit != null)
            {
                _timeLimit.text = Mode == SelectMode.Local ? LocalSettingsText() : string.Empty;
            }
            if (_message != null)
            {
                _message.text = MessageText();
            }
        }

        // ローカル対戦の設定の行（時間の制限・個人戦かチーム対戦か・CPU の数と、チーム対戦なら CPU のチーム）。
        private string LocalSettingsText()
        {
            string text = $"TIME LIMIT: {(_timed ? "ON" : "OFF")}     MODE: {(_teamMode ? "TEAM" : "FREE FOR ALL")}     CPU: {_cpuCount}";
            if (_teamMode && _cpuCount > 0)
            {
                byte[] teams = CpuTeams();
                var parts = new List<string>();
                for (int t = 1; t <= TeamColorsCount; t++)
                {
                    int n = Array.FindAll(teams, x => x == t).Length;
                    if (n > 0)
                    {
                        parts.Add($"{Airerage.Runtime.Presentation.TeamColors.NameOf(t)} {n}");
                    }
                }
                text += $" ({string.Join(" / ", parts)})";
            }
            return text;
        }

        private string MessageText()
        {
            switch (Mode)
            {
                case SelectMode.Local:
                    if (JoinedCount + _cpuCount < 2)
                    {
                        // 白い背景に収まるように、「C ／ LB」の前で 2 行に分ける（句点は使わない。2026-11-08 の指摘）。
                        return Loc.T("JOIN WITH ENTER (KEYBOARD) OR A (GAMEPAD)\nOR ADD CPUs WITH C / LB",
                            "Enter（キーボード）か A（ゲームパッド）で参加\nC ／ LB で CPU も入れられます");
                    }
                    return LocalProblem() ?? string.Empty;
                case SelectMode.Network:
                    return _networkMessage;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 5 人以上の枠を出しているか（ネットワーク対戦で 5P〜8P のだれかがいる）。そのときは四隅の枠を縮めて 2 枚ずつ並べる
        /// （1P〜4P の内側に 5P〜8P）。4 人までは今までと同じ 4 枚。
        /// </summary>
        public bool Compact
        {
            get
            {
                if (Mode != SelectMode.Network)
                {
                    return false;
                }
                for (int i = MaxLocalPlayers; i < MaxPlayers; i++)
                {
                    if (_slots[i].Joined)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private static Slot[] NewSlots()
        {
            var slots = new Slot[MaxPlayers];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new Slot();
            }
            return slots;
        }

        private void RefreshCorner(int i)
        {
            Slot slot = _slots[i];
            bool shown = (Mode != SelectMode.Single || i == 0) && (i < MaxLocalPlayers || Compact);
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
                _shownNames[i] = string.Empty;
                _cornerTags[i].text = string.Empty;
                _cornerStates[i].text = Mode == SelectMode.Local ? Loc.T("PRESS ENTER / A", "Enter ／ A で参加") : "EMPTY";
                _cornerStates[i].color = new Color(1f, 1f, 1f, 0.5f);
                return;
            }
            _cornerPlates[i].color = JoinedFill;
            if (Mode == SelectMode.Local && _teamMode)
            {
                // チーム対戦: 枠はチームの色、機器の名前の横にチームの名前（T9-6）。
                color = Airerage.Runtime.Presentation.TeamColors.Of(slot.Team);
            }
            _cornerPlates[i].BorderColor = slot.Ready ? color : new Color(color.r, color.g, color.b, 0.55f);
            _cornerDevices[i].text = Mode == SelectMode.Local && _teamMode
                ? $"{slot.DeviceName}  {Airerage.Runtime.Presentation.TeamColors.NameOf(slot.Team)}"
                : slot.DeviceName;
            if (slot.Character == RandomIndex)
            {
                _cornerLogos[i].enabled = false;
                _cornerNames[i].text = "RANDOM";
                _shownNames[i] = "RANDOM";
                _cornerTags[i].text = _language == GameLanguage.Japanese ? "#？？？" : "#???";
            }
            else
            {
                CharacterConfigAsset character = _roster.Get(slot.Character);
                _cornerLogos[i].texture = character.Logo;
                _cornerLogos[i].enabled = character.Logo != null;
                _cornerNames[i].text = character.DisplayName;
                _shownNames[i] = character.DisplayName;
                _cornerTags[i].text = TagsText(character.TagsIn(_language));
            }
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
            int index = CursorIndex(i);
            RectTransform icon = _icons[index].Rect;
            RectTransform cursor = _cursors[i];
            cursor.SetParent(icon.parent, false);
            cursor.SetAsLastSibling();
            cursor.anchorMin = icon.anchorMin;
            cursor.anchorMax = icon.anchorMax;
            cursor.pivot = icon.pivot;
            cursor.anchoredPosition = IconHome(index);
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
            if (index == RandomIndex)
            {
                _detailName.text = "RANDOM";
                _detailTags.text = _language == GameLanguage.Japanese ? "決めたときに、ランダムなキャラクターになる"
                    : "Becomes a random character when you decide.";
                if (_detailLogo != null)
                {
                    _detailLogo.enabled = false;
                }
                for (int s = 0; s < _skillNames.Length; s++)
                {
                    _skillNames[s].text = string.Empty;
                    LineBreakText.Set(_skillDescriptions[s], string.Empty);
                    _skillCooldowns[s].text = string.Empty;
                }
                return;
            }
            CharacterConfigAsset character = _roster.Get(index);
            if (character == null)
            {
                return;
            }
            _detailName.text = character.DisplayName;
            _detailTags.text = TagsText(character.TagsIn(_language));
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
                string skillName = text != null ? text.NameIn(_language) : string.Empty;
                _skillNames[i].text = !string.IsNullOrEmpty(skillName) ? skillName : combat.Get(slot).Name;
                // 説明は句読点を行の頭に置かずに折り返す（LineBreakText）。
                LineBreakText.Set(_skillDescriptions[i], text != null ? text.DescriptionIn(_language) : string.Empty);
                _skillCooldowns[i].text = CooldownText(combat, slot, text != null ? text.CooldownNoteIn(_language) : string.Empty, _language);
            }
        }

        /// <summary>ハッシュタグを 1 行に並べる。</summary>
        public static string TagsText(string[] tags)
        {
            return tags == null ? string.Empty : string.Join("  ", tags);
        }

        /// <summary>
        /// 技の説明の下に出す行: 特殊攻撃はクールタイム（数え始めるときの注記と、2 回以上続けて使えるならスタック数）、必殺技は必要なゲージ
        /// （言語 language で）。
        /// </summary>
        public static string CooldownText(CombatTuning combat, AttackSlot slot, string cooldownNote, GameLanguage language = GameLanguage.Japanese)
        {
            bool ja = language == GameLanguage.Japanese;
            if (slot == AttackSlot.Super)
            {
                return (ja ? "必要ゲージ " : "GAUGE ") + GaugeText(combat.SuperGaugeCost);
            }
            string seconds = SecondsText(combat.CooldownTicksOf(slot));
            string text = ja ? $"クールタイム {seconds} 秒" : $"COOLDOWN {seconds}s";
            if (!string.IsNullOrEmpty(cooldownNote))
            {
                text += ja ? $"（{cooldownNote}）" : $" ({cooldownNote})";
            }
            int charges = combat.MaxChargesOf(slot);
            if (charges >= 2)
            {
                text += ja ? $"  ／  スタック {charges}" : $"  /  STACKS {charges}";
            }
            return text;
        }

        // 秒の数（端数があれば小数 1 桁）。
        private static string SecondsText(int ticks)
        {
            return ticks % SimTime.TicksPerSecond == 0
                ? $"{ticks / SimTime.TicksPerSecond}"
                : ((float)ticks / SimTime.TicksPerSecond).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // ゲージ（0.1% 単位）を % で（端数があれば小数 1 桁。D-60）。
        private static string GaugeText(int gauge)
        {
            int per = CombatTuning.GaugePerPercent;
            return gauge % per == 0 ? $"{gauge / per}%" : $"{gauge / per}.{gauge % per}%";
        }
    }
}
