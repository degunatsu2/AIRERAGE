using System;
using Airerage.Core.Combat;
using Airerage.Core.Input;

namespace Airerage.Core.Rollback
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
        /// - 照準の方向は、ワイヤーのボタンを押したティック（WireController の射出）と、技のボタン（Q・E・R）を押している間
        ///   （ActionController が技の照準 CombatState.SkillAim を覚える）にしか使わないので、それ以外では消す。keepWireAim なら、
        ///   ワイヤーのボタンを押している間も残す（SOLDERER のインバータの間の鞭の向き。2026-10-21。呼ぶ側が自分の状態から決める。
        ///   決めた入力を全員が使うので、結果はずれない）。
        ///   シミュレーションの結果は変わらない。マウスの照準は毎ティック変わるため、特に効く。
        /// - 移動のスティックは、前のティックからの変化が moveHysteresis 以下なら前の値のままにする（スティックの揺れを無視する）。
        ///   ただし 0（離した）と端（いっぱいに倒した）にはすぐ合わせる。
        /// </summary>
        public static PlayerInput Normalize(PlayerInput input, PlayerInput previous, int moveHysteresis, bool keepWireAim = false)
        {
            bool wirePressed = input.IsHeld(InputButtons.Wire) && !previous.IsHeld(InputButtons.Wire);
            bool keepAim = wirePressed || (keepWireAim && input.IsHeld(InputButtons.Wire)) || (input.Buttons & ActionController.SkillButtons) != 0;
            sbyte aimX = keepAim ? input.AimX : (sbyte)0;
            sbyte aimY = keepAim ? input.AimY : (sbyte)0;
            sbyte moveX = Settle(input.MoveX, previous.MoveX, moveHysteresis);
            sbyte moveY = Settle(input.MoveY, previous.MoveY, moveHysteresis);
            // 抜けた印（InputButtons.Leave）は自分の入力には入れない（ホストが決めて全員へ送るもの）。
            return new PlayerInput(moveX, moveY, input.Buttons & ~InputButtons.Leave, aimX, aimY);
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
