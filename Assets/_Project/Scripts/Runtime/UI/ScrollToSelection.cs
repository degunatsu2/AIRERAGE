using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// スクロールの中の項目を選んだら（キーボード・ゲームパッドで動かしたら）、その項目が見える所までスクロールする（オプションの CONTROLS、
    /// ステージ選択など。2026-11-06）。選んだ項目が変わったときだけ動かすので、ホイールでのスクロールは邪魔しない。
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public sealed class ScrollToSelection : MonoBehaviour
    {
        [Tooltip("見せるときの上下の余白（ピクセル）")]
        [SerializeField] private float _margin = 12f;

        private ScrollRect _scroll;
        private GameObject _last;

        private void Awake()
        {
            _scroll = GetComponent<ScrollRect>();
        }

        private void OnEnable()
        {
            _last = null;
        }

        private void LateUpdate()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == _last)
            {
                return;
            }
            _last = selected;
            if (selected != null && _scroll != null && _scroll.content != null && selected.transform.IsChildOf(_scroll.content))
            {
                Reveal((RectTransform)selected.transform);
            }
        }

        /// <summary>item が見える所まで、中身を上下に動かす。</summary>
        public void Reveal(RectTransform item)
        {
            RectTransform content = _scroll.content;
            var view = (RectTransform)_scroll.transform;
            Vector3[] corners = new Vector3[4];
            item.GetWorldCorners(corners);
            // 中身の座標で、項目の上と下（上が 0、下へ正）。
            float top = -(content.InverseTransformPoint(corners[1]).y) - _margin;
            float bottom = -(content.InverseTransformPoint(corners[0]).y) + _margin;
            float height = view.rect.height;
            Vector2 position = content.anchoredPosition;
            if (top < position.y)
            {
                position.y = top;
            }
            else if (bottom > position.y + height)
            {
                position.y = bottom - height;
            }
            position.y = Mathf.Clamp(position.y, 0f, Mathf.Max(0f, content.rect.height - height));
            _scroll.StopMovement();
            content.anchoredPosition = position;
        }
    }
}
