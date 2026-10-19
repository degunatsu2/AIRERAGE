using System;
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
    /// （トレーニングのキャラクターごと、技の説明、ローカル対戦）を、いくつかの画面の大きさ（16:9、横に長い画面、16:10）で PNG にする。画面に重ねる Canvas は、撮る間だけカメラの
    /// 前に置く。保存先は Logs/Snapshots/。仮想の画面が要る（-nographics は使えない）:
    /// xvfb-run -a Unity -batchmode -quit -projectPath . -executeMethod Airerage.Editor.UiSnapshot.CaptureMenus -logFile Logs/snapshot.log
    /// </summary>
    public static class UiSnapshot
    {
        private const string StageScenePath = "Assets/_Project/Scenes/Stages/Stage_Athletic.unity";
        private const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
        private const string OutputFolder = "Logs/Snapshots";

        private static readonly Vector2Int[] Sizes = { new Vector2Int(1920, 1080), new Vector2Int(1421, 682), new Vector2Int(1440, 900) };

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>メニューの画面をまとめて撮る（キャラクター選択: トレーニング、技の説明、ローカル対戦）。</summary>
        public static void CaptureMenus()
        {
            CaptureCharacterSelect();
            CaptureLocalSelect();
            CaptureOnline();
        }

        /// <summary>
        /// ステージの画像（docs/15 6.11）: ステージごとに、全体（遠くから）と、試合のカメラの距離（30m）で真ん中を撮る。ステージのシーンには
        /// カメラがないので、撮る間だけ置く。ギミックの線（StageZoneView）は実行中に作るので写らない（台・柱・飾り・空・光を確かめる）。
        /// </summary>
        public static void CaptureStages()
        {
            Directory.CreateDirectory(OutputFolder);
            var catalog = AssetDatabase.LoadAssetAtPath<Airerage.Runtime.Flow.StageCatalog>(FlowSceneBuilder.StageCatalogPath);
            foreach (Airerage.Runtime.Flow.StageDefinition stage in catalog.Stages)
            {
                string[] found = AssetDatabase.FindAssets($"{stage.SceneName} t:Scene");
                if (found.Length == 0)
                {
                    throw new InvalidOperationException($"ステージのシーンがない: {stage.SceneName}");
                }
                EditorSceneManager.OpenScene(AssetDatabase.GUIDToAssetPath(found[0]), OpenSceneMode.Single);
                var go = new GameObject("SnapshotCamera");
                Camera camera = go.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.farClipPlane = 1000f;
                try
                {
                    camera.fieldOfView = 55f;
                    go.transform.position = new Vector3(0f, 12f, -78f);
                    RenderCamera(camera, new Vector2Int(1920, 1080), $"{OutputFolder}/Stage_{stage.SceneName}_Overview.png");
                    camera.fieldOfView = 40f;
                    go.transform.position = new Vector3(0f, 6f, -30f);
                    RenderCamera(camera, new Vector2Int(1920, 1080), $"{OutputFolder}/Stage_{stage.SceneName}_Close.png");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            // ステージ選択の画面。
            MenuScreens screens = OpenMenu();
            Transform select = screens.transform.Find("StageSelect");
            ShowOnly(screens, select);
            select.GetComponent<StageSelectScreen>().ShowNetwork(true, 3);
            CaptureAll(screens, "StageSelect");
        }

        private static void RenderCamera(Camera camera, Vector2Int size, string path)
        {
            var texture = new RenderTexture(size.x, size.y, 24);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                Debug.Log($"[UiSnapshot] {path}");
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = null;
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>ネットワーク対戦の接続の画面と待機所（待機所の文字は、2 人がつながって 1 人が READY の例を直接入れる）。</summary>
        public static void CaptureOnline()
        {
            MenuScreens screens = OpenMenu();
            Transform connect = screens.transform.Find("OnlineConnect");
            ShowOnly(screens, connect);
            connect.Find("Name/Field").GetComponent<UnityEngine.UI.InputField>().text = "DEGU";
            connect.Find("Address/Field").GetComponent<UnityEngine.UI.InputField>().text = "192.168.0.12";
            connect.Find("Port/Field").GetComponent<UnityEngine.UI.InputField>().text = "7777";
            CaptureAll(screens, "OnlineConnect");

            Transform waiting = screens.transform.Find("OnlineWaiting");
            ShowOnly(screens, waiting);
            waiting.Find("Info").GetComponent<UnityEngine.UI.Text>().text = "HOSTING ON PORT 7777. FRIENDS JOIN WITH YOUR ADDRESS.";
            waiting.Find("Message").GetComponent<UnityEngine.UI.Text>().text = "WHEN EVERYONE IS READY, CHARACTER SELECT STARTS";
            string[] names = { "DEGU  (HOST)  (YOU)", "FRIEND", "WAITING...", "WAITING..." };
            string[] states = { "READY!", "NOT READY", string.Empty, string.Empty };
            for (int p = 0; p < names.Length; p++)
            {
                Transform slot = waiting.Find($"Slot{p + 1}P");
                slot.Find("Name").GetComponent<UnityEngine.UI.Text>().text = names[p];
                slot.Find("State").GetComponent<UnityEngine.UI.Text>().text = states[p];
            }
            CaptureAll(screens, "OnlineWaiting");
        }

        /// <summary>トレーニングのキャラクター選択（1P のカーソルをキャラクターごとに。最後のキャラクターでは技の説明も開く）。</summary>
        public static void CaptureCharacterSelect()
        {
            MenuScreens screens = OpenMenu();
            CharacterSelectScreen screen = PrepareSelect(screens);
            for (int index = 0; index < screen.Count; index++)
            {
                screen.ShowSingle(index);
                CaptureAll(screens, $"CharacterSelect_{index}");
            }
            OpenDetails(screen, 0);
            CaptureAll(screens, "CharacterSelect_Details");
        }

        /// <summary>
        /// ローカル対戦のキャラクター選択（1P がキーボードで READY、2P・3P がゲームパッドで選んでいる途中、4P は空き）。参加は機器の操作で
        /// しか起きないので、中の枠に直接入れる。
        /// </summary>
        public static void CaptureLocalSelect()
        {
            MenuScreens screens = OpenMenu();
            CharacterSelectScreen screen = PrepareSelect(screens);
            screen.ShowLocal(true);
            Type slotType = typeof(CharacterSelectScreen).GetNestedType("Slot", BindingFlags.NonPublic);
            var slots = (Array)typeof(CharacterSelectScreen).GetField("_slots", Private).GetValue(screen);
            slots.SetValue(PreviewSlot(slotType, "KEYBOARD", 0, true), 0);
            slots.SetValue(PreviewSlot(slotType, "GAMEPAD 1", 2, false), 1);
            slots.SetValue(PreviewSlot(slotType, "GAMEPAD 2", 2, false), 2);
            typeof(CharacterSelectScreen).GetMethod("Refresh", Private).Invoke(screen, null);
            CaptureAll(screens, "LocalSelect");
        }

        // メニューを開いてキャラクター選択だけを出す（再生していないので Awake は呼ばれない。日本語の字体を直接用意する）。
        private static CharacterSelectScreen PrepareSelect(MenuScreens screens)
        {
            var screen = Object.FindFirstObjectByType<CharacterSelectScreen>(FindObjectsInactive.Include);
            if (screen == null)
            {
                throw new InvalidOperationException("メニューのシーンにキャラクター選択の画面がない");
            }
            ShowOnly(screens, screen.transform);
            typeof(CharacterSelectScreen).GetMethod("UseJapaneseFont", Private).Invoke(screen, null);
            return screen;
        }

        // 技の説明をせり出しきった状態にする（効果音を鳴らさずに）。
        private static void OpenDetails(CharacterSelectScreen screen, int slot)
        {
            typeof(CharacterSelectScreen).GetField("_detailsSlot", Private).SetValue(screen, slot);
            typeof(CharacterSelectScreen).GetField("_detailsOpen", Private).SetValue(screen, 1f);
            typeof(CharacterSelectScreen).GetMethod("UpdateDetailsSlide", Private).Invoke(screen, null);
        }

        private static object PreviewSlot(Type slotType, string device, int character, bool ready)
        {
            object slot = Activator.CreateInstance(slotType, true);
            slotType.GetField("Joined").SetValue(slot, true);
            slotType.GetField("DeviceName").SetValue(slot, device);
            slotType.GetField("Character").SetValue(slot, character);
            slotType.GetField("Ready").SetValue(slot, ready);
            return slot;
        }

        private static void CaptureAll(MenuScreens screens, string name)
        {
            Directory.CreateDirectory(OutputFolder);
            foreach (Vector2Int size in Sizes)
            {
                string path = $"{OutputFolder}/{name}_{size.x}x{size.y}.png";
                Capture(screens.GetComponent<Canvas>(), size, path);
                Debug.Log($"[UiSnapshot] {path}");
            }
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
