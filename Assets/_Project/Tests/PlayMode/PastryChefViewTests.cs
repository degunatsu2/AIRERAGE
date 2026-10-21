using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Combat;
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

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// PASTRY CHEF の表示（docs/14 1.4）: 一覧で選べる、Q の長押しで置く場所、置いたわたがし、E の長押しで飛び方の線、張り付いたクリームと
    /// クリームの上の印、R のマカロン。Sandbox_Wire で CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class PastryChefViewTests
    {
        [UnityTest]
        public IEnumerator PastryChef_ShowsTheBase_TheCream_AndTheMacarons()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var pastry = Object.FindFirstObjectByType<PastryEffectsView>();
            var objectsView = Object.FindFirstObjectByType<WorldObjectsView>();
            Assert.That(pastry, Is.Not.Null, "PASTRY CHEF の表示がない");
            CharacterConfigAsset character = null;
            for (int i = 0; i < 4; i++)
            {
                character = runner.CycleCharacter();
            }
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.PastryChefName), "一覧に PASTRY CHEF がない");
            Assert.That(character.Costume, Is.EqualTo(CharacterCostume.PastryChef));
            yield return WaitSeconds(0.6f);
            ISimulationFeed feed = runner;

            // Q を長押し: 置く場所を出す。離すと置く。
            var hold = Enumerable.Repeat(new SimInput(0, 0, InputButtons.Special1, 127, 0), 30).ToList();
            hold.Add(default);
            runner.InputOverride = new Script(hold);
            bool previewed = false;
            float start = Time.time;
            while (Time.time - start < 0.8f)
            {
                previewed |= pastry.ShowsBasePreview(0);
                yield return null;
            }
            Assert.That(previewed, Is.True, "Q の長押しで置く場所を出さない");
            Assert.That(feed.Objects.Items.Count(o => o.Kind == WorldObjectKind.CottonCandy), Is.EqualTo(1), "わたがしを置かない");
            Assert.That(objectsView.ShownCount, Is.GreaterThan(0), "わたがしを表示しない");

            // E を長押し（攻撃で抹茶）: 飛び方の線を出す。離すとクリームを飛ばす。
            var cream = Enumerable.Repeat(new SimInput(0, 0, InputButtons.Special2, 0, -127), 20).ToList();
            cream.Add(new SimInput(0, 0, InputButtons.Special2 | InputButtons.Attack, 0, -127));
            cream.AddRange(Enumerable.Repeat(new SimInput(0, 0, InputButtons.Special2, 0, -127), 5));
            cream.Add(default);
            runner.InputOverride = new Script(cream);
            bool aimed = false;
            start = Time.time;
            while (Time.time - start < 0.6f)
            {
                aimed |= pastry.ShowsCreamPreview(0);
                yield return null;
            }
            Assert.That(aimed, Is.True, "E の長押しで飛び方の線を出さない");
            yield return WaitSeconds(1f);
            List<WorldObject> patches = feed.Objects.Items.Where(o => o.Kind == WorldObjectKind.CreamPatch).ToList();
            Assert.That(patches.Count, Is.GreaterThan(0), "クリームが床に張り付かない");
            Assert.That(patches.All(p => p.Variant == (byte)CreamFlavor.Matcha), Is.True, "攻撃で抹茶にならない");

            // R: マカロンを投げる。
            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = CombatTuning.MaxGauge;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            yield return WaitSeconds(0.5f);
            Assert.That(feed.Objects.Items.Count(o => o.Kind == WorldObjectKind.Macaron), Is.EqualTo(6), "マカロンを 6 個投げない");
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
