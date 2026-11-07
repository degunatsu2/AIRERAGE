using System;
using System.Collections.Generic;
using System.IO;
using Airerage.Editor.Stages;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Flow;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using Airerage.Runtime.UI;
using Unity.Cinemachine;
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
    /// - App: いつも読み込んでおくシーン（GameFlow、EventSystem、メニューの効果音 UiSounds、メニューの BGM MusicPlayer）。ビルドの最初のシーン。
    /// - Menu: タイトル、メインメニュー、キャラクター選択、ステージ選択（uGUI）。背景にステージを映すカメラ。
    /// - Stage_Athletic: アスレチックステージ（地形、ギミック、撃破の範囲、コインの出現地点、拠点の候補、出現位置、光）。
    ///   Sandbox_Online と同じ配置（値は SandboxOnlineSceneBuilder の 1 か所）。
    /// - Stage_CityDay・Stage_CityNight・Stage_Volcano・Stage_SnowSea・Stage_SpaceStation: 追加のステージ（StageSceneBuilder。docs/15 6.11）。
    /// - Match_Training: トレーニングの試合の仕組み（自分＋ダミー 3 体、表示、HUD、カメラ、一時停止）。ステージのシーンに重ねる。
    /// - Match_Local: ローカル対戦の試合の仕組み（4 人分の表示、全員が入るカメラ、人ごとの技の札、HUD、一時停止、結果の項目。T6-5、T6-8）。
    ///   ステージのシーンに重ねる。
    /// - Match_Online: ネットワーク対戦の試合の仕組み（App の接続を使う OnlineRunner、結果の項目。T6-7、T6-8）。ステージのシーンに重ねる。
    /// ステージの設定（StageDefinition）と一覧（StageCatalog）も作る。
    /// </summary>
    public static class FlowSceneBuilder
    {
        public const string AppScenePath = "Assets/_Project/Scenes/App.unity";
        public const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
        public const string AthleticStagePath = "Assets/_Project/Scenes/Stages/Stage_Athletic.unity";
        public const string TrainingScenePath = "Assets/_Project/Scenes/Modes/Match_Training.unity";
        public const string LocalScenePath = "Assets/_Project/Scenes/Modes/Match_Local.unity";
        public const string OnlineMatchScenePath = "Assets/_Project/Scenes/Modes/Match_Online.unity";
        public const string StageFolder = "Assets/_Project/Data/Stages";
        public const string AthleticStageAssetPath = StageFolder + "/Athletic.asset";
        public const string StageCatalogPath = StageFolder + "/StageCatalog.asset";

        // トレーニングのダミーの数（ステージの 2〜4 番目の出現位置に出す）。
        private const int TrainingDummies = 3;

        // ビルドの設定のシーンの順（最初が起動するシーン）。ほかのシーンは後ろに並べる。テンプレートの SampleScene は外す。
        private static readonly string[] BuildOrder = CreateBuildOrder();

        private static string[] CreateBuildOrder()
        {
            var order = new List<string> { AppScenePath, MenuScenePath, AthleticStagePath };
            foreach (StageLayout layout in StageSceneBuilder.Layouts)
            {
                order.Add(StageSceneBuilder.ScenePathOf(layout));
            }
            order.AddRange(new[] { TrainingScenePath, LocalScenePath, OnlineMatchScenePath });
            return order.ToArray();
        }

        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Airerage/ゲームの流れのシーンを作り直す")]
        private static void BuildFromMenu()
        {
            if (!EditorUtility.DisplayDialog("ゲームの流れ", "App・Menu・ステージ・Match_Training・Match_Local・Match_Online を作り直します。シーンを手で編集した内容は消えます。",
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

        /// <summary>シーンとステージの設定を作り直して保存し、ビルドの設定の順を整える。</summary>
        public static void Build()
        {
            BuildAthleticStage();
            StageSceneBuilder.BuildAll();
            BuildTraining();
            BuildLocal();
            BuildOnlineMatch();
            BuildMenu();
            BuildApp();
            ArrangeBuildSettings();
            Debug.Log("FlowSceneBuilder: App、Menu、ステージ、Match_Training、Match_Local、Match_Online を作った");
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
            SandboxWireSceneBuilder.SetBool(runner, "_rememberCharacter", true);
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
            SandboxWireSceneBuilder.CreateNoticeHud(runner);
            EdgeGuideLayer guides = SandboxWireSceneBuilder.CreateEdgeGuides();
            SandboxWireSceneBuilder.CreateCoinViews(runner, guides);
            SandboxWireSceneBuilder.CreateBankView(runner, audio, guides);
            SandboxWireSceneBuilder.CreatePlayerGuides(runner, guides);
            SandboxWireSceneBuilder.CreateScoreboard(runner, audio);
            SandboxWireSceneBuilder.CreateAtmosphere(runner, audio);
            MenuUiBuilder.CreatePauseMenu(runner, inputActions);
            Save(scene, TrainingScenePath);
        }

        // ローカル対戦の試合の仕組み（docs/15 T6-5）: 8 人分の表示（オンラインと同じ色。人は 4 人まで、残りは CPU。T9-6）、全員が入るカメラ、
        // 人ごとの技の札、HUD、一時停止。
        private static void BuildLocal()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(Object.FindFirstObjectByType<Light>().gameObject);

            CharacterRoster roster = SandboxWireSceneBuilder.LoadOrCreateCharacterRoster(SandboxWireSceneBuilder.LoadOrCreateCharacterConfig());
            InputActionAsset inputActions = LoadInputActions();
            int count = LocalMatchRunner.MaxPlayers;
            CreatePlayerViews(count, true, out Object[] characterViews, out Object[] wireViews, out Object[] aimIndicators, out Color[] colors);

            // 全員が入るカメラ: Cinemachine が追う点を SharedCamera が全員の中心へ動かし、距離を変える（ホイールのズームは使わない）。
            var cameraTarget = new GameObject("CameraTarget").transform;
            CinemachineCamera follow = SandboxWireSceneBuilder.CreateFollowCamera(cameraTarget, inputActions);
            Object.DestroyImmediate(follow.GetComponent<CameraZoom>());
            Camera camera = Object.FindFirstObjectByType<Camera>();

            var runner = new GameObject("LocalMatch").AddComponent<LocalMatchRunner>();
            SandboxWireSceneBuilder.SetReference(runner, "_roster", roster);
            SandboxWireSceneBuilder.SetReference(runner, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(runner, "_camera", camera);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_characterViews", characterViews);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_wireViews", wireViews);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_aimIndicators", aimIndicators);
            SandboxOnlineSceneBuilder.SetColors(runner, "_playerColors", colors);

            var shared = new GameObject("SharedCamera").AddComponent<SharedCamera>();
            SandboxWireSceneBuilder.SetReference(shared, "_source", runner);
            SandboxWireSceneBuilder.SetReference(shared, "_composer", follow.GetComponent<CinemachinePositionComposer>());
            SandboxWireSceneBuilder.SetReference(shared, "_target", cameraTarget);
            SandboxWireSceneBuilder.SetReference(shared, "_camera", camera);

            GameAudio audio = SandboxWireSceneBuilder.CreateAudio(runner);
            SandboxWireSceneBuilder.CreateComboHud(runner, everyone: true);
            // 技の札は人の分だけ（人は 4 人まで。CPU には出さない）。
            SandboxWireSceneBuilder.CreateSkillHud(runner, audio, CharacterSelectScreen.MaxLocalPlayers);
            // 頭上のプレイヤーの番号の札（ローカル対戦だけ。全員が入るカメラで自分を見失わないように。2026-10-22）。
            var tags = new GameObject("PlayerTagHud").AddComponent<PlayerTagHud>();
            SandboxWireSceneBuilder.SetReference(tags, "_source", runner);
            SandboxWireSceneBuilder.SetReferenceArray(tags, "_views", characterViews);
            SandboxWireSceneBuilder.SetReference(tags, "_camera", camera);
            MatchHud matchHud = SandboxWireSceneBuilder.CreateMatchHud(runner, audio);
            SandboxWireSceneBuilder.CreateNoticeHud(runner);
            EdgeGuideLayer guides = SandboxWireSceneBuilder.CreateEdgeGuides();
            SandboxWireSceneBuilder.CreateCoinViews(runner, guides);
            SandboxWireSceneBuilder.CreateBankView(runner, audio, guides);
            SandboxWireSceneBuilder.CreatePlayerGuides(runner, guides);
            SandboxWireSceneBuilder.CreateScoreboard(runner, audio);
            SandboxWireSceneBuilder.CreateAtmosphere(runner, audio);
            MenuUiBuilder.CreatePauseMenu(runner, inputActions);
            MenuUiBuilder.CreateResultsMenu(runner, matchHud, inputActions);
            Save(scene, LocalScenePath);
        }

        // プレイヤー count 人分の表示（オンラインと同じ色の箱、向きの印、判定の線、ためる光、戦闘の表示、ワイヤー）。withAim なら人ごとの照準も。
        private static void CreatePlayerViews(int count, bool withAim, out Object[] characterViews, out Object[] wireViews,
            out Object[] aimIndicators, out Color[] colors)
        {
            Material wireMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Wire", new Color(0.6f, 0.2f, 0.85f), unlit: true);
            Material attachedAnchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("WireAnchor", new Color(1f, 0.5f, 0f), unlit: true);
            characterViews = new Object[count];
            wireViews = new Object[count];
            aimIndicators = new Object[withAim ? count : 0];
            colors = new Color[count];
            var playersRoot = new GameObject("Players").transform;
            for (int p = 0; p < count; p++)
            {
                var (materialName, color) = SandboxOnlineSceneBuilder.PlayerColors[p];
                colors[p] = color;
                Material material = SandboxWireSceneBuilder.LoadOrCreateMaterial(materialName, color);
                GameObject characterGo = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, $"Player{p + 1}", material, Vector3.one);
                characterGo.transform.SetParent(playersRoot, false);
                var view = characterGo.AddComponent<CharacterView>();
                SandboxWireSceneBuilder.AddFacingMarker(view);
                SandboxWireSceneBuilder.SetReference(view, "_hitboxLine", SandboxWireSceneBuilder.CreateHitboxLine($"Player{p + 1}Hitbox"));
                SandboxWireSceneBuilder.SetReference(view, "_chargeEffect", SandboxWireSceneBuilder.CreateChargeEffect($"Player{p + 1}Charge"));
                SandboxWireSceneBuilder.AddCombatIndicators(view, $"Player{p + 1}", playersRoot, color);
                characterViews[p] = view;
                wireViews[p] = SandboxWireSceneBuilder.CreateWireView($"Player{p + 1}Wire", playersRoot, wireMaterial, attachedAnchorMaterial);
                if (withAim)
                {
                    // ローカル対戦の照準は、射程の弧と導線の根元をプレイヤーの色にする（4 人の照準を見分ける）。
                    AimIndicatorView aim = SandboxWireSceneBuilder.CreateAimIndicator(color);
                    aim.name = $"Player{p + 1}Aim";
                    aimIndicators[p] = aim;
                }
            }
        }

        // ネットワーク対戦の試合の仕組み（docs/15 T6-7）: App の接続を使う OnlineRunner、4 人分の表示、自分を追うカメラ、HUD。
        // ステージのシーンに重ねる（ロビーはメニューで済んでいる）。
        private static void BuildOnlineMatch()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(Object.FindFirstObjectByType<Light>().gameObject);

            CharacterConfigAsset character = SandboxWireSceneBuilder.LoadOrCreateCharacterConfig();
            CharacterRoster roster = SandboxWireSceneBuilder.LoadOrCreateCharacterRoster(character);
            InputActionAsset inputActions = LoadInputActions();
            CreatePlayerViews(Airerage.Net.OnlineSession.MaxPlayers, false, out Object[] characterViews, out Object[] wireViews, out _,
                out Color[] colors);
            CinemachineCamera follow = SandboxWireSceneBuilder.CreateFollowCamera(null, inputActions);
            AimIndicatorView aimIndicator = SandboxWireSceneBuilder.CreateAimIndicator();
            Camera camera = Object.FindFirstObjectByType<Camera>();

            var runner = new GameObject("Online").AddComponent<Airerage.Net.OnlineRunner>();
            SandboxWireSceneBuilder.SetReference(runner, "_character", character);
            SandboxWireSceneBuilder.SetReference(runner, "_roster", roster);
            SandboxWireSceneBuilder.SetReference(runner, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(runner, "_camera", camera);
            SandboxWireSceneBuilder.SetReference(runner, "_followCamera", follow);
            SandboxWireSceneBuilder.SetReference(runner, "_aimIndicator", aimIndicator);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_characterViews", characterViews);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_wireViews", wireViews);
            SandboxOnlineSceneBuilder.SetColors(runner, "_playerColors", colors);
            SandboxWireSceneBuilder.SetBool(runner, "_useConnection", true);

            GameAudio audio = SandboxWireSceneBuilder.CreateAudio(runner);
            SandboxWireSceneBuilder.SetReference(runner, "_audio", audio);
            SandboxWireSceneBuilder.CreateComboHud(runner);
            SandboxWireSceneBuilder.CreateMissIndicator(runner, camera);
            SandboxWireSceneBuilder.CreateSkillHud(runner, audio);
            // チーム対戦の間だけ、頭上にチームの色の名前の札（敵と味方を見分ける。T9-6）。
            var tags = new GameObject("PlayerTagHud").AddComponent<PlayerTagHud>();
            SandboxWireSceneBuilder.SetReference(tags, "_source", runner);
            SandboxWireSceneBuilder.SetReferenceArray(tags, "_views", characterViews);
            SandboxWireSceneBuilder.SetReference(tags, "_camera", camera);
            SandboxWireSceneBuilder.SetBool(tags, "_teamMatchesOnly", true);
            MatchHud matchHud = SandboxWireSceneBuilder.CreateMatchHud(runner, audio);
            SandboxWireSceneBuilder.CreateNoticeHud(runner);
            EdgeGuideLayer guides = SandboxWireSceneBuilder.CreateEdgeGuides();
            SandboxWireSceneBuilder.CreateCoinViews(runner, guides);
            SandboxWireSceneBuilder.CreateBankView(runner, audio, guides);
            SandboxWireSceneBuilder.CreatePlayerGuides(runner, guides);
            SandboxWireSceneBuilder.CreateScoreboard(runner, audio);
            SandboxWireSceneBuilder.CreateAtmosphere(runner, audio);
            MenuUiBuilder.CreateResultsMenu(runner, matchHud, inputActions);
            Save(scene, OnlineMatchScenePath);
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
            // ロゴはいちばん太い字（Orbitron Black）。
            MenuUiBuilder.CreateText(title, "Logo", "AIRERAGE", 150, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(1600f, 200f), white).font = GameFonts.LatinBlack;
            UnityEngine.UI.Text press = MenuUiBuilder.CreateText(title, "PressAnyButton", "PRESS ANY BUTTON", 34, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(800f, 60f), white);

            // メインメニュー: 左に縦に並んだ斜めの札。
            RectTransform main = MenuUiBuilder.CreateFull(canvas.transform, "Main");
            // 左の背景は、白地に選んでいる項目の色の斜めの帯（D-91 の M2 を白地に。2026-11-08）。ロゴは黒。
            MainMenuPanel panel = MenuUiBuilder.CreateWhitePanel(main, "Side", Vector2.zero, new Vector2(0.42f, 1f), 0.5f, 0.43f, 0f, MenuAccents.Single);
            MenuUiBuilder.CreateText(main, "Logo", "AIRERAGE", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(90f, -60f), new Vector2(700f, 90f), MenuUiBuilder.Ink, false).font = GameFonts.LatinBlack;
            var anchor = new Vector2(0f, 1f);
            var size = new Vector2(560f, 72f);
            // LOCAL VS ・ NETWORK VS は、右の大きな名前が札にかかるので短くした（ユーザーの要望、2026-11-07）。
            string[] labels = { "SINGLE", "LOCAL", "NETWORK", "TRAINING", "OPTIONS", "QUIT" };
            // 項目の色（札の縁・裏の札・残像・縁を回る光・左の絵・右の大きな名前。2026-11-07）。
            Color[] accents = MenuUiBuilder.MainAccents;
            var items = new MenuOption[labels.Length];
            // 項目の左の小さな絵（D-88 の B12）と、選んでいない項目を暗く下げる（B13）。
            MenuIcon.Shape[] shapes =
            {
                MenuIcon.Shape.Person, MenuIcon.Shape.Pads, MenuIcon.Shape.Globe, MenuIcon.Shape.Target, MenuIcon.Shape.Gear, MenuIcon.Shape.Door,
            };
            // 札の右にすべり出る小さな説明（W16。D-90。2026-11-08）。
            string[] subsJapanese = { "CPUと対戦", "1台で最大4人", "参加コード・LAN", "練習", "設定", "終了" };
            string[] subsEnglish = { "VS CPU", "UP TO 4 ON 1 PC", "JOIN CODE / LAN", "PRACTICE", "SETTINGS", "EXIT GAME" };
            for (int i = 0; i < labels.Length; i++)
            {
                items[i] = MenuUiBuilder.CreateMenuItem(main, labels[i].Replace(" ", string.Empty), labels[i], anchor,
                    new Vector2(110f, -230f - i * 88f), size);
                MenuUiBuilder.AddMenuIcon(items[i], shapes[i]);
                // 白い背景の上なので、下げても濃いめに（0.5 では札が灰色になって名前が読みにくい。2026-11-08）。
                items[i].SetRecede(true, 0.82f);
                items[i].SetAccent(accents[i], true);
                MenuUiBuilder.AddTextMotion(items[i], true, subsEnglish[i], subsJapanese[i]);
            }
            CreateMainBackdrop(main, items, panel);
            MenuUiBuilder.CreateHint(main, "↑↓: select     Enter / A: OK     Esc / B: back", "↑↓:選択　　Enter ／ A: 決定　　Esc ／ B: 戻る");
            main.gameObject.SetActive(false);

            CharacterSelectScreen characterSelect = CharacterSelectUiBuilder.Create(canvas.transform);
            StageSelectScreen stageSelect = CreateStageSelect(canvas.transform);
            OptionsScreen options = OptionsUiBuilder.Create(canvas.transform, LoadInputActions());
            ItemSelectScreen itemSelect = ItemSelectUiBuilder.Create(canvas.transform, LoadInputActions());

            CpuLevelScreen cpuLevel = CreateCpuLevel(canvas.transform);

            var screens = canvas.gameObject.AddComponent<MenuScreens>();
            SandboxWireSceneBuilder.SetReference(screens, "_cpuLevel", cpuLevel);
            SandboxWireSceneBuilder.SetReference(screens, "_characterSelect", characterSelect);
            SandboxWireSceneBuilder.SetReference(screens, "_stageSelect", stageSelect);
            SandboxWireSceneBuilder.SetReference(screens, "_optionsScreen", options);
            SandboxWireSceneBuilder.SetReference(screens, "_itemSelect", itemSelect);
            Airerage.Net.OnlineMenu online = OnlineMenuUiBuilder.Create(canvas.transform, screens, characterSelect, stageSelect, LoadOrCreateStages(),
                itemSelect);
            SandboxWireSceneBuilder.SetReference(screens, "_online", online);
            // 終了の確認（いちばん手前に出すため、ほかの画面の後に作る）。
            QuitConfirmDialog quitDialog = MenuUiBuilder.CreateQuitDialog(canvas.transform);
            SandboxWireSceneBuilder.SetReference(screens, "_quitDialog", quitDialog);
            // 画面の切り替えの演出（D-87。いちばん最後の子。画面の写しはこの直前に置く）。
            RectTransform transitionLayer = MenuUiBuilder.CreateFull(canvas.transform, "Transition");
            var transition = transitionLayer.gameObject.AddComponent<ScreenTransition>();
            transition.Configure((RectTransform)canvas.transform, new[] { quitDialog.gameObject }, camera.GetComponent<MenuCameraDrift>());
            SandboxWireSceneBuilder.SetReference(screens, "_transition", transition);
            SandboxWireSceneBuilder.SetReference(online, "_transition", transition);
            // クリックした所の火花（2026-11-07）: 画面の切り替えの写しより手前。
            MenuUiBuilder.CreateClickSparks(canvas.transform);
            SandboxWireSceneBuilder.SetReference(screens, "_title", title.gameObject);
            SandboxWireSceneBuilder.SetReference(screens, "_pressAnyButton", press);
            SandboxWireSceneBuilder.SetReference(screens, "_main", main.gameObject);
            string[] fields = { "_single", "_local", "_network", "_training", "_options", "_quit" };
            for (int i = 0; i < fields.Length; i++)
            {
                SandboxWireSceneBuilder.SetReference(screens, fields[i], items[i]);
            }
            Save(scene, MenuScenePath);
        }

        // メインメニューの背景の連動（D-88 の B7。ユーザーの選択、2026-11-07）: 右側に、選んでいる項目の名前の見出しの札（白地に黒の
        // 文字、縁と裏の札は項目の色。2026-11-08）と説明。左の暗い面の後ろ（ロゴと項目より奥）に置く。
        private static void CreateMainBackdrop(RectTransform main, MenuOption[] items, MainMenuPanel panel)
        {
            RectTransform root = MenuUiBuilder.CreateFull(main, "Backdrop");
            root.SetSiblingIndex(1);
            HeadingPlate heading = MenuUiBuilder.CreateHeadingPlate(root, "Heading", new Vector2(1f, 0.66f), new Vector2(1f, 0.5f),
                new Vector2(-96f, 0f), 170f, 112, 980f, 60f);
            // 説明は見出しの下の説明の札（白地に黒の文字。縁と裏の札は項目の色。ステージ選択と同じ。2026-11-08）。
            DescriptionPlate description = MenuUiBuilder.CreateDescriptionPlate(root, "DescriptionPlate", new Vector2(1f, 0.5f),
                new Vector2(-100f, 40f), 900f, 28);
            string[] english =
            {
                "Battle 3 CPUs. Four difficulty levels.",
                "Up to 4 players on one PC, 8 with CPUs.",
                "Online with a join code, or on your LAN. Up to 8 players.",
                "Practice with any character on any stage.",
                "Volume, screen, controls, items and language.",
                "Close the game.",
            };
            string[] japanese =
            {
                "CPU3体と対戦する。強さは4段階。",
                "1台のPCで最大4人。CPUと合わせて8人まで。",
                "参加コードかLANで、最大8人で対戦する。",
                "好きなキャラクターとステージで練習する。",
                "音量・画面・操作・アイテム・言語の設定。",
                "ゲームを終了する。",
            };
            var backdrop = main.gameObject.AddComponent<MainMenuBackdrop>();
            backdrop.Configure(items, english, japanese, heading, description, panel);
        }

        // シングルモードの CPU の強さ（docs/16 T7-2）: 左に EASY・NORMAL・HARD・HELL の札（CpuLevel の順）、右に説明。
        private static CpuLevelScreen CreateCpuLevel(Transform canvas)
        {
            RectTransform panel = MenuUiBuilder.CreateFull(canvas, "CpuLevel");
            // 左はメインメニューと同じ白い背景（帯は SINGLE の色）、見出しは黒（2026-11-08）。
            MenuUiBuilder.CreateWhitePanel(panel, "Side", Vector2.zero, new Vector2(0.42f, 1f), 0.5f, 0.43f, 0f, MenuAccents.Single);
            MenuUiBuilder.CreateText(panel, "Title", "CPU LEVEL", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(90f, -60f), new Vector2(1200f, 90f), MenuUiBuilder.Ink, false);
            string[] labels = System.Array.ConvertAll(System.Enum.GetNames(typeof(Airerage.Core.Ai.CpuLevel)), n => n.ToUpperInvariant());
            var options = new Object[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                MenuOption option = MenuUiBuilder.CreateMenuItem(panel, $"Level{labels[i]}", labels[i], new Vector2(0f, 1f),
                    new Vector2(110f, -230f - i * 88f), new Vector2(560f, 72f));
                // 選んでいない項目は暗く下げる（D-88 の B13）。名前の前の「›››」（W17）。
                option.SetRecede(true);
                MenuUiBuilder.AddTextMotion(option, true);
                options[i] = option;
            }
            // 説明はメインメニューと同じ説明の札（白地に黒の文字。縁と裏の札は SINGLE の色。2026-11-08）。
            DescriptionPlate description = MenuUiBuilder.CreateDescriptionPlate(panel, "DescriptionPlate", new Vector2(1f, 0.6f), new Vector2(-80f, 40f),
                900f, 28, false);
            description.Text.text = "YOU + 3 CPUs. The CPUs pick random characters.";
            MenuUiBuilder.Localize(description.Text, "自分とCPU3体で対戦します。CPUのキャラクターはランダムです。");
            description.SetAccent(MenuAccents.Single);
            MenuUiBuilder.CreateHint(panel, "↑↓: select     Enter / A: OK     Esc / B: back", "↑↓:選択　　Enter ／ A: 決定　　Esc ／ B: 戻る");
            var screen = panel.gameObject.AddComponent<CpuLevelScreen>();
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_options", options);
            panel.gameObject.SetActive(false);
            return screen;
        }

        // ステージ選択（docs/15 T6-4）: 左にステージの札を 2 列に並べ（ステージが増えたので。2026-10-26）、右にカーソルのあるステージの
        // 名前と説明（ステージの色の帯）。札の左端に、RANDOM で選ぶかのチェック（オプションの RANDOM STAGES のときだけ出す）。
        private static StageSelectScreen CreateStageSelect(Transform canvas)
        {
            StageCatalog catalog = LoadOrCreateStages();
            RectTransform panel = MenuUiBuilder.CreateFull(canvas, "StageSelect");
            // 左はメインメニューと同じ白い背景（帯はカーソルのステージの色）、見出しは黒（2026-11-08）。
            MainMenuPanel side = MenuUiBuilder.CreateWhitePanel(panel, "Side", Vector2.zero, new Vector2(0.49f, 1f), 0.5f, 0.43f, 0f, MenuAccents.Random);
            MenuUiBuilder.CreateText(panel, "Title", "STAGE SELECT", 64, FontStyle.BoldAndItalic, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(90f, -60f), new Vector2(1200f, 90f), MenuUiBuilder.Ink, false);
            // 札はスクロールの中身に入れる（並びは実行中に StageSelectScreen.Layout が決める。カーソルの札が見える所まで動く。2026-11-06）。
            // 札の左の縁を見出しの 1 文字目の線にそろえるため、スクロールの左端は 44（チェックの箱が切れないように。2026-11-08）。
            var scrollPosition = new Vector2(44f, -176f);
            var scrollSize = new Vector2(900f, 690f);
            RectTransform content = OnlineMenuUiBuilder.CreateScrollArea(panel, scrollPosition, scrollSize, out UnityEngine.UI.ScrollRect scroll);
            scroll.name = "StageScroll";
            scroll.gameObject.AddComponent<ScrollToSelection>();
            // 画面の切り替えで、札を 1 枚ずつ動かす（D-87）。
            scroll.gameObject.AddComponent<TransitionGroup>();
            content.gameObject.AddComponent<TransitionGroup>();
            // RANDOM STAGES の、すべてにチェック ／ すべて外す（いちばん上）と DONE（スクロールの下）。
            MenuOption checkAll = CreateStagePlate(content, "CheckAll", "CHECK ALL");
            MenuOption uncheckAll = CreateStagePlate(content, "UncheckAll", "UNCHECK ALL");
            var options = new Object[catalog.Count];
            var stages = new Object[catalog.Count];
            var checkBoxes = new Object[catalog.Count];
            var checkMarks = new Object[catalog.Count];
            for (int i = 0; i < catalog.Count; i++)
            {
                StageDefinition stage = catalog.Get(i);
                stages[i] = stage;
                MenuOption option = CreateStagePlate(content, $"Stage{i + 1}", stage.DisplayName);
                // 札の縁と裏の札は、ステージのイメージカラー（2026-11-08）。
                option.SetAccent(new Color(stage.Color.r, stage.Color.g, stage.Color.b, 1f), true);
                options[i] = option;
                CreateStageCheck(option.transform, out GameObject box, out GameObject mark);
                // 選んで札が右へ動いても、チェックの箱は動かさない（押しやすいように）。
                option.SetPinned((RectTransform)box.transform);
                checkBoxes[i] = box;
                checkMarks[i] = mark;
            }
            // 最後に RANDOM（ユーザーの要望、2026-10-19）。
            MenuOption random = CreateStagePlate(content, "Random", "RANDOM");
            random.SetAccent(MenuAccents.Random, true);
            MenuOption done = CreateStagePlate(panel, "Done", "DONE");
            // 札の左端（見出しの 1 文字目の線。StageSelectScreen の札の並びと同じ）にそろえる（2026-11-08）。
            ((RectTransform)done.transform).anchoredPosition = new Vector2(scrollPosition.x + StageSelectScreen.PlateLeft, scrollPosition.y - scrollSize.y - 16f);
            // 名前は右の見出しの札（白地に黒の文字、縁と裏の札はステージのイメージカラー。帯はやめた。2026-11-08）、説明はその下に、右の
            // 端から置く（左の札とスクロールバーに重ならないように。説明は折り返す）。
            HeadingPlate heading = MenuUiBuilder.CreateHeadingPlate(panel, "Heading", new Vector2(1f, 0.65f), new Vector2(1f, 0.5f),
                new Vector2(-80f, 0f), 112f, 64, 840f, 44f);
            // 説明は専用の白い札の上に黒の文字（縁と裏の札はステージのイメージカラー。高さは文に合わせる。幅は前の説明の枠と同じ 820 で、
            // 左のスクロールバーに重ならない。ユーザーの要望、2026-11-08）。
            DescriptionPlate descriptionPlate = MenuUiBuilder.CreateDescriptionPlate(panel, "DescriptionPlate", new Vector2(1f, 0.5f),
                new Vector2(-80f, 68f), 820f, 28);
            UnityEngine.UI.Text description = descriptionPlate.Text;
            // 案内は、見出しとスクロールの上の端の間（アイテムを選ぶ画面と同じ。2026-11-06）。
            UnityEngine.UI.Text note = AccentTint.MarkOnWhite(MenuUiBuilder.CreateText(panel, "Note", string.Empty, 24, FontStyle.BoldAndItalic,
                TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(110f, -131f), new Vector2(1000f, 38f), MenuUiBuilder.Accent));
            note.font = GameFonts.JapaneseBold;
            MenuUiBuilder.CreateHint(panel, "↑↓←→: select     Enter / A: OK     Esc / B: back", "↑↓←→:選択　　Enter ／ A: 決定　　Esc ／ B: 戻る");

            var screen = panel.gameObject.AddComponent<StageSelectScreen>();
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_options", options);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_stages", stages);
            SandboxWireSceneBuilder.SetReference(screen, "_heading", heading);
            SandboxWireSceneBuilder.SetReference(screen, "_description", description);
            SandboxWireSceneBuilder.SetReference(screen, "_descriptionPlate", descriptionPlate);
            SandboxWireSceneBuilder.SetReference(screen, "_panel", side);
            SandboxWireSceneBuilder.SetReference(screen, "_note", note);
            SandboxWireSceneBuilder.SetReference(screen, "_random", random);
            SandboxWireSceneBuilder.SetReference(screen, "_content", content);
            SandboxWireSceneBuilder.SetReference(screen, "_checkAll", checkAll);
            SandboxWireSceneBuilder.SetReference(screen, "_uncheckAll", uncheckAll);
            SandboxWireSceneBuilder.SetReference(screen, "_done", done);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_checkBoxes", checkBoxes);
            SandboxWireSceneBuilder.SetReferenceArray(screen, "_checkMarks", checkMarks);
            panel.gameObject.SetActive(false);
            return screen;
        }

        // ステージの札（2 列。位置は実行中に StageSelectScreen.Layout が決める）。上下左右の移動は、並びから決める（Automatic）。
        private static MenuOption CreateStagePlate(RectTransform parent, string name, string label)
        {
            MenuOption option = MenuUiBuilder.CreateMenuItem(parent, name, label, new Vector2(0f, 1f), Vector2.zero, new Vector2(360f, 64f));
            option.LabelText.fontSize = 28;
            UnityEngine.UI.Button button = option.Button;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Automatic };
            return option;
        }

        // 札の左端のチェック（白い枠の箱と、付いているときの印。印は画面のイメージカラー）。
        internal static void CreateStageCheck(Transform plate, out GameObject box, out GameObject mark)
        {
            box = new GameObject("Check", typeof(RectTransform));
            box.transform.SetParent(plate, false);
            var rect = (RectTransform)box.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-8f, 0f);
            rect.sizeDelta = new Vector2(34f, 34f);
            box.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            UnityEngine.UI.Image inner = MenuUiBuilder.CreateFill(rect, "Inner", new Color(0.08f, 0.09f, 0.12f, 1f), Vector2.zero, Vector2.one);
            inner.rectTransform.offsetMin = new Vector2(3f, 3f);
            inner.rectTransform.offsetMax = new Vector2(-3f, -3f);
            UnityEngine.UI.Image check = AccentTint.Mark(MenuUiBuilder.CreateFill(rect, "Mark", MenuUiBuilder.Accent, Vector2.zero, Vector2.one));
            check.rectTransform.offsetMin = new Vector2(8f, 8f);
            check.rectTransform.offsetMax = new Vector2(-8f, -8f);
            mark = check.gameObject;
            box.SetActive(false);
        }

        // 試合へ入るときの黒い帯（D-87 の T10。いつも読み込む App のシーンに置き、ほかの画面より手前に出す）。
        private static ScreenCurtain CreateCurtain()
        {
            Canvas canvas = MenuUiBuilder.CreateCanvas("Curtain", 900);
            Object.DestroyImmediate(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>());
            RectTransform root = MenuUiBuilder.CreateFull(canvas.transform, "Bars");
            var bars = root.gameObject.AddComponent<CurtainGraphic>();
            UnityEngine.UI.Text label = MenuUiBuilder.CreateText(root, "Label", string.Empty, 48, FontStyle.Italic, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1600f, 90f), Color.white, false);
            label.font = GameFonts.LatinBlack;
            var curtain = canvas.gameObject.AddComponent<ScreenCurtain>();
            curtain.Configure(root.gameObject, bars, label);
            root.gameObject.SetActive(false);
            return curtain;
        }

        private static void BuildApp()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            StageCatalog catalog = LoadOrCreateStages();
            var flow = new GameObject("GameFlow").AddComponent<GameFlow>();
            SandboxWireSceneBuilder.SetReference(flow, "_stages", catalog);
            SandboxWireSceneBuilder.SetReference(flow, "_curtain", CreateCurtain());
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            // 画面の切り替えと試合へ入る黒い帯の間、選択と決定を止める仕組み（D-87）。
            events.AddComponent<NavigationGate>();
            var module = events.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            // 背景を押しても選んでいる項目を外さない（外れると Esc ／ B の「戻る」が効かない。2026-11-06）。外れたときは選び直す。
            module.deselectOnBackgroundClick = false;
            events.AddComponent<SelectionKeeper>();
            // メニューの効果音（決定した直後に画面のシーンを外しても途切れないよう、いつも読み込むシーンに置く）。
            // タイトルとキャラクターの決定は音声ファイル（docs/18 4.3）。
            var sounds = new GameObject("UiSounds").AddComponent<UiSounds>();
            SandboxWireSceneBuilder.SetReference(sounds, "_titleStartOverride", MusicAssets.LoadSe(MusicAssets.TitleStartSePath));
            SandboxWireSceneBuilder.SetReference(sounds, "_characterDecideOverride", MusicAssets.LoadSe(MusicAssets.CharacterDecideSePath));
            // カーソル（ダイヤルのカチッ）とボタンの決定も音声ファイル（2026-11-07。docs/18 4.18）。
            SandboxWireSceneBuilder.SetReference(sounds, "_moveOverride", MusicAssets.LoadSe(MusicAssets.UiCursorSePath));
            SandboxWireSceneBuilder.SetReference(sounds, "_confirmOverride", MusicAssets.LoadSe(MusicAssets.UiConfirmSePath));
            // 画面を進む風と戻る風（2026-11-07。docs/18 4.19）。
            SandboxWireSceneBuilder.SetReference(sounds, "_whooshOverride", MusicAssets.LoadSe(MusicAssets.UiWhooshSePath));
            SandboxWireSceneBuilder.SetReference(sounds, "_whooshBackOverride", MusicAssets.LoadSe(MusicAssets.UiWhooshBackSePath));
            // メニューの BGM（タイトルの曲と、キャラクター・ステージの選択の曲。ループの位置は曲のファイルのタグから。docs/18 6 章）。
            // 2 曲のラウドネスはそろっている（-16 LUFS ほど）ので、曲ごとの音量はどちらも 1。
            var music = new GameObject("MusicPlayer").AddComponent<MusicPlayer>();
            SandboxWireSceneBuilder.SetReference(music, "_menuMusic",
                MusicAssets.LoadOrCreateCue(MusicAssets.TitleMusicPath, MusicAssets.TitleCuePath, 1f));
            SandboxWireSceneBuilder.SetReference(music, "_selectMusic",
                MusicAssets.LoadOrCreateCue(MusicAssets.SelectMusicPath, MusicAssets.SelectCuePath, 1f));
            // ネットワーク対戦の接続（メニューと試合のシーンをまたいで保つ。T6-7）。
            var connection = new GameObject("OnlineConnection").AddComponent<Airerage.Net.OnlineConnection>();
            SandboxWireSceneBuilder.SetReference(connection, "_roster",
                SandboxWireSceneBuilder.LoadOrCreateCharacterRoster(SandboxWireSceneBuilder.LoadOrCreateCharacterConfig()));
            Save(scene, AppScenePath);
        }

        /// <summary>ステージの設定と一覧（アスレチック、追加のステージの順）。値はビルドのたびにここの値で作り直す。</summary>
        internal static StageCatalog LoadOrCreateStages()
        {
            SandboxWireSceneBuilder.EnsureFolder(StageFolder);
            var stages = new List<StageDefinition>
            {
                LoadOrCreateStage(AthleticStageAssetPath, "ATHLETIC", Path.GetFileNameWithoutExtension(AthleticStagePath),
                    "Jump pads, updrafts and boost rings.", "ジャンプ台、上昇気流、加速リングで空を飛び回るアスレチック。", new Color(0.35f, 0.85f, 1f)),
            };
            foreach (StageLayout layout in StageSceneBuilder.Layouts)
            {
                StageDefinition stage = LoadOrCreateStage($"{StageFolder}/{layout.Id}.asset", layout.DisplayName, layout.SceneName,
                    layout.Description, layout.DescriptionJa, layout.Color);
                // ステージの曲（ループの位置は曲のファイルのタグから。docs/18 6 章）。曲ごとの音量は 1（ファイルの大きさのまま）。
                stage.SetMusic(string.IsNullOrEmpty(layout.Music)
                    ? null
                    : MusicAssets.LoadOrCreateCue(MusicAssets.StageMusicPath(layout.Music), MusicAssets.StageCuePath(layout.Music), 1f));
                stages.Add(stage);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(StageCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<StageCatalog>();
                AssetDatabase.CreateAsset(catalog, StageCatalogPath);
            }
            catalog.Configure(stages.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static StageDefinition LoadOrCreateStage(string path, string displayName, string sceneName, string description, string descriptionJa,
            Color color)
        {
            var stage = AssetDatabase.LoadAssetAtPath<StageDefinition>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageDefinition>();
                AssetDatabase.CreateAsset(stage, path);
            }
            stage.Configure(displayName, sceneName, description, descriptionJa, color);
            EditorUtility.SetDirty(stage);
            return stage;
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
