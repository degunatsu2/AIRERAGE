using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの項目 1 つ（uGUI の Button。D-65）。キーボード・ゲームパッドで選んでいる項目は、縁が明るくなって少し右へ出る。
    /// マウスを乗せると、その項目を選ぶ（選んでいる項目は 1 つだけ）。使えない項目は灰色で、下に理由（「COMING SOON」など）を出す。
    /// 戻る操作（Esc、ゲームパッドの B）は Cancelled で知らせる。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class MenuOption : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, ICancelHandler
    {
        private const float SelectedShift = 18f;

        [SerializeField] private SlantedPlate _plate;
        [SerializeField] private Text _label;
        [Tooltip("使えないときの理由（なければ出さない）")]
        [SerializeField] private Text _note;
        [SerializeField] private Color _accent = new Color(0.35f, 0.85f, 1f);

        private static readonly Color IdleFill = new Color(0.03f, 0.05f, 0.09f, 0.78f);
        private static readonly Color SelectedFill = new Color(0.08f, 0.16f, 0.24f, 0.9f);
        private static readonly Color IdleBorder = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DisabledText = new Color(1f, 1f, 1f, 0.35f);

        private Button _button;
        private RectTransform _rect;
        private Vector2 _home;
        private bool _selected;

        /// <summary>選ばれた（決定した）とき。</summary>
        public Button.ButtonClickedEvent Clicked => Button.onClick;

        /// <summary>この項目を選んでいるときに、戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>今選んでいるか（テスト用）。</summary>
        public bool IsSelected => _selected;

        public Button Button => _button != null ? _button : _button = GetComponent<Button>();

        public string Label => _label != null ? _label.text : string.Empty;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(SlantedPlate plate, Text label, Text note, Color accent)
        {
            _plate = plate;
            _label = label;
            _note = note;
            _accent = accent;
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

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _home = _rect.anchoredPosition;
            Refresh();
        }

        private void OnDisable()
        {
            _selected = false;
            if (_rect != null)
            {
                _rect.anchoredPosition = _home;
            }
        }

        public void OnSelect(BaseEventData eventData)
        {
            _selected = true;
            Refresh();
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

        private void Refresh()
        {
            bool available = Button.interactable;
            if (_plate != null)
            {
                _plate.color = _selected ? SelectedFill : IdleFill;
                _plate.BorderColor = _selected ? _accent : IdleBorder;
            }
            if (_label != null)
            {
                _label.color = !available ? DisabledText : _selected ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            }
            if (_rect != null)
            {
                _rect.anchoredPosition = _home + new Vector2(_selected ? SelectedShift : 0f, 0f);
            }
        }
    }
}
