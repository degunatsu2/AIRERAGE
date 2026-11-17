using System.Collections;
using System.IO;
using System.Linq;
using Airerage.Core.Ai;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// 試合へのゲート（D-99 の T12）と開始前の空撮（D-98 の I2）のコマを Logs/Snapshots/Intro_*.png に保存する（名前で選んだときだけ動く。
    /// 見た目の確認用）。シングルモードの試合を始め、ゲートの閉じる途中・閉じきった札・開く途中と、カウントダウンの間のカメラを撮る
    /// （上下の帯と名前の札は OnGUI で描くので、ここでは写らない）。
    /// </summary>
    [Explicit("画像を撮るときだけ")]
    public sealed class MatchIntroSnapshotTests
    {
        private const string Folder = "Logs/Snapshots";

        [UnityTest]
        public IEnumerator CaptureGateAndAerial()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            Directory.CreateDirectory(Folder);
            string sceneName = System.Environment.GetEnvironmentVariable("AIRERAGE_INTRO_STAGE") ?? "Stage_CityNight";
            StageDefinition stage = flow.Stages.Stages.First(s => s.SceneName == sceneName);
            var curtain = Object.FindFirstObjectByType<ScreenCurtain>(FindObjectsInactive.Include);
            var players = new[]
            {
                LocalPlayerSetup.Cpu(0, CpuLevel.Easy), LocalPlayerSetup.Cpu(1, CpuLevel.Easy), LocalPlayerSetup.Cpu(2, CpuLevel.Easy),
                LocalPlayerSetup.Cpu(3, CpuLevel.Easy),
            };
            float start = Time.realtimeSinceStartup;
            flow.StartLocal(stage, players, timed: true, single: true);
            Camera menuCamera = Object.FindFirstObjectByType<MenuCameraDrift>().GetComponent<Camera>();
            foreach (float at in new[] { 0.12f, 0.24f, 0.42f })
            {
                while (Time.realtimeSinceStartup - start < at)
                {
                    yield return null;
                }
                CaptureCanvas(curtain, menuCamera, $"Intro_Gate_{at:0.00}");
            }
            while (flow.Current != GameFlow.FlowScreen.Local || flow.IsLoading || SceneManager.GetActiveScene().name != sceneName)
            {
                yield return null;
            }
            yield return null;
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            float opened = Time.realtimeSinceStartup;
            foreach (float at in new[] { 0.1f, 0.25f })
            {
                while (Time.realtimeSinceStartup - opened < at)
                {
                    yield return null;
                }
                CaptureCanvas(curtain, camera, $"Intro_Open_{at:0.00}");
            }
            var intro = Object.FindFirstObjectByType<MatchIntroCamera>();
            Assert.That(intro, Is.Not.Null, "空撮のカメラがない");
            var hud = Object.FindFirstObjectByType<MatchHud>();
            // カウントダウンの基準の秒ごとに撮る（空撮・突進・戻り・READY）。
            foreach (float at in new[] { 0.6f, 1.5f, 2.4f, 3.1f, 3.3f, 3.5f, 3.8f, 4.3f })
            {
                while (hud.ShowingIntro && hud.IntroTime < at)
                {
                    yield return null;
                }
                Capture(camera, $"Intro_Camera_{at:0.0}_{intro.Stage}");
            }
        }

        private static void Capture(Camera camera, string name)
        {
            var texture = new RenderTexture(1920, 1080, 24);
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            Save(texture, name);
            camera.targetTexture = previous;
            Object.Destroy(texture);
        }

        // uGUI の画面（ゲート）をカメラの前に置いて一緒に撮る。
        private static void CaptureCanvas(ScreenCurtain curtain, Camera camera, string name)
        {
            Canvas canvas = curtain.GetComponentInParent<Canvas>(true).rootCanvas;
            var texture = new RenderTexture(1920, 1080, 24);
            RenderMode mode = canvas.renderMode;
            Camera previousCamera = canvas.worldCamera;
            try
            {
                camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.5f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Save(texture, name);
            }
            finally
            {
                camera.targetTexture = null;
                canvas.renderMode = mode;
                canvas.worldCamera = previousCamera;
                Object.Destroy(texture);
            }
        }

        private static void Save(RenderTexture texture, string name)
        {
            RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
            RenderTexture.active = null;
            Object.Destroy(image);
        }
    }
}
