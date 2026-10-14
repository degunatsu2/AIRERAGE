using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Input;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 仮の BGM と効果音（ユーザーの要望、2026-10-13）。BGM が繰り返し鳴る設定になっていて、攻撃を出して当てると効果音を鳴らす。
    /// バッチモードには音を出す機器がないので、鳴らしたかどうかは GameAudio.History で確かめる（音そのものは耳で確かめる）。
    /// </summary>
    public sealed class AudioTests
    {
        [UnityTest]
        public IEnumerator Bgm_IsSetUpToLoop_AndF9CanStopIt()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(audio, Is.Not.Null, "GameAudio がない");
            Assert.That(audio.BgmSource.clip, Is.Not.Null);
            Assert.That(audio.BgmSource.clip.length, Is.GreaterThan(10f));
            Assert.That(audio.BgmSource.loop, Is.True);
            Assert.That(audio.BgmEnabled, Is.True, "BGM を鳴らす設定になっていない");

            audio.BgmOn = false;
            Assert.That(audio.BgmEnabled, Is.False);
        }

        [UnityTest]
        public IEnumerator AttackingTheDummy_PlaysSwingAndHit()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            float start = Time.time;
            while (Time.time - start < 0.5f)
            {
                yield return null;
            }

            var inputs = new List<SimInput>();
            for (int i = 0; i < 12; i++)
            {
                inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.None));
            }
            inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.Attack));
            for (int i = 0; i < 40; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);
            start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                yield return null;
            }

            Assert.That(audio.History.Contains(SoundId.Swing), Is.True, "攻撃の音が鳴らない");
            Assert.That(audio.History.Contains(SoundId.Hit), Is.True, "当たった音が鳴らない");
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
