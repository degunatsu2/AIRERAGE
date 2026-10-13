using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>
    /// 命中の表示（docs/11_combat_plan.md T3-7）: 火花、COMBO の表示、FATAL の虹色の光と「FATAL!」と重い音（ユーザーの要望）、
    /// ワイヤーキャンセルの光。Sandbox_Wire で、入力は InputOverride で与える。
    /// </summary>
    public sealed class HitEffectTests
    {
        [UnityTest]
        public IEnumerator GroundAttack_ShowsTheHitSpark_AndTheCombo()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var combo = Object.FindFirstObjectByType<ComboHud>();
            Assert.That(combo, Is.Not.Null, "コンボの表示がない");
            CharacterView dummyView = DummyView(runner);
            yield return WaitSeconds(0.5f);

            var inputs = new List<SimInput>();
            for (int i = 0; i < 14; i++)
            {
                inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.None));
            }
            inputs.Add(SimInput.FromAxes(1f, 0f, InputButtons.Attack));
            for (int i = 0; i < 50; i++)
            {
                inputs.Add(default);
            }
            runner.InputOverride = new Script(inputs);

            bool sawSpark = false;
            int maxCombo = 0;
            float start = Time.time;
            while (runner.InputOverride != null && Time.time - start < 3f)
            {
                sawSpark |= dummyView.HitSpark.IsPlaying;
                foreach (ComboHud.Entry entry in combo.Entries)
                {
                    maxCombo = Mathf.Max(maxCombo, entry.Active ? entry.Count : 0);
                }
                yield return null;
            }

            Assert.That(sawSpark, Is.True, "火花が出ない");
            Assert.That(maxCombo, Is.EqualTo(2), "地上技の 2 段で COMBO 2 が出ない");
        }

        [UnityTest]
        public IEnumerator FastKoMove_ShowsFatal_WithAHeavySound()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            CharacterView dummyView = DummyView(runner);
            Assert.That(dummyView.FatalBurst.Label, Is.EqualTo("FATAL!"));
            yield return WaitSeconds(0.5f);

            // ダミーの左上を右へ 14 m/s で飛んでいる状態にして、撃破技（下＋攻撃）を出す。チャージの後にちょうどダミーに当たる。
            CharacterState dummy = runner.GetDummyState(0);
            CharacterState player = CharacterState.Spawn(new System.Numerics.Vector3(dummy.Position.X - 4.06f, dummy.Position.Y + 1.87f, 0f), 1);
            player.Velocity = new System.Numerics.Vector3(14f, 0f, 0f);
            bool pressed = false;
            runner.InputOverride = new Scripted(() =>
            {
                if (!pressed)
                {
                    pressed = true;
                    runner.SetPlayerState(player);
                    return SimInput.FromAxes(0f, -1f, InputButtons.Attack);
                }
                return default;
            });

            bool sawFatal = false;
            float start = Time.time;
            while (runner.GetDummyState(0).Combat.FatalHitCount == 0 && Time.time - start < 3f)
            {
                yield return null;
            }
            for (int i = 0; i < 5; i++)
            {
                sawFatal |= dummyView.FatalBurst.IsPlaying;
                yield return null;
            }
            runner.InputOverride = null;

            Assert.That(runner.GetDummyState(0).Combat.FatalHitCount, Is.EqualTo(1), "FATAL にならなかった");
            Assert.That(sawFatal, Is.True, "FATAL の光が出ない");
            Assert.That(audio.History.Contains(SoundId.Fatal), Is.True, "重い打撃音が鳴らない");
        }

        [UnityTest]
        public IEnumerator WireCancel_ShowsTheBurst()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            yield return WaitSeconds(0.5f);

            var inputs = new List<SimInput> { SimInput.FromAxes(-1f, 0f, InputButtons.Attack) };
            for (int i = 0; i < 7; i++)
            {
                inputs.Add(default);
            }
            inputs.Add(SimInput.FromAxes(0f, 1f, InputButtons.Wire));
            runner.InputOverride = new Script(inputs);
            bool sawBurst = false;
            float start = Time.time;
            while (Time.time - start < 1f)
            {
                sawBurst |= runner.PlayerView.WireCancelBurst.IsPlaying;
                yield return null;
            }

            Assert.That(runner.PlayerState.Combat.WireCancelCooldownTicksLeft, Is.GreaterThan(0), "前提: ワイヤーでキャンセルした");
            Assert.That(sawBurst, Is.True, "ワイヤーキャンセルの光が出ない");
        }

        private static CharacterView DummyView(SimulationRunner runner)
        {
            return Object.FindObjectsByType<CharacterView>(FindObjectsSortMode.None).First(v => v != runner.PlayerView);
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

        private sealed class Scripted : ITickInputSource
        {
            private readonly System.Func<SimInput> _next;

            public Scripted(System.Func<SimInput> next)
            {
                _next = next;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _next();
                return true;
            }
        }
    }
}
