using System.Collections;
using Degunatsu.Net;
using Degunatsu.Runtime.Presentation;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>
    /// Sandbox_Online シーンを再生して、ホストが1人で試合を始められ、シミュレーションと表示がつながっていることを確かめる
    /// （docs/10_online_plan.md T2-7。複数人での確認は Multiplayer Play Mode で手動で行う）。
    /// </summary>
    public sealed class SandboxOnlineSmokeTests
    {
        private const string SceneName = "Sandbox_Online";

        // 地面（上面 y = 0）に立っているときの中心の高さ: 半分の高さ 0.9m + 地面との隙間 0.01m。
        private const float StandingY = 0.91f;

        [UnityTest]
        public IEnumerator HostAlone_StartsAndSimulates()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;

            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            Assert.That(runner, Is.Not.Null, "OnlineRunner がない");
            Assert.That(runner.enabled, Is.True, "OnlineRunner が無効になっている（設定の不足）");
            CharacterView[] views = Object.FindObjectsByType<CharacterView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(views.Length, Is.EqualTo(OnlineSession.MaxPlayers), "キャラクターの表示が4人分ない");
            foreach (CharacterView view in views)
            {
                Assert.That(view.gameObject.activeSelf, Is.False, "試合の前にキャラクターが表示されている");
            }

            Assert.That(runner.StartHost(0), Is.True, "待ち受けを始められない");
            Assert.That(runner.Session.State, Is.EqualTo(OnlineSession.SessionState.Lobby));
            runner.StartMatch();

            float start = Time.time;
            while (Time.time - start < 1f)
            {
                yield return null;
            }

            OnlineSession session = runner.Session;
            Assert.That(session.State, Is.EqualTo(OnlineSession.SessionState.Running));
            Assert.That(session.PlayerCount, Is.EqualTo(1));
            Assert.That(session.Session.CurrentTick, Is.GreaterThan(30), "ティックが進んでいない");

            CharacterView shown = System.Array.Find(views, v => v.gameObject.activeSelf);
            Assert.That(shown, Is.Not.Null, "自分のキャラクターが表示されていない");
            Assert.That(System.Array.FindAll(views, v => v.gameObject.activeSelf).Length, Is.EqualTo(1), "いない人のキャラクターが表示されている");
            Assert.That(shown.transform.position.y, Is.EqualTo(StandingY).Within(0.02f), "地面に立っていない");

            var followCamera = Object.FindFirstObjectByType<CinemachineCamera>();
            Assert.That(followCamera.Follow, Is.EqualTo(shown.transform), "カメラが自分のキャラクターを追従していない");

            runner.Leave();
            Assert.That(runner.Session, Is.Null);
            Assert.That(shown.gameObject.activeSelf, Is.False, "抜けた後もキャラクターが表示されている");
        }
    }
}
