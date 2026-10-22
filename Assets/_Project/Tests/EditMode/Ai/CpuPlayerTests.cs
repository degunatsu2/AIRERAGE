using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Ai;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Ai
{
    /// <summary>
    /// CPU（docs/16 T7-1）: コインを拾いに行く、ダミーに攻撃を当てる、落ちそうなら戻る、所持コインが多ければ拠点へ行く、同じ試合なら同じ動き。
    /// </summary>
    public class CpuPlayerTests
    {
        private static readonly Vector3 Left = new Vector3(-10f, 0.91f, 0f);

        [Test]
        public void GoesForAFieldCoin_AndPicksItUp()
        {
            var spawns = new List<CoinSpawnPoint> { new CoinSpawnPoint(new Vector2(12f, 1.2f), 5) };
            var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay(), spawns);
            world.AddCharacter(new CharacterConfig(), Left);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Normal));

            bool picked = Run(world, new[] { cpu }, 900, () => world.GetState(0).Coins.Held > 0);

            Assert.That(picked, Is.True, $"コインを拾わない（目標 {cpu.Goal} {cpu.Target}、位置 {world.GetState(0).Position}）");
        }

        [Test]
        public void ApproachesAndHitsAStandingDummy([Values(CpuLevel.Easy, CpuLevel.Normal, CpuLevel.Hard)] CpuLevel level)
        {
            var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
            world.AddCharacter(new CharacterConfig(), Left);
            world.AddCharacter(new CharacterConfig(), new Vector3(6f, 0.91f, 0f), -1);
            var cpu = new CpuPlayer(0, CpuTuning.For(level));

            bool hit = Run(world, new[] { cpu }, 900, () => world.GetState(1).Combat.HasLastAttacker);

            Assert.That(hit, Is.True, $"ダミーに当てない（出した攻撃 {cpu.AttacksStarted}、目標 {cpu.Goal}）");
            Assert.That(cpu.Goal, Is.EqualTo(CpuGoalKind.Fight));
        }

        [Test]
        public void RecoversWithTheWire_WhenFallingBesideTheStage()
        {
            // 真ん中の足場（-10〜10）と、つながる天井。右の空中（足場の外）から落ち始める。
            BoxTerrain terrain = new BoxTerrain()
                .AddBox(-10f, -1f, 10f, 0f)
                .AddBox(-30f, 12f, 30f, 13f, attachable: true);
            var bounds = new StageBounds(-28f, -14f, 28f, 30f);
            var world = new SimWorld(terrain, bounds, null, MatchRules.FreePlay());
            world.AddCharacter(new CharacterConfig(), new Vector3(15f, 4f, 0f), -1);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Normal));

            bool landed = Run(world, new[] { cpu }, 600, () => world.GetState(0).Grounded && world.GetState(0).Combat.Falls == 0);

            Assert.That(landed, Is.True, $"足場へ戻らない（位置 {world.GetState(0).Position}、撃破 {world.GetState(0).Combat.Falls}、ワイヤー {cpu.WiresFired}）");
            Assert.That(cpu.WiresFired, Is.GreaterThan(0), "ワイヤーを使っていない");
        }

        [Test]
        public void GoesToTheBank_WhenHoldingManyCoins()
        {
            var banks = new List<BankSpot> { new BankSpot(new Vector2(12f, 1.5f), isFirst: true) };
            var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay(), null, banks);
            world.AddCharacter(new CharacterConfig(), Left);
            CharacterState state = world.GetState(0);
            state.Coins.Held = 30;
            world.SetState(0, state);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Normal));

            bool banked = Run(world, new[] { cpu }, 900, () => world.GetState(0).Coins.Banked > 0);

            Assert.That(banked, Is.True, $"拠点で確定しない（目標 {cpu.Goal} {cpu.Target}、位置 {world.GetState(0).Position}）");
        }

        [Test]
        public void SameMatch_SameMoves()
        {
            ulong first = FightChecksum();
            ulong second = FightChecksum();

            Assert.That(second, Is.EqualTo(first), "同じ試合なのに CPU の動きが違う");
        }

        [Test]
        public void StaysStill_DuringTheCountdown()
        {
            var rules = new MatchRules();
            var world = new SimWorld(TestArenas.Playground(), null, null, rules);
            world.AddCharacter(new CharacterConfig(), Left);
            world.AddCharacter(new CharacterConfig(), new Vector3(6f, 0.91f, 0f), -1);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Hard));

            Assert.That(world.Phase, Is.EqualTo(MatchPhase.Countdown));
            Assert.That(cpu.Think(world), Is.EqualTo(default(PlayerInput)));
        }

        [Test]
        public void FourCpus_PlayAMinute_Fighting_AndCollecting()
        {
            // 撃破の範囲とコインのある遊び場で、CPU 4 体が 1 分遊ぶ: 全員が攻撃し、だれかがコインを拾い、コインの総量は保たれる。
            var spawns = new List<CoinSpawnPoint>();
            for (int i = 0; i < 6; i++)
            {
                spawns.Add(new CoinSpawnPoint(new Vector2(-20f + 8f * i, 2f + (i % 3) * 3f), 1 + 2 * (i % 3)));
            }
            var banks = new List<BankSpot> { new BankSpot(new Vector2(0f, 8f), isFirst: true) };
            var world = new SimWorld(TestArenas.Playground(), new StageBounds(-29f, -10f, 29f, 30f), null, MatchRules.FreePlay(), spawns, banks);
            var cpus = new CpuPlayer[4];
            for (int i = 0; i < 4; i++)
            {
                world.AddCharacter(new CharacterConfig(), new Vector3(-12f + 8f * i, 0.91f, 0f), i % 2 == 0 ? 1 : -1);
                cpus[i] = new CpuPlayer(i, CpuTuning.For(CpuLevel.Normal), salt: i);
            }

            Run(world, cpus, 3600, () => false);

            int held = 0;
            int damaged = 0;
            for (int i = 0; i < 4; i++)
            {
                CharacterState state = world.GetState(i);
                held += state.Coins.Held + state.Coins.Banked;
                damaged += state.Combat.HasLastAttacker ? 1 : 0;
                TestContext.WriteLine($"P{i}: 攻撃 {cpus[i].AttacksStarted}、ワイヤー {cpus[i].WiresFired}、撃破 {state.Combat.Knockouts}、" +
                    $"撃破された {state.Combat.Falls}、コイン {state.Coins.Held}/{state.Coins.Banked}、目標 {cpus[i].Goal}");
                Assert.That(cpus[i].AttacksStarted, Is.GreaterThan(0), $"P{i} が攻撃しない");
            }
            Assert.That(damaged, Is.GreaterThan(0), "だれも攻撃を受けていない");
            Assert.That(held, Is.GreaterThan(0), "だれもコインを拾わない");
            Assert.That(world.Coins.Totals(new List<CharacterState> { world.GetState(0), world.GetState(1), world.GetState(2), world.GetState(3) })
                .IsConserved, Is.True);
        }

        // 4 体の CPU どうしで戦わせ、最後の状態のチェックサム。
        private static ulong FightChecksum()
        {
            var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
            var cpus = new CpuPlayer[4];
            for (int i = 0; i < 4; i++)
            {
                world.AddCharacter(new CharacterConfig(), new Vector3(-12f + 8f * i, 0.91f, 0f), i % 2 == 0 ? 1 : -1);
                cpus[i] = new CpuPlayer(i, CpuTuning.For((CpuLevel)(i % 3)), salt: 3);
            }
            Run(world, cpus, 600, () => false);
            return world.Checksum();
        }

        // CPU に操作させて、done になるか maxTicks まで進める（CPU のいない番号は何もしない）。
        internal static bool Run(SimWorld world, IReadOnlyList<CpuPlayer> cpus, int maxTicks, System.Func<bool> done)
        {
            var inputs = new PlayerInput[world.CharacterCount];
            for (int t = 0; t < maxTicks; t++)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i] = default;
                }
                foreach (CpuPlayer cpu in cpus)
                {
                    inputs[cpu.Index] = cpu.Think(world);
                }
                world.Step(inputs);
                if (done())
                {
                    return true;
                }
            }
            return false;
        }
    }
}
