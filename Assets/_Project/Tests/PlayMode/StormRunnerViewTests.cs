using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
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
    /// STORM RUNNER の表示（docs/14 1.2、docs/13 T5-6）: 一覧で選べる、走る線、ウインドクロークの風と突風、スカイロケットの風の帯、
    /// キャッスルインジエアーの風、札の GUST、音。Sandbox_Wire で F3 の代わりに CycleCharacter を呼ぶ。
    /// </summary>
    public sealed class StormRunnerViewTests
    {
        [UnityTest]
        public IEnumerator StormRunner_ShowsTheRun_TheCloak_TheGust_TheWindZone_AndTheCastle()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;
            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var boost = Object.FindFirstObjectByType<BoostAuraView>();
            var objectsView = Object.FindFirstObjectByType<WorldObjectsView>();
            var hud = Object.FindFirstObjectByType<SkillHud>();
            var audio = Object.FindFirstObjectByType<GameAudio>();
            Assert.That(boost, Is.Not.Null, "強化の表示がない");

            runner.CycleCharacter();
            CharacterConfigAsset character = runner.CycleCharacter();
            Assert.That(character.DisplayName, Is.EqualTo(CharacterPresets.StormRunnerName), "一覧に STORM RUNNER がない");
            yield return WaitSeconds(0.6f);

            // 地上技で走る（後ろへ流れる線）。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Attack), default, default, default });
            yield return WaitSeconds(0.3f);
            Assert.That(runner.PlayerState.Action.Kind, Is.EqualTo(ActionKind.Attack), "前提: 走っている");
            Assert.That(boost.ShowsRun(0), Is.True, "走る線を出さない");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Guard), default });
            yield return WaitSeconds(0.8f);

            // Q: ウインドクローク。札は GUST（もう一度押すと突風）。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            yield return WaitSeconds(0.3f);
            Assert.That(boost.ShowsCloak(0), Is.True, "ウインドクロークの風を出さない");
            Assert.That(hud.SlotOf(0).Text, Is.EqualTo("GUST"), "効果中の札が GUST でない");
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            bool sawGust = false;
            float start = Time.time;
            while (Time.time - start < 0.5f)
            {
                ISimulationFeed feed = runner;
                sawGust |= feed.Objects.Items.Any(o => o.Kind == WorldObjectKind.Gust) && objectsView.ShownCount > 0;
                yield return null;
            }
            Assert.That(sawGust, Is.True, "突風を表示しない");
            Assert.That(audio.History.Contains(SoundId.Gust), Is.True, "突風の音が鳴らない");
            Assert.That(boost.ShowsCloak(0), Is.False, "解いたのにウインドクロークの風が残る");

            // E: 上へ吹く風の帯。
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special2, 0, 127), default });
            yield return WaitSeconds(0.3f);
            ISimulationFeed feed2 = runner;
            Assert.That(feed2.Objects.Items.Any(o => o.Kind == WorldObjectKind.WindZone), Is.True, "前提: 風の帯を置いた");
            Assert.That(audio.History.Contains(SoundId.SkyRocket), Is.True, "風の帯の音が鳴らない");

            // R: キャッスルインジエアー。
            CharacterState state = runner.PlayerState;
            state.Combat.Gauge = CombatTuning.MaxGauge;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Super), default });
            yield return WaitSeconds(0.3f);
            Assert.That(boost.ShowsCastle(0), Is.True, "キャッスルインジエアーの風を出さない");
            runner.CycleCharacter();
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
