using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// キャラクター選択のアイコン 1 つ（docs/15 6.9）。マウスを乗せたこと・クリックしたことを、キャラクター選択の画面に知らせるだけ
    /// （カーソルの移動と決定は CharacterSelectScreen が受け持つ。キーボード・ゲームパッドの項目の移動の対象にはしない）。
    /// </summary>
    public sealed class CharacterIcon : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [Tooltip("キャラクターの番号（CharacterRoster の番号）")]
        [SerializeField] private int _character;

        /// <summary>マウスを乗せた（キャラクターの番号）。</summary>
        public event Action<int> Hovered;

        /// <summary>左クリックした（キャラクターの番号）。</summary>
        public event Action<int> Clicked;

        public int Character => _character;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>シーンのビルダー用: キャラクターの番号を設定する。</summary>
        public void Configure(int character)
        {
            _character = character;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Hovered?.Invoke(_character);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Clicked?.Invoke(_character);
            }
        }
    }
}
