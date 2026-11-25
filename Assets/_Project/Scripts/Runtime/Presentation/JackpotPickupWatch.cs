using System.Collections.Generic;
using Airerage.Core.Economy;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 100 枚のコインを取った人を見つける（表示だけ。シミュレーションには影響しない）。100 枚のコインが前のフレームにあった（または
    /// この間に出た）のに今はなく、確定＋所持が 100 枚以上増えた人を、取った人とみなす。右上の札の虹色（ScoreboardHud）と、
    /// 取った人の JACKPOT の演出（2026-11-25 の要望。試合を進める部品が CharacterView.PlayJackpotPickup を呼ぶ）が同じ見つけ方を使う。
    /// 見る側ごとに 1 つ持つ（読むだけなので、いくつあってもよい）。
    /// </summary>
    public sealed class JackpotPickupWatch
    {
        private readonly List<int> _pickers = new List<int>();
        private int[] _totals = new int[0];
        private bool _wasPresent;
        private int _lastTick = -1;

        /// <summary>最後に見たフレームで 100 枚のコインを取った人の番号（ワールドの番号）。</summary>
        public IReadOnlyList<int> Pickers => _pickers;

        /// <summary>今の様子を見て、このフレームで 100 枚のコインを取った人を Pickers に入れる（いなければ空）。</summary>
        public IReadOnlyList<int> Observe(ISimulationFeed feed)
        {
            _pickers.Clear();
            if (feed == null)
            {
                return _pickers;
            }
            int count = feed.CharacterCount;
            int tick = feed.CurrentTick;
            MatchRules rules = feed.Rules;
            int spawn = rules != null ? FieldCoinRules.JackpotSpawnTick(rules) : -1;
            bool appeared = _wasPresent || (spawn >= 0 && _lastTick >= 0 && _lastTick <= spawn && tick > spawn);
            _lastTick = tick;
            bool present = false;
            if (feed.Coins != null)
            {
                foreach (FieldCoin coin in feed.Coins.Coins)
                {
                    present |= CoinView.LookOf(coin.Value) == CoinLook.Rainbow;
                }
            }
            bool resized = _totals.Length != count;
            if (resized)
            {
                _totals = new int[count];
            }
            for (int i = 0; i < count; i++)
            {
                CharacterState state = feed.GetState(i);
                int total = state.Coins.Held + state.Coins.Banked;
                if (!resized && appeared && !present && total - _totals[i] >= CoinView.RainbowValue)
                {
                    _pickers.Add(i);
                }
                _totals[i] = total;
            }
            _wasPresent = present;
            return _pickers;
        }
    }
}
