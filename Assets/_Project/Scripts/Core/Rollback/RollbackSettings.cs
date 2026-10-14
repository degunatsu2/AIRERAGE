using System;
using Airerage.Core.Common;

namespace Airerage.Core.Rollback
{
    /// <summary>ロールバックの設定。初期値はすべて【仮】（docs/10_online_plan.md 4章）。</summary>
    [Serializable]
    public sealed class RollbackSettings
    {
        /// <summary>入力遅延（ティック）。自分の入力を、何ティック後のティックに使うか。</summary>
        public int InputDelayTicks = 2;

        /// <summary>予測の上限（ティック）。確定していないティックをこれより先に進めない（入力が届くまで待つ）。</summary>
        public int MaxPredictionTicks = 8;

        /// <summary>移動のスティックの揺れとして無視する変化（量子化した値。最大 127）。InputNormalizer で使う。</summary>
        public int MoveHysteresis = 2;

        /// <summary>時刻の同期で、待つかを判断する間隔（ティック）。TimeSync で使う。</summary>
        public int TimeSyncIntervalTicks = 30;

        /// <summary>相手よりこのティック数以上進んでいたら待つ。TimeSync で使う。</summary>
        public int TimeSyncThresholdTicks = 1;

        /// <summary>不正な値があれば InvalidTuningException を投げる。</summary>
        public void Validate()
        {
            var v = new TuningValidator(nameof(RollbackSettings));
            v.NonNegative(nameof(InputDelayTicks), InputDelayTicks);
            v.NonNegative(nameof(MaxPredictionTicks), MaxPredictionTicks);
            v.NonNegative(nameof(MoveHysteresis), MoveHysteresis);
            v.Positive(nameof(TimeSyncIntervalTicks), TimeSyncIntervalTicks);
            v.Positive(nameof(TimeSyncThresholdTicks), TimeSyncThresholdTicks);
            if (InputDelayTicks + MaxPredictionTicks < 1)
            {
                // どちらも 0 だと、全員が互いの入力を待ち続けて進めない。
                v.Fail($"{nameof(InputDelayTicks)} と {nameof(MaxPredictionTicks)} の少なくとも一方は 1 以上が必要です");
            }
            v.ThrowIfInvalid();
        }
    }
}
