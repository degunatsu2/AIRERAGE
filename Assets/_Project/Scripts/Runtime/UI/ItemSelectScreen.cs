using System;
using System.Collections.Generic;
using Airerage.Core.Items;
using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// アイテムを選ぶ画面（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。ステージ選択と同じ形: 左にアイテムの札を 2 列に並べ（札の左に
    /// アイテムの絵、右に名前）、カーソルを合わせたアイテムの効果を、札の右にポップアップで出す。
    /// - 初期アイテム（ShowPick）: 決定でアイテムを選び、札の左に入るバッグのキー（Z・X・C。キーの割り当てを変えていればそのキー）を出す。
    ///   選んだアイテムをもう一度決定すると外す（後ろのアイテムは詰める）。上限（初期アイテム数 1〜3）まで。選ぶたびに PicksChanged。
    /// - ドロップするアイテム（ShowPool）: 札の左のチェックを決定で付け外しする（最後の 1 つは外せない）。変えるたびに PoolChanged。
    /// どちらも戻る操作（Esc・B）か DONE で Closed。オプション（PlayerPrefs の ItemSettings）とネットワーク対戦の待機所の両方で使う
    /// （待機所では、開いている間も待機所に入ったまま。状態は「アイテム選択中……」）。
    /// 2026-11-05（ユーザーの要望）: 札はパッシブとアクティブの見出しで分け、画面に収まらない分はスクロールで見る（ホイール・ドラッグ。
    /// カーソルを動かすと、選んだ札が見える所までスクロールする）。初期アイテムでは、いちばん上に RANDOM（ランダムセレクト）。選ぶと
    /// ほかの選択がすべて外れ、試合の始めに初期アイテム数だけのランダムなアイテムを持つ（全員で同じ。SimWorld.SetItems）。
    /// 札の並びは実行中に決める（Layout。アイテムを足しても、ビルダーの位置を直さなくてよい）。
    /// ドロップするアイテムでは、いちばん上に CHECK ALL ／ UNCHECK ALL（2026-11-06）。チェックが 0 個の間は閉じられない（案内を出す）。
    /// </summary>
    public sealed class ItemSelectScreen : MonoBehaviour
    {
        /// <summary>選び方。</summary>
        public enum SelectMode
        {
            None,
            Pick,
            Pool,
        }

        private const float PopupSeconds = 0.14f;

        // 札の並び（スクロールの中身の中の位置。左上から）。
        private const int Columns = 2;
        private const float PlateLeft = 64f;
        private const float ColumnStep = 430f;
        private const float RowStep = 76f;
        private const float HeaderHeight = 48f;
        private const float SectionGap = 14f;

        [Tooltip("アイテムの札（ItemCatalog の順）")]
        [SerializeField] private MenuOption[] _options = new MenuOption[0];
        [Tooltip("札の左の、入るバッグのキーの箱とその文字（_options と同じ順。初期アイテムで選んだときだけ）")]
        [SerializeField] private GameObject[] _keyBoxes = new GameObject[0];
        [SerializeField] private Text[] _keys = new Text[0];
        [Tooltip("札の左のチェックの箱と、付いているときの印（_options と同じ順。ドロップするアイテムのときだけ）")]
        [SerializeField] private GameObject[] _checkBoxes = new GameObject[0];
        [SerializeField] private GameObject[] _checkMarks = new GameObject[0];
        [Tooltip("札の中のアイテムの絵（_options と同じ順）")]
        [SerializeField] private RawImage[] _icons = new RawImage[0];
        [Tooltip("ランダムセレクトの札と、そのチェックの箱・印・絵（初期アイテムのときだけ出す）")]
        [SerializeField] private MenuOption _random;
        [SerializeField] private GameObject _randomCheck;
        [SerializeField] private GameObject _randomMark;
        [SerializeField] private RawImage _randomIcon;
        [Tooltip("ドロップするアイテムで、すべてにチェックを付ける札と、すべて外す札")]
        [SerializeField] private MenuOption _checkAll;
        [SerializeField] private MenuOption _uncheckAll;
        [Tooltip("札を入れるスクロールの中身と、スクロール（ホイール・ドラッグ）")]
        [SerializeField] private RectTransform _content;
        [SerializeField] private ScrollRect _scroll;
        [Tooltip("パッシブとアクティブの見出し（スクロールの中身の中）")]
        [SerializeField] private Text _passiveHeader;
        [SerializeField] private Text _activeHeader;
        [SerializeField] private MenuOption _done;
        [SerializeField] private Text _title;
        [SerializeField] private Text _note;
        [Tooltip("効果のポップアップ（札の右に、カーソルの札の高さで出す）")]
        [SerializeField] private RectTransform _popup;
        [SerializeField] private RawImage _popupIcon;
        [SerializeField] private Text _popupName;
        [SerializeField] private Text _popupKind;
        [SerializeField] private Text _popupDescription;
        [Tooltip("キーの名前を読む入力のアセット（試合で使うものと同じ）")]
        [SerializeField] private InputActionAsset _inputActions;

        private readonly List<ItemId> _picks = new List<ItemId>();
        private readonly string[] _keyNames = { "Z", "X", "C" };
        private ulong _pool = ItemCatalog.AllMask;
        private int _limit = ItemCatalog.BagSlots;
        private int _selectAtFrame = -1;
        private int _highlighted = -1;
        private float _popupAt = -10f;
        private bool _wired;

        /// <summary>今の選び方（出していなければ None）。</summary>
        public SelectMode Mode { get; private set; }

        /// <summary>最後に出したときの選び方（閉じた後も残る。Closed で、どちらを閉じたかを知るため）。</summary>
        public SelectMode LastMode { get; private set; }

        /// <summary>初期アイテムを選んだ（Z・X・C の順。上限まで）。</summary>
        public event Action<ItemId[]> PicksChanged;

        /// <summary>ドロップするアイテムを変えた（ビット）。</summary>
        public event Action<ulong> PoolChanged;

        /// <summary>閉じた（戻る操作か DONE）。</summary>
        public event Action Closed;

        public bool IsShowing => gameObject.activeSelf;

        /// <summary>この画面のイメージカラー（オプションから開けば OPTIONS、待機所から開けば NETWORK の色。2026-11-08）。</summary>
        public Color Accent { get; private set; } = MenuAccents.Default;

        /// <summary>この画面のイメージカラーを accent にする（以前は水色だった文字・線・印。2026-11-08 のユーザーの要望）。</summary>
        public void SetAccent(Color accent)
        {
            Accent = accent;
            AccentTint.Apply(this, accent);
        }
        public int Count => _options.Length;
        public MenuOption Option(int index) => _options[index];
        public MenuOption Done => _done;
        public MenuOption RandomOption => _random;
        public MenuOption CheckAllOption => _checkAll;
        public MenuOption UncheckAllOption => _uncheckAll;

        /// <summary>選んでいる初期アイテム（Z・X・C の順。ランダムセレクトなら ItemId.Random だけ）。</summary>
        public IReadOnlyList<ItemId> Picks => _picks;

        /// <summary>ランダムセレクトを選んでいるか。</summary>
        public bool RandomPicked => _picks.Count == 1 && _picks[0] == ItemId.Random;

        /// <summary>ドロップするアイテム（ビット）。</summary>
        public ulong Pool => _pool;

        /// <summary>ランダムセレクトのチェックが付いているか（テスト用）。</summary>
        public bool RandomShown => _randomMark != null && _randomMark.activeSelf && _random != null && _random.gameObject.activeSelf;

        /// <summary>スクロールの中身の位置（上からの量。テスト用）。</summary>
        public float ScrollOffset => _content != null ? _content.anchoredPosition.y : 0f;

        /// <summary>札 index の左に出しているキー（テスト用。なければ空）。</summary>
        public string KeyShown(int index) => index < _keyBoxes.Length && _keyBoxes[index] != null && _keyBoxes[index].activeSelf ? _keys[index].text : string.Empty;

        /// <summary>札 index のチェックが付いているか（テスト用）。</summary>
        public bool CheckShown(int index) => index < _checkMarks.Length && _checkMarks[index] != null && _checkMarks[index].activeSelf;

        /// <summary>ポップアップに出しているアイテムの名前（テスト用）。</summary>
        public string PopupName => _popupName != null && _popup != null && _popup.gameObject.activeSelf ? _popupName.text : string.Empty;

        private void Awake()
        {
            Wire();
        }

        // 札の操作をつなぐ（1 回だけ。エディタで画像を撮るときは Awake が呼ばれないので Show からも呼ぶ）。
        private bool Wire()
        {
            if (_wired)
            {
                return true;
            }
            if (_options.Length != ItemCatalog.Count || _keys.Length != _options.Length || _keyBoxes.Length != _options.Length
                || _checkBoxes.Length != _options.Length
                || _checkMarks.Length != _options.Length || _icons.Length != _options.Length || _done == null || _random == null
                || _randomCheck == null || _randomMark == null || _content == null || _scroll == null || _passiveHeader == null || _activeHeader == null
                || _checkAll == null || _uncheckAll == null)
            {
                Debug.LogError($"{name}: アイテムの札（{_options.Length} / {ItemCatalog.Count}）か、キー・チェック・絵・RANDOM・スクロール・見出し・DONE が" +
                    "設定されていません", this);
                enabled = false;
                return false;
            }
            _wired = true;
            for (int i = 0; i < _options.Length; i++)
            {
                int index = i;
                _options[i].Selected += () => Highlight(index);
                _options[i].Clicked.AddListener(() => Toggle(index));
                _options[i].Cancelled += Close;
                if (_icons[i] != null)
                {
                    _icons[i].texture = Presentation.ItemIcons.Get(ItemCatalog.At(i));
                }
            }
            _done.Clicked.AddListener(Close);
            _done.Cancelled += Close;
            _done.Selected += () => Highlight(-1);
            _random.Selected += () => Highlight(RandomIndex);
            _random.Clicked.AddListener(ToggleRandom);
            _random.Cancelled += Close;
            _checkAll.Clicked.AddListener(() => SetAll(true));
            _uncheckAll.Clicked.AddListener(() => SetAll(false));
            _checkAll.Cancelled += Close;
            _uncheckAll.Cancelled += Close;
            _checkAll.Selected += () => Highlight(-1);
            _uncheckAll.Selected += () => Highlight(-1);
            if (_randomIcon != null)
            {
                _randomIcon.texture = Presentation.ItemIcons.RandomIcon();
            }
            return true;
        }

        /// <summary>初期アイテムを選ぶ画面として出す（limit は初期アイテム数。current は今の選び方）。</summary>
        public void ShowPick(int limit, IReadOnlyList<ItemId> current)
        {
            if (!Wire())
            {
                return;
            }
            _limit = Mathf.Clamp(limit, 1, ItemCatalog.BagSlots);
            _picks.Clear();
            if (current != null && current.Count > 0 && current[0] == ItemId.Random)
            {
                _picks.Add(ItemId.Random);
            }
            else
            {
                _picks.AddRange(ItemSettings.Trim(current, _limit));
            }
            ReadKeyNames();
            Open(SelectMode.Pick);
        }

        /// <summary>ドロップするアイテムを選ぶ画面として出す（pool はビット）。</summary>
        public void ShowPool(ulong pool)
        {
            if (!Wire())
            {
                return;
            }
            _pool = ItemSettings.ValidPool(pool);
            Open(SelectMode.Pool);
        }

        public void Hide()
        {
            _selectAtFrame = -1;
            Mode = SelectMode.None;
            gameObject.SetActive(false);
        }

        /// <summary>閉じる（Closed）。ドロップするアイテムのチェックが 0 個なら閉じない（案内を出す）。</summary>
        public void Close()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }
            if (Mode == SelectMode.Pool && (_pool & ItemCatalog.AllMask) == 0)
            {
                Note(Loc.T("CHECK AT LEAST ONE ITEM", "1つ以上のアイテムにチェックを付けてください"));
                return;
            }
            Hide();
            Closed?.Invoke();
        }

        private void Open(SelectMode mode)
        {
            gameObject.SetActive(true);
            Mode = mode;
            LastMode = mode;
            if (_title != null)
            {
                _title.text = mode == SelectMode.Pick ? "START ITEMS" : "DROP ITEMS";
            }
            // 名前はオプションの言語で（日本語の字体は英字も出せる）。
            for (int i = 0; i < _options.Length; i++)
            {
                _options[i].SetLabel(Presentation.ItemTexts.Name(ItemCatalog.At(i)));
            }
            _random.SetLabel(Loc.T("RANDOM", "ランダム"));
            _checkAll.SetLabel(Loc.T("CHECK ALL", "すべてチェック"));
            _uncheckAll.SetLabel(Loc.T("UNCHECK ALL", "すべて外す"));
            _passiveHeader.text = Loc.T("PASSIVE", "パッシブ");
            _activeHeader.text = Loc.T("ACTIVE", "アクティブ");
            Layout();
            Refresh();
            Note();
            _highlighted = -1;
            _selectAtFrame = Time.frameCount + 1;
            if (_popup != null)
            {
                _popup.gameObject.SetActive(false);
            }
        }

        // キーの割り当て（保存した変更を当てた上で）から、バッグのキーの名前を読む。
        private void ReadKeyNames()
        {
            if (_inputActions == null)
            {
                return;
            }
            BindingSettings.Apply(_inputActions, GameSettings.LoadBindingOverrides());
            string[] actions = { "Item1", "Item2", "Item3" };
            for (int i = 0; i < actions.Length; i++)
            {
                string key = BindingSettings.KeyName(_inputActions, actions[i], BindingSettings.Column.Keyboard);
                _keyNames[i] = string.IsNullOrEmpty(key) || key == "-" ? _keyNames[i] : key;
            }
        }

        // 札の並び: （初期アイテムなら RANDOM）→ パッシブの見出しと札 → アクティブの見出しと札（2 列）。中身の高さも合わせる。
        private void Layout()
        {
            float y = 0f;
            bool pick = Mode == SelectMode.Pick;
            _random.gameObject.SetActive(pick);
            _checkAll.gameObject.SetActive(!pick);
            _uncheckAll.gameObject.SetActive(!pick);
            // 選んだときに動かす札は、元の位置（SetHome）で置く（anchoredPosition だと、選んだときに作ったときの位置へ戻る）。
            if (pick)
            {
                _random.SetHome(new Vector2(PlateLeft, y));
            }
            else
            {
                _checkAll.SetHome(new Vector2(PlateLeft, y));
                _uncheckAll.SetHome(new Vector2(PlateLeft + ColumnStep, y));
            }
            y -= RowStep + SectionGap;
            y = LayoutSection(_passiveHeader, 0, ItemCatalog.PassiveCount, y);
            y -= SectionGap;
            y = LayoutSection(_activeHeader, ItemCatalog.PassiveCount, ItemCatalog.ActiveCount, y);
            Vector2 size = _content.sizeDelta;
            size.y = -y + 12f;
            _content.sizeDelta = size;
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            SyncScrollbar();
        }

        // スクロールバーのつまみを中身の位置に合わせる（つまみの初めの値 0 は「いちばん下」なので、開いたときに下へ飛ばないように）。
        private void SyncScrollbar()
        {
            Scrollbar bar = _scroll.verticalScrollbar;
            if (bar == null)
            {
                return;
            }
            float range = _content.sizeDelta.y - ((RectTransform)_scroll.transform).rect.height;
            bar.SetValueWithoutNotify(range > 0f ? 1f - Mathf.Clamp01(_content.anchoredPosition.y / range) : 1f);
        }

        private float LayoutSection(Text header, int first, int count, float y)
        {
            var rect = header.rectTransform;
            rect.anchoredPosition = new Vector2(PlateLeft - 40f, y);
            y -= HeaderHeight;
            for (int k = 0; k < count; k++)
            {
                _options[first + k].SetHome(new Vector2(PlateLeft + (k % Columns) * ColumnStep, y - (k / Columns) * RowStep));
            }
            return y - (count + Columns - 1) / Columns * RowStep;
        }

        // 選んだ札が見える所までスクロールする（見出しのすぐ下の札なら見出しも見せる）。
        private void KeepVisible(RectTransform plate)
        {
            if (plate == null || plate.parent != _content)
            {
                return;
            }
            float view = ((RectTransform)_scroll.transform).rect.height;
            float top = -plate.anchoredPosition.y - HeaderHeight - 6f;
            float bottom = -plate.anchoredPosition.y + plate.rect.height + 8f;
            Vector2 position = _content.anchoredPosition;
            if (top < position.y)
            {
                position.y = Mathf.Max(0f, top);
            }
            else if (bottom > position.y + view)
            {
                position.y = bottom - view;
            }
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, _content.sizeDelta.y - view));
            _scroll.StopMovement();
            _content.anchoredPosition = position;
            SyncScrollbar();
        }

        // すべてにチェックを付ける・すべて外す（ドロップするアイテム。外したら、閉じるまでに 1 つ以上に付ける）。
        private void SetAll(bool on)
        {
            if (Mode != SelectMode.Pool)
            {
                return;
            }
            _pool = on ? ItemCatalog.AllMask : 0UL;
            if (on)
            {
                PoolChanged?.Invoke(_pool);
            }
            Refresh();
            Note();
        }

        // ランダムセレクトの付け外し（付けると、ほかの選択はすべて外れる）。
        private void ToggleRandom()
        {
            if (Mode != SelectMode.Pick)
            {
                return;
            }
            bool random = RandomPicked;
            _picks.Clear();
            if (!random)
            {
                _picks.Add(ItemId.Random);
            }
            PicksChanged?.Invoke(_picks.ToArray());
            Refresh();
            Note();
        }

        private void Toggle(int index)
        {
            ItemId id = ItemCatalog.At(index);
            if (Mode == SelectMode.Pick)
            {
                if (RandomPicked)
                {
                    // ランダムセレクトから、選んだアイテムへ切り替える。
                    _picks.Clear();
                }
                if (_picks.Remove(id))
                {
                    PicksChanged?.Invoke(_picks.ToArray());
                }
                else if (_picks.Count < _limit)
                {
                    _picks.Add(id);
                    PicksChanged?.Invoke(_picks.ToArray());
                }
                else
                {
                    Note(Loc.T($"UP TO {_limit} ITEMS. SELECT A CHOSEN ITEM TO REMOVE IT", $"選べるのは{_limit}個までです。選んだアイテムを決定すると外せます"));
                    return;
                }
            }
            else if (Mode == SelectMode.Pool)
            {
                ulong bit = ItemCatalog.Bit(id);
                _pool ^= bit;
                // 0 個の間は知らせない（閉じるまでに 1 つ以上にチェックを付ける。Close）。
                if ((_pool & ItemCatalog.AllMask) != 0)
                {
                    PoolChanged?.Invoke(_pool);
                }
            }
            Refresh();
            Note();
        }

        private void Refresh()
        {
            bool random = RandomPicked;
            _randomCheck.SetActive(Mode == SelectMode.Pick);
            _randomMark.SetActive(Mode == SelectMode.Pick && random);
            for (int i = 0; i < _options.Length; i++)
            {
                ItemId id = ItemCatalog.At(i);
                int slot = _picks.IndexOf(id);
                bool picked = Mode == SelectMode.Pick && slot >= 0;
                if (_keyBoxes[i] != null)
                {
                    _keyBoxes[i].SetActive(picked);
                }
                if (_keys[i] != null)
                {
                    _keys[i].text = picked ? _keyNames[slot] : string.Empty;
                }
                if (_checkBoxes[i] != null)
                {
                    _checkBoxes[i].SetActive(Mode == SelectMode.Pool);
                }
                if (_checkMarks[i] != null)
                {
                    _checkMarks[i].SetActive(Mode == SelectMode.Pool && (_pool & ItemCatalog.Bit(id)) != 0);
                }
                if (_icons[i] != null)
                {
                    // 選んでいない・チェックのないアイテムは少し暗く。
                    bool on = Mode == SelectMode.Pick ? picked : (_pool & ItemCatalog.Bit(id)) != 0;
                    _icons[i].color = on || (Mode == SelectMode.Pick && !random && _picks.Count < _limit) ? Color.white : new Color(0.55f, 0.55f, 0.6f, 1f);
                }
            }
        }

        private void Note(string text = null)
        {
            if (_note == null)
            {
                return;
            }
            if (text != null)
            {
                _note.text = text;
                return;
            }
            if (Mode == SelectMode.Pick && RandomPicked)
            {
                _note.text = Loc.T($"RANDOM: START WITH {_limit} RANDOM ITEMS", $"ランダム: 試合の始めに{_limit}個のランダムなアイテムを持ちます");
            }
            else if (Mode == SelectMode.Pick)
            {
                _note.text = Loc.T($"CHOOSE UP TO {_limit} START ITEMS ({_picks.Count} / {_limit})", $"初期アイテムを{_limit}個まで選んでください（{_picks.Count}／{_limit}）");
            }
            else
            {
                _note.text = Loc.T("CHOOSE THE ITEMS THAT APPEAR", "出現させるアイテムを選んでください");
            }
        }

        private void Highlight(int index)
        {
            _highlighted = index;
            _popupAt = Time.unscaledTime;
            if (_popup == null)
            {
                return;
            }
            if (index == -1)
            {
                _popup.gameObject.SetActive(false);
                return;
            }
            bool random = index == RandomIndex;
            ItemId id = random ? ItemId.Random : ItemCatalog.At(index);
            var plate = (RectTransform)(random ? _random.transform : _options[index].transform);
            KeepVisible(plate);
            _popup.gameObject.SetActive(true);
            if (_popupIcon != null)
            {
                _popupIcon.texture = random ? Presentation.ItemIcons.RandomIcon() : Presentation.ItemIcons.Get(id);
            }
            if (_popupName != null)
            {
                _popupName.text = random ? Loc.T("RANDOM SELECT", "ランダムセレクト") : Presentation.ItemTexts.Name(id);
                _popupName.font = GameFonts.Switch(_popupName.font, GameSettings.LoadLanguage() == GameLanguage.Japanese);
            }
            if (_popupKind != null)
            {
                _popupKind.text = random ? Loc.T($"{_limit} ITEMS", $"{_limit}個") : Presentation.ItemTexts.Kind(id);
                // 暗い色のアイテム（臙脂色の刀など）でも読めるように、白に寄せる。
                _popupKind.color = random ? Accent : Color.Lerp(Presentation.ItemIcons.Accent(id), Color.white, 0.45f);
            }
            if (_popupDescription != null)
            {
                LineBreakText.Set(_popupDescription, random
                    ? Loc.T($"Clears your other picks. You start with {_limit} random items (the same picks on every player's screen).",
                        $"ほかの選択はすべて外れる。試合の始めに、初期アイテム数（{_limit}個）だけランダムなアイテムを持つ（どの人の画面でも同じアイテム）。")
                    : Presentation.ItemTexts.Description(id));
            }
            // カーソルの札の高さに合わせる（スクロールの中の札は、画面での高さに直す。画面の外にはみ出さない）。
            var parent = (RectTransform)_popup.parent;
            Vector3 local = parent.InverseTransformPoint(plate.position);
            float plateY = local.y - parent.rect.yMax;
            Vector2 position = _popup.anchoredPosition;
            position.y = Mathf.Clamp(plateY + 20f, -960f + _popup.sizeDelta.y, -150f);
            _popup.anchoredPosition = position;
        }

        /// <summary>Highlight の、ランダムセレクトの札の番号（アイテムの札は 0〜、DONE は -1）。</summary>
        private const int RandomIndex = -2;


        private void Update()
        {
            if (_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame)
            {
                _selectAtFrame = -1;
                if (Mode == SelectMode.Pick)
                {
                    _random.SelectQuietly();
                    Highlight(RandomIndex);
                }
                else
                {
                    _options[0].SelectQuietly();
                    Highlight(0);
                }
            }
            if (_popup != null && _popup.gameObject.activeSelf)
            {
                // ポップアップは、出た瞬間に少し大きくなってから落ち着く。
                float t = Mathf.Clamp01((Time.unscaledTime - _popupAt) / PopupSeconds);
                float scale = Mathf.Lerp(0.92f, 1f, 1f - (1f - t) * (1f - t));
                _popup.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
