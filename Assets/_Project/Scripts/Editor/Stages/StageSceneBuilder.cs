using System;
using System.Collections.Generic;
using System.IO;
using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ステージの配置（StageLayout）から、ステージのシーンを作る（docs/15 6.11）。中身はアスレチックステージ（FlowSceneBuilder）と同じ:
    /// 地形、ギミック（StageZoneAuthoring と見た目）、撃破の範囲、コインの出現地点、拠点の候補、出現位置、光と空。カメラは置かない
    /// （試合の仕組み・メニューのシーンにある）。材質はステージごとのフォルダに、ビルドのたびにここの色で作り直す。
    /// </summary>
    internal static class StageSceneBuilder
    {
        public const string SceneFolder = "Assets/_Project/Scenes/Stages";
        private const string MaterialRoot = "Assets/_Project/Materials/Stages";

        /// <summary>新しく加えたステージ（ステージ選択に並ぶ順。アスレチックの後ろ）。</summary>
        public static readonly StageLayout[] Layouts =
        {
            CityStages.Day(),
            CityStages.Night(),
            VolcanoStage.Create(),
            SnowSeaStage.Create(),
            SpaceStationStage.Create(),
        };

        public static string ScenePathOf(StageLayout layout)
        {
            return $"{SceneFolder}/{layout.SceneName}.unity";
        }

        /// <summary>すべてのステージのシーンを作り直して保存する。</summary>
        public static void BuildAll()
        {
            foreach (StageLayout layout in Layouts)
            {
                Build(layout);
            }
        }

        public static void Build(StageLayout layout)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Object.DestroyImmediate(Object.FindFirstObjectByType<Camera>().gameObject);
            var decor = new StageDecor($"{MaterialRoot}/{layout.Id}", Seed(layout.Id));
            StageTheme theme = layout.Theme;
            ApplyLighting(theme, decor);

            SandboxWireSceneBuilder.CreateTerrain(layout.Terrain, layout.Anchors, decor.Lit("Terrain", theme.Terrain),
                decor.Lit("TerrainAttachable", theme.Attachable), decor.Lit("Anchor", theme.Anchor));
            CreateZones(layout.Zones, theme, decor);
            SandboxCombatSceneBuilder.CreateBounds(layout.BoundsMin, layout.BoundsMax);
            SandboxWireSceneBuilder.CreateCoinSpawns(layout.Coins, layout.Jackpots);
            SandboxWireSceneBuilder.CreateBankSpots(layout.Banks);
            var spawns = new GameObject("SpawnPoints").transform;
            for (int p = 0; p < layout.Spawns.Length; p++)
            {
                var point = new GameObject($"Spawn{p + 1}");
                point.transform.SetParent(spawns, false);
                point.transform.position = layout.Spawns[p];
                point.AddComponent<SpawnPointAuthoring>().Configure(p);
            }
            layout.Decorate?.Invoke(decor);

            string path = ScenePathOf(layout);
            SandboxWireSceneBuilder.EnsureFolder(SceneFolder);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException($"シーンを保存できなかった: {path}");
            }
            SandboxWireSceneBuilder.AddToBuildSettings(path);
            Debug.Log($"StageSceneBuilder: {path} を作った");
        }

        // 名前から決まる種（string.GetHashCode は実行ごとに変わることがあるので使わない）。
        private static int Seed(string id)
        {
            int seed = 17;
            foreach (char c in id)
            {
                seed = seed * 31 + c;
            }
            return seed;
        }

        // 光、空（Skybox/Procedural）、環境光、霧。ステージのシーンを最初に読み込むので、このシーンの設定が試合とメニューの背景で使われる。
        private static void ApplyLighting(StageTheme theme, StageDecor decor)
        {
            Light light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.color = theme.Light;
                light.intensity = theme.LightIntensity;
                light.transform.rotation = Quaternion.Euler(theme.LightEuler);
                light.shadows = LightShadows.Soft;
            }
            Material sky = decor.Sky(theme);
            RenderSettings.skybox = sky;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = theme.AmbientSky;
            RenderSettings.ambientEquatorColor = theme.AmbientEquator;
            RenderSettings.ambientGroundColor = theme.AmbientGround;
            RenderSettings.fog = theme.Fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = theme.FogColor;
            RenderSettings.fogStartDistance = theme.FogStart;
            RenderSettings.fogEndDistance = theme.FogEnd;
        }

        private static void CreateZones(ZoneSpec[] zones, StageTheme theme, StageDecor decor)
        {
            var lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject("Gimmicks").transform;
            foreach (ZoneSpec zone in zones)
            {
                var go = new GameObject(zone.Name);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(zone.Center.x, zone.Center.y, 0f);
                var authoring = go.AddComponent<StageZoneAuthoring>();
                authoring.Configure(zone.Kind, zone.Size, zone.Direction, zone.Power, zone.Limit);
                authoring.ConfigureTiming(zone.PeriodSeconds, zone.ActiveSeconds, zone.PhaseSeconds, zone.JitterSeconds, zone.WarningSeconds);
                authoring.ConfigureTarget(zone.Target);
                authoring.ConfigureShape(zone.Width, zone.Travel, zone.TravelSeconds);
                Color color = theme.ZoneColors.TryGetValue(zone.Kind, out Color themed) ? themed : DefaultColor(zone.Kind);
                var view = go.AddComponent<StageZoneView>();
                view.Configure(lineMaterial, color);
                view.ConfigureWarningSound(zone.WarningSound);
                Transform propParent = go.transform;
                if (zone.TravelSeconds > 0f)
                {
                    // 動くギミック: 見た目をこの子に入れて、StageZoneView が動かす（ギミックの物体は動かさない）。
                    propParent = new GameObject("Moving").transform;
                    propParent.SetParent(go.transform, false);
                    view.ConfigureMoving(propParent);
                }
                GameObject active = CreateProp(propParent, go.transform, zone, color, decor);
                bool periodic = zone.PeriodSeconds > 0f;
                if (zone.Kind == StageZoneKind.Gust || zone.Kind == StageZoneKind.Hazard || zone.Kind == StageZoneKind.Blizzard
                    || (zone.Kind == StageZoneKind.Launcher && periodic))
                {
                    // 効き始めるまでは出さない（StageZoneView が効いている間だけ出す）。
                    view.ConfigureActiveVisual(active);
                    active.SetActive(!periodic);
                }
            }
        }

        // ギミックの台・柱などの見た目（当たりはない）。風の柱・間欠泉の噴き出し・危険の柱・吹雪の幕は、効いている間だけ出す（StageZoneView）。
        // 予告の表示（黄色の斜線と警告の記号）は StageZoneView が実行中に作る。parent は動くギミックの見た目の親、root はギミックの物体。
        private static GameObject CreateProp(Transform parent, Transform root, ZoneSpec zone, Color color, StageDecor decor)
        {
            Vector2 size = zone.Size;
            string kind = zone.Kind.ToString();
            switch (zone.Kind)
            {
                case StageZoneKind.JumpPad:
                    return decor.Box("Pad", parent, new Vector3(0f, -size.y * 0.5f + 0.1f, 0f), new Vector3(size.x, 0.2f, 1.6f), decor.Lit(kind, color * 0.9f));
                case StageZoneKind.SpeedLane:
                    return decor.Box("Lane", parent, new Vector3(0f, -size.y * 0.5f + 0.05f, 0f), new Vector3(size.x, 0.1f, 2.4f),
                        decor.Lit(kind, Color.Lerp(color, Color.black, 0.75f)));
                case StageZoneKind.Launcher:
                    if (zone.PeriodSeconds > 0f)
                    {
                        return CreateGeyser(parent, zone, color, decor);
                    }
                    return decor.Box("Vent", parent, new Vector3(0f, -size.y * 0.5f + 0.15f, 0f), new Vector3(size.x, 0.3f, 1.8f),
                        decor.Lit(kind, Color.Lerp(color, Color.black, 0.4f)));
                case StageZoneKind.Bumper:
                    // 正方形の範囲は球（噴石など）、長方形は板（看板など）。
                    var bodySize = new Vector3(size.x * 0.9f, size.y * 0.9f, Mathf.Min(size.x, size.y) * 0.9f);
                    return Mathf.Approximately(size.x, size.y)
                        ? decor.Sphere("Body", parent, Vector3.zero, bodySize, decor.Unlit(kind, color * 0.85f))
                        : decor.Box("Body", parent, Vector3.zero, new Vector3(bodySize.x, bodySize.y, 1.2f), decor.Unlit(kind, color * 0.85f));
                case StageZoneKind.Ice:
                    return decor.Box("Sheet", parent, new Vector3(0f, -size.y * 0.5f + 0.03f, 0f), new Vector3(size.x, 0.06f, 4.1f), decor.Lit(kind, color));
                case StageZoneKind.Updraft:
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, 1f), size, decor.Transparent(kind, WithAlpha(color, 0.12f)));
                case StageZoneKind.Gust:
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, 1f), size, decor.Transparent(kind, WithAlpha(color, 0.14f)));
                case StageZoneKind.LowGravity:
                    // 見やすく（ユーザーの要望、2026-10-19）: 奥の板を濃くし、手前にも薄い色を重ねる。
                    decor.Quad("FieldFront", parent, new Vector3(0f, 0f, -1.5f), size, decor.Transparent(kind + "Front", WithAlpha(color, 0.06f)));
                    return decor.Quad("Field", parent, new Vector3(0f, 0f, 3f), size, decor.Transparent(kind, WithAlpha(color, 0.12f)));
                case StageZoneKind.Warp:
                    GameObject gate = decor.Sphere("Gate", parent, Vector3.zero, new Vector3(size.x, size.y, 0.3f), decor.Transparent(kind, WithAlpha(color, 0.35f)));
                    // 出口は動かない（動くゲートでも、ギミックの物体の子にする）。
                    Vector3 exit = new Vector3(zone.Target.x - zone.Center.x, zone.Target.y - zone.Center.y, 0f);
                    decor.Sphere("Exit", root, exit, new Vector3(1f, 1f, 0.2f), decor.Transparent(kind + "Exit", WithAlpha(color, 0.25f)));
                    return gate;
                case StageZoneKind.Hazard:
                {
                    // 幅 1 の柱の板（StageZoneView が周期ごとの柱の位置と幅に合わせる）。
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, -0.5f), new Vector2(1f, size.y), decor.Transparent(kind, WithAlpha(color, 0.78f)));
                }
                case StageZoneKind.Blizzard:
                {
                    // 範囲の全体の幕（手前。キャラクターの前にかかる）。
                    return decor.Quad("Storm", parent, new Vector3(0f, 0f, -2.5f), size, decor.Transparent(kind, WithAlpha(color, 0.22f)));
                }
                default:
                    return null;
            }
        }

        // 壁の間欠泉: 噴き出し口（範囲の、向きと反対側の辺の真ん中）と、向きへ斜めに伸びる噴き出し（噴いている間だけ出す）。
        private static GameObject CreateGeyser(Transform parent, ZoneSpec zone, Color color, StageDecor decor)
        {
            Vector2 direction = zone.Direction.normalized;
            Vector2 half = zone.Size * 0.5f;
            // 範囲の中心から、向きの逆へ範囲の端まで（噴き出し口）。
            float toEdge = Mathf.Min(Mathf.Abs(direction.x) > 1e-3f ? half.x / Mathf.Abs(direction.x) : float.MaxValue,
                Mathf.Abs(direction.y) > 1e-3f ? half.y / Mathf.Abs(direction.y) : float.MaxValue);
            Vector2 mouth = -direction * toEdge;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            GameObject vent = decor.Box("Mouth", parent, new Vector3(mouth.x, mouth.y, 0f), new Vector3(1.6f, 0.6f, 2.2f),
                decor.Lit("GeyserMouth", Color.Lerp(color, Color.black, 0.65f)));
            vent.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            float length = toEdge * 2f + 2f;
            float width = Mathf.Min(zone.Size.x, zone.Size.y) * 0.8f;
            Vector2 middle = mouth + direction * length * 0.5f;
            GameObject jet = decor.Quad("Jet", parent, new Vector3(middle.x, middle.y, -0.4f), new Vector2(width, length),
                decor.Transparent("GeyserJet", WithAlpha(Color.Lerp(color, Color.white, 0.55f), 0.55f)));
            jet.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return jet;
        }

        private static Color DefaultColor(StageZoneKind kind)
        {
            switch (kind)
            {
                case StageZoneKind.JumpPad: return new Color(0.4f, 1f, 0.5f);
                case StageZoneKind.Updraft: return new Color(0.75f, 0.92f, 1f);
                case StageZoneKind.BoostRing: return new Color(1f, 0.65f, 0.2f);
                case StageZoneKind.SpeedLane: return new Color(0.3f, 0.9f, 1f);
                case StageZoneKind.Launcher: return new Color(1f, 0.5f, 0.25f);
                case StageZoneKind.Bumper: return new Color(1f, 0.3f, 0.8f);
                case StageZoneKind.Gust: return new Color(0.88f, 0.95f, 1f);
                case StageZoneKind.Ice: return new Color(0.8f, 0.95f, 1f);
                case StageZoneKind.LowGravity: return new Color(0.7f, 0.5f, 1f);
                case StageZoneKind.Warp: return new Color(0.65f, 0.4f, 1f);
                case StageZoneKind.Hazard: return new Color(1f, 0.4f, 0.08f);
                case StageZoneKind.Blizzard: return new Color(0.9f, 0.96f, 1f);
                default: return Color.white;
            }
        }

        internal static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }

    /// <summary>
    /// 飾りと材質を作る道具（見た目だけ。当たりはなく、地形として読み込まれない）。材質はステージのフォルダに名前で作り、色はいつも上書きする。
    /// Random はステージごとに決まった種（背景のビルの並びなどが、作り直しても変わらない）。
    /// </summary>
    internal sealed class StageDecor
    {
        private readonly string _folder;
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        public readonly Transform Root;
        public readonly System.Random Random;

        public StageDecor(string folder, int seed)
        {
            _folder = folder;
            Random = new System.Random(seed);
            Root = new GameObject("Decor").transform;
        }

        public float Range(float min, float max)
        {
            return min + (float)Random.NextDouble() * (max - min);
        }

        public Material Lit(string name, Color color)
        {
            return Get(name, "Universal Render Pipeline/Lit", color);
        }

        /// <summary>光の影響を受けない色（ネオン、溶岩、星、窓の明かり）。</summary>
        public Material Unlit(string name, Color color)
        {
            return Get(name + "Glow", "Universal Render Pipeline/Unlit", color);
        }

        /// <summary>半透明の色（線と同じ Sprites/Default）。</summary>
        public Material Transparent(string name, Color color)
        {
            return Get(name + "Fill", "Sprites/Default", color);
        }

        public Material Sky(StageTheme theme)
        {
            Material sky = Get("Sky", "Skybox/Procedural", Color.white);
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", theme.SunSize);
            sky.SetFloat("_AtmosphereThickness", theme.AtmosphereThickness);
            sky.SetColor("_SkyTint", theme.SkyTint);
            sky.SetColor("_GroundColor", theme.Ground);
            sky.SetFloat("_Exposure", theme.Exposure);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        public GameObject Box(string name, Vector3 center, Vector3 size, Material material)
        {
            return Box(name, Root, center, size, material);
        }

        public GameObject Box(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material)
        {
            return Create(PrimitiveType.Cube, name, parent, localCenter, size, material);
        }

        public GameObject Sphere(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material)
        {
            return Create(PrimitiveType.Sphere, name, parent, localCenter, size, material);
        }

        public GameObject Sphere(string name, Vector3 center, Vector3 size, Material material)
        {
            return Create(PrimitiveType.Sphere, name, Root, center, size, material);
        }

        /// <summary>カメラ（-Z 側）に向いた板。</summary>
        public GameObject Quad(string name, Transform parent, Vector3 localCenter, Vector2 size, Material material)
        {
            GameObject go = Create(PrimitiveType.Quad, name, parent, localCenter, new Vector3(size.x, size.y, 1f), material);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        public GameObject Quad(string name, Vector3 center, Vector2 size, Material material)
        {
            return Quad(name, Root, center, size, material);
        }

        private static GameObject Create(PrimitiveType type, string name, Transform parent, Vector3 localCenter, Vector3 size, Material material)
        {
            GameObject go = SandboxWireSceneBuilder.CreateVisual(type, name, material, size);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            return go;
        }

        private Material Get(string name, string shaderName, Color color)
        {
            if (_materials.TryGetValue(name, out Material cached))
            {
                return cached;
            }
            string path = $"{_folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    throw new InvalidOperationException($"シェーダーが見つからない: {shaderName}");
                }
                SandboxWireSceneBuilder.EnsureFolder(_folder);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            // URP の材質は _BaseColor だけを決める（_Color は URP が合わせる。両方を書くと、作り直すたびに丸めの差が出る）。
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            EditorUtility.SetDirty(material);
            _materials.Add(name, material);
            return material;
        }
    }
}
