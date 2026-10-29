using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// SPACE TRAVELER の表示（docs/14 1.1、docs/13 T5-6）: 一覧で選べる、ゆっくり落ちる光、ワープポータルとワープの光と音、
    /// ミサイルの狙いの線・弾・爆発、コズミックレイのビーム、札の回数と「PORTAL x2」。Sandbox_Wire で CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class SpaceTravelerViewTests
    {
        [UnityTest]
        public IEnumerator SpaceTraveler_ShowsThePortals_TheMissile_AndTheRay()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var space = Object.FindFirstObjectByType<SpaceEffectsView>();
            var aimArrow = Object.FindFirstObjectByType<SkillAimView>();
            var objectsView = Object.FindFirstObjectByType<WorldObjectsView>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(space, Is.Not.Null, "SPACE TRAVELER の表示がない");

            runner.CycleCharacter();
            runner.CycleCharacter();
            CharacterConfigAsset character = runner.CycleCharacter();
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.SpaceTravelerName), "一覧に SPACE TRAVELER がない");
            yield return WaitSeconds(0.6f);
            ISimulationFeed feed = runner;

            // 地上技: 斜め後ろへ飛んで、ゆっくり落ちる（足もとに星）。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Attack), default });
            bool floated = false;
            float start = Time.time;
            while (Time.time - start < 1.2f)
            {
                floated |= space.ShowsSlowFall(0);
                yield return null;
            }
            Assert.That(floated, Is.True, "ゆっくり落ちる光を出さない");

            // Q を 2 回: ポータルを 2 つ置く。札は回数を示す。必殺技はポータル 2 つで出せる。
            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = CombatTuning.MaxGauge;
            runner.SetPlayerState(state);
            yield return null;
            Assert.That(hud.SlotOf(2).Label, Is.EqualTo("PORTAL x2"), "ポータルがないときに、必殺技の札で知らせない");
            Assert.That(hud.SlotOf(0).MaxCharges, Is.EqualTo(2), "札にスタックのゲージを出さない");
            Assert.That(hud.SlotOf(0).StackFill, Is.EqualTo(1f).Within(1e-4f), "2 スタックたまっているのに、ゲージが満タンでない");
            Assert.That(hud.SlotOf(1).MaxCharges, Is.EqualTo(1), "1 回だけの技にスタックのゲージを出した");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            yield return WaitSeconds(0.3f);
            Assert.That(hud.SlotOf(0).Label, Does.StartWith("x1"), "札に残りの回数を出さない");
            // 1 つ使うと半分（次の 1 つがたまっている途中の分だけ、少し上）。
            Assert.That(hud.SlotOf(0).StackFill, Is.InRange(0.5f, 0.55f), "1 スタックのときに、ゲージが半分でない");
            state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(state.Position.X + 6f, state.Position.Y + 3f, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            yield return WaitSeconds(0.3f);
            Assert.That(feed.Objects.Items.Count(o => o.Kind == WorldObjectKind.Portal), Is.EqualTo(2), "前提: ポータルを 2 つ置いた");
            Assert.That(audio.History.Contains(SoundId.PortalPlace), Is.True, "ポータルの音が鳴らない");

            // E を長押しで狙いの矢印（ほかの照準の方向の技と同じ水色の矢印。2026-10-29）、離して撃つ。
            var aim = Enumerable.Repeat(new SimInput(0, 0, InputButtons.Special2, 127, 40), 20).ToList();
            aim.Add(new SimInput(0, 0, InputButtons.None, 127, 40));
            runner.InputOverride = new Script(aim);
            bool aiming = false;
            bool flying = false;
            start = Time.time;
            while (Time.time - start < 1f)
            {
                aiming |= aimArrow.ShowsArrow(0);
                flying |= feed.Objects.Items.Any(o => o.Kind == WorldObjectKind.Missile);
                yield return null;
            }
            Assert.That(aiming, Is.True, "長押しで狙いの矢印を出さない");
            Assert.That(flying, Is.True, "前提: ミサイルを撃った");
            Assert.That(audio.History.Contains(SoundId.MissileFire), Is.True, "ミサイルの音が鳴らない");

            // R: ポータルの間のビーム。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            bool beam = false;
            start = Time.time;
            while (Time.time - start < 1f && !beam)
            {
                beam = feed.Objects.Items.Any(o => o.Kind == WorldObjectKind.Beam);
                yield return null;
            }
            Assert.That(beam, Is.True, "コズミックレイのビームを出さない");
            Assert.That(audio.History.Contains(SoundId.CosmicRay), Is.True, "ビームの音が鳴らない");

            // 自分で古いポータルに入ると、もう一方へ移り、光と音。
            yield return WaitSeconds(1.5f);
            WorldObject entry = feed.Objects.Items.First(o => o.Kind == WorldObjectKind.Portal);
            state = runner.PlayerState;
            // 外から入ったことにする（今いるポータルの記録を消して、ポータルの中に置く）。
            state.Position = new System.Numerics.Vector3(entry.Position.X, entry.Position.Y, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            state.Combat.InPortalId = 0;
            state.Combat.WarpCooldownTicks = 0;
            runner.SetPlayerState(state);
            start = Time.time;
            while (Time.time - start < 1f && space.WarpFlashesOf(0) == 0)
            {
                yield return null;
            }
            Assert.That(space.WarpFlashesOf(0), Is.GreaterThan(0), "ワープの光を出さない");
            Assert.That(audio.History.Contains(SoundId.Warp), Is.True, "ワープの音が鳴らない");
            runner.CycleCharacter();
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private sealed class Script : ITickInputSource
        {
            private readonly List<SimInput> _inputs;
            private int _next;

            public Script(List<SimInput> inputs)
            {
                _inputs = inputs;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _next < _inputs.Count ? _inputs[_next] : default;
                return _next++ < _inputs.Count;
            }
        }
    }
}
