using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Physics;
using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using NUnit.Framework;

namespace Airerage.Core.Tests.Skills
{
    /// <summary>
    /// 技の照準（CombatState.SkillAim）とオンラインの入力の正規化（InputNormalizer）: 照準の方向に出す技（突風、風の帯、ミサイル）が、
    /// オンライン（照準を送るのは技のボタンを押している間とワイヤーを押したティックだけ）でも向きではなく照準の方向に出る
    /// （ユーザーの指摘、2026-10-16。前はワイヤーを押したティックの照準しか残さず、向きの方向に出ていた）。
    /// 正規化の前と後で、シミュレーションの結果が変わらない。
    /// </summary>
    public class SkillAimTests
    {
        private const float GroundY = 0.91f;
        private const int Hysteresis = 2;

        // 正規化の前と後で同じ: オンラインと同じく、1 つ前の正規化した入力と比べる。
        private sealed class Player
        {
            private PlayerInput _previous;

            public PlayerInput Next(PlayerInput raw, int hysteresis)
            {
                _previous = InputNormalizer.Normalize(raw, _previous, hysteresis);
                return _previous;
            }
        }

        // マウスの照準（毎ティック、照準を送る。正規化すると、技のボタンを押している間とワイヤーを押したティックのほかは消える）。
        private static PlayerInput Aimed(InputButtons buttons, float aimX, float aimY)
        {
            return PlayerInput.FromAxes(0f, 0f, buttons).WithAim(new Vector2(aimX, aimY));
        }

        private static void Step(SimWorld world, Player player, int ticks, PlayerInput raw)
        {
            for (int t = 0; t < ticks; t++)
            {
                world.Step(new[] { player.Next(raw, Hysteresis) });
            }
        }

        [Test]
        public void Normalizer_KeepsTheAim_OnlyWhileASkillButtonIsHeld_OrTheWireIsPressed()
        {
            var player = new Player();
            Assert.That(player.Next(Aimed(InputButtons.None, 1f, 0f), Hysteresis).HasAim, Is.False, "何も押していないのに照準を残した");
            foreach (InputButtons skill in new[] { InputButtons.Special1, InputButtons.Special2, InputButtons.Super })
            {
                Assert.That(player.Next(Aimed(skill, 0f, 1f), Hysteresis).HasAim, Is.True, $"{skill} を押したティックの照準を消した");
                Assert.That(player.Next(Aimed(skill, -1f, 0f), Hysteresis).HasAim, Is.True, $"{skill} を押している間の照準を消した");
                Assert.That(player.Next(Aimed(InputButtons.None, 1f, 1f), Hysteresis).HasAim, Is.False, $"{skill} を離した後も照準を残した");
            }
            Assert.That(player.Next(Aimed(InputButtons.Attack, 1f, 1f), Hysteresis).HasAim, Is.False, "通常攻撃は照準を使わない");
            Assert.That(player.Next(Aimed(InputButtons.Wire, 1f, 1f), Hysteresis).HasAim, Is.True, "ワイヤーを押したティックの照準を消した");
            Assert.That(player.Next(Aimed(InputButtons.Wire, 1f, 1f), Hysteresis).HasAim, Is.False, "ワイヤーを押し続けているティックの照準は使わない");
        }

