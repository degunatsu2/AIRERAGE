using System;
using System.Collections.Generic;
using System.IO;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Airerage.Editor
{
    /// <summary>
    /// ゲームの流れのシーン（docs/15 T6-1、T6-2、T6-6。D-66）を作る:
    /// - App: いつも読み込んでおくシーン（GameFlow、EventSystem）。ビルドの最初のシーン。
    /// - Menu: タイトルとメインメニュー（uGUI）。背景にステージを映すカメラ。
    /// - Stage_Athletic: アスレチックステージ（地形、ギミック、撃破の範囲、コインの出現地点、拠点の候補、出現位置、光）。
    ///   Sandbox_Online と同じ配置（値は SandboxOnlineSceneBuilder の 1 か所）。
    /// - Match_Training: トレーニングの試合の仕組み（自分＋ダミー 3 体、表示、HUD、カメラ、一時停止）。ステージのシーンに重ねる。
    /// ステージの設定（StageDefinition）と一覧（StageCatalog）も作る。
    /// </summary>
    public static class FlowSceneBuilder
    {
        public const string AppScenePath = "Assets/_Project/Scenes/App.unity";
        public const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
        public const string AthleticStagePath = "Assets/_Project/Scenes/Stages/Stage_Athletic.unity";
        public const string TrainingScenePath = "Assets/_Project/Scenes/Modes/Match_Training.unity";
        public const string StageFolder = "Assets/_Project/Data/Stages";
        public const string AthleticStageAssetPath = StageFolder + "/Athletic.asset";
        public const string StageCatalogPath = StageFolder + "/StageCatalog.asset";

        // トレーニングのダミーの数（ステージの 2〜4 番目の出現位置に出す）。
        private const int TrainingDummies = 3;

        // ビルドの設定のシーンの順（最初が起動するシーン）。ほかのシーンは後ろに並べる。テンプレートの SampleScene は外す。
        private static readonly string[] BuildOrder =
        {
            AppScenePath, MenuScenePath, AthleticStagePath, TrainingScenePath,
        };

        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Airerage/ゲームの流れのシーンを作り直す")]
        private static void BuildFromMenu()
        {
            if (!EditorUtility.DisplayDialog("ゲームの流れ", "App・Menu・Stage_Athletic・Match_Training を作り直します。シーンを手で編集した内容は消えます。",
                    "作り直す", "やめる"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            Build();
        }

        /// <summary>4 つのシーンとステージの設定を作り直して保存し、ビルドの設定の順を整える。</summary>
        public static void Build()
        {
            BuildAthleticStage();
            BuildTraining();
            BuildMenu();
            BuildApp();
            ArrangeBuildSettings();
            Debug.Log("FlowSceneBuilder: App、Menu、Stage_Athletic、Match_Training を作った");
        }

        private static void BuildAthleticStage()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // 光はステージのものを使う（D-66）。カメラは試合の仕組み・メニューのシーンにある。
            Object.DestroyImmediate(Object.FindFirstObjectByType<Camera>().gameObject);

            Material terrainMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Terrain", new Color(0.55f, 0.55f, 0.58f));
            Material attachableMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("TerrainAttachable", new Color(0.25f, 0.6f, 0.75f));
            Material anchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Anchor", new Color(0.95f, 0.85f, 0.2f));
            SandboxWireSceneBuilder.CreateTerrain(SandboxOnlineSceneBuilder.Terrain, SandboxOnlineSceneBuilder.AnchorPoints,
                terrainMaterial, attachableMaterial, anchorMaterial);
            SandboxOnlineSceneBuilder.CreateZones();
            SandboxCombatSceneBuilder.CreateBounds(SandboxOnlineSceneBuilder.BoundsMin, SandboxOnlineSceneBuilder.BoundsMax);
            SandboxWireSceneBuilder.CreateCoinSpawns(SandboxOnlineSceneBuilder.CoinSpawns, SandboxOnlineSceneBuilder.JackpotSpots);
            SandboxWireSceneBuilder.CreateBankSpots(SandboxOnlineSceneBuilder.BankSpots);

            var spawns = new GameObject("SpawnPoints").transform;
            for (int p = 0; p < SandboxOnlineSceneBuilder.SpawnPositions.Length; p++)
            {
                var point = new GameObject($"Spawn{p + 1}");
                point.transform.SetParent(spawns, false);
                point.transform.position = SandboxOnlineSceneBuilder.SpawnPositions[p];
                point.AddComponent<SpawnPointAuthoring>().Configure(p);
            }
            Save(scene, AthleticStagePath);
        }

        private static void BuildTraining()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(Object.FindFirstObjectByType<Light>().gameObject);

            var character = SandboxWireSceneBuilder.LoadOrCreateCharacterConfig();
            InputActionAsset inputActions = LoadInputActions();
            Material characterMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Character", new Color(0.95f, 0.55f, 0.2f));
            Material wireMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Wire", new Color(0.6f, 0.2f, 0.85f), unlit: true);
            Material attachedAnchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("WireAnchor", new Color(1f, 0.5f, 0f), unlit: true);
            Material dummyMaterial = SandboxWireSceneBuilder.LoadDummyMaterial();

            // 位置は実行がステージの出現位置に合わせる。
            CharacterView characterView = SandboxWireSceneBuilder.CreateCharacterView("Character", characterMaterial, Vector3.zero);
            var dummyViews = new Object[TrainingDummies];
            for (int i = 0; i < TrainingDummies; i++)
            {
                dummyViews[i] = SandboxWireSceneBuilder.CreateCharacterView($"Dummy{i + 1}", dummyMaterial, Vector3.zero);
            }
            WireView wireView = SandboxWireSceneBuilder.CreateWireView("Wire", null, wireMaterial, attachedAnchorMaterial);
            SandboxWireSceneBuilder.CreateFollowCamera(characterView.transform, inputActions);
            AimIndicatorView aimIndicator = SandboxWireSceneBuilder.CreateAimIndicator();
            Camera camera = Object.FindFirstObjectByType<Camera>();

            var runner = new GameObject("Simulation").AddComponent<SimulationRunner>();
            SandboxWireSceneBuilder.SetReference(runner, "_character", character);
            SandboxWireSceneBuilder.SetReference(runner, "_roster", SandboxWireSceneBuilder.LoadOrCreateCharacterRoster(character));
            SetBool(runner, "_rememberCharacter", true);
            SandboxWireSceneBuilder.SetReference(runner, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(runner, "_characterView", characterView);
            SandboxWireSceneBuilder.SetReference(runner, "_wireView", wireView);
            SandboxWireSceneBuilder.SetReference(runner, "_aimIndicator", aimIndicator);
            SandboxWireSceneBuilder.SetReference(runner, "_camera", camera);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_dummyViews", dummyViews);

            SandboxWireSceneBuilder.CreateMissIndicator(runner, camera);
            GameAudio audio = SandboxWireSceneBuilder.CreateAudio(runner);
            SandboxWireSceneBuilder.CreateDebugTools(runner, inputActions, audio);
            SandboxWireSceneBuilder.CreateComboHud(runner);
            SandboxWireSceneBuilder.CreateSkillHud(runner, audio);
            SandboxWireSceneBuilder.CreateMatchHud(runner, audio);
            SandboxWireSceneBuilder.CreateNoticeHud(runner, audio);
            EdgeGuideLayer guides = SandboxWireSceneBuilder.CreateEdgeGuides();
            SandboxWireSceneBuilder.CreateCoinViews(runner, guides);
            SandboxWireSceneBuilder.CreateBankView(runner, audio, guides);
            SandboxWireSceneBuilder.CreatePlayerGuides(runner, guides);
            SandboxWireSceneBuilder.CreateScoreboard(runner, audio);
            SandboxWireSceneBuilder.CreateAtmosphere(runner, audio);
            MenuUiBuilder.CreatePauseMenu(runner, inputActions);
            Save(scene, TrainingScenePath);
        }

        private static void BuildMenu()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(Object.FindFirstObjectByType<Light>().gameObject);
            Camera camera = Object.FindFirstObjectByType<Camera>();
            camera.fieldOfView = 40f;
            camera.gameObject.AddComponent<MenuCameraDrift>();

            Canvas canvas = MenuUiBuilder.CreateCanvas("Menu", 0);
            var white = Color.white;

            // タイトル: 大きな「AIRERAGE」と、点滅する「PRESS ANY BUTTON」。
            RectTransform title = MenuUiBuilder.CreateFull(canvas.transform, "Title");
            MenuUiBuilder.CreateFill(title, "Band", new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, 0.46f), new Vector2(1f, 0.74f));
            MenuUiBuilder.CreateText(title, "Logo", "AIRERAGE", 150, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1600f, 200f), white);
            UnityEngine.UI.Text press = MenuUiBuilder.CreateText(title, "PressAnyButton", "PRESS ANY BUTTON", 34, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(800f, 60f), white);

            // メインメニュー: 左に縦に並んだ斜めの札。
            RectTransform main = MenuUiBuilder.CreateFull(canvas.transform, "Main");
            MenuUiBuilder.CreateFill(main, "Side", new Color(0f, 0f, 0f, 0.4f), Vector2.zero, new Vector2(0.42f, 1f));
            MenuUiBuilder.CreateText(main, "Logo", "AIRERAGE", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(90f, -60f), new Vector2(700f, 90f), white);
            var anchor = new Vector2(0f, 1f);
            var size = new Vector2(560f, 72f);
            string[] labels = { "SINGLE", "LOCAL VS", "NETWORK VS", "TRAINING", "OPTIONS", "QUIT" };
            var items = new MenuOption[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                items[i] = MenuUiBuilder.CreateMenuItem(main, labels[i].Replace(" ", string.Empty), labels[i], anchor,
                    new Vector2(110f, -230f - i * 88f), size);
            }
            MenuUiBuilder.CreateText(main, "Hint", "Up / Down: select     Enter / A: OK     Esc / B: back", 22, FontStyle.Bold,
                TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(90f, 50f), new Vector2(900f, 40f), new Color(1f, 1f, 1f, 0.7f));
            main.gameObject.SetActive(false);

            var screens = canvas.gameObject.AddComponent<MenuScreens>();
            SandboxWireSceneBuilder.SetReference(screens, "_title", title.gameObject);
            SandboxWireSceneBuilder.SetReference(screens, "_pressAnyButton", press);
            SandboxWireSceneBuilder.SetReference(screens, "_main", main.gameObject);
            string[] fields = { "_single", "_local", "_network", "_training", "_options", "_quit" };
            for (int i = 0; i < fields.Length; i++)
            {
                SandboxWireSceneBuilder.SetReference(screens, fields[i], items[i]);
            }
            SandboxWireSceneBuilder.CreateVersionLabel();
            Save(scene, MenuScenePath);
        }

        private static void BuildApp()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StageCatalog catalog = LoadOrCreateStages();
            var flow = new GameObject("GameFlow").AddComponent<GameFlow>();
            SandboxWireSceneBuilder.SetReference(flow, "_stages", catalog);
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            Save(scene, AppScenePath);
        }

        /// <summary>ステージの設定と一覧（今はアスレチックステージだけ）。値はビルドのたびにここの値で作り直す。</summary>
        internal static StageCatalog LoadOrCreateStages()
        {
            SandboxWireSceneBuilder.EnsureFolder(StageFolder);
            var athletic = AssetDatabase.LoadAssetAtPath<StageDefinition>(AthleticStageAssetPath);
            if (athletic == null)
            {
                athletic = ScriptableObject.CreateInstance<StageDefinition>();
                AssetDatabase.CreateAsset(athletic, AthleticStageAssetPath);
            }
            athletic.Configure("ATHLETIC", Path.GetFileNameWithoutExtension(AthleticStagePath),
                "Jump pads, updrafts and boost rings.", new Color(0.35f, 0.85f, 1f));
            EditorUtility.SetDirty(athletic);

            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(StageCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<StageCatalog>();
                AssetDatabase.CreateAsset(catalog, StageCatalogPath);
            }
            catalog.Configure(new[] { athletic });
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        // ビルドの設定のシーンの順を整える（App を最初に。テンプレートの SampleScene は外す。ファイルは消さない）。
        private static void ArrangeBuildSettings()
        {
            var ordered = new List<EditorBuildSettingsScene>();
            foreach (string path in BuildOrder)
            {
                ordered.Add(new EditorBuildSettingsScene(path, true));
            }
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path != SampleScenePath && Array.IndexOf(BuildOrder, existing.path) < 0)
                {
                    ordered.Add(existing);
                }
            }
            EditorBuildSettings.scenes = ordered.ToArray();
        }

        private static InputActionAsset LoadInputActions()
        {
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(SandboxWireSceneBuilder.InputActionsPath);
            if (inputActions == null)
            {
                throw new InvalidOperationException($"入力アクションのアセットがない: {SandboxWireSceneBuilder.InputActionsPath}");
            }
            return inputActions;
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name} に {field} がない");
            }
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Save(Scene scene, string path)
        {
            SandboxWireSceneBuilder.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {path}");
            }
            SandboxWireSceneBuilder.AddToBuildSettings(path);
        }
    }
}
