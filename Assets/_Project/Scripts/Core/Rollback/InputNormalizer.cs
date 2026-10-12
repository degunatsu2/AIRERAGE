using System;
using Degunatsu.Core.Input;

namespace Degunatsu.Core.Rollback
{
    /// <summary>
    /// 送る前の入力の正規化（docs/10_online_plan.md T2-5、docs/04_network.md 3.3）。
    /// 相手の入力は「直前の入力を続ける」と予測するので、入力が変わるたびに予測が外れうる。
    /// シミュレーションにほぼ影響しない細かな変化を消して、予測が外れる回数を減らす。
    /// 自分の入力に、送る前と自分のシミュレーションに使う前の両方で同じものを使う（全員が同じ入力で計算する）。
    /// </summary>
    public static class InputNormalizer
    {
        /// <summary>
        /// input を正規化する。previous は同じプレイヤーの1つ前のティックの、正規化した後の入力。
        /// - 照準の方向は、ワイヤーのボタンを押したティックにしか使わない（WireController の射出）ので、それ以外では消す。
        ///   シミュレーションの結果は変わらない。マウスの照準は毎ティック変わるため、特に効く。
        /// - 移動のスティックは、前のティックからの変化が moveHysteresis 以下なら前の値のままにする（スティックの揺れを無視する）。
        ///   ただし 0（離した）と端（いっぱいに倒した）にはすぐ合わせる。
        /// </summary>
        public static PlayerInput Normalize(PlayerInput input, PlayerInput previous, int moveHysteresis)
        {
            bool wirePressed = input.IsHeld(InputButtons.Wire) && !previous.IsHeld(InputButtons.Wire);
            sbyte aimX = wirePressed ? input.AimX : (sbyte)0;
            sbyte aimY = wirePressed ? input.AimY : (sbyte)0;
            sbyte moveX = Settle(input.MoveX, previous.MoveX, moveHysteresis);
            sbyte moveY = Settle(input.MoveY, previous.MoveY, moveHysteresis);
            return new PlayerInput(moveX, moveY, input.Buttons, aimX, aimY);
        }

        private static sbyte Settle(sbyte value, sbyte previous, int hysteresis)
        {
            if (value == 0 || Math.Abs(value) >= PlayerInput.AxisMax)
            {
                return value;
            }
            return Math.Abs(value - previous) <= hysteresis ? previous : value;
        }
    }
}
