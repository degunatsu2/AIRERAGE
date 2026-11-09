using System.Collections;
using System.IO;
using Airerage.Runtime.Flow;
using Airerage.Runtime.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Airerage.PlayMode.Tests
{
    /// <summary>
    /// メニューの演出（D-87・D-88）の途中のコマを Logs/Snapshots/Menu_*.png に保存する（名前で選んだときだけ動く。見た目の確認用）。
    /// メニューの Canvas をカメラの前に置いて、背景のステージと一緒に 1920×1080 で描く。
    /// </summary>
    [Explicit("画像を撮るときだけ")]
    public sealed class MenuMotionSnapshotTests
    {
        private const string Folder = "Logs/Snapshots";

        [UnityTest]
        public IEnumerator CaptureTransitionsAndButtons()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            var menu = Object.FindFirstObjectByType<MenuScreens>();
            yield return Wait(0.5f);
            Capture(menu, "Menu_Title");

            // 画面の切り替え: 進みを決まった秒数に合わせて撮る（この環境は 1 コマが遅いので、時刻を待つと途中を撮れない）。
            ScreenTransition transition = menu.Transition;
            menu.ContinueFromTitle();
            foreach (float t in new[] { 0.12f, 0.24f, 0.33f, 0.45f, 0.6f })
            {
                transition.SeekForTest(t);
                Capture(menu, $"Menu_SkyDive_{t * 100f:000}");
            }
            transition.Complete();
            yield return Wait(0.3f);
            Capture(menu, "Menu_Main");

            EventSystem.current.SetSelectedGameObject(menu.Training.gameObject);
            yield return null;
            Capture(menu, "Menu_Main_Select");
            yield return Wait(0.6f);
            Capture(menu, "Menu_Main_Training");

            menu.Training.Button.onClick.Invoke();
            foreach (float t in new[] { 0.05f, 0.18f, 0.3f, 0.42f, 0.55f, 0.7f, 0.85f })
            {
                transition.SeekForTest(t);
                Capture(menu, $"Menu_Slant_{t * 100f:000}");
            }
            transition.Complete();
            yield return Wait(0.3f);
            Capture(menu, "Menu_CharacterSelect");

            // キャラクター選択の決定（D-89）: ロゴが枠へ飛び、判が押される。
            CharacterSelectScreen characters = menu.CharacterSelect;
            characters.Icon(characters.CursorIndex(0)).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            yield return null;
            Capture(menu, "Menu_CharacterLock_A");
            yield return Wait(0.25f);
            Capture(menu, "Menu_CharacterLock_B");
            yield return Wait(0.6f);

            // 戻る（逆向きの風）。
            var back = typeof(MenuScreens).GetMethod("ShowMain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(back, Is.Not.Null);
            transition.Run(TransitionKind.Gust, -1, null, () => back.Invoke(menu, new object[] { menu.Training }));
            foreach (float t in new[] { 0.08f, 0.18f, 0.3f, 0.42f })
            {
                transition.SeekForTest(t);
                Capture(menu, $"Menu_Gust_{t * 100f:000}");
            }
            transition.Complete();
            yield return Wait(0.3f);

            // 試合へ入る黒い帯（閉じきったところ）。
            var curtain = Object.FindFirstObjectByType<ScreenCurtain>();
            flow.StartTraining(flow.Stages.Get(0));
            yield return Wait(0.48f);
            CaptureCanvas(curtain.GetComponent<Canvas>(), Object.FindFirstObjectByType<MenuCameraDrift>().GetComponent<Camera>(), "Menu_Curtain");
            while (flow.IsLoading)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 待機所の項目の「›››」と札の右の小さな説明（2026-11-09）: ホストとして待機所を開き、チーム対戦・アイテムのモードを変えながら
        /// 項目を選んで撮る（Logs/Snapshots/Waiting_*.png）。
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureWaitingRoom()
        {
            SceneManager.LoadScene("App");
            yield return null;
            var flow = Object.FindFirstObjectByType<GameFlow>();
            while (flow.Current != GameFlow.FlowScreen.Menu || flow.IsLoading)
            {
                yield return null;
            }
            var menu = Object.FindFirstObjectByType<MenuScreens>();
            menu.ContinueFromTitle();
            menu.Transition.Complete();
            yield return null;
            menu.Network.Button.onClick.Invoke();
            menu.Transition.Complete();
            yield return null;
            var online = Object.FindFirstObjectByType<Airerage.Net.OnlineMenu>();
            online.PortField.text = "0";
            online.NameField.text = "DEGU";
            online.Host();
            float start = Time.realtimeSinceStartup;
            while (online.ShownPanel != "Waiting" && Time.realtimeSinceStartup - start < 5f)
            {
                yield return null;
            }
            menu.Transition.Complete();
            yield return Wait(0.4f);
            yield return Shoot(menu, online.ReadyButton, "Waiting_Ready");
            online.ModeButton.Button.onClick.Invoke();
            yield return null;
            online.TeamHeader(3).Button.onClick.Invoke();
            yield return Wait(0.2f);
            yield return Shoot(menu, online.TeamButton, "Waiting_Team");
            online.ItemModeButton.Button.onClick.Invoke();
            yield return Wait(0.2f);
            yield return Shoot(menu, online.DropPoolButton, "Waiting_Drop");
            online.ItemModeButton.Button.onClick.Invoke();
            yield return Wait(0.2f);
            yield return Shoot(menu, online.ItemPickButton, "Waiting_Preset");
            Object.FindFirstObjectByType<Airerage.Net.OnlineConnection>().Leave();
            yield return null;
        }

        private static IEnumerator Shoot(MenuScreens menu, MenuOption option, string name)
        {
            EventSystem.current.SetSelectedGameObject(option.gameObject);
            yield return Wait(0.6f);
            Capture(menu, name);
        }

        private static void Capture(MenuScreens menu, string name)
        {
            Camera camera = Object.FindFirstObjectByType<MenuCameraDrift>().GetComponent<Camera>();
            CaptureCanvas(menu.GetComponent<Canvas>(), camera, name);
        }

        private static void CaptureCanvas(Canvas canvas, Camera camera, string name)
        {
            var size = new Vector2Int(1920, 1080);
            var texture = new RenderTexture(size.x, size.y, 24);
            RenderMode mode = canvas.renderMode;
            try
            {
                camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.5f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes($"{Folder}/{name}.png", image.EncodeToPNG());
                Object.Destroy(image);
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = null;
                canvas.renderMode = mode;
                Object.Destroy(texture);
            }
        }

        private static IEnumerator Until(float start, float seconds)
        {
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            yield return Until(Time.realtimeSinceStartup, seconds);
        }
    }
}
