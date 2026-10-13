using System;
using System.Collections;
using Degunatsu.Core.Input;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>
    /// 空中戦の検証用シーン（Sandbox_Combat、docs/11_combat_plan.md T3-3）。足場から落ちると撃破されて出現位置に戻り、
    /// ダミーを足場の外へ飛ばすと、当てたプレイヤーの撃破になる。入力は InputOverride で与える。
    /// </summary>
    public sealed class SandboxCombatTests
    {
        private const string SceneName = "Sandbox_Combat";

        [UnityTest]
        public IEnumerator Scene_HasStageBoundsAndThreeDummiesOnPlatforms()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            Assert.That(runner, Is.Not.Null, "SimulationRunner がない");
            Assert.That(runner.enabled, Is.True, "SimulationRunner が無効になっている（設定の不足）");
            Assert.That(runner.HasStageBounds, Is.True, "ステージの範囲がない");
            Assert.That(runner.DummyCount, Is.EqualTo(3));

            yield return WaitSeconds(0.5f);
            Assert.That(runner.PlayerState.Grounded, Is.True, "プレイヤーが足場に立っていない");
            for (int i = 0; i < runner.DummyCount; i++)
            {
                Assert.That(runner.GetDummyState(i).Grounded, Is.True, $"ダミー {i + 1} が足場に立っていない");
                Assert.That(runner.GetDummyState(i).Combat.Falls, Is.EqualTo(0), $"ダミー {i + 1} が落ちた");
            }
        }

        [UnityTest]
        public IEnumerator WalkingOffThePlatform_IsAFall_AndRespawns()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            yield return WaitSeconds(0.5f);
            Vector3 spawn = ToUnity(runner.PlayerState.Position);

            // 左へ歩いて中央の足場から落ち、その後は何もしない。
            int ticks = 0;
            runner.InputOverride = new Scripted(_ => ticks++ < 90 ? SimInput.FromAxes(-1f, 0f, InputButtons.None) : default);
            yield return WaitUntil(() => runner.PlayerState.Combat.Falls > 0, 5f);

            CharacterState state = runner.PlayerState;
            Assert.That(state.Combat.Falls, Is.EqualTo(1), "ステージの外に出なかった");
            Assert.That(state.Combat.Knockouts, Is.EqualTo(0));
            Assert.That(Vector3.Distance(ToUnity(state.Position), spawn), Is.LessThan(0.5f), "出現位置に戻っていない");
            runner.InputOverride = null;
        }

        [UnityTest]
        public IEnumerator GroundAttack_KnocksTheEdgeDummyOut_AndCreditsThePlayer()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            yield return WaitSeconds(0.5f);
            float dummyX = runner.GetDummyState(0).Position.X;

            // 足場の右端の近くにいるダミーの手前まで歩き、地上技を出して、その後は何もしない。
            bool attacked = false;
            runner.InputOverride = new Scripted(r =>
            {
                if (attacked)
                {
                    return default;
                }
                if (r.PlayerState.Position.X < dummyX - 1.6f)
                {
                    return SimInput.FromAxes(1f, 0f, InputButtons.None);
                }
                attacked = true;
                return SimInput.FromAxes(1f, 0f, InputButtons.Attack);
            }, runner);
            yield return WaitUntil(() => runner.GetDummyState(0).Combat.Falls > 0, 6f);

            Assert.That(runner.GetDummyState(0).Combat.Falls, Is.EqualTo(1), "ダミーがステージの外に出なかった");
            Assert.That(runner.PlayerState.Combat.Knockouts, Is.EqualTo(1), "プレイヤーの撃破になっていない");
            runner.InputOverride = null;
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float start = Time.time;
            while (!condition() && Time.time - start < timeoutSeconds)
            {
                yield return null;
            }
        }

        // ティックごとに入力を決める（終わらない。テストの最後に InputOverride を外す）。
        private sealed class Scripted : ITickInputSource
        {
            private readonly Func<SimulationRunner, SimInput> _next;
            private readonly SimulationRunner _runner;

            public Scripted(Func<SimulationRunner, SimInput> next, SimulationRunner runner = null)
            {
                _next = next;
                _runner = runner;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _next(_runner);
                return true;
            }
        }
    }
}
