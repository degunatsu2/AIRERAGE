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
    /// （上下の帯と名前の札・READY ／ GO! は OnGUI で描くので、ここでは写らない）。
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
            flow.StartLocal(stage, players, timed: true, single: true);
            Camera menuCamera = Object.FindFirstObjectByType<MenuCameraDrift>().GetComponent<Camera>();
            CurtainGraphic bars = curtain.GetComponentInChildren<CurtainGraphic>(true);
            // 1 枚撮るのに 1 秒ほどかかる（ソフトウェアの描画）ので、時刻ではなく扉の状態を待って撮る（撮った後は次のフレームへ進める）。
            yield return Shoot(() => bars.Close >= 0.4f, () => CaptureCanvas(curtain, menuCamera, "Intro_Gate_1_Closing"));
            yield return Shoot(() => bars.Plate >= 0.5f, () => CaptureCanvas(curtain, menuCamera, "Intro_Gate_2_Plate"));
            while (flow.Current != GameFlow.FlowScreen.Local || flow.IsLoading || SceneManager.GetActiveScene().name != sceneName)
            {
                yield return null;
            }
            Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            yield return Shoot(() => bars.Open >= 0.2f || !curtain.Showing, () => CaptureCanvas(curtain, camera, "Intro_Gate_3_Opening"));
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

        /// <summary>
        /// 開始前の演出の画面全体（OnGUI で描く帯・ステージ名・札・Loading・ラウンドコール・試合中の表示のフェードインを含む）を
        /// Logs/Snapshots/IntroHud_*.png に撮る。バッチモードでない Unity（仮想の画面）で動かしたときだけ写る（バッチモードには画面がない）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureIntroHud()
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
            var players = new[]
            {
                LocalPlayerSetup.Cpu(0, CpuLevel.Easy), LocalPlayerSetup.Cpu(1, CpuLevel.Easy), LocalPlayerSetup.Cpu(2, CpuLevel.Easy),
                LocalPlayerSetup.Cpu(3, CpuLevel.Easy),
            };
            flow.StartLocal(stage, players, timed: true, single: true);
            while (flow.Current != GameFlow.FlowScreen.Local || flow.IsLoading || SceneManager.GetActiveScene().name != sceneName)
            {
                yield return null;
            }
            var hud = Object.FindFirstObjectByType<MatchHud>();
            foreach (float at in new[] { 0.3f, 1.2f, 2.1f, 2.35f, 3.2f, 3.6f, 4.2f, 5.4f, 6.5f })
            {
                // 始まった後（ROLL!）は、演出が終わるのを待つだけ。
                while (at <= MatchIntroTimeline.DesignSeconds ? hud.ShowingIntro && hud.IntroTime < at : hud.ShowingIntro)
                {
                    yield return null;
                }
                yield return new WaitForEndOfFrame();
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes($"{Folder}/IntroHud_{at:0.00}.png", shot.EncodeToPNG());
                Object.Destroy(shot);
                yield return null;
            }
        }

        // cond が満たされたフレームで shoot を 1 回だけ呼び、次のフレームへ進める（30 秒待っても満たされなければ失敗）。
        private static IEnumerator Shoot(System.Func<bool> cond, System.Action shoot)
        {
            float start = Time.realtimeSinceStartup;
            while (!cond())
            {
                Assert.That(Time.realtimeSinceStartup - start, Is.LessThan(30f), "撮る状態にならない");
                yield return null;
            }
            shoot();
            yield return null;
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
