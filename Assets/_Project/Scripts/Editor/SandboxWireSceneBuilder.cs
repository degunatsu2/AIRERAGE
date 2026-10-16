using System;
using System.IO;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.DebugTools;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Airerage.Editor
{
    /// <summary>
    /// ワイヤー移動の検証用シーン（Sandbox_Wire）を作る。地形の配置はこのコードで決め、シーンは生成物として扱う。
    /// 調整値のアセットとマテリアルは、すでにあれば作り直さない（調整した値を消さないため）。
    ///
    /// バッチモードでの実行:
    ///   Unity -batchmode -nographics -quit -projectPath . -executeMethod Airerage.Editor.SandboxWireSceneBuilder.Build
    /// </summary>
    public static class SandboxWireSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Sandbox_Wire.unity";
        public const string CharacterConfigPath = "Assets/_Project/Data/Characters/DefaultCharacter.asset";
        public const string CharacterRosterPath = "Assets/_Project/Data/Characters/CharacterRoster.asset";
        public const string RiskTakerPath = "Assets/_Project/Data/Characters/RiskTaker.asset";
        public const string StormRunnerPath = "Assets/_Project/Data/Characters/StormRunner.asset";
        public const string SpaceTravelerPath = "Assets/_Project/Data/Characters/SpaceTraveler.asset";
        public const string InputActionsPath = "Assets/_Project/Input/AirerageControls.inputactions";
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
        // 初期のカメラの距離（m）。近すぎて毎回ズームアウトしていたので 22 から遠くした（ユーザーの要望、2026-10-15）。
        private const float CameraDistance = 30f;
        private const float CameraDamping = 0.3f;    // 秒

        [MenuItem("Airerage/Sandbox_Wire シーンを作り直す")]
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
            SetReference(runner, "_roster", LoadOrCreateCharacterRoster(character));
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

            GameAudio audio = CreateAudio(runner);
            CreateDebugTools(runner, inputActions, audio);
            CreateComboHud(runner);
            CreateSkillHud(runner, audio);
            CreateMatchHud(runner, audio);

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
                // 菱形（立方体を 45° 回す。丸だとコインと見分けにくかった。ユーザーの要望、2026-10-15）。対角線は約 0.85m。
                // 判定は WireTuning.AimAssistRadius。
                GameObject go = CreateVisual(PrimitiveType.Cube, "AnchorPoint", anchorMaterial, new Vector3(0.6f, 0.6f, 0.3f));
                go.transform.SetParent(anchorRoot, false);
                go.transform.localPosition = new Vector3(point.x, point.y, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                go.AddComponent<WireAnchorPoint>();
            }
        }

        /// <summary>キャラクターの箱の表示と、攻撃の判定を描く線、撃破技のチャージのエフェクトを作る。</summary>
        internal static CharacterView CreateCharacterView(string name, Material material, Vector3 position)
        {
            GameObject go = CreateVisual(PrimitiveType.Cube, name, material, Vector3.one);
            go.transform.position = position;
            var view = go.AddComponent<CharacterView>();
            AddFacingMarker(view);
            SetReference(view, "_hitboxLine", CreateHitboxLine(name + "Hitbox"));
            SetReference(view, "_chargeEffect", CreateChargeEffect(name + "Charge"));
            AddCombatIndicators(view, name, null, material.GetColor("_BaseColor"));
            return view;
        }

        /// <summary>
        /// キャラクターの箱に、向いている方向の印を付ける（ユーザーの要望、2026-10-16）: 前の面の上のほうの、向いている側に寄せた白い目の帯と、
        /// 向いている側の横へ突き出た印。箱（大きさ 1 の立方体を体の大きさに伸ばしたもの）の子なので、大きさは箱に対する割合で書く。
        /// 右向きで作る（左を向いたら CharacterView が左右を反転する）。オンライン用のシーンでも使う。
        /// </summary>
        internal static void AddFacingMarker(CharacterView view)
        {
            Material material = LoadOrCreateMaterial("FacingMarker", Color.white, unlit: true);
            var root = new GameObject("Facing").transform;
            root.SetParent(view.transform, false);
            GameObject visor = CreateVisual(PrimitiveType.Cube, "Visor", material, new Vector3(0.62f, 0.09f, 0.04f));
            visor.transform.SetParent(root, false);
            visor.transform.localPosition = new Vector3(0.19f, 0.28f, -0.52f);
            GameObject nose = CreateVisual(PrimitiveType.Cube, "Nose", material, new Vector3(0.28f, 0.1f, 0.5f));
            nose.transform.SetParent(root, false);
            nose.transform.localPosition = new Vector3(0.64f, 0.28f, 0f);
            SetReference(view, "_facingMarker", root);
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
            // のけぞり中に地形で跳ね返った（白っぽい水色。土ぼこりのような短い光）。
            BurstEffectView bounce = CreateBurst(name + "BounceBurst", parent);
            bounce.Configure(new Color(0.85f, 0.95f, 1f, 1f), false, 0.3f, 0.5f, 2f, 8, 0.8f, 0.14f, string.Empty, 0, 0f);
            SetReference(view, "_bounceBurst", bounce);
            // ジャンプ台・加速リングが効いた（明るい緑。短い）。
            BurstEffectView gimmick = CreateBurst(name + "GimmickBurst", parent);
            gimmick.Configure(new Color(0.55f, 1f, 0.6f, 1f), false, 0.3f, 0.4f, 1.6f, 8, 0.7f, 0.1f, string.Empty, 0, 0f);
            SetReference(view, "_gimmickBurst", gimmick);
            // 特殊攻撃を出した（プレイヤーの色の、鋭い光。docs/13 T5-4）。
            BurstEffectView special = CreateBurst(name + "SpecialBurst", parent);
            special.Configure(Color.Lerp(playerColor, Color.white, 0.35f), false, 0.3f, 0.3f, 2.2f, 12, 1.4f, 0.13f, string.Empty, 0, 0f);
            SetReference(view, "_specialBurst", special);
            // 必殺技を出した（虹色の大きな光と「SUPER!」。画面の暗転は SuperFlashHud）。
            BurstEffectView super = CreateBurst(name + "SuperBurst", parent);
            super.Configure(Color.white, true, 0.8f, 0.5f, 4.5f, 16, 2.6f, 0.22f, "SUPER!", 56, 0f);
            SetReference(view, "_superBurst", super);
            // ルーレットの最大（JACKPOT。RISK TAKER の撃破技）を受けた（虹色の光と「JACKPOT!」）。
            BurstEffectView jackpot = CreateBurst(name + "JackpotBurst", parent);
            jackpot.Configure(Color.white, true, 1f, 0.4f, 4.5f, 12, 3f, 0.24f, "JACKPOT!", 60, 0.3f);
            SetReference(view, "_jackpotBurst", jackpot);
            // コインの効率が上がっている残り（フィーバータイム。金色）。
            SetReference(view, "_feverGauge", CreateRingGauge(name + "FeverGauge", parent, "FEVER", 1.9f, new Color(1f, 0.8f, 0.2f, 0.75f)));
            // 復帰までの残り（出現位置の周りに大きく。クールタイムのゲージの大きい版。ユーザーの要望、2026-10-13）。
            RingGaugeView respawn = CreateRingGauge(name + "RespawnGauge", parent, "RESPAWN", 2.6f,
                new Color(playerColor.r, playerColor.g, playerColor.b, 0.7f));
            respawn.Configure("RESPAWN", 2.6f, new Color(playerColor.r, playerColor.g, playerColor.b, 0.7f), 0.24f, 28);
            SetReference(view, "_respawnGauge", respawn);
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

        /// <summary>操作するキャラクターのコンボの表示（T3-7）。source は SimulationRunner か OnlineRunner（ISimulationFeed）。</summary>
        internal static void CreateComboHud(MonoBehaviour source)
        {
            var hud = new GameObject("ComboHud").AddComponent<ComboHud>();
            SetReference(hud, "_source", source);
        }

        /// <summary>
        /// 技の表示: 自分の特殊攻撃と必殺技の札（画面の下の中央）、必殺技の暗転（docs/13 T5-4）、置かれた物と投射物、長押しで預ける
        /// 枚数（T5-6）。source は ISimulationFeed。
        /// </summary>
        internal static void CreateSkillHud(MonoBehaviour source, GameAudio audio)
        {
            CreateWorldObjectsView(source, audio);
            var hold = new GameObject("HoldDepositHud").AddComponent<HoldDepositHud>();
            SetReference(hold, "_source", source);
            SetReference(hold, "_camera", Object.FindFirstObjectByType<Camera>());
            CreateChargeAura(source, audio);
            var space = new GameObject("SpaceEffects").AddComponent<SpaceEffectsView>();
            LineRenderer spaceLine = CreateIndicatorLine(space.transform, "SpaceLineTemplate", AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat"), 0.08f);
            spaceLine.gameObject.SetActive(false);
            BurstEffectView warp = CreateBurst("WarpBurstTemplate", space.transform);
            warp.Configure(new Color(0.75f, 0.5f, 1f), false, 0.35f, 0.2f, 1.8f, 10, 1.4f, 0.14f, string.Empty, 0, 0f);
            warp.gameObject.SetActive(false);
            SetReference(space, "_source", source);
            SetReference(space, "_lineTemplate", spaceLine);
            SetReference(space, "_burstTemplate", warp);
            var boost = new GameObject("BoostAura").AddComponent<BoostAuraView>();
            LineRenderer boostLine = CreateIndicatorLine(boost.transform, "BoostLineTemplate", AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat"), 0.08f);
            boostLine.gameObject.SetActive(false);
            SetReference(boost, "_source", source);
            SetReference(boost, "_lineTemplate", boostLine);
            var roulette = new GameObject("RouletteView").AddComponent<RouletteView>();
            SetReference(roulette, "_source", source);
            SetReference(roulette, "_camera", Object.FindFirstObjectByType<Camera>());
            SetReference(roulette, "_audio", audio);
            var root = new GameObject("SkillHud");
            var hud = root.AddComponent<SkillHud>();
            SetReference(hud, "_source", source);
            SetReference(hud, "_audio", audio);
            SetReference(hud, "_camera", Object.FindFirstObjectByType<Camera>());
            var flash = root.AddComponent<SuperFlashHud>();
            SetReference(flash, "_source", source);
            SetReference(flash, "_camera", Object.FindFirstObjectByType<Camera>());
        }

        /// <summary>ためる技のオーラ（ChargeAuraView。見本の線と光は隠しておく）。</summary>
        internal static void CreateChargeAura(MonoBehaviour source, GameAudio audio)
        {
            var view = new GameObject("ChargeAura").AddComponent<ChargeAuraView>();
            var lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            LineRenderer line = CreateIndicatorLine(view.transform, "AuraLineTemplate", lineMaterial, 0.1f);
            line.loop = true;
            BurstEffectView burst = CreateBurst("AuraBurstTemplate", view.transform);
            line.gameObject.SetActive(false);
            burst.gameObject.SetActive(false);
            SetReference(view, "_source", source);
            SetReference(view, "_lineTemplate", line);
            SetReference(view, "_burstTemplate", burst);
            SetReference(view, "_audio", audio);
        }

        /// <summary>
        /// 置かれた物と投射物の表示（WorldObjectsView。見本はミラーボール、セーフティボックス、投射物、突風、風の帯、ワープポータル、
        /// ミサイル、ビームの順）。
        /// </summary>
        internal static void CreateWorldObjectsView(MonoBehaviour source, GameAudio audio)
        {
            var view = new GameObject("WorldObjects").AddComponent<WorldObjectsView>();
            var lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

            // ミラーボールは 2026-10-16 の要望で大きくした（直径 0.9m → 1.5m）。
            GameObject ball = CreateVisual(PrimitiveType.Sphere, "MirrorBallTemplate", LoadOrCreateMaterial("MirrorBall", new Color(0.85f, 0.88f, 0.95f)),
                Vector3.one * 1.5f);
            ball.transform.SetParent(view.transform, false);
            CreateIndicatorLine(ball.transform, "Rays", lineMaterial, 0.06f);

            GameObject safe = CreateVisual(PrimitiveType.Cube, "SafeTemplate", LoadOrCreateMaterial("Safe", new Color(0.32f, 0.34f, 0.38f)),
                new Vector3(1.4f, 1.4f, 1f));
            safe.transform.SetParent(view.transform, false);
            GameObject dial = CreateVisual(PrimitiveType.Cylinder, "Dial", LoadOrCreateMaterial("Coin", new Color(1f, 0.78f, 0.15f)),
                new Vector3(0.35f, 0.05f, 0.35f));
            dial.transform.SetParent(safe.transform, false);
            dial.transform.localPosition = new Vector3(0.15f, 0f, -0.52f);
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // 次のチャージまでの円形のゲージ（下地と、伸びる弧）。
            LineRenderer ringBack = CreateIndicatorLine(safe.transform, "ChargeRingBack", lineMaterial, 0.08f);
            ringBack.loop = true;
            ringBack.startColor = ringBack.endColor = new Color(1f, 0.85f, 0.3f, 0.22f);
            LineRenderer ring = CreateIndicatorLine(safe.transform, "ChargeRing", lineMaterial, 0.12f);
            ring.startColor = ring.endColor = new Color(1f, 0.82f, 0.2f, 0.95f);

            GameObject coin = CreateVisual(PrimitiveType.Cylinder, "ProjectileTemplate", LoadOrCreateMaterial("Coin", new Color(1f, 0.78f, 0.15f)),
                new Vector3(0.55f, 0.06f, 0.55f));
            coin.transform.SetParent(view.transform, false);

            // 突風（弧の形の風の線 3 本）と風の帯（枠と流れる線 6 本）。線だけで作る。
            var gust = new GameObject("GustTemplate");
            gust.transform.SetParent(view.transform, false);
            for (int i = 0; i < 3; i++)
            {
                CreateIndicatorLine(gust.transform, "Streak" + i, lineMaterial, 0.12f - 0.02f * i);
            }
            var windZone = new GameObject("WindZoneTemplate");
            windZone.transform.SetParent(view.transform, false);
            LineRenderer outline = CreateIndicatorLine(windZone.transform, "Outline", lineMaterial, 0.06f);
            outline.startColor = outline.endColor = new Color(0.6f, 1f, 0.45f, 0.35f);
            for (int i = 0; i < 6; i++)
            {
                LineRenderer streak = CreateIndicatorLine(windZone.transform, "Streak" + i, lineMaterial, 0.08f);
                streak.startColor = new Color(0.85f, 1f, 0.75f, 0f);
                streak.endColor = new Color(0.7f, 1f, 0.5f, 0.75f);
            }

            // ワープポータル（輪、らせん、つながりの線）、ミサイル（弾と軌跡）、ビーム（光と芯）。
            var portal = new GameObject("PortalTemplate");
            portal.transform.SetParent(view.transform, false);
            CreateIndicatorLine(portal.transform, "Ring", lineMaterial, 0.14f);
            CreateIndicatorLine(portal.transform, "Swirl", lineMaterial, 0.07f);
            CreateIndicatorLine(portal.transform, "Link", lineMaterial, 0.05f);
            GameObject missile = CreateVisual(PrimitiveType.Cube, "MissileTemplate", LoadOrCreateMaterial("Missile", new Color(0.85f, 0.7f, 1f), unlit: true),
                new Vector3(0.8f, 0.28f, 0.28f));
            missile.transform.SetParent(view.transform, false);
            CreateIndicatorLine(missile.transform, "Trail", lineMaterial, 0.18f);
            var beam = new GameObject("BeamTemplate");
            beam.transform.SetParent(view.transform, false);
            CreateIndicatorLine(beam.transform, "Glow", lineMaterial, 1.5f);
            CreateIndicatorLine(beam.transform, "Core", lineMaterial, 0.4f);

            foreach (GameObject template in new[] { ball, safe, coin, gust, windZone, portal, missile, beam })
            {
                foreach (Collider collider in template.GetComponentsInChildren<Collider>())
                {
                    Object.DestroyImmediate(collider);
                }
                template.SetActive(false);
            }
            SetReference(view, "_source", source);
            SetReferenceArray(view, "_templates", new Object[] { ball, safe, coin, gust, windZone, portal, missile, beam });
            BurstEffectView explosion = CreateBurst("ExplosionTemplate", view.transform);
            explosion.Configure(new Color(0.85f, 0.6f, 1f), false, 0.45f, 0.3f, 2.6f, 12, 2.4f, 0.22f, string.Empty, 0, 0.12f);
            explosion.gameObject.SetActive(false);
            SetReference(view, "_explosionTemplate", explosion);
            SetReference(view, "_camera", Object.FindFirstObjectByType<Camera>());
            SetReference(view, "_audio", audio);
        }

        /// <summary>試合の表示（始まる前の演出、残り時間、結果。docs/12）。source は ISimulationFeed、controls は結果のボタン（IMatchControls）。</summary>
        internal static void CreateMatchHud(MonoBehaviour source, GameAudio audio, MonoBehaviour controls = null)
        {
            var hud = new GameObject("MatchHud").AddComponent<MatchHud>();
            SetReference(hud, "_source", source);
            SetReference(hud, "_audio", audio);
            if (controls != null)
            {
                SetReference(hud, "_controls", controls);
            }
        }

        /// <summary>試合中のお知らせ（タイマーの下の札と、画面を横切る帯。ユーザーの選択、2026-10-14）。source は ISimulationFeed。</summary>
        internal static void CreateNoticeHud(MonoBehaviour source, GameAudio audio)
        {
            var hud = new GameObject("NoticeHud").AddComponent<NoticeHud>();
            SetReference(hud, "_source", source);
            SetReference(hud, "_audio", audio);
        }

        // 100 枚のコインの枚数（ユーザーの要望、2026-10-13）。
        internal const int JackpotValue = 100;

        /// <summary>
        /// コインの出現地点（docs/12 T4-4）と、コインの表示（回る金色のコイン）、所持コインと「+n」の表示を作る。
        /// source は ISimulationFeed。
        /// </summary>
        internal static void CreateCoins((Vector2 Position, int Value)[] spawns, Vector2[] jackpotSpots, MonoBehaviour source, EdgeGuideLayer guides)
        {
            var root = new GameObject("CoinSpawns").transform;
            for (int i = 0; i < spawns.Length; i++)
            {
                var point = new GameObject($"CoinSpawn{i + 1}_{spawns[i].Value}");
                point.transform.SetParent(root, false);
                point.transform.position = new Vector3(spawns[i].Position.x, spawns[i].Position.y, 0f);
                point.AddComponent<CoinSpawnAuthoring>().Configure(spawns[i].Value);
            }
            // 100 枚のコインの候補の地点（残り 1 分に、この中の1つに出る）。
            for (int i = 0; i < jackpotSpots.Length; i++)
            {
                var point = new GameObject($"JackpotSpot{i + 1}");
                point.transform.SetParent(root, false);
                point.transform.position = new Vector3(jackpotSpots[i].x, jackpotSpots[i].y, 0f);
                point.AddComponent<CoinSpawnAuthoring>().Configure(JackpotValue, Airerage.Core.Economy.CoinSpawnKind.Jackpot);
            }

            // 見た目の見本（CoinLook の順: 1 枚は金色、3 枚は金色のピラミッド、5 枚は赤、100 枚は虹色に光る）。
            Material gold = LoadOrCreateMaterial("Coin", new Color(1f, 0.78f, 0.15f));
            Material red = LoadOrCreateMaterial("CoinRed", new Color(0.9f, 0.12f, 0.1f));
            Material rainbow = LoadOrCreateMaterial("CoinRainbow", Color.white, unlit: true);
            var view = new GameObject("Coins").AddComponent<CoinView>();
            GameObject single = CreateCoinTemplate("CoinSingle", view.transform, gold, 0.7f, new[] { Vector2.zero });
            GameObject triple = CreateCoinTemplate("CoinTriple", view.transform, gold, 0.6f,
                new[] { new Vector2(0f, 0.29f), new Vector2(-0.33f, -0.28f), new Vector2(0.33f, -0.28f) });
            GameObject redCoin = CreateCoinTemplate("CoinRed", view.transform, red, 0.75f, new[] { Vector2.zero });
            GameObject jackpot = CreateCoinTemplate("CoinRainbow", view.transform, rainbow, 1.1f, new[] { Vector2.zero });
            var lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            // COIN RUSH のときにコインの周りに出す金色の輪（CoinView が名前で探して使う。ふだんは出さない）。
            foreach (GameObject template in new[] { single, triple, redCoin })
            {
                CreateIndicatorLine(template.transform, "RushHalo", lineMaterial, 0.07f).enabled = false;
            }
            jackpot.AddComponent<RainbowCoinGlow>().Configure(
                jackpot.GetComponentsInChildren<MeshRenderer>(),
                CreateIndicatorLine(jackpot.transform, "Halo", lineMaterial, 0.14f),
                CreateIndicatorLine(jackpot.transform, "Wave", lineMaterial, 0.08f));
            SetReference(view, "_source", source);
            SetReferenceArray(view, "_templates", new Object[] { single, triple, redCoin, jackpot });
            // 赤いコインの出現地点の、次に出るまでのゲージの見本（文字なし。CoinView が地点ごとに複製する）。
            RingGaugeView gauge = CreateRingGauge("RespawnGaugeTemplate", view.transform, string.Empty, 0.75f, new Color(1f, 0.3f, 0.25f, 0.8f));
            SetReference(view, "_respawnGauge", gauge);

            var hud = new GameObject("CoinHud").AddComponent<CoinHud>();
            SetReference(hud, "_source", source);
            SetReference(hud, "_camera", Object.FindFirstObjectByType<Camera>());
            SetReference(hud, "_guides", guides);
        }

        /// <summary>画面の外の物の誘導表示（矢印と札）をまとめて描く層（EdgeGuideLayer）。</summary>
        internal static EdgeGuideLayer CreateEdgeGuides()
        {
            var layer = new GameObject("EdgeGuides").AddComponent<EdgeGuideLayer>();
            SetReference(layer, "_camera", Object.FindFirstObjectByType<Camera>());
            return layer;
        }

        /// <summary>ほかのプレイヤーが画面の外にいるときの誘導表示（PlayerGuides）。</summary>
        internal static void CreatePlayerGuides(MonoBehaviour source, EdgeGuideLayer guides)
        {
            var view = new GameObject("PlayerGuides").AddComponent<PlayerGuides>();
            SetReference(view, "_source", source);
            SetReference(view, "_guides", guides);
        }

        /// <summary>
        /// 拠点の候補の地点（BankSpotAuthoring。spots[0] が試合の始まりの拠点）と、拠点の表示（BankView。輪、回る弧、光の柱、
        /// 次の地点の輪）を作る（docs/12 T4-7）。
        /// </summary>
        internal static void CreateBank(Vector2[] spots, MonoBehaviour source, GameAudio audio, EdgeGuideLayer guides)
        {
            var root = new GameObject("BankSpots").transform;
            for (int i = 0; i < spots.Length; i++)
            {
                var point = new GameObject(i == 0 ? "BankSpot1_First" : $"BankSpot{i + 1}");
                point.transform.SetParent(root, false);
                point.transform.position = new Vector3(spots[i].x, spots[i].y, 0f);
                point.AddComponent<BankSpotAuthoring>().Configure(i == 0);
            }

            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var view = new GameObject("Bank").AddComponent<BankView>();
            SetReference(view, "_source", source);
            SetReference(view, "_camera", Object.FindFirstObjectByType<Camera>());
            SetReference(view, "_ring", CreateIndicatorLine(view.transform, "Ring", material, 0.22f));
            SetReference(view, "_sweep", CreateIndicatorLine(view.transform, "Sweep", material, 0.14f));
            SetReference(view, "_beam", CreateIndicatorLine(view.transform, "Beam", material, 1.6f));
            SetReference(view, "_next", CreateIndicatorLine(view.transform, "Next", material, 0.14f));
            SetReference(view, "_audio", audio);
            SetReference(view, "_guides", guides);
        }

        /// <summary>右上の全員の情報（ScoreboardHud。順位の順に並ぶ札）。</summary>
        internal static void CreateScoreboard(MonoBehaviour source, GameAudio audio)
        {
            var hud = new GameObject("Scoreboard").AddComponent<ScoreboardHud>();
            SetReference(hud, "_source", source);
            SetReference(hud, "_audio", audio);
        }

        /// <summary>試合の終盤の雰囲気（MatchAtmosphere。100 枚のコインの予告から、緊張感のある BGM と赤い空）。</summary>
        internal static void CreateAtmosphere(MonoBehaviour source, GameAudio audio)
        {
            var atmosphere = new GameObject("Atmosphere").AddComponent<MatchAtmosphere>();
            SetReference(atmosphere, "_source", source);
            SetReference(atmosphere, "_audio", audio);
        }

        // コインの見た目の見本: 空の親の下に、面をカメラに向けたコイン（円柱）を faces の位置に置く。
        private static GameObject CreateCoinTemplate(string name, Transform parent, Material material, float diameter, Vector2[] faces)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            for (int i = 0; i < faces.Length; i++)
            {
                GameObject face = CreateVisual(PrimitiveType.Cylinder, $"Face{i + 1}", material, new Vector3(diameter, 0.05f, diameter));
                face.transform.SetParent(root.transform, false);
                face.transform.localPosition = new Vector3(faces[i].x, faces[i].y, 0f);
                face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            return root;
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
        /// 仮の BGM と効果音（音はその場で合成する。ユーザーの要望、2026-10-13）。状態の変化から効果音を鳴らす部品も付ける
        /// （source は SimulationRunner か OnlineRunner。ISimulationFeed）。
        /// </summary>
        internal static GameAudio CreateAudio(MonoBehaviour source)
        {
            var go = new GameObject("Audio");
            var audio = go.AddComponent<GameAudio>();
            var events = go.AddComponent<CombatSoundEvents>();
            SetReference(events, "_source", source);
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
        /// マウスホイールと十字キーの上下で距離を変えられる（CameraZoom）。
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

        /// <summary>
        /// 基準のキャラクター（STANDARD）のアセット。ほかのキャラクターと同じく、ビルドのたびに CharacterPresets の値で作り直す
        /// （値はコードの 1 か所で決める。ロゴを設定するとアセットの全部の項目が書き出され、コードの初期値を変えても反映されなく
        /// なるため。docs/13 T5-8）。
        /// </summary>
        internal static CharacterConfigAsset LoadOrCreateCharacterConfig()
        {
            return WritePresetCharacter(CharacterConfigPath, CharacterPresets.Standard(), CharacterPresets.StandardName);
        }

        /// <summary>
        /// 選べるキャラクターの一覧（docs/13 T5-5、T5-6）を作り直す: character（STANDARD）と、CharacterPresets から作るキャラクター
        /// （RISK TAKER、STORM RUNNER、SPACE TRAVELER）。キャラクターのアセットは、ビルドのたびに CharacterPresets の値で作り直す
        /// （値はコードの 1 か所で決める）。仮のロゴ（CharacterLogoPainter）も描き直して設定する（docs/13 T5-8）。
        /// </summary>
        internal static CharacterRoster LoadOrCreateCharacterRoster(CharacterConfigAsset character)
        {
            CharacterConfigAsset riskTaker = WritePresetCharacter(RiskTakerPath, CharacterPresets.RiskTaker(), CharacterPresets.RiskTakerName);
            CharacterConfigAsset stormRunner = WritePresetCharacter(StormRunnerPath, CharacterPresets.StormRunner(), CharacterPresets.StormRunnerName);
            CharacterConfigAsset spaceTraveler = WritePresetCharacter(SpaceTravelerPath, CharacterPresets.SpaceTraveler(), CharacterPresets.SpaceTravelerName);
            AssignLogo(character, "Standard", CharacterLogoPainter.Standard());
            AssignLogo(riskTaker, "RiskTaker", CharacterLogoPainter.RiskTaker());
            AssignLogo(stormRunner, "StormRunner", CharacterLogoPainter.StormRunner());
            AssignLogo(spaceTraveler, "SpaceTraveler", CharacterLogoPainter.SpaceTraveler());
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(CharacterRosterPath);
            if (roster == null)
            {
                EnsureFolder(Path.GetDirectoryName(CharacterRosterPath));
                roster = ScriptableObject.CreateInstance<CharacterRoster>();
                AssetDatabase.CreateAsset(roster, CharacterRosterPath);
            }
            roster.Configure(new[] { character, riskTaker, stormRunner, spaceTraveler });
            EditorUtility.SetDirty(roster);
            AssetDatabase.SaveAssets();
            return roster;
        }

        // 仮のロゴ（CharacterLogoPainter）を描き直して、キャラクターに設定する。ほかの絵（本番の絵）が設定されていれば、そのままにする。
        private static void AssignLogo(CharacterConfigAsset character, string fileName, Color[] pixels)
        {
            string path = $"{CharacterLogoPainter.Folder}/{fileName}Logo.asset";
            Texture2D logo = CharacterLogoPainter.Write(path, pixels);
            if (character.Logo == null || AssetDatabase.GetAssetPath(character.Logo) == path)
            {
                character.SetLogo(logo);
                EditorUtility.SetDirty(character);
            }
        }

        private static CharacterConfigAsset WritePresetCharacter(string path, Airerage.Core.Simulation.CharacterConfig config, string displayName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterConfigAsset>(path);
            if (asset == null)
            {
                EnsureFolder(Path.GetDirectoryName(path));
                asset = ScriptableObject.CreateInstance<CharacterConfigAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.Configure(config, displayName);
            EditorUtility.SetDirty(asset);
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
