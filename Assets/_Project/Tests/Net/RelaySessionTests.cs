using Airerage.Core.Rollback;
using Airerage.Core.Simulation;
using NUnit.Framework;
using Unity.Networking.Transport.Relay;

namespace Airerage.Net.Tests
{
    /// <summary>
    /// 参加コード（Relay。docs/15 T6-10）: 参加コードの書き方のそろえ方と、Relay の割り当てで作ったセッション（ホストはすぐに待機所、
    /// 参加する人は接続中）。Relay のサーバーとは本当にはつながない（割り当ては作り物。サービスへは問い合わせない）。
    /// </summary>
    public sealed class RelaySessionTests
    {
        [Test]
        public void JoinCodes_AreUpperCase_WithoutSpacesOrHyphens()
        {
            Assert.That(JoinCodes.Normalize(" ab12-cd "), Is.EqualTo("AB12CD"));
            Assert.That(JoinCodes.Normalize("XYZ789"), Is.EqualTo("XYZ789"));
            Assert.That(JoinCodes.Normalize(string.Empty), Is.Empty);
            Assert.That(JoinCodes.Normalize(null), Is.Empty);
            Assert.That(JoinCodes.Normalize("AB"), Is.Empty, "短すぎるコード");
            Assert.That(JoinCodes.Normalize("ABCDEFGHIJKLM"), Is.Empty, "長すぎるコード");
            Assert.That(JoinCodes.Normalize("AB!2CD"), Is.Empty, "英数字でない文字");
            Assert.That(JoinCodes.Normalize("ＡＢ１２ＣＤ"), Is.Empty, "全角の文字");
        }

        [Test]
        public void RelayHost_IsInTheLobby_WithTheJoinCode()
        {
            var allocation = new RelayHostAllocation(FakeRelay(), "AB12CD");
            using (OnlineSession host = OnlineSession.HostRelay(allocation, new[] { new CharacterConfig() }, new RollbackSettings()))
            {
                Assert.That(host.IsHost, Is.True);
                Assert.That(host.UsesRelay, Is.True);
                Assert.That(host.State, Is.EqualTo(OnlineSession.SessionState.Lobby));
                Assert.That(host.JoinCode, Is.EqualTo("AB12CD"));
                Assert.That(host.Status, Does.Contain("AB12CD"));
                host.Update(1f / 60f, tick => default);
                Assert.That(host.Status, Does.Contain("AB12CD"), "待機所の文に参加コードがない");
            }
        }

        [Test]
        public void RelayClient_IsConnecting_WithTheJoinCode()
        {
            using (OnlineSession client = OnlineSession.JoinRelay(FakeRelay(), "AB12CD", new RollbackSettings()))
            {
                Assert.That(client.IsHost, Is.False);
                Assert.That(client.UsesRelay, Is.True);
                Assert.That(client.State, Is.EqualTo(OnlineSession.SessionState.Connecting));
                Assert.That(client.JoinCode, Is.EqualTo("AB12CD"));
                client.Update(1f / 60f, tick => default);
                Assert.That(client.State, Is.EqualTo(OnlineSession.SessionState.Connecting), "Relay のサーバーがないのにつながった");
            }
        }

        [Test]
        public void DirectHost_HasNoJoinCode()
        {
            using (OnlineSession host = OnlineSession.Host(0, new[] { new CharacterConfig() }, new RollbackSettings()))
            {
                Assert.That(host.UsesRelay, Is.False);
                Assert.That(host.JoinCode, Is.Empty);
                Assert.That(host.Status, Does.Contain("port"));
            }
        }

        // 作り物の割り当て（どこにもつながらない Relay のサーバー）。
        private static RelayServerData FakeRelay()
        {
            var allocationId = new byte[16];
            for (int i = 0; i < allocationId.Length; i++)
            {
                allocationId[i] = (byte)(i + 1);
            }
            return new RelayServerData("127.0.0.1", 9, allocationId, new byte[255], new byte[255], new byte[64], false);
        }
    }
}
