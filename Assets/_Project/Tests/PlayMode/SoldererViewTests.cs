using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Runtime.Data;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;
using CombatTuning = Airerage.Core.Combat.CombatTuning;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// SOLDERER の表示（ユーザーの案、2026-10-21）: 一覧で選べる、衣装、Q の突撃の跡と長押しのオーラ、札の CHAIN、E のインバータの火花、
    /// 感電の稲妻、R のボールライトニング。PASTRY CHEF のボウルで防いだときの GUARD も見る。Sandbox_Wire で CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class SoldererViewTests
    {
        [UnityTest]
        public IEnumerator Solderer_ShowsTheDash_TheInverter_TheStun_AndTheBalls()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var electric = Object.FindFirstObjectByType<ElectricEffectsView>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            Assert.That(electric, Is.Not.Null, "電気の表示がない");
            CharacterConfigAsset character = null;
            for (int i = 0; i < 5; i++)
            {
                character = runner.CycleCharacter();
            }
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.SoldererName), "一覧に SOLDERER がない");
            Assert.That(character.Costume, Is.EqualTo(CharacterCostume.Solderer));
            Assert.That(character.Logo, Is.Not.Null, "SOLDERER のロゴがない");
            yield return WaitSeconds(0.6f);
            ISimulationFeed feed = runner;

            // Q を短く押す: 突撃の跡。続きを出せる間、札に CHAIN。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            bool dashed = false;
            bool chained = false;
            float start = Time.time;
            while (Time.time - start < 0.8f)
            {
                dashed |= electric.ShowsDash(0);
                chained |= hud.SlotOf(0).Label == "CHAIN";
                yield return null;
            }
            Assert.That(dashed, Is.True, "イナズマステップの跡を出さない");
            Assert.That(chained, Is.True, "続きを出せる間、札に CHAIN と出さない");

            // Q を長押し: 長押しになった瞬間に強突撃が出て、オーラが回る（離さなくてよい。2026-10-22）。
            runner.InputOverride = new Script(Enumerable.Repeat(new SimInput(0, 0, InputButtons.Special1), 30).Append(default).ToList());
            bool charged = false;
            start = Time.time;
            while (Time.time - start < 0.6f)
            {
                charged |= electric.ShowsCharge(0);
                yield return null;
            }
            Assert.That(charged, Is.True, "長押しのオーラを出さない");
            yield return WaitSeconds(0.6f);

            // E: インバータをオンにすると火花、もう一度でオフ。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special2), default });
            yield return WaitSeconds(0.3f);
            Assert.That(feed.GetState(0).Combat.InverterOn, Is.True, "E でインバータがオンにならない");
            Assert.That(electric.ShowsInverter(0), Is.True, "インバータの火花を出さない");

            // 感電: 体を走る稲妻。
            CharacterState state = runner.PlayerState;
            state.Combat.StunTicksLeft = 40;
            runner.SetPlayerState(state);
            yield return null;
            yield return null;
            Assert.That(electric.ShowsStun(0), Is.True, "感電の稲妻を出さない");
            yield return WaitSeconds(1f);
            Assert.That(electric.ShowsStun(0), Is.False, "感電が終わっても稲妻を出している");

            // R: 電気の球を 3 つ。
            state = runner.PlayerState;
            state.Combat.Gauge = CombatTuning.MaxGauge;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            yield return WaitSeconds(1.2f);
            Assert.That(feed.Objects.Items.Count(o => o.Kind == WorldObjectKind.BallLightning), Is.EqualTo(3), "電気の球を 3 つ撃たない");
        }

        [UnityTest]
        public IEnumerator PastryChef_ShowsGuard_WhenTheBowlBlocks()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var pastry = Object.FindFirstObjectByType<PastryEffectsView>();
            yield return null;
            yield return null;
            int before = pastry.GuardsOf(0);
            CharacterState state = runner.PlayerState;
            state.Combat.ShieldBlockCount++;
            runner.SetPlayerState(state);
            yield return null;
            yield return null;
            Assert.That(pastry.GuardsOf(0), Is.EqualTo(before + 1), "ボウルで防いでも GUARD を出さない");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
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
