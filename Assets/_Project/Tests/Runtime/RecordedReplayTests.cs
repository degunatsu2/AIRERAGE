using System.Collections.Generic;
using System.IO;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Replay;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.DebugTools;
using Degunatsu.Runtime.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Degunatsu.Runtime.Tests
{
    /// <summary>
    /// InputRecordings フォルダの入力の記録（Sandbox_Wire で記録したもの）を、記録したときの調整値と Sandbox_Wire の地形で
    /// 再生し、最後の状態が記録と一致するかを確かめる。別の PC で記録したファイルで、PC 間の決定性
    /// （docs/04_network.md 3.5、R-11）を確かめるのに使う。
    /// シミュレーションのコードを変えると古い記録は一致しなくなるので、通常のテストでは実行しない
    /// （Test Runner で選んで実行するか、bash tools/unity/run_unity_tests.sh EditMode Degunatsu.Runtime.Tests.RecordedReplayTests）。
    /// </summary>
    [Explicit("入力の記録ファイルを確かめるときだけ実行する")]
    public sealed class RecordedReplayTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Sandbox_Wire.unity";

        [Test]
        public void AllRecordings_ReachTheirRecordedFinalState()
        {
            string folder = ReplayControls.DefaultFolder;
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.txt") : new string[0];
            if (files.Length == 0)
            {
                Assert.Ignore($"{folder} に記録がない");
            }

            BoxTerrain terrain = LoadSandboxTerrain();
            var failures = new List<string>();
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                InputRecording recording = InputRecording.Parse(File.ReadAllText(file));
                if (string.IsNullOrEmpty(recording.ConfigText))
                {
                    failures.Add($"{fileName}: 調整値が記録されていない");
                    continue;
                }
                var config = new CharacterConfig();
                JsonUtility.FromJsonOverwrite(recording.ConfigText, config);

                // 記録がどこまでの動きを含むかの目安（ワイヤーの接続の回数と、ロープが巻き付いた支点の最大の数）。
                int attaches = 0;
                int maxPivots = 0;
                bool wasAttached = false;
                CharacterState final = recording.Simulate(terrain, config, state =>
                {
                    bool attached = state.Wire.Phase == Degunatsu.Core.Wire.WirePhase.Attached;
                    attaches += attached && !wasAttached ? 1 : 0;
                    wasAttached = attached;
                    maxPivots = Mathf.Max(maxPivots, state.Wire.Pivots.Count);
                });
                ulong checksum = StateCodec.Checksum(final);
                if (checksum == recording.FinalChecksum)
                {
                    Debug.Log($"RecordedReplayTests: {fileName} は一致（{recording.Inputs.Count} ティック、{checksum:x16}、接続 {attaches} 回、巻き付きの支点は最大 {maxPivots}）");
                }
                else
                {
                    failures.Add($"{fileName}: 最後の状態が一致しない（記録 {recording.FinalChecksum:x16}、再生 {checksum:x16}）。再生の最後: {final}");
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static BoxTerrain LoadSandboxTerrain()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Physics.SyncTransforms();
                var errors = new List<string>();
                BoxTerrain terrain = SceneTerrainBuilder.Build(1 << 0, errors);
                Assert.That(errors, Is.Empty, "Sandbox_Wire の地形を読めない");
                return terrain;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
