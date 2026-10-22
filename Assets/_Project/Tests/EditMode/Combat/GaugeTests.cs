using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Combat
{
    /// <summary>ゲージの獲得（docs/11_combat_plan.md T3-6。docs/01_game_design.md 6.7）。</summary>
    public class GaugeTests
    {
        private const float GroundY = 0.91f;
        private static readonly CombatTuning Combat = new CombatTuning();
        private static readonly PlayerInput Attack = new PlayerInput(0, 0, InputButtons.Attack);
        private static readonly PlayerInput Guard = new PlayerInput(0, 0, InputButtons.Guard);

        // 0 が右向きで地上技を出し、1（左向き）が受ける（guardAt >= 0 なら、0 が攻撃を押してから guardAt ティック後に 1 がガード。
        // 負なら -guardAt ティック前）。最初の命中まで進める。
        private static SimWorld FirstHit(int? guardAt)
        {
            var world = new SimWorld(TestArenas.Ground());
            var config = new CharacterConfig();
            config.Combat.Ground.Hits = new[] { config.Combat.Ground.Hits[0] };
            world.AddCharacter(config, new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(config, new Vector3(1.6f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            int start = guardAt.HasValue && guardAt.Value < 0 ? -guardAt.Value : 0;
            int guardTick = guardAt.HasValue ? start + guardAt.Value : -1;
            for (int t = 0; t < 60; t++)
            {
                PlayerInput a = t == start ? Attack : default;
                PlayerInput g = t == guardTick ? Guard : default;
                world.Step(new[] { a, g });
                if (world.LastHits.Count > 0)
                {
                    return world;
                }
            }
            Assert.Fail("攻撃が届かなかった");
            return world;
        }

        [Test]
        public void Hit_GivesGaugeToBoth()
        {
            SimWorld world = FirstHit(null);

            Assert.That(world.LastHits[0].Result, Is.EqualTo(HitResult.Hit));
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(Combat.Ground.Hits[0].Gauge), "当てた側（判定のゲージ）");
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(Combat.GaugeOnDamaged), "受けた側");
        }

        [Test]
        public void NormalGuard_GivesGaugeToBoth()
        {
            SimWorld world = FirstHit(-4);

            Assert.That(world.LastHits[0].Result, Is.EqualTo(HitResult.Guarded));
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(Combat.Ground.Hits[0].Gauge * Combat.GaugeGuardedPercent / 100),
                "攻撃した側（判定のゲージの割合）");
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(Combat.GaugeOnGuard), "守った側");
        }

        [Test]
        public void JustGuard_GivesALotOfGaugeToTheDefenderOnly()
        {
            SimWorld world = FirstHit(2);

            Assert.That(world.LastHits[0].Result, Is.EqualTo(HitResult.JustGuarded));
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(0), "ジャストガードされた側");
            Assert.That(world.GetState(1).Combat.Gauge, Is.EqualTo(Combat.GaugeOnJustGuard), "守った側");
        }

        [Test]
        public void HitGauge_IsSetPerAttack()
        {
            // 技ごとのゲージ（2026-10-22）: 判定のゲージを変えると、当てた側が得る量が変わる。
            var world = new SimWorld(TestArenas.Ground());
            var config = new CharacterConfig();
            config.Combat.Ground.Hits = new[] { config.Combat.Ground.Hits[0] };
            config.Combat.Ground.Hits[0].Gauge = 123;
            world.AddCharacter(config, new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.6f, GroundY, 0f), -1);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Attack, default });
            for (int t = 0; t < 30 && world.LastHits.Count == 0; t++)
            {
                world.Step(new PlayerInput[2]);
            }

            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(123));
        }

        [Test]
        public void SuperHits_GiveNoGauge()
        {
            // 必殺技の判定はゲージが 0（どのキャラクターも。続けて必殺技を出せないように）。
            foreach (CharacterConfig config in new[]
            {
                CharacterPresets.Standard(), CharacterPresets.RiskTaker(), CharacterPresets.StormRunner(), CharacterPresets.SpaceTraveler(),
                CharacterPresets.PastryChef(), CharacterPresets.Solderer(),
            })
            {
                foreach (AttackHit hit in config.Combat.Super.Hits)
                {
                    Assert.That(hit.Gauge, Is.Zero, config.Combat.Super.Name);
                }
            }
            var skills = new Airerage.Core.Skills.SkillTuning();
            Assert.That(skills.Beam.Hit.Gauge, Is.Zero, "コズミックレイ");
            Assert.That(skills.TeaParty.Hit.Gauge, Is.Zero, "ティーパーティ");
        }

        [Test]
        public void Gauge_IsCappedAtTheMaximum()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            world.AddCharacter(new CharacterConfig(), new Vector3(1.6f, GroundY, 0f), -1);
            CharacterState attacker = world.GetState(0);
            attacker.Combat.Gauge = CombatTuning.MaxGauge - 1;
            world.SetState(0, attacker);
            world.Step(new PlayerInput[2]);
            world.Step(new[] { Attack, default });
            for (int t = 0; t < 30 && world.LastHits.Count == 0; t++)
            {
                world.Step(new PlayerInput[2]);
            }

            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(CombatTuning.MaxGauge));
        }

        [Test]
        public void Gauge_IsKept_WhenKnockedOut()
        {
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-20f, -10f, 20f, 30f));
            world.AddCharacter(new CharacterConfig(), new Vector3(0f, GroundY, 0f), 1);
            CharacterState state = world.GetState(0);
            state.Combat.Gauge = 42;
            state.Position = new Vector3(0f, -15f, 0f);
            world.SetState(0, state);

            world.Step(new PlayerInput[1]);

            Assert.That(world.GetState(0).Combat.Falls, Is.EqualTo(1), "前提: 撃破された");
            Assert.That(world.GetState(0).Combat.Gauge, Is.EqualTo(42));
        }
    }
}
