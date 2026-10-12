using System;
using System.IO;
using Degunatsu.Runtime.Data;
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

        private readonly struct TerrainBox
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

        // T1-7 の確認用の最初の配置。Core のテストの遊び場（TestArenas.Playground）と同じ。T1-8 で広げる。
        private static readonly TerrainBox[] Terrain =
        {
            new TerrainBox("Ground", -30f, -1f, 30f, 0f),
            new TerrainBox("Ceiling", -30f, 10f, 30f, 11f, attachable: true),
            new TerrainBox("WallLeft", -31f, -1f, -30f, 11f),
            new TerrainBox("WallRight", 30f, -1f, 31f, 11f),
            new TerrainBox("ThinWall", 8f, 0f, 8.2f, 6f),
            new TerrainBox("FloatingPlatform", -4f, 4f, -1f, 4.5f, attachable: true),
        };

        private static readonly Vector2[] AnchorPoints = { new Vector2(3f, 7f), new Vector2(-10f, 6f) };
        private static readonly Vector3 SpawnPosition = new Vector3(-15f, 1f, 0f);

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


            var terrainRoot = new GameObject("Terrain").transform;
            foreach (TerrainBox box in Terrain)
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
            foreach (Vector2 point in AnchorPoints)
            {
                GameObject go = CreateVisual(PrimitiveType.Sphere, "AnchorPoint", anchorMaterial, Vector3.one * 0.5f);
                go.transform.SetParent(anchorRoot, false);
                go.transform.localPosition = new Vector3(point.x, point.y, 0f);
                go.AddComponent<WireAnchorPoint>();
            }

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = SpawnPosition;

            GameObject characterGo = CreateVisual(PrimitiveType.Cube, "Character", characterMaterial, Vector3.one);
            var characterView = characterGo.AddComponent<CharacterView>();

            var wireGo = new GameObject("Wire");
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

            CreateFollowCamera(characterGo.transform);

            var runner = new GameObject("Simulation").AddComponent<SimulationRunner>();
            SetReference(runner, "_character", character);
            SetReference(runner, "_inputActions", inputActions);
            SetReference(runner, "_spawnPoint", spawn);
            SetReference(runner, "_characterView", characterView);
            SetReference(runner, "_wireView", wireView);

            EnsureFolder(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {ScenePath}");
            }
            AddToBuildSettings(ScenePath);
            Debug.Log($"SandboxWireSceneBuilder: {ScenePath} を作った");
        }

        /// <summary>ビルド設定のシーン一覧に加える（PlayMode テストで名前から読み込むため）。</summary>
        private static void AddToBuildSettings(string path)
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

        /// <summary>キャラクターを横から追従するカメラ（Cinemachine 3）。回転はせず、位置だけを合わせる。</summary>
        private static void CreateFollowCamera(Transform target)
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
        }

        private static CharacterConfigAsset LoadOrCreateCharacterConfig()
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

        private static Material LoadOrCreateMaterial(string name, Color color, bool unlit = false)
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
        private static GameObject CreateVisual(PrimitiveType type, string name, Material material, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static void SetReference(Object target, string field, Object value)
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

        private static void EnsureFolder(string folder)
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
