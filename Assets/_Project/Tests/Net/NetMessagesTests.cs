using System;
using NUnit.Framework;

namespace Degunatsu.Net.Tests
{
    public sealed class NetMessagesTests
    {
        [Test]
        public void Welcome_RoundTrips()
        {
            byte[] data = NetMessages.Welcome(3);
            Assert.That(NetMessages.KindOf(data), Is.EqualTo(NetKind.Welcome));
            Assert.That(NetMessages.ReadWelcome(data), Is.EqualTo(3));
        }

        [Test]
        public void Start_RoundTripsCountAndConfig()
        {
            byte[] data = NetMessages.Start(4, 7, "{\"a\":\"ワイヤー\"}");
            NetMessages.ReadStart(data, out int count, out int match, out string json);
            Assert.That(count, Is.EqualTo(4));
            Assert.That(match, Is.EqualTo(7));
            Assert.That(json, Is.EqualTo("{\"a\":\"ワイヤー\"}"));
        }

        [Test]
        public void Input_KeepsTargetAndPayload()
        {
            byte[] data = NetMessages.Input(5, 2, new byte[] { 9, 8, 7 });
            Assert.That(NetMessages.KindOf(data), Is.EqualTo(NetKind.Input));
            Assert.That(NetMessages.InputMatch(data), Is.EqualTo(5));
            Assert.That(NetMessages.InputTarget(data), Is.EqualTo(2));
            Assert.That(NetMessages.InputPayload(data), Is.EqualTo(new byte[] { 9, 8, 7 }));
        }

        [Test]
        public void Checksum_RoundTrips()
        {
            byte[] data = NetMessages.Checksum(3, 1, 1234567, 0xfedcba9876543210UL);
            NetMessages.ReadChecksum(data, out int match, out int player, out int tick, out ulong checksum);
            Assert.That(match, Is.EqualTo(3));
            Assert.That(player, Is.EqualTo(1));
            Assert.That(tick, Is.EqualTo(1234567));
            Assert.That(checksum, Is.EqualTo(0xfedcba9876543210UL));
        }

        [Test]
        public void BrokenData_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => NetMessages.KindOf(new byte[0]));
            Assert.Throws<FormatException>(() => NetMessages.ReadWelcome(new[] { (byte)NetKind.Welcome }));
            Assert.Throws<FormatException>(() => NetMessages.ReadChecksum(new[] { (byte)NetKind.Checksum, (byte)1, (byte)2 }, out _, out _, out _, out _));
            Assert.Throws<FormatException>(() => NetMessages.InputTarget(NetMessages.Welcome(1)));
        }
    }
}
