using System;
using System.Collections.Generic;

namespace Airerage.Core.Economy
{
    /// <summary>
    /// ワールド全体のコインの状態（docs/12_match_plan.md T4-3）: フィールドのコイン（番号の順）、次のコインの番号、
    /// これまでに生成・破棄した枚数（総量の検査に使う）、出現地点ごとの「次に出せるティック」（拾われたティックから数える。T4-4）。
    /// 巻き戻しのため、スナップショットに写し、チェックサムに含める。
    /// 増減は CoinLedger だけが行う。
    /// </summary>
    public sealed class CoinField
    {
        private readonly List<FieldCoin> _coins = new List<FieldCoin>();
        private readonly List<int> _spawnReadyTicks = new List<int>();

        /// <summary>フィールドのコイン（番号の小さい順）。</summary>
        public IReadOnlyList<FieldCoin> Coins => _coins;

        /// <summary>次に出すコインの番号。</summary>
        public int NextCoinId { get; internal set; } = 1;

        /// <summary>これまでに生成した枚数（出現、ダメージ報酬、撃破報酬）。</summary>
        public long Generated { get; internal set; }

        /// <summary>これまでに破棄した枚数（消滅）。</summary>
        public long Destroyed { get; internal set; }

        /// <summary>出現地点ごとの、次にコインを出せるティック（拾われたティック ＋ 間隔。まだ拾われていなければ 0）。</summary>
        public IReadOnlyList<int> SpawnReadyTicks => _spawnReadyTicks;

        /// <summary>出現地点 point から、次にコインを出せるティック。</summary>
        public int SpawnReadyTick(int point)
        {
            return point < _spawnReadyTicks.Count ? _spawnReadyTicks[point] : 0;
        }

        /// <summary>フィールドのコインの枚数の合計。</summary>
        public long FieldTotal
        {
            get
            {
                long total = 0;
                foreach (FieldCoin coin in _coins)
                {
                    total += coin.Value;
                }
                return total;
            }
        }

        /// <summary>番号 id のコインの位置（なければ -1）。</summary>
        public int IndexOf(int id)
        {
            for (int i = 0; i < _coins.Count; i++)
            {
                if (_coins[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>other と同じ状態にする（スナップショットの保存と復元）。</summary>
        public void CopyFrom(CoinField other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            _coins.Clear();
            _coins.AddRange(other._coins);
            _spawnReadyTicks.Clear();
            _spawnReadyTicks.AddRange(other._spawnReadyTicks);
            NextCoinId = other.NextCoinId;
            Generated = other.Generated;
            Destroyed = other.Destroyed;
        }

        /// <summary>同じ状態か（テスト用）。</summary>
        public bool SameAs(CoinField other)
        {
            if (other == null || _coins.Count != other._coins.Count || NextCoinId != other.NextCoinId
                || Generated != other.Generated || Destroyed != other.Destroyed || _spawnReadyTicks.Count != other._spawnReadyTicks.Count)
            {
                return false;
            }
            for (int i = 0; i < _spawnReadyTicks.Count; i++)
            {
                if (_spawnReadyTicks[i] != other._spawnReadyTicks[i])
                {
                    return false;
                }
            }
            for (int i = 0; i < _coins.Count; i++)
            {
                if (!_coins[i].Equals(other._coins[i]))
                {
                    return false;
                }
            }
            return true;
        }

        internal void Add(FieldCoin coin)
        {
            _coins.Add(coin);
        }

        internal void RemoveAt(int index)
        {
            _coins.RemoveAt(index);
        }

        internal void SetSpawnReady(int point, int tick)
        {
            while (_spawnReadyTicks.Count <= point)
            {
                _spawnReadyTicks.Add(0);
            }
            _spawnReadyTicks[point] = tick;
        }
    }
}
