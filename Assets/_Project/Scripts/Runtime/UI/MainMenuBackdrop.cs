using Airerage.Runtime.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メインメニューの背景の連動（D-88 の B7。ユーザーの選択、2026-11-07）: 右側に、選んでいる項目の名前を大きな縁取りの文字で出し、
    /// その下に説明（日本語 ／ 英語は言語の設定に従う）。項目を変えると、前の名前は左へ流れて消え、新しい名前が右から入る。
    /// 名前の中は白、縁取りは項目の色（MenuOption.Accent。札の縁・裏の札・縁を回る光と同じ色。背景の色の光は出さない。2026-11-07）。
    /// </summary>
    public sealed class MainMenuBackdrop : MonoBehaviour
    {
        private const float OutSeconds = 0.22f;
        private const float InSeconds = 0.38f;
        private const float OutlineAlpha = 0.95f;

        [SerializeField] private MenuOption[] _items = new MenuOption[0];
        [SerializeField] private string[] _english = new string[0];
        [SerializeField] private string[] _japanese = new string[0];
        [Tooltip("名前の大きな文字（2 つを交互に使う。出ていく名前と入ってくる名前）")]
        [SerializeField] private Text _wordA;
        [SerializeField] private Text _wordB;
        [SerializeField] private Text _description;

        private Text _current;
        private Text _previous;
        private Vector2 _wordHome;
        private Vector2 _descriptionHome;
        private float _changedAt = -10f;
        private int _shown = -1;

        /// <summary>今出している項目（テスト用。まだなければ -1）。</summary>
        public int Shown => _shown;

        /// <summary>今出している名前（テスト用）。</summary>
        public string Word => _current != null ? _current.text : string.Empty;

        /// <summary>今出している説明（テスト用）。</summary>
        public string Description => _description != null ? _description.text : string.Empty;

        /// <summary>今出している名前の縁取りの色（テスト用）。</summary>
        public Color WordOutline
        {
            get
            {
                Outline outline = _current != null ? _current.GetComponent<Outline>() : null;
                return outline != null ? outline.effectColor : Color.clear;
            }
        }

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(MenuOption[] items, string[] english, string[] japanese, Text wordA, Text wordB, Text description)
        {
            _items = items;
            _english = english;
            _japanese = japanese;
            _wordA = wordA;
            _wordB = wordB;
            _description = description;
        }

        private void Awake()
        {
            _current = _wordA;
            _previous = _wordB;
            if (_wordA != null)
            {
                _wordHome = _wordA.rectTransform.anchoredPosition;
            }
            if (_description != null)
            {
                _descriptionHome = _description.rectTransform.anchoredPosition;
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
            // 画面を出し直したときは、前の名前の動きを残さない。
            _shown = -1;
            if (_wordA != null)
            {
                _wordA.text = string.Empty;
            }
            if (_wordB != null)
            {
                _wordB.text = string.Empty;
            }
            if (_description != null)
            {
                _description.text = string.Empty;
            }
        }

        /// <summary>index の項目の名前と説明を出す。</summary>
        public void Show(int index)
        {
            if (index < 0 || index >= _items.Length || index == _shown)
            {
                return;
            }
            bool first = _shown < 0;
            _shown = index;
            // 前の名前を、出ていく側へ。
            Text old = _current;
            _current = _previous;
            _previous = old;
            if (_current != null)
            {
                _current.text = _items[index] != null ? _items[index].Label : string.Empty;
                // 縁取りは項目の色。
                Color outline = _items[index] != null ? _items[index].Accent : Color.white;
                outline.a = OutlineAlpha;
                foreach (Outline effect in _current.GetComponents<Outline>())
                {
                    effect.effectColor = outline;
                }
            }
            if (_description != null)
            {
                _description.text = Loc.T(At(_english, index), At(_japanese, index));
            }
            _changedAt = first ? -10f : Time.unscaledTime;
            if (first && _previous != null)
            {
                _previous.text = string.Empty;
            }
            Animate();
        }

        private static string At(string[] texts, int index) => texts != null && index < texts.Length ? texts[index] : string.Empty;

        private void Update()
        {
            Animate();
        }

        private void Animate()
        {
            float t = Time.unscaledTime - _changedAt;
            if (_previous != null)
            {
                float p = UiEase.Clamp01(t / OutSeconds);
                _previous.rectTransform.anchoredPosition = _wordHome + new Vector2(-60f * UiEase.InCubic(p), 0f);
                SetAlpha(_previous, 1f - p);
            }
            if (_current != null)
            {
                float p = UiEase.Clamp01(t / InSeconds);
                _current.rectTransform.anchoredPosition = _wordHome + new Vector2(80f * (1f - UiEase.OutCubic(p)), 0f);
                SetAlpha(_current, p);
            }
            if (_description != null)
            {
                float p = UiEase.Clamp01((t - 0.08f) / 0.32f);
                _description.rectTransform.anchoredPosition = _descriptionHome + new Vector2(0f, -10f * (1f - UiEase.OutCubic(p)));
                SetAlpha(_description, p);
            }
        }

        // 縁取り（Outline）ごと薄くするため、文字の物の CanvasGroup で濃さを変える。
        private static void SetAlpha(Graphic graphic, float alpha)
        {
            CanvasGroup group = graphic.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = graphic.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            group.alpha = alpha;
        }
    }
}
