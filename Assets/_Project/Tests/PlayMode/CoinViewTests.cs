using System.Collections;
using System.Linq;
using Airerage.Core.Economy;
using Airerage.Core.Simulation;
using Airerage.Net;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// フィールドのコイン（docs/12_match_plan.md T4-4）: 出現の表示、拾ったときの「+n」と音、オンラインのステージの出現地点、
    /// 枚数ごとの見た目、残り 1 分の 100 枚のコインと画面の端の矢印。
    /// </summary>
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
        public IEnumerator EachValue_HasItsOwnLook()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<CoinView>();
            yield return WaitSeconds(0.3f);

            ISimulationFeed feed = runner;
            var looks = new System.Collections.Generic.HashSet<CoinLook>();
            foreach (FieldCoin coin in feed.Coins.Coins)
            {
                Assert.That(view.TryGetLook(coin.Id, out CoinLook look), Is.True, $"コイン {coin.Id} が表示されていない");
                Assert.That(look, Is.EqualTo(CoinView.LookOf(coin.Value)), $"{coin.Value} 枚のコインの見た目が違う");
                looks.Add(look);
            }
            Assert.That(looks, Is.EquivalentTo(new[] { CoinLook.Single, CoinLook.Triple, CoinLook.Red }), "1・3・5 枚の見た目がそろっていない");
            Assert.That(CoinView.LookOf(1), Is.EqualTo(CoinLook.Single));
            Assert.That(CoinView.LookOf(3), Is.EqualTo(CoinLook.Triple));
            Assert.That(CoinView.LookOf(5), Is.EqualTo(CoinLook.Triple));
            Assert.That(CoinView.LookOf(20), Is.EqualTo(CoinLook.Red));
            Assert.That(CoinView.LookOf(100), Is.EqualTo(CoinLook.Rainbow));
        }

        [UnityTest]
        public IEnumerator Jackpot_IsAnnouncedWithOneMinuteLeft_ThenAppears_AndIsMarked()
        {
            SceneManager.LoadScene("Sandbox_Online");
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            var hud = Object.FindFirstObjectByType<CoinHud>();
            var view = Object.FindFirstObjectByType<CoinView>();
            var atmosphere = Object.FindFirstObjectByType<MatchAtmosphere>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(atmosphere, Is.Not.Null, "終盤の雰囲気の切り替えがない");
            runner.HostRules.CountdownTicks = 0;
            // 始まって 0.5 秒で残り 1 分（予告）になり、その 0.5 秒後に出る。
            runner.HostRules.DurationTicks = runner.HostRules.JackpotTicksBeforeEnd + 30;
            runner.HostRules.JackpotNoticeTicks = 30;
            runner.TimedMatch = true;
            runner.StartHost(0);
            runner.StartMatch();
            yield return WaitSeconds(0.2f);
            Assert.That(hud.JackpotPresent || hud.JackpotIncoming, Is.False, "残り 1 分より前に予告した");
            Assert.That(atmosphere.Tense, Is.False);
            Assert.That(audio.BgmTense, Is.False);

            yield return WaitSeconds(0.45f);
            Assert.That(hud.JackpotIncoming, Is.True, "残り 1 分で予告しない");
            Assert.That(hud.JackpotPresent, Is.False, "予告の間に出た");
            Assert.That(view.PreviewShowing, Is.True, "出る地点を示していない");
            Assert.That(atmosphere.Tense, Is.True, "終盤の雰囲気にならない");
            Assert.That(audio.BgmTense, Is.True, "緊張感のある BGM にならない");
            Assert.That(audio.History.Contains(SoundId.JackpotAlarm), Is.True, "警報が鳴らない");
            Assert.That(atmosphere.SkyBlend, Is.GreaterThan(0f), "空が赤くならない");

            yield return WaitSeconds(0.6f);
            Assert.That(view.PreviewShowing, Is.False, "出た後も予告の印が残っている");
            FieldCoin jackpot = default;
            int found = 0;
            foreach (FieldCoin coin in runner.Session.World.Coins.Field.Coins)
            {
                if (coin.Value == CoinView.RainbowValue)
                {
                    jackpot = coin;
                    found++;
                }
            }
            Assert.That(found, Is.EqualTo(1), "100 枚のコインが 1 つ出ていない");
            Assert.That(view.TryGetLook(jackpot.Id, out CoinLook look) && look == CoinLook.Rainbow, Is.True, "虹色の見た目になっていない");
            var glow = Object.FindFirstObjectByType<RainbowCoinGlow>();
            Assert.That(glow, Is.Not.Null, "虹色の光がない");
            Color before = glow.CurrentColor;
            yield return WaitSeconds(0.3f);
            Assert.That(glow.CurrentColor, Is.Not.EqualTo(before), "色が移り変わらない");
            Assert.That(hud.JackpotPresent, Is.True, "100 枚のコインを見つけていない");
            Assert.That(hud.JackpotBannerShowing, Is.True, "「JACKPOT」が出ない");

            bool onScreen = !ScreenEdgeMarker.Locate(Object.FindFirstObjectByType<Camera>(),
                new Vector3(jackpot.Position.X, jackpot.Position.Y, 0f), 56f, out _, out _);
            Assert.That(hud.JackpotOffscreen, Is.EqualTo(!onScreen), "画面の外かどうかの判定が合わない");

            // 再戦すると、ふつうの BGM に戻る。
            runner.HostRules.DurationTicks = 0;
            runner.Restart();
            yield return WaitSeconds(0.3f);
            Assert.That(atmosphere.Tense, Is.False, "再戦しても終盤の雰囲気のまま");
            Assert.That(audio.BgmTense, Is.False);
            runner.Leave();
        }

        [UnityTest]
        public IEnumerator PickingUpARedCoin_ShowsItsRespawnGauge()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<CoinView>();
            yield return WaitSeconds(0.2f);
            Assert.That(view.RespawnGaugeCount, Is.EqualTo(0), "赤いコインがあるのにゲージが出ている");

            ISimulationFeed feed = runner;
            FieldCoin red = default;
            bool found = false;
            foreach (FieldCoin coin in feed.Coins.Coins)
            {
                if (CoinView.LookOf(coin.Value) == CoinLook.Red)
                {
                    red = coin;
                    found = true;
                }
            }
            Assert.That(found, Is.True, "前提: 赤いコインがある");
            Assert.That(red.Value, Is.EqualTo(CoinView.RedValue));
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(red.Position.X, red.Position.Y, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.2f);

            Assert.That(runner.PlayerState.Coins.Held, Is.EqualTo(CoinView.RedValue), "前提: 拾った");
            Assert.That(view.RespawnGaugeCount, Is.EqualTo(1), "次に出るまでのゲージが出ない");
        }

        [UnityTest]
        public IEnumerator EdgeMarker_PointsTowardsTheTarget()
        {
            var cameraGo = new GameObject("TestCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -20f);
            yield return null;
            try
            {
                Assert.That(ScreenEdgeMarker.Locate(camera, Vector3.zero, 40f, out Vector2 center, out _), Is.False, "正面が画面の外になった");
                Assert.That(center.x, Is.EqualTo(Screen.width * 0.5f).Within(1f));

                Assert.That(ScreenEdgeMarker.Locate(camera, new Vector3(500f, 0f, 0f), 40f, out Vector2 right, out float rightAngle), Is.True);
                Assert.That(right.x, Is.EqualTo(Screen.width - 40f).Within(1f), "右の端に置かれない");
                Assert.That(rightAngle, Is.EqualTo(0f).Within(1f), "右を向かない");

                // 上（GUI の座標では -y）。
                Assert.That(ScreenEdgeMarker.Locate(camera, new Vector3(0f, 500f, 0f), 40f, out Vector2 top, out float topAngle), Is.True);
                Assert.That(top.y, Is.EqualTo(40f).Within(1f), "上の端に置かれない");
                Assert.That(topAngle, Is.EqualTo(-90f).Within(1f), "上を向かない");

                Texture2D arrow = ScreenEdgeMarker.CreateArrowTexture(32);
                Assert.That(arrow.GetPixel(2, 16).a, Is.GreaterThan(0.5f), "矢印の根元が透明");
                Assert.That(arrow.GetPixel(30, 2).a, Is.LessThan(0.5f), "矢印の外が塗られている");
                Object.Destroy(arrow);
            }
            finally
            {
                Object.Destroy(cameraGo);
            }
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
            int regular = runner.Stage.CoinSpawns.Count(point => point.Kind == CoinSpawnKind.Regular);
            Assert.That(runner.Stage.CoinSpawns.Count(point => point.Kind == CoinSpawnKind.Jackpot), Is.GreaterThan(0), "100 枚のコインの候補がない");
            Assert.That(runner.Session.World.Coins.Field.Coins.Count, Is.EqualTo(regular), "始まってもコインが出ない（100 枚のコインはまだ出ない）");
            Assert.That(Object.FindFirstObjectByType<CoinView>().ShownCount, Is.EqualTo(regular));
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
