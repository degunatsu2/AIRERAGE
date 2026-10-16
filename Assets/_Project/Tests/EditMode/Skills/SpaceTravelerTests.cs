using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// SPACE TRAVELER（docs/14 1.1、C-4〜C-9、D-62、D-63）: ゆっくり落ちる地上技、ワープポータル（置く・移る・長押しで入る・2 回まで）、
    /// スペースミサイル（照準の方向・時間で強くなる・爆発・ポータルを通る）、コズミックレイ（ポータル 2 つの間・6 回・拠点で確定）、巻き戻し。
    /// </summary>
    public class SpaceTravelerTests
    {
        private const float GroundY = 0.91f;
        private static readonly CharacterConfig Space = CharacterPresets.SpaceTraveler();

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        private static void Step(SimWorld world, int ticks, params PlayerInput[] inputs)
        {
            var all = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < all.Length && i < inputs.Length; i++)
            {
                all[i] = inputs[i];
            }
            for (int t = 0; t < ticks; t++)
            {
                world.Step(all);
            }
        }

        private static void Edit(SimWorld world, int index, System.Func<CharacterState, CharacterState> edit)
        {
            world.SetState(index, edit(world.GetState(index)));
        }

        private static void MoveTo(SimWorld world, int index, Vector3 position)
        {
            Edit(world, index, s => { s.Position = position; s.Velocity = Vector3.Zero; return s; });
        }

        private static List<WorldObject> Portals(SimWorld world)
        {
            return world.Objects.Items.Where(o => o.Kind == WorldObjectKind.Portal).ToList();
        }

        // Q を押してすぐ離す（ポータルを置く）。
        private static void TapPortal(SimWorld world)
        {
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 12);
        }

        [Test]
        public void Preset_IsValid()
        {
            Assert.DoesNotThrow(() => CharacterPresets.SpaceTraveler().Validate());
            Assert.That(Space.Combat.Special1.Effect, Is.EqualTo(SkillEffect.Portal));
            Assert.That(Space.Combat.Special1MaxCharges, Is.EqualTo(2), "ポータルが 2 スタックでない");
            Assert.That(Space.Combat.Special2.Effect, Is.EqualTo(SkillEffect.Missile));
            Assert.That(Space.Combat.Super.Effect, Is.EqualTo(SkillEffect.CosmicRay));
            Assert.That(Space.Combat.SuperGaugeCost, Is.EqualTo(CombatTuning.MaxGauge), "コズミックレイが 100% でない（D-62）");
        }

        [Test]
        public void MoonHop_FloatsBackward_ThenFallsSlowly_UntilAnotherMove()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground));
            Step(world, 12);
            CharacterState hop = world.GetState(0);
            Assert.That(hop.Grounded, Is.False, "飛び上がらない");
            Assert.That(hop.Position.X, Is.LessThan(-0.3f), "斜め後ろへ飛ばない");
            Assert.That(hop.Combat.SlowFallSerial, Is.Not.Zero, "ゆっくり落ちるのが始まらない");

            float fastest = 0f;
            for (int t = 0; t < 50; t++)
            {
                Step(world, 1, Press(InputButtons.None, 1f));
                fastest = System.Math.Min(fastest, world.GetState(0).Velocity.Y);
            }
            Assert.That(fastest, Is.GreaterThanOrEqualTo(-Space.Skills.SlowFall.MaxFallSpeed - 1e-3f), "ゆっくり落ちない");
            Assert.That(world.GetState(0).Velocity.X, Is.GreaterThan(1f), "左右に動けない");

            // 空中ジャンプで終わる。
            Step(world, 1, Press(InputButtons.Jump));
            Assert.That(world.GetState(0).Combat.SlowFallSerial, Is.Zero, "ジャンプしてもゆっくり落ちたまま");
            Step(world, 40);
            Assert.That(world.GetState(0).Velocity.Y, Is.LessThan(-Space.Skills.SlowFall.MaxFallSpeed - 1f), "終わった後もゆっくり落ちる");
        }

        [Test]
        public void MoonHop_JumpsHigh_AndHitsForward_LaunchingUpward()
        {
            // 高く飛ぶ（ユーザーの要望、2026-10-16。前は 13 m/s で約 2.8m）。
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            float top = GroundY;
            for (int t = 0; t < 80; t++)
            {
                Step(world, 1);
                top = System.Math.Max(top, world.GetState(0).Position.Y);
            }
            Assert.That(top - GroundY, Is.GreaterThan(4f), "高く飛ばない");

            // 飛び上がる瞬間に、前の相手を強く上へ飛ばす。
            world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(2f, GroundY, 0f), -1);
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            HitRecord? hit = null;
            int at = -1;
            for (int t = 0; t < 30 && hit == null; t++)
            {
                Step(world, 1);
                if (world.LastHits.Count > 0)
                {
                    hit = world.LastHits[0];
                    at = world.GetState(0).Action.Ticks;
                }
            }
            Assert.That(hit.HasValue, Is.True, "前の相手に当たらない");
            Assert.That(hit.Value.Attack, Is.EqualTo(AttackSlot.Ground));
            Assert.That(at, Is.EqualTo(Space.Combat.Ground.Hits[0].StartTick + 1).Within(1), "飛び上がる瞬間に当たらない");
            Assert.That(hit.Value.Knockback.Y, Is.GreaterThan(System.Math.Abs(hit.Value.Knockback.X) * 4f), "吹き飛びが強い上向きでない");
            Assert.That(hit.Value.Knockback.X, Is.GreaterThan(0f), "前へ飛ばさない");

            // 後ろの相手には当たらない。
            world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(-2f, GroundY, 0f), 1);
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            for (int t = 0; t < 30; t++)
            {
                Step(world, 1);
                Assert.That(world.LastHits.Count, Is.Zero, "後ろの相手に当たった");
            }
        }

        [Test]
        public void Portal_WarpsWhoeverEnters_KeepsTheVelocity_AndNotThePlacerAtOnce()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(30f, 50f, 0f));
            TapPortal(world);
            Assert.That(Portals(world).Count, Is.EqualTo(1), "ポータルを置かない");
            Assert.That(world.GetState(0).Combat.WarpCount, Is.Zero, "1 つだけなのに移った");

            MoveTo(world, 0, new Vector3(20f, 50f, 0f));
            TapPortal(world);
            Assert.That(Portals(world).Count, Is.EqualTo(2));
            Assert.That(world.GetState(0).Combat.WarpCount, Is.Zero, "置いた直後に自分が入った（ユーザーの要望、2026-10-16）");
            Assert.That(world.GetState(0).Combat.PortalCount, Is.EqualTo(2));

            // ほかの人が古いポータルに入ると、新しいポータルから同じ速度で出る。
            WorldObject first = Portals(world)[0];
            WorldObject second = Portals(world)[1];
            Edit(world, 1, s => { s.Position = new Vector3(first.Position.X - 3f, first.Position.Y, 0f); s.Velocity = new Vector3(15f, 0f, 0f); return s; });
            int warped = -1;
            for (int t = 0; t < 30 && warped < 0; t++)
            {
                Step(world, 1);
                if (world.GetState(1).Combat.WarpCount > 0)
                {
                    warped = t;
                }
            }
            Assert.That(warped, Is.GreaterThanOrEqualTo(0), "ポータルに入っても移らない");
            CharacterState other = world.GetState(1);
            Assert.That(Vector2.Distance(new Vector2(other.Position.X, other.Position.Y), second.Position), Is.LessThan(1.5f), "もう一方から出ない");
            Assert.That(other.Velocity.X, Is.GreaterThan(10f), "速度が変わった");
            Step(world, 5);
            Assert.That(world.GetState(1).Combat.WarpCount, Is.EqualTo(1), "出たポータルにすぐ戻った");

            // 3 つ目を置くと、いちばん古いものが消える。
            Step(world, 600);
            MoveTo(world, 0, new Vector3(-20f, 50f, 0f));
            TapPortal(world);
            List<WorldObject> after = Portals(world);
            Assert.That(after.Count, Is.EqualTo(2), "3 つ目でいちばん古いものが消えない");
            Assert.That(after.Any(p => p.Id == first.Id), Is.False);
        }

        [Test]
        public void Portal_HoldThenRelease_EntersThePlacedPortal()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            TapPortal(world);
            WorldObject first = Portals(world)[0];
            MoveTo(world, 0, new Vector3(25f, 50f, 0f));
            Step(world, Space.Skills.Portal.EnterHoldTicks + 2, Press(InputButtons.Special1));
            Step(world, 1);
            Assert.That(Portals(world).Count, Is.EqualTo(2), "前提: 置いた");
            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.WarpCount, Is.EqualTo(1), "長押ししてから離しても入らない");
            Assert.That(Vector2.Distance(new Vector2(state.Position.X, state.Position.Y), first.Position), Is.LessThan(1f), "もう一方へ移らない");
        }

        [Test]
        public void Portal_HasTwoCharges_AndOneComesBackAfterTheCooldown()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            TapPortal(world);
            MoveTo(world, 0, new Vector3(10f, 50f, 0f));
            TapPortal(world);
            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.Special1Spent, Is.EqualTo(2));
            Assert.That(ActionController.CanUse(state, AttackSlot.Special1, Space.Combat), Is.False, "2 回使った後も使える");
            Step(world, Space.Combat.Special1CooldownTicks);
            state = world.GetState(0);
            Assert.That(state.Combat.Special1Spent, Is.EqualTo(1), "クールタイムで 1 つ戻らない");
            Assert.That(ActionController.CanUse(state, AttackSlot.Special1, Space.Combat), Is.True);
            Assert.That(state.Combat.Special1CooldownTicksLeft, Is.GreaterThan(0), "まだ減っているのに次のクールタイムが始まらない");
            Step(world, Space.Combat.Special1CooldownTicks);
            Assert.That(world.GetState(0).Combat.Special1Spent, Is.Zero, "2 つとも戻らない");
        }

        [Test]
        public void Missile_FliesTowardTheAim_GrowsStronger_AndExplodes()
        {
            var world = new SimWorld(new BoxTerrain(), new StageBounds(-200f, -200f, 200f, 200f));
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, 80f, 0f));
            // E を押したまま照準を上へ（狙い）、離して撃つ。
            Step(world, 20, new PlayerInput(0, 0, InputButtons.Special2, 0, 127));
            Assert.That(world.GetState(0).Combat.Holding, Is.True, "長押しで狙えない");
            Assert.That(world.GetState(0).Combat.SkillAim.Y, Is.GreaterThan(0.9f), "狙いの向きを覚えない");
            Step(world, 1, new PlayerInput(0, 0, InputButtons.None, 0, 127));
            Step(world, 4);
            WorldObject missile = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Missile);
            Assert.That(missile.Velocity.Y, Is.EqualTo(Space.Skills.Missile.Speed).Within(1e-3f), "照準の方向へ撃たない");

            // 30m 上の相手に当たる。1 秒半ほど飛ぶので、威力は 5 より大きい。
            HitRecord? hit = null;
            for (int t = 0; t < 200 && hit == null; t++)
            {
                Edit(world, 1, s => { s.Position = new Vector3(0f, 80f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
                if (world.LastHits.Any(h => h.FromObject && h.Victim == 1))
                {
                    hit = world.LastHits.First(h => h.FromObject && h.Victim == 1);
                }
            }
            Assert.That(hit.HasValue, Is.True, "ミサイルが当たらない");
            Assert.That(hit.Value.Damage, Is.GreaterThanOrEqualTo(15), "飛んだ時間で強くならない（1 秒で +10）");
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Missile), Is.False, "当たっても消えない");
        }

        [Test]
        public void Missile_ExplodesOutsideTheStage_AndBreaksASafe()
        {
            var world = new SimWorld(new BoxTerrain(), new StageBounds(-20f, -20f, 20f, 80f));
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(CharacterPresets.RiskTaker(), new Vector3(-10f, 50f, 0f));
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Special2, 127, 0));
            Step(world, 1, new PlayerInput(0, 0, InputButtons.None, 127, 0));
            Step(world, 4);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Missile), Is.True, "前提: 撃った");
            Step(world, 90);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Missile), Is.False, "ステージの外に出ても消えない（D-63）");

            // 金庫に当たると壊す。
            Step(world, 400);
            world.Objects.Add(new WorldObject { Kind = WorldObjectKind.Safe, Owner = 1, Position = new Vector2(-6f, 50f), SpawnTick = world.Tick,
                ExpireTick = int.MaxValue });
            MoveTo(world, 0, new Vector3(0f, 50f, 0f));
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Special2, -127, 0));
            Step(world, 1, new PlayerInput(0, 0, InputButtons.None, -127, 0));
            Step(world, 40);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Safe), Is.False, "ミサイルで金庫が壊れない");
        }

        [Test]
        public void Missile_PassesThroughThePortals()
        {
            var world = new SimWorld(new BoxTerrain(), new StageBounds(-200f, -200f, 200f, 200f));
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            MoveTo(world, 0, new Vector3(5f, 50f, 0f));
            TapPortal(world);
            MoveTo(world, 0, new Vector3(0f, 80f, 0f));
            TapPortal(world);
            WorldObject exit = Portals(world)[1];
            Step(world, 600);
            MoveTo(world, 0, new Vector3(0f, 50f, 0f));
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Special2, 127, 0));
            Step(world, 1, new PlayerInput(0, 0, InputButtons.None, 127, 0));
            bool through = false;
            for (int t = 0; t < 60 && !through; t++)
            {
                Step(world, 1);
                through = world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Missile && o.Position.Y > 70f);
            }
            Assert.That(through, Is.True, "ミサイルがポータルを通らない");
            WorldObject missile = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Missile);
            Assert.That(missile.Velocity.X, Is.GreaterThan(0f), "入ったときの向きのまま出ない");
            Assert.That(missile.Position.X, Is.InRange(exit.Position.X - 2f, exit.Position.X + 3f));
        }

        [Test]
        public void CosmicRay_NeedsTwoPortals_HitsSixTimesThroughWalls_AndBanksTheHeldCoins()
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 0 };
            // 拠点は (10, 50)。ビームの線（(0,50) から (20,50)）がその上を通る。間に壁がある。
            var banks = new[] { new BankSpot(new Vector2(10f, 50f), isFirst: true) };
            var terrain = new BoxTerrain().AddBox(6f, 40f, 7f, 60f);
            var world = new SimWorld(terrain, null, null, rules, null, banks);
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(14f, 50f, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; s.Coins.Held = 40; return s; });
            TapPortal(world);
            Assert.That(ActionController.CanUse(world.GetState(0), AttackSlot.Super, Space.Combat), Is.False, "ポータルが 1 つでも出せる");

            MoveTo(world, 0, new Vector3(20f, 50f, 0f));
            TapPortal(world);
            MoveTo(world, 0, new Vector3(0f, 30f, 0f));
            Assert.That(ActionController.CanUse(world.GetState(0), AttackSlot.Super, Space.Combat), Is.True, "ポータルが 2 つあるのに出せない");
            Step(world, 1, Press(InputButtons.Super));
            int beamHits = 0;
            for (int t = 0; t < 120; t++)
            {
                Edit(world, 1, s => { s.Position = new Vector3(14f, 50f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
                beamHits += world.LastHits.Count(h => h.FromObject && h.Attacker == 0 && h.Victim == 1);
            }
            Assert.That(beamHits, Is.EqualTo(Space.Skills.Beam.Hits), "ビームが 6 回当たらない（壁を貫通しない）");
            // 撃ち終わったときの手持ち（40 枚）を確定する。その後のコンボの報酬（ダメージの 30%）は手持ちに入る。
            Assert.That(world.GetState(0).Coins.Banked, Is.EqualTo(40), "ビームが拠点に触れても手持ちのコインを確定しない");
        }

        [Test]
        public void Rollback_ReproducesThePortalsMissilesAndTheRay()
        {
            var scripts = new PlayerInput[2][];
            for (int p = 0; p < 2; p++)
            {
                scripts[p] = new PlayerInput[900];
                for (int t = 0; t < scripts[p].Length; t++)
                {
                    int phase = (t + p * 29) % 160;
                    InputButtons buttons = phase == 2 || (phase >= 60 && phase < 80) ? InputButtons.Special1
                        : phase >= 30 && phase < 45 ? InputButtons.Special2
                        : phase == 100 ? InputButtons.Super
                        : phase == 120 ? InputButtons.Attack
                        : phase == 130 ? InputButtons.Jump
                        : InputButtons.None;
                    float x = phase < 60 ? (p == 0 ? 0.8f : -0.8f) : 0f;
                    scripts[p][t] = new PlayerInput((sbyte)(x * 127), 0, buttons, (sbyte)(p == 0 ? 100 : -100), 60);
                }
            }
            void Setup(SimWorld world)
            {
                for (int i = 0; i < world.CharacterCount; i++)
                {
                    world.SetConfig(i, CharacterPresets.SpaceTraveler());
                    CharacterState state = world.GetState(i);
                    state.Combat.Gauge = CombatTuning.MaxGauge;
                    world.SetState(i, state);
                }
            }
            var network = new PseudoNetwork(scripts, new RollbackSettings(), 5, 2, 0.05, 37, Setup);

            network.RunUntil(700, maxFrames: 4000);

            SimWorld reference = network.Reference(700);
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Worlds[1].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 巻き戻している");
        }
    }
}
