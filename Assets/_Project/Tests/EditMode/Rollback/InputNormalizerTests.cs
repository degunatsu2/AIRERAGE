using System;
using System.Collections.Generic;
using System.Numerics;
using Degunatsu.Core.Input;
using Degunatsu.Core.Rollback;
using Degunatsu.Core.Simulation;
using Degunatsu.Core.Tests.Support;
using Degunatsu.Core.Wire;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Rollback
{
    /// <summary>送る前の入力の正規化（docs/10_online_plan.md T2-5）。</summary>
    public class InputNormalizerTests
    {
        [Test]
        public void Aim_IsKeptOnlyOnTheTickTheWireIsPressed()
        {
            PlayerInput aimed = PlayerInput.FromAxes(0f, 0f, InputButtons.Wire).WithAim(new Vector2(1f, 1f));

            PlayerInput pressed = InputNormalizer.Normalize(aimed, default, 2);
            PlayerInput held = InputNormalizer.Normalize(aimed, pressed, 2);
            PlayerInput notPressed = InputNormalizer.Normalize(PlayerInput.FromAxes(0f, 0f, InputButtons.None).WithAim(new Vector2(1f, 0f)), held, 2);

            Assert.That(pressed.HasAim, Is.True, "押したティックには照準を残す");
            Assert.That(held.HasAim, Is.False, "押し続けているティックの照準は使わないので消す");
            Assert.That(notPressed.HasAim, Is.False);
        }

        [TestCase(40, 41, 40, TestName = "小さな揺れは前の値のまま")]
        [TestCase(40, 43, 43, TestName = "大きな変化は新しい値")]
        [TestCase(1, 0, 0, TestName = "離したらすぐ 0")]
        [TestCase(125, 127, 127, TestName = "いっぱいに倒したらすぐ端")]
        public void Move_IgnoresSmallJitter(int previous, int current, int expected)
        {
            var before = new PlayerInput((sbyte)previous, 0, InputButtons.None);
            var input = new PlayerInput((sbyte)current, 0, InputButtons.None);

            Assert.That(InputNormalizer.Normalize(input, before, 2).MoveX, Is.EqualTo(expected));
        }

        [Test]
        public void StrippingAim_DoesNotChangeTheSimulation()
        {
            // 毎ティック照準が変わる（マウスを動かし続ける）入力列で、正規化の前と後のシミュレーションが一致する。
            var random = new Random(5);
            var raw = new List<PlayerInput>();
            for (int t = 0; t < 900; t++)
            {
                InputButtons buttons = (t / 25) % 3 == 0 ? InputButtons.None : InputButtons.Wire;
                if (t % 40 == 7)
                {
                    buttons |= InputButtons.Jump;
                }
                var aim = new Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
                raw.Add(PlayerInput.FromAxes((t / 60) % 2 == 0 ? 1f : -1f, 0f, buttons).WithAim(aim));
            }
            var normalized = new List<PlayerInput>();
            PlayerInput previous = default;
            int rawChanges = 0;
            int normalizedChanges = 0;
            foreach (PlayerInput input in raw)
            {
                PlayerInput n = InputNormalizer.Normalize(input, previous, 0);
                if (normalized.Count > 0)
                {
                    rawChanges += input.Equals(raw[normalized.Count - 1]) ? 0 : 1;
                    normalizedChanges += n.Equals(previous) ? 0 : 1;
                }
                normalized.Add(n);
                previous = n;
            }

            foreach (WireModelType type in new[] { WireModelType.Inertial, WireModelType.Responsive })
            {
                Assert.That(Run(normalized, type), Is.EqualTo(Run(raw, type)), $"{type}: 照準を消すと結果が変わった");
            }
            Assert.That(normalizedChanges, Is.LessThan(rawChanges / 5), "前提: 入力の変化が大きく減る");
        }

        private static ulong Run(List<PlayerInput> inputs, WireModelType type)
        {
            var config = new CharacterConfig();
            config.Wire.ModelType = type;
            var harness = new SimHarness(TestArenas.Playground(), config, new Vector3(-6f, 0.91f, 0f));
            foreach (PlayerInput input in inputs)
            {
                harness.Step(input);
            }
            return StateCodec.Checksum(harness.State);
        }
    }
}
