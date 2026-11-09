using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メインメニューの背景の連動（D-88 の B7。ユーザーの選択、2026-11-07）: 右側に、選んでいる項目の名前の見出しの札（HeadingPlate）を出し、
    /// その下に説明（日本語 ／ 英語は言語の設定に従う）。項目を変えると、前の札は左へ抜けて消え、新しい札が右から入る。
    /// 見出しの札は白地に黒の文字で、縁と裏の札は項目の色（MenuOption.Accent。札の縁・裏の札・縁を回る光・左の絵と同じ色。2026-11-08 の
    /// ユーザーの要望。それまでは項目の色の縁取りの大きな文字）。
    /// 左の白い背景（MainMenuPanel。D-91）の斜めの帯も、選んでいる項目の色にする。説明は白地に黒の文字の説明の札（DescriptionPlate。縁と
    /// 裏の札は項目の色。ステージ選択と同じ。2026-11-08）。
    /// </summary>
    public sealed class MainMenuBackdrop : MonoBehaviour
    {
        [SerializeField] private MenuOption[] _items = new MenuOption[0];
        [SerializeField] private string[] _english = new string[0];
        [SerializeField] private string[] _japanese = new string[0];
        [Tooltip("名前の見出しの札")]
        [SerializeField] private HeadingPlate _heading;
        [SerializeField] private DescriptionPlate _description;
        [Tooltip("左の背景（項目の色の帯。D-91）")]
        [SerializeField] private MainMenuPanel _panel;
        [Tooltip("左上のロゴの縁取り（縁の色を選んだ項目の色に移す。D-92 の T2。2026-11-09）")]
        [SerializeField] private TitleStroke _logo;

        private Vector2 _descriptionHome;
        private float _changedAt = -10f;
        private int _shown = -1;

        /// <summary>今出している項目（テスト用。まだなければ -1）。</summary>
        public int Shown => _shown;

        /// <summary>今出している名前（テスト用）。</summary>
        public string Word => _heading != null ? _heading.Word : string.Empty;

        /// <summary>名前の見出しの札（テスト用）。</summary>
        public HeadingPlate Heading => _heading;

        /// <summary>左の背景（テスト用）。</summary>
        public MainMenuPanel Panel => _panel;

        /// <summary>左上のロゴの縁取り（テスト用）。</summary>
        public TitleStroke Logo => _logo;

        /// <summary>今出している説明（テスト用）。</summary>
        public string Description => _description != null ? _description.Value : string.Empty;

        /// <summary>説明の札（テスト用）。</summary>
        public DescriptionPlate DescriptionPlate => _description;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(MenuOption[] items, string[] english, string[] japanese, HeadingPlate heading, DescriptionPlate description, MainMenuPanel panel,
            TitleStroke logo = null)
        {
            _panel = panel;
            _logo = logo;
            _items = items;
            _english = english;
            _japanese = japanese;
            _heading = heading;
            _description = description;
        }

        private void Awake()
        {
            if (_description != null)
            {
                _descriptionHome = ((RectTransform)_description.transform).anchoredPosition;
            }
            for (int i = 0; i < _items.Length; i++)
            {
                int index = i;
                if (_items[i] != null)
                {
                    _items[i].Selected += () => Show(index);
                }
            }
        }

        private void OnEnable()
        {
            // 画面を出し直したときは、前の名前の動きを残さない（見出しの札も自分で消す）。
            _shown = -1;
            if (_description != null)
            {
                // 項目を選ぶまで説明の札は見せない。
                DescriptionGroup().alpha = 0f;
            }
        }

        /// <summary>index の項目の名前と説明を出す。</summary>
        public void Show(int index)
        {
            if (index < 0 || index >= _items.Length || index == _shown)
            {
                return;
            }
            _shown = index;
            if (_heading != null && _items[index] != null)
            {
                _heading.Show(_items[index].Label, _items[index].Accent);
            }
            if (_panel != null && _items[index] != null)
            {
                _panel.Select(index, _items[index].Accent);
            }
            if (_logo != null && _items[index] != null)
            {
                _logo.SetAccent(_items[index].Accent);
            }
            if (_description != null && _items[index] != null)
            {
                _description.Show(Loc.T(At(_english, index), At(_japanese, index)), _items[index].Accent);
            }
            _changedAt = Time.unscaledTime;
            Animate();
        }

        private static string At(string[] texts, int index) => texts != null && index < texts.Length ? texts[index] : string.Empty;

        private void Update()
        {
            Animate();
        }

        private void Animate()
        {
            if (_description == null)
            {
                return;
            }
            float t = Time.unscaledTime - _changedAt;
            float p = UiEase.Clamp01((t - 0.1f) / 0.32f);
            ((RectTransform)_description.transform).anchoredPosition = _descriptionHome + new Vector2(0f, -10f * (1f - UiEase.OutCubic(p)));
            DescriptionGroup().alpha = p;
        }

        private CanvasGroup DescriptionGroup()
        {
            CanvasGroup group = _description.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = _description.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            return group;
        }
    }
}
