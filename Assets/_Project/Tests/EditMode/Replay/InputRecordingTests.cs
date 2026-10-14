using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Airerage.Core.Input;
using Airerage.Core.Physics;
using Airerage.Core.Replay;
using Airerage.Core.Simulation;
using Airerage.Core.Tests.Support;
using Airerage.Core.Wire;
using NUnit.Framework;

namespace Airerage.Core.Tests.Replay
{
    /// <summary>
    /// 入力の記録と再生（docs/06_prototype_plan.md T1-9b）。
    /// </summary>
    public class InputRecordingTests
    {
        private static readonly Vector3 Spawn = new Vector3(-6f, 0.91f, 0f);

        // 走る、ジャンプ、ワイヤーで振る、解除、空中ジャンプを含む入力列。
        private static List<PlayerInput> Script()
        {
            var script = new List<PlayerInput>();
            script.AddRange(Enumerable.Repeat(Inputs.Right, 20));
            script.AddRange(Enumerable.Repeat(Inputs.RightJump, 3));
            script.AddRange(Enumerable.Repeat(Inputs.Wire(0.4f, 1f), 70));
            script.AddRange(Enumerable.Repeat(Inputs.Right, 15));
            script.AddRange(Enumerable.Repeat(Inputs.Jump, 2));
            script.AddRange(Enumerable.Repeat(PlayerInput.FromAxes(-1f, 0f, InputButtons.Wire).WithAim(new Vector2(-1f, 2f)), 60));
            script.AddRange(Enumerable.Repeat(Inputs.None, 30));
            return script;
        }

        [TestCase(WireModelType.Inertial)]
        [TestCase(WireModelType.Responsive)]
        public void RecordSaveLoadAndSimulate_ReachesTheSameState(WireModelType modelType)
        {
            var config = new CharacterConfig();
            config.Wire.ModelType = modelType;
            var harness = new SimHarness(TestArenas.Playground(), config, Spawn);
            var recorder = new InputRecorder(10000);
            recorder.Restart(harness.State, modelType);
            foreach (PlayerInput input in Script())
            {
                harness.Step(input);
                recorder.Add(input, harness.State);
            }

            InputRecording loaded = InputRecording.Parse(recorder.Build("{}").ToText());
            CharacterState replayed = loaded.Simulate(TestArenas.Playground(), config);

            Assert.That(loaded.FinalChecksum, Is.EqualTo(StateCodec.Checksum(harness.State)));
            Assert.That(replayed, Is.EqualTo(harness.State));
            Assert.That(StateCodec.Checksum(replayed), Is.EqualTo(loaded.FinalChecksum));
        }

        [Test]
        public void Recorder_WhenFull_KeepsTheLatestInputsAndTheirStartState()
        {
            var config = new CharacterConfig();
            var harness = new SimHarness(TestArenas.Playground(), config, Spawn);
            var recorder = new InputRecorder(50);
            recorder.Restart(harness.State, config.Wire.ModelType);
            List<PlayerInput> script = Script();
            var before = new List<CharacterState>();
            foreach (PlayerInput input in script)
            {
                before.Add(harness.State);
                harness.Step(input);
                recorder.Add(input, harness.State);
            }

            InputRecording recording = recorder.Build(string.Empty);

            Assert.That(recorder.Count, Is.EqualTo(50));
            Assert.That(recording.Inputs, Is.EqualTo(script.Skip(script.Count - 50)));
            Assert.That(recording.StartState, Is.EqualTo(before[script.Count - 50]));
            Assert.That(recording.Simulate(TestArenas.Playground(), config), Is.EqualTo(harness.State));
        }

        [Test]
        public void Recorder_Restart_DropsEarlierInputs()
        {
            var recorder = new InputRecorder(100);
            var first = CharacterState.Spawn(Vector3.Zero, 1);
            var second = CharacterState.Spawn(new Vector3(5f, 0f, 0f), -1);
            recorder.Restart(first, WireModelType.Inertial);
            recorder.Add(Inputs.Right, first);
            recorder.Add(Inputs.Right, first);

            recorder.Restart(second, WireModelType.Responsive);
            recorder.Add(Inputs.Left, second);
            InputRecording recording = recorder.Build(string.Empty);

            Assert.That(recording.Inputs, Is.EqualTo(new[] { Inputs.Left }));
            Assert.That(recording.StartState, Is.EqualTo(second));
            Assert.That(recording.WireModelType, Is.EqualTo(WireModelType.Responsive));
        }

