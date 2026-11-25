using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Airerage.Core.Combat;
using Airerage.Core.Input;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// THIEF の Q で奪えるすべての物（全ステージのギミックと仕掛け）を、トレーニングで奪って目の前に置き、置いた物へ歩いて入って
    /// しばらく進めても、エラーが出ず、試合が止まらない（ティックが進み続ける）ことを確かめる（2026-11-25 の指摘: 研究所の電球〈プラズマ球〉を
    /// 写すとフリーズする。ほかにも同じ不具合がないか、奪える物を全部試す）。置いた物は左右どちらの向きにも置く（左右の反転も試す）。
    /// </summary>
    public sealed class ThiefSnatchAllTests
    {
        /// <summary>置いてから進める時間（秒。シミュレーションは SpeedScale 倍で進む）と、その間に進むべき最小のティック数。</summary>
        private const float RunSeconds = 0.9f;
        private const float Speed = 2f;
        private const int MinTicks = 45;

        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator EveryStealableObject_CanBePlaced_WithoutErrorsOrStalls()
        {
            int savedCharacter = PlayerPrefs.GetInt(CharacterRoster.SelectedPreferenceKey, 0);
            var problems = new List<string>();
            string current = "(始まる前)";
            Application.LogCallback collect = (message, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                {
                    problems.Add($"{current}: {type}: {message}\n{FirstLines(stack, 6)}");
                }
            };
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += collect;
            int tried = 0;
            try
            {
                SceneManager.LoadScene("App");
                yield return null;
                var flow = UnityEngine.Object.FindFirstObjectByType<GameFlow>();
                yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Menu && !flow.IsLoading, 15f, "メニューにならない");
                foreach (StageDefinition stage in flow.Stages.Stages)
                {
                    current = $"{stage.SceneName}（読み込み）";
                    flow.StartTraining(stage);
                    yield return WaitFor(() => flow.Current == GameFlow.FlowScreen.Training && !flow.IsLoading
                        && SceneManager.GetActiveScene().name == stage.SceneName, 20f, $"{stage.SceneName} のトレーニングにならない");
                    yield return WaitSeconds(0.4f);
                    var runner = UnityEngine.Object.FindFirstObjectByType<SimulationRunner>();
                    ISimulationFeed feed = runner;
                    yield return BecomeThief(runner);
                    int zoneCount = feed.Zones.Count;
                    var items = new List<(StealKind Kind, int Index, string Name)>();
                    for (int i = 0; i < zoneCount; i++)
                    {
                        if (ThiefRules.IsStealable(feed.Zones[i]))
                        {
                            items.Add((StealKind.Zone, i, $"ギミック {i}（{feed.Zones[i].Kind}）"));
                        }
                    }
                    for (int i = 0; i < feed.Traps.Count; i++)
                    {
                        if (feed.Traps[i].Kind != StageTrapKind.WispRing && feed.Traps[i].Kind != StageTrapKind.DonutFryer)
                        {
                            items.Add((StealKind.Trap, i, $"仕掛け {i}（{feed.Traps[i].Kind}）"));
                        }
                    }
                    Vector3 start = ToUnity(runner.PlayerState.Position);
                    foreach (var item in items)
                    {
                        foreach (int side in new[] { 1, -1 })
                        {
                            current = $"{stage.SceneName} の {item.Name}（{(side > 0 ? "右" : "左")}に置く）";
                            Debug.Log($"[ThiefSnatchAll] {current}");
                            tried++;
                            int before = runner.CurrentTick;
                            yield return PlaceAndWalkIn(runner, item.Kind, item.Index, side, start);
                            int advanced = runner.CurrentTick - before;
                            if (advanced < MinTicks)
                            {
                                problems.Add($"{current}: 試合が止まった（{advanced} ティックしか進まない）");
                            }
                        }
                    }
                    runner.SpeedScale = 1f;
                }
            }
            finally
            {
                Application.logMessageReceived -= collect;
                LogAssert.ignoreFailingMessages = false;
                PlayerPrefs.SetInt(CharacterRoster.SelectedPreferenceKey, savedCharacter);
            }
            Debug.Log($"[ThiefSnatchAll] 試した数: {tried}");
            Assert.That(tried, Is.GreaterThan(20), "奪える物を試していない");
            Assert.That(problems, Is.Empty, "奪って置いたら不具合が出た:\n" + string.Join("\n", problems.Distinct().Take(20)));
        }

        private static IEnumerator BecomeThief(SimulationRunner runner)
        {
            for (int i = 0; i < 8 && ((ISimulationFeed)runner).CharacterOf(0).DisplayName != CharacterPresets.ThiefName; i++)
            {
                runner.CycleCharacter();
            }
            Assert.That(((ISimulationFeed)runner).CharacterOf(0).DisplayName, Is.EqualTo(CharacterPresets.ThiefName), "THIEF にならない");
            yield return WaitSeconds(0.2f);
        }

        // 出現位置へ戻し、奪ったことにして side の向き（+1 右・-1 左）へ Q を短く押して置き、その向きへ歩いて置いた物に入る。
        private static IEnumerator PlaceAndWalkIn(SimulationRunner runner, StealKind kind, int source, int side, Vector3 start)
        {
            CharacterState state = runner.PlayerState;
            state.Position = new System.Numerics.Vector3(start.x, start.y, start.z);
            state.Velocity = System.Numerics.Vector3.Zero;
            state.Action = default;
            state.Combat.CaptureTicks = 0;
            state.Combat.StealKind = kind;
            state.Combat.StealSource = source;
            state.Combat.StealUses = 1;
            state.Combat.Special1CooldownTicksLeft = 0;
            state.Facing = side;
            runner.SetPlayerState(state);
            var inputs = new List<SimInput>
            {
                SimInput.FromAxes(0f, 0f, InputButtons.Special1).WithAim(new System.Numerics.Vector2(side, 0f)),
                default,
            };
            for (int i = 0; i < 90; i++)
            {
                inputs.Add(SimInput.FromAxes(side, 0f, InputButtons.None));
            }
            runner.InputOverride = new Script(inputs);
            runner.SpeedScale = Speed;
            yield return WaitSeconds(RunSeconds);
            runner.InputOverride = null;
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v)
        {
            return new Vector3(v.X, v.Y, v.Z);
        }

        private static string FirstLines(string text, int lines)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var builder = new StringBuilder();
            foreach (string line in text.Split('\n').Take(lines))
            {
                builder.AppendLine("    " + line.Trim());
            }
            return builder.ToString();
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, string message)
        {
            float startTime = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - startTime > seconds)
                {
                    Assert.Fail(message);
                }
                yield return null;
            }
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float startTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startTime < seconds)
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
