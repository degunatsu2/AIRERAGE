using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Economy;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Economy
{
    /// <summary>コイン台帳（docs/12_match_plan.md T4-3）: 取引、二重の加算の拒否、不正な取引の拒否、総量の保存、巻き戻し。</summary>
    public class CoinLedgerTests
    {
        private const int Never = int.MaxValue;
        private static readonly Vector2 Here = new Vector2(1f, 5f);

        private CoinLedger _ledger;
        private List<CharacterState> _states;

        [SetUp]
        public void SetUp()
        {
            _ledger = new CoinLedger(new CoinField());
            _ledger.BeginTick(10);
            _states = new List<CharacterState> { default, default };
        }

        private static CoinEventId Id(int tick, CoinEventKind kind, int subject, int sequence = 0)
        {
            return new CoinEventId(tick, kind, subject, sequence);
        }

        private void AssertConserved()
        {
            CoinTotals totals = _ledger.Totals(_states);
            Assert.That(totals.IsConserved, Is.True, $"総量が保存されていない: {totals}");
        }

        [Test]
        public void Spawn_PutsACoinOnTheField()
        {
            int coin = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 0), Here, 5, 10, Never);

            Assert.That(coin, Is.EqualTo(1));
            Assert.That(_ledger.Field.Coins.Count, Is.EqualTo(1));
            Assert.That(_ledger.Field.Coins[0].Value, Is.EqualTo(5));
            Assert.That(_ledger.Field.Generated, Is.EqualTo(5));
            Assert.That(_ledger.Events.Count, Is.EqualTo(1));
            AssertConserved();
        }

        [Test]
        public void Pickup_MovesTheCoin_ToTheHeldCoins()
        {
            int coin = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 0), Here, 5, 10, Never);

            Assert.That(_ledger.Pickup(Id(10, CoinEventKind.Pickup, coin), coin, _states, 1), Is.True);
            Assert.That(_states[1].Coins.Held, Is.EqualTo(5));
            Assert.That(_ledger.Field.Coins, Is.Empty);
            AssertConserved();
        }

        [Test]
        public void TheSameCoin_CannotBePickedUpTwice()
        {
            // 2人が同じティックに同じコインに触れても、1人だけが拾う（二重の加算を防ぐ）。
            int coin = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 0), Here, 5, 10, Never);

            Assert.That(_ledger.Pickup(Id(10, CoinEventKind.Pickup, coin), coin, _states, 0), Is.True);
            Assert.That(_ledger.Pickup(Id(10, CoinEventKind.Pickup, coin), coin, _states, 1), Is.False);
            Assert.That(_states[0].Coins.Held + _states[1].Coins.Held, Is.EqualTo(5));
            Assert.That(_ledger.RejectedCount, Is.EqualTo(1));
            AssertConserved();
        }

        [Test]
        public void TheSameEventId_IsAppliedOnlyOnce_InATick()
        {
            CoinEventId reward = Id(10, CoinEventKind.DamageReward, 1, 3);
            Assert.That(_ledger.Reward(reward, _states, 0, 4), Is.True);
            Assert.That(_ledger.Reward(reward, _states, 0, 4), Is.False, "同じ ID の報酬を2回受け付けた");
            Assert.That(_states[0].Coins.Held, Is.EqualTo(4));

            _ledger.BeginTick(11);
            Assert.That(_ledger.Reward(Id(11, CoinEventKind.DamageReward, 1, 3), _states, 0, 4), Is.True, "次のティックは別の取引");
            Assert.That(_states[0].Coins.Held, Is.EqualTo(8));
            AssertConserved();
        }

        [Test]
        public void ACoinDroppedRecently_CannotBePickedUpYet()
        {
            int coin = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 0), Here, 5, 40, Never);

            Assert.That(_ledger.Pickup(Id(10, CoinEventKind.Pickup, coin), coin, _states, 0), Is.False);
            _ledger.BeginTick(40);
            Assert.That(_ledger.Pickup(Id(40, CoinEventKind.Pickup, coin), coin, _states, 0), Is.True);
        }

        [Test]
        public void Drop_MovesHeldCoins_ToTheField()
        {
            _ledger.Reward(Id(10, CoinEventKind.KoReward, 0), _states, 0, 10);

            int coin = _ledger.Drop(Id(10, CoinEventKind.HitDrop, 0), _states, 0, 3, Here, 40, 600);

            Assert.That(coin, Is.GreaterThan(0));
            Assert.That(_states[0].Coins.Held, Is.EqualTo(7));
            Assert.That(_ledger.Field.Coins[0].Value, Is.EqualTo(3));
            Assert.That(_ledger.Field.Coins[0].PickableFromTick, Is.EqualTo(40));
            Assert.That(_ledger.Field.Generated, Is.EqualTo(10), "ドロップは生成ではない");
            AssertConserved();
        }

        [Test]
        public void Bank_MovesHeldCoins_ToBanked()
        {
            _ledger.Reward(Id(10, CoinEventKind.DamageReward, 0), _states, 1, 10);

            Assert.That(_ledger.Bank(Id(10, CoinEventKind.Bank, 1), _states, 1, 6), Is.True);
            Assert.That(_states[1].Coins.Held, Is.EqualTo(4));
            Assert.That(_states[1].Coins.Banked, Is.EqualTo(6));
            AssertConserved();
        }

        [Test]
        public void Expire_DestroysTheCoin()
        {
            int coin = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 0), Here, 5, 10, 20);

            Assert.That(_ledger.Expire(Id(10, CoinEventKind.Expire, coin), coin), Is.True);
            Assert.That(_ledger.Field.Coins, Is.Empty);
            Assert.That(_ledger.Field.Destroyed, Is.EqualTo(5));
            AssertConserved();
        }

        [Test]
        public void InvalidTransactions_AreRejected_AndChangeNothing()
        {
            _ledger.Reward(Id(10, CoinEventKind.DamageReward, 0), _states, 0, 5);
            CharacterState before = _states[0];
            long generated = _ledger.Field.Generated;

            Assert.That(_ledger.Bank(Id(10, CoinEventKind.Bank, 0), _states, 0, 6), Is.False, "所持より多く確定");
            Assert.That(_ledger.Drop(Id(10, CoinEventKind.KoDrop, 0), _states, 0, 6, Here, 0, Never), Is.EqualTo(-1), "所持より多く落とす");
            Assert.That(_ledger.Bank(Id(10, CoinEventKind.Bank, 0, 1), _states, 0, 0), Is.False, "0 枚");
            Assert.That(_ledger.Reward(Id(10, CoinEventKind.DamageReward, 0, 1), _states, 0, -3), Is.False, "負の枚数");
            Assert.That(_ledger.Reward(Id(10, CoinEventKind.Bank, 0, 2), _states, 0, 3), Is.False, "報酬の種類ではない");
            Assert.That(_ledger.Drop(Id(10, CoinEventKind.Pickup, 0), _states, 0, 1, Here, 0, Never), Is.EqualTo(-1), "ドロップの種類ではない");
            Assert.That(_ledger.Pickup(Id(10, CoinEventKind.Pickup, 99), 99, _states, 0), Is.False, "ないコイン");
            Assert.That(_ledger.Expire(Id(10, CoinEventKind.Expire, 99), 99), Is.False, "ないコイン");
            Assert.That(_ledger.Reward(Id(9, CoinEventKind.DamageReward, 0, 3), _states, 0, 3), Is.False, "前のティックの ID");

            Assert.That(_states[0], Is.EqualTo(before));
            Assert.That(_ledger.Field.Generated, Is.EqualTo(generated));
            Assert.That(_ledger.RejectedCount, Is.EqualTo(9));
            Assert.That(_ledger.LastRejection, Is.Not.Empty, "理由が残っていない");
            AssertConserved();
        }

        [Test]
        public void ManyRandomTransactions_KeepTheTotal_AndNeverGoNegative()
        {
            var random = new Random(1234);
            _states = new List<CharacterState> { default, default, default, default };
            for (int tick = 10; tick < 2010; tick++)
            {
                _ledger.BeginTick(tick);
                for (int n = 0; n < 4; n++)
                {
                    int character = random.Next(4);
                    int amount = random.Next(-2, 12);
                    var coins = _ledger.Field.Coins;
                    switch (random.Next(6))
                    {
                        case 0:
                            _ledger.Spawn(Id(tick, CoinEventKind.Spawn, n), Here, amount, tick + random.Next(0, 5), tick + random.Next(1, 300));
                            break;
                        case 1:
                            if (coins.Count > 0)
                            {
                                int coin = coins[random.Next(coins.Count)].Id;
                                _ledger.Pickup(Id(tick, CoinEventKind.Pickup, coin), coin, _states, character);
                            }
                            break;
                        case 2:
                            _ledger.Drop(Id(tick, CoinEventKind.HitDrop, character, n), _states, character, amount, Here, tick + 30, tick + 600);
                            break;
                        case 3:
                            _ledger.Bank(Id(tick, CoinEventKind.Bank, character, n), _states, character, amount);
                            break;
                        case 4:
                            _ledger.Reward(Id(tick, CoinEventKind.DamageReward, character, n), _states, character, amount);
                            break;
                        default:
                            if (coins.Count > 0)
                            {
                                int coin = coins[random.Next(coins.Count)].Id;
                                _ledger.Expire(Id(tick, CoinEventKind.Expire, coin), coin);
                            }
                            break;
                    }
                }
                foreach (CharacterState state in _states)
                {
                    Assert.That(state.Coins.Held, Is.GreaterThanOrEqualTo(0));
                    Assert.That(state.Coins.Banked, Is.GreaterThanOrEqualTo(0));
                }
                AssertConserved();
            }
            Assert.That(_ledger.Field.Generated, Is.GreaterThan(0), "前提: 取引が行われた");
        }

        [Test]
        public void CoinIds_IncreaseInOrder()
        {
            int a = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 0), Here, 1, 10, Never);
            int b = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 1), Here, 1, 10, Never);
            _ledger.Expire(Id(10, CoinEventKind.Expire, a), a);
            int c = _ledger.Spawn(Id(10, CoinEventKind.Spawn, 2), Here, 1, 10, Never);

            Assert.That(new[] { a, b, c }, Is.EqualTo(new[] { 1, 2, 3 }), "番号を使い回した");
            Assert.That(_ledger.Field.Coins[0].Id, Is.EqualTo(b));
            Assert.That(_ledger.Field.Coins[1].Id, Is.EqualTo(c));
        }

        [Test]
        public void World_SavesAndRestores_TheCoins()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 0.91f, 0f));
            world.Step(new Input.PlayerInput[1]);
            ulong empty = world.Checksum();

            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            world.Coins.BeginTick(world.Tick);
            world.Coins.Spawn(Id(world.Tick, CoinEventKind.Spawn, 0), Here, 7, world.Tick, Never);
            Assert.That(world.Checksum(), Is.Not.EqualTo(empty), "コインがチェックサムに入っていない");

            world.LoadFrom(snapshot);
            Assert.That(world.Coins.Field.Coins, Is.Empty, "巻き戻してもコインが残っている");
            Assert.That(world.Checksum(), Is.EqualTo(empty));
        }

        [Test]
        public void KnockedOut_KeepsTheBankedCoins_AndDropsHalfOfTheHeld()
        {
            // 撃破されても、確定コインは減らない。所持コインの半分は台帳を通してフィールドに落ちる（消えない。T4-6）。
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-10f, -10f, 10f, 30f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 0.91f, 0f));
            CharacterState state = world.GetState(0);
            state.Coins = new CoinPurse { Held = 12, Banked = 30 };
            state.Position = new Vector3(0f, 50f, 0f);
            world.SetState(0, state);
            world.Step(new Input.PlayerInput[1]);

            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).Coins, Is.EqualTo(new CoinPurse { Held = 6, Banked = 30 }));
            Assert.That(world.Coins.Field.FieldTotal, Is.EqualTo(6), "落とした分がフィールドにない");
        }

        [Test]
        public void Snapshot_KeepsTheCoins()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 0.91f, 0f));
            world.Step(new Input.PlayerInput[1]);
            world.Coins.BeginTick(world.Tick);
            world.Coins.Spawn(Id(world.Tick, CoinEventKind.Spawn, 0), Here, 7, world.Tick, Never);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);

            Assert.That(snapshot.Coins.SameAs(world.Coins.Field), Is.True);
            Assert.That(snapshot.Checksum(), Is.EqualTo(world.Checksum()));
        }
    }
}