        [Test]
        public void Recorder_BuildWithoutInputs_Throws()
        {
            var recorder = new InputRecorder(10);
            recorder.Restart(default(CharacterState), WireModelType.Inertial);

            Assert.Throws<InvalidOperationException>(() => recorder.Build(string.Empty));
        }

        [Test]
        public void Text_RoundTripsEverything_AndMergesRepeatedInputs()
        {
            CharacterState start = CharacterState.Spawn(new Vector3(1f, 2f, 0f), -1);
            start.Velocity = new Vector3(0.1f, -0.3f, 0f);
            start.Wire.Phase = WirePhase.Cooldown;
            start.Wire.CooldownTicksLeft = 7;
            var inputs = new List<PlayerInput>();
            inputs.AddRange(Enumerable.Repeat(Inputs.Right, 100));
            inputs.AddRange(Enumerable.Repeat(PlayerInput.FromAxes(0.3f, -0.7f, InputButtons.Wire | InputButtons.Jump).WithAim(new Vector2(-1f, 1f)), 3));
            inputs.Add(Inputs.None);
            var recording = new InputRecording(start, WireModelType.Responsive, inputs, 0x0123456789abcdefUL, "{\"Wire\":{\"MaxLength\":12.5}}");

            string text = recording.ToText();
            InputRecording loaded = InputRecording.Parse(text);

            Assert.That(loaded.StartState, Is.EqualTo(start));
            Assert.That(loaded.WireModelType, Is.EqualTo(WireModelType.Responsive));
            Assert.That(loaded.Inputs, Is.EqualTo(inputs));
            Assert.That(loaded.FinalChecksum, Is.EqualTo(0x0123456789abcdefUL));
            Assert.That(loaded.ConfigText, Is.EqualTo(recording.ConfigText));
            int inputLines = text.Split('\n').SkipWhile(l => l != "inputs").Skip(1).TakeWhile(l => l != "end").Count();
            Assert.That(inputLines, Is.EqualTo(3), "同じ入力が続く部分は1行にまとめる");
        }

        [Test]
        public void Parse_AcceptsWindowsLineEndings()
        {
            var recording = new InputRecording(default(CharacterState), WireModelType.Inertial, new[] { Inputs.Jump }, 42UL, string.Empty);

            InputRecording loaded = InputRecording.Parse(recording.ToText().Replace("\n", "\r\n"));

            Assert.That(loaded.Inputs, Is.EqualTo(new[] { Inputs.Jump }));
            Assert.That(loaded.ConfigText, Is.Empty);
        }

        [Test]
        public void Parse_ARecordingFromBeforeTheRename_IsStillRead()
        {
            // ゲームの名前を AIRERAGE に変える前の記録（先頭が degunatsu-input-recording）も読める。
            var recording = new InputRecording(default(CharacterState), WireModelType.Inertial, new[] { Inputs.Jump }, 42UL, string.Empty);
            string text = recording.ToText();
            Assert.That(text, Does.StartWith("airerage-input-recording "), "前提: 新しい名前で書く");

            InputRecording loaded = InputRecording.Parse(text.Replace("airerage-input-recording", "degunatsu-input-recording"));
            Assert.That(loaded.Inputs, Is.EqualTo(new[] { Inputs.Jump }));
        }

        [TestCase("ticks 2", "ticks 3", "一致しません")]
        [TestCase("final-checksum 000000000000002a", "final-checksum xyz", "16進数")]
        [TestCase("wire-model Inertial", "wire-model Orbit", "不明なワイヤーのタイプ")]
        [TestCase("airerage-input-recording 1", "airerage-input-recording 99", "版")]
        public void Parse_Malformed_ThrowsWithLineNumber(string original, string broken, string expectedMessage)
        {
            var recording = new InputRecording(default(CharacterState), WireModelType.Inertial, new[] { Inputs.Jump, Inputs.Jump }, 42UL, string.Empty);
            string text = recording.ToText();
            Assert.That(text, Does.Contain(original), "前提: 置き換える行がある");

            var error = Assert.Throws<FormatException>(() => InputRecording.Parse(text.Replace(original, broken)));
            Assert.That(error.Message, Does.Contain("行目").And.Contain(expectedMessage));
        }

        [Test]
        public void Simulate_WithDifferentWireType_Throws()
        {
            var recording = new InputRecording(default(CharacterState), WireModelType.Responsive, new[] { Inputs.Jump }, 0UL, string.Empty);
            var config = new CharacterConfig();
            config.Wire.ModelType = WireModelType.Inertial;

            Assert.Throws<ArgumentException>(() => recording.Simulate(new BoxTerrain(), config));
        }
    }
}
