using System;
using System.Numerics;

namespace Airerage.Core.Input
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Wire = 1 << 1,
        Attack = 1 << 2,

        /// <summary>ガード（Shift ／ ゲームパッドの B。D-35）。</summary>
        Guard = 1 << 3,

        /// <summary>特殊攻撃 1（Q ／ ゲームパッドの Y【仮】。D-53、D-56）。</summary>
        Special1 = 1 << 4,

        /// <summary>特殊攻撃 2（E ／ ゲームパッドの LT【仮】）。</summary>
        Special2 = 1 << 5,

        /// <summary>必殺技（R ／ ゲームパッドの右スティックの押し込み【仮】）。</summary>
        Super = 1 << 6,

        /// <summary>アイテムバッグの 1〜3 枠目（Z・X・C ／ ゲームパッドの Y・LT・L3【仮】。拾う・使う。docs/20_item_plan.md）。</summary>
        Item1 = 1 << 7,
        Item2 = 1 << 8,
        Item3 = 1 << 9,

        /// <summary>アイテムを捨てる（マウスのホイールの押し込み ／ ゲームパッドの SELECT【仮】。枠のキーを押しながら押す）。</summary>
        ItemDrop = 1 << 10,

        /// <summary>
        /// 試合から抜けた（ネットワーク対戦で切れた人。人が押すボタンではない）。ホストが決めたティックから、全員がその人の入力を
        /// PlayerInput.Departed として同じように使う（docs/15 T6-10）。
        /// </summary>
        Leave = 1 << 15,
    }

    /// <summary>
    /// 1ティック分のプレイヤー入力。ネットワークで送れるよう、スティックと照準は整数に量子化する。
    /// Input System を直接読むのは Runtime 側のアダプターだけで、シミュレーションはこの値だけを見る。
    /// </summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public const int AxisMax = 127;

        /// <summary>試合から抜けた人の入力（InputButtons.Leave だけ）。</summary>
        public static readonly PlayerInput Departed = new PlayerInput(0, 0, InputButtons.Leave);

        public readonly sbyte MoveX;
        public readonly sbyte MoveY;
        public readonly InputButtons Buttons;

        /// <summary>
        /// 移動と別に指定する照準の方向（マウスでの照準。D-08）。指定がなければ (0, 0) で、移動のスティックで照準する。
        /// </summary>
        public readonly sbyte AimX;
        public readonly sbyte AimY;

        /// <summary>
        /// 照準の届く先（ワイヤーの射程に対する割合を 255 段階に。0 は指定なしで射程いっぱい）。PC はマウスカーソルまでの距離、
        /// ゲームパッドは右スティックの倒し具合（2026-11-12 のユーザーの要望。フックはそこまで飛び、途中で何にも当たらなければ外れる）。
        /// </summary>
        public readonly byte AimReach;

        /// <summary>届く先の段階の数（AimReach の最大）。</summary>
        public const int ReachMax = 255;

        public PlayerInput(sbyte moveX, sbyte moveY, InputButtons buttons)
            : this(moveX, moveY, buttons, 0, 0, 0)
        {
        }

        public PlayerInput(sbyte moveX, sbyte moveY, InputButtons buttons, sbyte aimX, sbyte aimY)
            : this(moveX, moveY, buttons, aimX, aimY, 0)
        {
        }

        public PlayerInput(sbyte moveX, sbyte moveY, InputButtons buttons, sbyte aimX, sbyte aimY, byte aimReach)
        {
            MoveX = moveX;
            MoveY = moveY;
            Buttons = buttons;
            AimX = aimX;
            AimY = aimY;
            AimReach = aimReach;
        }

        /// <summary>-1〜1 のスティック値を量子化して作る（照準の指定なし）。</summary>
        public static PlayerInput FromAxes(float x, float y, InputButtons buttons)
        {
            return new PlayerInput(Quantize(x), Quantize(y), buttons);
        }

        /// <summary>照準の方向を指定した入力を作る。方向は正規化して量子化する。長さが 0 なら指定なし。</summary>
        public PlayerInput WithAim(Vector2 direction)
        {
            float length = direction.Length();
            if (length < 1e-6f)
            {
                return new PlayerInput(MoveX, MoveY, Buttons);
            }
            Vector2 unit = direction / length;
            return new PlayerInput(MoveX, MoveY, Buttons, Quantize(unit.X), Quantize(unit.Y));
        }

        /// <summary>
        /// 照準の方向と届く先（射程に対する割合 0〜1。PC はマウスカーソルまでの距離、ゲームパッドは右スティックの倒し具合）を指定した
        /// 入力を作る。割合はいちばん小さくても 1 段階（0 は「指定なし」なので使わない）。
        /// </summary>
        public PlayerInput WithAim(Vector2 direction, float reach)
        {
            PlayerInput aimed = WithAim(direction);
            if (!aimed.HasAim)
            {
                return aimed;
            }
            int steps = (int)MathF.Round(Math.Clamp(reach, 0f, 1f) * ReachMax);
            return new PlayerInput(MoveX, MoveY, Buttons, aimed.AimX, aimed.AimY, (byte)Math.Clamp(steps, 1, ReachMax));
        }

        /// <summary>照準の届く先（ワイヤーの射程に対する割合。指定がなければ 1）。</summary>
        public float Reach => AimReach == 0 ? 1f : AimReach / (float)ReachMax;

        /// <summary>照準の方向が指定されているか。</summary>
        public bool HasAim => AimX != 0 || AimY != 0;

        /// <summary>指定された照準の方向（長さはおよそ 1）。指定がなければ (0, 0)。</summary>
        public Vector2 Aim => new Vector2(AimX / (float)AxisMax, AimY / (float)AxisMax);

        /// <summary>スティックの値（長さは最大 1）。</summary>
        public Vector2 Move
        {
            get
            {
                var move = new Vector2(MoveX / (float)AxisMax, MoveY / (float)AxisMax);
                float length = move.Length();
                return length > 1f ? move / length : move;
            }
        }

        public bool IsHeld(InputButtons button)
        {
            return (Buttons & button) == button;
        }

        public bool Equals(PlayerInput other)
        {
            return MoveX == other.MoveX && MoveY == other.MoveY && Buttons == other.Buttons
                && AimX == other.AimX && AimY == other.AimY && AimReach == other.AimReach;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerInput other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MoveX, MoveY, Buttons, AimX, AimY, AimReach);
        }

        public override string ToString()
        {
            if (!HasAim)
            {
                return $"({MoveX}, {MoveY}) {Buttons}";
            }
            return AimReach == 0 ? $"({MoveX}, {MoveY}) {Buttons} aim=({AimX}, {AimY})" : $"({MoveX}, {MoveY}) {Buttons} aim=({AimX}, {AimY}) reach={AimReach}";
        }

        private static sbyte Quantize(float value)
        {
            float clamped = Math.Clamp(value, -1f, 1f);
            return (sbyte)MathF.Round(clamped * AxisMax);
        }
    }

    /// <summary>
    /// 前のティックの入力と比べて「押した瞬間」「離した瞬間」を求めたもの。
    /// </summary>
    public readonly struct InputFrame
    {
        public readonly Vector2 Move;

        /// <summary>ワイヤーの照準に使う方向。照準の指定があればそれ、なければ移動のスティック。</summary>
        public readonly Vector2 Aim;

        /// <summary>照準の届く先（ワイヤーの射程に対する割合。照準の指定がないか、届く先の指定がなければ 1）。</summary>
        public readonly float Reach;

        public readonly InputButtons Held;
        public readonly InputButtons Pressed;
        public readonly InputButtons Released;

        public InputFrame(PlayerInput current, InputButtons previous)
        {
            Move = current.Move;
            Aim = current.HasAim ? current.Aim : Move;
            Reach = current.HasAim ? current.Reach : 1f;
            Held = current.Buttons;
            Pressed = current.Buttons & ~previous;
            Released = previous & ~current.Buttons;
        }

        public bool IsHeld(InputButtons button)
        {
            return (Held & button) == button;
        }

        public bool WasPressed(InputButtons button)
        {
            return (Pressed & button) == button;
        }
    }
}
