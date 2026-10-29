using System.Collections.Generic;
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
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// JUGGERNAUT（docs/14 1.7。ユーザーの案、2026-10-29）: 体（大きい・地上で速く空中で遅い・跳ぶのが遅く低い・ガードの延長と歩き）、
    /// 回る地上技（押し続けると続いて上がる・砲塔の先で FATAL）、空中横の砲弾、Q フラック（構え・弾道の段階・爆発）、E 地雷（張り付き・
    /// もう一度 E で爆発）、R メーデー（吊られて飛び去り、選んだ空いた場所へ運ばれる。その間は無敵）。
    /// </summary>
    public class JuggernautTests
    {
        private const float StandardGroundY = 0.91f;
        private static readonly CharacterConfig Juggernaut = CharacterPresets.Juggernaut();
        private static readonly float GroundY = Juggernaut.Movement.HalfHeight + 0.01f;

        private static PlayerInput Press(InputButtons buttons, float x = 0f, float y = 0f)
        {
            return PlayerInput.FromAxes(x, y, buttons);
        }

        private static PlayerInput Aimed(InputButtons buttons, Vector2 aim)
        {
            return PlayerInput.FromAxes(0f, 0f, buttons).WithAim(aim);
        }

        private static List<HitRecord> Step(SimWorld world, int ticks, params PlayerInput[] inputs)
        {
            var hits = new List<HitRecord>();
            var all = new PlayerInput[world.CharacterCount];
            for (int i = 0; i < all.Length && i < inputs.Length; i++)
            {
                all[i] = inputs[i];
            }
            for (int t = 0; t < ticks; t++)
            {
                world.Step(all);
                hits.AddRange(world.LastHits);
            }
            return hits;
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
            world.AddCharacter(CharacterPresets.Juggernaut(), at);
            foreach (Vector3 dummy in dummies)
            {
                world.AddCharacter(new CharacterConfig(), dummy);
            }
            return world;
        }

        // ---------------------------------------------------------------- 体

        [Test]
        public void Preset_IsValid_AndHasItsSkills()
        {
            Assert.DoesNotThrow(() => CharacterPresets.Juggernaut().Validate());
            var standard = new CharacterConfig();
            Assert.That(Juggernaut.Movement.HalfWidth, Is.GreaterThan(standard.Movement.HalfWidth), "体がふつうより大きくない");
            Assert.That(Juggernaut.Movement.HalfHeight, Is.GreaterThanOrEqualTo(standard.Movement.HalfHeight));
            Assert.That(Juggernaut.Movement.RunSpeed, Is.EqualTo(standard.Movement.RunSpeed * 1.5f).Within(0.001f), "地上の速さが +50% でない");
            Assert.That(Juggernaut.Movement.AirRunSpeed, Is.EqualTo(standard.Movement.RunSpeed * 0.5f).Within(0.001f), "空中の速さが -50% でない");
            Assert.That(Juggernaut.Wire.MaxLength, Is.EqualTo(standard.Wire.MaxLength * 1.5f).Within(0.001f), "ワイヤーの射程が 1.5 倍でない");
            Assert.That(Juggernaut.Wire.MaxAttachedSpeed, Is.EqualTo(standard.Wire.MaxAttachedSpeed * 0.5f).Within(0.001f), "ワイヤーの間の速さが -50% でない");
            Assert.That(Juggernaut.Combat.GuardCooldownTicks, Is.EqualTo(standard.Combat.GuardCooldownTicks * 2), "ガードのクールタイムが 2 倍でない");
            Assert.That(Juggernaut.Combat.GuardHoldMaxTicks, Is.EqualTo(240), "ガードの延長が 4 秒でない");
            Assert.That(Juggernaut.Combat.KoAttack, Is.EqualTo(AttackSlot.Ground), "撃破技が地上技でない");
            Assert.That(Juggernaut.Combat.Special1.Effect, Is.EqualTo(SkillEffect.Flak));
            Assert.That(Juggernaut.Combat.Special1MaxCharges, Is.EqualTo(3), "フラックが 3 回まででない");
            Assert.That(Juggernaut.Combat.Special1CooldownTicks, Is.EqualTo(240), "フラックの 1 回分が 4 秒で戻らない");
            Assert.That(Juggernaut.Combat.Special2CooldownTicks, Is.EqualTo(600), "地雷のクールタイムが 10 秒でない");
            Assert.That(Juggernaut.Combat.SuperGaugeCost, Is.EqualTo(100 * CombatTuning.GaugePerPercent), "メーデーが 100% でない");
            Assert.That(Juggernaut.Skills.Juggernaut.CannonHit.Damage, Is.EqualTo(10), "空中横の砲弾の威力が 10 でない");
            Assert.That(Juggernaut.Skills.Juggernaut.FlakHit.Damage, Is.EqualTo(30), "フラックの威力が 30 でない");
            Assert.That(Juggernaut.Skills.Juggernaut.MineHit.Damage, Is.EqualTo(30), "地雷の威力が 30 でない");
            Assert.That(JuggernautRules.IsJuggernaut(Juggernaut), Is.True);
            Assert.That(JuggernautRules.IsJuggernaut(standard), Is.False);
        }

        [Test]
        public void Jump_StartsLate_AndReachesHalfTheHeight()
        {
            float standardPeak = JumpPeak(new CharacterConfig(), StandardGroundY, out _);
            float peak = JumpPeak(Juggernaut, GroundY, out int liftoff);
            Assert.That(liftoff, Is.GreaterThanOrEqualTo(Juggernaut.Movement.JumpStartupTicks), "ジャンプの発生が遅くない");
            Assert.That(peak / standardPeak, Is.EqualTo(0.5f).Within(0.06f), "跳ぶ高さが半分でない");
        }

        // 地上から 1 回跳んだときの高さ（m）と、押してから地面を離れるまでのティック。
        private static float JumpPeak(CharacterConfig config, float groundY, out int liftoff)
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(config, new Vector3(0f, groundY, 0f));
            Step(world, 2);
            float startY = world.GetState(0).Position.Y;
            float peak = startY;
            liftoff = -1;
            for (int t = 0; t < 120; t++)
            {
                Step(world, 1, Press(t < 2 ? InputButtons.Jump : InputButtons.None));
                CharacterState state = world.GetState(0);
                if (liftoff < 0 && state.Position.Y > startY + 0.01f)
                {
                    liftoff = t;
                }
                peak = System.Math.Max(peak, state.Position.Y);
            }
            return peak - startY;
        }

        [Test]
        public void AirSpeed_IsHalfOfStandard_AndGroundSpeedIsHigher()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(Juggernaut, new Vector3(0f, GroundY, 0f));
            Step(world, 60, Press(InputButtons.None, 1f));
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(Juggernaut.Movement.RunSpeed).Within(0.1f), "地上の速さで走らない");

            Edit(world, 0, s =>
            {
                s.Position = new Vector3(0f, 30f, 0f);
                s.Velocity = Vector3.Zero;
                s.Grounded = false;
                return s;
            });
            Step(world, 40, Press(InputButtons.None, 1f));
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(Juggernaut.Movement.AirRunSpeed).Within(0.1f), "空中の速さが半分でない");
        }

        [Test]
        public void Guard_LastsWhileHeld_UpTo4Seconds_AndCanWalkOnTheGround()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(Juggernaut, new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 100, Press(InputButtons.Guard));
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard), "押し続けてもガードが延びない");

            // ガードしながら歩ける（ふだんの地上の速さ。ユーザーの回答、2026-10-29）。
            Step(world, 40, Press(InputButtons.Guard, 1f));
            CharacterState walking = world.GetState(0);
            Assert.That(walking.Action.Kind, Is.EqualTo(ActionKind.Guard));
            Assert.That(walking.Velocity.X, Is.EqualTo(Juggernaut.Movement.RunSpeed).Within(0.2f), "ガードしながらふだんの速さで歩けない");

            // 4 秒を超えると終わり、クールタイム（2 倍）が始まる。
            Step(world, 120, Press(InputButtons.Guard));
            CharacterState ended = world.GetState(0);
            Assert.That(ended.Action.Kind, Is.Not.EqualTo(ActionKind.Guard), "4 秒を超えてもガードが続く");
            Assert.That(ended.Combat.GuardCooldownTicksLeft, Is.GreaterThan(Juggernaut.Combat.GuardCooldownTicks - 30));
        }

        [Test]
        public void Guard_EndsWhenReleased_AfterTheUsualLength()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(Juggernaut, new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Guard));
            Step(world, Juggernaut.Combat.GuardTicks + 2);
            Assert.That(world.GetState(0).Action.Kind, Is.Not.EqualTo(ActionKind.Guard), "離してもガードが終わらない");
        }

        // ---------------------------------------------------------------- 地上技

        [Test]
        public void Spin_ContinuesWhileHeld_RisesSlowly_AndEndsWhenReleased()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(Juggernaut, new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            AttackData spin = Juggernaut.Combat.Ground;
            Step(world, spin.TotalTicks + 20, Press(InputButtons.Attack));
            CharacterState held = world.GetState(0);
            Assert.That(held.Action.Kind, Is.EqualTo(ActionKind.Attack), "押し続けても回り続けない");
            Assert.That(held.Action.Attack, Is.EqualTo(AttackSlot.Ground));
            Assert.That(held.Position.Y, Is.GreaterThan(GroundY + 0.1f), "長く押してもゆっくり上がらない");
            Assert.That(held.Velocity.Y, Is.EqualTo(spin.SustainRiseSpeed).Within(0.01f), "上がる速さが違う");

            // 回りながら左右に動ける。
            Step(world, 20, Press(InputButtons.Attack, 1f));
            Assert.That(world.GetState(0).Velocity.X, Is.GreaterThan(1f), "回りながら動けない");

            Step(world, 2);
            Assert.That(world.GetState(0).Action.Kind, Is.Not.EqualTo(ActionKind.Attack), "離しても回るのが終わらない");
        }

        [Test]
        public void Spin_TurretTip_IsFatal_WithDoubleDamageAndKnockback()
        {
            // 砲塔の先だけが届く距離（体の判定の外）に相手を置く。
            AttackHit tip = Juggernaut.Combat.Ground.Hits[0];
            AttackHit body = Juggernaut.Combat.Ground.Hits[1];
            float distance = (body.HalfWidth + 0.4f + tip.OffsetX + tip.HalfWidth + 0.4f) * 0.5f;
            Assert.That(distance, Is.GreaterThan(body.HalfWidth + 0.4f), "前提: 体の判定の外");
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(distance, StandardGroundY, 0f));
            Step(world, 2);
            List<HitRecord> hits = Step(world, 30, Press(InputButtons.Attack));
            HitRecord first = hits.First(h => h.Victim == 1);
            Assert.That(first.HitIndex, Is.EqualTo(0), "砲塔の先で当たっていない");
            Assert.That(first.IsFatalHit, Is.True, "砲塔の先が当たっても FATAL にならない");
            Assert.That(first.Damage, Is.EqualTo(tip.Damage * 2), "威力が 2 倍でない");
            Assert.That(first.Knockback.Length(), Is.EqualTo(new Vector2(tip.KnockbackX, tip.KnockbackY).Length() * 2f).Within(0.01f), "吹き飛びが 2 倍でない");
        }

        [Test]
        public void Spin_HitsTheSameTargetAgain_EveryRotation()
        {
            // 体に重なる相手は、回るたびに当たり直す（同じ相手に RehitTicks に 1 回）。
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(0.8f, StandardGroundY, 0f));
            Step(world, 2);
            Edit(world, 1, s =>
            {
                s.Combat.GuardCooldownTicksLeft = 9999;
                return s;
            });
            int count = 0;
            for (int t = 0; t < 90; t++)
            {
                // 相手が吹き飛ばないように、毎ティック元の場所へ戻す。
                Edit(world, 1, s =>
                {
                    s.Position = new Vector3(0.8f, StandardGroundY, 0f);
                    s.Velocity = Vector3.Zero;
                    return s;
                });
                count += Step(world, 1, Press(InputButtons.Attack)).Count(h => h.Victim == 1);
            }
            Assert.That(count, Is.GreaterThanOrEqualTo(3), "回り続けても同じ相手に当たり直さない");
        }

        // ---------------------------------------------------------------- 空中横

        [Test]
        public void CannonShot_FiresAStraightShell_AndRecoilsBackward()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, 12f, 0f), new Vector3(14f, 12.3f, 0f));
            Edit(world, 1, s =>
            {
                s.Grounded = false;
                return s;
            });
            Step(world, 1, Press(InputButtons.Attack, 1f));
            List<HitRecord> hits = new List<HitRecord>();
            float vxAfterShot = 0f;
            for (int t = 0; t < 60; t++)
            {
                // 相手を空中に止めておく。
                Edit(world, 1, s =>
                {
                    s.Position = new Vector3(14f, 12.3f, 0f);
                    s.Velocity = Vector3.Zero;
                    return s;
                });
                hits.AddRange(Step(world, 1));
                if (t == Juggernaut.Combat.AirSide.EffectTick)
                {
                    vxAfterShot = world.GetState(0).Velocity.X;
                    Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Shell && o.Variant == JuggernautRules.CannonShell), Is.True,
                        "砲弾が出ていない");
                }
            }
            Assert.That(vxAfterShot, Is.LessThan(-1f), "反動で後ろへ下がらない");
            HitRecord hit = hits.Single(h => h.Victim == 1);
            Assert.That(hit.FromObject, Is.True);
            Assert.That(hit.Damage, Is.EqualTo(10), "砲弾の威力が 10 でない");
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Shell), Is.False, "当たっても砲弾が消えない");
        }

        // ---------------------------------------------------------------- Q フラック

        [Test]
        public void Flak_FirstPressEntersTheStance_SecondPressFires()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 3);
            CharacterState stance = world.GetState(0);
            Assert.That(stance.Combat.FlakStance, Is.True, "Q で構えない");
            Assert.That(stance.Combat.Special1Spent, Is.Zero, "構えるだけで回数を使った");
            Assert.That(world.Objects.Count, Is.Zero, "構えただけで撃った");

            Step(world, 1, Aimed(InputButtons.Special1, new Vector2(1f, 1f)));
            Step(world, 8);
            CharacterState fired = world.GetState(0);
            Assert.That(fired.Combat.Special1Spent, Is.EqualTo(1), "構えの間の Q で撃たない");
            Assert.That(fired.Combat.FlakShotCount, Is.EqualTo(1));
            Assert.That(fired.Combat.FlakStance, Is.True, "撃った後に構えが解けた");
            WorldObject shell = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Shell);
            Assert.That(shell.Variant, Is.EqualTo(JuggernautRules.FlakShell));
            Assert.That(shell.Velocity.X, Is.GreaterThan(0f));
        }

        [Test]
        public void Flak_UpAndDownChangeTheLevel_AndTheLevelIsKeptAfterAKnockout()
        {
            var bounds = new StageBounds(-50f, -20f, 50f, 50f);
            var world = World(new SimWorld(TestArenas.Ground(), bounds), new Vector3(0f, GroundY, 0f));
            JuggernautTuning tuning = Juggernaut.Skills.Juggernaut;
            Step(world, 2);
            Assert.That(JuggernautRules.FlakLevel(world.GetState(0), tuning), Is.EqualTo(2), "初めの段階が強でない");
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            Step(world, 1, Press(InputButtons.None, 0f, 1f));
            Step(world, 1);
            Assert.That(JuggernautRules.FlakLevel(world.GetState(0), tuning), Is.EqualTo(3), "上で弩にならない");
            Step(world, 1, Press(InputButtons.None, 0f, 1f));
            Step(world, 1);
            Assert.That(JuggernautRules.FlakLevel(world.GetState(0), tuning), Is.EqualTo(3), "弩より上へ行った");
            for (int i = 0; i < 5; i++)
            {
                Step(world, 1, Press(InputButtons.None, 0f, -1f));
                Step(world, 1);
            }
            Assert.That(JuggernautRules.FlakLevel(world.GetState(0), tuning), Is.EqualTo(0), "下で弱にならない");
            Assert.That(world.GetState(0).Combat.FlakStance, Is.True, "上下で構えが解けた");

            // 撃破されても、段階はその試合の間覚えておく。
            Edit(world, 0, s =>
            {
                s.Position = new Vector3(0f, -40f, 0f);
                return s;
            });
            Step(world, 1);
            CharacterState respawned = world.GetState(0);
            Assert.That(respawned.Combat.IsWaitingToRespawn, Is.True, "前提: 撃破された");
            Assert.That(JuggernautRules.FlakLevel(respawned, tuning), Is.EqualTo(0), "撃破で弾道の段階を忘れた");
        }

        [TestCase(InputButtons.Jump)]
        [TestCase(InputButtons.Attack)]
        [TestCase(InputButtons.Guard)]
        [TestCase(InputButtons.Wire)]
        [TestCase(InputButtons.None)]
        public void Flak_StanceIsCancelledByOtherActions(InputButtons other)
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.FlakStance, Is.True, "前提: 構えた");
            // None は左右の移動。
            Step(world, 1, other == InputButtons.None ? Press(InputButtons.None, 1f) : Aimed(other, new Vector2(0f, 1f)));
            Assert.That(world.GetState(0).Combat.FlakStance, Is.False, $"{other} で構えが解けない");
        }

        [Test]
        public void Flak_StanceEndsWhenOutOfShots_AndCannotBeUsedInTheAir()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            for (int shot = 0; shot < 3; shot++)
            {
                Step(world, 1, Aimed(InputButtons.Special1, new Vector2(1f, 1f)));
                Step(world, Juggernaut.Combat.Special1.TotalTicks + 2);
            }
            CharacterState spent = world.GetState(0);
            Assert.That(spent.Combat.FlakShotCount, Is.EqualTo(3));
            Assert.That(spent.Combat.FlakStance, Is.False, "回数がなくなっても構えが解けない");

            // 空中では構えられない。
            Edit(world, 0, s =>
            {
                s.Position = new Vector3(0f, 20f, 0f);
                s.Grounded = false;
                s.Combat.Special1Spent = 0;
                s.Combat.Special1CooldownTicksLeft = 0;
                return s;
            });
            Step(world, 1, Press(InputButtons.Special1));
            Assert.That(world.GetState(0).Combat.FlakStance, Is.False, "空中で構えた");
        }

        [Test]
        public void FlakShell_ExplodesOnACharacter_ForThirtyDamage()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(8f, StandardGroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            // ほぼまっすぐ相手の方へ撃つ（相手は近いので、重力で落ちる前に当たる）。
            Step(world, 1, Aimed(InputButtons.Special1, new Vector2(1f, 0.05f)));
            List<HitRecord> hits = Step(world, 40);
            HitRecord hit = hits.Single(h => h.Victim == 1);
            Assert.That(hit.Damage, Is.EqualTo(30), "フラックの威力が 30 でない");
            WorldObject blast = world.Objects.Items.SingleOrDefault(o => o.Kind == WorldObjectKind.Shell);
            Assert.That(blast.Variant == JuggernautRules.ShellBlast || world.Objects.Count == 0, Is.True, "爆発の印になっていない");
        }

        [Test]
        public void PredictFlak_MatchesTheShellsPath()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            var aim = new Vector2(1f, 1f);
            CharacterState before = world.GetState(0);
            JuggernautRules.FlakLaunch(before, Juggernaut, Vector2.Normalize(aim), out Vector2 origin, out Vector2 lead, out Vector2 velocity);
            var points = new Vector2[200];
            int count = JuggernautRules.PredictFlak(world.Collision, origin, lead, velocity, Juggernaut.Skills.Juggernaut.FlakGravity, points,
                out bool hit);
            Assert.That(hit, Is.True, "予測の線が地面に届かない");

            Step(world, 1, Aimed(InputButtons.Special1, aim));
            Step(world, Juggernaut.Combat.Special1.EffectTick + 1);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Shell), Is.True, "前提: 撃った");
            Vector2 previous = default;
            for (int t = 0; t < 300 && world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Shell && o.Variant == JuggernautRules.FlakShell); t++)
            {
                previous = world.Objects.Items.First(o => o.Kind == WorldObjectKind.Shell).Position;
                Step(world, 1);
            }
            Assert.That(Vector2.Distance(previous, points[count - 1]), Is.LessThan(1.5f), "予測の線の終わりと、砲弾が落ちた所が違う");
        }

        // ---------------------------------------------------------------- E 地雷

        [Test]
        public void Mine_SticksToAWall_AndExplodesOnTheSecondPress()
        {
            BoxTerrain terrain = TestArenas.Ground().AddBox(8f, 0f, 9f, 10f);
            // 相手は、地雷が飛ぶ間は後ろにいて、張り付いてから壁の近くへ来る。
            var world = World(terrain, new Vector3(0f, GroundY, 0f), new Vector3(-6f, StandardGroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(1f, 0.1f)));
            Step(world, 1, Aimed(InputButtons.None, new Vector2(1f, 0.1f)));
            Step(world, 30);
            WorldObject mine = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Mine);
            Assert.That(mine.Variant, Is.EqualTo(JuggernautRules.MineOnTerrain), "壁に張り付かない");
            Assert.That(mine.Position.X, Is.EqualTo(8f).Within(0.05f));
            Assert.That(world.GetState(0).Combat.Special2CooldownTicksLeft, Is.EqualTo(CombatState.CooldownWhileActive), "爆発の前にクールタイムが始まった");

            Edit(world, 1, s =>
            {
                s.Position = new Vector3(7f, StandardGroundY, 0f);
                return s;
            });
            List<HitRecord> hits = Step(world, 1, Press(InputButtons.Special2));
            Assert.That(hits.Count(h => h.Victim == 1), Is.EqualTo(1), "もう一度 E で爆発しない");
            Assert.That(hits.Single(h => h.Victim == 1).Damage, Is.EqualTo(30), "地雷の威力が 30 でない");
            CharacterState owner = world.GetState(0);
            Assert.That(owner.Combat.MineBlastCount, Is.EqualTo(1));
            Assert.That(owner.Combat.Special2CooldownTicksLeft, Is.EqualTo(Juggernaut.Combat.Special2CooldownTicks), "爆発の後からクールタイムを数えない");
        }

        [Test]
        public void Mine_AimedAtTheGroundRightBelow_SticksToTheGround()
        {
            // 足もとへ向けて撃つと、出す距離（MineLaunchOffset）の先は地面の中。地面を抜けずに、地面に張り付く。
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(0.5f, -1f)));
            Step(world, 1, Aimed(InputButtons.None, new Vector2(0.5f, -1f)));
            Step(world, 10);
            WorldObject mine = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Mine);
            Assert.That(mine.Variant, Is.EqualTo(JuggernautRules.MineOnTerrain), "足もとへ撃った地雷が地面に張り付かない");
            Assert.That(mine.Position.Y, Is.EqualTo(0f).Within(0.01f), "地面の上に張り付いていない");
        }

        [Test]
        public void FlakShell_AimedIntoTheGround_ExplodesThere()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 1);
            Step(world, 1, Aimed(InputButtons.Special1, new Vector2(0.3f, -1f)));
            Step(world, 10);
            WorldObject blast = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Shell);
            Assert.That(blast.Variant, Is.EqualTo(JuggernautRules.ShellBlast), "地面へ向けた砲弾が地面で爆発しない");
            Assert.That(blast.Position.Y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void Mine_SticksToACharacter_AndMovesWithThem()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f), new Vector3(5f, StandardGroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(1f, 0f)));
            Step(world, 1, Aimed(InputButtons.None, new Vector2(1f, 0f)));
            Step(world, 20);
            WorldObject mine = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Mine);
            Assert.That(mine.Variant, Is.EqualTo(JuggernautRules.MineOnCharacter), "キャラクターに張り付かない");
            Assert.That(mine.Stored, Is.EqualTo(1));

            Vector2 offset = mine.Position - new Vector2(world.GetState(1).Position.X, world.GetState(1).Position.Y);
            Step(world, 30, default, Press(InputButtons.None, 1f));
            CharacterState carrier = world.GetState(1);
            WorldObject moved = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Mine);
            Assert.That(carrier.Position.X, Is.GreaterThan(6f), "前提: 張り付かれた人が動いた");
            Assert.That(Vector2.Distance(moved.Position, new Vector2(carrier.Position.X, carrier.Position.Y) + offset), Is.LessThan(0.01f),
                "張り付いた人と一緒に動かない");
        }

        [Test]
        public void Mine_ThatFliesOutWithoutSticking_StartsTheCooldown()
        {
            var world = World(TestArenas.Ground(), new Vector3(0f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, Aimed(InputButtons.Special2, new Vector2(0f, 1f)));
            Step(world, 1, Aimed(InputButtons.None, new Vector2(0f, 1f)));
            Step(world, Juggernaut.Skills.Juggernaut.MineFlightTicks + 10);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Mine), Is.False, "張り付かずに飛び切っても消えない");
            int cooldown = world.GetState(0).Combat.Special2CooldownTicksLeft;
            Assert.That(cooldown, Is.GreaterThan(0), "消えた後にクールタイムが始まらない");
        }

        // ---------------------------------------------------------------- R メーデー

        private static SimWorld MaydayWorld(out StageBounds bounds)
        {
            bounds = new StageBounds(-40f, -20f, 40f, 40f);
            var world = World(new SimWorld(TestArenas.Ground().AddBox(10f, 0f, 14f, 6f), bounds), new Vector3(-10f, GroundY, 0f),
                new Vector3(-12f, StandardGroundY, 0f));
            Step(world, 2);
            Edit(world, 0, s =>
            {
                s.Combat.Gauge = Juggernaut.Combat.SuperGaugeCost;
                return s;
            });
            return world;
        }

        // R を押して、場所を選べるようになるまで進める。
        private static void LiftOff(SimWorld world)
        {
            Step(world, 1, Press(InputButtons.Super));
            Step(world, Juggernaut.Combat.Super.EffectTick + 1);
            Assert.That(world.GetState(0).Combat.MaydayPhase, Is.EqualTo(JuggernautRules.MaydayLift), "前提: 吊られた");
            Step(world, Juggernaut.Skills.Juggernaut.MaydayLiftTicks);
            Assert.That(world.GetState(0).Combat.MaydayPhase, Is.EqualTo(JuggernautRules.MaydaySelect), "前提: 場所を選べる");
        }

        private static PlayerInput Target(SimWorld world, Vector2 target, InputButtons buttons)
        {
            JuggernautRules.TargetArea(world.Bounds, Juggernaut.Skills.Juggernaut, out Vector2 center, out Vector2 half);
            return JuggernautRules.EncodeTarget(Press(buttons), target, center, half);
        }

        [Test]
        public void Mayday_CarriesTheTankToTheChosenFreeSpot_AndIsUntouchableMeanwhile()
        {
            var world = MaydayWorld(out _);
            LiftOff(world);
            CharacterState away = world.GetState(0);
            Assert.That(away.Combat.IsAway, Is.True);
            Assert.That(away.Combat.IsUntouchable, Is.True, "吊られている間に無敵でない");

            var target = new Vector2(20f, 8f);
            Step(world, 1, Target(world, target, InputButtons.None));
            Step(world, 1, Target(world, target, InputButtons.Attack));
            CharacterState carried = world.GetState(0);
            Assert.That(carried.Combat.MaydayPhase, Is.EqualTo(JuggernautRules.MaydayCarry), "空いた場所を選んでも運ばれない");
            Assert.That(carried.Position.X, Is.EqualTo(target.X).Within(0.4f));
            Assert.That(carried.Position.Y, Is.EqualTo(target.Y).Within(0.4f));

            Step(world, Juggernaut.Skills.Juggernaut.MaydayCarryTicks + 1);
            CharacterState landed = world.GetState(0);
            Assert.That(landed.Combat.MaydayPhase, Is.Zero, "運ばれ終わらない");
            Assert.That(landed.Combat.IsUntouchable, Is.False);
            Assert.That(landed.Position.X, Is.EqualTo(target.X).Within(0.4f), "選んだ場所に降りていない");
        }

        [Test]
        public void Mayday_RejectsASpotInsideTerrain()
        {
            var world = MaydayWorld(out _);
            LiftOff(world);
            var inside = new Vector2(12f, 3f);
            Step(world, 1, Target(world, inside, InputButtons.None));
            Step(world, 1, Target(world, inside, InputButtons.Attack));
            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.MaydayPhase, Is.EqualTo(JuggernautRules.MaydaySelect), "地形の中を選べた");
            Assert.That(state.Combat.MaydayRejectCount, Is.EqualTo(1), "選べなかったことを数えていない（N/A とエラーの音）");
        }

        [Test]
        public void Mayday_ReturnsToTheStart_WhenNothingIsChosenIn10Seconds()
        {
            var world = MaydayWorld(out _);
            Vector3 start = world.GetState(0).Position;
            LiftOff(world);
            Step(world, Juggernaut.Skills.Juggernaut.MaydaySelectTicks + 1);
            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.MaydayPhase, Is.EqualTo(JuggernautRules.MaydayCarry), "10 秒たっても戻らない");
            Assert.That(state.Position.X, Is.EqualTo(start.X).Within(0.1f), "元の場所へ戻らない");
        }

        [Test]
        public void Mayday_IsNotKnockedOut_AndCannotBeHit_OrPickCoins_WhileAway()
        {
            var spawns = new List<CoinSpawnPoint> { new CoinSpawnPoint(new Vector2(-10f, 1.2f), 5) };
            var bounds = new StageBounds(-40f, -20f, 40f, 40f);
            var sim = new SimWorld(TestArenas.Ground(), bounds, null, MatchRules.FreePlay(), spawns);
            var world = World(sim, new Vector3(-10f, GroundY + 5f, 0f), new Vector3(-12f, StandardGroundY, 0f));
            Edit(world, 0, s =>
            {
                s.Combat.MaydayPhase = JuggernautRules.MaydaySelect;
                s.Combat.MaydayFrom = new Vector2(-10f, GroundY + 5f);
                return s;
            });
            Step(world, 1);
            int coinsBefore = world.Coins.Field.Coins.Count;
            Assert.That(coinsBefore, Is.EqualTo(1), "前提: コインが出た");
            Step(world, 60);
            CharacterState state = world.GetState(0);
            Assert.That(state.Coins.Held, Is.Zero, "吊られている間にコインを拾った");
            Assert.That(state.Position.Y, Is.EqualTo(GroundY + 5f).Within(0.001f), "吊られている間に落ちた");
            Assert.That(world.Coins.Field.Coins.Count, Is.EqualTo(coinsBefore));

            // 場所を撃破の範囲の外に置いても、撃破にならない。
            Edit(world, 0, s =>
            {
                s.Position = new Vector3(0f, -60f, 0f);
                return s;
            });
            Step(world, 1);
            Assert.That(world.GetState(0).Combat.IsWaitingToRespawn, Is.False, "吊られている間に撃破された");
        }
    }
}
