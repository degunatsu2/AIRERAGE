using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
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
    /// 特殊攻撃と必殺技の表示（docs/13_character_plan.md T5-4）: 画面の下の札（クールタイム、ゲージ、使えるようになった瞬間）、
    /// 技を出した瞬間の光と音、必殺技の暗転。Sandbox_Wire で、入力は InputOverride で与える。
    /// </summary>
    public sealed class SkillHudTests
    {
        [UnityTest]
        public IEnumerator Special_ShowsTheBurst_AndTheCooldown_ThenFlashesWhenReady()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(hud, Is.Not.Null, "特殊攻撃の表示がない");
            yield return WaitSeconds(0.5f);

            SkillHud.Slot slot = hud.SlotOf(0);
            Assert.That(slot.Shown, Is.True);
            Assert.That(slot.Ready, Is.True, "最初から使える");
            Assert.That(slot.Text, Is.EqualTo(runner.PlayerConfig.Combat.Special1.Name), "使えるときは技の名前");
            Assert.That(slot.ReadyCount, Is.Zero);

            var inputs = new List<SimInput> { new SimInput(0, 0, InputButtons.Special1) };
            for (int i = 0; i < 20; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);
            bool sawBurst = false;
            bool sawCooldown = false;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 2f)
            {
                sawBurst |= runner.PlayerView.SpecialBurst.IsPlaying;
                sawCooldown |= !slot.Ready && slot.Fill < 1f && slot.Text.Contains(".");
                yield return null;
            }
            Assert.That(runner.PlayerState.Combat.Special1CooldownTicksLeft, Is.GreaterThan(0), "前提: 特殊攻撃を出した");
            Assert.That(sawBurst, Is.True, "特殊攻撃の光が出ない");
            Assert.That(sawCooldown, Is.True, "クールタイムの残りが出ない");
            Assert.That(audio.History.Contains(SoundId.SpecialMove), Is.True, "特殊攻撃の音が鳴らない");

            // クールタイムが終わると、札が光って音が鳴る。
            CharacterState state = runner.PlayerState;
            state.Combat.Special1CooldownTicksLeft = 1;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.2f);
            Assert.That(slot.Ready, Is.True);
            Assert.That(slot.ReadyCount, Is.EqualTo(1), "使えるようになったことを示さない");
            Assert.That(audio.History.Contains(SoundId.SkillReady), Is.True, "使えるようになった音が鳴らない");
        }

        [UnityTest]
        public IEnumerator Super_NeedsAFullGauge_ThenDarkensTheScreen()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            var flash = Object.FindFirstObjectByType<SuperFlashHud>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(flash, Is.Not.Null, "必殺技の暗転がない");
            yield return WaitSeconds(0.5f);

            SkillHud.Slot slot = hud.SlotOf(2);
            Assert.That(slot.Ready, Is.False, "ゲージ 0 で使える");
            Assert.That(slot.Text, Is.EqualTo("0"));

            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = runner.PlayerConfig.Combat.MaxGauge;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.1f);
            Assert.That(slot.Ready, Is.True);
            Assert.That(slot.Text, Is.EqualTo("READY!"));
            Assert.That(slot.Fill, Is.EqualTo(1f));
            Assert.That(slot.ReadyCount, Is.EqualTo(1));

            var inputs = new List<SimInput> { new SimInput(0, 0, InputButtons.Super) };
            for (int i = 0; i < 10; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);
            bool sawFlash = false;
            bool sawBurst = false;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 2f)
            {
                sawFlash |= flash.IsFlashing;
                sawBurst |= runner.PlayerView.SuperBurst.IsPlaying;
                yield return null;
            }
            Assert.That(runner.PlayerState.Combat.Gauge, Is.Zero, "前提: 必殺技を出してゲージを使った");
            Assert.That(sawFlash, Is.True, "暗転しない");
            Assert.That(flash.FlashCount, Is.EqualTo(1));
            Assert.That(flash.LastTarget, Is.EqualTo(0), "出した人に光を当てない");
            Assert.That(sawBurst, Is.True, "必殺技の光（SUPER!）が出ない");
            Assert.That(audio.History.Contains(SoundId.SuperStart), Is.True, "必殺技の音が鳴らない");
            Assert.That(slot.Ready, Is.False, "ゲージを使ったのに使えるまま");

            yield return WaitSeconds(1f);
            Assert.That(flash.IsFlashing, Is.False, "暗転が終わらない");
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
