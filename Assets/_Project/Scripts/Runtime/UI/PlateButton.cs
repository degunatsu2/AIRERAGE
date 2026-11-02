using System;
using Airerage.Runtime.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 選べる札（uGUI の Button）。見た目は持ち主が決める（札の色を持ち主が毎フレーム決める画面で使う。待機所のプレイヤーの札。
    /// docs/19 T9-5）。MenuOption と同じく、マウスを乗せると選び、選んだときにカーソルの音、決定で決定の音、戻る操作は Cancelled。
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class PlateButton : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, ICancelHandler
    {
        private Button _button;
        private UiSounds _sounds;

        /// <summary>決定した（クリック、Enter、ゲームパッドの A）。</summary>
        public Button.ButtonClickedEvent Clicked => Button.onClick;

        /// <summary>この札を選んでいるときに、戻る操作をした。</summary>
        public event Action Cancelled;

        /// <summary>今選んでいるか（持ち主が見た目に使う）。</summary>
        public bool IsSelected { get; private set; }

        public Button Button => _button != null ? _button : _button = GetComponent<Button>();

        private UiSounds Sounds => _sounds != null ? _sounds : _sounds = UiSounds.Find();

        private void Awake()
        {
            Button.onClick.AddListener(() => Sounds.Play(SoundId.UiConfirm));
        }

        private void OnDisable()
        {
            IsSelected = false;
        }

        public void OnSelect(BaseEventData eventData)
        {
            IsSelected = true;
            Sounds.Play(SoundId.UiMove);
        }

        public void OnDeselect(BaseEventData eventData)
        {
            IsSelected = false;
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
    }
}
