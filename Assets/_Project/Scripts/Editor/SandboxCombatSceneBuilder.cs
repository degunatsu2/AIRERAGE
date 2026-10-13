using System;
using System.IO;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using TerrainBox = Degunatsu.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Degunatsu.Editor
{
    /// <summary>
    /// 空中戦の検証用シーン（Sandbox_Combat、docs/11_combat_plan.md T3-3）を作る。足場が少なく、下と左右が外
    /// （ステージの範囲の外に出ると撃破）。操作するキャラクター1人と、ダミー3人を置く。
    /// 地形の配置はこのコードで決め、シーンは生成物として扱う。
    ///
    /// バッチモードでの実行:
    ///   Unity -batchmode -nographics -quit -projectPath . -executeMethod Degunatsu.Editor.SandboxCombatSceneBuilder.Build
    /// </summary>
    public static class SandboxCombatSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Sandbox_Combat.unity";

        // 足場はすべて接続できる（落ちたときにワイヤーで戻れるように）。中央の足場は横 16m、左右と上の足場は小さい。
        // 配置は今の移動とワイヤーの仮の値（ジャンプ 2.4m＋空中ジャンプ、ワイヤーの最大長 12m）で行き来できるように決めた【仮】。
        private static readonly TerrainBox[] Terrain =
        {
            new TerrainBox("MainPlatform", -8f, -1.5f, 8f, 0f, attachable: true),
            new TerrainBox("SidePlatformLeft", -16f, 4f, -11f, 4.5f, attachable: true),
            new TerrainBox("SidePlatformRight", 11f, 4f, 16f, 4.5f, attachable: true),
            new TerrainBox("TopPlatform", -3f, 9f, 3f, 9.5f, attachable: true),
        };

        private static readonly Vector2[] AnchorPoints =
        {
            new Vector2(-9f, 13f),
            new Vector2(9f, 13f),
            new Vector2(0f, 17f),
            new Vector2(-21f, 9f),
            new Vector2(21f, 9f),
        };

        // コインの出現地点（docs/12 T4-4）【仮】。
        private static readonly (Vector2 Position, int Value)[] CoinSpawns =
        {
            (new Vector2(0f, 4f), 5),
            (new Vector2(-13.5f, 8f), 5),
            (new Vector2(13.5f, 8f), 5),
            (new Vector2(0f, 13f), 10),
            (new Vector2(-21f, 13f), 15),
            (new Vector2(21f, 13f), 15),
        };

        // ステージの範囲（外に出ると撃破）【仮】。足場から下へ 16m、左右へ 18m ほど。
        private static readonly Vector2 BoundsMin = new Vector2(-34f, -16f);
        private static readonly Vector2 BoundsMax = new Vector2(34f, 36f);

        internal static readonly Vector3 SpawnPosition = new Vector3(0f, 1f, 0f);

        // ダミーの出現位置。1人目は中央の足場の右端の近く（外へ飛ばして撃破を試す）、残りは左右の足場の上。
        internal static readonly Vector3[] DummySpawnPositions =
        {
            new Vector3(6.5f, 1f, 0f),
            new Vector3(-13.5f, 5.5f, 0f),
            new Vector3(13.5f, 5.5f, 0f),
        };

        [MenuItem("Degunatsu/Sandbox_Combat シーンを作り直す")]
        private static void BuildFromMenu()
        {
            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog("Sandbox_Combat", $"{ScenePath} を作り直します。シーンを手で編集した内容は消えます。", "作り直す", "やめる"))
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

            var character = SandboxWireSceneBuilder.LoadOrCreateCharacterConfig();
            var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(SandboxWireSceneBuilder.InputActionsPath);
            if (inputActions == null)
            {
                throw new InvalidOperationException($"入力アクションのアセットがない: {SandboxWireSceneBuilder.InputActionsPath}");
            }

            Material terrainMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Terrain", new Color(0.55f, 0.55f, 0.58f));
            Material attachableMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("TerrainAttachable", new Color(0.25f, 0.6f, 0.75f));
            Material characterMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Character", new Color(0.95f, 0.55f, 0.2f));
            Material anchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Anchor", new Color(0.95f, 0.85f, 0.2f));
            Material wireMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Wire", new Color(0.6f, 0.2f, 0.85f), unlit: true);
            Material attachedAnchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("WireAnchor", new Color(1f, 0.5f, 0f), unlit: true);

            SandboxWireSceneBuilder.CreateTerrain(Terrain, AnchorPoints, terrainMaterial, attachableMaterial, anchorMaterial);
            CreateBounds(BoundsMin, BoundsMax);

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = SpawnPosition;
            CharacterView characterView = SandboxWireSceneBuilder.CreateCharacterView("Character", characterMaterial, SpawnPosition);

            Material dummyMaterial = SandboxWireSceneBuilder.LoadDummyMaterial();
            var dummySpawns = new Object[DummySpawnPositions.Length];
            var dummyViews = new Object[DummySpawnPositions.Length];
            for (int i = 0; i < DummySpawnPositions.Length; i++)
            {
                var dummySpawn = new GameObject($"DummySpawn{i + 1}").transform;
                dummySpawn.position = DummySpawnPositions[i];
                dummySpawns[i] = dummySpawn;
                dummyViews[i] = SandboxWireSceneBuilder.CreateCharacterView($"Dummy{i + 1}", dummyMaterial, DummySpawnPositions[i]);
            }

            WireView wireView = SandboxWireSceneBuilder.CreateWireView("Wire", null, wireMaterial, attachedAnchorMaterial);
            SandboxWireSceneBuilder.CreateFollowCamera(characterView.transform, inputActions);
            AimIndicatorView aimIndicator = SandboxWireSceneBuilder.CreateAimIndicator();
            Camera camera = Object.FindFirstObjectByType<Camera>();

            var runner = new GameObject("Simulation").AddComponent<SimulationRunner>();
            SandboxWireSceneBuilder.SetReference(runner, "_character", character);
            SandboxWireSceneBuilder.SetReference(runner, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(runner, "_spawnPoint", spawn);
            SandboxWireSceneBuilder.SetReference(runner, "_characterView", characterView);
            SandboxWireSceneBuilder.SetReference(runner, "_wireView", wireView);
            SandboxWireSceneBuilder.SetReference(runner, "_aimIndicator", aimIndicator);
            SandboxWireSceneBuilder.SetReference(runner, "_camera", camera);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_dummySpawns", dummySpawns);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_dummyViews", dummyViews);

            SandboxWireSceneBuilder.CreateMissIndicator(runner, camera);
            GameAudio audio = SandboxWireSceneBuilder.CreateAudio(runner);
            SandboxWireSceneBuilder.CreateDebugTools(runner, inputActions, audio);
            SandboxWireSceneBuilder.CreateComboHud(runner);
            SandboxWireSceneBuilder.CreateMatchHud(runner, audio);
            SandboxWireSceneBuilder.CreateCoins(CoinSpawns, runner);

            SandboxWireSceneBuilder.EnsureFolder(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {ScenePath}");
            }
            SandboxWireSceneBuilder.AddToBuildSettings(ScenePath);
            Debug.Log($"SandboxCombatSceneBuilder: {ScenePath} を作った");
        }

        /// <summary>ステージの範囲（KnockoutBounds）と、その枠の線（赤）を作る。オンライン用のシーンでも使う。</summary>
        internal static void CreateBounds(Vector2 min, Vector2 max)
        {
            var go = new GameObject("KnockoutBounds");
            go.AddComponent<KnockoutBounds>().Configure(min, max);

            var material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            LineRenderer frame = SandboxWireSceneBuilder.CreateIndicatorLine(go.transform, "Frame", material, 0.2f);
            frame.startColor = frame.endColor = new Color(1f, 0.25f, 0.25f, 0.8f);
            frame.loop = true;
            frame.positionCount = 4;
            frame.SetPosition(0, new Vector3(min.x, min.y, 0f));
            frame.SetPosition(1, new Vector3(max.x, min.y, 0f));
            frame.SetPosition(2, new Vector3(max.x, max.y, 0f));
            frame.SetPosition(3, new Vector3(min.x, max.y, 0f));
        }
    }
}
