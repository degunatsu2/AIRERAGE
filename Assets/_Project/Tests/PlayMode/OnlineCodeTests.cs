using System;
using System.Collections;
using System.Threading.Tasks;
using Airerage.Net;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using NUnit.Framework;
using Unity.Networking.Transport.Relay;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 参加コード（docs/15 T6-10）: NETWORK VS の HOST ONLINE で Relay の割り当てと参加コードを作り、待機所に参加コードを出す（コピーできる）。
    /// JOIN WITH CODE で入力欄のコード（大文字にそろえる）で参加する。サービスに問い合わせている間はボタンを押せず、BACK で抜けると
    /// 届いた結果は捨てる。サービスが断ったら理由を出す。サービスには問い合わせない（偽物の Relay）。
    /// </summary>
    public sealed class OnlineCodeTests
    {
        // 問い合わせの結果を、テストが決めたときに返す偽物の Relay。
        private sealed class FakeRelay : IRelayBackend
        {
            public TaskCompletionSource<RelayHostAllocation> Create = new TaskCompletionSource<RelayHostAllocation>();
            public TaskCompletionSource<RelayServerData> Join = new TaskCompletionSource<RelayServerData>();
            public int CreateCalls;
            public int MaxClients;
            public string JoinedCode = string.Empty;

            public Task<RelayHostAllocation> CreateAsync(int maxClients)
            {
                CreateCalls++;
                MaxClients = maxClients;
                return Create.Task;
            }

            public Task<RelayServerData> JoinAsync(string joinCode)
            {
                JoinedCode = joinCode;
                return Join.Task;
            }
        }

        [UnityTest]
        public IEnumerator HostOnline_ShowsTheJoinCode_AndJoinWithCode_ReportsWhyItFailed()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");
            var menu = UnityEngine.Object.FindFirstObjectByType<MenuScreens>();
            menu.ContinueFromTitle();
            yield return null;
            menu.Network.Button.onClick.Invoke();
            yield return null;
            var online = UnityEngine.Object.FindFirstObjectByType<OnlineMenu>();
            var connection = UnityEngine.Object.FindFirstObjectByType<OnlineConnection>();
            var relay = new FakeRelay();
            connection.RelayBackend = relay;
            Assert.That(online.ShownPanel, Is.EqualTo("Connect"));

            // HOST ONLINE: 問い合わせている間はボタンを押せない。
            online.NameField.text = "HOST";
            online.HostOnlineButton.Button.onClick.Invoke();
            yield return null;
            Assert.That(relay.CreateCalls, Is.EqualTo(1), "HOST ONLINE で割り当てを作らない");
            Assert.That(relay.MaxClients, Is.EqualTo(OnlineSession.MaxPlayers - 1), "ホストのほかに 3 人入れる割り当てでない");
            Assert.That(connection.Busy, Is.True);
            Assert.That(online.HostOnlineButton.Button.interactable || online.JoinOnlineButton.Button.interactable
                || online.HostButton.Button.interactable || online.JoinButton.Button.interactable, Is.False, "問い合わせ中にボタンを押せる");
            Assert.That(online.ConnectMessage, Does.Contain("online room"), "問い合わせ中であることを出さない");

            // 割り当てができたら待機所へ。参加コードを出し、コピーできる。
            relay.Create.SetResult(new RelayHostAllocation(FakeServer(), "AB12CD"));
            yield return WaitFor(() => online.ShownPanel == "Waiting", 5f, "割り当てができても待機所を出さない");
            Assert.That(connection.Session.JoinCode, Is.EqualTo("AB12CD"));
            Assert.That(online.WaitingInfo, Does.Contain("JOIN CODE: AB12CD"), "待機所に参加コードを出さない");
            Assert.That(online.CopyCodeButton.gameObject.activeInHierarchy, Is.True, "参加コードのコピーがない");
            online.CopyCodeButton.Button.onClick.Invoke();
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("AB12CD"), "参加コードをコピーしない");

            // 抜けると接続の画面へ。ボタンはまた押せる。
            connection.Leave();
            yield return WaitFor(() => online.ShownPanel == "Connect", 5f, "抜けても接続の画面に戻らない");
            yield return null;
            Assert.That(online.HostOnlineButton.Button.interactable, Is.True, "抜けた後にボタンを押せない");

            // JOIN WITH CODE: コードが空なら問い合わせない。
            online.CodeField.text = string.Empty;
            online.JoinOnlineButton.Button.onClick.Invoke();
            yield return null;
            Assert.That(relay.JoinedCode, Is.Empty, "コードが空なのに問い合わせた");
            Assert.That(online.ConnectMessage, Does.Contain("JOIN CODE"), "コードを入れるように出さない");

            // 小文字・ハイフンでも大文字にそろえて問い合わせる。サービスが断ったら理由を出す。
            online.CodeField.text = "zz-99xy";
            online.JoinOnlineButton.Button.onClick.Invoke();
            yield return null;
            Assert.That(relay.JoinedCode, Is.EqualTo("ZZ99XY"), "コードをそろえて問い合わせない");
            relay.Join.SetException(new RequestFailedException(404, "join code not found"));
            yield return WaitFor(() => !connection.Busy, 5f, "断られても問い合わせ中のまま");
            yield return null;
            Assert.That(connection.Session, Is.Null, "断られたのに接続を作った");
            Assert.That(online.ConnectMessage, Does.Contain("ZZ99XY").And.Contains("join code not found"), "断られた理由を出さない");

            // 問い合わせの間に BACK で抜けたら、届いた結果は捨てる。
            relay.Join = new TaskCompletionSource<RelayServerData>();
            online.CodeField.text = "QW34ER";
            online.JoinOnlineButton.Button.onClick.Invoke();
            yield return null;
            Assert.That(connection.Busy, Is.True);
            connection.Leave();
            relay.Join.SetResult(FakeServer());
            yield return null;
            yield return null;
            Assert.That(connection.Session, Is.Null, "抜けた後に届いた結果でつないだ");
            Assert.That(connection.Busy, Is.False);
        }

        // 作り物の割り当て（どこにもつながらない Relay のサーバー）。
        private static RelayServerData FakeServer()
        {
            var allocationId = new byte[16];
            for (int i = 0; i < allocationId.Length; i++)
            {
                allocationId[i] = (byte)(i + 1);
            }
            return new RelayServerData("127.0.0.1", 9, allocationId, new byte[255], new byte[255], new byte[64], false);
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string message)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > seconds)
                {
                    Assert.Fail(message);
                }
                yield return null;
            }
        }
    }
}
