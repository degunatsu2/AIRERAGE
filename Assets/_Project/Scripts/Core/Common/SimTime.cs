using System;

namespace Airerage.Core.Common
{
    /// <summary>
    /// シミュレーションの時間の基準。固定ティック（D-31: 60Hz）で進める。
    /// フレームデータやタイマーはティック数で扱い、Unity の Time には依存しない。
    /// </summary>
    public static class SimTime
    {
        public const int TicksPerSecond = 60;
        public const float DeltaTime = 1f / TicksPerSecond;

        public static int SecondsToTicks(float seconds)
        {
            return (int)MathF.Round(seconds * TicksPerSecond);
        }

        public static float TicksToSeconds(int ticks)
        {
            return ticks * DeltaTime;
        }
    }
}
