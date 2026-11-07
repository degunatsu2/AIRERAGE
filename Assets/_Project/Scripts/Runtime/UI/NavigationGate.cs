using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの選択と決定（uGUI のナビゲーション）を止める仕組み（D-87。画面の切り替えと、試合へ入るときの黒い帯）。止めたい部品が
    /// Hold し、終わったら Release する。止めている部品が 1 つでもあれば止まる（どちらが先に終わっても、残りが止め続ける）。
    /// EventSystem と同じ物に付ける（なければ足す）。
    /// </summary>
    public sealed class NavigationGate : MonoBehaviour
    {
        private readonly HashSet<Object> _holders = new HashSet<Object>();
        private EventSystem _events;

        /// <summary>止めている部品があるか（テスト用）。</summary>
        public bool Held => _holders.Count > 0;

        /// <summary>今の EventSystem の仕組み（なければ null）。</summary>
        public static NavigationGate Current
        {
            get
            {
                EventSystem events = EventSystem.current;
                if (events == null)
                {
                    return null;
                }
                var gate = events.GetComponent<NavigationGate>();
                return gate != null ? gate : events.gameObject.AddComponent<NavigationGate>();
            }
        }

        public void Hold(Object holder)
        {
            _holders.Add(holder);
            Apply();
        }

        public void Release(Object holder)
        {
            _holders.Remove(holder);
            Apply();
        }

        private void Apply()
        {
            _holders.RemoveWhere(h => h == null);
            if (_events == null)
            {
                _events = GetComponent<EventSystem>();
            }
            if (_events != null)
            {
                _events.sendNavigationEvents = _holders.Count == 0;
            }
        }
    }
}
