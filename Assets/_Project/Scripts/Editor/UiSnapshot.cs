using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Airerage.Runtime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Airerage.Editor
{
    /// <summary>
    /// メニューの画面の画像を撮る（クラウドで配置を確かめる。docs/08 4.2）。ステージとメニューのシーンを開き、キャラクター選択の画面
    /// （キャラクターごと）とローカル対戦の参加の画面を、いくつかの画面の大きさ（16:9、横に長い画面、16:10）で PNG にする。画面に重ねる Canvas は、撮る間だけカメラの
    /// 前に置く。保存先は Logs/Snapshots/。仮想の画面が要る（-nographics は使えない）:
    /// xvfb-run -a Unity -batchmode -quit -projectPath . -executeMethod Airerage.Editor.UiSnapshot.CaptureMenus -logFile Logs/snapshot.log
    /// </summary>
    public static class UiSnapshot
    {
        private const string StageScenePath = "Assets/_Project/Scenes/Stages/Stage_Athletic.unity";
        private const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
        private const string OutputFolder = "Logs/Snapshots";

        private static readonly Vector2Int[] Sizes = { new Vector2Int(1920, 1080), new Vector2Int(1421, 682), new Vector2Int(1440, 900) };

        /// <summary>メニューの画面をまとめて撮る（キャラクター選択、ローカル対戦の参加）。</summary>
        public static void CaptureMenus()
        {
            CaptureCharacterSelect();
            CaptureLocalJoin();
        }

        public static void CaptureCharacterSelect()
        {
            MenuScreens screens = OpenMenu();
            var screen = Object.FindFirstObjectByType<CharacterSelectScreen>(FindObjectsInactive.Include);
            if (screen == null)
            {
                throw new InvalidOperationException("メニューのシーンにキャラクター選択の画面がない");
            }
            ShowOnly(screens, screen.transform);
            // 再生していないので Awake は呼ばれない。日本語の字体と、カーソルのあるキャラクターの表示を直接呼ぶ。
            typeof(CharacterSelectScreen).GetMethod("UseJapaneseFont", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, null);
            MethodInfo highlight = typeof(CharacterSelectScreen).GetMethod("Highlight", BindingFlags.Instance | BindingFlags.NonPublic);
            Directory.CreateDirectory(OutputFolder);
            for (int index = 0; index < screen.Count; index++)
            {
                highlight.Invoke(screen, new object[] { index });
                screen.Card(index).SetSelectedShift(new Vector2(0f, 16f));
                foreach (Vector2Int size in Sizes)
                {
                    string path = $"{OutputFolder}/CharacterSelect_{index}_{size.x}x{size.y}.png";
                    Capture(screens.GetComponent<Canvas>(), size, path);
                    Debug.Log($"[UiSnapshot] {path}");
                }
            }
        }

        /// <summary>
        /// ローカル対戦の参加の画面を撮る（P1 がキーボードで READY、P2 がゲームパッドで選んでいる途中、P3・P4 は空き）。参加は機器の操作で
        /// しか起きないので、中の一覧に直接入れる。
        /// </summary>
        public static void CaptureLocalJoin()
        {
            MenuScreens screens = OpenMenu();
            var screen = Object.FindFirstObjectByType<LocalJoinScreen>(FindObjectsInactive.Include);
            if (screen == null)
            {
                throw new InvalidOperationException("メニューのシーンにローカル対戦の参加の画面がない");
            }
            ShowOnly(screens, screen.transform);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type entryType = typeof(LocalJoinScreen).GetNestedType("Entry", BindingFlags.NonPublic);
            var entries = (IList)typeof(LocalJoinScreen).GetField("_entries", flags).GetValue(screen);
            entries.Clear();
            entries.Add(PreviewEntry(entryType, "KEYBOARD", 0, true));
            entries.Add(PreviewEntry(entryType, "GAMEPAD 1", 2, false));
            typeof(LocalJoinScreen).GetMethod("Refresh", flags).Invoke(screen, null);
            Directory.CreateDirectory(OutputFolder);
            foreach (Vector2Int size in Sizes)
            {
                string path = $"{OutputFolder}/LocalJoin_{size.x}x{size.y}.png";
                Capture(screens.GetComponent<Canvas>(), size, path);
                Debug.Log($"[UiSnapshot] {path}");
            }
        }

        private static object PreviewEntry(Type entryType, string device, int character, bool ready)
        {
            object entry = Activator.CreateInstance(entryType, true);
            entryType.GetField("DeviceName").SetValue(entry, device);
            entryType.GetField("Character").SetValue(entry, character);
            entryType.GetField("Ready").SetValue(entry, ready);
            entryType.GetField("Devices").SetValue(entry, new UnityEngine.InputSystem.InputDevice[0]);
            return entry;
        }

        private static MenuScreens OpenMenu()
        {
            EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Additive);
            var screens = Object.FindFirstObjectByType<MenuScreens>(FindObjectsInactive.Include);
            if (screens == null)
            {
                throw new InvalidOperationException("メニューのシーンに MenuScreens がない");
            }
            return screens;
        }

        // 画面 shown だけを出す。
        private static void ShowOnly(MenuScreens screens, Transform shown)
        {
            foreach (Transform child in screens.transform)
            {
                child.gameObject.SetActive(child == shown);
            }
        }

        // Canvas をカメラの前に置き、CanvasScaler と同じ倍率（1920×1080 を基準に、幅と高さの中間）で描いて保存する。
        private static void Capture(Canvas canvas, Vector2Int size, string path)
        {
            Camera camera = Object.FindFirstObjectByType<Camera>();
            var texture = new RenderTexture(size.x, size.y, 24);
            RenderMode mode = canvas.renderMode;
            var scaler = canvas.GetComponent<CanvasScaler>();
            try
            {
                camera.targetTexture = texture;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.5f;
                if (scaler != null)
                {
                    scaler.enabled = false;
                    Vector2 reference = scaler.referenceResolution;
                    float log = Mathf.Lerp(Mathf.Log(size.x / reference.x, 2f), Mathf.Log(size.y / reference.y, 2f), scaler.matchWidthOrHeight);
                    canvas.scaleFactor = Mathf.Pow(2f, log);
                }
                Canvas.ForceUpdateCanvases();
                foreach (var group in canvas.GetComponentsInChildren<LayoutGroup>(true))
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = null;
                canvas.renderMode = mode;
                if (scaler != null)
                {
                    scaler.enabled = true;
                }
                Object.DestroyImmediate(texture);
            }
        }
    }
}
