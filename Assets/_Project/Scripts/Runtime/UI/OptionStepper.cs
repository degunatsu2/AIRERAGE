using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// 左右で値を変える項目（オプションの音量など。docs/15 T6-9）。この項目を選んでいるときに左右（矢印・十字キー・スティック）を
    /// 入れると Stepped で -1 ／ +1 を知らせる。上下の移動は項目の Button（Selectable）の移動に任せる（左右の移動先は決めない）。
    /// </summary>
    public sealed class OptionStepper : MonoBehaviour, IMoveHandler
    {
        /// <summary>左右を入れた（-1 が左、+1 が右）。</summary>
        public event Action<int> Stepped;

        /// <summary>左（-1）か右（+1）を入れたことにする（値の左右の矢印のクリック。OptionArrow）。</summary>
        public void Step(int direction)
        {
            Stepped?.Invoke(direction < 0 ? -1 : 1);
        }

        public void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left)
            {
                Stepped?.Invoke(-1);
            }
            else if (eventData.moveDir == MoveDirection.Right)
            {
                Stepped?.Invoke(1);
            }
        }
    }
}
