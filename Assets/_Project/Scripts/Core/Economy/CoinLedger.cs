using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Simulation;

namespace Airerage.Core.Economy
{
    /// <summary>
    /// コイン台帳（docs/01_game_design.md 3.2、docs/12_match_plan.md T4-3）。コインの増減は、すべてここを通す。
    /// - 取引ごとにイベント ID（CoinEventId）を受け取り、同じティックに同じ ID の取引が来たら拒否する（二重の加算を防ぐ）。
    ///   ID は取引を出す側が、ティック・種類・対象から決める（同じことを2回やろうとすると、同じ ID になる）。
    /// - 量が 0 以下、所持コインが足りない、コインがない・まだ拾えない、などの不正な取引は拒否し、何も変えない。
    /// - 生成（出現、報酬）と破棄（消滅）以外では、コインの総量が変わらない（Totals で調べられる）。
    /// - 状態は、ワールド全体の CoinField と、キャラクターごとの CharacterState.Coins にある（巻き戻しに乗る）。
    ///   ここが持つのは、このティックに受け付けた ID と取引の記録だけ（ティックごとに空にする）。
    /// </summary>
    public sealed class CoinLedger
    {
        private readonly List<CoinEventId> _appliedIds = new List<CoinEventId>();
        private readonly List<CoinEvent> _events = new List<CoinEvent>();

        public CoinLedger(CoinField field)
        {
            Field = field ?? throw new System.ArgumentNullException(nameof(field));
        }

        public CoinField Field { get; }

        /// <summary>今のティック（BeginTick で渡したもの）。</summary>
        public int Tick { get; private set; }

        /// <summary>このティックに行った取引（表示と音用）。</summary>
        public IReadOnlyList<CoinEvent> Events => _events;

        /// <summary>これまでに拒否した取引の数と、最後に拒否した理由（調査用）。</summary>
        public int RejectedCount { get; private set; }
        public string LastRejection { get; private set; } = string.Empty;

        /// <summary>ティックの始まりに呼ぶ。このティックに受け付けた ID と取引の記録を空にする。</summary>
        public void BeginTick(int tick)
        {
            Tick = tick;
            _appliedIds.Clear();
            _events.Clear();
        }

        /// <summary>
        /// 出現（生成）。フィールドにコインを1つ出し、その番号を返す（拒否したら -1）。source は出現地点の番号（なければ -1）。
        /// </summary>
        public int Spawn(CoinEventId id, Vector2 position, int value, int pickableFromTick, int expireTick, int source = -1)
        {
            if (!Accept(id, value))
            {
                return -1;
            }
            int coinId = Field.NextCoinId++;
            Field.Add(new FieldCoin(coinId, position, value, pickableFromTick, expireTick, source));
            Field.Generated += value;
            Record(id, -1, coinId, value, position);
            return coinId;
        }

        /// <summary>拾得（移動）。コイン coinId を、キャラクター character の所持コインにする。</summary>
        public bool Pickup(CoinEventId id, int coinId, List<CharacterState> states, int character)
        {
            int index = Field.IndexOf(coinId);
            if (index < 0)
            {
                return Reject(id, $"コイン #{coinId} がない");
            }
            FieldCoin coin = Field.Coins[index];
            if (Tick < coin.PickableFromTick)
            {
                return Reject(id, $"コイン #{coinId} はティック {coin.PickableFromTick} まで拾えない");
            }
            if (!Accept(id, coin.Value))
            {
                return false;
            }
            Field.RemoveAt(index);
            CharacterState state = states[character];
            state.Coins.Held += coin.Value;
            states[character] = state;
            Record(id, character, coinId, coin.Value, coin.Position);
            return true;
        }

        /// <summary>
        /// ドロップ（移動。kind は HitDrop か KoDrop）。キャラクター character の所持コインから amount 枚を、フィールドのコイン1つにする。
        /// 出したコインの番号を返す（拒否したら -1）。
        /// </summary>
        public int Drop(CoinEventId id, List<CharacterState> states, int character, int amount, Vector2 position, int pickableFromTick, int expireTick)
        {
            if (id.Kind != CoinEventKind.HitDrop && id.Kind != CoinEventKind.KoDrop)
            {
                Reject(id, "ドロップの種類ではない");
                return -1;
            }
            CharacterState state = states[character];
            if (amount > state.Coins.Held)
            {
                Reject(id, $"所持コインが足りない（{state.Coins.Held} 枚に {amount} 枚）");
                return -1;
            }
            if (!Accept(id, amount))
            {
                return -1;
            }
            state.Coins.Held -= amount;
            states[character] = state;
            int coinId = Field.NextCoinId++;
            Field.Add(new FieldCoin(coinId, position, amount, pickableFromTick, expireTick, -1, id.Kind));
            Record(id, character, coinId, amount, position);
            return coinId;
        }

