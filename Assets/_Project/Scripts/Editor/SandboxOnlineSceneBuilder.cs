using System;
using System.IO;
using Degunatsu.Core.Simulation;
using Degunatsu.Net;
using Degunatsu.Runtime.Audio;
using Degunatsu.Runtime.Presentation;
using Degunatsu.Runtime.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using TerrainBox = Degunatsu.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Degunatsu.Editor
{
    /// <summary>
    /// オンライン対戦のシーン（Sandbox_Online）を作る（docs/10_online_plan.md T2-7、docs/11_combat_plan.md T3-8）。
    /// 友達と遊べる、空中戦のアスレチックステージ（ユーザーの要望、2026-10-13）: 地上の部分は少なく、浮いた足場、飛び石、
    /// ジャンプ台、加速リング、上昇気流、ワイヤーのアンカーを置く。ステージの範囲の外に出ると撃破。4人分の出現位置。
    /// 4人分のキャラクターとワイヤーの表示を置き、OnlineRunner が人数に合わせて表示する。
    /// 地形とギミックの配置と値はこのコードで決め、シーンは生成物として扱う【仮】。
    ///
    /// バッチモードでの実行:
    ///   Unity -batchmode -nographics -quit -projectPath . -executeMethod Degunatsu.Editor.SandboxOnlineSceneBuilder.Build
    /// </summary>
    public static class SandboxOnlineSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Sandbox_Online.unity";

        private const string MaterialFolder = "Assets/_Project/Materials";

        // プレイヤーごとの色（仮）。1人目は Sandbox_Wire のキャラクターと同じ色。
        internal static readonly (string Material, Color Color)[] PlayerColors =
        {
            ("Character", new Color(0.95f, 0.55f, 0.2f)),
            ("Player2", new Color(0.25f, 0.45f, 0.95f)),
            ("Player3", new Color(0.3f, 0.8f, 0.35f)),
            ("Player4", new Color(0.9f, 0.3f, 0.75f)),
        };

        // 地形（左右対称）。青は接続できる、灰色（飛び石）は接続できない（跳んで渡る）。
        // 移動とワイヤーの今の値（ジャンプ 2.4m＋空中ジャンプ、走り 8m/s、ワイヤーの最大長 12m）で行き来できるように決めた【仮】。
        internal static readonly TerrainBox[] Terrain =
        {
            // 中央の島（横 16m）。真ん中にジャンプ台。
            new TerrainBox("CenterIsland", -8f, -1.5f, 8f, 0f, attachable: true),
            // 左右の島。内側の端にジャンプ台。
            new TerrainBox("SideIslandLeft", -29f, 1.5f, -19f, 2.5f, attachable: true),
            new TerrainBox("SideIslandRight", 19f, 1.5f, 29f, 2.5f, attachable: true),
            // 中段の足場（加速リングの下）。
            new TerrainBox("MidLedgeLeft", -17f, 7f, -12f, 7.5f, attachable: true),
            new TerrainBox("MidLedgeRight", 12f, 7f, 17f, 7.5f, attachable: true),
            // 最上段の足場（中央のジャンプ台→加速リングで上がる）。
            new TerrainBox("TopPerchLeft", -10f, 20f, -4f, 20.5f, attachable: true),
            new TerrainBox("TopPerchRight", 4f, 20f, 10f, 20.5f, attachable: true),
            // 外側の飛び石（4m ずつ高くなる。ジグザグに二段ジャンプで跳んで最上段へ）。
            new TerrainBox("SteppingStoneLeft1", -24.5f, 6f, -22f, 6.5f),
            new TerrainBox("SteppingStoneLeft2", -29f, 10f, -26.5f, 10.5f),
            new TerrainBox("SteppingStoneLeft3", -24.5f, 14f, -22f, 14.5f),
            new TerrainBox("SteppingStoneLeft4", -19f, 18f, -16.5f, 18.5f),
            new TerrainBox("SteppingStoneRight1", 22f, 6f, 24.5f, 6.5f),
            new TerrainBox("SteppingStoneRight2", 26.5f, 10f, 29f, 10.5f),
            new TerrainBox("SteppingStoneRight3", 22f, 14f, 24.5f, 14.5f),
            new TerrainBox("SteppingStoneRight4", 16.5f, 18f, 19f, 18.5f),
        };

        // ワイヤーのアンカー。外側の 2 つ（x = ±37）は、横へ飛ばされたときに戻るためのもの。
        internal static readonly Vector2[] AnchorPoints =
        {
            new Vector2(0f, 28f),
            new Vector2(-14f, 25f),
            new Vector2(14f, 25f),
            new Vector2(-27f, 20f),
            new Vector2(27f, 20f),
            new Vector2(-9f, 12f),
            new Vector2(9f, 12f),
            new Vector2(-37f, 8f),
            new Vector2(37f, 8f),
        };

        // ギミック（中心、大きさ、向き、強さ、上限）【仮】。
        internal static readonly (string Name, StageZoneKind Kind, Vector2 Center, Vector2 Size, Vector2 Direction, float Power, float Limit)[] Zones =
        {
            // 中央のジャンプ台: 真上の加速リングまで届く（高さ約 15m）。
            ("CenterPad", StageZoneKind.JumpPad, new Vector2(0f, 0.2f), new Vector2(2f, 0.4f), Vector2.up, 30f, 0f),
            // 左右の島のジャンプ台: 高さ約 9.6m。少し内側へ流れると、加速リングに入れる。
            ("SidePadLeft", StageZoneKind.JumpPad, new Vector2(-20f, 2.7f), new Vector2(2f, 0.4f), Vector2.up, 24f, 0f),
            ("SidePadRight", StageZoneKind.JumpPad, new Vector2(20f, 2.7f), new Vector2(2f, 0.4f), Vector2.up, 24f, 0f),
            // 中央の加速リング（上向き）: 最上段へ。
            ("CenterRing", StageZoneKind.BoostRing, new Vector2(0f, 17f), new Vector2(3f, 3.5f), Vector2.up, 22f, 0f),
            // 左右の加速リング（中央へ向かう）: 速さを乗せて撃破技を当てる（FATAL は 14m/s 以上）。
            ("SideRingLeft", StageZoneKind.BoostRing, new Vector2(-16f, 11f), new Vector2(2.5f, 3f), new Vector2(1f, 0.1f), 18f, 0f),
            ("SideRingRight", StageZoneKind.BoostRing, new Vector2(16f, 11f), new Vector2(2.5f, 3f), new Vector2(-1f, 0.1f), 18f, 0f),
            // 島の間の上昇気流（落ちた人を島の高さまで押し上げる。強く吹き飛ばされると下まで落ちる）。
            ("UpdraftLeft", StageZoneKind.Updraft, new Vector2(-13.5f, -8f), new Vector2(7f, 20f), Vector2.up, 45f, 10f),
            ("UpdraftRight", StageZoneKind.Updraft, new Vector2(13.5f, -8f), new Vector2(7f, 20f), Vector2.up, 45f, 10f),
        };

        // ステージの範囲（外に出ると撃破）【仮】。
        internal static readonly Vector2 BoundsMin = new Vector2(-46f, -22f);
        internal static readonly Vector2 BoundsMax = new Vector2(46f, 44f);

        // 試合の前にカメラを向ける位置。
        internal static readonly Vector3 CameraFocus = new Vector3(0f, 1f, 0f);

        // 出現位置（番号の順）。2人なら中央の島で向かい合い、3・4人目は左右の島。
        internal static readonly Vector3[] SpawnPositions =
        {
            new Vector3(-5f, 1f, 0f),
            new Vector3(5f, 1f, 0f),
            new Vector3(-24f, 3.5f, 0f),
            new Vector3(24f, 3.5f, 0f),
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

            SandboxWireSceneBuilder.CreateTerrain(Terrain, AnchorPoints, terrainMaterial, attachableMaterial, anchorMaterial);
            CreateZones();
            SandboxCombatSceneBuilder.CreateBounds(BoundsMin, BoundsMax);

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.position = CameraFocus;
            var spawnPoints = new Object[SpawnPositions.Length];
            for (int p = 0; p < SpawnPositions.Length; p++)
            {
                var point = new GameObject($"SpawnPoint{p + 1}").transform;
                point.position = SpawnPositions[p];
                spawnPoints[p] = point;
            }

            var characterViews = new CharacterView[OnlineSession.MaxPlayers];
            var wireViews = new WireView[OnlineSession.MaxPlayers];
            var colors = new Color[OnlineSession.MaxPlayers];
            var playersRoot = new GameObject("Players").transform;
            for (int p = 0; p < OnlineSession.MaxPlayers; p++)
            {
                colors[p] = PlayerColors[p].Color;
                Material material = SandboxWireSceneBuilder.LoadOrCreateMaterial(PlayerColors[p].Material, PlayerColors[p].Color);
                GameObject characterGo = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, $"Player{p + 1}", material, Vector3.one);
                characterGo.transform.SetParent(playersRoot, false);
                characterGo.transform.position = SpawnPositions[p];
                characterViews[p] = characterGo.AddComponent<CharacterView>();
                SandboxWireSceneBuilder.SetReference(characterViews[p], "_hitboxLine", SandboxWireSceneBuilder.CreateHitboxLine($"Player{p + 1}Hitbox"));
                SandboxWireSceneBuilder.SetReference(characterViews[p], "_chargeEffect", SandboxWireSceneBuilder.CreateChargeEffect($"Player{p + 1}Charge"));
                SandboxWireSceneBuilder.AddCombatIndicators(characterViews[p], $"Player{p + 1}", playersRoot, PlayerColors[p].Color);
                wireViews[p] = SandboxWireSceneBuilder.CreateWireView($"Player{p + 1}Wire", playersRoot, wireMaterial, attachedAnchorMaterial);
            }

            var followCamera = SandboxWireSceneBuilder.CreateFollowCamera(spawn, inputActions);
            AimIndicatorView aimIndicator = SandboxWireSceneBuilder.CreateAimIndicator();

            var runner = new GameObject("Online").AddComponent<OnlineRunner>();
            SandboxWireSceneBuilder.SetReference(runner, "_character", character);
            SandboxWireSceneBuilder.SetReference(runner, "_inputActions", inputActions);
            SandboxWireSceneBuilder.SetReference(runner, "_spawnPoint", spawn);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_spawnPoints", spawnPoints);
            SandboxWireSceneBuilder.SetReference(runner, "_camera", Object.FindFirstObjectByType<Camera>());
            SandboxWireSceneBuilder.SetReference(runner, "_followCamera", followCamera);
            SandboxWireSceneBuilder.SetReference(runner, "_aimIndicator", aimIndicator);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_characterViews", characterViews);
            SandboxWireSceneBuilder.SetReferenceArray(runner, "_wireViews", wireViews);
            SetColors(runner, "_playerColors", colors);

            GameAudio audio = SandboxWireSceneBuilder.CreateAudio(runner);
            SandboxWireSceneBuilder.SetReference(runner, "_audio", audio);
            SandboxWireSceneBuilder.CreateComboHud(runner);
            SandboxWireSceneBuilder.CreateMatchHud(runner, audio);

            SandboxWireSceneBuilder.EnsureFolder(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {ScenePath}");
            }
            SandboxWireSceneBuilder.AddToBuildSettings(ScenePath);
            Debug.Log($"SandboxOnlineSceneBuilder: {ScenePath} を作った");
        }

        /// <summary>ギミック（StageZoneAuthoring と、その見た目）を作る。</summary>
        private static void CreateZones()
        {
            var lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            Material padMaterial = SandboxWireSceneBuilder.LoadOrCreateMaterial("JumpPad", new Color(0.3f, 0.95f, 0.4f));
            Material updraftMaterial = LoadOrCreateTransparentMaterial("UpdraftFill", new Color(0.55f, 0.85f, 1f, 0.12f));
            var root = new GameObject("Gimmicks").transform;
            foreach (var zone in Zones)
            {
                var go = new GameObject(zone.Name);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(zone.Center.x, zone.Center.y, 0f);
                go.AddComponent<StageZoneAuthoring>().Configure(zone.Kind, zone.Size, zone.Direction, zone.Power, zone.Limit);
                go.AddComponent<StageZoneView>().Configure(lineMaterial, ColorOf(zone.Kind));

                switch (zone.Kind)
                {
                    case StageZoneKind.JumpPad:
                        // 足場の上の薄い台（範囲の下の端が足場の上面）。
                        GameObject pad = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, "Pad", padMaterial, new Vector3(zone.Size.x, 0.2f, 1.6f));
                        pad.transform.SetParent(go.transform, false);
                        pad.transform.localPosition = new Vector3(0f, -zone.Size.y * 0.5f + 0.1f, 0f);
                        break;
                    case StageZoneKind.Updraft:
                        // 薄い水色の柱（キャラクターの後ろ）。
                        GameObject column = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "Column", updraftMaterial, new Vector3(zone.Size.x, zone.Size.y, 1f));
                        column.transform.SetParent(go.transform, false);
                        column.transform.localPosition = new Vector3(0f, 0f, 1f);
                        column.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                        break;
                }
            }
        }

        private static Color ColorOf(StageZoneKind kind)
        {
            switch (kind)
            {
                case StageZoneKind.JumpPad: return new Color(0.4f, 1f, 0.5f, 1f);
                case StageZoneKind.Updraft: return new Color(0.75f, 0.92f, 1f, 1f);
                default: return new Color(1f, 0.65f, 0.2f, 1f);
            }
        }

        // 半透明の色の材質（ライトを使わない Sprites/Default。線と同じシェーダー）。
        private static Material LoadOrCreateTransparentMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new InvalidOperationException("シェーダーが見つからない: Sprites/Default");
            }
            SandboxWireSceneBuilder.EnsureFolder(MaterialFolder);
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetColors(Object target, string field, Color[] colors)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException($"{target.GetType().Name} に {field} がない");
            }
            property.arraySize = colors.Length;
            for (int i = 0; i < colors.Length; i++)
            {
                property.GetArrayElementAtIndex(i).colorValue = colors[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
