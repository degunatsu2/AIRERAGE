using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// STORM RUNNER（docs/14 1.2、C-3、C-10〜C-13）: 走り続ける地上技、行動の速さの強化（ウインドクローク）と突風、風の帯
    /// （スカイロケット）、キャッスルインジエアー（速さとワイヤー）、空中技、巻き戻し。
    /// </summary>
    public class StormRunnerTests
    {
        private const float GroundY = 0.91f;
        private static readonly CharacterConfig Storm = CharacterPresets.StormRunner();

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

        private static SimWorld StartRun(BoxTerrain terrain, Vector3 position, int facing = 1)
        {
            var world = new SimWorld(terrain);
            world.AddCharacter(CharacterPresets.StormRunner(), position, facing);
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground), "前提: 走り出した");
            return world;
        }

        [Test]
        public void Preset_IsValid()
        {
            Assert.DoesNotThrow(() => CharacterPresets.StormRunner().Validate());
            Assert.That(Storm.Combat.Ground.Run, Is.True);
            Assert.That(Storm.Combat.Special1.Effect, Is.EqualTo(SkillEffect.SpeedBoost));
            Assert.That(Storm.Combat.Special2.Effect, Is.EqualTo(SkillEffect.WindZone));
            Assert.That(Storm.Combat.Super.Effect, Is.EqualTo(SkillEffect.SkyBoost));
            Assert.That(Storm.Combat.SuperGaugeCost, Is.EqualTo(80 * CombatTuning.GaugePerPercent));
            Assert.That(Storm.Combat.KoAttack, Is.EqualTo(AttackSlot.AirDown));
            Assert.That(Storm.Combat.AirNeutral.Hits.Length, Is.EqualTo(3), "つむじ風が 3 段でない");
        }

        [Test]
        public void Run_KeepsGoing_AndAcceleratesUpToTheMaximum()
        {
            SimWorld world = StartRun(TestArenas.Ground(), new Vector3(-90f, GroundY, 0f));
            RunTuning run = Storm.Skills.Run;
            Step(world, 1);
            Assert.That(world.GetState(0).Velocity.X, Is.EqualTo(run.InitialSpeed).Within(0.2f), "初速で走らない");

            Step(world, 60);
            CharacterState state = world.GetState(0);
            Assert.That(state.Action.Kind, Is.EqualTo(ActionKind.Attack), "攻撃の長さで終わった");
            Assert.That(state.Action.RunSpeed, Is.EqualTo(run.InitialSpeed + run.Acceleration).Within(0.2f), "1 秒で 6 m/s 加速しない");
            Assert.That(state.Velocity.X, Is.EqualTo(state.Action.RunSpeed).Within(0.01f));

            // 地上技の入力は無視して走り続ける。
            Step(world, 1, Press(InputButtons.Attack));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground));
            Assert.That(world.GetState(0).Action.RunSpeed, Is.GreaterThan(run.InitialSpeed + run.Acceleration - 0.2f), "走り直した");

            Step(world, 300);
            Assert.That(world.GetState(0).Action.RunSpeed, Is.EqualTo(run.MaxSpeed), "最大の速さで止まらない");
        }

        [Test]
        public void Run_EndsWithAnotherAction_AndThatActionStartsAtOnce()
        {
            SimWorld world = StartRun(TestArenas.Ground(), new Vector3(-50f, GroundY, 0f));
            Step(world, 30);
            Step(world, 1, Press(InputButtons.Jump));
            CharacterState jumped = world.GetState(0);
            Assert.That(jumped.Action.IsFree, Is.True, "ジャンプで走るのが終わらない");
            Assert.That(jumped.Velocity.Y, Is.GreaterThan(5f), "そのティックにジャンプしない");
            Assert.That(jumped.Velocity.X, Is.GreaterThan(5f), "走った勢いが残らない");

            // ガードでも終わる。
            world = StartRun(TestArenas.Ground(), new Vector3(-50f, GroundY, 0f));
            Step(world, 10);
            Step(world, 1, Press(InputButtons.Guard));
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Guard), "ガードで走るのが終わらない");
        }

        [Test]
        public void Run_ReverseInput_TurnsAround_AndRestartsFromTheInitialSpeed()
        {
            SimWorld world = StartRun(TestArenas.Ground(), new Vector3(-50f, GroundY, 0f));
            RunTuning run = Storm.Skills.Run;
            Step(world, 90);
            Assert.That(world.GetState(0).Action.RunSpeed, Is.GreaterThan(run.InitialSpeed + 5f), "前提: 加速した");

            Step(world, 1, Press(InputButtons.None, -1f));
            Assert.That(world.GetState(0).Action.TurnTicksLeft, Is.EqualTo(run.TurnTicks), "反転しない");
            Step(world, run.TurnTicks - 1, Press(InputButtons.None, -1f));
            Assert.That(world.GetState(0).Facing, Is.EqualTo(1), "反転の途中で向きが変わった");
            Step(world, 1, Press(InputButtons.None, -1f));
            CharacterState turned = world.GetState(0);
            Assert.That(turned.Facing, Is.EqualTo(-1), "反転の後に逆を向かない");
            Assert.That(turned.Action.RunSpeed, Is.EqualTo(run.InitialSpeed).Within(0.01f), "速さが初めに戻らない");
            Step(world, 2);
            Assert.That(world.GetState(0).Velocity.X, Is.LessThan(-run.InitialSpeed + 0.5f), "逆向きに走らない");
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground), "反転で走るのが終わった");
        }

        [Test]
        public void Run_HopsAtTheLedge_AndWallJumpsBack()
        {
            // 足場の端（x = 5）で浅く跳ぶ。
            var ledge = new BoxTerrain().AddBox(-20f, -1f, 5f, 0f).AddBox(-60f, -30f, 60f, -29f);
            SimWorld world = StartRun(ledge, new Vector3(0f, GroundY, 0f));
            bool hopped = false;
            for (int t = 0; t < 60 && !hopped; t++)
            {
                Step(world, 1);
                CharacterState s = world.GetState(0);
                hopped = !s.Grounded && s.Position.X > 5f && s.Velocity.Y > 0f;
            }
            Assert.That(hopped, Is.True, "足場の端で跳ばない");
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.Ground), "跳んだら走るのが終わった");

            // 壁（x = 10）にぶつかると、反対側へ浅く跳ぶ。
            var wall = new BoxTerrain().AddBox(-60f, -1f, 60f, 0f).AddBox(10f, 0f, 11f, 20f);
            world = StartRun(wall, new Vector3(0f, GroundY, 0f));
            bool bounced = false;
            for (int t = 0; t < 120 && !bounced; t++)
            {
                Step(world, 1);
                CharacterState s = world.GetState(0);
                bounced = s.Facing == -1 && s.Velocity.X < 0f && s.Velocity.Y > 0f;
            }
            Assert.That(bounced, Is.True, "壁で反対側へ跳ばない");
        }

        [Test]
        public void Run_HitsEnemies_ByTheSpeed_OnceEveryHalfSecond()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(30f, GroundY, 0f), -1);
            Step(world, 2);
            Step(world, 1, Press(InputButtons.Attack));
            HitRecord? hit = null;
            for (int t = 0; t < 200 && hit == null; t++)
            {
                Step(world, 1);
                if (world.LastHits.Count > 0)
                {
                    hit = world.LastHits[0];
                }
            }
            Assert.That(hit.HasValue, Is.True, "走って当たらない");
            RunTuning run = Storm.Skills.Run;
            CharacterState runner = world.GetState(0);
            int bonus = run.BonusPercentAt(runner.Action.RunSpeed);
            Assert.That(bonus, Is.GreaterThan(0), "前提: 初速より速い");
            Assert.That(hit.Value.BonusPercent, Is.EqualTo(bonus), "速さで強くならない");
            Assert.That(hit.Value.Damage, Is.EqualTo(AttackPower.Damage(Storm.Combat.Ground.Hits[0].Damage, 1f, bonus)));
            float perSpeed = Storm.Combat.Ground.Hits[0].Damage / run.InitialSpeed;
            Assert.That(hit.Value.Damage, Is.EqualTo((int)System.MathF.Round(runner.Action.RunSpeed * perSpeed)).Within(1), "ダメージが速さに比例しない");
            Assert.That(runner.Action.Attack, Is.EqualTo(AttackSlot.Ground), "当てたら走るのが終わった");

            // 同じ相手には 0.5 秒（30 ティック）に 1 回まで。
            for (int t = 0; t < 20; t++)
            {
                Edit(world, 1, s => { s.Position = world.GetState(0).Position + new Vector3(0.5f, 0f, 0f); s.Velocity = Vector3.Zero; return s; });
                Step(world, 1);
                Assert.That(world.LastHits.Count, Is.Zero, "0.5 秒たたずに同じ相手にまた当たった");
            }
        }

        [Test]
        public void WindCloak_SpeedsUpMovementAndAttackFrames_ButNotTheHitstop()
        {
            // 歩く速さ。
            float Walk(bool boosted)
            {
                var w = new SimWorld(TestArenas.Ground());
                w.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, GroundY, 0f));
                Step(w, 2);
                if (boosted)
                {
                    Edit(w, 0, s => { s.Combat.SpeedBoostTicksLeft = 1000; return s; });
                }
                Step(w, 60, Press(InputButtons.None, 1f));
                return w.GetState(0).Position.X;
            }
            float plain = Walk(false);
            float fast = Walk(true);
            Assert.That(fast / plain, Is.EqualTo(1.2f).Within(0.05f), "移動が 20% 速くならない");

            // 攻撃のフレームも 20% 速く進む。
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, 50f, 0f));
            Edit(world, 0, s => { s.Combat.SpeedBoostTicksLeft = 1000; return s; });
            Step(world, 1, Press(InputButtons.Attack, 1f));
            Assert.That(world.GetState(0).Action.Attack, Is.EqualTo(AttackSlot.AirSide), "前提: 空中・横");
            Step(world, 10);
            Assert.That(world.GetState(0).Action.Ticks, Is.EqualTo(12), "攻撃のフレームが 20% 速く進まない");

            // ヒットストップはそのまま（止まっている間はフレームが進まない）。
            Edit(world, 0, s => { s.Combat.HitstopTicksLeft = 10; return s; });
            Step(world, 10);
            Assert.That(world.GetState(0).Action.Ticks, Is.EqualTo(12), "ヒットストップの間に進んだ");
            Assert.That(world.GetState(0).Combat.HitstopTicksLeft, Is.Zero, "ヒットストップが速く終わった（10 ティックで 0 になる）");
        }

        [Test]
        public void WindCloak_RecastReleasesAGust_AndTheCooldownStartsAfterTheEffect()
        {
            var world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, 50f, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(6f, 50f, 0f), -1);
            Step(world, 1, Press(InputButtons.Special1));
            CharacterState boosted = world.GetState(0);
            Assert.That(boosted.Combat.SpeedBoostTicksLeft, Is.EqualTo(Storm.Skills.SpeedBoost.DurationTicks), "強化が始まらない");
            Assert.That(boosted.Combat.Special1CooldownTicksLeft, Is.EqualTo(CombatState.CooldownWhileActive), "効果中のクールタイムを止めていない");
            Assert.That(ActionController.CanUse(boosted, AttackSlot.Special1, Storm.Combat), Is.True, "効果中にもう一度出せない");

            Step(world, 20);
            Edit(world, 1, s => { s.Position = new Vector3(6f, world.GetState(0).Position.Y, 0f); s.Velocity = Vector3.Zero; return s; });
            // 照準を右（相手の方）へ向けて、もう一度押す。短く溜めてから撃つ（ユーザーの要望、2026-10-16）。
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Special1, 127, 0));
            Assert.That(world.GetState(0).Action.Recast, Is.True, "もう一度出した技になっていない");
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.EqualTo(Storm.Combat.Special1CooldownTicks), "解いたときにクールタイムが始まらない");
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Gust), Is.False, "溜めずに撃った");
            int waited = 0;
            while (!world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Gust) && waited < 30)
            {
                Step(world, 1);
                waited++;
            }
            Assert.That(waited, Is.InRange(Storm.Skills.SpeedBoost.GustWindupTicks * 3 / 4, Storm.Skills.SpeedBoost.GustWindupTicks), "溜めの長さが違う");
            Assert.That(world.GetState(0).Combat.SpeedBoostTicksLeft, Is.Zero, "もう一度押しても強化が解けない");
            WorldObject gust = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Gust);
            Assert.That(gust.Velocity.X, Is.EqualTo(Storm.Skills.SpeedBoost.GustSpeed).Within(1e-3f), "照準の方向へ撃たない");
            Assert.That(world.GetState(0).Velocity.X, Is.LessThan(-Storm.Skills.SpeedBoost.GustRecoilSpeed * 0.5f), "撃った反動で下がらない");

            float before = world.GetState(1).Velocity.X;
            Step(world, 15);
            Assert.That(world.GetState(1).Velocity.X - before, Is.GreaterThan(Storm.Skills.SpeedBoost.GustPush * 0.5f), "突風が相手を押さない");
            Assert.That(world.GetState(1).Action.Kind, Is.Not.EqualTo(ActionKind.Hitstun), "突風に攻撃の判定がある");
            Step(world, Storm.Skills.SpeedBoost.GustTicks);
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.Gust), Is.False, "届く距離を過ぎても消えない");

            // 照準を上へ向けると、上へ撃って下へ下がる。
            world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, 50f, 0f));
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, 15);
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Special1, 0, 127));
            Step(world, Storm.Skills.SpeedBoost.GustWindupTicks);
            WorldObject up = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Gust);
            Assert.That(up.Velocity.Y, Is.EqualTo(Storm.Skills.SpeedBoost.GustSpeed).Within(1e-3f), "照準の方向（上）へ撃たない");
            Assert.That(world.GetState(0).Velocity.Y, Is.LessThan(-Storm.Skills.SpeedBoost.GustRecoilSpeed * 0.5f), "上へ撃った反動で下がらない");

            // 時間切れでも、効果が終わってからクールタイムが始まる。
            world = new SimWorld(new BoxTerrain());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, 50f, 0f));
            Step(world, 1, Press(InputButtons.Special1));
            Step(world, Storm.Skills.SpeedBoost.DurationTicks + 1);
            Assert.That(world.GetState(0).Combat.Special1CooldownTicksLeft, Is.InRange(1, Storm.Combat.Special1CooldownTicks), "時間切れの後にクールタイムが始まらない");
        }

        [Test]
        public void SkyRocket_BlowsTowardTheAim_ForEveryoneInside()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(20f, GroundY, 0f));
            Step(world, 2);
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Special2, 0, 127));
            Step(world, Storm.Combat.Special2.EffectTick + 1);
            WorldObject zone = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.WindZone);
            Assert.That(zone.Velocity.Y, Is.EqualTo(1f).Within(1e-4f), "照準の方向（上）へ吹かない");
            Assert.That(zone.ExpireTick - zone.SpawnTick, Is.EqualTo(Storm.Skills.WindZone.DurationTicks));

            // 中にいる人（持ち主も、ほかの人も）は上へ飛ばされる。
            Edit(world, 1, s => { s.Position = new Vector3(0.5f, GroundY, 0f); return s; });
            float maxRise = 0f;
            for (int t = 0; t < 40; t++)
            {
                Step(world, 1);
                maxRise = System.Math.Max(maxRise, world.GetState(1).Velocity.Y);
            }
            Assert.That(maxRise, Is.GreaterThan(5f), "風の帯がほかの人を飛ばさない");
            Assert.That(world.GetState(0).Position.Y, Is.GreaterThan(GroundY + 1f), "風の帯が持ち主を飛ばさない");
        }

        [Test]
        public void Castle_SpeedsUp_AndTheWireAttachesToAnythingOrInTheAir()
        {
            var world = new SimWorld(new BoxTerrain().AddBox(-100f, -1f, 100f, 0f));
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, 30f, 0f));
            Edit(world, 0, s => { s.Combat.Gauge = CombatTuning.MaxGauge; return s; });
            Step(world, 1, Press(InputButtons.Super));
            CharacterState state = world.GetState(0);
            Assert.That(state.Combat.SkyBoostTicksLeft, Is.EqualTo(Storm.Skills.SkyBoost.DurationTicks), "強化が始まらない");
            Assert.That(SpeedBoosts.PercentOf(state, Storm), Is.EqualTo(50));
            state.Combat.SpeedBoostTicksLeft = 10;
            Assert.That(SpeedBoosts.PercentOf(state, Storm), Is.EqualTo(70), "ウインドクロークと重なったら足さない");

            // 何もない空中へ撃っても、最大射程の点につながる。
            Step(world, 30);
            Step(world, 1, new PlayerInput(0, 0, InputButtons.Wire, 0, 127));
            for (int t = 0; t < 30 && world.GetState(0).Wire.Phase == WirePhase.Firing; t++)
            {
                Step(world, 1, new PlayerInput(0, 0, InputButtons.Wire, 0, 127));
            }
            Assert.That(world.GetState(0).Wire.Phase, Is.EqualTo(WirePhase.Attached), "空中につながらない");

            // 弾く物（Attachable でない地形）にもつながる。
            WireTuning strong = SpeedBoosts.EffectiveConfig(world.GetState(0), Storm).Wire;
            Assert.That(strong.AttachAnySurface, Is.True);
            Assert.That(Storm.Wire.AttachAnySurface, Is.False, "元の調整値を書き換えた");
            var floor = new BoxTerrain().AddBox(-100f, -1f, 100f, 0f);
            WireShotPreview preview = WireTargeting.PredictShot(new Vector3(0f, 3f, 0f), new Vector2(0f, -1f), 1, strong, floor);
            Assert.That(preview.Result, Is.EqualTo(WireShotResult.Attach), "弾く物につながらない");
        }

        [Test]
        public void Rollback_ReproducesTheRunAndTheWind()
        {
            var scripts = new PlayerInput[2][];
            for (int p = 0; p < 2; p++)
            {
                scripts[p] = new PlayerInput[900];
                for (int t = 0; t < scripts[p].Length; t++)
                {
                    int phase = (t + p * 17) % 150;
                    InputButtons buttons = phase == 2 ? InputButtons.Special1
                        : phase == 5 || phase == 70 ? InputButtons.Attack
                        : phase == 40 ? InputButtons.Special2
                        : phase == 60 ? InputButtons.Special1
                        : phase == 90 ? InputButtons.Jump
                        : phase == 100 ? InputButtons.Super
                        : phase == 110 ? InputButtons.Wire
                        : InputButtons.None;
                    float x = phase > 50 && phase < 56 ? -1f : 0f;
                    scripts[p][t] = new PlayerInput((sbyte)(x * 127), 0, buttons, 40, 100);
                }
            }
            void Setup(SimWorld world)
            {
                for (int i = 0; i < world.CharacterCount; i++)
                {
                    world.SetConfig(i, CharacterPresets.StormRunner());
                    CharacterState state = world.GetState(i);
                    state.Combat.Gauge = CombatTuning.MaxGauge;
                    world.SetState(i, state);
                }
            }
            var network = new PseudoNetwork(scripts, new RollbackSettings(), 5, 2, 0.05, 31, Setup);

            network.RunUntil(700, maxFrames: 4000);

            SimWorld reference = network.Reference(700);
            Assert.That(network.Worlds[0].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Worlds[1].Checksum(), Is.EqualTo(reference.Checksum()));
            Assert.That(network.Sessions[0].RollbackCount, Is.GreaterThan(0), "前提: 巻き戻している");
        }
    }
}
