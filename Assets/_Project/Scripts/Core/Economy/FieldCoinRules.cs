using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;

namespace Degunatsu.Core.Economy
{
    /// <summary>
    /// フィールドのコインの決まり（docs/12_match_plan.md T4-4）。SimWorld が、全員を進めて命中と撃破を決めた後に呼ぶ。
    /// 増減はすべて CoinLedger を通す。
    /// - 出現: 試合中に、試合が始まったティックと、その後 MatchRules.CoinSpawnIntervalTicks ごとに、コインが残っていない
    ///   出現地点にだけ出す（同じ地点のコインは1つまで）。出現地点のコインは消えない。
    /// - 拾得: キャラクターの箱を MatchRules.CoinPickupRadius だけ広げた中にコインの中心が入ったら拾う。何人も触れていたら、
    ///   いちばん近い人（同じなら番号の小さい人）が拾う。復帰を待っている人は拾わない。拾えるティックより前は拾えない。
    /// - 消滅: 寿命（ExpireTick）のティックで消える（ドロップのコイン。T4-6）。
    /// コインは番号の順、キャラクターは番号の順に調べる（結果が順番で変わらない）。
    /// </summary>
    public static class FieldCoinRules
    {
        private const int Never = int.MaxValue;

        /// <param name="tick">このティック（進めた後のティック。取引のイベント ID に使う）。</param>
        /// <param name="playing">試合中か（出現は試合中だけ）。</param>
        public static void Step(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs,
            IReadOnlyList<CoinSpawnPoint> spawns, MatchRules rules, int tick, bool playing)
        {
            if (playing)
            {
                Spawn(ledger, spawns, rules, tick);
            }
            Pickup(ledger, states, configs, rules, tick);
            Expire(ledger, tick);
        }

        private static void Spawn(CoinLedger ledger, IReadOnlyList<CoinSpawnPoint> spawns, MatchRules rules, int tick)
        {
            // 試合が始まってから何ティック目のステップか（始まった最初のステップが 0）。
            int elapsed = tick - 1 - rules.CountdownTicks;
            if (spawns == null || elapsed < 0 || elapsed % rules.CoinSpawnIntervalTicks != 0)
            {
                return;
            }
            for (int i = 0; i < spawns.Count; i++)
            {
                if (HasCoinFrom(ledger.Field, i))
                {
                    continue;
                }
                CoinSpawnPoint point = spawns[i];
                ledger.Spawn(new CoinEventId(tick, CoinEventKind.Spawn, i), point.Position, point.Value, tick, Never, i);
            }
        }

        private static bool HasCoinFrom(CoinField field, int source)
        {
            foreach (FieldCoin coin in field.Coins)
            {
                if (coin.Source == source)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Pickup(CoinLedger ledger, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, MatchRules rules, int tick)
        {
            IReadOnlyList<FieldCoin> coins = ledger.Field.Coins;
            for (int c = 0; c < coins.Count; c++)
            {
                FieldCoin coin = coins[c];
                if (tick < coin.PickableFromTick)
                {
                    continue;
                }
                int picker = ClosestToucher(coin.Position, states, configs, rules.CoinPickupRadius);
                if (picker >= 0 && ledger.Pickup(new CoinEventId(tick, CoinEventKind.Pickup, coin.Id), coin.Id, states, picker))
                {
                    // 拾ったコインはなくなったので、同じ位置から調べ直す。
                    c--;
                }
            }
        }

        private static int ClosestToucher(Vector2 coin, List<CharacterState> states, IReadOnlyList<CharacterConfig> configs, float radius)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < states.Count; i++)
            {
                CharacterState state = states[i];
                if (state.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                Vector3 half = configs[i].Movement.HalfExtents;
                float dx = coin.X - state.Position.X;
                float dy = coin.Y - state.Position.Y;
                if (MathF.Abs(dx) > half.X + radius || MathF.Abs(dy) > half.Y + radius)
                {
                    continue;
                }
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static void Expire(CoinLedger ledger, int tick)
        {
            IReadOnlyList<FieldCoin> coins = ledger.Field.Coins;
            for (int c = 0; c < coins.Count; c++)
            {
                FieldCoin coin = coins[c];
                if (coin.ExpireTick <= tick && ledger.Expire(new CoinEventId(tick, CoinEventKind.Expire, coin.Id), coin.Id))
                {
                    c--;
                }
            }
        }
    }
}
