using System;
using System.IO;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Data;
using Degunatsu.Runtime.DebugTools;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Degunatsu.Editor
{
    /// <summary>
    /// ワイヤー移動の検証用シーン（Sandbox_Wire）を作る。地形の配置はこのコードで決め、シーンは生成物として扱う。
    /// 調整値のアセットとマテリアルは、すでにあれば作り直さない（調整した値を消さないため）。
    ///
    /// バッチモードでの実行:
    ///   Unity -batchmode -nographics -quit -projectPath . -executeMethod Degunatsu.Editor.SandboxWireSceneBuilder.Build
    /// </summary>
    public static class SandboxWireSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Sandbox_Wire.unity";
        public const string CharacterConfigPath = "Assets/_Project/Data/Characters/DefaultCharacter.asset";
        public const string InputActionsPath = "Assets/_Project/Input/DegunatsuControls.inputactions";
        private const string MaterialFolder = "Assets/_Project/Materials";

        // 地形の奥行き（見た目のため）。キャラクターの奥行きより大きければよい。
        private const float TerrainDepth = 4f;

        internal readonly struct TerrainBox
        {
            public readonly string Name;
            public readonly Vector2 Min;
            public readonly Vector2 Max;
            public readonly bool Attachable;

            public TerrainBox(string name, float minX, float minY, float maxX, float maxY, bool attachable = false)
            {
                Name = name;
                Min = new Vector2(minX, minY);
                Max = new Vector2(maxX, maxY);
                Attachable = attachable;
            }
        }

        // 検証用アリーナ（docs/06_prototype_plan.md T1-8）。横 120m（x = -60〜60）、高さ 30m。
        // 中央のスタートから、左（足場渡り）・右（細い壁越え）・上（空中）の3コースに分かれる。左端は天井の低い通路。
        // 配置は現在の移動とワイヤーの仮の値（ジャンプ 2.4m＋空中ジャンプ、ワイヤーの最大長 12m）で届くように決めた。
        private static readonly TerrainBox[] Terrain =
        {
            // 外枠（天井は接続できる）
            new TerrainBox("Ground", -60f, -1f, 60f, 0f),
            new TerrainBox("Ceiling", -60f, 30f, 60f, 31f, attachable: true),
            new TerrainBox("WallLeft", -61f, -1f, -60f, 31f),
            new TerrainBox("WallRight", 60f, -1f, 61f, 31f),

            // 左「Platforms」: 幅 1〜2m の足場の連なり（接続できる）。下を歩けるよう、いちばん低い足場の下面は 2.2m。
            new TerrainBox("Platform1", -7.5f, 2.2f, -5.5f, 2.6f, attachable: true),
            new TerrainBox("Platform2", -12f, 3.6f, -10.5f, 4f, attachable: true),
            new TerrainBox("Platform3", -16.5f, 5f, -15.5f, 5.4f, attachable: true),
            new TerrainBox("Platform4", -21f, 6.2f, -19.5f, 6.6f, attachable: true),
            new TerrainBox("Platform5", -26f, 7.6f, -25f, 8f, attachable: true),
            new TerrainBox("Platform6", -31f, 9f, -29.5f, 9.4f, attachable: true),
            new TerrainBox("PlatformGoal", -36f, 10.4f, -33f, 10.8f, attachable: true),

            // 右「Walls」: 厚さ 0.2〜0.5m・高さ 8m の細い壁（接続できない）と、上の梁（接続できる）。
            new TerrainBox("WallsBeam", 6f, 12f, 32f, 13f, attachable: true),
            new TerrainBox("ThinWall1", 10f, 0f, 10.2f, 8f),
            new TerrainBox("ThinWall2", 16f, 0f, 16.3f, 8f),
            new TerrainBox("ThinWall3", 22f, 0f, 22.5f, 8f),
            new TerrainBox("ThinWall4", 28f, 0f, 28.2f, 8f),

            // 上「Air」: 空中の障害物（接続できる）と、高さ 24m のゴールの足場。
            new TerrainBox("AirBlock1", 3f, 8f, 5f, 8.5f, attachable: true),
            new TerrainBox("AirBlock2", -6f, 13f, -4f, 13.5f, attachable: true),
            new TerrainBox("AirBlock3", 2f, 18f, 4f, 18.5f, attachable: true),
            new TerrainBox("AirGoal", -2f, 24f, 2f, 24.5f, attachable: true),

            // 左端: 天井の低い通路（引っかかりの確認用。計測はしない）。すき間は 0.2m と 0.1m（キャラクターの高さは 1.8m）。
            new TerrainBox("CorridorCeilingHigh", -58f, 2f, -50f, 3f),
            new TerrainBox("CorridorCeilingLow", -50f, 1.9f, -44f, 3f),
        };

        private static readonly Vector2[] AnchorPoints =
        {
            new Vector2(-2f, 10f),
            new Vector2(4.5f, 14f),
            new Vector2(-3f, 20f),
            new Vector2(7f, 9f),
        };

        internal static readonly Vector3 SpawnPosition = new Vector3(0f, 1f, 0f);

        // 攻撃を当てて試すダミーの出現位置（docs/11_combat_plan.md T3-2）。出現位置の右、計測のスタートの範囲の外。
        private static readonly Vector3 DummySpawnPosition = new Vector3(4f, 1f, 0f);

        private readonly struct Zone
        {
            public readonly CourseZone.ZoneKind Kind;
            public readonly string Course;
            public readonly Vector2 Min;
            public readonly Vector2 Max;

            public Zone(CourseZone.ZoneKind kind, string course, float minX, float minY, float maxX, float maxY)
            {
                Kind = kind;
                Course = course;
                Min = new Vector2(minX, minY);
                Max = new Vector2(maxX, maxY);
            }
        }

        // タイム計測コースのスタートとゴール（キャラクターの箱が範囲に重なったら入ったとみなす）。
        private static readonly Zone[] Zones =
        {
            new Zone(CourseZone.ZoneKind.Start, "", -2f, 0f, 2f, 2.5f),
            new Zone(CourseZone.ZoneKind.Goal, "Platforms", -36f, 10.8f, -33f, 13.3f),
            new Zone(CourseZone.ZoneKind.Goal, "Walls", 33f, 0f, 37f, 3f),
            new Zone(CourseZone.ZoneKind.Goal, "Air", -2f, 24.5f, 2f, 27f),
        };

        // 範囲の枠の太さと、奥行きの位置（キャラクターより奥に置く）。
        private const float ZoneFrameWidth = 0.08f;
        private const float ZoneFrameZ = 0.6f;

        // 追従カメラの初期値【仮】（D-13。プレイして調整する）。シーンの CinemachineCamera で編集できる。
        // 視野角 40°・距離 22m で、縦 約16m × 横 約28m（16:9）が映る（ワイヤーの最大長 12m が収まる）。
        // デッドゾーンは使わない（止まったときのキャラクターの画面上の位置が、毎回同じ中央になるように）。
        private const float CameraFieldOfView = 40f;
        private const float CameraDistance = 22f;
        private const float CameraDamping = 0.3f;    // 秒

        [MenuItem("Degunatsu/Sandbox_Wire シーンを作り直す")]
        private static void BuildFromMenu()
        {
            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog("Sandbox_Wire", $"{ScenePath} を作り直します。シーンを手で編集した内容は消えます。", "作り直す", "やめる"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            Build();
        }

        /// <summary>シーンを作り直して保存する。</summary>
        public static void Build()
        {
            // 先に新しいシーンを作る（シーンを作り直すと、それまでに読み込んだアセットが解放され、参照が切れるため）。
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var character = LoadOrCreateCharacterConfig();
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
            {
                throw new InvalidOperationException($"入力アクションのアセットがない: {InputActionsPath}");
            }

            Material terrainMaterial = LoadOrCreateMaterial("Terrain", new Color(0.55f, 0.55f, 0.58f));
            Material attachableMaterial = LoadOrCreateMaterial("TerrainAttachable", new Color(0.25f, 0.6f, 0.75f));
            Material characterMaterial = LoadOrCreateMaterial("Character", new Color(0.95f, 0.55f, 0.2f));
            Material anchorMaterial = LoadOrCreateMaterial("Anchor", new Color(0.95f, 0.85f, 0.2f));
            // ワイヤーの色は仮（背景の明るい空と区別できるように、線は紫、接続点の印は橙）。
            Material wireMaterial = LoadOrCreateMaterial("Wire", new Color(0.6f, 0.2f, 0.85f), unlit: true);
            Material attachedAnchorMaterial = LoadOrCreateMaterial("WireAnchor", new Color(1f, 0.5f, 0f), unlit: true);
            Material startZoneMaterial = LoadOrCreateMaterial("ZoneStart", new Color(0.2f, 0.8f, 0.3f), unlit: true);
            Material goalZoneMaterial = LoadOrCreateMaterial("ZoneGoal", new Color(0.9f, 0.25f, 0.25f), unlit: true);

            CreateArena(terrainMaterial, attachableMaterial, anchorMaterial);

            var zoneRoot = new GameObject("CourseZones").transform;
            foreach (Zone zone in Zones)
            {
                bool isStart = zone.Kind == CourseZone.ZoneKind.Start;
                CreateZone(zoneRoot, zone, isStart ? startZoneMaterial : goalZoneMaterial);
            }

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = SpawnPosition;

            CharacterView characterView = CreateCharacterView("Character", characterMaterial, SpawnPosition);

            var dummySpawn = new GameObject("DummySpawn").transform;
            dummySpawn.position = DummySpawnPosition;
            CharacterView dummyView = CreateCharacterView("Dummy", LoadDummyMaterial(), DummySpawnPosition);

            WireView wireView = CreateWireView("Wire", null, wireMaterial, attachedAnchorMaterial);

            CreateFollowCamera(characterView.transform, inputActions);
            AimIndicatorView aimIndicator = CreateAimIndicator();

            var runner = new GameObject("Simulation").AddComponent<SimulationRunner>();
            SetReference(runner, "_character", character);
            SetReference(runner, "_inputActions", inputActions);
            SetReference(runner, "_spawnPoint", spawn);
            SetReference(runner, "_characterView", characterView);
            SetReference(runner, "_wireView", wireView);
            SetReference(runner, "_aimIndicator", aimIndicator);
            SetReference(runner, "_camera", Object.FindFirstObjectByType<Camera>());
            SetReferenceArray(runner, "_dummySpawns", new Object[] { dummySpawn });
            SetReferenceArray(runner, "_dummyViews", new Object[] { dummyView });

            CreateMissIndicator(runner, Object.FindFirstObjectByType<Camera>());

            var courseTimer = new GameObject("CourseTimer").AddComponent<CourseTimerHud>();
            SetReference(courseTimer, "_runner", runner);

            CreateDebugTools(runner, inputActions, CreateAudio(runner));
            CreateComboHud(runner);

            EnsureFolder(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {ScenePath}");
            }
            AddToBuildSettings(ScenePath);
            Debug.Log($"SandboxWireSceneBuilder: {ScenePath} を作った");
        }

        /// <summary>検証用アリーナの地形（BoxCollider）とアンカーポイントを作る。オンライン用のシーンでも使う。</summary>
        internal static void CreateArena(Material terrainMaterial, Material attachableMaterial, Material anchorMaterial)
        {
            CreateTerrain(Terrain, AnchorPoints, terrainMaterial, attachableMaterial, anchorMaterial);
        }

        /// <summary>地形（BoxCollider）とアンカーポイントを作る。</summary>
        internal static void CreateTerrain(
            TerrainBox[] boxes, Vector2[] anchorPoints, Material terrainMaterial, Material attachableMaterial, Material anchorMaterial)
        {
            var terrainRoot = new GameObject("Terrain").transform;
            foreach (TerrainBox box in boxes)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = box.Name;
                go.transform.SetParent(terrainRoot, false);
                Vector2 center = (box.Min + box.Max) * 0.5f;
                Vector2 size = box.Max - box.Min;
                go.transform.localPosition = new Vector3(center.x, center.y, 0f);
                go.transform.localScale = new Vector3(size.x, size.y, TerrainDepth);
                go.GetComponent<MeshRenderer>().sharedMaterial = box.Attachable ? attachableMaterial : terrainMaterial;
                if (box.Attachable)
                {
                    go.AddComponent<WireAttachable>();
                }
            }

            var anchorRoot = new GameObject("AnchorPoints").transform;
            foreach (Vector2 point in anchorPoints)
            {
                GameObject go = CreateVisual(PrimitiveType.Sphere, "AnchorPoint", anchorMaterial, Vector3.one * 0.5f);
                go.transform.SetParent(anchorRoot, false);
                go.transform.localPosition = new Vector3(point.x, point.y, 0f);
                go.AddComponent<WireAnchorPoint>();
            }
        }

        /// <summary>キャラクターの箱の表示と、攻撃の判定を描く線、撃破技のチャージのエフェクトを作る。</summary>
        internal static CharacterView CreateCharacterView(string name, Material material, Vector3 position)
        {
            GameObject go = CreateVisual(PrimitiveType.Cube, name, material, Vector3.one);
            go.transform.position = position;
            var view = go.AddComponent<CharacterView>();
            SetReference(view, "_hitboxLine", CreateHitboxLine(name + "Hitbox"));
            SetReference(view, "_chargeEffect", CreateChargeEffect(name + "Charge"));
            AddCombatIndicators(view, name, null, material.GetColor("_BaseColor"));
            return view;
        }

        // ガードのクールタイムのゲージの色（ワイヤーキャンセルの紫と区別する）。
        private static readonly Color GuardGaugeColor = new Color(0.3f, 0.8f, 1f, 0.55f);

        /// <summary>
        /// キャラクターの表示に、戦闘の表示を付ける: ワイヤーキャンセルとガードのクールタイムのゲージ、ガードとジャストガードの表示
        /// （playerColor でガードの光を出す）。オンライン用のシーンでも使う。
        /// </summary>
        internal static void AddCombatIndicators(CharacterView view, string name, Transform parent, Color playerColor)
        {
            SetReference(view, "_wireCancelGauge", CreateRingGauge(name + "WireCancelGauge", parent, "WIRE CANCEL", 1.3f));
            SetReference(view, "_guardGauge", CreateRingGauge(name + "GuardGauge", parent, "GUARD", 1.6f, GuardGaugeColor));
            SetReference(view, "_guardEffect", CreateGuardEffect(name + "GuardEffect", parent, playerColor));

            // 命中の火花（白っぽい黄色。短い）。
            BurstEffectView spark = CreateBurst(name + "HitSpark", parent);
            spark.Configure(new Color(1f, 0.95f, 0.6f, 1f), false, 0.2f, 0.2f, 1.1f, 6, 0.9f, 0.1f, string.Empty, 0, 0f);
            SetReference(view, "_hitSpark", spark);
            // FATAL（虹色の大きな光、「FATAL!」、画面の閃光。ユーザーの要望、2026-10-13）。
            BurstEffectView fatal = CreateBurst(name + "FatalBurst", parent);
            fatal.Configure(Color.white, true, 1.1f, 0.4f, 5f, 14, 3.2f, 0.28f, "FATAL!", 64, 0.45f);
            SetReference(view, "_fatalBurst", fatal);
            // ワイヤーキャンセル（紫。クールタイムのゲージと同じ色）。
            BurstEffectView wireCancel = CreateBurst(name + "WireCancelBurst", parent);
            wireCancel.Configure(new Color(0.75f, 0.45f, 1f, 1f), false, 0.35f, 0.4f, 1.9f, 10, 1.2f, 0.12f, string.Empty, 0, 0f);
            SetReference(view, "_wireCancelBurst", wireCancel);
        }

        /// <summary>一瞬の光（輪 2 つと光の筋。文字はシーンのカメラで画面に写す）。値は呼んだ側が Configure で決める。</summary>
        internal static BurstEffectView CreateBurst(string name, Transform parent)
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<BurstEffectView>();
            SetReference(view, "_ring", CreateIndicatorLine(root.transform, "Ring", material, 0.12f));
            SetReference(view, "_innerRing", CreateIndicatorLine(root.transform, "InnerRing", material, 0.1f));
            SetReference(view, "_rays", CreateIndicatorLine(root.transform, "Rays", material, 0.1f));
            SetReference(view, "_camera", Object.FindFirstObjectByType<Camera>());
            return view;
        }

        /// <summary>操作するキャラクターのコンボの表示（T3-7）。</summary>
        internal static void CreateComboHud(SimulationRunner runner)
        {
            var hud = new GameObject("ComboHud").AddComponent<ComboHud>();
            SetReference(hud, "_runner", runner);
        }

        /// <summary>ガードとジャストガードの表示（光る輪、広がる輪、光の筋。文字はシーンのカメラで画面に写す）。</summary>
        internal static GuardEffectView CreateGuardEffect(string name, Transform parent, Color playerColor)
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<GuardEffectView>();
            view.Configure(playerColor);
            SetReference(view, "_glow", CreateIndicatorLine(root.transform, "Glow", material, 0.14f));
            SetReference(view, "_burst", CreateIndicatorLine(root.transform, "Burst", material, 0.3f));
            SetReference(view, "_rays", CreateIndicatorLine(root.transform, "Rays", material, 0.1f));
            SetReference(view, "_camera", Object.FindFirstObjectByType<Camera>());
            return view;
        }

        /// <summary>
        /// 円形のゲージ（薄い輪、残りの弧、先端の火花）。label が空でなければ、シーンのカメラで画面に写して上に出す。
        /// 大きさの影響を受けないように、キャラクターの子にしない。
        /// </summary>
        internal static RingGaugeView CreateRingGauge(string name, Transform parent, string label, float radius, Color? color = null)
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<RingGaugeView>();
            view.Configure(label, radius, color);
            SetReference(view, "_track", CreateIndicatorLine(root.transform, "Track", material, 0.08f));
            SetReference(view, "_fuse", CreateIndicatorLine(root.transform, "Fuse", material, 0.12f));
            SetReference(view, "_spark", CreateIndicatorLine(root.transform, "Spark", material, 0.1f));
            SetReference(view, "_camera", Object.FindFirstObjectByType<Camera>());
            return view;
        }

        /// <summary>
        /// 撃破技のチャージのエフェクト（輪2つと光の筋。キャラクターの子にしない。大きさの影響を受けないように）。
        /// 「CHARGE!」の文字はシーンのカメラで画面に写す。
        /// </summary>
        internal static ChargeEffectView CreateChargeEffect(string name)
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject(name);
            var view = root.AddComponent<ChargeEffectView>();
            SetReference(view, "_ring", CreateIndicatorLine(root.transform, "Ring", material, 0.1f));
            SetReference(view, "_pulse", CreateIndicatorLine(root.transform, "Pulse", material, 0.08f));
            SetReference(view, "_rays", CreateIndicatorLine(root.transform, "Rays", material, 0.06f));
            SetReference(view, "_camera", Object.FindFirstObjectByType<Camera>());
            return view;
        }

        /// <summary>ダミー（攻撃を当てて試す相手）の色。</summary>
        internal static Material LoadDummyMaterial()
        {
            return LoadOrCreateMaterial("Dummy", new Color(0.55f, 0.5f, 0.6f));
        }

        /// <summary>ワイヤーの線と、接続点の印、接続を保てる残り時間のゲージ（接続点の周り）。</summary>
        internal static WireView CreateWireView(string name, Transform parent, Material wireMaterial, Material attachedAnchorMaterial)
        {
            var wireGo = new GameObject(name);
            wireGo.transform.SetParent(parent, false);
            var line = wireGo.AddComponent<LineRenderer>();
            line.sharedMaterial = wireMaterial;
            line.widthMultiplier = 0.06f;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            GameObject anchorMarker = CreateVisual(PrimitiveType.Sphere, "AttachedAnchor", attachedAnchorMaterial, Vector3.one * 0.3f);
            anchorMarker.transform.SetParent(wireGo.transform, false);
            var wireView = wireGo.AddComponent<WireView>();
            SetReference(wireView, "_anchorMarker", anchorMarker.transform);
            SetReference(wireView, "_attachGauge", CreateRingGauge(name + "AttachGauge", wireGo.transform, string.Empty, 0.55f));
            return wireView;
        }

        /// <summary>ビルド設定のシーン一覧に加える（PlayMode テストで名前から読み込むため）。</summary>
        internal static void AddToBuildSettings(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            foreach (EditorBuildSettingsScene existing in scenes)
            {
                if (existing.path == path)
                {
                    return;
                }
            }
            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(path, true);
            EditorBuildSettings.scenes = updated;
        }

        /// <summary>計測コースの範囲（トリガーの BoxCollider）と、その枠の表示を作る。</summary>
        private static void CreateZone(Transform parent, Zone zone, Material frameMaterial)
        {
            string name = zone.Kind == CourseZone.ZoneKind.Start ? "Start" : $"Goal_{zone.Course}";
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Vector2 center = (zone.Min + zone.Max) * 0.5f;
            Vector2 size = zone.Max - zone.Min;
            go.transform.localPosition = new Vector3(center.x, center.y, 0f);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 1f);
            go.AddComponent<CourseZone>().Configure(zone.Kind, zone.Course);

            // 枠（上下左右の4本の棒）
            float w = ZoneFrameWidth;
            CreateFrameBar(go.transform, frameMaterial, new Vector3(0f, size.y * 0.5f, ZoneFrameZ), new Vector3(size.x, w, w));
            CreateFrameBar(go.transform, frameMaterial, new Vector3(0f, -size.y * 0.5f, ZoneFrameZ), new Vector3(size.x, w, w));
            CreateFrameBar(go.transform, frameMaterial, new Vector3(-size.x * 0.5f, 0f, ZoneFrameZ), new Vector3(w, size.y, w));
            CreateFrameBar(go.transform, frameMaterial, new Vector3(size.x * 0.5f, 0f, ZoneFrameZ), new Vector3(w, size.y, w));
        }

        private static void CreateFrameBar(Transform parent, Material material, Vector3 localPosition, Vector3 scale)
        {
            GameObject bar = CreateVisual(PrimitiveType.Cube, "Frame", material, scale);
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = localPosition;
            bar.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// 仮の BGM と効果音（音はその場で合成する。ユーザーの要望、2026-10-13）。状態の変化から効果音を鳴らす部品も付ける。
        /// </summary>
        internal static GameAudio CreateAudio(SimulationRunner runner)
        {
            var go = new GameObject("Audio");
            var audio = go.AddComponent<GameAudio>();
            var events = go.AddComponent<CombatSoundEvents>();
            SetReference(events, "_runner", runner);
            SetReference(events, "_audio", audio);
            return audio;
        }

        /// <summary>デバッグ機能（操作、表示、入力の記録と再生、軌跡）。docs/06_prototype_plan.md T1-9。</summary>
        internal static void CreateDebugTools(SimulationRunner runner, InputActionAsset inputActions, GameAudio audio)
        {
            var root = new GameObject("DebugTools");
            var controls = root.AddComponent<DebugControls>();
            SetReference(controls, "_runner", runner);
            SetReference(controls, "_inputActions", inputActions);
            SetReference(controls, "_audio", audio);
            var hud = root.AddComponent<DebugHud>();
            SetReference(hud, "_runner", runner);
            var replay = root.AddComponent<ReplayControls>();
            SetReference(replay, "_runner", runner);
            SetReference(replay, "_inputActions", inputActions);

            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            LineRenderer trailLine = CreateIndicatorLine(root.transform, "Trail", material, 0.05f);
            var trail = trailLine.gameObject.AddComponent<TrailView>();
            SetReference(trail, "_runner", runner);
        }

        /// <summary>照準のインジケーター（照準の線、終点の印、射程の円）。半透明にするため Sprites-Default を使う。</summary>
        internal static AimIndicatorView CreateAimIndicator()
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            if (material == null)
            {
                throw new InvalidOperationException("組み込みのマテリアル Sprites-Default が見つからない");
            }

            var root = new GameObject("AimIndicator");
            LineRenderer aimLine = CreateIndicatorLine(root.transform, "AimLine", material, 0.07f);
            LineRenderer marker = CreateIndicatorLine(root.transform, "TargetMarker", material, 0.06f);
            LineRenderer ring = CreateIndicatorLine(root.transform, "RangeRing", material, 0.08f);

            var view = root.AddComponent<AimIndicatorView>();
            SetReference(view, "_aimLine", aimLine);
            SetReference(view, "_targetMarker", marker);
            SetReference(view, "_rangeRing", ring);
            return view;
        }

        /// <summary>ワイヤーが外れたときの表示（×印と MISS）。半透明にするため Sprites-Default を使う。</summary>
        internal static void CreateMissIndicator(SimulationRunner runner, Camera camera)
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject("MissIndicator");
            LineRenderer cross = CreateIndicatorLine(root.transform, "Cross", material, 0.1f);
            var view = cross.gameObject.AddComponent<MissIndicatorView>();
            SetReference(view, "_runner", runner);
            SetReference(view, "_camera", camera);
        }

        /// <summary>攻撃の判定を描く線（キャラクターの子にしない。大きさの影響を受けないように）。</summary>
        internal static LineRenderer CreateHitboxLine(string name)
        {
            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            LineRenderer line = CreateIndicatorLine(null, name, material, 0.05f);
            line.startColor = line.endColor = new Color(1f, 0.2f, 0.2f, 0.9f);
            line.loop = true;
            line.enabled = false;
            return line;
        }

        internal static LineRenderer CreateIndicatorLine(Transform parent, string name, Material material, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary>
        /// キャラクターを横から追従するカメラ（Cinemachine 3）。回転はせず、位置だけを合わせる。
        /// マウスホイールと LB / RB で距離を変えられる（CameraZoom）。
        /// </summary>
        internal static CinemachineCamera CreateFollowCamera(Transform target, InputActionAsset inputActions)
        {
            // カメラの位置は Cinemachine が決める（再生時にキャラクターへ合わせる）ので、ここでは置かない。
            Camera camera = Object.FindFirstObjectByType<Camera>();
            camera.gameObject.AddComponent<CinemachineBrain>();

            var go = new GameObject("FollowCamera");
            var virtualCamera = go.AddComponent<CinemachineCamera>();
            virtualCamera.Follow = target;
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = CameraFieldOfView;
            virtualCamera.Lens = lens;

            var composer = go.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = CameraDistance;
            composer.Damping = new Vector3(CameraDamping, CameraDamping, 0f);
            composer.Composition = ScreenComposerSettings.Default; // デッドゾーンなし（画面の中央に合わせる）

            var zoom = go.AddComponent<CameraZoom>();
            SetReference(zoom, "_composer", composer);
            SetReference(zoom, "_inputActions", inputActions);
            return virtualCamera;
        }

        internal static CharacterConfigAsset LoadOrCreateCharacterConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterConfigAsset>(CharacterConfigPath);
            if (asset != null)
            {
                return asset;
            }
            EnsureFolder(Path.GetDirectoryName(CharacterConfigPath));
            asset = ScriptableObject.CreateInstance<CharacterConfigAsset>();
            AssetDatabase.CreateAsset(asset, CharacterConfigPath);
            return asset;
        }

        internal static Material LoadOrCreateMaterial(string name, Color color, bool unlit = false)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            string shaderName = unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"シェーダーが見つからない: {shaderName}");
            }
            EnsureFolder(MaterialFolder);
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>見た目だけの物を作る（コライダーを外す。地形として読み込まれないように）。</summary>
        internal static GameObject CreateVisual(PrimitiveType type, string name, Material material, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        internal static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name} に {field} がない");
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (property.objectReferenceValue == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name}.{field} に参照を設定できなかった");
            }
        }

        internal static void SetReferenceArray(Object target, string field, Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
            {
                throw new InvalidOperationException($"{target.GetType().Name} に配列 {field} がない");
            }
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
