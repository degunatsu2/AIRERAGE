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
    /// <summary>拠点（バンク。docs/12_match_plan.md T4-7）: 表示、確定したときの「+n BANKED」と音、移動と予告。</summary>
    public sealed class BankViewTests
    {
        [UnityTest]
        public IEnumerator EnteringTheBank_BanksTheHeldCoins()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var view = Object.FindFirstObjectByType<BankView>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(view, Is.Not.Null, "拠点の表示がない");
            yield return WaitSeconds(0.2f);

            ISimulationFeed feed = runner;
            Assert.That(feed.BankSpots.Count, Is.GreaterThanOrEqualTo(3), "拠点の候補がない");
            Assert.That(feed.BankSpots[0].IsFirst, Is.True, "最初の拠点が先頭にない");
            Assert.That(view.CurrentSpot, Is.EqualTo(0), "始まりの拠点が最初の地点ではない");

            // 所持コインを持たせて、拠点の中へ移す。
            BankSpot bank = feed.BankSpots[view.CurrentSpot];
            CharacterState state = runner.PlayerState;
            state.Coins.Held = 7;
            state.Position = new System.Numerics.Vector3(bank.Position.X, bank.Position.Y, 0f);
            state.Velocity = System.Numerics.Vector3.Zero;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.2f);

            Assert.That(runner.PlayerState.Coins.Banked, Is.EqualTo(7), "拠点に入っても確定しない");
            Assert.That(runner.PlayerState.Coins.Held, Is.EqualTo(0));
            Assert.That(view.LastBankedAmount, Is.EqualTo(7), "「+n BANKED」が出ない");
            Assert.That(view.PopupCount, Is.GreaterThan(0));
            Assert.That(audio.History.Contains(SoundId.Bank), Is.True, "確定の音が鳴らない");
        }

        [UnityTest]
        public IEnumerator TheBank_Moves_AfterAnnouncingTheNextSpot()
        {
            SceneManager.LoadScene("Sandbox_Online");
            yield return null;
            var runner = Object.FindFirstObjectByType<OnlineRunner>();
            var view = Object.FindFirstObjectByType<BankView>();
            Assert.That(runner.Stage.BankSpots.Count, Is.GreaterThanOrEqualTo(4), "拠点の候補がない");
            runner.HostRules.CountdownTicks = 0;
            // 1 秒ごとに移り、0.5 秒前から予告する。
            runner.HostRules.BankMoveIntervalTicks = 60;
            runner.HostRules.BankNoticeTicks = 30;
            runner.TimedMatch = false;
            runner.StartHost(0);
            runner.StartMatch();
            yield return WaitSeconds(0.2f);

            Assert.That(runner.Session.World.BankSpots.Count, Is.EqualTo(runner.Stage.BankSpots.Count), "試合のワールドに拠点が渡っていない");
            Assert.That(view.CurrentSpot, Is.EqualTo(0), "始まりの拠点が最初の地点ではない");

            var notices = Object.FindFirstObjectByType<NoticeHud>();
            Assert.That(notices, Is.Not.Null, "お知らせの表示（NoticeHud）がない");
            bool chip = false;
            bool announced = false;
            int announcedSpot = -1;
            float start = Time.time;
            while (Time.time - start < 3f && view.CurrentSpot == 0)
            {
                chip |= notices.ChipTitles.Contains("BANK MOVES");
                if (view.NextSpot >= 0)
                {
                    announced = true;
                    announcedSpot = view.NextSpot;
                }
                yield return null;
            }
            Assert.That(announced, Is.True, "次の地点を予告しない");
            Assert.That(view.CurrentSpot, Is.Not.EqualTo(0), "拠点が移らない");
            Assert.That(view.CurrentSpot, Is.EqualTo(announcedSpot), "予告と違う地点に移った");
            Assert.That(view.NoticeCount, Is.GreaterThanOrEqualTo(1), "予告の表示を出していない");
            Assert.That(chip, Is.True, "タイマーの下に「BANK MOVES」の札が出ない");
            Assert.That(Object.FindFirstObjectByType<GameAudio>().History.Contains(SoundId.BankNotice), Is.True, "予告の呼び鈴が鳴らない");
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
