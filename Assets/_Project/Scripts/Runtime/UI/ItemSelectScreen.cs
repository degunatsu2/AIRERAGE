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
        private uint _pool = ItemCatalog.AllMask;
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
        public event Action<uint> PoolChanged;

        /// <summary>閉じた（戻る操作か DONE）。</summary>
        public event Action Closed;

        public bool IsShowing => gameObject.activeSelf;
        public int Count => _options.Length;
        public MenuOption Option(int index) => _options[index];
        public MenuOption Done => _done;

        /// <summary>選んでいる初期アイテム（Z・X・C の順）。</summary>
        public IReadOnlyList<ItemId> Picks => _picks;

        /// <summary>ドロップするアイテム（ビット）。</summary>
        public uint Pool => _pool;

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
                || _checkMarks.Length != _options.Length || _icons.Length != _options.Length || _done == null)
            {
                Debug.LogError($"{name}: アイテムの札（{_options.Length} / {ItemCatalog.Count}）か、キー・チェック・絵・DONE が設定されていません", this);
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
            _picks.AddRange(ItemSettings.Trim(current, _limit));
            ReadKeyNames();
            Open(SelectMode.Pick);
        }

        /// <summary>ドロップするアイテムを選ぶ画面として出す（pool はビット）。</summary>
        public void ShowPool(uint pool)
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

        /// <summary>閉じる（Closed）。</summary>
        public void Close()
        {
            if (!gameObject.activeSelf)
            {
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

        private void Toggle(int index)
        {
            ItemId id = ItemCatalog.At(index);
            if (Mode == SelectMode.Pick)
            {
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
                uint bit = ItemCatalog.Bit(id);
                uint next = _pool ^ bit;
                if ((next & ItemCatalog.AllMask) == 0)
                {
                    Note(Loc.T("AT LEAST ONE ITEM MUST STAY CHECKED", "1つ以上のアイテムにチェックが必要です"));
                    return;
                }
                _pool = next;
                PoolChanged?.Invoke(_pool);
            }
            Refresh();
            Note();
        }

        private void Refresh()
        {
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
                    _icons[i].color = on || (Mode == SelectMode.Pick && _picks.Count < _limit) ? Color.white : new Color(0.55f, 0.55f, 0.6f, 1f);
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
            if (Mode == SelectMode.Pick)
            {
                _note.text = Loc.T($"CHOOSE UP TO {_limit} ITEMS ({_picks.Count} / {_limit})", $"持っていくアイテムを{_limit}個まで選んでください（{_picks.Count}／{_limit}）");
            }
            else
            {
                _note.text = Loc.T("ONLY CHECKED ITEMS APPEAR IN MATCHES", "チェックしたアイテムだけが試合に出ます");
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
            if (index < 0)
            {
                _popup.gameObject.SetActive(false);
                return;
            }
            ItemId id = ItemCatalog.At(index);
            _popup.gameObject.SetActive(true);
            if (_popupIcon != null)
            {
                _popupIcon.texture = Presentation.ItemIcons.Get(id);
            }
            if (_popupName != null)
            {
                _popupName.text = Presentation.ItemTexts.Name(id);
                _popupName.font = GameFonts.Switch(_popupName.font, GameSettings.LoadLanguage() == GameLanguage.Japanese);
            }
            if (_popupKind != null)
            {
                _popupKind.text = Presentation.ItemTexts.Kind(id);
                // 暗い色のアイテム（臙脂色の刀など）でも読めるように、白に寄せる。
                _popupKind.color = Color.Lerp(Presentation.ItemIcons.Accent(id), Color.white, 0.45f);
            }
            if (_popupDescription != null)
            {
                LineBreakText.Set(_popupDescription, Presentation.ItemTexts.Description(id));
            }
            // カーソルの札の高さに合わせる（画面の外にはみ出さない）。
            var plate = (RectTransform)_options[index].transform;
            Vector2 position = _popup.anchoredPosition;
            position.y = Mathf.Clamp(plate.anchoredPosition.y + 20f, -960f + _popup.sizeDelta.y, -150f);
            _popup.anchoredPosition = position;
        }

        private void Update()
        {
            if (_selectAtFrame >= 0 && Time.frameCount >= _selectAtFrame)
            {
                _selectAtFrame = -1;
                _options[0].SelectQuietly();
                Highlight(0);
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
