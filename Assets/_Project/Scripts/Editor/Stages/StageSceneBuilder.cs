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
            // ユーザーの案（2026-10-26）。
            LabStage.Create(),
            KitchenStage.Create(),
            ManorStage.Create(),
            StadiumStage.Create(),
            WharfStage.Create(),
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
            CreateBigAnchors(layout.BigAnchors, theme, decor);
            CreateZones(layout.Zones, theme, decor);
            CreatePlatforms(layout.Platforms, theme, decor);
            CreateTraps(layout.Traps, decor);
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

        // 動く足場と扉（StagePlatformAuthoring と、見た目を動かす StagePlatformView。見た目の子に当たりはない）。
        private static void CreatePlatforms(PlatformSpec[] platforms, StageTheme theme, StageDecor decor)
        {
            if (platforms == null || platforms.Length == 0)
            {
                return;
            }
            var root = new GameObject("Platforms").transform;
            foreach (PlatformSpec spec in platforms)
            {
                var go = new GameObject(spec.Name);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(spec.Center.x, spec.Center.y, 0f);
                var authoring = go.AddComponent<StagePlatformAuthoring>();
                if (spec.Kind == StagePlatformKind.Door)
                {
                    authoring.ConfigureDoor(spec.Size, spec.Attachable, spec.OpenHeight, spec.OpenSeconds, spec.CloseSeconds, spec.Group,
                        spec.OpenEverySeconds, spec.OpenForSeconds, spec.PhaseSeconds);
                }
                else if (spec.Kind == StagePlatformKind.Button)
                {
                    authoring.ConfigureButton(spec.Size, spec.Group);
                }
                else
                {
                    authoring.ConfigurePath(spec.Size, spec.Attachable, spec.Keys, spec.PeriodSeconds, spec.PhaseSeconds, spec.ActiveFromSeconds,
                        spec.ActiveToSeconds, spec.Smooth);
                }
                var visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);
                if (spec.Visual != null)
                {
                    spec.Visual(decor, visual, spec);
                }
                else
                {
                    Color color = spec.Color ?? (spec.Attachable ? theme.Attachable : theme.Terrain);
                    decor.Box("Body", visual, Vector3.zero, new Vector3(spec.Size.x, spec.Size.y, 4f), decor.Lit(spec.Name + "Body", color));
                }
                if (spec.Attachable)
                {
                    AddGlowRims(decor, visual, spec.Size);
                }
                var view = go.AddComponent<StagePlatformView>();
                view.Configure(visual);
                if (spec.Kind == StagePlatformKind.Button)
                {
                    // ボタン: 手前の面の丸いランプ（ワイヤーをつないでいる間は緑、ほかは赤）。
                    GameObject lamp = decor.Sphere("Lamp", visual, new Vector3(0f, 0f, -2.1f), new Vector3(spec.Size.x * 0.75f, spec.Size.y * 0.75f, 0.2f),
                        decor.Unlit("ButtonOff", new Color(1f, 0.2f, 0.15f)));
                    view.ConfigureLamp(lamp.GetComponent<Renderer>(), decor.Unlit("ButtonOff", new Color(1f, 0.2f, 0.15f)),
                        decor.Unlit("ButtonOn", new Color(0.3f, 1f, 0.45f)));
                }
            }
        }

        // 大きいアンカー（エイムアシストの半径が大きい。見た目も大きな菱形と、まわりの輪。ユーザーの要望、2026-10-26）。
        private static void CreateBigAnchors((Vector2 Position, float Radius)[] anchors, StageTheme theme, StageDecor decor)
        {
            if (anchors == null || anchors.Length == 0)
            {
                return;
            }
            var root = new GameObject("BigAnchorPoints").transform;
            Material body = decor.Lit("BigAnchor", theme.Anchor);
            Material ring = decor.Unlit("BigAnchorRing", Color.Lerp(theme.Anchor, Color.white, 0.4f));
            foreach ((Vector2 position, float radius) in anchors)
            {
                var go = new GameObject("BigAnchor");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(position.x, position.y, 0f);
                go.AddComponent<WireAnchorPoint>().Configure(radius);
                float size = Mathf.Clamp(radius * 0.7f, 0.9f, 2.2f);
                GameObject diamond = decor.Box("Diamond", go.transform, Vector3.zero, new Vector3(size, size, 0.4f), body);
                diamond.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                GameObject outer = decor.Cylinder("Ring", go.transform, new Vector3(0f, 0f, 0.3f), new Vector3(size * 2.1f, 0.03f, size * 2.1f), ring);
                outer.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                GameObject inner = decor.Cylinder("Hole", go.transform, new Vector3(0f, 0f, 0.25f), new Vector3(size * 1.8f, 0.04f, size * 1.8f),
                    decor.Lit("BigAnchorBack", Color.Lerp(theme.Anchor, Color.black, 0.6f)));
                inner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        // ステージの仕掛け（StageTrapAuthoring と見た目 StageTrapView）。赤外線: 半透明の赤い板、発射装置とランプ、ミサイル。
        // ピッチャーマシン: 壁の機械と、ボール（試合のときだけ出る）。
        private static void CreateTraps(TrapSpec[] traps, StageDecor decor)
        {
            if (traps == null || traps.Length == 0)
            {
                return;
            }
            var lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            var root = new GameObject("Traps").transform;
            foreach (TrapSpec spec in traps)
            {
                var go = new GameObject(spec.Name);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(spec.Center.x, spec.Center.y, 0f);
                var authoring = go.AddComponent<StageTrapAuthoring>();
                var view = go.AddComponent<StageTrapView>();
                if (spec.Kind == StageTrapKind.Tripwire)
                {
                    authoring.ConfigureTripwire(spec.BeamSize, spec.MuzzleOffset, spec.Direction, spec.Speed, spec.Range, spec.Power, spec.Knockback,
                        spec.ArmSeconds, spec.CooldownSeconds, spec.MissileSize);
                    GameObject beam = decor.Quad("Beam", go.transform, new Vector3(0f, 0f, -0.3f), spec.BeamSize,
                        decor.Transparent("Infrared", new Color(1f, 0.08f, 0.12f, 0.32f)));
                    // 発射装置（発射口の後ろの箱）とランプ。
                    Vector2 back = spec.MuzzleOffset - spec.Direction * 0.9f;
                    GameObject launcher = decor.Box("Launcher", go.transform, new Vector3(back.x, back.y, 0f), new Vector3(1.8f, 1.2f, 2.4f),
                        decor.Lit("MissileLauncher", new Color(0.3f, 0.32f, 0.36f)));
                    launcher.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(spec.Direction.y, spec.Direction.x) * Mathf.Rad2Deg);
                    GameObject lamp = decor.Sphere("Lamp", go.transform, new Vector3(back.x, back.y + 0.85f, -1.2f), new Vector3(0.4f, 0.4f, 0.2f),
                        decor.Unlit("LauncherLamp", new Color(1f, 0.15f, 0.1f)));
                    // ミサイル（右向きに作る。StageTrapView が向きに回す）。
                    var missile = new GameObject("Missile").transform;
                    missile.SetParent(go.transform, false);
                    decor.Box("Body", missile, Vector3.zero, new Vector3(spec.MissileSize.x, spec.MissileSize.y * 0.8f, 0.6f),
                        decor.Lit("MissileBody", new Color(0.85f, 0.87f, 0.9f)));
                    decor.Sphere("Nose", missile, new Vector3(spec.MissileSize.x * 0.5f, 0f, 0f), new Vector3(0.5f, spec.MissileSize.y * 0.8f, 0.6f),
                        decor.Lit("MissileNose", new Color(0.9f, 0.15f, 0.1f)));
                    decor.Box("Flame", missile, new Vector3(-spec.MissileSize.x * 0.65f, 0f, 0f), new Vector3(0.6f, spec.MissileSize.y * 0.5f, 0.3f),
                        decor.Unlit("MissileFlame", new Color(1f, 0.7f, 0.2f)));
                    view.Configure(beam.GetComponent<Renderer>(), missile, lamp.GetComponent<Renderer>(), lineMaterial);
                }
                else
                {
                    authoring.ConfigurePitcher(spec.LaunchVelocity, spec.PeriodSeconds, spec.PhaseSeconds, spec.Radius, spec.Power, spec.Knockback,
                        spec.MinHitSpeed, spec.BlinkSeconds);
                    // ピッチャーマシン（壁に付けた箱と、打ち出す筒）。
                    Material machine = decor.Lit("PitchingMachine", new Color(0.2f, 0.45f, 0.85f));
                    decor.Box("Machine", go.transform, new Vector3(-spec.Facing * 0.9f, -0.3f, 0f), new Vector3(1.8f, 1.6f, 1.8f), machine);
                    float angle = Mathf.Atan2(spec.LaunchVelocity.y, spec.LaunchVelocity.x) * Mathf.Rad2Deg;
                    GameObject barrel = decor.Cylinder("Barrel", go.transform, Vector3.zero, new Vector3(0.6f, 0.7f, 0.6f),
                        decor.Lit("PitchingBarrel", new Color(0.85f, 0.85f, 0.88f)));
                    barrel.transform.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
                    decor.Cylinder("Wheel", go.transform, new Vector3(-spec.Facing * 0.9f, -1.1f, -0.95f), new Vector3(0.9f, 0.08f, 0.9f),
                        decor.Lit("PitchingWheel", new Color(0.15f, 0.15f, 0.17f))).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var ball = new GameObject("Ball").transform;
                    ball.SetParent(go.transform, false);
                    decor.Sphere("Leather", ball, Vector3.zero, Vector3.one * spec.Radius * 2f, decor.Lit("Baseball", new Color(0.97f, 0.96f, 0.92f)));
                    decor.Box("Seam", ball, new Vector3(0f, 0f, -spec.Radius * 0.9f), new Vector3(spec.Radius * 1.6f, 0.05f, 0.05f),
                        decor.Unlit("BaseballSeam", new Color(0.85f, 0.1f, 0.1f)));
                    view.Configure(null, ball, null, lineMaterial);
                }
            }
        }

        // ワイヤーが刺さる動く足場の正面の縁を、オレンジに光らせる（動かない地形の AttachableGlow と同じ色。足場と一緒に動く）。
        internal static void AddGlowRims(StageDecor decor, Transform visual, Vector2 size)
        {
            const float w = 0.14f;
            Material rim = decor.Unlit("PlatformRim", SandboxWireSceneBuilder.AttachableGlowColor);
            float front = -2.03f;
            float rimW = Mathf.Min(w, size.x * 0.5f, size.y * 0.5f);
            decor.Box("RimTop", visual, new Vector3(0f, size.y * 0.5f - rimW * 0.5f, front), new Vector3(size.x, rimW, 0.02f), rim);
            decor.Box("RimBottom", visual, new Vector3(0f, -size.y * 0.5f + rimW * 0.5f, front), new Vector3(size.x, rimW, 0.02f), rim);
            decor.Box("RimLeft", visual, new Vector3(-size.x * 0.5f + rimW * 0.5f, 0f, front), new Vector3(rimW, size.y, 0.02f), rim);
            decor.Box("RimRight", visual, new Vector3(size.x * 0.5f - rimW * 0.5f, 0f, front), new Vector3(rimW, size.y, 0.02f), rim);
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
                Color color = zone.Color ?? (theme.ZoneColors.TryGetValue(zone.Kind, out Color themed) ? themed : DefaultColor(zone.Kind));
                var view = go.AddComponent<StageZoneView>();
                view.Configure(lineMaterial, color);
                view.ConfigureWarningSound(zone.WarningSound);
                view.ConfigureLook(zone.Look);
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
                case StageZoneKind.Water:
                {
                    // 水の手前の面（キャラクターと地形の前。水の中のキャラクターとコインが透けて見える。撃破の線より下まで）と、手前の水面。
                    float depth = size.y + 30f;
                    GameObject front = decor.Quad("Front", parent, new Vector3(0f, size.y * 0.5f - depth * 0.5f, -2.4f), new Vector2(size.x, depth),
                        decor.Transparent(kind, WithAlpha(color, 0.5f)));
                    GameObject surface = decor.Quad("Surface", parent, new Vector3(0f, size.y * 0.5f, -6f), new Vector2(size.x, 8f),
                        decor.Transparent(kind + "Surface", WithAlpha(Color.Lerp(color, Color.white, 0.25f), 0.4f)));
                    surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    return front;
                }
                case StageZoneKind.Blizzard:
                {
                    // 範囲の全体の幕（手前。キャラクターの前にかかる）。
                    return decor.Quad("Storm", parent, new Vector3(0f, 0f, -2.5f), size, decor.Transparent(kind, WithAlpha(color, 0.22f)));
                }
                case StageZoneKind.Grab:
                    return CreateDemonHand(parent, zone, color, decor);
                default:
                    return null;
            }
        }

        // 悪魔の手: 地面から生えた黒い手のひらと、長い爪の指 5 本（DemonHandView が指を曲げる）と、手首のまわりの紫のもや。
        private static GameObject CreateDemonHand(Transform parent, ZoneSpec zone, Color color, StageDecor decor)
        {
            Vector2 size = zone.Size;
            var hand = new GameObject("DemonHand").transform;
            hand.SetParent(parent, false);
            // 地面より少しだけ上（地形の上の面と飾りの面が重なってちらつかないように）。
            hand.localPosition = new Vector3(0f, -size.y * 0.5f + 0.02f, 0f);
            Material skin = decor.Lit("DemonSkin", Color.Lerp(color, Color.black, 0.6f));
            Material claw = decor.Unlit("DemonClaw", Color.Lerp(color, Color.white, 0.3f));
            decor.Box("Wrist", hand, new Vector3(0f, 0.3f, 0f), new Vector3(size.x * 0.45f, 0.6f, 1.2f), skin);
            decor.Box("Palm", hand, new Vector3(0f, 0.75f, 0f), new Vector3(size.x * 0.7f, 0.5f, 1.4f), skin);
            var fingers = new Transform[5];
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * size.x * 0.17f;
                var pivot = new GameObject($"Finger{i + 1}").transform;
                pivot.SetParent(hand, false);
                pivot.localPosition = new Vector3(x, 1f, 0f);
                float length = i == 0 || i == 4 ? size.y * 0.42f : size.y * 0.55f;
                decor.Box("Bone", pivot, new Vector3(0f, length * 0.5f, 0f), new Vector3(0.22f, length, 0.3f), skin);
                decor.Box("Claw", pivot, new Vector3(0f, length + 0.18f, -0.05f), new Vector3(0.12f, 0.4f, 0.16f), claw);
                fingers[i] = pivot;
            }
            decor.Quad("Mist", hand, new Vector3(0f, 0.2f, -0.6f), new Vector2(size.x * 1.3f, 0.9f),
                decor.Transparent("DemonMist", WithAlpha(new Color(0.55f, 0.15f, 0.8f), 0.35f)));
            hand.gameObject.AddComponent<DemonHandView>().Configure(fingers);
            return hand.gameObject;
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
                case StageZoneKind.Water: return new Color(0.12f, 0.42f, 0.75f);
                case StageZoneKind.Grab: return new Color(0.55f, 0.1f, 0.25f);
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

        /// <summary>円柱（Unity の円柱は高さ 2。size.y はその半分の倍率）。</summary>
        public GameObject Cylinder(string name, Transform parent, Vector3 localCenter, Vector3 size, Material material)
        {
            return Create(PrimitiveType.Cylinder, name, parent, localCenter, size, material);
        }

        public GameObject Cylinder(string name, Vector3 center, Vector3 size, Material material)
        {
            return Create(PrimitiveType.Cylinder, name, Root, center, size, material);
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
