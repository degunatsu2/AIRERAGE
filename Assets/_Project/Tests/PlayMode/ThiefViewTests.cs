using System.Collections;
using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// THIEF の表示（ユーザーの案、2026-10-28。docs/14 1.6）: 一覧で選べる、衣装とロゴ、地上技の煙、空中 N の前転、Q の長押しの矢印と弾と
    /// 奪った技の札、E の透明（体を隠して足元に影）と分身、R の詠唱中の 5 秒・10 秒のゲージとコインを得たときの虹色の光。THIEF の物があっても、ほかの物の表示（WorldObjectsView）は
    /// 止まらない。Sandbox_Wire で CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class ThiefViewTests
    {
        [UnityTest]
        public IEnumerator Thief_ShowsSmoke_TheRoll_TheShot_Invisibility_TheClone_AndTheHeist()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var thief = Object.FindFirstObjectByType<ThiefEffectsView>();
            var objects = Object.FindFirstObjectByType<WorldObjectsView>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            var aimArrow = Object.FindFirstObjectByType<SkillAimView>();
            Assert.That(thief, Is.Not.Null, "THIEF の表示がない");
            Assert.That(thief.enabled, Is.True, "THIEF の表示の見本が足りない");
            Assert.That(objects.enabled, Is.True, "置かれた物の表示が止まっている（見本の数）");
            CharacterConfigAsset character = null;
            for (int i = 0; i < 6; i++)
            {
                character = runner.CycleCharacter();
            }
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.ThiefName), "一覧に THIEF がない");
            Assert.That(character.Costume, Is.EqualTo(CharacterCostume.Thief));
            Assert.That(character.Logo, Is.Not.Null, "THIEF のロゴがない");
            Assert.That(character.Tags, Is.EqualTo(new[] { "#盗む", "#コイン重視", "#変則的" }));
            yield return WaitSeconds(0.6f);
            ISimulationFeed feed = runner;
            CharacterView view = runner.PlayerView;

            // 地上技（SMOKE BOMB）: 真下へ投げた煙球が着弾して煙が出る。続けて空中 N（ROLLING CUT）で前転する。
            Assert.That(feed.GetState(0).Grounded, Is.True, "前提: 地上にいる");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Attack), default });
            int smoke = 0;
            float start = Time.time;
            while (Time.time - start < 1f && smoke == 0)
            {
                smoke = thief.SmokeCount;
                yield return null;
            }
            Assert.That(smoke, Is.GreaterThan(0), "煙球が着弾しても煙を出さない");
            yield return WaitUntil(() => feed.GetState(0).Action.IsFree && !feed.GetState(0).Grounded, 1f);
            Assert.That(feed.GetState(0).Grounded, Is.False, "前提: 地上技で飛び上がる");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Attack), default });
            float flip = 0f;
            start = Time.time;
            while (Time.time - start < 0.5f)
            {
                flip = Mathf.Max(flip, Mathf.Abs(view.FlipDegrees));
                yield return null;
            }
            Assert.That(flip, Is.GreaterThan(90f), "空中 N で前転しない");
            yield return WaitUntil(() => feed.GetState(0).Grounded && feed.GetState(0).Action.IsFree, 3f);

            // Q（TALENT SNATCH）: こげ茶の弾を出す。
            // 長押しの間は照準の方向に水色の矢印を出し（弾はまだ出ない）、離すと撃つ（ユーザーの要望、2026-10-29）。
            var hold = new List<SimInput>();
            for (int i = 0; i < 20; i++)
            {
                hold.Add(new SimInput(0, 0, InputButtons.Special1));
            }
            hold.Add(default);
            runner.InputOverride = new Script(hold);
            int shots = 0;
            bool arrow = false;
            bool shotWhileHolding = false;
            start = Time.time;
            while (Time.time - start < 0.8f)
            {
                bool aiming = aimArrow.ShowsArrow(0);
                arrow |= aiming;
                shotWhileHolding |= aiming && thief.ShotCount > 0;
                shots = Mathf.Max(shots, thief.ShotCount);
                yield return null;
            }
            Assert.That(arrow, Is.True, "長押しの間に狙いの矢印を出さない");
            Assert.That(shotWhileHolding, Is.False, "長押しの間に弾を撃った");
            Assert.That(shots, Is.GreaterThan(0), "離してもタレントスナッチの弾を出さない");
            yield return WaitSeconds(0.6f);

            // 奪った技（ダミーの Q。STANDARD の DASH）は、札にその名前と STOLEN。
            CharacterState state = runner.PlayerState;
            state.Combat.StealKind = StealKind.Skill;
            state.Combat.StealSource = 1;
            state.Combat.StealUses = 1;
            state.Combat.StealCount++;
            state.Combat.Special1CooldownTicksLeft = 0;
            runner.SetPlayerState(state);
            yield return null;
            yield return null;
            Assert.That(hud.SlotOf(0).Name, Is.EqualTo(CharacterPresets.Standard().Combat.Special1.Name), "奪った技の名前を札に出さない");
            Assert.That(hud.SlotOf(0).Label, Is.EqualTo("STOLEN"), "奪った技の札に STOLEN と出さない");
            Assert.That(thief.StealBursts, Is.GreaterThan(0), "奪ったときに STEAL! を出さない");

            // E（MISDIRECTION）: 体と周りの表示を隠し、足元に影、その場に分身。3 秒で解ける。
            yield return WaitUntil(() => feed.GetState(0).Grounded && feed.GetState(0).Action.IsFree, 2f);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special2), default });
            yield return WaitSeconds(0.3f);
            Assert.That(feed.GetState(0).Combat.InvisibleTicksLeft, Is.GreaterThan(0), "前提: E で透明になる");
            Assert.That(view.HiddenForInvisible, Is.True, "透明の間に体を出している");
            Assert.That(view.GetComponent<Renderer>().enabled, Is.False, "透明の間に箱を出している");
            Assert.That(thief.ShowsShadow(0), Is.True, "透明の間に足元の影を出さない");
            Assert.That(view.ShowingClone, Is.True, "分身を出さない");
            Assert.That(Vector3.Distance(view.ClonePosition, view.transform.position), Is.GreaterThan(0.3f), "分身が本体と同じ所にいる");
            yield return WaitSeconds(3.2f);
            Assert.That(view.HiddenForInvisible, Is.False, "3 秒たっても透明のまま");
            Assert.That(view.ShowingClone, Is.False, "3 秒たっても分身を出している");
            Assert.That(thief.ShowsShadow(0), Is.False, "透明が解けても影を出している");

            // R（BANK HEIST）の詠唱中: 札の上に 5 秒ごと・10 秒ごとにいっぱいになるゲージ（7 秒なら 0.4 と 0.7）。
            Assert.That(feed.GetConfig(0).Combat.Super.Effect, Is.EqualTo(Airerage.Core.Skills.SkillEffect.BankHeist));
            state = runner.PlayerState;
            state.Combat.HeistTicks = 1 + 7 * 60;
            state.Combat.HeistSpot = -1;
            runner.SetPlayerState(state);
            yield return null;
            yield return null;
            Assert.That(feed.GetState(0).Combat.HeistTicks, Is.GreaterThan(0), "前提: 詠唱している");
            Assert.That(hud.SlotOf(2).HeistFive, Is.EqualTo(0.4f).Within(0.05f), "5 秒ごとのゲージを出さない");
            Assert.That(hud.SlotOf(2).HeistTen, Is.EqualTo(0.7f).Within(0.05f), "10 秒ごとのゲージを出さない");

            // コインを得た: ほかのキャラクターの必殺技と同じ虹色の光（ユーザーの要望、2026-10-29）。
            state = runner.PlayerState;
            state.Combat.HeistTicks = 0;
            state.Combat.HeistPayoutCount++;
            state.Combat.HeistLastPayout = 25;
            runner.SetPlayerState(state);
            yield return null;
            yield return null;
            Assert.That(view.SuperBurst.IsPlaying, Is.True, "コインを得ても必殺技の虹色の光を出さない");
            Assert.That(hud.SlotOf(2).HeistFive, Is.LessThan(0f), "詠唱が終わってもゲージを出している");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> done, float seconds)
        {
            float start = Time.time;
            while (!done() && Time.time - start < seconds)
            {
                yield return null;
            }
        }

        private sealed class Script : ITickInputSource
        {
            private readonly List<SimInput> _inputs;
            private int _next;

            public Script(List<SimInput> inputs)
            {
                _inputs = inputs;
            }

            public bool TryGetNext(out SimInput input)
            {
                input = _next < _inputs.Count ? _inputs[_next] : default;
                return _next++ < _inputs.Count;
            }
        }
    }
}
