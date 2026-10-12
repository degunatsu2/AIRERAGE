using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Degunatsu.Net.Tests
{
    /// <summary>
    /// TransportLink を同じ PC の中（127.0.0.1）で実際に通信させて確かめる。ポートは 0（空いているポート）を使う。
    /// </summary>
    public sealed class TransportLinkTests
    {
        private const int TimeoutMs = 5000;

        private TransportLink _host;
        private TransportLink _client;
        private readonly List<TransportLink.Event> _hostEvents = new List<TransportLink.Event>();
        private readonly List<TransportLink.Event> _clientEvents = new List<TransportLink.Event>();

        [SetUp]
        public void SetUp()
        {
            // NUnit はテストクラスの同じインスタンスを使い回すので、前のテストの出来事を消す。
            _hostEvents.Clear();
            _clientEvents.Clear();
            _host = TransportLink.Host(0);
            _client = TransportLink.Connect("127.0.0.1", _host.LocalPort);
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            _host?.Dispose();
        }

        [Test]
        public void Host_ListensOnAssignedPort()
        {
            Assert.That(_host.IsHost, Is.True);
            Assert.That(_host.LocalPort, Is.Not.EqualTo(0));
        }

        [Test]
        public void Connect_BothSidesSeeConnection()
        {
            Connect();
            Assert.That(_host.ConnectedPeerCount, Is.EqualTo(1));
            Assert.That(_client.ConnectedPeerCount, Is.EqualTo(1));
        }

        [Test]
        public void Send_ReliableAndUnreliableArriveInBothDirections()
        {
            Connect();
            int hostPeer = _hostEvents.Find(e => e.Kind == TransportLink.EventKind.Connected).Peer;

            Assert.That(_client.Send(0, new byte[] { 1, 2, 3 }, reliable: false), Is.True);
            Assert.That(_client.Send(0, new byte[] { 4, 5 }, reliable: true), Is.True);
            Assert.That(_host.Send(hostPeer, new byte[] { 6 }, reliable: true), Is.True);

            // 信頼性ありの経路は、分割が必要な大きさでも届く。
            var large = new byte[4000];
            for (int i = 0; i < large.Length; i++)
            {
                large[i] = (byte)(i * 7);
            }
            Assert.That(_host.Send(hostPeer, large, reliable: true), Is.True);

            List<byte[]> atHost = WaitForData(_hostEvents, 2);
            List<byte[]> atClient = WaitForData(_clientEvents, 2);
            Assert.That(atHost, Has.Some.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(atHost, Has.Some.EqualTo(new byte[] { 4, 5 }));
            Assert.That(atClient[0], Is.EqualTo(new byte[] { 6 }));
            Assert.That(atClient[1], Is.EqualTo(large));
        }

        [Test]
        public void Dispose_HostSeesDisconnect()
        {
            Connect();
            _client.Dispose();
            _client = null;

            Pump(() => _hostEvents.Exists(e => e.Kind == TransportLink.EventKind.Disconnected), _host);
            Assert.That(_host.ConnectedPeerCount, Is.EqualTo(0));
        }

        [Test]
        public void Connect_RejectsUnreadableAddress()
        {
            Assert.Throws<ArgumentException>(() => TransportLink.Connect("not an address", 7777).Dispose());
        }

        private void Connect()
        {
            Pump(() => _hostEvents.Exists(e => e.Kind == TransportLink.EventKind.Connected)
                && _clientEvents.Exists(e => e.Kind == TransportLink.EventKind.Connected), _host, _client);
        }

        // 送った側も回す（Unity Transport は、送る側の Update で実際に送り、信頼性ありの経路は確認応答をやり取りする）。
        private List<byte[]> WaitForData(List<TransportLink.Event> events, int count)
        {
            Pump(() => events.FindAll(e => e.Kind == TransportLink.EventKind.Data).Count >= count, _host, _client);
            return events.FindAll(e => e.Kind == TransportLink.EventKind.Data).ConvertAll(e => e.Data);
        }

        // 条件を満たすまで送受信を進める。時間内に満たさなければ失敗。
        private void Pump(Func<bool> done, params TransportLink[] links)
        {
            var watch = Stopwatch.StartNew();
            while (!done())
            {
                if (watch.ElapsedMilliseconds > TimeoutMs)
                {
                    Assert.Fail("時間内に通信が終わらなかった");
                }
                foreach (TransportLink link in links)
                {
                    link.Update(link == _host ? _hostEvents : _clientEvents);
                }
                Thread.Sleep(1);
            }
        }
    }
}
