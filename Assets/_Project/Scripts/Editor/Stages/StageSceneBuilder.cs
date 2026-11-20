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
            if (layout.Look != null)
            {
                // 背景とオブジェクトのクオリティアップ（D-97）。
                StageDressing.Apply(layout.Look, layout.BoundsMin, layout.BoundsMax);
            }

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
                    authoring.ConfigureAnchor(spec.AnchorPoint, spec.AnchorRadius);
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
                    // 撃った後の待ちの円形のゲージ（発射装置の周り。ユーザーの要望、2026-10-26）。
                    RingGaugeView reload = SandboxWireSceneBuilder.CreateRingGauge("ReloadGauge", go.transform, "RELOAD", 1.5f,
                        new Color(1f, 0.3f, 0.25f, 0.85f), sceneCamera: false);
                    view.Configure(beam.GetComponent<Renderer>(), missile, lamp.GetComponent<Renderer>(), lineMaterial, reload, launcher.transform);
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
                authoring.ConfigureShape(zone.Width, zone.Travel, zone.TravelSeconds, zone.DwellSeconds, zone.CapHeight, zone.FallLength);
                Color color = zone.Color ?? (theme.ZoneColors.TryGetValue(zone.Kind, out Color themed) ? themed : DefaultColor(zone.Kind));
                var view = go.AddComponent<StageZoneView>();
                view.Configure(lineMaterial, color);
                view.ConfigureWarningSound(zone.WarningSound);
                view.ConfigureLook(zone.Look);
                if (zone.Kind == StageZoneKind.Water)
                {
                    // 水はシェーダーで描く（水面の線は出さない）。
                    view.ConfigureWater();
                }
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
                        return CreateGeyser(parent, root, zone, color, decor);
                    }
                    if (zone.Look == StageZoneView.ZoneLook.Steam)
                    {
                        return CreateFumarole(parent, zone, decor);
                    }
                    return decor.Box("Vent", parent, new Vector3(0f, -size.y * 0.5f + 0.15f, 0f), new Vector3(size.x, 0.3f, 1.8f),
                        decor.Lit(kind, Color.Lerp(color, Color.black, 0.4f)));
                case StageZoneKind.Bumper when zone.Look == StageZoneView.ZoneLook.Plasma:
                    return CreatePlasmaGlobe(parent, zone, decor);
                case StageZoneKind.Bumper when zone.Look == StageZoneView.ZoneLook.MagmaRock:
                    return CreateVolcanicBomb(parent, zone, decor);
                case StageZoneKind.Bumper:
                    // 正方形の範囲は球（噴石など）、長方形は板（看板など）。
                    var bodySize = new Vector3(size.x * 0.9f, size.y * 0.9f, Mathf.Min(size.x, size.y) * 0.9f);
                    return Mathf.Approximately(size.x, size.y)
                        ? decor.Sphere("Body", parent, Vector3.zero, bodySize, decor.Unlit(kind, color * 0.85f))
                        : decor.Box("Body", parent, Vector3.zero, new Vector3(bodySize.x, bodySize.y, 1.2f), decor.Unlit(kind, color * 0.85f));
                case StageZoneKind.Ice:
                    return decor.Box("Sheet", parent, new Vector3(0f, -size.y * 0.5f + 0.03f, 0f), new Vector3(size.x, 0.06f, 4.1f), decor.Lit(kind, color));
                case StageZoneKind.Updraft when zone.Look == StageZoneView.ZoneLook.DustDevil:
                    return CreateDustDevil(parent, zone, decor);
                case StageZoneKind.Updraft:
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, 1f), size, decor.Transparent(kind, WithAlpha(color, 0.12f)));
                case StageZoneKind.Gust when zone.Look == StageZoneView.ZoneLook.Steam:
                    return CreateKettleSteam(parent, root, zone, decor);
                case StageZoneKind.Gust when IsSteadyDraft(zone):
                    // いつも吹いている縦の風は、上昇気流と同じ見た目（StageZoneView も同じ筋を流す。ユーザーの要望、2026-10-29）。
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, 1f), size, decor.Transparent(kind + "Draft", WithAlpha(color, 0.12f)));
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
                    if (zone.Look == StageZoneView.ZoneLook.Flame)
                    {
                        return CreateFlames(parent, root, zone, decor);
                    }
                    if (zone.Look == StageZoneView.ZoneLook.Lava)
                    {
                        return CreateLavaFountain(parent, root, zone, decor);
                    }
                    if (zone.Look == StageZoneView.ZoneLook.Lightning)
                    {
                        return CreateLightning(parent, root, zone, decor);
                    }
                    if (zone.Look == StageZoneView.ZoneLook.Icicle)
                    {
                        return CreateIcicles(parent, root, zone, decor);
                    }
                    // 幅 1 の柱の板（StageZoneView が周期ごとの柱の位置と幅に合わせる）。
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, -0.5f), new Vector2(1f, size.y), decor.Transparent(kind, WithAlpha(color, 0.78f)));
                }
                case StageZoneKind.Water:
                {
                    // 水の手前の面（キャラクターと地形の前。水の中のキャラクターとコインが透けて見える。撃破の線より下まで）と、手前の水面。
                    // どちらも水のシェーダー（揺れる水面と泡・深さの色・光の網と筋・空を映す水面。2026-11-18 の要望）。
                    float depth = size.y + 30f;
                    float surfaceY = zone.Center.y + size.y * 0.5f;
                    Color shallow = Color.Lerp(color, new Color(0.2f, 0.68f, 0.75f), 0.5f);
                    Color deep = new Color(0.02f, 0.1f, 0.24f);
                    // 水面の線の揺れの分だけ上まで（シェーダーが水面より上を描かない）。
                    GameObject front = decor.Quad("Front", parent, new Vector3(0f, size.y * 0.5f - depth * 0.5f + 0.3f, -2.4f), new Vector2(size.x, depth + 0.6f),
                        decor.Water($"Water{zone.Name}", shallow, deep, true, surfaceY, false));
                    GameObject surface = decor.Quad("Surface", parent, new Vector3(0f, size.y * 0.5f, -6f), new Vector2(size.x, 8f),
                        decor.Water($"Water{zone.Name}Surface", shallow, deep, false, surfaceY, false));
                    surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    return front;
                }
                case StageZoneKind.Blizzard:
                    return CreateBlizzard(parent, root, zone, decor);
                case StageZoneKind.Grab:
                    return CreateDemonHand(parent, zone, color, decor);
                default:
                    return null;
            }
        }

        // 悪魔の手（2026-11-20 の要望で精巧に）: 地面の紫に光る裂け目から生えた、骨ばった黒い手。前腕・手のひら・拳の節・
        // 3 つの節で曲がる指 4 本と親指・長く鋭い爪・紫に光る血管と、根元から昇る瘴気の煙（CreateDemonMiasma）。指の付け根と節は DemonHandView が曲げる
        // （這う間は交互に曲げ伸ばし、握るときは内へ深く曲げる）。大きさは範囲（幅 2.6m・高さ 2.4m）に合わせる。
        private static GameObject CreateDemonHand(Transform parent, ZoneSpec zone, Color color, StageDecor decor)
        {
            Vector2 size = zone.Size;
            const string meshes = "Gimmicks";
            var hand = new GameObject("DemonHand").transform;
            hand.SetParent(parent, false);
            // 地面より少しだけ上（地形の上の面と飾りの面が重なってちらつかないように）。
            hand.localPosition = new Vector3(0f, -size.y * 0.5f + 0.02f, 0f);
            Material skin = decor.Lit("DemonSkin", new Color(0.13f, 0.08f, 0.13f));
            skin.SetFloat("_Smoothness", 0.62f);
            Material knuckle = decor.Lit("DemonKnuckle", new Color(0.2f, 0.13f, 0.19f));
            knuckle.SetFloat("_Smoothness", 0.7f);
            Material claw = decor.Lit("DemonClaw", new Color(0.86f, 0.8f, 0.7f));
            claw.SetFloat("_Smoothness", 0.85f);
            Material vein = decor.Unlit("DemonVein", Color.Lerp(color, new Color(0.75f, 0.25f, 1f), 0.7f));
            Material rift = decor.Unlit("DemonRift", new Color(0.65f, 0.2f, 1f));
            Material pit = decor.Lit("DemonPit", new Color(0.02f, 0.01f, 0.03f));
            float scale = size.y / 2.4f;

            // 地面の裂け目（黒い穴と、紫に光る縁）。
            decor.Cylinder("RiftGlow", hand, new Vector3(0f, 0.012f, 0f), new Vector3(1.9f, 0.01f, 1.25f) * scale, rift);
            decor.Cylinder("RiftPit", hand, new Vector3(0f, 0.02f, 0f), new Vector3(1.6f, 0.012f, 1.0f) * scale, pit);
            // 前腕（裂け目から斜めに）と手のひら。
            Mesh forearm = ProceduralMesh.Tube(meshes, "DemonForearm",
                new[] { new Vector3(0f, -0.2f, 0.05f), new Vector3(0f, 0.25f, 0.02f), new Vector3(0f, 0.62f, 0f) }, new[] { 0.36f, 0.31f, 0.3f }, 12);
            ProceduralMesh.Place("Forearm", hand, forearm, Vector3.zero, Quaternion.identity, Vector3.one * scale, skin);
            decor.Sphere("Palm", hand, new Vector3(0f, 0.82f, 0f) * scale, new Vector3(1.05f, 0.62f, 0.62f) * scale, skin);
            decor.Sphere("Wrist", hand, new Vector3(0f, 0.6f, 0f) * scale, new Vector3(0.7f, 0.38f, 0.58f) * scale, skin);
            // 光る血管（手の甲から前腕へ）。
            Mesh veinA = ProceduralMesh.Tube(meshes, "DemonVeinA",
                new[] { new Vector3(-0.28f, 1.0f, -0.28f), new Vector3(-0.12f, 0.8f, -0.31f), new Vector3(-0.05f, 0.45f, -0.31f), new Vector3(0.06f, 0.05f, -0.34f) },
                new[] { 0.025f, 0.03f, 0.03f, 0.022f }, 6);
            Mesh veinB = ProceduralMesh.Tube(meshes, "DemonVeinB",
                new[] { new Vector3(0.3f, 1.0f, -0.27f), new Vector3(0.14f, 0.78f, -0.31f), new Vector3(0.1f, 0.4f, -0.31f) },
                new[] { 0.022f, 0.028f, 0.02f }, 6);
            ProceduralMesh.Place("VeinA", hand, veinA, Vector3.zero, Quaternion.identity, Vector3.one * scale, vein);
            ProceduralMesh.Place("VeinB", hand, veinB, Vector3.zero, Quaternion.identity, Vector3.one * scale, vein);

            // 指: 付け根（Finger）→ 節（Joint）→ 節（Joint）。どの節も短い管と、節の丸み。先に爪。
            Mesh segment = ProceduralMesh.Tube(meshes, "DemonFingerSegment", new[] { Vector3.zero, new Vector3(0f, 1f, 0f) }, new[] { 0.1f, 0.085f }, 8);
            Mesh clawMesh = ProceduralMesh.Lathe(meshes, "DemonClaw",
                new[] { new Vector2(0.085f, 0f), new Vector2(0.075f, 0.12f), new Vector2(0.045f, 0.32f), new Vector2(0f, 0.55f) }, 10);
            var fingers = new Transform[5];
            var joints = new List<Transform>();
            float[] lengths = { 0.34f, 0.46f, 0.5f, 0.46f, 0.38f };
            for (int i = 0; i < 5; i++)
            {
                bool thumb = i == 0;
                float x = thumb ? -0.5f : -0.3f + (i - 1) * 0.2f;
                var root = new GameObject($"Finger{i + 1}").transform;
                root.SetParent(hand, false);
                root.localPosition = new Vector3(x, thumb ? 0.8f : 1.06f, -0.04f) * scale;
                // 親指は外へ開き、ほかは少し扇に広げる。
                root.localRotation = Quaternion.Euler(0f, 0f, thumb ? 55f : (1.5f - i) * 6f);
                fingers[i] = root;
                Transform at = root;
                float length = lengths[i] * scale;
                for (int k = 0; k < 3; k++)
                {
                    float part = length * (k == 0 ? 1f : k == 1 ? 0.78f : 0.6f);
                    float thickness = scale * (1f - k * 0.12f);
                    ProceduralMesh.Place("Segment", at, segment, Vector3.zero, Quaternion.identity, new Vector3(thickness, part, thickness), skin);
                    decor.Sphere("Knuckle", at, Vector3.zero, Vector3.one * 0.23f * thickness, knuckle);
                    var next = new GameObject("Joint").transform;
                    next.SetParent(at, false);
                    next.localPosition = new Vector3(0f, part, 0f);
                    joints.Add(next);
                    at = next;
                }
                // 爪（先が少し手前へ曲がる）。
                ProceduralMesh.Place("Claw", at, clawMesh, Vector3.zero, Quaternion.Euler(-18f, 0f, 0f), Vector3.one * scale, claw);
            }
            CreateDemonMiasma(hand, size, decor);
            hand.gameObject.AddComponent<DemonHandView>().Configure(fingers, joints.ToArray());
            return hand.gameObject;
        }

        // 落雷（ZoneLook.Lightning。2026-11-20 の方針: 実際の現象に寄せる。NOAA・NWS の雷の解説と、ゲームの落雷の演出の分解を手本にする）:
        // 稲妻・お迎え放電・地面の閃光の光・焼けた跡・火花・煙は LightningStrikeView（表示だけ）。判定は危険の柱のまま（本線は柱の幅の中）。
        private static GameObject CreateLightning(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            float ground = zone.Center.y - size.y * 0.5f;
            var visual = new GameObject("Lightning");
            visual.transform.SetParent(parent, false);
            var fx = new GameObject("LightningFx").transform;
            fx.SetParent(root, false);
            Material line = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("LightningLine", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 3.2f);
                m.SetFloat("_Core", 1.6f);
            }, decor.Folder);
            var flash = new GameObject("Flash").AddComponent<Light>();
            flash.transform.SetParent(fx, false);
            flash.transform.localPosition = new Vector3(0f, -size.y * 0.5f + 1.5f, -1f);
            flash.type = LightType.Point;
            flash.color = new Color(0.75f, 0.8f, 1f);
            flash.range = 18f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            // 赤く焼けた跡（地面に寝かせた、縁のぼけた光の板）。
            Material scorchMaterial = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("LightningScorch", "Airerage/SoftParticle", m =>
            {
                m.SetFloat("_Softness", 0.9f);
                m.SetFloat("_Noise", 0.5f);
                m.SetFloat("_NoiseScale", 4f);
                m.SetFloat("_Intensity", 1.5f);
                m.SetColor("_Tint", new Color(1f, 0.42f, 0.14f, 0f));
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            }, decor.Folder);
            GameObject scorch = decor.Quad("Scorch", fx, new Vector3(0f, -size.y * 0.5f + 0.05f, 0.2f), new Vector2(2.6f, 1.3f), scorchMaterial);
            scorch.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            // 火花: 青白く光って、飛びながら橙へ冷える。
            ParticleSystem sparks = new ParticleFx
            {
                Name = "Sparks", LifetimeMin = 0.35f, LifetimeMax = 0.9f, SpeedMin = 4f, SpeedMax = 11f, SizeMin = 0.04f, SizeMax = 0.1f, Gravity = 1.2f,
                ConeAngle = 60f, ConeRadius = 0.2f, Additive = true, Stretch = 0.02f, Intensity = 3f, MaxParticles = 200,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.85f, 0.9f, 1f), 0f), (new Color(1f, 0.75f, 0.35f), 0.4f), (new Color(0.8f, 0.25f, 0.05f), 1f) },
                    new[] { (1f, 0f), (0.9f, 0.6f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, -size.y * 0.5f + 0.1f, 0f), up, decor.Folder);
            // 煙: 焼けた所から灰色の煙が昇って薄れる。
            ParticleSystem smoke = new ParticleFx
            {
                Name = "Smoke", LifetimeMin = 1.5f, LifetimeMax = 2.6f, SpeedMin = 0.8f, SpeedMax = 1.6f, SizeMin = 0.6f, SizeMax = 1.2f, GrowFrom = 0.7f,
                GrowTo = 2.6f, ConeAngle = 25f, ConeRadius = 0.4f, EdgeNoise = 0.6f, Softness = 0.85f, Turbulence = 0.6f, Spin = 30f, MaxParticles = 60,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.38f, 0.35f, 0.42f), 0f), (new Color(0.22f, 0.21f, 0.25f), 1f) },
                    new[] { (0f, 0f), (0.5f, 0.15f), (0.3f, 0.6f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, -size.y * 0.5f + 0.3f, 0.2f), up, decor.Folder);
            // 空の光（落ちる所の奥の大きな縁のぼけた板。雲の中の放電で空が一瞬明るくなる）。
            Material skyMaterial = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("LightningSky", "Airerage/SoftParticle", m =>
            {
                m.SetFloat("_Softness", 1f);
                m.SetFloat("_Noise", 0.3f);
                m.SetFloat("_NoiseScale", 2f);
                m.SetFloat("_Intensity", 1f);
                m.SetColor("_Tint", new Color(0.55f, 0.5f, 0.85f, 0f));
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            }, decor.Folder);
            GameObject sky = decor.Quad("SkyFlash", fx, new Vector3(0f, size.y * 0.35f, 30f), new Vector2(70f, 46f), skyMaterial);
            fx.gameObject.AddComponent<LightningStrikeView>().Configure(root.GetComponent<StageZoneView>(), line, zone.Center.y + size.y * 0.5f, ground, 0.3f,
                flash, scorch.GetComponent<Renderer>(), sparks, smoke, sky.GetComponent<Renderer>());
            return visual;
        }

        // 落ちてくるつらら（ZoneLook.Icicle。2026-11-20 の方針: 判定は落ちてくるつららの所だけ〈Core の StageZone.FallLength〉）。
        // 範囲の上の端（天井）に、波打つ回転体のつららを並べて下げる。予告の間は落ちるつららが揺れて氷のかけらとしずくが落ち、効いている
        // 間はそのつららが判定と同じ動きで落ち、床で砕けて氷のかけらと氷の粉が飛ぶ（IcicleFallView・HazardEffectRig）。
        private static GameObject CreateIcicles(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            float length = zone.FallLength > 0f ? zone.FallLength : 1.5f;
            var visual = new GameObject("IcicleColumn");
            visual.transform.SetParent(parent, false);
            Material ice = decor.Lit("Icicle", new Color(0.8f, 0.92f, 1f));
            ice.SetFloat("_Smoothness", 0.92f);
            // 根元が太く先が細い、表面が波打つつらら（根元が y 0、先が y 1。下へ向けて置く）。
            var profile = new Vector2[11];
            for (int i = 0; i < profile.Length; i++)
            {
                float u = i / (float)(profile.Length - 1);
                profile[i] = new Vector2(0.17f * Mathf.Pow(1f - u, 1.15f) * (1f + 0.12f * Mathf.Sin(u * Mathf.PI * 6f)), u);
            }
            Mesh mesh = ProceduralMesh.Lathe("Gimmicks", "Icicle", profile, 14);
            Transform Icicle(string name, Vector3 at, Vector3 scale)
            {
                var holder = new GameObject(name).transform;
                holder.SetParent(root, false);
                holder.localPosition = at;
                holder.localScale = scale;
                ProceduralMesh.Place("Mesh", holder, mesh, Vector3.zero, Quaternion.Euler(180f, decor.Range(0f, 360f), 0f), Vector3.one, ice)
                    .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                return holder;
            }
            var hanging = new List<Transform>();
            for (float x = -size.x * 0.5f + 0.5f; x < size.x * 0.5f - 0.4f; x += decor.Range(0.75f, 1.05f))
            {
                float thick = decor.Range(0.85f, 1.2f);
                hanging.Add(Icicle("Icicle", new Vector3(x, size.y * 0.5f, decor.Range(0.15f, 0.55f)), new Vector3(thick, length * decor.Range(0.8f, 1f), thick)));
            }
            Transform falling = Icicle("FallingIcicle", new Vector3(0f, size.y * 0.5f, 0.3f), new Vector3(1f, length, 1f));
            falling.gameObject.SetActive(false);

            var fx = new GameObject("IcicleFx").transform;
            fx.SetParent(root, false);
            Quaternion down = Quaternion.Euler(90f, 0f, 0f);
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            Gradient iceColors = ParticleFx.Ramp(new[] { (new Color(0.9f, 0.97f, 1f), 0f), (new Color(0.75f, 0.88f, 1f), 1f) },
                new[] { (0.95f, 0f), (0.85f, 0.7f), (0f, 1f) });
            // 予告: ひびが入って氷のかけらとしずくが落ちる。
            ParticleSystem crumbs = new ParticleFx
            {
                Name = "Crumbs", LifetimeMin = 0.6f, LifetimeMax = 1f, SpeedMin = 0.3f, SpeedMax = 1.2f, SizeMin = 0.03f, SizeMax = 0.07f, Gravity = 1f,
                ConeAngle = 20f, ConeRadius = 0.25f, Colors = iceColors, Softness = 0.25f, EdgeNoise = 0.3f, Spin = 300f, MaxParticles = 80,
            }.Build(fx, new Vector3(0f, size.y * 0.5f - 0.2f, 0.3f), down, decor.Folder);
            ParticleSystem drips = new ParticleFx
            {
                Name = "Drips", LifetimeMin = 0.5f, LifetimeMax = 0.8f, SpeedMin = 0.2f, SpeedMax = 0.6f, SizeMin = 0.025f, SizeMax = 0.04f, Gravity = 1f,
                ConeAngle = 3f, ConeRadius = 0.05f, Stretch = 0.03f, Softness = 0.4f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.8f, 0.92f, 1f), 0f), (new Color(0.7f, 0.85f, 1f), 1f) }, new[] { (0.8f, 0f), (0.6f, 0.8f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, size.y * 0.5f - length * 0.9f, 0.3f), down, decor.Folder);
            // 床で砕ける: 氷のかけら（回りながら跳ねて落ちる）と、白い氷の粉。
            ParticleSystem shards = new ParticleFx
            {
                Name = "Shards", LifetimeMin = 0.6f, LifetimeMax = 1.1f, SpeedMin = 3f, SpeedMax = 7f, SizeMin = 0.06f, SizeMax = 0.2f, Gravity = 1.6f,
                ConeAngle = 70f, ConeRadius = 0.3f, Colors = iceColors, Softness = 0.2f, EdgeNoise = 0.45f, Spin = 420f, MaxParticles = 120,
            }.Build(fx, new Vector3(0f, -size.y * 0.5f + 0.1f, 0.2f), up, decor.Folder);
            ParticleSystem powder = new ParticleFx
            {
                Name = "Powder", LifetimeMin = 0.8f, LifetimeMax = 1.4f, SpeedMin = 0.5f, SpeedMax = 1.5f, SizeMin = 0.5f, SizeMax = 1f, GrowFrom = 0.6f,
                GrowTo = 2.4f, ConeAngle = 80f, ConeRadius = 0.4f, EdgeNoise = 0.55f, Softness = 0.9f, Turbulence = 0.4f, MaxParticles = 30,
                Colors = ParticleFx.Ramp(new[] { (Color.white, 0f), (new Color(0.88f, 0.94f, 1f), 1f) }, new[] { (0.6f, 0f), (0.35f, 0.4f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, -size.y * 0.5f + 0.2f, 0.2f), up, decor.Folder);
            var rig = fx.gameObject.AddComponent<HazardEffectRig>();
            rig.Configure(root.GetComponent<StageZoneView>(), fx, new[] { crumbs, drips }, new[] { 14f, 6f }, new[] { crumbs }, new[] { 25 },
                new ParticleSystem[0], new float[0]);
            rig.ConfigureEndBurst(new[] { shards, powder }, new[] { 45, 10 });
            fx.gameObject.AddComponent<IcicleFallView>().Configure(root.GetComponent<StageZoneView>(), hanging.ToArray(), falling);
            return visual;
        }

        // やかんの湯気（風 + ZoneLook.Steam。2026-11-20 の方針: 注ぎ口のすぐ外は見えず〈湯気はまだ気体〉、少し上で冷えて白い湯気になり、
        // ふくらみながら昇って薄れる）。判定は風の範囲（幅 2m）のまま。湯気の幅も上で範囲の幅くらい。
        private static GameObject CreateKettleSteam(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            var visual = new GameObject("SteamPlume");
            visual.transform.SetParent(parent, false);
            var fx = new GameObject("SteamFx").transform;
            fx.SetParent(root, false);
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            float speed = size.y / 1.6f;
            var spout = new Vector3(0f, -size.y * 0.5f, 0f);
            ParticleSystem steam = new ParticleFx
            {
                Name = "Steam", LifetimeMin = 1.4f, LifetimeMax = 2f, SpeedMin = speed * 0.7f, SpeedMax = speed, SizeMin = 0.25f, SizeMax = 0.45f,
                GrowFrom = 0.6f, GrowTo = 6.5f, ConeAngle = 5f, ConeRadius = 0.12f, EdgeNoise = 0.55f, Softness = 0.9f, Turbulence = 0.9f, Spin = 30f,
                MaxParticles = 300,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.96f, 0.96f, 0.97f), 0f), (new Color(0.9f, 0.9f, 0.92f), 1f) },
                    new[] { (0f, 0f), (0f, 0.06f), (0.5f, 0.2f), (0.3f, 0.6f), (0f, 1f) }),
            }.Build(fx, spout, up, decor.Folder);
            ParticleSystem wisps = new ParticleFx
            {
                Name = "Wisps", LifetimeMin = 1f, LifetimeMax = 1.6f, SpeedMin = 1f, SpeedMax = 2f, SizeMin = 0.15f, SizeMax = 0.3f, GrowFrom = 0.6f, GrowTo = 4f,
                ConeAngle = 10f, ConeRadius = 0.1f, EdgeNoise = 0.5f, Softness = 0.9f, Turbulence = 0.5f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (Color.white, 0f), (new Color(0.9f, 0.9f, 0.9f), 1f) }, new[] { (0f, 0f), (0.35f, 0.25f), (0f, 1f) }),
            }.Build(fx, spout, up, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), null,
                new[] { wisps }, new[] { 8f }, new[] { steam }, new[] { 15 }, new[] { steam }, new[] { 70f });
            return visual;
        }

        // つむじ風（上昇気流 + ZoneLook.DustDevil。2026-11-20 の方針: 実際のつむじ風は、地面から巻き上げた塵が回りながら昇る柱。根元が
        // 広く〈椀の形〉、上へ行くほど薄れて見えなくなる。紙くずや葉も一緒に回る）。いつも出し続け、動くつむじ風と一緒に動く。判定は
        // 上昇気流の範囲（幅 6m）のまま（塵の柱と根元の広がりはその幅に収まる）。
        private static GameObject CreateDustDevil(Transform parent, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            // 塵の色（ZoneSpec.Tinted があればその色。キッチンのミキサーの渦は粉と砂糖の白）。
            Color dust = zone.Color ?? new Color(0.72f, 0.6f, 0.46f);
            Color dark = Color.Lerp(dust, Color.black, 0.55f);
            Color light = Color.Lerp(dust, Color.white, 0.3f);
            var visual = new GameObject("DustDevil").transform;
            visual.SetParent(parent, false);
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            var ground = new Vector3(0f, -size.y * 0.5f + 0.2f, 0.3f);
            new ParticleFx
            {
                Name = "DustCore", Rate = 40f, LifetimeMin = 2.4f, LifetimeMax = 3.4f, SpeedMin = 5f, SpeedMax = 8f, SizeMin = 0.7f, SizeMax = 1.3f,
                GrowFrom = 0.8f, GrowTo = 2.4f, ConeAngle = 6f, ConeRadius = 0.9f, Orbit = 260f, Radial = -0.15f, EdgeNoise = 0.6f, Softness = 0.85f,
                Turbulence = 0.4f, Spin = 60f, MaxParticles = 220,
                Colors = ParticleFx.Ramp(new[] { (dust, 0f), (light, 1f) },
                    new[] { (0f, 0f), (0.55f, 0.08f), (0.32f, 0.5f), (0f, 1f) }),
            }.Build(visual, ground, up, decor.Folder);
            new ParticleFx
            {
                Name = "DustSkirt", Rate = 26f, LifetimeMin = 1f, LifetimeMax = 1.6f, SpeedMin = 0.8f, SpeedMax = 1.8f, SizeMin = 1f, SizeMax = 2f, GrowFrom = 1f,
                GrowTo = 2.2f, ConeAngle = 80f, ConeRadius = 1.6f, Orbit = 200f, Radial = 0.6f, EdgeNoise = 0.6f, Softness = 0.9f, Turbulence = 0.3f,
                MaxParticles = 80,
                Colors = ParticleFx.Ramp(new[] { (dust, 0f), (light, 1f) },
                    new[] { (0f, 0f), (0.45f, 0.15f), (0f, 1f) }),
            }.Build(visual, ground, up, decor.Folder);
            new ParticleFx
            {
                Name = "Debris", Rate = 10f, LifetimeMin = 2f, LifetimeMax = 3.2f, SpeedMin = 4f, SpeedMax = 8f, SizeMin = 0.06f, SizeMax = 0.16f,
                ConeAngle = 10f, ConeRadius = 1f, Orbit = 340f, Spin = 400f, Softness = 0.2f, EdgeNoise = 0.5f, MaxParticles = 60,
                Colors = ParticleFx.Ramp(new[] { (dark, 0f), (Color.Lerp(dark, dust, 0.4f), 1f) },
                    new[] { (1f, 0f), (1f, 0.8f), (0f, 1f) }),
            }.Build(visual, ground, up, decor.Folder);
            return visual.gameObject;
        }

        // 吹雪（2026-11-20 の方針: 実際の吹雪は、強い風で雪が横に流れ、地面と空の区別がつかないほど白くなる〈ホワイトアウト。NWS・
        // 英国気象庁〉）。吹いている間は、範囲の全体で風下へ流れる雪の筋（地形にぶつかると消える。足場の陰には雪が届かず、判定の
        // 「風上に地形があると凍らない」と見た目が合う）と、風下へ流れる白いもやの塊。吹き始める前は、ちらつく雪が舞い始める。
        private static GameObject CreateBlizzard(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            var visual = new GameObject("Storm");
            visual.transform.SetParent(parent, false);
            var fx = new GameObject("BlizzardFx").transform;
            fx.SetParent(root, false);
            Vector2 wind = zone.Direction.normalized;
            var box = new Vector3(size.x, size.y, 5f);
            ParticleSystem snow = new ParticleFx
            {
                Name = "Snow", LifetimeMin = 1f, LifetimeMax = 1.6f, SizeMin = 0.08f, SizeMax = 0.18f, BoxSize = box,
                WorldVelocity = new Vector3(wind.x, wind.y, 0f) * 17f, VelocityJitter = 0.25f, Stretch = 0.055f, Softness = 0.5f, Collide = true,
                MaxParticles = 4200,
                Colors = ParticleFx.Ramp(new[] { (Color.white, 0f), (new Color(0.9f, 0.95f, 1f), 1f) }, new[] { (0f, 0f), (0.85f, 0.1f), (0.7f, 0.8f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, 0f, 0.5f), Quaternion.identity, decor.Folder);
            ParticleSystem whiteout = new ParticleFx
            {
                Name = "Whiteout", LifetimeMin = 2.5f, LifetimeMax = 4f, SizeMin = 9f, SizeMax = 16f, GrowFrom = 0.8f, GrowTo = 1.4f, BoxSize = box,
                WorldVelocity = new Vector3(wind.x, wind.y, 0f) * 9f, EdgeNoise = 0.6f, Softness = 1f, Turbulence = 0.3f, MaxParticles = 400,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.94f, 0.96f, 1f), 0f), (new Color(0.88f, 0.92f, 0.98f), 1f) },
                    new[] { (0f, 0f), (0.34f, 0.2f), (0.28f, 0.7f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, 0f, -1f), Quaternion.identity, decor.Folder);
            ParticleSystem flurry = new ParticleFx
            {
                Name = "Flurry", LifetimeMin = 1.6f, LifetimeMax = 2.4f, SizeMin = 0.08f, SizeMax = 0.16f, BoxSize = box,
                WorldVelocity = new Vector3(wind.x, wind.y - 0.4f, 0f) * 6f, VelocityJitter = 0.4f, Softness = 0.6f, Turbulence = 0.6f, Collide = true,
                MaxParticles = 500,
                Colors = ParticleFx.Ramp(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (0.8f, 0.15f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, 0f, 0.5f), Quaternion.identity, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), null,
                new[] { flurry }, new[] { 160f }, new[] { whiteout }, new[] { 40 }, new[] { snow, whiteout }, new[] { 2400f, 85f });
            return visual;
        }

        // プラズマ球（跳ね返す物 + ZoneLook.Plasma。2026-11-20 の方針: 手本はプラズマボール。ガラスの球の真ん中の電極から、気体が光る
        // 細い筋がガラスへ伸び、熱でゆらゆら昇る〈PlasmaGlobeView〉。色は封入した気体の色〈ネオンの赤紫と、アルゴンなどの青紫〉）。
        // ガラス・ほのかに光る中の気体・電極・下の金属の台。判定は跳ね返す範囲（球はその範囲に収まる）。
        private static GameObject CreatePlasmaGlobe(Transform parent, ZoneSpec zone, StageDecor decor)
        {
            float radius = Mathf.Min(zone.Size.x, zone.Size.y) * 0.45f;
            var globe = new GameObject("PlasmaGlobe").transform;
            globe.SetParent(parent, false);
            decor.Sphere("Glass", globe, Vector3.zero, Vector3.one * radius * 2f, decor.Transparent("PlasmaGlass", new Color(0.75f, 0.8f, 1f, 0.16f)));
            Material gas = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("PlasmaGas", "Airerage/SoftParticle", m =>
            {
                m.SetFloat("_Softness", 1f);
                m.SetFloat("_Noise", 0.2f);
                m.SetFloat("_Intensity", 1f);
                m.SetColor("_Tint", new Color(0.45f, 0.2f, 0.7f, 0.6f));
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            }, decor.Folder);
            decor.Quad("Gas", globe, new Vector3(0f, 0f, 0.05f), Vector2.one * radius * 2.3f, gas);
            Material electrode = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("PlasmaElectrode", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(1f, 0.7f, 1f));
                m.SetFloat("_Intensity", 3f);
            }, decor.Folder);
            decor.Sphere("Electrode", globe, Vector3.zero, Vector3.one * radius * 0.32f, electrode);
            Material metal = decor.Lit("PlasmaBase", new Color(0.18f, 0.19f, 0.22f));
            metal.SetFloat("_Smoothness", 0.75f);
            metal.SetFloat("_Metallic", 0.8f);
            decor.Cylinder("Base", globe, new Vector3(0f, -radius * 1.02f, 0f), new Vector3(radius * 0.9f, radius * 0.12f, radius * 0.9f), metal);
            decor.Cylinder("Stem", globe, new Vector3(0f, -radius * 0.65f, 0f), new Vector3(radius * 0.12f, radius * 0.3f, radius * 0.12f), metal);
            Material line = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("PlasmaFilament", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 3f);
                m.SetFloat("_Core", 1.5f);
            }, decor.Folder);
            globe.gameObject.AddComponent<PlasmaGlobeView>().Configure(line, radius * 0.95f, 8);
            return globe.gameObject;
        }

        // 噴石（跳ね返す物 + ZoneLook.MagmaRock。2026-11-20 の方針: 手本は USGS のパン皮状火山弾。冷えた皮がひび割れ、割れ目の奥がまだ
        // 赤く光る〈Airerage/MagmaRock〉）。少しいびつな塊に、昇る火の粉と熱い煙。判定は跳ね返す範囲（塊はその範囲に収まる）。
        private static GameObject CreateVolcanicBomb(Transform parent, ZoneSpec zone, StageDecor decor)
        {
            float diameter = Mathf.Min(zone.Size.x, zone.Size.y) * 0.9f;
            var bomb = new GameObject("VolcanicBomb").transform;
            bomb.SetParent(parent, false);
            Material crust = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("MagmaRock", "Airerage/MagmaRock", m =>
            {
                m.SetFloat("_CellScale", 2.6f);
                m.SetFloat("_CrackWidth", 0.12f);
            }, decor.Folder);
            GameObject body = decor.Sphere("Body", bomb, Vector3.zero, new Vector3(diameter, diameter * 0.86f, diameter * 0.92f), crust);
            body.transform.localRotation = Quaternion.Euler(decor.Range(0f, 40f), decor.Range(0f, 360f), decor.Range(-20f, 20f));
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            new ParticleFx
            {
                Name = "Embers", Rate = 7f, LifetimeMin = 1f, LifetimeMax = 2f, SpeedMin = 0.6f, SpeedMax = 1.6f, SizeMin = 0.03f, SizeMax = 0.07f,
                ConeAngle = 40f, ConeRadius = diameter * 0.4f, Additive = true, Turbulence = 1f, Intensity = 3f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.7f, 0.25f), 0f), (new Color(0.8f, 0.2f, 0.03f), 1f) }, new[] { (1f, 0f), (0f, 1f) }),
            }.Build(bomb, new Vector3(0f, diameter * 0.3f, 0f), up, decor.Folder);
            new ParticleFx
            {
                Name = "HeatSmoke", Rate = 3f, LifetimeMin = 1.6f, LifetimeMax = 2.6f, SpeedMin = 0.5f, SpeedMax = 1f, SizeMin = 0.5f, SizeMax = 0.9f,
                GrowFrom = 0.7f, GrowTo = 2f, ConeAngle = 20f, ConeRadius = diameter * 0.3f, EdgeNoise = 0.6f, Softness = 0.9f, Turbulence = 0.5f,
                MaxParticles = 20,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.3f, 0.2f, 0.17f), 0f), (new Color(0.18f, 0.16f, 0.16f), 1f) },
                    new[] { (0f, 0f), (0.35f, 0.2f), (0f, 1f) }),
            }.Build(bomb, new Vector3(0f, diameter * 0.35f, 0.1f), up, decor.Folder);
            return bomb.gameObject;
        }

        // 噴気孔（いつも噴く発射台 + ZoneLook.Steam。2026-11-20 の方針: 手本は USGS の噴気孔。地面の穴から白い湯気〈ほとんど水蒸気〉が
        // いつも昇り、口のまわりの岩に硫黄の黄色い結晶が付く）。黒い口・まわりの岩・黄色い硫黄・いつも出る湯気。判定は発射台の範囲
        // （口のすぐ上の濃い湯気の所）。
        private static GameObject CreateFumarole(Transform parent, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            var vent = new GameObject("Fumarole").transform;
            vent.SetParent(parent, false);
            float floor = -size.y * 0.5f;
            Material rock = decor.Lit("FumaroleRock", new Color(0.22f, 0.2f, 0.19f));
            Material sulfur = decor.Lit("FumaroleSulfur", new Color(0.95f, 0.85f, 0.2f));
            Material hole = decor.Unlit("FumaroleHole", new Color(0.03f, 0.02f, 0.02f));
            decor.Cylinder("Mouth", vent, new Vector3(0f, floor + 0.04f, 0.3f), new Vector3(size.x * 0.45f, 0.02f, 0.9f), hole);
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + decor.Range(-0.3f, 0.3f);
                var at = new Vector3(Mathf.Cos(a) * size.x * 0.42f, floor + 0.1f, 0.3f + Mathf.Sin(a) * 0.7f);
                decor.Sphere("Rock", vent, at, new Vector3(decor.Range(0.4f, 0.7f), decor.Range(0.25f, 0.4f), decor.Range(0.4f, 0.6f)), rock);
                // 硫黄の結晶（岩の内側の縁に黄色く付く）。
                decor.Sphere("Sulfur", vent, Vector3.Lerp(at, new Vector3(0f, floor + 0.12f, 0.3f), 0.3f) + Vector3.up * 0.14f,
                    new Vector3(decor.Range(0.4f, 0.6f), 0.16f, decor.Range(0.35f, 0.5f)), sulfur);
            }
            Vector2 direction = zone.Direction.normalized;
            Quaternion along = Quaternion.LookRotation(new Vector3(direction.x, direction.y, 0f), Vector3.back);
            new ParticleFx
            {
                Name = "Steam", Rate = 34f, LifetimeMin = 1.6f, LifetimeMax = 2.4f, SpeedMin = 3.5f, SpeedMax = 6f, SizeMin = 0.3f, SizeMax = 0.55f,
                GrowFrom = 0.6f, GrowTo = 5f, ConeAngle = 8f, ConeRadius = size.x * 0.2f, EdgeNoise = 0.55f, Softness = 0.9f, Turbulence = 0.8f, Spin = 25f,
                MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.95f, 0.95f, 0.93f), 0f), (new Color(0.85f, 0.85f, 0.84f), 1f) },
                    new[] { (0f, 0f), (0.05f, 0.06f), (0.45f, 0.2f), (0.25f, 0.6f), (0f, 1f) }),
            }.Build(vent, new Vector3(0f, floor + 0.1f, 0.3f), along, decor.Folder);
            return vent.gameObject;
        }

        // 悪魔の手の根元の瘴気（2026-11-20 の指摘: 紫の四角い板 → 禍々しい煙に。手本は実際の煙〈渦を巻いてふくらみ、上へ昇りながら
        // 薄れる〉と、ゲームの闇の瘴気の演出〈黒に近い紫の煙・下から照らす紫の光・漂う火の粉〉）。地面の裂け目から、
        // ① 黒紫の濃い煙が渦を巻いて昇り（ふつうに重ねる）、② 裂け目の紫の光に下から照らされた薄い紫のもや（足し算）が揺れ、
        // ③ 紫の火の粉が漂って昇る。いつも出し続け、手が動くと煙は後ろへ尾を引く（粒はワールドの空間で動く）。当たりはない。
        private static void CreateDemonMiasma(Transform hand, Vector2 size, StageDecor decor)
        {
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            float radius = size.x * 0.35f;
            new ParticleFx
            {
                Name = "MiasmaSmoke", Rate = 16f, LifetimeMin = 1.8f, LifetimeMax = 3f, SpeedMin = 0.5f, SpeedMax = 1.3f, SizeMin = 0.7f, SizeMax = 1.3f,
                GrowFrom = 0.6f, GrowTo = 2.6f, ConeAngle = 22f, ConeRadius = radius, EdgeNoise = 0.65f, Softness = 0.85f, Turbulence = 0.7f, Spin = 40f,
                Gravity = -0.03f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.2f, 0.06f, 0.26f), 0f), (new Color(0.08f, 0.03f, 0.11f), 0.45f), (new Color(0.04f, 0.02f, 0.06f), 1f) },
                    new[] { (0f, 0f), (0.75f, 0.15f), (0.5f, 0.6f), (0f, 1f) }),
            }.Build(hand, new Vector3(0f, 0.15f, 0f), up, decor.Folder);
            new ParticleFx
            {
                Name = "MiasmaGlow", Rate = 7f, LifetimeMin = 1.2f, LifetimeMax = 2f, SpeedMin = 0.3f, SpeedMax = 0.8f, SizeMin = 0.9f, SizeMax = 1.6f,
                GrowFrom = 0.7f, GrowTo = 1.8f, ConeAngle = 15f, ConeRadius = radius * 0.8f, EdgeNoise = 0.5f, Softness = 0.95f, Turbulence = 0.4f,
                Additive = true, Intensity = 1.2f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.6f, 0.15f, 0.9f), 0f), (new Color(0.35f, 0.05f, 0.55f), 1f) },
                    new[] { (0f, 0f), (0.35f, 0.2f), (0.15f, 0.6f), (0f, 1f) }),
            }.Build(hand, new Vector3(0f, 0.05f, 0f), up, decor.Folder);
            new ParticleFx
            {
                Name = "MiasmaEmbers", Rate = 9f, LifetimeMin = 1.2f, LifetimeMax = 2.4f, SpeedMin = 0.6f, SpeedMax = 1.6f, SizeMin = 0.04f, SizeMax = 0.09f,
                ConeAngle = 30f, ConeRadius = radius, Turbulence = 1.2f, Additive = true, Intensity = 3f, MaxParticles = 60,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.85f, 0.45f, 1f), 0f), (new Color(0.5f, 0.1f, 0.85f), 1f) }, new[] { (1f, 0f), (0.8f, 0.6f), (0f, 1f) }),
            }.Build(hand, new Vector3(0f, 0.1f, 0f), up, decor.Folder);
        }

        // 流れる液体の筋の材質（Airerage/Stream）。
        internal static Material StreamMaterial(string name, Color liquid, Color edge, Color foam, float speed, Vector2 streakScale, float foamAmount,
            float emission, string folder)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/Stream", m =>
            {
                m.SetColor("_Color", liquid);
                m.SetColor("_EdgeColor", edge);
                m.SetColor("_FoamColor", foam);
                m.SetFloat("_Speed", speed);
                m.SetVector("_StreakScale", new Vector4(streakScale.x, streakScale.y, 0f, 0f));
                m.SetFloat("_Foam", foamAmount);
                m.SetFloat("_Emission", emission);
                // 名前から決まる模様のずれ（作り直しても同じ値）。
                int seed = 0;
                foreach (char c in name)
                {
                    seed = (seed * 31 + c) % 97;
                }
                m.SetFloat("_Seed", seed);
            }, folder);
        }


        // 炎の板の材質（舌の形か、放射の形か）。
        private static Material FlameMaterial(string name, float intensity, float seed, string folder, bool radial)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/Flame", m =>
            {
                m.SetFloat("_Intensity", intensity);
                m.SetFloat("_Seed", seed);
                m.SetFloat("_Radial", radial ? 1f : 0f);
                m.SetFloat("_Speed", radial ? 1.6f : 1.6f);
                // 芯は黄色まで（青を抑えて白く飛ばさない）、縁は黒ずんだ赤。
                m.SetColor("_CoreColor", new Color(2.2f, 1.45f, 0.3f));
                m.SetColor("_MidColor", new Color(1.9f, 0.62f, 0.08f));
                m.SetColor("_EdgeColor", new Color(0.75f, 0.11f, 0.02f));
            }, folder);
        }

        // オーブンの火（ZoneLook.Flame。2026-11-20 の要望: 網が燃えている演出ではなく、オーブンの奥から火が勢いよく噴き出す。手本は
        // 閉じた所に溜まった炎が扉を開けた瞬間に噴き出す現象〈バックドラフト・ロールオーバー〉と、火炎放射の映像: 炎の塊が奥から手前へ
        // ふくらみながら押し出され、口の上の縁から上へ巻き上がり、火の粉が手前へ飛び散って、上に黒い煙が昇る）。
        // 炎の板は幅 1 の箱の中（StageZoneView が柱の幅に合わせて横に伸ばすので、丸い板は横の大きさを柱の幅で割っておく）。効いている間だけ出す。
        private static GameObject CreateFlames(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            float width = Mathf.Max(1f, zone.Width > 0f ? zone.Width : size.x);
            var flames = new GameObject("Flames").transform;
            flames.SetParent(parent, false);
            // オーブンの口の真ん中（範囲の下から 4 割の高さ）と口の上の縁。
            float mouthY = -size.y * 0.5f + size.y * 0.4f;
            float lipY = -size.y * 0.5f + size.y * 0.8f;
            // 奥から手前へふくらむ炎の塊（放射の板。奥ほど小さく明るい）。
            // 重ねすぎると白く飛ぶので 3 枚（足し算）。奥ほど小さく濃い。
            // 口を出た炎は上へ昇る（手前の塊ほど上へずれる）。いちばん手前は判定の範囲（口の下から上の端まで）を覆う。いちばん奥は庫内の
            // 奥の熱の光の板を覆う大きさ（板の四角い縁が炎の中に見えないように）。
            (float Z, float Diameter, float Intensity)[] bursts = { (9f, 14f, 0.55f), (5.5f, 14f, 0.48f), (2f, 17f, 0.4f) };
            for (int i = 0; i < bursts.Length; i++)
            {
                GameObject disc = decor.Quad($"Burst{i + 1}", flames, new Vector3(0f, mouthY + i * 1.2f, bursts[i].Z), new Vector2(bursts[i].Diameter / width, bursts[i].Diameter),
                    FlameMaterial($"Flame{zone.Name}Burst{i + 1}", bursts[i].Intensity, i * 4.3f, decor.Folder, true));
                disc.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            // 口の上の縁から上へ巻き上がる炎の舌（判定の範囲の上の端まで。見た目と判定を合わせる）。
            for (int i = 0; i < 3; i++)
            {
                float height = (size.y * 0.5f - lipY + 1f) * (1f - i * 0.15f);
                decor.Quad($"Rollover{i + 1}", flames, new Vector3(0f, lipY + height * 0.45f, 4.5f - i * 2f), new Vector2(0.92f - i * 0.12f, height),
                    FlameMaterial($"Flame{zone.Name}Roll{i + 1}", 0.7f - i * 0.12f, 20f + i * 3.1f, decor.Folder, false));
            }
            // 粒: 手前へ飛び散る火の粉（効き始めに一度にも）、上に昇る黒い煙。予告の間は、扉の隙間からの煙と少しの火の粉。
            var fx = new GameObject("FireFx").transform;
            fx.SetParent(root, false);
            Quaternion outward = Quaternion.LookRotation(new Vector3(0f, 0.55f, -1f));
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            Gradient sparkColors = ParticleFx.Ramp(new[] { (new Color(1f, 0.9f, 0.55f), 0f), (new Color(1f, 0.5f, 0.1f), 0.4f), (new Color(0.7f, 0.12f, 0.02f), 1f) },
                new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) });
            ParticleSystem sparks = new ParticleFx
            {
                Name = "Sparks", LifetimeMin = 0.6f, LifetimeMax = 1.4f, SpeedMin = 6f, SpeedMax = 13f, SizeMin = 0.05f, SizeMax = 0.12f, Gravity = 0.5f,
                ConeAngle = 35f, ConeRadius = 3f, Colors = sparkColors, Additive = true, Stretch = 0.015f, Turbulence = 1.2f, Intensity = 2.5f, MaxParticles = 200,
            }.Build(fx, new Vector3(0f, mouthY, 6f), outward, decor.Folder);
            ParticleSystem smoke = new ParticleFx
            {
                Name = "Smoke", LifetimeMin = 2.5f, LifetimeMax = 4f, SpeedMin = 2.5f, SpeedMax = 5f, SizeMin = 2f, SizeMax = 3.5f, GrowFrom = 0.6f, GrowTo = 2.4f,
                ConeAngle = 20f, ConeRadius = 4f, EdgeNoise = 0.6f, Softness = 0.85f, Turbulence = 0.6f, Spin = 25f,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.4f, 0.22f, 0.12f), 0f), (new Color(0.16f, 0.14f, 0.13f), 0.4f), (new Color(0.1f, 0.1f, 0.1f), 1f) },
                    new[] { (0f, 0f), (0.5f, 0.15f), (0.3f, 0.6f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, lipY + 1f, 2.5f), up, decor.Folder);
            ParticleSystem wisps = new ParticleFx
            {
                Name = "DoorSmoke", LifetimeMin = 1.5f, LifetimeMax = 2.5f, SpeedMin = 0.8f, SpeedMax = 1.6f, SizeMin = 0.6f, SizeMax = 1.1f, GrowFrom = 0.5f, GrowTo = 2f,
                ConeAngle = 15f, ConeRadius = 5f, EdgeNoise = 0.5f, Softness = 0.85f, Turbulence = 0.4f,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.3f, 0.28f, 0.27f), 0f), (new Color(0.2f, 0.2f, 0.2f), 1f) }, new[] { (0f, 0f), (0.35f, 0.25f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, lipY, 6f), up, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), null,
                new[] { wisps, sparks }, new[] { 6f, 4f }, new[] { sparks }, new[] { 35 }, new[] { sparks, smoke }, new[] { 28f, 18f });
            return flames.gameObject;
        }

        // 溶岩の噴泉（ZoneLook.Lava。2026-11-20 の方針: 実際の現象に寄せる。火山の「溶岩流」は真上へ噴き上がるので、ハワイの火山の溶岩の
        // 噴泉を手本にする）: 芯が黄白・外へ橙・赤の噴き上がる柱（Airerage/LavaJet。頂きは判定の上の丸みと同じ丸い冠）、冠から放り出されて
        // 落ちながら黄 → 橙 → 赤 → 黒へ冷える塊、昇る火の粉、上に昇る煙。予告の間は、火口（溶岩の湖の面）がふつふつと跳ね、火の粉と煙が
        // 少し出る。柱は幅 1 の箱の中（StageZoneView が柱の位置と幅に合わせる）。粒は柱の真ん中へ動かす（HazardEffectRig）。
        private static GameObject CreateLavaFountain(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            float width = Mathf.Max(1f, zone.Width > 0f ? zone.Width : size.x);
            float crown = zone.CapHeight > 0f ? zone.CapHeight / size.y : 0f;
            var lava = new GameObject("Lava").transform;
            lava.SetParent(parent, false);
            // 塊の大きさは約 1m（柱の幅の塊の数 _Clots）。後ろの板は少し小さな塊で、奥行きのある柱に見せる。
            Material Jet(string name, float seed, float clots, float speed) => SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/LavaJet", m =>
            {
                m.SetFloat("_Crown", crown);
                m.SetFloat("_Seed", seed);
                m.SetFloat("_Aspect", size.y / width);
                m.SetFloat("_Clots", clots);
                m.SetFloat("_Speed", speed);
                // 温度の色（白く飛ばさない。芯も黄色まで）。
                m.SetColor("_HotColor", new Color(2f, 1.05f, 0.2f));
                m.SetColor("_WarmColor", new Color(1.7f, 0.45f, 0.06f));
                m.SetColor("_CoolColor", new Color(0.75f, 0.1f, 0.025f));
                m.SetColor("_CrustColor", new Color(0.07f, 0.03f, 0.02f));
            }, decor.Folder);
            decor.Quad("Jet", lava, new Vector3(0f, 0f, -0.4f), new Vector2(1f, size.y), Jet($"LavaJet{zone.Name}", 1.7f, width / 1.1f, 14f))
                .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            decor.Quad("JetBack", lava, new Vector3(0f, -size.y * 0.02f, 1.4f), new Vector2(0.92f, size.y * 0.96f), Jet($"LavaJet{zone.Name}Back", 6.1f, width / 0.8f, 11f))
                .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var fx = new GameObject("LavaFx").transform;
            fx.SetParent(root, false);
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            float crownY = size.y * 0.5f - Mathf.Max(zone.CapHeight, 1f) * 0.6f;
            float ventY = -size.y * 0.5f + 1.2f;
            // 冷えていく塊: 黄（約 1100°C）→ 橙 → 赤 → 黒（USGS: 空中で毎秒数百度冷え、黒い塊になって落ちる）。
            Gradient cooling = ParticleFx.Ramp(new[]
            {
                (new Color(1f, 0.62f, 0.16f), 0f), (new Color(0.95f, 0.32f, 0.05f), 0.25f), (new Color(0.5f, 0.06f, 0.02f), 0.5f), (new Color(0.06f, 0.03f, 0.02f), 0.75f),
            }, new[] { (1f, 0f), (1f, 0.88f), (0f, 1f) });
            ParticleSystem fragments = new ParticleFx
            {
                // 冠から外へ放り出されて落ちる塊（ぎざぎざの丸い塊。伸ばさない。黒く冷えても見えるように重ね塗り）。
                Name = "Fragments", LifetimeMin = 1.4f, LifetimeMax = 2.4f, SpeedMin = 5f, SpeedMax = 12f, SizeMin = 0.3f, SizeMax = 0.85f, Gravity = 1.1f,
                ConeAngle = 55f, ConeRadius = width * 0.4f, Colors = cooling, EdgeNoise = 0.45f, Softness = 0.25f, Intensity = 1.8f, Spin = 90f,
                MaxParticles = 500,
            }.Build(fx, new Vector3(0f, crownY, -0.3f), up, decor.Folder);
            ParticleSystem embers = new ParticleFx
            {
                Name = "Embers", LifetimeMin = 1f, LifetimeMax = 2.4f, SpeedMin = 3f, SpeedMax = 8f, SizeMin = 0.04f, SizeMax = 0.1f, Gravity = -0.05f,
                ConeAngle = 35f, ConeRadius = width * 0.4f, Additive = true, Turbulence = 1.4f, Intensity = 2.5f, MaxParticles = 300,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.8f, 0.35f), 0f), (new Color(1f, 0.35f, 0.05f), 0.5f), (new Color(0.5f, 0.05f, 0f), 1f) },
                    new[] { (1f, 0f), (0.8f, 0.6f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, crownY, -0.3f), up, decor.Folder);
            ParticleSystem plume = new ParticleFx
            {
                Name = "Plume", LifetimeMin = 3f, LifetimeMax = 5f, SpeedMin = 2f, SpeedMax = 4f, SizeMin = 2.2f, SizeMax = 3.6f, GrowFrom = 0.6f, GrowTo = 2.4f,
                ConeAngle = 22f, ConeRadius = width * 0.4f, EdgeNoise = 0.6f, Softness = 0.85f, Turbulence = 0.6f, Spin = 20f,
                // 下から溶岩に照らされた煙（下の方は橙、上へ灰色）。
                Colors = ParticleFx.Ramp(new[] { (new Color(0.45f, 0.22f, 0.12f), 0f), (new Color(0.15f, 0.12f, 0.11f), 0.45f), (new Color(0.09f, 0.08f, 0.08f), 1f) },
                    new[] { (0f, 0f), (0.45f, 0.15f), (0.28f, 0.6f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, crownY + 1f, 0.8f), up, decor.Folder);
            // 予告: 火口がふつふつと跳ねる（小さな塊が低く跳ねる）、火の粉、煙。
            ParticleSystem bubbling = new ParticleFx
            {
                Name = "VentSpatter", LifetimeMin = 0.5f, LifetimeMax = 0.9f, SpeedMin = 2f, SpeedMax = 5f, SizeMin = 0.2f, SizeMax = 0.45f, Gravity = 1f,
                ConeAngle = 30f, ConeRadius = width * 0.3f, Colors = cooling, EdgeNoise = 0.45f, Softness = 0.25f, Intensity = 1.8f, Spin = 90f, MaxParticles = 120,
            }.Build(fx, new Vector3(0f, ventY, -0.3f), up, decor.Folder);
            ParticleSystem ventEmbers = new ParticleFx
            {
                Name = "VentEmbers", LifetimeMin = 1f, LifetimeMax = 2f, SpeedMin = 1.5f, SpeedMax = 3.5f, SizeMin = 0.05f, SizeMax = 0.12f, Gravity = -0.05f,
                ConeAngle = 25f, ConeRadius = width * 0.35f, Additive = true, Turbulence = 1f, Intensity = 4f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.7f, 0.3f), 0f), (new Color(0.8f, 0.2f, 0.03f), 1f) }, new[] { (1f, 0f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, ventY, -0.3f), up, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), fx,
                new[] { bubbling, ventEmbers }, new[] { 30f, 18f }, new[] { fragments }, new[] { 60 }, new[] { fragments, embers, plume }, new[] { 55f, 50f, 14f });
            return lava.gameObject;
        }

        // 壁の間欠泉: 噴き出し口（範囲の、向きと反対側の辺の真ん中）と、向きへ斜めに伸びる噴き出し（噴いている間だけ出す）。
        private static GameObject CreateGeyser(Transform parent, Transform root, ZoneSpec zone, Color color, StageDecor decor)
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
            // 噴き出しは流れる液体の筋（口から外へ流れる。2026-11-20 の要望: ギミックをそれぞれのイメージに合わせて精巧に）。
            // 蒸気（ZoneLook.Steam）は湯気の粒だけで見せる（平たい板は四角く見えたので使わない。2026-11-20 の方針）。
            if (zone.Look == StageZoneView.ZoneLook.Steam)
            {
                var plume = new GameObject("SteamJet");
                plume.transform.SetParent(parent, false);
                CreateSteamVent(root, zone, mouth, direction, length, width, decor);
                return plume;
            }
            GameObject jet = decor.Quad("Jet", parent, new Vector3(middle.x, middle.y, -0.4f), new Vector2(width, length),
                StreamMaterial($"GeyserJet{zone.Name}", WithAlpha(Color.Lerp(color, Color.white, 0.55f), 0.6f), WithAlpha(color, 0.8f),
                    new Color(1f, 1f, 1f, 0.85f), 2.4f, new Vector2(6f, 2f), 0.55f, 0.35f, decor.Folder));
            jet.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return jet;
        }

        // 噴気の湯気（ZoneLook.Steam。2026-11-20 の方針: 実際の現象に寄せる。火山の噴気孔・間欠泉の映像を手本にする: 口のすぐ外は
        // 透明に近く〈湯気がまだ冷えて白くなっていない〉、少し離れると白い湯気の塊がふくらみながら勢いよく伸び、渦を巻いて消える。
        // 熱い湯のしぶきが飛ぶ）。噴いている間は湯気としぶき、予告の間は口から細い湯気。粒は HazardEffectRig が出す。
        private static void CreateSteamVent(Transform root, ZoneSpec zone, Vector2 mouth, Vector2 direction, float length, float width, StageDecor decor)
        {
            var fx = new GameObject("SteamFx").transform;
            fx.SetParent(root, false);
            Quaternion along = Quaternion.LookRotation(new Vector3(direction.x, direction.y, 0f), Vector3.back);
            float speed = length / 0.9f;
            ParticleSystem steam = new ParticleFx
            {
                Name = "Steam", LifetimeMin = 0.7f, LifetimeMax = 1.2f, SpeedMin = speed * 0.8f, SpeedMax = speed * 1.15f, SizeMin = width * 0.25f,
                SizeMax = width * 0.45f, GrowFrom = 0.5f, GrowTo = 3f, ConeAngle = 7f, ConeRadius = 0.35f, EdgeNoise = 0.55f, Softness = 0.9f,
                Turbulence = 0.9f, Spin = 30f, MaxParticles = 300,
                // 口のすぐ外は透明、少し離れて白く、ふくらんで消える。
                Colors = ParticleFx.Ramp(new[] { (new Color(0.92f, 0.9f, 0.88f), 0f), (new Color(0.8f, 0.79f, 0.78f), 1f) },
                    new[] { (0f, 0f), (0.04f, 0.08f), (0.38f, 0.25f), (0.2f, 0.65f), (0f, 1f) }),
            }.Build(fx, new Vector3(mouth.x, mouth.y, -0.3f), along, decor.Folder);
            ParticleSystem droplets = new ParticleFx
            {
                Name = "Droplets", LifetimeMin = 0.5f, LifetimeMax = 0.9f, SpeedMin = speed * 0.9f, SpeedMax = speed * 1.3f, SizeMin = 0.03f, SizeMax = 0.07f,
                Gravity = 1f, ConeAngle = 12f, ConeRadius = 0.3f, Stretch = 0.01f, Softness = 0.4f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.9f, 0.96f, 1f), 0f), (new Color(0.8f, 0.9f, 1f), 1f) }, new[] { (0.9f, 0f), (0.7f, 0.6f), (0f, 1f) }),
            }.Build(fx, new Vector3(mouth.x, mouth.y, -0.3f), along, decor.Folder);
            ParticleSystem wisps = new ParticleFx
            {
                Name = "Wisps", LifetimeMin = 1.2f, LifetimeMax = 2f, SpeedMin = 1.5f, SpeedMax = 3f, SizeMin = 0.4f, SizeMax = 0.8f, GrowFrom = 0.6f, GrowTo = 2.5f,
                ConeAngle = 12f, ConeRadius = 0.3f, EdgeNoise = 0.5f, Softness = 0.9f, Turbulence = 0.5f,
                Colors = ParticleFx.Ramp(new[] { (Color.white, 0f), (new Color(0.9f, 0.9f, 0.9f), 1f) }, new[] { (0f, 0f), (0.35f, 0.25f), (0f, 1f) }),
            }.Build(fx, new Vector3(mouth.x, mouth.y, -0.3f), along, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), null,
                new[] { wisps }, new[] { 10f }, new[] { steam }, new[] { 30 }, new[] { steam, droplets }, new[] { 90f, 25f });
        }

        // いつも吹いている縦の風（周期のない上下向きの風。水の流れは除く）か（StageZoneView.IsSteadyDraft と同じ決め方）。
        private static bool IsSteadyDraft(ZoneSpec zone)
        {
            return zone.Kind == StageZoneKind.Gust && zone.PeriodSeconds <= 0f && zone.Look != StageZoneView.ZoneLook.Flow
                && Mathf.Abs(zone.Direction.y) > Mathf.Abs(zone.Direction.x);
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

        /// <summary>材質を置くフォルダ。</summary>
        public string Folder => _folder;

        /// <summary>
        /// 水の材質（Airerage/Water。2026-11-18 の要望: 水の表現をリアルに）。cross が真なら水の断面（半透明。surfaceY は水面のワールドの
        /// 高さ）、偽なら水面（opaque なら不透明。海の箱や奥の海の板）。
        /// </summary>
        public Material Water(string name, Color shallow, Color deep, bool cross, float surfaceY, bool opaque)
        {
            return WaterMaterial(name, shallow, deep, cross, surfaceY, opaque, new Color(0.42f, 0.6f, 0.88f), new Color(0.85f, 0.9f, 0.96f), _folder);
        }

        /// <summary>水の材質を作る（StageDressing の奥の海の板からも使う）。</summary>
        public static Material WaterMaterial(string name, Color shallow, Color deep, bool cross, float surfaceY, bool opaque, Color sky, Color horizon, string folder)
        {
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/Water", m =>
            {
                m.SetFloat("_Mode", cross ? 0f : 1f);
                m.SetColor("_ShallowColor", shallow);
                m.SetColor("_DeepColor", deep);
                m.SetFloat("_SurfaceY", surfaceY);
                m.SetFloat("_Opacity", opaque ? 1f : cross ? 0.42f : 0.45f);
                m.SetFloat("_DeepOpacity", opaque ? 1f : 0.82f);
                m.SetColor("_SkyColor", sky);
                m.SetColor("_HorizonColor", horizon);
                m.SetFloat("_SrcBlend", opaque ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", opaque ? (float)BlendMode.Zero : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", opaque ? 1f : 0f);
            }, folder);
            material.renderQueue = opaque ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
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
