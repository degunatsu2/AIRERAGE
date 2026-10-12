using System;
using System.Linq;
using Degunatsu.Core.Input;
using Degunatsu.Core.Rollback;
using NUnit.Framework;

namespace Degunatsu.Core.Tests.Rollback
{
    /// <summary>入力のメッセージのバイト列（docs/10_online_plan.md T2-4）。</summary>
    public class InputMessageTests
    {
        [Test]
        public void Bytes_RoundTripEverything()
        {
            var inputs = new[]
            {
                new PlayerInput(-127, 127, InputButtons.Jump | InputButtons.Wire, -90, 90),
                PlayerInput.FromAxes(0.25f, -1f, InputButtons.None),
                default(PlayerInput),
            };
            var message = new InputMessage(3, 1234570, -5, 1234567, 1234500, inputs);

            InputMessage read = InputMessage.FromBytes(message.ToBytes());

            Assert.That(read.Sender, Is.EqualTo(3));
            Assert.That(read.SenderTick, Is.EqualTo(1234570));
            Assert.That(read.Advantage, Is.EqualTo(-5));
            Assert.That(read.Ack, Is.EqualTo(1234567));
            Assert.That(read.StartTick, Is.EqualTo(1234500));
            Assert.That(read.Inputs, Is.EqualTo(inputs));
            Assert.That(message.ToBytes().Length, Is.EqualTo(16 + 6 * inputs.Length), "16バイト＋1つの入力につき6バイト");
        }

        [Test]
        public void Bytes_WithoutInputs_RoundTrip()
        {
            InputMessage read = InputMessage.FromBytes(new InputMessage(0, 0, 0, -1, 2, new PlayerInput[0]).ToBytes());

            Assert.That(read.Inputs, Is.Empty);
            Assert.That(read.Ack, Is.EqualTo(-1));
        }

        [Test]
        public void FromBytes_BrokenData_Throws()
        {
            byte[] bytes = new InputMessage(1, 7, 0, 5, 6, new[] { PlayerInput.FromAxes(1f, 0f, InputButtons.Jump) }).ToBytes();

            byte[] wrongVersion = (byte[])bytes.Clone();
            wrongVersion[0] = 99;
            Assert.Throws<FormatException>(() => InputMessage.FromBytes(wrongVersion));
            Assert.Throws<FormatException>(() => InputMessage.FromBytes(bytes.Take(bytes.Length - 1).ToArray()));
            Assert.Throws<FormatException>(() => InputMessage.FromBytes(new byte[3]));
        }

        [Test]
        public void TooManyInputs_Throws()
        {
            var inputs = new PlayerInput[InputMessage.MaxInputs + 1];

            Assert.Throws<ArgumentException>(() => new InputMessage(0, 0, 0, 0, 0, inputs));
        }

        [Test]
        public void Advantage_IsClampedToTheByteRange()
        {
            Assert.That(new InputMessage(0, 0, 500, 0, 0, new PlayerInput[0]).Advantage, Is.EqualTo(127));
            Assert.That(new InputMessage(0, 0, -500, 0, 0, new PlayerInput[0]).Advantage, Is.EqualTo(-128));
        }
    }
}
