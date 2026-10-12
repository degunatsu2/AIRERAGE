using System.Collections.Generic;
using Degunatsu.Core.Wire;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// デバッグ機能でのワイヤーのタイプの名前と、切り替えの順番。
    /// </summary>
    public static class WireTypeLabels
    {
        private static readonly WireModelType[] OrderArray =
        {
            WireModelType.Inertial, WireModelType.Responsive,
        };

        /// <summary>切り替えと表示の順番（A → B → A）。</summary>
        public static IReadOnlyList<WireModelType> Order => OrderArray;

        public static string Short(WireModelType type)
        {
            switch (type)
            {
                case WireModelType.Inertial:
                    return "A";
                case WireModelType.Responsive:
                    return "B";
                default:
                    return type.ToString();
            }
        }

        public static string Long(WireModelType type)
        {
            switch (type)
            {
                case WireModelType.Inertial:
                    return "A (Inertial)";
                case WireModelType.Responsive:
                    return "B (Responsive)";
                default:
                    return type.ToString();
            }
        }

        /// <summary>切り替えの順番で次のタイプ。</summary>
        public static WireModelType Next(WireModelType type)
        {
            int index = System.Array.IndexOf(OrderArray, type);
            return OrderArray[(index + 1) % OrderArray.Length];
        }
    }
}
