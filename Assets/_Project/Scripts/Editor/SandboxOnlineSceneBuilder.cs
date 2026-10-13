using System;
using System.IO;
using Degunatsu.Net;
using Degunatsu.Runtime.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Degunatsu.Editor
{
    /// <summary>
    /// オンライン接続の検証用シーン（Sandbox_Online、docs/10_online_plan.md T2-7）を作る。地形は Sandbox_Wire と同じアリーナ。
    /// 4人分のキャラクターとワイヤーの表示を置き、OnlineRunner が人数に合わせて表示する。
    ///
    /// バッチモードでの実行:
    ///   Unity -batchmode -nographics -quit -projectPath . -executeMethod Degunatsu.Editor.SandboxOnlineSceneBuilder.Build
    /// </summary>
    public static class SandboxOnlineSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Sandbox_Online.unity";

        // プレイヤーごとの色（仮）。1人目は Sandbox_Wire のキャラクターと同じ色。
        private static readonly (string Material, Color Color)[] PlayerColors =
        {
            ("Character", new Color(0.95f, 0.55f, 0.2f)),
            ("Player2", new Color(0.25f, 0.45f, 0.95f)),
            ("Player3", new Color(0.3f, 0.8f, 0.35f)),
            ("Player4", new Color(0.9f, 0.3f, 0.75f)),
        };

        [MenuItem("Degunatsu/Sandbox_Online シーンを作り直す")]
        private static void BuildFromMenu()
        {
            if (File.Exists(ScenePath)
                && !EditorUtility.DisplayDialog("Sandbox_Online", $"{ScenePath} を作り直します。シーンを手で編集した内容は消えます。", "作り直す", "やめる"))
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
            Material anchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Anchor", new Color(0.95f, 0.85f, 0.2f));
            Material wireMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("Wire", new Color(0.6f, 0.2f, 0.85f), unlit: true);
            Material attachedAnchorMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("WireAnchor", new Color(1f, 0.5f, 0f), unlit: true);

            SandboxWireSceneBuilder.CreateArena(terrainMaterial, attachableMaterial, anchorMaterial);

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = SandboxWireSceneBuilder.SpawnPosition;

            var characterViews = new CharacterView[OnlineSession.MaxPlayers];
            var wireViews = new WireView[OnlineSession.MaxPlayers];
            var playersRoot = new GameObject("Players").transform;
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                Material material = SandboxWireSceneBuilder.LoadOrCreateMaterial(PlayerColors[p].Material, PlayerColors[p].Color);
                GameObject characterGo = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, $"Player{p + 1}", material, Vector3.one);
                characterGo.transform.SetParent(playersRoot, false);
                characterGo.transform.position = spawn.position;
                characterViews[p] = characterGo.AddComponent<CharacterView>();
                SandboxWireSceneBuilder.SetReference(characterViews[p], "_hitboxLine", SandboxWireSceneBuilder.CreateHitboxLine($"Player{p + 1}Hitbox"));
                SandboxWireSceneBuilder.SetReference(characterViews[p], "_chargeEffect", SandboxWireSceneBuilder.CreateChargeEffect($"Player{p + 1}Charge"));
                SandboxWireSceneBuilder.SetReference(characterViews[p], "_wireCancelGauge",
                    SandboxWireSceneBuilder.CreateRingGauge($"Player{p + 1}WireCancelGauge", playersRoot, "WIRE CANCEL", 1.3f));
                wireViews[p] = SandboxWireSceneBuilder.CreateWireView($"Player{p + 1}Wire", playersRoot, wireMaterial, attachedAnchorMaterial);
            }

            var followCamera = SandboxWireSceneBuilder.CreateFollowCamera(spawn, inputActions);
            AimIndicatorView aimIndicator = SandboxWireSceneBuilder.CreateAimIndicator();

            var runner = new GameObject("Online").AddComponent<OnlineRunner>();
            SandboxWireSceneBuilder.SetReference(runner, "_character", character);
            SandboxWireSceneBuilder.SetReference(runner, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(runner, "_spawnPoint", spawn);
            SandboxWireSceneBuilder.SetReference(runner, "_camera", Object.FindFirstObjectByType<Camera>());
            SandboxWireSceneBuilder.SetReference(runner, "_followCamera", followCamera);
            SandboxWireSceneBuilder.SetReference(runner, "_aimIndicator", aimIndicator);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_characterViews", characterViews);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_wireViews", wireViews);

            SandboxWireSceneBuilder.EnsureFolder(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {ScenePath}");
            }
            SandboxWireSceneBuilder.AddToBuildSettings(ScenePath);
            Debug.Log($"SandboxOnlineSceneBuilder: {ScenePath} を作った");
        }
    }
}
