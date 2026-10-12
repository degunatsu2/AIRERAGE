using System.Collections.Generic;
using Degunatsu.Core.Wire;

namespace Degunatsu.Runtime.DebugTools
{
    /// <summary>
    /// デバッグ機能でのワイヤーのタイプの名前と、切り替えの順番。
    /// A は円運動の試行版（D-11、Orbit）。従来の物理・慣性モデル（Inertial）は比較のため「A-old」として残している。
    /// </summary>
    public static class WireTypeLabels
    {
        private static readonly WireModelType[] OrderArray =
        {
            WireModelType.Orbit, WireModelType.Responsive, WireModelType.Inertial,
        };

        /// <summary>切り替えと表示の順番（A → B → A-old → A）。</summary>
        public static IReadOnlyList<WireModelType> Order => OrderArray;

        public static string Short(WireModelType type)
        {
            switch (type)
            {
                case WireModelType.Orbit:
                    return "A";
                case WireModelType.Responsive:
                    return "B";
                case WireModelType.Inertial:
                    return "A-old";
                default:
                    return type.ToString();
            }
        }

        public static string Long(WireModelType type)
        {
            switch (type)
            {
                case WireModelType.Orbit:
                    return "A (Orbit, trial)";
                case WireModelType.Responsive:
                    return "B (Responsive)";
                case WireModelType.Inertial:
                    return "A-old (Inertial)";
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
