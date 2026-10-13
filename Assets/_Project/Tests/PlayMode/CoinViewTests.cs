using System.Collections;
using System.Linq;
using Degunatsu.Core.Simulation;
using Degunatsu.Net;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>フィールドのコイン（docs/12_match_plan.md T4-4）: 出現の表示、拾ったときの「+n」と音、オンラインのステージの出現地点。</summary>
    public sealed class CoinViewTests
    {
        [UnityTest]
        public IEnumerator Coins_AppearInTheAir_AndArePickedUpByTouching()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<CoinView>();
            var hud = Object.FindFirstObjectByType<CoinHud>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(view, Is.Not.Null, "コインの表示がない");
            Assert.That(hud, Is.Not.Null, "コインの数の表示がない");
            yield return WaitSeconds(0.3f);

            ISimulationFeed feed = runner;
            int count = feed.Coins.Coins.Count;
            Assert.That(count, Is.GreaterThan(0), "コインが出ていない");
            Assert.That(view.ShownCount, Is.EqualTo(count), "出ているコインが表示されていない");

            // 中央の足場の上のコインに重ねる。
            var coin = feed.Coins.Coins[0];
            foreach (var c in feed.Coins.Coins)
            {
                if (Mathf.Abs(c.Position.X) < 1f)
                {
                    coin = c;
                }
            }
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(coin.Position.X, coin.Position.Y, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.2f);

            Assert.That(runner.PlayerState.Coins.Held, Is.EqualTo(coin.Value), "触れても拾わない");
            Assert.That(view.ShownCount, Is.EqualTo(count - 1), "拾ったコインが表示に残っている");
            Assert.That(hud.LastPopupAmount, Is.EqualTo(coin.Value), "「+n」が出ない");
            Assert.That(audio.History.Contains(SoundId.CoinPickup), Is.True, "拾った音が鳴らない");
        }

        [UnityTest]
        public IEnumerator OnlineStage_HasCoinSpawnPoints_InTheMatch()
        {
            SceneManager.LoadScene("Sandbox_Online");
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            Assert.That(runner.Stage.CoinSpawns.Count, Is.GreaterThanOrEqualTo(8), "コインの出現地点がない");
            runner.HostRules.CountdownTicks = 0;
            runner.TimedMatch = false;
            runner.StartHost(0);
            runner.StartMatch();
            yield return WaitSeconds(0.3f);

            Assert.That(runner.Session.World.CoinSpawns.Count, Is.EqualTo(runner.Stage.CoinSpawns.Count), "試合のワールドに出現地点が渡っていない");
            Assert.That(runner.Session.World.Coins.Field.Coins.Count, Is.EqualTo(runner.Stage.CoinSpawns.Count), "始まってもコインが出ない");
            Assert.That(Object.FindFirstObjectByType<CoinView>().ShownCount, Is.EqualTo(runner.Stage.CoinSpawns.Count));
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
