using System;
using System.Numerics;

namespace Degunatsu.Core.Input
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Wire = 1 << 1,
    }

    /// <summary>
    /// 1ティック分のプレイヤー入力。ネットワークで送れるよう、スティックは整数に量子化する。
    /// Input System を直接読むのは Runtime 側のアダプターだけで、シミュレーションはこの値だけを見る。
    /// </summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public const int AxisMax = 127;

        public readonly sbyte MoveX;
        public readonly sbyte MoveY;
        public readonly InputButtons Buttons;

        public PlayerInput(sbyte moveX, sbyte moveY, InputButtons buttons)
        {
            MoveX = moveX;
            MoveY = moveY;
            Buttons = buttons;
        }

        /// <summary>-1〜1 のスティック値を量子化して作る。</summary>
        public static PlayerInput FromAxes(float x, float y, InputButtons buttons)
        {
            return new PlayerInput(Quantize(x), Quantize(y), buttons);
        }

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
            return MoveX == other.MoveX && MoveY == other.MoveY && Buttons == other.Buttons;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerInput other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(MoveX, MoveY, Buttons);
        }

        public override string ToString()
        {
            return $"({MoveX}, {MoveY}) {Buttons}";
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
        public readonly InputButtons Held;
        public readonly InputButtons Pressed;
        public readonly InputButtons Released;

        public InputFrame(PlayerInput current, InputButtons previous)
        {
            Move = current.Move;
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
