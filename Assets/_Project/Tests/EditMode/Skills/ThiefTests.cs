using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Input;
using Airerage.Core.Match;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// THIEF（docs/14 1.6。ユーザーの案、2026-10-28）: 通常攻撃（煙球、5 段の刺突と最後だけの FATAL）、Q タレントスナッチ（Q のコピー、
    /// ギミック・仕掛けを奪って置く）、E ミスディレクション（分身と透明）、R バンクハイスト（拠点の近くでの詠唱とコイン）。
    /// </summary>
    public class ThiefTests
    {
        private const float GroundY = 0.91f;
        private static readonly CharacterConfig Thief = CharacterPresets.Thief();

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        private static PlayerInput Aimed(InputButtons buttons, Vector2 aim)
        {
            return PlayerInput.FromAxes(0f, 0f, buttons).WithAim(aim);
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

        private static SimWorld World(ICollisionQuery terrain, Vector3 at, params Vector3[] dummies)
        {
            return World(new SimWorld(terrain), at, dummies);
        }

        private static SimWorld World(SimWorld world, Vector3 at, params Vector3[] dummies)
        {
            world.AddCharacter(CharacterPresets.Thief(), at);
            foreach (Vector3 dummy in dummies)
            {
                world.AddCharacter(new CharacterConfig(), dummy);
            }
            return world;
        }

        [Test]
        public void Preset_IsValid()
        {
            Assert.DoesNotThrow(() => CharacterPresets.Thief().Validate());
            Assert.That(Thief.Combat.KoAttack, Is.EqualTo(AttackSlot.AirUp), "撃破技が空中上でない");
            Assert.That(Thief.Combat.Special1CooldownTicks, Is.EqualTo(720), "Q のクールタイムが 12 秒でない");
            Assert.That(Thief.Combat.Special2CooldownTicks, Is.EqualTo(1200), "E のクールタイムが 20 秒でない");
            Assert.That(Thief.Combat.SuperGaugeCost, Is.EqualTo(80 * CombatTuning.GaugePerPercent), "R が 80% でない");
            Assert.That(ThiefRules.IsThief(Thief), Is.True);
            Assert.That(ThiefRules.IsThief(new CharacterConfig()), Is.False);
        }

        [Test]
        public void FiveStabs_AreThinShortStabsOverTheHead_InTheOrder24153()
        {
            AttackHit[] hits = Thief.Combat.AirUp.Hits;
            Assert.That(hits.Length, Is.EqualTo(5));
            float column = Thief.Movement.HalfWidth * 2f / 5f;
            int[] order = { 2, 4, 1, 5, 3 };
            for (int i = 0; i < 5; i++)
            {
                Assert.That(hits[i].OffsetX, Is.EqualTo((order[i] - 3) * column).Within(1e-5f), $"{i + 1} 発目の列が違う");
                Assert.That(hits[i].OffsetY, Is.GreaterThan(Thief.Movement.HalfHeight), $"{i + 1} 発目が頭上でない");
                Assert.That(hits[i].HalfWidth, Is.LessThan(column * 0.5f), $"{i + 1} 発目が細くない");
                if (i > 0)
                {
                    Assert.That(hits[i].StartTick, Is.GreaterThanOrEqualTo(hits[i - 1].StartTick + hits[i - 1].ActiveTicks), "判定が重なっている");
                }
            }
            Assert.That(hits.Take(4).Sum(h => h.Damage), Is.EqualTo(16), "1〜4 発目が 4 × 4 でない");
            Assert.That(hits[4].Damage, Is.EqualTo(14));
            Assert.That(hits[4].LoneFatal && hits[4].LoneFatalDamage == 80, Is.True, "5 発目だけの FATAL が威力 80 でない");
        }

        [Test]
        public void FiveStabs_AllHit_GiveFourTimesFourPlusFourteen_WithoutFatal()
        {
            // 真上にいる相手には 5 発とも当たる。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 10f, 0f), new Vector3(0f, 12.4f, 0f));
            Edit(world, 1, s => { s.Velocity = Vector3.Zero; return s; });
            Step(world, 1, Press(InputButtons.Attack, 0f, 1f));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirUp));
            // 当たるたびにヒットストップで止まるので、長めに進める。
            for (int t = 0; t < 70; t++)
            {
                // 相手をその場に留める（落ちて外れないように）。
                Edit(world, 1, s =>
                {
                    s.Position = new Vector3(world.GetState(0).Position.X, world.GetState(0).Position.Y + 2.4f, 0f);
                    return s;
                });
                Step(world, 1);
            }
            CharacterState victim = world.GetState(1);
            Assert.That(victim.Combat.ComboCount, Is.EqualTo(5), "5 発とも当たらない");
            Assert.That(victim.Combat.ComboDamage, Is.EqualTo(30), "威力の合計が 4 × 4 + 14 でない");
            Assert.That(victim.Combat.FatalHitCount, Is.EqualTo(0), "全部当てたのに FATAL になった");
        }

        [Test]
        public void FiveStabs_OnlyTheLastHit_IsFatal_WithEightyDamage()
        {
            // 1〜4 発目の間は相手を遠くに置き、5 発目の直前に頭上へ入れる。
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, 10f, 0f), new Vector3(20f, 30f, 0f));
            Step(world, 1, Press(InputButtons.Attack, 0f, 1f));
            int fifth = Thief.Combat.AirUp.Hits[4].StartTick;
            while (world.GetState(0).Action.Ticks < fifth - 1)
            {
                Edit(world, 1, s => { s.Position = new Vector3(20f, 30f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
            }
            Edit(world, 1, s =>
            {
                s.Position = new Vector3(world.GetState(0).Position.X, world.GetState(0).Position.Y + 2.4f, 0f);
                s.Velocity = Vector3.Zero;
                return s;
            });
            Step(world, 3);
            CharacterState victim = world.GetState(1);
            Assert.That(victim.Combat.ComboCount, Is.EqualTo(1), "5 発目だけが当たっていない");
            Assert.That(victim.Combat.FatalHitCount, Is.EqualTo(1), "5 発目だけを当てたのに FATAL にならない");
            Assert.That(victim.Combat.ComboDamage, Is.EqualTo(80), "FATAL の威力が 80 でない");
        }

        [Test]
        public void SmokeBomb_HitsAtTheFeet_LeavesSmoke_AndLeapsBackUp()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(0.9f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground));
            bool smoke = false;
            float maxY = 0f;
            float minX = 0f;
            for (int t = 0; t < 30; t++)
            {
                Step(world, 1);
                smoke |= world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Smoke);
                maxY = System.Math.Max(maxY, world.GetState(0).Position.Y);
                minX = System.Math.Min(minX, world.GetState(0).Position.X);
            }
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "足もとの相手に当たらない");
            Assert.That(world.GetState(1).Combat.ComboDamage, Is.EqualTo(5), "威力が 5 でない");
            Assert.That(smoke, Is.True, "煙が出ない");
            Assert.That(maxY, Is.GreaterThan(GroundY + 2f), "飛び上がらない");
            Assert.That(minX, Is.LessThan(-1f), "斜め後ろへ飛ばない");
        }

        [Test]
        public void TalentSnatch_CopiesTheTargetsQ_ForOneUse_AndTheTargetKeepsIt()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(6f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special1, Vector2.UnitX));
            Step(world, 20);
            CharacterState thief = world.GetState(0);
            Assert.That(thief.Combat.StealKind, Is.EqualTo(StealKind.Skill), "Q をコピーしない");
            Assert.That(thief.Combat.StealSource, Is.EqualTo(1));
            Assert.That(world.GetConfig(0).Combat.Special1.Name, Is.EqualTo("DASH"), "Q がコピーした技にならない");
            Assert.That(ActionController.CanUse(thief, AttackSlot.Special1, world.GetConfig(0).Combat), Is.True, "コピーした Q をすぐ使えない");
            Assert.That(world.GetState(1).Combat.Special1CooldownTicksLeft, Is.EqualTo(0), "相手の Q が使えなくなった（コピーだけのはず）");
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.False, "弾に威力がある");

            Step(world, 1, Press(InputButtons.Special1));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Special1));
            Step(world, 20);
            Assert.That(world.GetState(1).Combat.HasLastAttacker, Is.True, "コピーした DASH が当たらない");
            Step(world, 60);
            thief = world.GetState(0);
            Assert.That(thief.Combat.StealKind, Is.EqualTo(StealKind.None), "使ったのにコピーが残っている");
            Assert.That(world.GetConfig(0).Combat.Special1.Name, Is.EqualTo("TALENT SNATCH"), "使った後に Q が戻らない");
            Assert.That(thief.Combat.Special1CooldownTicksLeft, Is.GreaterThan(600), "使った後に 12 秒のクールタイムが始まらない");
        }

        [Test]
        public void TalentSnatch_Missing_StartsTheCooldown()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special1, Vector2.UnitX));
            Step(world, 5);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.SnatchShot), Is.True, "弾を撃たない");
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.LessThan(0), "飛んでいる間にクールタイムを数えている");
            Step(world, 60);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.SnatchShot), Is.False, "外れた弾が消えない");
            Assert.That(world.GetState(0).Combat.StealKind, Is.EqualTo(StealKind.None));
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.InRange(600, 720), "外れたのにクールタイムが始まらない");
        }

        [Test]
        public void TalentSnatch_StealsAJumpPad_AndPlacesItInFrontTowardTheAim_UpToTwo()
        {
            var pad = new StageZone(StageZoneKind.JumpPad, 5f, 0f, 7f, 0.5f, Vector2.UnitY, 20f);
            var world = World(new SimWorld(TestArenas.Ground(), zones: new[] { pad }), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            for (int k = 0; k < 3; k++)
            {
                Edit(world, 0, s => { s.Combat.Special1CooldownTicksLeft = 0; return s; });
                Step(world, 1, Aimed(InputButtons.Special1, Vector2.Normalize(new Vector2(6f, -0.6f))));
                Step(world, 20);
                Assert.That(world.GetState(0).Combat.StealKind, Is.EqualTo(StealKind.Zone), $"{k + 1} 回目にジャンプ台を奪わない");
                Assert.That(world.GetConfig(0).Combat.Special1.Effect, Is.EqualTo(SkillEffect.PlaceGimmick));
                Step(world, 1, Aimed(InputButtons.Special1, new Vector2(-1f, 0f)));
                Step(world, 20);
                Assert.That(world.GetState(0).Combat.StealKind, Is.EqualTo(StealKind.None), "置いた後に奪ったものが残る");
            }
            WorldObject[] placed = world.Objects.Items.Where(o => o.Kind == WorldObjectKind.PlacedZone).ToArray();
            Assert.That(placed.Length, Is.EqualTo(2), "3 つ目を置いても古いものが消えない");
            Assert.That(placed.All(p => p.Position.X < -1f), Is.True, "照準の方向（左）の目の前に置いていない");
            Assert.That(world.ActiveZones.Count, Is.EqualTo(3), "置いたギミックが効く一覧に入っていない");
            Assert.That(world.ActiveZones.Skip(1).All(z => z.Kind == StageZoneKind.JumpPad), Is.True);
        }

        [Test]
        public void PlacedJumpPad_LaunchesPeople()
        {
            var pad = new StageZone(StageZoneKind.JumpPad, 5f, 0f, 7f, 0.5f, Vector2.UnitY, 20f);
            var world = World(new SimWorld(TestArenas.Ground(), zones: new[] { pad }), new Vector3(0f, GroundY, 0f), new Vector3(-12f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special1, Vector2.Normalize(new Vector2(6f, -0.6f))));
            Step(world, 20);
            Step(world, 1, Aimed(InputButtons.Special1, new Vector2(-1f, 0f)));
            Step(world, 20);
            WorldObject placed = world.Objects.Items.First(o => o.Kind == WorldObjectKind.PlacedZone);
            Edit(world, 1, s => { s.Position = new Vector3(placed.Position.X, GroundY, 0f); return s; });
            float rise = 0f;
            for (int t = 0; t < 10; t++)
            {
                Step(world, 1);
                rise = System.Math.Max(rise, world.GetState(1).Velocity.Y);
            }
            Assert.That(rise, Is.GreaterThan(15f), "置いたジャンプ台が打ち上げない");
        }

        [Test]
        public void TalentSnatch_DoesNotStealTheSea()
        {
            var sea = new StageZone(StageZoneKind.Water, -50f, -20f, 50f, -1f, Vector2.UnitY, 5f, 30f);
            Assert.That(ThiefRules.IsStealable(sea), Is.False, "広い所を覆う海を奪える");
            var wind = new StageZone(StageZoneKind.Gust, 0f, 0f, 4f, 4f, Vector2.UnitX, 10f, 10f);
            Assert.That(ThiefRules.IsStealable(wind), Is.False, "いつも吹いているビル風を奪える");
            var whirl = new StageZone(StageZoneKind.Gust, 0f, 0f, 4f, 4f, Vector2.UnitY, 10f, 10f, new StageZoneTiming(600, 120), Vector2.Zero);
            Assert.That(ThiefRules.IsStealable(whirl), Is.True, "周期のある風（つむじ風）を奪えない");
        }

        [Test]
        public void TalentSnatch_StealsAPitcher_ThatFiresABallTheMomentItIsPlaced()
        {
            StageTrap pitcher = StageTrap.Pitcher(new Vector2(8f, 2f), new Vector2(-6f, 4f), 1200, 0, 0.4f, 10, 18f, 8f, 40);
            var world = World(new SimWorld(TestArenas.Ground(), traps: new[] { pitcher }), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special1, Vector2.Normalize(new Vector2(8f, 1.1f))));
            Step(world, 20);
            Assert.That(world.GetState(0).Combat.StealKind, Is.EqualTo(StealKind.Trap), "ピッチャーマシンを奪わない");
            Step(world, 1, Aimed(InputButtons.Special1, new Vector2(-1f, 0f)));
            Step(world, 4);
            WorldObject placed = world.Objects.Items.First(o => o.Kind == WorldObjectKind.PlacedTrap);
            Assert.That(placed.Position.X, Is.LessThan(-1f), "照準の方向に置いていない");
            WorldObject ball = world.Objects.Items.FirstOrDefault(o => o.Kind == WorldObjectKind.TrapBall);
            Assert.That(ball.Kind, Is.EqualTo(WorldObjectKind.TrapBall), "置いた瞬間にボールを打ち出さない");
            Assert.That(ball.Velocity.X, Is.LessThan(0f), "照準の向き（左）へ打ち出していない");
        }

        [Test]
        public void TalentSnatch_StealsATripwire_ThatFiresAMissileTheMomentItIsPlaced()
        {
            StageTrap wire = StageTrap.Tripwire(new Vector2(8f, 1.5f), new Vector2(20f, 1.7f), new Vector2(7f, 1.6f), Vector2.UnitX, 30f, 14f, 20, 20f,
                30, 120, new Vector2(0.3f, 0.2f));
            var world = World(new SimWorld(TestArenas.Ground(), traps: new[] { wire }), new Vector3(0f, GroundY, 0f), new Vector3(-8f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special1, Vector2.Normalize(new Vector2(7f, 0.7f))));
            Step(world, 20);
            Assert.That(world.GetState(0).Combat.StealKind, Is.EqualTo(StealKind.Trap), "発射装置を奪わない");
            Step(world, 1, Aimed(InputButtons.Special1, new Vector2(-1f, 0f)));
            Step(world, 30);
            Assert.That(world.GetState(1).Combat.HazardHitCount, Is.EqualTo(1), "置いた瞬間に撃ったミサイルが、前の相手に当たらない");
            Assert.That(world.GetState(0).Combat.HazardHitCount, Is.EqualTo(0), "ミサイルが自分に当たった");
        }

        [Test]
        public void Misdirection_LeavesACloneWhereItWas_TurnsInvisible_AndDashesTowardTheAim()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(-1f, 0f)));
            // 長押しで狙える技（AimHold）なので、離したティックに出る。
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.InvisibleTicksLeft, Is.GreaterThan(170), "透明にならない");
            Assert.That(world.HasClone(0), Is.True, "分身が出ない");
            Assert.That(world.GetClone(0).Position.X, Is.EqualTo(0f).Within(0.2f), "分身がその場に残らない");
            Step(world, 12);
            Assert.That(world.GetState(0).Position.X, Is.InRange(-3f, -1.2f), "照準の方向へごく短く突進しない");
            Step(world, 200);
            Assert.That(world.GetState(0).Combat.InvisibleTicksLeft, Is.EqualTo(0), "3 秒で透明が解けない");
            Assert.That(world.HasClone(0), Is.False, "3 秒で分身が消えない");
        }

        [TestCase(InputButtons.Attack, TestName = "攻撃すると透明が解ける")]
        [TestCase(InputButtons.Special2, TestName = "もう一度 E で透明が解ける")]
        [TestCase(InputButtons.Guard, TestName = "ガードすると透明が解ける")]
        public void Misdirection_EndsOnAnotherAction(InputButtons action)
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, Vector2.UnitX));
            Step(world, 20);
            Assert.That(world.GetState(0).Combat.InvisibleTicksLeft, Is.GreaterThan(0));
            Step(world, 1, Press(action));
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.InvisibleTicksLeft, Is.EqualTo(0), "解けない");
            Assert.That(world.HasClone(0), Is.False, "分身が残る");
        }

        [Test]
        public void Misdirection_MovingAndJumping_KeepsInvisible()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, Vector2.UnitX));
            Step(world, 15);
            Step(world, 20, Press(InputButtons.None, 1f));
            Step(world, 1, Press(InputButtons.Jump, 1f));
            Step(world, 10, Press(InputButtons.Jump, 1f));
            Assert.That(world.GetState(0).Combat.InvisibleTicksLeft, Is.GreaterThan(0), "走る・跳ぶで透明が解けた");
        }

        [Test]
        public void Clone_HitByAnAttack_TurnsToSmoke_ButTheThiefStaysInvisible()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(-5f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(1f, 0f)));
            Step(world, 2);
            // 本人は遠くへ離し（攻撃に巻き込まれないように）、相手を分身の隣へ置いて、地上技を出す。
            Edit(world, 0, s => { s.Position = new Vector3(25f, GroundY, 0f); return s; });
            Vector3 clone = world.GetClone(0).Position;
            Edit(world, 1, s => { s.Position = new Vector3(clone.X - 1.6f, GroundY, 0f); s.Facing = 1; return s; });
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.None), Press(InputButtons.Attack, 1f));
            Step(world, 15);
            Assert.That(world.HasClone(0), Is.False, "攻撃が当たっても分身が消えない");
            Assert.That(world.GetState(0).Combat.ClonePopCount, Is.EqualTo(1));
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Smoke && o.Variant == 1), Is.True, "分身が煙にならない");
            Assert.That(world.GetState(0).Combat.InvisibleTicksLeft, Is.GreaterThan(0), "分身が消えたら透明も解けた");
            Assert.That(world.GetState(0).Combat.HasLastAttacker, Is.False, "分身への攻撃が本人に当たった");
        }

        private static readonly BankSpot Bank = new BankSpot(new Vector2(0f, 1.5f), isFirst: true);

        private static SimWorld HeistWorld(float x, int interval = 100000)
        {
            var rules = new MatchRules { CountdownTicks = 0, DurationTicks = 0, BankMoveIntervalTicks = interval, BankNoticeTicks = 10, Seed = 3 };
            var spots = new[] { Bank, new BankSpot(new Vector2(30f, 1.5f)) };
            SimWorld world = World(new SimWorld(TestArenas.Ground(), null, null, rules, null, spots), new Vector3(x, GroundY, 0f),
                new Vector3(-20f, GroundY, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            Step(world, 2);
            return world;
        }

        [TestCase(3, 3)]
        [TestCase(5, 25)]
        [TestCase(10, 70)]
        [TestCase(15, 95)]
        [TestCase(20, 140)]
        public void HeistCoins_FollowTheUsersTable(int seconds, int coins)
        {
            Assert.That(Thief.Skills.Thief.HeistCoins(seconds * 60), Is.EqualTo(coins));
        }

        [Test]
        public void BankHeist_NearTheBank_ChantsInPlace_AndPaysOnTheSecondPress()
        {
            SimWorld world = HeistWorld(4f);
            Assert.That(world.GetState(0).Combat.NearBank, Is.True, "拠点の近くなのに出せない");
            Step(world, 1, Press(InputButtons.Super));
            Assert.That(world.GetState(0).Combat.HeistTicks, Is.GreaterThan(0), "詠唱を始めない");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge - 800), "ゲージを 80% 使わない");
            float x = world.GetState(0).Position.X;
            Step(world, 600, Press(InputButtons.None, 1f));
            Assert.That(world.GetState(0).Position.X, Is.EqualTo(x).Within(0.01f), "詠唱の間に動けた");
            int before = world.GetState(0).Coins.Held + world.GetState(0).Coins.Banked;
            Step(world, 1, Press(InputButtons.Super));
            Step(world, 2);
            CharacterState thief = world.GetState(0);
            Assert.That(thief.Combat.HeistTicks, Is.EqualTo(0), "もう一度 R で止まらない");
            Assert.That(thief.Coins.Held + thief.Coins.Banked - before, Is.EqualTo(70), "10 秒の詠唱で 70 枚にならない");
            Assert.That(thief.Combat.HeistLastPayout, Is.EqualTo(70));
        }

        [Test]
        public void BankHeist_FarFromTheBank_CannotBeUsed()
        {
            SimWorld world = HeistWorld(12f);
            Assert.That(world.GetState(0).Combat.NearBank, Is.False);
            Step(world, 1, Press(InputButtons.Super));
            Step(world, 2);
            Assert.That(world.GetState(0).Combat.HeistTicks, Is.EqualTo(0), "拠点から遠いのに詠唱した");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge), "出せないのにゲージが減った");
        }

        [Test]
        public void BankHeist_HitDuringTheChant_StopsWithoutCoins()
        {
            SimWorld world = HeistWorld(4f);
            Step(world, 1, Press(InputButtons.Super));
            Step(world, 360);
            Edit(world, 1, s => { s.Position = new Vector3(world.GetState(0).Position.X - 1.6f, GroundY, 0f); s.Facing = 1; return s; });
            int before = world.GetState(0).Coins.Held + world.GetState(0).Coins.Banked;
            Step(world, 1, PlayerInput.FromAxes(0f, 0f, InputButtons.None), Press(InputButtons.Attack, 1f));
            Step(world, 30);
            CharacterState thief = world.GetState(0);
            Assert.That(thief.Combat.HeistTicks, Is.EqualTo(0), "のけぞっても詠唱が続く");
            Assert.That(thief.Combat.HeistPayoutCount, Is.EqualTo(0), "のけぞったのにコインを得た");
            Assert.That(thief.Coins.Held + thief.Coins.Banked, Is.LessThanOrEqualTo(before));
        }

        [Test]
        public void BankHeist_EndsAndPays_WhenTheBankMoves()
        {
            SimWorld world = HeistWorld(4f, interval: 400);
            Step(world, 1, Press(InputButtons.Super));
            Step(world, 420);
            CharacterState thief = world.GetState(0);
            Assert.That(thief.Combat.HeistTicks, Is.EqualTo(0), "拠点が移っても詠唱が続く");
            Assert.That(thief.Combat.HeistPayoutCount, Is.EqualTo(1), "拠点が移ったのにコインを得ない");
            Assert.That(thief.Combat.HeistLastPayout, Is.EqualTo(Thief.Skills.Thief.HeistCoins(6 * 60)).Or.EqualTo(Thief.Skills.Thief.HeistCoins(5 * 60)));
        }

        [Test]
        public void Rollback_WithACloneAndAStolenSkill_ReplaysTheSame()
        {
            SimWorld world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(6f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special1, Vector2.UnitX));
            Step(world, 20);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(-1f, 0.2f)));
            Step(world, 1);
            var snapshot = new WorldSnapshot();
            world.SaveTo(snapshot);
            ulong saved = world.Checksum();
            Step(world, 30, Press(InputButtons.Special1, 1f));
            ulong after = world.Checksum();
            world.LoadFrom(snapshot);
            Assert.That(world.Checksum(), Is.EqualTo(saved), "巻き戻した状態が違う");
            Assert.That(world.HasClone(0), Is.True, "巻き戻すと分身がない");
            Step(world, 30, Press(InputButtons.Special1, 1f));
            Assert.That(world.Checksum(), Is.EqualTo(after), "同じ入力で同じ結果にならない");
        }
    }
}
