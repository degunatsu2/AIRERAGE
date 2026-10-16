using System.Collections;
using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Airerage.PlayMode.Tests
{
    /// <summary>右上の全員の情報（ScoreboardHud。ユーザーの要望、2026-10-14）: 順位の順に並び、確定コインの首位に王冠。</summary>
    public sealed class ScoreboardTests
    {
        [UnityTest]
        public IEnumerator Rows_FollowTheRanking_AndTheLeaderGetsACrown()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var board = Object.FindFirstObjectByType<ScoreboardHud>();
            Assert.That(board, Is.Not.Null, "右上の表がない");
            yield return WaitSeconds(0.2f);

            ISimulationFeed feed = runner;
            Assert.That(board.Order.Count, Is.EqualTo(feed.CharacterCount), "全員の札がない");
            Assert.That(board.CrownCount, Is.EqualTo(0), "だれも確定していないのに王冠がある");

            // 操作するキャラクター（番号 0）が撃破されたことにすると、同じ確定コインの中で最後に並ぶ。
            CharacterState state = runner.PlayerState;
            state.Combat.Falls = 3;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.1f);
            Assert.That(board.Order[board.Order.Count - 1], Is.EqualTo(0), "撃破された数で並びが変わらない");

            // 確定コインを持つと、いちばん上に来て王冠がつく。
            state = runner.PlayerState;
            state.Coins.Banked = 25;
            runner.SetPlayerState(state);
            yield return WaitSeconds(0.1f);
            Assert.That(board.Order[0], Is.EqualTo(0), "確定コインの首位が上に来ない");
            Assert.That(board.HasCrown(0), Is.True, "首位に王冠がない");
            Assert.That(board.CrownCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Rows_ShowTheCharacterLogo_AndItFollowsTheCharacter()
        {
            SceneManager.LoadScene("Sandbox_Combat");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            Assert.That(Object.FindFirstObjectByType<ScoreboardHud>(), Is.Not.Null, "右上の表がない");
            ISimulationFeed feed = runner;
            Texture2D standard = ScoreboardHud.LogoOf(feed, 0);
            Assert.That(standard, Is.Not.Null, "札にキャラクターのロゴがない（docs/13 T5-8）");
            Assert.That(standard, Is.SameAs(feed.CharacterOf(0).Logo));

            // キャラクターを変えると、札のロゴも変わる。
            runner.CycleCharacter();
            yield return null;
            Texture2D next = ScoreboardHud.LogoOf(feed, 0);
            Assert.That(next, Is.Not.Null);
            Assert.That(next, Is.Not.SameAs(standard), "キャラクターを変えても、ロゴが変わらない");
        }

        [UnityTest]
        public IEnumerator OfflineSwitch_ShowsTheLogoAndName_InTheMiddle()
        {
            // オフラインのキャラクターの選択（F3）: 切り替えると、中央にロゴと名前（Sandbox_Wire には右上の札がないため）。
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var banner = Object.FindFirstObjectByType<Airerage.Runtime.DebugTools.CharacterSwitchBanner>();
            Assert.That(banner, Is.Not.Null, "キャラクターの切り替えの表示がない");
            yield return null;
            Assert.That(banner.Showing, Is.Null, "切り替えていないのに出した");
            Airerage.Runtime.Data.CharacterConfigAsset next = runner.CycleCharacter();
            yield return null;
            Assert.That(banner.Showing, Is.SameAs(next), "切り替えたキャラクターを出さない");
            Assert.That(next.Logo, Is.Not.Null, "切り替えたキャラクターにロゴがない");
            float start = Time.time;
            while (Time.time - start < 2f)
            {
                yield return null;
            }
            Assert.That(banner.Showing, Is.Null, "しばらくしても消えない");
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
