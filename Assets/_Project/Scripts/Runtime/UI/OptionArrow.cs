using UnityEngine;
using UnityEngine.EventSystems;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 値の左右の矢印のクリック（オプション。docs/15 T6-9。ユーザーの要望、2026-10-20）: 値の左半分をクリックすると左、右半分をクリック
    /// すると右を入れたことにする（OptionStepper.Step）。項目も選ぶ（音は値の変化の音だけ）。透明な Graphic（raycastTarget）に付ける。
    /// </summary>
    public sealed class OptionArrow : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private OptionStepper _stepper;
        [Tooltip("-1 が左、+1 が右")]
        [SerializeField] private int _direction = 1;

        public int Direction => _direction;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(OptionStepper stepper, int direction)
        {
            _stepper = stepper;
            _direction = direction < 0 ? -1 : 1;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_stepper == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            if (_stepper.TryGetComponent(out MenuOption option) && !option.IsSelected)
            {
                option.SelectQuietly();
            }
            _stepper.Step(_direction);
        }
    }
}
