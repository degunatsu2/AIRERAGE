using System.Collections;
using Airerage.Core.Combat;
using Airerage.Core.Input;
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
    /// ガードとジャストガード（docs/11_combat_plan.md T3-5）。ガード中は「GUARD!」の表示、終わるとクールタイムのゲージ。
    /// F8（DummiesAttack）で攻撃してくるダミーの攻撃を、直前にガードするとジャストガードになり「PARRY!」が出る。
    /// </summary>
    public sealed class GuardViewTests
    {
        [UnityTest]
        public IEnumerator Guard_ShowsTheGuardEffect_ThenTheCooldownGauge()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            GuardEffectView effect = runner.PlayerView.GuardEffect;
            RingGaugeView gauge = runner.PlayerView.GuardGauge;
            Assert.That(effect, Is.Not.Null, "ガードの表示がない");
            Assert.That(gauge, Is.Not.Null, "ガードのクールタイムのゲージがない");
            Assert.That(gauge.Label, Is.EqualTo("GUARD"));
            yield return WaitSeconds(0.5f);

            int ticks = 0;
            runner.InputOverride = new Scripted(_ => ticks++ == 0 ? new SimInput(0, 0, InputButtons.Guard) : default);
            yield return WaitUntil(() => runner.PlayerState.Action.Kind == ActionKind.Guard, 1f);
            yield return null;
            Assert.That(effect.ShowingGuard, Is.True, "ガード中の表示が出ない");

            yield return WaitUntil(() => runner.PlayerState.Action.IsFree, 2f);
            yield return null;
            Assert.That(effect.ShowingGuard, Is.False, "ガードが終わっても表示が残る");
            Assert.That(gauge.IsShowing, Is.True, "クールタイムのゲージが出ない");
            runner.InputOverride = null;
        }

        [UnityTest]
        public IEnumerator JustGuard_AgainstTheAttackingDummy_ShowsParry()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            GuardEffectView effect = runner.PlayerView.GuardEffect;
            yield return WaitSeconds(0.5f);
            float dummyX = runner.GetDummyState(0).Position.X;

            // ダミーの手前まで歩いて止まり、少し待ってからダミーに攻撃させ、その 2 ティック後にガードを押す
            // （ダミーの地上技は 5 ティック目に出るので、ジャストガードの受付 6 ティック以内に当たる）。
            int stopped = -1;
            int tick = 0;
            runner.InputOverride = new Scripted(r =>
            {
                tick++;
                if (stopped < 0)
                {
                    if (r.PlayerState.Position.X < dummyX - 1.7f)
                    {
                        return SimInput.FromAxes(1f, 0f, InputButtons.None);
                    }
                    stopped = tick;
                }
                if (tick == stopped + 20)
                {
                    r.DummiesAttack = true;
                }
                return tick == stopped + 22 ? new SimInput(0, 0, InputButtons.Guard) : default;
            }, runner);

            bool sawParry = false;
            float start = Time.time;
            while (runner.PlayerState.Combat.JustGuardCount == 0 && Time.time - start < 4f)
            {
                yield return null;
            }
            for (int i = 0; i < 5; i++)
            {
                sawParry |= effect.ShowingParry;
                yield return null;
            }
            runner.InputOverride = null;
            runner.DummiesAttack = false;

            Assert.That(runner.PlayerState.Combat.JustGuardCount, Is.EqualTo(1), "ジャストガードにならなかった");
            Assert.That(runner.PlayerState.Combat.ComboCount, Is.EqualTo(0), "攻撃を受けた");
            Assert.That(sawParry, Is.True, "「PARRY!」の表示が出ない");
            Assert.That(runner.GetDummyState(0).Action.Kind, Is.EqualTo(ActionKind.Hitstun), "ダミーに隙ができない");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
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
            private readonly System.Func<SimulationRunner, SimInput> _next;
            private readonly SimulationRunner _runner;

            public Scripted(System.Func<SimulationRunner, SimInput> next, SimulationRunner runner = null)
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
