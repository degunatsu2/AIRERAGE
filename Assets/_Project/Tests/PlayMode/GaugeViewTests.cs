using System.Collections;
using System.Collections.Generic;
using Degunatsu.Core.Input;
using Degunatsu.Core.Wire;
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
    /// 円形のゲージ（ユーザーの要望、2026-10-13）: ワイヤーキャンセルのクールタイム（キャラクターの周り、「WIRE CANCEL」）と、
    /// ワイヤーの接続を保てる残り時間（接続点の周り）。入力は InputOverride で与える。
    /// </summary>
    public sealed class GaugeViewTests
    {
        [UnityTest]
        public IEnumerator WireCancel_ShowsTheCooldownGauge_UntilTheCooldownEnds()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            RingGaugeView gauge = runner.PlayerView.WireCancelGauge;
            Assert.That(gauge, Is.Not.Null, "ワイヤーキャンセルのゲージがない");
            Assert.That(gauge.Label, Is.EqualTo("WIRE CANCEL"));
            yield return WaitSeconds(0.5f);

            // 地上技（ダミーのいない左へ）を出し、判定が出てからワイヤーでキャンセルする。
            var inputs = new List<SimInput> { SimInput.FromAxes(-1f, 0f, InputButtons.Attack) };
            for (int i = 0; i < 7; i++)
            {
                inputs.Add(default);
            }
            inputs.Add(SimInput.FromAxes(0f, 1f, InputButtons.Wire));
            runner.InputOverride = new Script(inputs);
            yield return WaitUntil(() => runner.InputOverride == null, 2f);

            Assert.That(runner.PlayerState.Combat.WireCancelCooldownTicksLeft, Is.GreaterThan(0), "前提: ワイヤーでキャンセルした");
            yield return null;
            Assert.That(gauge.IsShowing, Is.True, "ゲージが出ない");
            float first = gauge.Fraction;
            Assert.That(first, Is.GreaterThan(0.9f));

            yield return WaitSeconds(1f);
            Assert.That(gauge.Fraction, Is.LessThan(first - 0.2f), "ゲージが減らない");

            yield return WaitUntil(() => runner.PlayerState.Combat.WireCancelCooldownTicksLeft == 0, 4f);
            yield return null;
            Assert.That(gauge.IsShowing, Is.False, "クールタイムが終わってもゲージが残っている");
        }

        [UnityTest]
        public IEnumerator Attach_ShowsTheRemainingTimeGauge_AroundTheAnchor()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            WireView wireView = Object.FindFirstObjectByType<WireView>();
            RingGaugeView gauge = wireView.AttachGauge;
            Assert.That(gauge, Is.Not.Null, "接続の残り時間のゲージがない");
            yield return WaitSeconds(0.5f);

            // 出現位置の左上のアンカーポイント（-2, 10）へ、マウスの照準で射出して押し続ける。
            SimInput hold = SimInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(new System.Numerics.Vector2(-2f, 9f));
            runner.InputOverride = new Repeat(hold);
            yield return WaitUntil(() => runner.PlayerState.Wire.Phase == WirePhase.Attached, 2f);
            Assert.That(runner.PlayerState.Wire.Phase, Is.EqualTo(WirePhase.Attached), "前提: 接続した");
            yield return null;

            Assert.That(gauge.IsShowing, Is.True, "ゲージが出ない");
            Vector3 anchor = new Vector3(runner.PlayerState.Wire.Anchor.X, runner.PlayerState.Wire.Anchor.Y, 0f);
            Assert.That(Vector2.Distance(gauge.Center, anchor), Is.LessThan(0.01f), "ゲージの中心が接続点にない");
            float first = gauge.Fraction;
            yield return WaitSeconds(1f);
            Assert.That(gauge.Fraction, Is.LessThan(first - 0.2f), "ゲージが減らない");

            // 接続を保てる時間（MaxAttachTicks）が過ぎると外れて、ゲージも消える。
            yield return WaitUntil(() => runner.PlayerState.Wire.Phase != WirePhase.Attached, 4f);
            runner.InputOverride = null;
            yield return null;
            Assert.That(runner.PlayerState.Wire.LastReleaseReason, Is.EqualTo(WireReleaseReason.MaxDuration));
            Assert.That(gauge.IsShowing, Is.False, "外れてもゲージが残っている");
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

        private sealed class Repeat : ITickInputSource
        {
            private readonly SimInput _input;

            public Repeat(SimInput input)
            {
                _input = input;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _input;
                return true;
            }
        }
    }
}
