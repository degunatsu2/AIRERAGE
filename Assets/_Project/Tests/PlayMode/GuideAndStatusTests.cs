using System.Collections;
using System.Linq;
using Airerage.Core.Economy;
using Airerage.Core.Simulation;
using Airerage.Net;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 誘導表示（EdgeGuideLayer、PlayerGuides、撃破ドロップのかたまり）、撃破されたときの右上の札、プレイヤーネーム
    /// （ユーザーの要望、2026-10-14）。
    /// </summary>
    public sealed class GuideAndStatusTests
    {
        [UnityTest]
        public IEnumerator Guides_AreShownOnlyForOffscreenTargets_AndDoNotOverlap()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var layer = Object.FindFirstObjectByType<EdgeGuideLayer>();
            var players = Object.FindFirstObjectByType<PlayerGuides>();
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            Assert.That(layer, Is.Not.Null, "誘導表示の層がない");
            Assert.That(players, Is.Not.Null, "ほかのプレイヤーの誘導表示がない");
            yield return WaitSeconds(0.3f);

            // ほかのプレイヤー（ダミー）のうち、画面の外にいる人だけに出す。
            ISimulationFeed feed = runner;
            var camera = Object.FindFirstObjectByType<Camera>();
            int expected = 0;
            for (int i = 0; i < feed.CharacterCount; i++)
            {
                CharacterState state = feed.GetState(i);
                if (i != feed.LocalCharacter && !state.Combat.IsWaitingToRespawn
                    && ScreenEdgeMarker.Locate(camera, new Vector3(state.Position.X, state.Position.Y, 0f), 56f, out _, out _))
                {
                    expected++;
                    Assert.That(layer.Texts.Contains(feed.LabelOf(i)), Is.True, $"{feed.LabelOf(i)} の札がない");
                }
            }
            Assert.That(players.ShownCount, Is.EqualTo(expected), "画面の外にいる人の数と合わない");

            // 画面の中の物には出さず、外の物にだけ出す。
            yield return null;
            Assert.That(layer.Add(camera.transform.position + camera.transform.forward * 10f, Color.white, null, "IN"), Is.False);
            Assert.That(layer.Add(new Vector3(500f, 0f, 0f), Color.white, null, "OUT"), Is.True);
        }

        [UnityTest]
        public IEnumerator BeingKnockedOut_DropsACluster_AndCracksThenGraysTheStatus_ThenFlashes()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var hud = Object.FindFirstObjectByType<CoinHud>();
            var board = Object.FindFirstObjectByType<ScoreboardHud>();
            yield return WaitSeconds(0.2f);

            // コインを持たせて、ステージの外へ出す。
            CharacterState state = runner.PlayerState;
            state.Coins.Held = 10;
            state.Position = new System.Numerics.Vector3(0f, -60f, 0f);
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.1f);
            Assert.That(runner.PlayerState.Combat.IsWaitingToRespawn, Is.True, "前提: 撃破された");

            ISimulationFeed feed = runner;
            int dropped = feed.Coins.Coins.Where(c => c.Origin == CoinEventKind.KoDrop).Sum(c => c.Value);
            Assert.That(dropped, Is.EqualTo(5), "所持の半分を落とさない");
            Assert.That(hud.KoDropClusterCount, Is.EqualTo(1), "撃破ドロップが1つのかたまりになっていない");
            Assert.That(board.LookOf(0).Crack, Is.GreaterThan(0f), "ひび割れない");

            yield return WaitSeconds(1f);
            PlayerStatusPlate.Look gray = board.LookOf(0);
            Assert.That(gray.Gray, Is.EqualTo(1f), "灰色にならない");
            Assert.That(gray.Overlay, Does.StartWith("RESPAWN"), "復帰までの時間を出さない");

            float start = Time.time;
            while (runner.PlayerState.Combat.IsWaitingToRespawn && Time.time - start < 4f)
            {
                yield return null;
            }
            yield return null;
            Assert.That(runner.PlayerState.Combat.IsWaitingToRespawn, Is.False, "前提: 復帰した");
            PlayerStatusPlate.Look back = board.LookOf(0);
            Assert.That(back.Flash, Is.GreaterThan(0f), "復帰したときに光らない");
            Assert.That(back.Gray, Is.EqualTo(0f), "復帰しても灰色のまま");
        }

        [UnityTest]
        public IEnumerator ThePlayerName_IsUsedInTheMatch()
        {
            SceneManager.LoadScene("Sandbox_Online");
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            runner.HostRules.CountdownTicks = 0;
            runner.TimedMatch = false;
            runner.PlayerName = "  Tester  ";
            runner.StartHost(0);
            runner.StartMatch();
            yield return WaitSeconds(0.2f);

            ISimulationFeed feed = runner;
            Assert.That(feed.LabelOf(0), Is.EqualTo("Tester"), "名前が試合に反映されない");
            runner.PlayerName = string.Empty;
            runner.Restart();
            yield return WaitSeconds(0.2f);
            Assert.That(feed.LabelOf(0), Is.EqualTo("P1"), "名前がないときは P1");
            runner.Leave();
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }
    }
}
