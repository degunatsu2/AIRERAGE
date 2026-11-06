using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの選んでいる項目が外れたら（マウスで背景を押したなど）、前に選んでいた項目を選び直す（EventSystem に置く。2026-11-06）。
    /// 何も選んでいないと、Esc ／ B の「戻る」とキーボード・ゲームパッドの移動の行き先がなくなるため（ステージ選択で Esc が効かなかった）。
    /// 前の項目が隠れている・使えないときは選び直さない（タイトルのように、わざと何も選ばない画面はそのまま）。
    /// </summary>
    [RequireComponent(typeof(EventSystem))]
    public sealed class SelectionKeeper : MonoBehaviour
    {
        private EventSystem _events;
        private GameObject _last;

        private void Awake()
        {
            _events = GetComponent<EventSystem>();
        }

        private void LateUpdate()
        {
            GameObject current = _events.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy)
            {
                _last = current;
                return;
            }
            if (_last == null || !_last.activeInHierarchy || _events.alreadySelecting)
            {
                return;
            }
            Selectable selectable = _last.GetComponent<Selectable>();
            if (selectable != null && selectable.IsInteractable())
            {
                _events.SetSelectedGameObject(_last);
            }
        }
    }
}
