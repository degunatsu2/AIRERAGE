using System.Collections;
using System.Collections.Generic;
using System.IO;
using Degunatsu.Core.Input;
using Degunatsu.Runtime.DebugTools;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SimInput = Degunatsu.Core.Input.PlayerInput;

namespace Degunatsu.PlayMode.Tests
{
    /// <summary>
    /// 入力の記録と再生（docs/06_prototype_plan.md T1-9b）を Sandbox_Wire で動かして確かめる（キーの代わりにメソッドを呼ぶ）。
    /// 決まった入力で動かして保存し、再生して最後の状態が一致することを確かめる。
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
        public IEnumerator SaveThenPlay_ReachesTheSameFinalState()
        {
            SceneManager.LoadScene("Sandbox_Wire");
            yield return null;

            var runner = Object.FindFirstObjectByType<SimulationRunner>();
            var replay = Object.FindFirstObjectByType<ReplayControls>();
            Assert.That(replay, Is.Not.Null, "ReplayControls がない");

            // 走る、ジャンプ、右上へワイヤー、解除、左へ走る。
            var script = new ScriptedInput();
            script.Add(SimInput.FromAxes(1f, 0f, InputButtons.None), 20);
            script.Add(SimInput.FromAxes(1f, 0f, InputButtons.Jump), 3);
            script.Add(SimInput.FromAxes(1f, 0f, InputButtons.Wire).WithAim(new System.Numerics.Vector2(1f, 1.2f)), 60);
            script.Add(SimInput.FromAxes(-1f, 0f, InputButtons.None), 40);
            runner.InputOverride = script;
            yield return WaitUntil(() => runner.InputOverride == null, 10f);
            Assert.That(replay.RecordedTicks, Is.GreaterThanOrEqualTo(script.Count), "前提: 決まった入力を記録している");

            string path = replay.SaveRecording(_folder);
            Assert.That(path, Is.Not.Null, replay.Message);
            Assert.That(File.Exists(path), Is.True, "保存したファイルがない");

            replay.PlayFile(path);
            Assert.That(replay.IsReplaying, Is.True, replay.Message);
            yield return WaitUntil(() => !replay.IsReplaying, 30f);

            // 再生の最後のティックで、状態のチェックサムを記録と比べている。
            Assert.That(replay.LastReplayMatched, Is.True, replay.Message);
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