        /// <summary>確定（移動）。キャラクター character の所持コインから amount 枚を確定コインにする。</summary>
        public bool Bank(CoinEventId id, List<CharacterState> states, int character, int amount)
        {
            CharacterState state = states[character];
            if (amount > state.Coins.Held)
            {
                return Reject(id, $"所持コインが足りない（{state.Coins.Held} 枚に {amount} 枚）");
            }
            if (!Accept(id, amount))
            {
                return false;
            }
            state.Coins.Held -= amount;
            state.Coins.Banked += amount;
            states[character] = state;
            Record(id, character, -1, amount, Vector2.Zero);
            return true;
        }

        /// <summary>報酬（生成。kind は DamageReward か KoReward）。キャラクター character の所持コインに amount 枚を加える。</summary>
        public bool Reward(CoinEventId id, List<CharacterState> states, int character, int amount)
        {
            if (id.Kind != CoinEventKind.DamageReward && id.Kind != CoinEventKind.KoReward)
            {
                return Reject(id, "報酬の種類ではない");
            }
            if (!Accept(id, amount))
            {
                return false;
            }
            CharacterState state = states[character];
            state.Coins.Held += amount;
            states[character] = state;
            Field.Generated += amount;
            Record(id, character, -1, amount, Vector2.Zero);
            return true;
        }

        /// <summary>消滅（破棄）。フィールドのコイン coinId をなくす。</summary>
        public bool Expire(CoinEventId id, int coinId)
        {
            int index = Field.IndexOf(coinId);
            if (index < 0)
            {
                return Reject(id, $"コイン #{coinId} がない");
            }
            FieldCoin coin = Field.Coins[index];
            if (!Accept(id, coin.Value))
            {
                return false;
            }
            Field.RemoveAt(index);
            Field.Destroyed += coin.Value;
            Record(id, -1, coinId, coin.Value, coin.Position);
            return true;
        }

        /// <summary>コインの枚数の合計（フィールド、所持、確定、生成、破棄）。</summary>
        public CoinTotals Totals(IReadOnlyList<CharacterState> states)
        {
            long held = 0;
            long banked = 0;
            for (int i = 0; i < states.Count; i++)
            {
                held += states[i].Coins.Held;
                banked += states[i].Coins.Banked;
            }
            return new CoinTotals(Field.FieldTotal, held, banked, Field.Generated, Field.Destroyed);
        }

        // ID とティック、量を調べ、受け付けるなら ID を記録する。
        private bool Accept(CoinEventId id, int amount)
        {
            if (id.Tick != Tick)
            {
                return Reject(id, $"ティックが違う（今は {Tick}）");
            }
            if (amount <= 0)
            {
                return Reject(id, $"量が 0 以下（{amount}）");
            }
            if (_appliedIds.Contains(id))
            {
                return Reject(id, "同じ ID の取引が、このティックにもう行われた");
            }
            _appliedIds.Add(id);
            return true;
        }

        private bool Reject(CoinEventId id, string reason)
        {
            RejectedCount++;
            LastRejection = $"{id}: {reason}";
            return false;
        }

        private void Record(CoinEventId id, int character, int coinId, int amount, Vector2 position)
        {
            _events.Add(new CoinEvent(id, character, coinId, amount, position));
        }
    }

    /// <summary>コインの枚数の合計。生成 − 破棄 = フィールド ＋ 所持 ＋ 確定 が成り立つ（総量の保存）。</summary>
    public readonly struct CoinTotals
    {
        public readonly long Field;
        public readonly long Held;
        public readonly long Banked;
        public readonly long Generated;
        public readonly long Destroyed;

        public CoinTotals(long field, long held, long banked, long generated, long destroyed)
        {
            Field = field;
            Held = held;
            Banked = banked;
            Generated = generated;
            Destroyed = destroyed;
        }

        /// <summary>総量が保存されているか。</summary>
        public bool IsConserved => Field + Held + Banked == Generated - Destroyed;

        public override string ToString()
        {
            return $"field={Field} held={Held} banked={Banked} generated={Generated} destroyed={Destroyed}";
        }
    }
}
