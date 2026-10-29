using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
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
    /// THIEF が奪って置いたギミック・仕掛けは、ステージの本物の見た目を写して出す（ユーザーの要望、2026-10-29）。アスレチックのジャンプ台と、
    /// 研究所の赤外線とミサイルを、トレーニングで置いてみる。写しのギミックは、シミュレーションのギミックとしては集めない。
    /// </summary>
    public sealed class ThiefPlacedLookTests
    {
        [UnityTest]
        public IEnumerator PlacedJumpPad_And_Tripwire_CopyTheRealLooks()
        {
            int savedCharacter = PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0);
            SceneManager.LoadScene("App");
            yield return null;
            var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 10f, "メニューにならない");

            // アスレチック: ジャンプ台を奪って置く。
            yield return StartTraining(flow, "Stage_Athletic");
            var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            var thief = UnityEngine.Object.FindFirstObjectByType<ThiefEffectsView>();
            ISimulationFeed feed = runner;
            yield return BecomeThief(runner);
            int pad = Enumerable.Range(0, feed.Zones.Count).First(i => feed.Zones[i].Kind == StageZoneKind.JumpPad);
            int realViews = UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None).Length;
            yield return Place(runner, StealKind.Zone, pad);
            Assert.That(feed.Objects.Items.Count(o => o.Kind == WorldObjectKind.PlacedZone && !o.Ended), Is.EqualTo(1), "前提: ジャンプ台を置いた");
            Assert.That(thief.CopiedCount, Is.EqualTo(1), "置いたジャンプ台に本物の見た目を写さない");
            StageZoneAuthoring copy = UnityEngine.Object.FindObjectsByType<StageZoneAuthoring>(FindObjectsSortMode.None).Single(a => a.IsCopy);
            Assert.That(copy.Kind, Is.EqualTo(StageZoneKind.JumpPad));
            Assert.That(UnityEngine.Object.FindObjectsByType<StageZoneView>(FindObjectsSortMode.None).Length, Is.EqualTo(realViews + 1),
                "写しに本物と同じ線の表示がない");
            Assert.That(copy.GetComponentsInChildren<MeshRenderer>().Length, Is.GreaterThan(0), "写しにジャンプ台の台がない");
            Assert.That(StageZoneAuthoring.Collect(new List<string>()).Count, Is.EqualTo(feed.Zones.Count), "写しをシミュレーションのギミックとして集めた");
            Assert.That(copy.ToZone().Equals(ThiefRules.PlacedZone(feed.Zones[pad], feed.Objects.Items.First(o => o.Kind == WorldObjectKind.PlacedZone))),
                Is.True, "写しの範囲が置いたギミックと違う");

            // 研究所: 赤外線とミサイルを奪って置く（置いた瞬間に 1 発撃つ）。
            yield return StartTraining(flow, "Stage_Lab");
            runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
            thief = UnityEngine.Object.FindFirstObjectByType<ThiefEffectsView>();
            feed = runner;
            yield return BecomeThief(runner);
            int tripwire = Enumerable.Range(0, feed.Traps.Count).First(i => feed.Traps[i].Kind == StageTrapKind.Tripwire);
            int realTrapViews = UnityEngine.Object.FindObjectsByType<StageTrapView>(FindObjectsSortMode.None).Length;
            yield return Place(runner, StealKind.Trap, tripwire);
            Assert.That(feed.Objects.Items.Count(o => o.Kind == WorldObjectKind.PlacedTrap && !o.Ended), Is.EqualTo(1), "前提: 仕掛けを置いた");
            Assert.That(thief.CopiedCount, Is.EqualTo(1), "置いた仕掛けに本物の見た目を写さない");
            Transform trapCopy = thief.transform.Cast<Transform>().Single(t => t.name.StartsWith("Placed"));
            Assert.That(trapCopy.Find("Launcher"), Is.Not.Null, "写しに発射装置がない");
            Assert.That(trapCopy.Find("Beam"), Is.Not.Null, "写しに赤外線がない");
            Assert.That(trapCopy.GetComponent<StageTrapView>(), Is.Null, "写しが本物の仕掛けの状態を見ている");
            Assert.That(UnityEngine.Object.FindObjectsByType<StageTrapView>(FindObjectsSortMode.None).Length, Is.EqualTo(realTrapViews));
            Assert.That(StageTrapAuthoring.Collect(new List<string>()).Count, Is.EqualTo(feed.Traps.Count), "写しをシミュレーションの仕掛けとして集めた");
            PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, savedCharacter);
        }

        private static IEnumerator StartTraining(GameFlow flow, string scene)
        {
            StageDefinition stage = flow.Stages.Stages.First(s => s.SceneName == scene);
            flow.StartTraining(stage);
            yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading && SceneManager.GetActiveScene().name == scene, 10f,
                $"{scene} のトレーニングにならない");
            yield return WaitSeconds(0.5f);
        }

        private static IEnumerator BecomeThief(SimulationRunner runner)
        {
            for (int i = 0; i < 8 && ((ISimulationFeed)runner).CharacterOf(0).DisplayName != CharacterPresets.ThiefName; i++)
            {
                runner.CycleCharacter();
            }
            Assert.That(((ISimulationFeed)runner).CharacterOf(0).DisplayName, Is.EqualTo(CharacterPresets.ThiefName), "THIEF にならない");
            yield return WaitSeconds(0.3f);
        }

        // 奪ったことにして、Q を短く押して目の前に置く。
        private static IEnumerator Place(SimulationRunner runner, StealKind kind, int source)
        {
            CharacterState state = runner.PlayerState;
            state.Combat.StealKind = kind;
            state.Combat.StealSource = source;
            state.Combat.StealUses = 1;
            state.Combat.Special1CooldownTicksLeft = 0;
            runner.SetPlayerState(state);
            runner.InputOverride = new Script(new List<SimInput> { new SimInput(0, 0, InputButtons.Special1), default });
            yield return WaitSeconds(0.4f);
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string message)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > seconds)
                {
                    Assert.Fail(message);
                }
                yield return null;
            }
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
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
