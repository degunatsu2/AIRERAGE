using System;
using Airerage.Core.Movement;
using Airerage.Core.Simulation;
using Airerage.Core.Wire;

namespace Airerage.Core.Skills
{
    /// <summary>
    /// 行動の速さの強化（C-3。STORM RUNNER のウインドクロークとキャッスルインジエアー。docs/14 1.2）。
    /// 強化の間は、そのキャラクターの時間が s 倍（1 + 割合）で進むように動かす:
    /// - 移動とワイヤー: 速さを s 倍、加速度と重力を s² 倍にした調整値で動かす（同じ軌道を s 倍の速さでたどる。ジャンプの高さ、
    ///   ワイヤーの射程と長さは変わらない）。SimWorld が Apply で作った調整値を CharacterSimulator に渡す。
    /// - 攻撃のフレーム: ActionController が s 倍の速さで進める（ActionState.TickRemainder）。攻撃の動きの速さも s 倍にする。
    /// ヒットストップ、のけぞり、ガード、クールタイム、強化の残りは速くしない（ユーザーの回答、2026-10-16）。
    /// 割合は、効いている強化の割合を足す（ウインドクローク 20% とキャッスルインジエアー 50% なら 70%。D-64【提案】の「上乗せは足す」）。
    /// 攻撃のフレームを 1 ティックに 2 つまでしか進めないので、合計は MaxPercent まで。
    /// キャッスルインジエアーの間は、ワイヤーが弾く物にもつながり、最大射程まで何もなければ空中につながる。
    /// </summary>
    public static class SpeedBoosts
    {
        /// <summary>速さの強化の割合の上限（%）。</summary>
        public const int MaxPercent = 100;

        /// <summary>state に今効いている速さの強化の割合（%。0 なら強化なし）。</summary>
        public static int PercentOf(in CharacterState state, CharacterConfig config)
        {
            int percent = 0;
            if (state.Combat.SpeedBoostTicksLeft > 0)
            {
                percent += config.Skills.SpeedBoost.SpeedPercent;
            }
            if (state.Combat.SkyBoostTicksLeft > 0)
            {
                percent += config.Skills.SkyBoost.SpeedPercent;
            }
            return Math.Min(percent, MaxPercent);
        }

        /// <summary>速さの倍率 s（1 + 割合 ÷ 100）。</summary>
        public static float ScaleOf(in CharacterState state, CharacterConfig config)
        {
            return 1f + PercentOf(state, config) / 100f;
        }

        /// <summary>ワイヤーが強くなっているか（キャッスルインジエアーの間）。</summary>
        public static bool HasStrongWire(in CharacterState state)
        {
            return state.Combat.SkyBoostTicksLeft > 0;
        }

        /// <summary>
        /// state の強化を当てた調整値（強化がなければ config そのもの）。戦闘と技の値は config と同じものを使う。
        /// 強化の間だけ、ティックごとに新しく作る（調整値をその場で変えても、すぐに反映されるように）。
        /// </summary>
        public static CharacterConfig EffectiveConfig(in CharacterState state, CharacterConfig config)
        {
            int percent = PercentOf(state, config);
            bool strongWire = HasStrongWire(state);
            return percent == 0 && !strongWire ? config : Apply(config, percent, strongWire);
        }

        /// <summary>割合 percent（%）の速さと、strongWire ならワイヤーの強化を当てた調整値。</summary>
        public static CharacterConfig Apply(CharacterConfig config, int percent, bool strongWire)
        {
            float s = 1f + Math.Min(Math.Max(percent, 0), MaxPercent) / 100f;
            float s2 = s * s;

            MovementTuning movement = config.Movement.Clone();
            movement.RunSpeed *= s;
            movement.GroundAcceleration *= s2;
            movement.AirAcceleration *= s2;
            movement.AirDrag *= s;
            movement.Gravity *= s2;
            movement.MaxFallSpeed *= s;
            movement.JumpSpeed *= s;

            WireTuning wire = config.Wire.Clone();
            wire.FireSpeed *= s;
            wire.MaxAttachedSpeed *= s;
            wire.ExitSpeedCap *= s;
            wire.Inertial.StartPullSpeed *= s;
            wire.Inertial.ReelSpeed *= s;
            wire.Inertial.ReelOutSpeed *= s;
            wire.Inertial.HoldSwingAcceleration *= s2;
            wire.Inertial.SwingAcceleration *= s2;
            wire.Inertial.Damping *= s;
            wire.Responsive.PullSpeed *= s;
            wire.Responsive.PullAcceleration *= s2;
            wire.Responsive.SteerSpeed *= s;
            if (strongWire)
            {
                wire.AttachAnySurface = true;
                wire.AttachInAir = true;
            }

            return new CharacterConfig
            {
                Movement = movement,
                Wire = wire,
                Combat = config.Combat,
                Skills = config.Skills,
            };
        }
    }
}
