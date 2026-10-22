using System;
using System.Collections.Generic;
using System.Numerics;
using Airerage.Core.Ai;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Ai
{
    /// <summary>
    /// CPU の技の使い分け（docs/16 T7-4）: 技のデータから使い時と押し方を決める（CpuSkills）。撃つ技は相手が見えるときだけ、
    /// 落ちそうなときの技（わたがし・ワープ）、長押しの間に押すボタン（抹茶）、切り替え（インバータ）、動きで届く技（イナズマステップ）、
    /// 試合で 6 体それぞれが技を使う。
    /// </summary>
    public class CpuSkillsTests
    {
        private static readonly StageBounds Bounds = new StageBounds(-28f, -14f, 28f, 30f);

        // 真ん中の足場（-10〜10）と、つながる天井。
        private static BoxTerrain Island()
        {
            return new BoxTerrain()
                .AddBox(-10f, -1f, 10f, 0f)
                .AddBox(-30f, 12f, 30f, 13f, attachable: true);
        }

        [Test]
        public void Missile_AimsAtAnOpponentInSight_NotThroughAWall()
        {
            CharacterConfig space = CharacterPresets.SpaceTraveler();
            var open = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
            open.AddCharacter(space, new Vector3(-6f, 0.91f, 0f));
            open.AddCharacter(new CharacterConfig(), new Vector3(4f, 0.91f, 0f), -1);
            var walled = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
            walled.AddCharacter(space, new Vector3(2f, 0.91f, 0f));
            walled.AddCharacter(new CharacterConfig(), new Vector3(14f, 0.91f, 0f), -1);   // 間に細い壁（x = 8）

            bool seen = CpuSkills.Plan(open, 0, open.GetState(0), space, Situation(open, 1), AttackSlot.Special2, out CpuSkillUse use);
            bool hidden = CpuSkills.Plan(walled, 0, walled.GetState(0), space, Situation(walled, 1), AttackSlot.Special2, out _);

            Assert.That(seen, Is.True, "見えている相手にミサイルを撃たない");
            Assert.That(use.Aim.X, Is.GreaterThan(0f), "相手の方へ照準していない");
            Assert.That(use.HoldTicks, Is.EqualTo(1), "ミサイルは短押しで撃つ");
            Assert.That(hidden, Is.False, "壁の向こうの相手にミサイルを撃つ");
        }

        [Test]
        public void InazumaStep_TapsWhenClose_HoldsWhenALittleFar_SkipsWhenFar()
        {
            CharacterConfig solderer = CharacterPresets.Solderer();
            float[] distances = { 2.5f, 6.5f, 15f };
            var holds = new List<int>();
            foreach (float distance in distances)
            {
                var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
                world.AddCharacter(solderer, new Vector3(-12f, 0.91f, 0f));
                world.AddCharacter(new CharacterConfig(), new Vector3(-12f + distance, 0.91f, 0f), -1);
                bool use = CpuSkills.Plan(world, 0, world.GetState(0), solderer, Situation(world, 1), AttackSlot.Special1, out CpuSkillUse plan);
                holds.Add(use ? plan.HoldTicks : 0);
            }

            Assert.That(holds[0], Is.EqualTo(1), "近い相手に短押しで突撃しない");
            Assert.That(holds[1], Is.GreaterThan(solderer.Combat.Special1.ChargedFromTicks), "少し遠い相手に長押しで突撃しない");
            Assert.That(holds[2], Is.EqualTo(0), "届かない相手に突撃する");
        }

        [Test]
        public void Recovering_PastryChef_PutsCottonCandyUnderItself()
        {
            var world = new SimWorld(Island(), Bounds, null, MatchRules.FreePlay());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(16f, 3f, 0f), -1);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Hard));

            bool placed = CpuPlayerTests.Run(world, new[] { cpu }, 120, () => Count(world, WorldObjectKind.CottonCandy) > 0);

            Assert.That(placed, Is.True, $"落ちそうなのに、わたがしの足場を置かない（目標 {cpu.Goal}、位置 {world.GetState(0).Position}）");
            WorldObject candy = Find(world, WorldObjectKind.CottonCandy);
            Assert.That(candy.Position.Y, Is.LessThan(world.GetState(0).Position.Y + 0.5f), "足場が足元にない");
        }

        [Test]
        public void SpaceTraveler_LeavesAPortal_ThenWarpsBackToIt_WhenFalling()
        {
            var world = new SimWorld(Island(), Bounds, null, MatchRules.FreePlay());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 0.91f, 0f));
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Hard));

            bool left = CpuPlayerTests.Run(world, new[] { cpu }, 300, () => world.GetState(0).Combat.PortalCount == 1);
            Assert.That(left, Is.True, "落ち着いているのにワープポータルを置かない");
            Vector2 portal = Find(world, WorldObjectKind.Portal).Position;

            // 足場の外の空中へ移して、落ち始めさせる。
            CharacterState state = world.GetState(0);
            state.Position = new Vector3(17f, 2f, 0f);
            state.Velocity = Vector3.Zero;
            state.Grounded = false;
            world.SetState(0, state);
            bool warped = CpuPlayerTests.Run(world, new[] { cpu }, 120,
                () => Vector2.Distance(new Vector2(world.GetState(0).Position.X, world.GetState(0).Position.Y), portal) < 1.5f);

            Assert.That(warped, Is.True, $"落ちそうなのに、置いたポータルへ戻らない（位置 {world.GetState(0).Position}、ポータル {portal}）");
            Assert.That(world.GetState(0).Combat.Falls, Is.Zero);
        }

        [Test]
        public void PastryChef_ThrowsMatchaCream_AtAnOpponentALittleAway()
        {
            var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
            world.AddCharacter(CharacterPresets.PastryChef(), new Vector3(-16f, 0.91f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(-10f, 0.91f, 0f), -1);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Hard));

            bool thrown = CpuPlayerTests.Run(world, new[] { cpu }, 600, () => Count(world, WorldObjectKind.CreamShot) > 0);

            Assert.That(thrown, Is.True, $"相手にクリームを投げない（技 {cpu.SkillsStarted}、目標 {cpu.Goal}）");
            WorldObject cream = Find(world, WorldObjectKind.CreamShot);
            Assert.That(cream.Variant, Is.EqualTo((byte)CreamFlavor.Matcha), "抹茶を選んでいない");
            Assert.That(cream.Velocity.X, Is.GreaterThan(0f), "相手の方へ投げていない");
        }

        [Test]
        public void Solderer_TurnsTheInverterOn_NearAnOpponent_AndOff_WhenFalling()
        {
            var world = new SimWorld(Island(), Bounds, null, MatchRules.FreePlay());
            world.AddCharacter(CharacterPresets.Solderer(), new Vector3(-3f, 0.91f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(3f, 0.91f, 0f), -1);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Hard));

            bool on = CpuPlayerTests.Run(world, new[] { cpu }, 600, () => world.GetState(0).Combat.InverterOn);
            Assert.That(on, Is.True, "相手が近いのにインバータを入れない");

            CharacterState state = world.GetState(0);
            state.Position = new Vector3(17f, 4f, 0f);
            state.Velocity = Vector3.Zero;
            state.Grounded = false;
            state.Action = default;
            world.SetState(0, state);
            bool off = CpuPlayerTests.Run(world, new[] { cpu }, 120, () => !world.GetState(0).Combat.InverterOn);

            Assert.That(off, Is.True, "落ちそうなのにインバータを切らない（ワイヤーの射程が短いまま）");
        }

        [Test]
        public void RiskTaker_DepositsIntoASafe_WhenRichAndAlone()
        {
            var world = new SimWorld(TestArenas.Playground(), null, null, MatchRules.FreePlay());
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(-16f, 0.91f, 0f));
            CharacterState state = world.GetState(0);
            state.Coins.Held = 40;
            world.SetState(0, state);
            var cpu = new CpuPlayer(0, CpuTuning.For(CpuLevel.Hard));

            bool deposited = CpuPlayerTests.Run(world, new[] { cpu }, 600, () => Count(world, WorldObjectKind.Safe) > 0);

            Assert.That(deposited, Is.True, "コインを多く持ってひとりなのに、金庫に預けない");
            Assert.That(Find(world, WorldObjectKind.Safe).Stored, Is.EqualTo(40), "持っている分を預けきっていない");
            Assert.That(world.GetState(0).Coins.Held, Is.Zero);
        }

        [TestCase(CharacterPresets.StandardName)]
        [TestCase(CharacterPresets.RiskTakerName)]
        [TestCase(CharacterPresets.StormRunnerName)]
        [TestCase(CharacterPresets.SpaceTravelerName)]
        [TestCase(CharacterPresets.PastryChefName)]
        [TestCase(CharacterPresets.SoldererName)]
        public void InAMatch_EachCharacterUsesItsSkills(string name)
        {
            // 撃破の範囲とコインと拠点のある遊び場で、そのキャラクターの CPU（HARD）と STANDARD の CPU 3 体が 1 分半遊ぶ。
            // 必殺技のゲージは最初から満たしておく。技を出した枠を数える。
            var spawns = new List<CoinSpawnPoint>();
            for (int i = 0; i < 6; i++)
            {
                spawns.Add(new CoinSpawnPoint(new Vector2(-20f + 8f * i, 2f + (i % 3) * 3f), 1 + 2 * (i % 3)));
            }
            var banks = new List<BankSpot> { new BankSpot(new Vector2(0f, 8f), isFirst: true) };
            var world = new SimWorld(TestArenas.Playground(), new StageBounds(-29f, -10f, 29f, 30f), null, MatchRules.FreePlay(), spawns, banks);
            CharacterConfig config = Preset(name);
            var cpus = new CpuPlayer[4];
            for (int i = 0; i < 4; i++)
            {
                world.AddCharacter(i == 0 ? config : new CharacterConfig(), new Vector3(-12f + 8f * i, 0.91f, 0f), i % 2 == 0 ? 1 : -1);
                cpus[i] = new CpuPlayer(i, CpuTuning.For(i == 0 ? CpuLevel.Hard : CpuLevel.Normal), salt: i);
            }
            CharacterState first = world.GetState(0);
            first.Combat.Gauge = config.Combat.SuperGaugeCost;
            world.SetState(0, first);

            var used = new Dictionary<AttackSlot, int> { { AttackSlot.Special1, 0 }, { AttackSlot.Special2, 0 }, { AttackSlot.Super, 0 } };
            int serial = world.GetState(0).Combat.AttackSerial;
            CpuPlayerTests.Run(world, cpus, 5400, () =>
            {
                CharacterState state = world.GetState(0);
                if (state.Combat.AttackSerial != serial && state.Action.Kind == ActionKind.Attack && used.ContainsKey(state.Action.Attack))
                {
                    used[state.Action.Attack]++;
                }
                serial = state.Combat.AttackSerial;
                return false;
            });

            TestContext.WriteLine($"{name}: Q {used[AttackSlot.Special1]}、E {used[AttackSlot.Special2]}、R {used[AttackSlot.Super]}" +
                $"（押した {cpus[0].SkillsStarted}、攻撃 {cpus[0].AttacksStarted}、撃破 {world.GetState(0).Combat.Knockouts}、" +
                $"撃破された {world.GetState(0).Combat.Falls}）");
            Assert.That(used[AttackSlot.Special1] + used[AttackSlot.Special2] + used[AttackSlot.Super], Is.GreaterThan(0), $"{name} の CPU が技を使わない");
        }

        private static CharacterConfig Preset(string name)
        {
            switch (name)
            {
                case CharacterPresets.RiskTakerName: return CharacterPresets.RiskTaker();
                case CharacterPresets.StormRunnerName: return CharacterPresets.StormRunner();
                case CharacterPresets.SpaceTravelerName: return CharacterPresets.SpaceTraveler();
                case CharacterPresets.PastryChefName: return CharacterPresets.PastryChef();
                case CharacterPresets.SoldererName: return CharacterPresets.Solderer();
                default: return CharacterPresets.Standard();
            }
        }

        // 番号 0 の CPU から見た、相手 opponent との場面（相手を攻めている）。
        private static CpuSituation Situation(SimWorld world, int opponent)
        {
            Vector3 self = world.GetState(0).Position;
            Vector3 other = world.GetState(opponent).Position;
            float distance = Vector2.Distance(new Vector2(self.X, self.Y), new Vector2(other.X, other.Y));
            return new CpuSituation(CpuGoalKind.Fight, new Vector2(other.X, other.Y), opponent, distance, overVoid: false, bankAtHeldCoins: 20);
        }

        private static int Count(SimWorld world, WorldObjectKind kind)
        {
            int count = 0;
            for (int o = 0; o < world.Objects.Count; o++)
            {
                count += world.Objects[o].Kind == kind && !world.Objects[o].Ended ? 1 : 0;
            }
            return count;
        }

        private static WorldObject Find(SimWorld world, WorldObjectKind kind)
        {
            for (int o = 0; o < world.Objects.Count; o++)
            {
                if (world.Objects[o].Kind == kind && !world.Objects[o].Ended)
                {
                    return world.Objects[o];
                }
            }
            throw new InvalidOperationException($"{kind} がない");
        }
    }
}
