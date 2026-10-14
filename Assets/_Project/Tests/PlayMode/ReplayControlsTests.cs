using System.Collections;
using System.Collections.Generic;
using System.IO;
using Airerage.Core.Input;
using Airerage.Runtime.DebugTools;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Airerage.Core.Input.PlayerInput;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 入力の記録と再生（docs/06_prototype_plan.md T1-9b）を Sandbox_Wire で動かして確かめる（キーの代わりにメソッドを呼ぶ）。
    /// 記録を始めてから決まった入力で動かして保存し、再生すると記録を始めた状態から始まり、最後の状態が一致することを確かめる。
    /// </summary>
    public sealed class ReplayControlsTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Application.temporaryCachePath, "ReplayControlsTests");
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [UnityTest]
        public IEnumerator RecordThenPlay_StartsWhereRecordingStarted_AndReachesTheSameFinalState()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var replay = Object.FindFirstObjectByType<ReplayControls>();
            Assert.That(replay, Is.Not.Null, "ReplayControls がない");
            yield return WaitSeconds(0.3f); // 出現位置から落ちて着地するまで（記録の前の動き）

            // 記録を始めてから、決まった入力で動かす（走る、ジャンプ、右上へワイヤー、解除、左へ走る）。
            replay.StartRecording();
            Airerage.Core.Simulation.CharacterState start = runner.PlayerState;
            var script = new ScriptedInput();
            script.Add(SimInput.FromAxes(1f, 0f, InputButtons.None), 20);
            script.Add(SimInput.FromAxes(1f, 0f, InputButtons.Jump), 3);
            script.Add(SimInput.FromAxes(1f, 0f, InputButtons.Wire).WithAim(new System.Numerics.Vector2(1f, 1.2f)), 60);
            script.Add(SimInput.FromAxes(-1f, 0f, InputButtons.None), 40);
            runner.InputOverride = script;
            yield return WaitUntil(() => runner.InputOverride == null, 10f);
            Assert.That(replay.IsRecording, Is.True);
            Assert.That(replay.RecordedTicks, Is.GreaterThanOrEqualTo(script.Count), "前提: 決まった入力を記録している");
            int recordedTicks = replay.RecordedTicks;

            string path = replay.StopRecording(_folder);
            Assert.That(path, Is.Not.Null, replay.Message);
            Assert.That(File.Exists(path), Is.True, "保存したファイルがない");
            Assert.That(replay.IsRecording, Is.False);

            // 再生は、記録を始めたときの状態から始まり、記録した長さで終わる。
            replay.PlayFile(path);
            Assert.That(replay.IsReplaying, Is.True, replay.Message);
            Assert.That(runner.PlayerState, Is.EqualTo(start), "再生が記録を始めたときの状態から始まらない");
            int startTick = runner.CurrentTick;
            yield return WaitUntil(() => !replay.IsReplaying, 30f);

            // 再生の最後のティックで、状態のチェックサムを記録と比べている。
            Assert.That(replay.LastReplayMatched, Is.True, replay.Message);
            Assert.That(runner.CurrentTick - startTick, Is.GreaterThanOrEqualTo(recordedTicks));
            Assert.That(runner.CurrentTick - startTick, Is.LessThan(recordedTicks + 30), "記録より長く再生している");
        }

        [UnityTest]
        public IEnumerator StopReplay_ReturnsControlImmediately()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var replay = Object.FindFirstObjectByType<ReplayControls>();
            replay.StartRecording();
            yield return WaitSeconds(0.5f);
            string path = replay.StopRecording(_folder);
            Assert.That(path, Is.Not.Null, replay.Message);

            replay.PlayFile(path);
            Assert.That(replay.IsReplaying, Is.True);
            replay.StopReplay();

            Assert.That(replay.IsReplaying, Is.False);
            Assert.That(runner.InputOverride, Is.Null, "入力の差し替えが残っている");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                {
                    Assert.Fail($"{timeoutSeconds} 秒以内に終わらなかった");
                }
                yield return null;
            }
        }

        private sealed class ScriptedInput : ITickInputSource
        {
            private readonly List<SimInput> _inputs = new List<SimInput>();
            private int _next;

            public int Count => _inputs.Count;

            public void Add(SimInput input, int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    _inputs.Add(input);
                }
            }

            public bool TryGetNext(out SimInput input)
            {
                if (_next >= _inputs.Count)
                {
                    input = default;
                    return false;
                }
                input = _inputs[_next++];
                return true;
            }
        }
    }
}
