using System;

namespace Degunatsu.Core.Rollback
{
    /// <summary>
    /// 相手とのティックの進み具合の差を推定し、自分が進みすぎていれば待つフレーム数を勧める
    /// （docs/10_online_plan.md T2-6、docs/04_network.md 3.3。GGPO と同じ考え方）。
    /// 自分から見た差（自分のティック − 届いたメッセージの送り手のティック）は片道の遅れの分だけ大きく見えるが、
    /// 相手から見た差（メッセージで届く）と比べて半分にすると、遅れが打ち消されて本当の差が残る。
    /// どちらも直近の数件の平均を使い、揺らぎをならす。シミュレーションの結果には関係しない（進める間合いだけ）。
    /// </summary>
    public sealed class TimeSync
    {
        // 平均に使う直近のメッセージの数。
        private const int Window = 16;

        private readonly RollbackSettings _settings;
        private readonly int[] _latestRemoteTick;
        private readonly int[][] _localSamples;
        private readonly int[][] _remoteSamples;
        private readonly int[] _sampleCount;
        private int _nextRecommendationTick;

        public TimeSync(int playerCount, int localPlayer, int startTick, RollbackSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            LocalPlayer = localPlayer;
            _latestRemoteTick = new int[playerCount];
            _localSamples = new int[playerCount][];
            _remoteSamples = new int[playerCount][];
            _sampleCount = new int[playerCount];
            for (int p = 0; p < playerCount; p++)
            {
                _latestRemoteTick[p] = startTick;
                _localSamples[p] = new int[Window];
                _remoteSamples[p] = new int[Window];
            }
            _nextRecommendationTick = startTick + settings.TimeSyncIntervalTicks;
        }

        public int LocalPlayer { get; }

        /// <summary>相手に伝える、自分から見た進み（自分のティック − 相手の最新のティック）。</summary>
        public int AdvantageOver(int remote, int localTick)
        {
            return localTick - _latestRemoteTick[remote];
        }

        /// <summary>相手 remote からメッセージが届いたときに呼ぶ。</summary>
        public void OnMessage(int remote, int localTick, int senderTick, int senderAdvantage)
        {
            _latestRemoteTick[remote] = Math.Max(_latestRemoteTick[remote], senderTick);
            int index = _sampleCount[remote] % Window;
            _localSamples[remote][index] = localTick - senderTick;
            _remoteSamples[remote][index] = senderAdvantage;
            _sampleCount[remote]++;
        }

        /// <summary>自分が相手より何ティック先に進んでいるかの推定（相手のうち最大。相手から届いていなければ 0）。</summary>
        public float TicksAhead
        {
            get
            {
                float ahead = 0f;
                for (int p = 0; p < _sampleCount.Length; p++)
                {
                    if (p == LocalPlayer || _sampleCount[p] == 0)
                    {
                        continue;
                    }
                    int n = Math.Min(_sampleCount[p], Window);
                    float local = 0f;
                    float remote = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        local += _localSamples[p][i];
                        remote += _remoteSamples[p][i];
                    }
                    ahead = Math.Max(ahead, (local - remote) / (2f * n));
                }
                return ahead;
            }
        }

        /// <summary>
        /// 待つべきフレーム数を勧める（TimeSyncIntervalTicks ごとに1回だけ。それ以外は 0）。
        /// 進みすぎが TimeSyncThresholdTicks 以上なら、その分（最大 MaxPredictionTicks）を返す。
        /// 呼び出し側は、その数のフレームだけティックを進めずに待つ（送受信は続ける）。
        /// </summary>
        public int RecommendWait(int localTick)
        {
            if (localTick < _nextRecommendationTick)
            {
                return 0;
            }
            _nextRecommendationTick = localTick + _settings.TimeSyncIntervalTicks;
            int ahead = (int)Math.Round(TicksAhead, MidpointRounding.AwayFromZero);
            return ahead >= _settings.TimeSyncThresholdTicks ? Math.Min(ahead, Math.Max(1, _settings.MaxPredictionTicks)) : 0;
        }
    }
}
