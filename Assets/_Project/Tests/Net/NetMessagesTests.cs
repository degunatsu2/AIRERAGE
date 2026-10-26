using System;
using Airerage.Core.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Airerage.Net.Tests
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
        public void Start_CompressesTheConfigs_SoFourCharactersFitInOneMessage()
        {
            // キャラクターが 4 体違っても、通信路の 1 つのメッセージの上限（16 KB）に収まる（docs/13 T5-5）。
            string one = JsonUtility.ToJson(new CharacterConfig());
            string json = "[" + one + "," + one.Replace("8", "9") + "," + one.Replace("1", "2") + "," + one.Replace("0", "3") + "]";
            byte[] data = NetMessages.Start(4, 1, json);
            NetMessages.ReadStart(data, out _, out _, out string back);
            Assert.That(back, Is.EqualTo(json));
            Assert.That(data.Length, Is.LessThan(16 * 1024), $"縮めても大きすぎる（JSON {json.Length} 文字）");
            Assert.That(data.Length, Is.LessThan(json.Length / 2), "縮めていない");
            Assert.Throws<FormatException>(() => NetMessages.ReadStart(new byte[] { (byte)NetKind.Start, 1, 1, 9, 9, 9 }, out _, out _, out _));
        }

        [Test]
        public void StagePool_RoundTrips()
        {
            const ulong mask = 0x8000_0000_0000_0105UL;
            byte[] data = NetMessages.StagePool(mask);
            Assert.That(NetMessages.KindOf(data), Is.EqualTo(NetKind.StagePool));
            Assert.That(NetMessages.ReadStagePool(data), Is.EqualTo(mask));
            Assert.Throws<FormatException>(() => NetMessages.ReadStagePool(new[] { (byte)NetKind.StagePool, (byte)1 }));
        }

        [Test]
        public void Pick_RoundTrips()
        {
            byte[] data = NetMessages.Pick(2);
            Assert.That(NetMessages.KindOf(data), Is.EqualTo(NetKind.Pick));
            Assert.That(NetMessages.ReadPick(data), Is.EqualTo(2));
            Assert.That(NetMessages.ReadPick(NetMessages.Pick(-3)), Is.Zero);
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