        [Test]
        public void Online_WindZone_BlowsTowardTheAim_NotTheFacing()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, GroundY, 0f), 1);
            var player = new Player();
            Step(world, player, 2, Aimed(InputButtons.None, -1f, 1f));
            // E を押してすぐ離す（照準は左上。キャラクターは右を向いている）。
            Step(world, player, 1, Aimed(InputButtons.Special2, -1f, 1f));
            Step(world, player, 20, Aimed(InputButtons.None, 1f, 0f));
            WorldObject zone = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.WindZone);
            Assert.That(zone.Velocity.X, Is.LessThan(-0.6f), "照準（左上）ではなく向き（右）へ吹いた");
            Assert.That(zone.Velocity.Y, Is.GreaterThan(0.6f), "照準（左上）ではなく向き（右）へ吹いた");
        }

        [Test]
        public void Online_Missile_FliesTowardTheAimWhileHeld()
        {
            var world = new SimWorld(new BoxTerrain(), new StageBounds(-200f, -200f, 200f, 200f));
            world.AddCharacter(CharacterPresets.SpaceTraveler(), new Vector3(0f, 50f, 0f), 1);
            var player = new Player();
            // E を押したまま照準を左上へ動かし、離して撃つ（離した後の照準は右。使わない）。
            Step(world, player, 10, Aimed(InputButtons.Special2, 1f, 0f));
            Step(world, player, 10, Aimed(InputButtons.Special2, -1f, 1f));
            Step(world, player, 6, Aimed(InputButtons.None, 1f, 0f));
            WorldObject missile = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.Missile);
            Assert.That(missile.Velocity.X, Is.LessThan(-1f), "最後に狙った方向（左上）へ撃たない");
            Assert.That(missile.Velocity.Y, Is.GreaterThan(1f), "最後に狙った方向（左上）へ撃たない");
        }

        [Test]
        public void Online_Gust_ReleasesTowardTheAim_NotTheFacing()
        {
            var world = new SimWorld(TestArenas.Ground());
            world.AddCharacter(CharacterPresets.StormRunner(), new Vector3(0f, GroundY, 0f), 1);
            var player = new Player();
            Step(world, player, 2, Aimed(InputButtons.None, 1f, 0f));
            Step(world, player, 1, Aimed(InputButtons.Special1, 1f, 0f));
            Step(world, player, 20, Aimed(InputButtons.None, 1f, 0f));
            Assert.That(world.GetState(0).Combat.SpeedBoostTicksLeft, Is.GreaterThan(0), "前提: 風をまとった");
            // 効果中に Q を押してすぐ離す（照準は左。キャラクターは右を向いている）。突風は溜めの後に出る。
            Step(world, player, 1, Aimed(InputButtons.Special1, -1f, 0f));
            Step(world, player, 30, Aimed(InputButtons.None, 1f, 0f));
            WorldObject gust = world.Objects.Items.First(o => o.Kind == WorldObjectKind.Gust);
            Assert.That(gust.Velocity.X, Is.LessThan(0f), "照準（左）ではなく向き（右）へ撃った");
        }

        [Test]
        public void TappedDuringAnotherAttack_FiresTowardTheAimWhileHeld()
        {
            // 攻撃の終わりぎわに E を押してすぐ離す（先行入力）。技が出るのは離した後なので、照準は押していたときのもの。
            var world = new SimWorld(TestArenas.Ground());
            CharacterConfig storm = CharacterPresets.StormRunner();
            world.AddCharacter(storm, new Vector3(0f, 10f, 0f), 1);
            var player = new Player();
            Step(world, player, 1, Aimed(InputButtons.Attack, 1f, 0f));
            Assert.That(world.GetState(0).Action.Kind, Is.EqualTo(ActionKind.Attack), "前提: 攻撃を出した");
            int left = ActionController.LengthOf(world.GetState(0), storm) - world.GetState(0).Action.Ticks;
            Step(world, player, Math.Max(0, left - 4), Aimed(InputButtons.None, 1f, 0f));
            Step(world, player, 2, Aimed(InputButtons.Special2, 0f, 1f));
            Assert.That(world.Objects.Items.Any(o => o.Kind == WorldObjectKind.WindZone), Is.False, "前提: 押している間はまだ出ない");
            Step(world, player, 30, Aimed(InputButtons.None, 1f, 0f));
            WorldObject zone = world.Objects.Items.Single(o => o.Kind == WorldObjectKind.WindZone);
            Assert.That(zone.Velocity.Y, Is.GreaterThan(0.9f), "押していたときの照準（上）へ吹かない");
        }

        [Test]
        public void Normalizing_DoesNotChangeTheSimulation_WithAimedSkills()
        {
            // 毎ティック照準が変わる（マウスを動かし続ける）入力で、技を押す・ためる・攻撃中に押す。正規化の前と後で結果が同じ。
            foreach (Func<CharacterConfig> preset in new Func<CharacterConfig>[] { CharacterPresets.StormRunner, CharacterPresets.SpaceTraveler })
            {
                var random = new Random(11);
                var raw = new List<PlayerInput>();
                for (int t = 0; t < 1200; t++)
                {
                    InputButtons buttons = InputButtons.None;
                    int phase = t % 200;
                    if (phase < 2 || (phase >= 60 && phase < 90) || phase == 130 || phase == 131)
                    {
                        buttons |= (t / 200) % 2 == 0 ? InputButtons.Special2 : InputButtons.Special1;
                    }
                    if (phase == 120 || phase == 160)
                    {
                        buttons |= InputButtons.Attack;
                    }
                    if (phase == 40 || phase == 170)
                    {
                        buttons |= InputButtons.Jump;
                    }
                    var aim = new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
                    raw.Add(PlayerInput.FromAxes((t / 90) % 2 == 0 ? 0.6f : -0.6f, 0f, buttons).WithAim(aim));
                }
                var player = new Player();
                List<PlayerInput> normalized = raw.Select(input => player.Next(input, 0)).ToList();
                Assert.That(Run(preset, normalized), Is.EqualTo(Run(preset, raw)), "照準を消すと結果が変わった");
            }
        }

        private static ulong Run(Func<CharacterConfig> preset, List<PlayerInput> inputs)
        {
            var world = new SimWorld(TestArenas.Ground(), new StageBounds(-200f, -200f, 200f, 200f));
            world.AddCharacter(preset(), new Vector3(0f, GroundY, 0f));
            world.AddCharacter(new CharacterConfig(), new Vector3(3f, GroundY, 0f), -1);
            foreach (PlayerInput input in inputs)
            {
                world.Step(new[] { input, default });
            }
            return world.Checksum();
        }
    }
}
