using System.Collections;
using System.IO;
using System.Linq;
using Airerage.Core.Simulation;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NVector3 = System.Numerics.Vector3;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 背景とオブジェクトのクオリティアップ（D-97）の試合中の見た目を Logs/Snapshots/Play_*.png に保存する（名前で選んだときだけ動く。見た目の
    /// 確認用）。トレーニングでステージに入り、試合中だけ描く物（ギミックの光る線・風の柱・拠点の輪と光の柱・着地の光の輪）を撮る。
    /// </summary>
    [Explicit("画像を撮るときだけ")]
    public sealed class StageLookSnapshotTests
    {
        private const string Folder = "Logs/Snapshots";

        [UnityTest]
        public IEnumerator CaptureTrainingOnTheDressedStages()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            string[] scenes = (System.Environment.GetEnvironmentVariable("AIRERAGE_PLAY_STAGES") ?? "Stage_Athletic,Stage_CityNight").Split(',');
            foreach (string scene in scenes)
            {
                flow.StartTraining(flow.Stages.Stages.First(s => s.SceneName == scene));
                while (flow.Current != GameFlow.FlowScreen.Training || flow.IsLoading || SceneManager.GetActiveScene().name != scene)
                {
                    yield return null;
                }
                yield return Wait(1.2f);
                var runner = Object.FindFirstObjectByType<SimulationRunner>();
                Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
                Capture(camera, $"Play_{scene}_Start");

                // 高い所から落ちて着地する（光の輪）。
                CharacterState state = runner.PlayerState;
                state.Position = new NVector3(-3f, 6f, 0f);
                state.Velocity = new NVector3(0f, -14f, 0f);
                state.Grounded = false;
                runner.SetPlayerState(state);
                float start = Time.time;
                while (!runner.PlayerState.Grounded && Time.time - start < 2f)
                {
                    yield return null;
                }
                yield return Wait(0.12f);
                Capture(camera, $"Play_{scene}_Landing");
                yield return Wait(1.5f);
                Capture(camera, $"Play_{scene}_Later");

                // 風（周期のある風の予告と吹いている間を含むように、0.5 秒ごとに 7 秒）。
                for (int i = 0; i < 14; i++)
                {
                    yield return Wait(0.5f);
                    Capture(camera, $"Play_{scene}_Wind{i:00}");
                }
            }
        }

        private static void Capture(Camera camera, string name)
        {
            var texture = new RenderTexture(1920, 1080, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
            camera.targetTexture = previous;
            RenderTexture.active = null;
            Object.Destroy(texture);
            Object.Destroy(image);
        }

        private static IEnumerator Wait(float seconds)
        {
            float start = Time.time;
            while (Time.time - start < seconds)
            {
                yield return null;
            }
        }
    }
}
