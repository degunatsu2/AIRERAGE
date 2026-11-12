using System;
using Airerage.Runtime.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの項目 1 つ（uGUI の Button。D-65）。キーボード・ゲームパッドで選んでいる項目は、縁が明るくなって少し右へ出る。
    /// マウスを乗せると、その項目を選ぶ（選んでいる項目は 1 つだけ）。使えない項目は灰色で、下に理由（「COMING SOON」など）を出す。
    /// 戻る操作（Esc、ゲームパッドの B）は Cancelled で知らせる。
    /// 選んだときにカーソルの音、決定したときに決定の音（UiSounds。ユーザーの要望、2026-10-16）。画面を開いたときに最初の項目を
    /// 選ぶ（SelectQuietly）ときは鳴らさない。
    /// 選んだとき・決定したときの演出（D-88。ユーザーが選んだ B1・B2・B3・B9・B10・B12・B13。2026-11-07）:
    /// - B1 導火線の縁・B3 裏の札（表の札と少し角度をずらし、選んだ瞬間に振れてから揺れる。2026-11-08）・B9 の札の傾きと光・B10 の白い光は
    ///   札（SlantedPlate）が描く。
    /// - B2: 選んだ所へ少し行きすぎてから戻って止まり、残像が追いかける。
    /// - B5（英字の名前のデコード）は、長く遊ぶと読みにくいので消した（ユーザーの要望、2026-11-08）。
    /// - B9: マウスを札に乗せている間、マウスの方へ札がわずかに傾き、光の反射が付いてくる（ゲームパッドでは動かない）。
    /// - B10: 決定した瞬間に札がつぶれて戻り、白く光り、輪と金色の火花が散る（UiBurst。マウスで決定したときの輪と火花は、クリックした
    ///   所に ClickSparks が出す。2026-11-07）。
    /// - B12: 子に絵（MenuIcon）があれば、選んでいる間だけ動かす。
    /// - B13（Recede をつけた一覧だけ）: 同じ親のほかの項目を選んでいる間は、暗くして少し下げる。
    /// 文字の演出（D-90。ユーザーが選んだ W1・W2・W16・W17。2026-11-08。動きは見本のページと同じ。距離は札の高さ 58 を基準に大きくする）:
    /// - W1 スラントイン: 選んだ瞬間、名前が左下から札の傾きに沿ってすべり込み、0.16 秒で濃くなる。
    /// - W2 レターライズ: 選んだ瞬間、名前の文字が 1 文字ずつ順にはね上がって戻る（LetterRise。文字の網だけを動かす）。
    /// - W16 サブラベル（_sub がある項目。メインメニュー）: 札の右に、日本語の小さな説明が左からすべり出る。
    /// - W17 シェブロン（_chevrons がある項目。メインメニューなどの一覧）: 名前が右へ寄り、前に項目の色の「›››」が順に光る（ChevronMarks）。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class MenuOption : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, ICancelHandler
    {
        /// <summary>決定してつぶれて戻るまでの秒数（B10）。</summary>
        public const float PunchSeconds = 0.22f;
        /// <summary>ほかの項目を選んでいるときの濃さ（B13）。</summary>
        public const float RecedeAlpha = 0.5f;

        private const float BackSeconds = 0.18f;
        // 文字の演出の大きさの基準（見本のページの札の高さ）と、W1・W16・W17 の秒数と距離（基準の札でのピクセル）。
        private const float MotionUnitHeight = 58f;
        private const float SlantSeconds = 0.16f;
        private static readonly Vector2 SlantFrom = new Vector2(-14f, -3f);
        private const float ChevronShift = 26f;
        private const float ChevronShiftSeconds = 0.18f;
        private const float SubTravel = 10f;
        private const float SubFadeSeconds = 0.16f;
        private const float SubMoveSeconds = 0.2f;
        // 縮めて収めるとき、名前と W16 の説明の間に空ける幅（基準の札でのピクセル）。
        private const float SubGap = 12f;

        [SerializeField] private SlantedPlate _plate;
        [SerializeField] private Text _label;
        [Tooltip("使えないときの理由（なければ出さない）")]
        [SerializeField] private Text _note;
        [SerializeField] private Color _accent = new Color(0.35f, 0.85f, 1f);
        [Tooltip("選んでいるときに動かす量（ピクセル。メニューは右へ、キャラクターの札は上へ）")]
        [SerializeField] private Vector2 _selectedShift = new Vector2(18f, 0f);
        [Tooltip("文字が枠の幅より長ければ、1 行のまま字を小さくして収める（待機所の SPLIT など）")]
        [SerializeField] private bool _shrinkToFit;
        [Tooltip("選んで札が動いても、その場にとどめる子（札の左のチェックの箱など。押しやすいように。2026-11-06）")]
        [SerializeField] private RectTransform[] _pinned = new RectTransform[0];
        [Tooltip("同じ親のほかの項目を選んでいる間は、暗くして少し下げる（B13。メインメニューなどの一覧）")]
        [SerializeField] private bool _recede;
        [Tooltip("下げている間の濃さ（B13。白い背景の上では薄くしすぎると読めないので、メインメニューは濃いめ。2026-11-08）")]
        [SerializeField] private float _recedeAlpha = RecedeAlpha;
        [Tooltip("導火線の光も項目の色（_accent）にする（メインメニュー。2026-11-07）")]
        [SerializeField] private bool _accentFuse;
        [Tooltip("選んでいる間、名前の前に出す「›››」（W17。なければ名前は寄らない）")]
        [SerializeField] private ChevronMarks _chevrons;
        [Tooltip("選んでいる間、札の右にすべり出る小さな説明（W16。なければ出さない）")]
        [SerializeField] private Text _sub;
        private Vector2[] _pinnedHomes;

        // 縮める前の字の大きさ（Awake で覚える）と、縮めるときのいちばん小さい大きさ。収めたときの W16 の説明の文（言語を変えて文が
        // 変わったら収め直す）。
        private int _baseFontSize;
        private string _fittedSub;
        private const int MinFitFontSize = 16;

        // 表の札の中は不透明（半透明だと、後ろに重なる裏の札が透けて見えた。ユーザーの指摘、2026-11-08）。
        private static readonly Color IdleFill = new Color(0.03f, 0.05f, 0.09f, 1f);
        private static readonly Color SelectedFill = new Color(0.08f, 0.16f, 0.24f, 1f);
        private static readonly Color IdleBorder = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DisabledText = new Color(1f, 1f, 1f, 0.35f);

        private Button _button;
        private RectTransform _rect;
        private Vector2 _home;
        private bool _selected;
        private bool _quiet;
        private UiSounds _sounds;
        private MenuIcon _icon;
        private UiBurst _burst;
        private CanvasGroup _group;

        // 札の位置の動き（B2・B13）: 今の位置（home からのずれ）と、動きの始まりと行き先。
        private Vector2 _shiftNow;
        private Vector2 _shiftFrom;
        private Vector2 _shiftTo;
        private float _shiftAt = -10f;
        private bool _shiftOvershoot;
        private bool _receded;
        private float _punchAt = -10f;
        private Vector2 _lean;
        private float _sheenX = 0.5f;

        // 文字の演出（W1・W2・W16・W17）: 名前の置き場所と今のずれ、W17 の右へ寄る動き、W1 の始まり、W16 の説明の動き。
        private float _unit = 1f;
        private Vector2 _labelHome;
        private Vector2 _subHome;
        private LetterRise _rise;
        private float _labelShiftNow;
        private float _labelShiftFrom;
        private float _labelShiftTo;
        private float _labelShiftAt = -10f;
        private float _slantAt = -10f;
        private float _subShown;
        private float _subFrom;
        private float _subTo;
        private float _subAt = -10f;

        /// <summary>選ばれた（決定した）とき。</summary>
        public Button.ButtonClickedEvent Clicked => Button.onClick;

        /// <summary>この項目を選んでいるときに、戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>この項目が選ばれた（カーソルが来た）。</summary>
        public event Action Selected;

        /// <summary>今選んでいるか（テスト用）。</summary>
        public bool IsSelected => _selected;

        /// <summary>ほかの項目を選んでいるので、暗く下げているか（B13。テスト用）。</summary>
        public bool IsReceded => _receded;

        public Button Button => _button != null ? _button : _button = GetComponent<Button>();

        /// <summary>名前。</summary>
        public string Label => _label != null ? _label.text : string.Empty;

        /// <summary>名前の文字（字体を変えるときに使う）。</summary>
        public Text LabelText => _label;

        /// <summary>札（テスト用）。</summary>
        public SlantedPlate Plate => _plate;

        /// <summary>決定の輪と火花（まだ決定していなければ null。テスト用）。</summary>
        public UiBurst Burst => _burst;

        /// <summary>名前の文字のはね（W2。テスト用）。</summary>
        public LetterRise Rise => _rise;

        /// <summary>名前の前の「›››」（W17。なければ null。テスト用）。</summary>
        public ChevronMarks Chevrons => _chevrons;

        /// <summary>札の右の小さな説明（W16。なければ null。テスト用）。</summary>
        public Text Sub => _sub;

        /// <summary>同じ親のほかの項目を選んでいる間、暗く下げるか（B13）。</summary>
        public bool Recedes => _recede;

        /// <summary>導火線の光が札の色か（メインメニューと同じ。B1）。</summary>
        public bool AccentFuse => _accentFuse;

        /// <summary>文字が長ければ 1 行のまま縮めて収めるか。</summary>
        public bool ShrinksToFit => _shrinkToFit;

        /// <summary>高さ height の札で、選んで「›››」（W17）の分だけ名前が右へ寄る量（ピクセル）。</summary>
        public static float ChevronShiftFor(float height) => ChevronShift * Mathf.Max(0.5f, height / MotionUnitHeight);

        /// <summary>名前が置き場所からずれている量（W1・W17。テスト用）。</summary>
        public Vector2 LabelOffset => _label != null ? _label.rectTransform.anchoredPosition - _labelHome : Vector2.zero;

        /// <summary>文字の演出の W17 の印と W16 の説明を付ける（シーンのビルダー用。どちらも null でよい）。</summary>
        public void ConfigureTextMotion(ChevronMarks chevrons, Text sub)
        {
            _chevrons = chevrons;
            _sub = sub;
            Refresh();
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(SlantedPlate plate, Text label, Text note, Color accent)
        {
            _plate = plate;
            _label = label;
            _note = note;
            _accent = accent;
        }

        /// <summary>項目の色（選んでいるときの縁・裏の札・残像・縁の光の色）。</summary>
        public Color Accent => _accent;

        /// <summary>
        /// 項目の色を変える（シーンのビルダー用）。fuse なら、選んだ瞬間に縁を回る光も項目の色にする（メインメニュー。2026-11-07）。
        /// </summary>
        public void SetAccent(Color accent, bool fuse)
        {
            _accent = accent;
            _accentFuse = fuse;
            // 左の絵（B12）も項目の色に（選んでいないときは少し薄く。2026-11-07）。
            MenuIcon icon = GetComponentInChildren<MenuIcon>(true);
            if (icon != null)
            {
                Color idle = Color.Lerp(accent, Color.white, 0.2f);
                idle.a = 0.85f;
                icon.SetColors(idle, Color.Lerp(accent, Color.white, 0.1f));
            }
            Refresh();
        }

        /// <summary>同じ親のほかの項目を選んでいる間、暗く下げるか（B13。シーンのビルダー用）。alpha は下げている間の濃さ。</summary>
        public void SetRecede(bool recede, float alpha = RecedeAlpha)
        {
            _recede = recede;
            _recedeAlpha = alpha;
        }

        /// <summary>項目の文字を変える（READY ／ CANCEL READY など）。</summary>
        public void SetLabel(string text)
        {
            if (_label != null && _label.text != text)
            {
                _label.text = text;
                FitLabel();
            }
        }

        /// <summary>文字が長ければ 1 行のまま縮めて枠に収めるか（シーンのビルダー用）。</summary>
        public void SetShrinkToFit(bool shrink)
        {
            _shrinkToFit = shrink;
        }

        /// <summary>
        /// W16 の小さな説明の文を変える（英語 en と日本語 ja。待機所の READY ／ CANCEL READY など、名前が変わる項目）。説明のない項目では
        /// 何もしない。
        /// </summary>
        public void SetSub(string en, string ja)
        {
            if (_sub == null)
            {
                return;
            }
            LocalizedText localized = _sub.GetComponent<LocalizedText>();
            if (localized != null && localized.English == en && localized.Japanese == ja)
            {
                return;
            }
            LocalizedText.Attach(_sub, en, ja);
            FitLabel();
        }

        // 字の大きさを、元の大きさから、文字の幅が枠に収まるまで小さくする（MinFitFontSize まで）。
        private void FitLabel()
        {
            if (!_shrinkToFit || _label == null)
            {
                return;
            }
            if (_baseFontSize <= 0)
            {
                _baseFontSize = _label.fontSize;
            }
            _fittedSub = _sub != null ? _sub.text : null;
            float width = FitWidth();
            int size = _baseFontSize;
            _label.fontSize = size;
            while (size > MinFitFontSize && _label.preferredWidth > width)
            {
                size -= 2;
                _label.fontSize = size;
            }
        }

        // 名前を収める幅: 選んで W17 の「›››」の分だけ右へ寄り、札の右に W16 の説明が出ても重ならない幅（待機所。2026-11-09）。
        // 名前と説明は札の子なので、札の中の位置で比べる（動いている途中でも、置き場所の位置で）。
        private float FitWidth()
        {
            RectTransform label = _label.rectTransform;
            bool awake = _rect != null;
            float unit = awake ? _unit : Mathf.Max(0.5f, ((RectTransform)transform).rect.height / MotionUnitHeight);
            float shift = _chevrons != null ? ChevronShift * unit : 0f;
            float width = label.rect.width - shift;
            if (_sub == null || string.IsNullOrEmpty(_sub.text))
            {
                return width;
            }
            RectTransform sub = _sub.rectTransform;
            Vector2 labelHome = awake ? _labelHome : label.anchoredPosition;
            Vector2 subHome = awake ? _subHome : sub.anchoredPosition;
            float labelLeft = label.localPosition.x - (label.anchoredPosition.x - labelHome.x) + label.rect.xMin + shift;
            float subRight = sub.localPosition.x - (sub.anchoredPosition.x - subHome.x) + sub.rect.xMax;
            float subLeft = subRight - Mathf.Min(sub.rect.width, _sub.preferredWidth);
            return Mathf.Min(width, subLeft - SubGap * unit - labelLeft);
        }

        /// <summary>名前が、選んで右へ寄り W16 の説明が出ても、説明に重ならずに収まっているか（テスト用）。</summary>
        public bool LabelFitsBesideSub => _label == null || _label.preferredWidth <= FitWidth() + 0.5f;

        /// <summary>
        /// 項目の置き場所（選んでいないときの anchoredPosition）を変える（出す項目が変わる画面で並べ直す。待機所。docs/19 T9-5）。
        /// </summary>
        public void SetHome(Vector2 position)
        {
            if (_rect == null)
            {
                _rect = (RectTransform)transform;
            }
            _home = position;
            Place();
        }

        /// <summary>選んでいるときに動かす量を変える（シーンのビルダー用）。</summary>
        public void SetSelectedShift(Vector2 shift)
        {
            _selectedShift = shift;
        }

        /// <summary>使えるかと、使えないときの理由を変える。</summary>
        public void SetAvailable(bool available, string reason = "")
        {
            Button.interactable = available;
            if (_note != null)
            {
                _note.text = available ? string.Empty : reason;
            }
            Refresh();
        }

        private UiSounds Sounds => _sounds != null ? _sounds : _sounds = UiSounds.Find();

        /// <summary>音を鳴らさずに、この項目を選ぶ（画面を開いたときの最初の項目）。</summary>
        public void SelectQuietly()
        {
            if (EventSystem.current == null)
            {
                return;
            }
            _quiet = true;
            EventSystem.current.SetSelectedGameObject(gameObject);
            _quiet = false;
        }

        /// <summary>
        /// 決定の衝撃（B10）: つぶれて戻り、札が白く光り、輪と金色の火花が散る。決定したときに自分で出す（画面を切り替える決定では、
        /// 画面の切り替えが写しを作る前にも呼ぶ。同じフレームに 2 回呼んでも 1 回だけ）。
        /// </summary>
        public void Punch()
        {
            float now = Time.unscaledTime;
            if (now - _punchAt < 0.05f)
            {
                return;
            }
            _punchAt = now;
            if (_plate != null)
            {
                _plate.Flash();
            }
            // マウスで決定したときは、クリックした所にクリックの火花（ClickSparks）が出ているので、札からは出さない（2026-11-07）。
            if (DecidedWithMouse() && ClickSparks.Covers(this))
            {
                return;
            }
            if (_burst == null)
            {
                var go = new GameObject("Burst", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                // 札の中の並び（LayoutGroup）に入れない。
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                _burst = go.AddComponent<UiBurst>();
            }
            _burst.transform.SetAsLastSibling();
            _burst.Play();
        }

        // この決定がマウスのクリックか（uGUI はマウスのボタンを離したフレームに決定する）。
        private static bool DecidedWithMouse()
        {
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasReleasedThisFrame;
        }

        private void Start()
        {
            // 名前を縮めて収める札は、出したときにも合わせる（実行中に名前を変えない札。2026-11-12）。
            FitLabel();
        }

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            if (_label != null && _baseFontSize <= 0)
            {
                _baseFontSize = _label.fontSize;
            }
            _icon = GetComponentInChildren<MenuIcon>(true);
            _unit = Mathf.Max(0.5f, _rect.rect.height / MotionUnitHeight);
            if (_label != null)
            {
                _labelHome = _label.rectTransform.anchoredPosition;
                _rise = _label.GetComponent<LetterRise>();
                if (_rise == null)
                {
                    _rise = _label.gameObject.AddComponent<LetterRise>();
                }
            }
            if (_sub != null)
            {
                _subHome = _sub.rectTransform.anchoredPosition;
                SetSubAlpha(0f);
            }
            if (_recede)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null)
                {
                    _group = gameObject.AddComponent<CanvasGroup>();
                }
            }
            FitLabel();
            Button.onClick.AddListener(() =>
            {
                Sounds.Play(SoundId.UiConfirm);
                Punch();
            });
            Refresh();
        }

        private void OnDisable()
        {
            _selected = false;
            _receded = false;
            _shiftNow = Vector2.zero;
            _shiftFrom = Vector2.zero;
            _shiftTo = Vector2.zero;
            _shiftAt = -10f;
            _punchAt = -10f;
            _lean = Vector2.zero;
            if (_rect != null)
            {
                _rect.anchoredPosition = _home;
                _rect.localScale = Vector3.one;
            }
            if (_plate != null)
            {
                _plate.ResetEffects();
            }
            if (_group != null)
            {
                _group.alpha = 1f;
            }
            if (_icon != null)
            {
                _icon.Selected = false;
            }
            PlacePinned(Vector2.zero);
            ResetTextMotion();
        }

        // 文字の演出を止めて元に戻す（画面を閉じたとき）。
        private void ResetTextMotion()
        {
            _labelShiftNow = 0f;
            _labelShiftFrom = 0f;
            _labelShiftTo = 0f;
            _labelShiftAt = -10f;
            _slantAt = -10f;
            _subShown = 0f;
            _subFrom = 0f;
            _subTo = 0f;
            _subAt = -10f;
            if (_label != null && _rect != null)
            {
                _label.rectTransform.anchoredPosition = _labelHome;
                _label.canvasRenderer.SetAlpha(1f);
            }
            if (_rise != null)
            {
                _rise.Stop();
            }
            if (_chevrons != null)
            {
                _chevrons.Show(false);
            }
            if (_sub != null && _rect != null)
            {
                _sub.rectTransform.anchoredPosition = _subHome + new Vector2(-SubTravel * _unit, 0f);
                SetSubAlpha(0f);
            }
        }

        public void OnSelect(BaseEventData eventData)
        {
            _selected = true;
            if (!_quiet)
            {
                Sounds.Play(SoundId.UiMove);
            }
            if (_plate != null)
            {
                _plate.RunFuse();
                // 裏の札が伸びて出てから、ゆっくり揺れる（文字の演出の見本のページと同じ動き。2026-11-08）。
                _plate.SwingBack();
            }
            // 名前が左下からすべり込み（W1）、1 文字ずつはね上がる（W2）。
            _slantAt = Time.unscaledTime;
            if (_rise != null)
            {
                _rise.Play();
            }
            Refresh();
            Selected?.Invoke();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _selected = false;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Button.IsInteractable() && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        public void OnCancel(BaseEventData eventData)
        {
            Cancelled?.Invoke();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            // 言語を変えて W16 の説明の文が変わったら、名前を収め直す。
            if (_shrinkToFit && _sub != null && _sub.text != _fittedSub)
            {
                FitLabel();
            }

            // B13: 同じ親のほかの項目を選んでいる間は、暗く下げる。
            if (_recede)
            {
                bool receded = !_selected && SiblingSelected();
                if (receded != _receded)
                {
                    _receded = receded;
                    Retarget();
                }
                if (_group != null)
                {
                    _group.alpha = Mathf.MoveTowards(_group.alpha, _receded ? _recedeAlpha : 1f, dt / 0.2f * Mathf.Max(0.05f, 1f - _recedeAlpha));
                }
            }

            // B2: 選んだ所へ、少し行きすぎてから戻る（選ぶのをやめたときは、行きすぎずに戻る）。
            float moveSeconds = _shiftOvershoot ? SlantedPlate.MoveSeconds : BackSeconds;
            float m = (now - _shiftAt) / moveSeconds;
            Vector2 shift = m >= 1f ? _shiftTo
                : Vector2.LerpUnclamped(_shiftFrom, _shiftTo, _shiftOvershoot ? SlantedPlate.MoveEase(m) : UiEase.OutCubic(m));
            bool moved = shift != _shiftNow;
            _shiftNow = shift;

            // B10: 決定した瞬間につぶれて戻る（札の真ん中を中心に）。
            float punch = (now - _punchAt) / PunchSeconds;
            Vector2 scale = Vector2.one;
            if (punch >= 0f && punch < 1f)
            {
                scale = punch < 0.3f ? Vector2.Lerp(Vector2.one, new Vector2(1.07f, 0.84f), punch / 0.3f)
                    : punch < 0.65f ? Vector2.Lerp(new Vector2(1.07f, 0.84f), new Vector2(0.97f, 1.05f), (punch - 0.3f) / 0.35f)
                    : Vector2.Lerp(new Vector2(0.97f, 1.05f), Vector2.one, (punch - 0.65f) / 0.35f);
            }
            if (moved || (Vector2)_rect.localScale != scale)
            {
                _rect.localScale = new Vector3(scale.x, scale.y, 1f);
                Place();
            }

            AnimateText(now);

            // B9: マウスを乗せている間、マウスの方へわずかに傾き、光の反射が付いてくる。
            if (_plate != null)
            {
                Vector2 target = Vector2.zero;
                float sheen = 0f;
                if (_selected && PointerOver(out Vector2 local))
                {
                    Rect r = _rect.rect;
                    target = new Vector2(Mathf.Clamp((local.x - r.center.x) / (r.width * 0.5f), -1f, 1f),
                        Mathf.Clamp((local.y - r.center.y) / (r.height * 0.5f), -1f, 1f));
                    sheen = 1f;
                    _sheenX = Mathf.InverseLerp(r.xMin, r.xMax, local.x);
                }
                _lean = Vector2.Lerp(_lean, target, 1f - Mathf.Exp(-dt * 14f));
                if (_lean.sqrMagnitude < 0.00001f && target == Vector2.zero)
                {
                    _lean = Vector2.zero;
                }
                // 光の反射は、マウスが離れたら傾きと一緒に薄れる。
                _plate.SetLean(_lean, _sheenX, sheen > 0f ? 1f : Mathf.Clamp01(_lean.magnitude * 3f));
            }
        }

        // 文字の演出（W1・W17 の名前のずれと濃さ、W16 の説明）。
        private void AnimateText(float now)
        {
            if (_label != null)
            {
                float c = (now - _labelShiftAt) / ChevronShiftSeconds;
                _labelShiftNow = c >= 1f ? _labelShiftTo : Mathf.LerpUnclamped(_labelShiftFrom, _labelShiftTo, EaseOut(c));
                var offset = new Vector2(_labelShiftNow, 0f);
                float alpha = 1f;
                float w = (now - _slantAt) / SlantSeconds;
                if (w >= 0f && w < 1f)
                {
                    float e = EaseOut(w);
                    offset = Vector2.LerpUnclamped(SlantFrom * _unit, offset, e);
                    alpha = Mathf.Clamp01(e);
                }
                RectTransform rect = _label.rectTransform;
                if (rect.anchoredPosition != _labelHome + offset)
                {
                    rect.anchoredPosition = _labelHome + offset;
                }
                if (!Mathf.Approximately(_label.canvasRenderer.GetAlpha(), alpha))
                {
                    _label.canvasRenderer.SetAlpha(alpha);
                }
            }
            if (_sub != null)
            {
                float t = now - _subAt;
                _subShown = t >= SubFadeSeconds ? _subTo : Mathf.Lerp(_subFrom, _subTo, UiEase.CssEase(t / SubFadeSeconds));
                float move = t >= SubMoveSeconds ? 1f : EaseOut(t / SubMoveSeconds);
                float from = (_subTo > 0.5f ? -SubTravel : 0f) * _unit;
                float to = (_subTo > 0.5f ? 0f : -SubTravel) * _unit;
                var position = _subHome + new Vector2(Mathf.LerpUnclamped(from, to, move), 0f);
                if (_sub.rectTransform.anchoredPosition != position)
                {
                    _sub.rectTransform.anchoredPosition = position;
                }
                SetSubAlpha(_subShown);
            }
        }

        // 説明の濃さは文字の色に持つ（画面の切り替えの写しは色を引き継ぐ。描く側の濃さ CanvasRenderer.alpha は写しに引き継がれず、決定した
        // 瞬間の写しで全部の札に説明が出ていた。ユーザーの指摘、2026-11-08）。
        private void SetSubAlpha(float alpha)
        {
            Color c = _sub.color;
            if (!Mathf.Approximately(c.a, alpha))
            {
                c.a = alpha;
                _sub.color = c;
            }
        }

        private static float EaseOut(float t) => UiEase.SnapOut(t);

        // 名前の右へ寄る量（W17）と説明（W16）の行き先を決め、変わったら動き始める。
        private void RetargetText()
        {
            float now = Time.unscaledTime;
            bool live = isActiveAndEnabled && _rect != null && Application.isPlaying;
            float shift = _chevrons != null && _selected ? ChevronShift * _unit : 0f;
            if (!Mathf.Approximately(shift, _labelShiftTo))
            {
                _labelShiftFrom = live ? _labelShiftNow : shift;
                _labelShiftTo = shift;
                _labelShiftAt = live ? now : -10f;
            }
            float sub = _sub != null && _selected ? 1f : 0f;
            if (!Mathf.Approximately(sub, _subTo))
            {
                _subFrom = live ? _subShown : sub;
                _subTo = sub;
                _subAt = live ? now : -10f;
            }
            if (_chevrons != null)
            {
                Color mark = _accent;
                mark.a = 1f;
                _chevrons.color = mark;
                _chevrons.Show(_selected && live);
            }
            if (_sub != null)
            {
                Color text = _accent;
                text.a = _sub.color.a;
                _sub.color = text;
            }
        }

        // マウスがこの札の上にあるか（local はこの札の中の位置）。
        private bool PointerOver(out Vector2 local)
        {
            local = Vector2.zero;
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = mouse.position.ReadValue();
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screen, cam, out local) && _rect.rect.Contains(local);
        }

        // 同じ親のほかの項目を選んでいるか。
        private bool SiblingSelected()
        {
            GameObject current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return current != null && current != gameObject && current.transform.parent == transform.parent && current.activeInHierarchy
                && current.GetComponent<MenuOption>() != null;
        }

        private void Refresh()
        {
            bool available = Button.interactable;
            if (_plate != null)
            {
                _plate.color = _selected ? SelectedFill : IdleFill;
                _plate.BorderColor = _selected ? _accent : IdleBorder;
                _plate.AccentColor = _accent;
                _plate.FuseColor = _accentFuse ? Color.Lerp(_accent, Color.white, 0.35f) : Color.white;
                _plate.Glow = _selected;
                _plate.BackTarget = _selected ? 1f : 0f;
            }
            if (_label != null)
            {
                _label.color = !available ? DisabledText : _selected ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            }
            if (_icon != null)
            {
                _icon.Selected = _selected;
            }
            Retarget();
            RetargetText();
        }

        // 札の行き先（選んでいれば右へ、ほかを選んでいて Recede なら少し下げる）を決め、変わったら動き始める。
        private void Retarget()
        {
            Vector2 target = _selected ? _selectedShift : _receded ? -_selectedShift * 0.45f : Vector2.zero;
            if (target == _shiftTo)
            {
                return;
            }
            bool overshoot = _selected;
            if (!isActiveAndEnabled || _rect == null)
            {
                // 画面を出す前（まだ動かない）: すぐその位置に置く。
                _shiftNow = target;
                _shiftFrom = target;
                _shiftTo = target;
                _shiftAt = -10f;
                Place();
                return;
            }
            _shiftFrom = _shiftNow;
            _shiftTo = target;
            _shiftAt = Time.unscaledTime;
            _shiftOvershoot = overshoot;
            if (overshoot && _plate != null)
            {
                _plate.Trail(target - _shiftFrom);
            }
        }

        // 位置（置き場所＋ずれ。決定でつぶれている間は、札の真ん中が動かないように直す）。
        private void Place()
        {
            if (_rect == null)
            {
                return;
            }
            Vector2 size = _rect.rect.size;
            Vector2 pivot = _rect.pivot;
            Vector3 scale = _rect.localScale;
            var squash = new Vector2((1f - scale.x) * size.x * (0.5f - pivot.x), (1f - scale.y) * size.y * (0.5f - pivot.y));
            _rect.anchoredPosition = _home + _shiftNow + squash;
            PlacePinned(_shiftNow);
        }

        // とどめる子を、札の動いた分だけ逆へずらす（画面の上では動かない）。
        private void PlacePinned(Vector2 shift)
        {
            if (_pinned == null || _pinned.Length == 0)
            {
                return;
            }
            if (_pinnedHomes == null || _pinnedHomes.Length != _pinned.Length)
            {
                _pinnedHomes = new Vector2[_pinned.Length];
                for (int i = 0; i < _pinned.Length; i++)
                {
                    _pinnedHomes[i] = _pinned[i] != null ? _pinned[i].anchoredPosition : Vector2.zero;
                }
            }
            for (int i = 0; i < _pinned.Length; i++)
            {
                if (_pinned[i] != null)
                {
                    _pinned[i].anchoredPosition = _pinnedHomes[i] - shift;
                }
            }
        }

        /// <summary>選んで札が動いても、その場にとどめる子を決める（シーンのビルダー用）。</summary>
        public void SetPinned(params RectTransform[] children)
        {
            _pinned = children ?? new RectTransform[0];
            _pinnedHomes = null;
        }
    }
}
