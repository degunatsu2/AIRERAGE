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
            CreateTraps(layout.Traps, layout.Terrain, decor);
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
                else if (spec.Kind == StagePlatformKind.Hoist)
                {
                    authoring.ConfigureHoist(spec.Size, spec.OpenHeight, spec.BouncePower, spec.AnchorRadius);
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
                // 刺さる足場の縁の光（荷役の網は真ん中のアンカーにつながる網なので、縁は光らせない）。
                if (spec.Attachable && spec.Kind != StagePlatformKind.Hoist)
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
        private static void CreateTraps(TrapSpec[] traps, SandboxWireSceneBuilder.TerrainBox[] terrain, StageDecor decor)
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
                if (spec.Kind == StageTrapKind.DonutFryer)
                {
                    // ドーナツは決まった道のりを弾むので、見た目は DonutFryerView が動かす（StageTrapView は使わない）。
                    authoring.ConfigureDonutFryer(spec.Waypoints, spec.PeriodSeconds, spec.PhaseSeconds, spec.Radius, spec.HopHeight);
                    CreateDonutFryer(go.transform, authoring, spec, decor);
                    continue;
                }
                if (spec.Kind == StageTrapKind.WispRing)
                {
                    // 鬼火の輪は飛び回るので、見た目は WispRingView が動かす（StageTrapView は使わない）。
                    authoring.ConfigureWispRing(spec.Waypoints, spec.Speed, spec.HoverSeconds, spec.RingSize, spec.Reward, spec.RespawnMinSeconds,
                        spec.RespawnMaxSeconds);
                    CreateWispRingTrap(go.transform, spec, decor);
                    continue;
                }
                var view = go.AddComponent<StageTrapView>();
                if (spec.Kind == StageTrapKind.Tripwire)
                {
                    authoring.ConfigureTripwire(spec.BeamSize, spec.MuzzleOffset, spec.Direction, spec.Speed, spec.Range, spec.Power, spec.Knockback,
                        spec.ArmSeconds, spec.CooldownSeconds, spec.MissileSize);
                    CreateTripwire(go.transform, spec, view, lineMaterial, terrain, decor);
                }
                else
                {
                    authoring.ConfigurePitcher(spec.LaunchVelocity, spec.PeriodSeconds, spec.PhaseSeconds, spec.Radius, spec.Power, spec.Knockback,
                        spec.MinHitSpeed, spec.BlinkSeconds);
                    CreatePitchingMachine(go.transform, spec, view, lineMaterial, decor);
                }
            }
        }

        // 地形のうち、x の真下（y より下）でいちばん高い上の面（なければ y - fallback）。
        private static float FloorBelow(SandboxWireSceneBuilder.TerrainBox[] terrain, float x, float y, float fallback)
        {
            float best = float.NegativeInfinity;
            if (terrain != null)
            {
                foreach (SandboxWireSceneBuilder.TerrainBox box in terrain)
                {
                    if (x >= box.Min.x && x <= box.Max.x && box.Max.y <= y + 0.01f && box.Max.y > best)
                    {
                        best = box.Max.y;
                    }
                }
            }
            return float.IsNegativeInfinity(best) ? y - fallback : best;
        }

        // 赤外線とミサイル（2026-11-21 の方針: 実際の現象に寄せる）。手本は防犯の赤外線センサー（向かい合った投光器と受光器の間に、目に
        // 見えない近赤外の光を通し、光が遮られると警報。光は点滅させて周りの光と見分ける）。目に見えないので、ゲームの警報の光線のように
        // 細い赤い 2 本の線（上下 2 段の光線のセンサー）で見せ、光線の上で塵がきらめく。両端に床から立てた投光器と受光器。ミサイルは
        // 胴・尖った頭・4 枚の尾翼と尾の炎、飛ぶ間は白い煙の帯を残す。発射装置は 4 つの筒の箱。当たり（赤外線・ミサイル）は今までどおり。
        private static void CreateTripwire(Transform trap, TrapSpec spec, StageTrapView view, Material lineMaterial, SandboxWireSceneBuilder.TerrainBox[] terrain,
            StageDecor decor)
        {
            Material beamMaterial = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("InfraredBeam", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 2.2f);
                m.SetFloat("_Core", 1.4f);
            }, decor.Folder);
            var red = new Color(1f, 0.1f, 0.08f, 0.85f);
            float halfX = spec.BeamSize.x * 0.5f;
            float gap = Mathf.Min(0.08f, spec.BeamSize.y * 0.3f);
            LineRenderer upper = Line("Beam", trap, beamMaterial, new Vector3(-halfX + 0.25f, gap, -0.1f), new Vector3(halfX - 0.25f, gap, -0.1f), red, 0.035f);
            LineRenderer lower = Line("Beam2", trap, beamMaterial, new Vector3(-halfX + 0.25f, -gap, -0.1f), new Vector3(halfX - 0.25f, -gap, -0.1f), red, 0.035f);
            // 投光器と受光器（床から立てた柱の上の白い箱と、赤みがかった窓、緑の表示灯）。
            Material housing = decor.Lit("SensorHousing", new Color(0.82f, 0.84f, 0.86f));
            Material pole = decor.Lit("SensorPole", new Color(0.3f, 0.32f, 0.35f));
            pole.SetFloat("_Metallic", 0.6f);
            Material window = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("SensorWindow", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(0.6f, 0.03f, 0.05f));
                m.SetFloat("_Intensity", 0.9f);
            }, decor.Folder);
            Material led = decor.Unlit("SensorLed", new Color(0.3f, 1f, 0.4f));
            Vector3 origin = trap.position;
            foreach (float end in new[] { -1f, 1f })
            {
                float x = end * (halfX - 0.1f);
                float floor = FloorBelow(terrain, origin.x + x, origin.y - 0.2f, 0.8f) - origin.y;
                decor.Box("Sensor", trap, new Vector3(x, 0f, 0f), new Vector3(0.3f, 0.6f, 0.36f), housing);
                decor.Box("SensorWindow", trap, new Vector3(x - end * 0.155f, 0f, 0f), new Vector3(0.02f, 0.4f, 0.26f), window);
                decor.Sphere("SensorLed", trap, new Vector3(x, 0.22f, -0.19f), Vector3.one * 0.06f, led);
                // 下の床が近ければ柱で立て、遠ければ（研究所の発射装置の側は 8m 下が床）後ろの金具で発射装置の箱に取り付ける。
                float stand = -0.3f - floor;
                if (stand > 0.02f && stand < 1.8f)
                {
                    decor.Box("SensorPole", trap, new Vector3(x, floor + stand * 0.5f, 0f), new Vector3(0.08f, stand, 0.08f), pole);
                    decor.Box("SensorFoot", trap, new Vector3(x, floor + 0.03f, 0f), new Vector3(0.36f, 0.06f, 0.36f), pole);
                }
                else if (stand >= 1.8f)
                {
                    decor.Box("SensorBracket", trap, new Vector3(x - end * 0.25f, -0.22f, 0.12f), new Vector3(0.5f, 0.08f, 0.16f), pole);
                }
            }
            // 発射装置（4 つの筒の箱。発射口の後ろ）とランプ。
            Vector2 back = spec.MuzzleOffset - spec.Direction * 0.9f;
            float angle = Mathf.Atan2(spec.Direction.y, spec.Direction.x) * Mathf.Rad2Deg;
            var launcher = new GameObject("Launcher").transform;
            launcher.SetParent(trap, false);
            launcher.localPosition = new Vector3(back.x, back.y, 0f);
            launcher.localRotation = Quaternion.Euler(0f, 0f, angle);
            Material pod = decor.Lit("MissileLauncher", new Color(0.3f, 0.34f, 0.3f));
            pod.SetFloat("_Smoothness", 0.35f);
            Material bore = decor.Lit("MissileBore", new Color(0.03f, 0.03f, 0.035f));
            decor.Box("Pod", launcher, Vector3.zero, new Vector3(1.8f, 1.2f, 1.6f), pod);
            foreach (float y in new[] { -0.28f, 0.28f })
            {
                foreach (float z in new[] { -0.36f, 0.36f })
                {
                    decor.Cylinder("Tube", launcher, new Vector3(0.86f, y, z), new Vector3(0.42f, 0.06f, 0.42f), bore).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }
            decor.Box("Mount", launcher, new Vector3(-0.1f, -0.72f, 0f), new Vector3(0.7f, 0.26f, 0.9f), pole);
            GameObject lamp = decor.Sphere("Lamp", trap, new Vector3(back.x, back.y + 0.78f, 0f), new Vector3(0.36f, 0.36f, 0.36f),
                decor.Unlit("LauncherLamp", new Color(1f, 0.15f, 0.1f)));
            // ミサイル（右向きに作る。StageTrapView が向きに回す）: 胴・尖った頭・4 枚の尾翼・尾の炎。
            var missile = new GameObject("Missile").transform;
            missile.SetParent(trap, false);
            float length = spec.MissileSize.x;
            float radius = spec.MissileSize.y * 0.36f;
            Material body = decor.Lit("MissileBody", new Color(0.86f, 0.88f, 0.9f));
            body.SetFloat("_Smoothness", 0.6f);
            Material nose = decor.Lit("MissileNose", new Color(0.85f, 0.14f, 0.1f));
            decor.Cylinder("Body", missile, Vector3.zero, new Vector3(radius * 2f, length * 0.34f, radius * 2f), body).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            ProceduralMesh.Place("Nose", missile, ProceduralMesh.Lathe("Gimmicks", "MissileNose", new[]
            {
                new Vector2(1f, 0f), new Vector2(0.92f, 0.3f), new Vector2(0.7f, 0.6f), new Vector2(0.38f, 0.85f), new Vector2(0f, 1f),
            }, 16), new Vector3(length * 0.34f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), new Vector3(radius, length * 0.26f, radius), nose);
            for (int k = 0; k < 4; k++)
            {
                var fin = new GameObject("Fin").transform;
                fin.SetParent(missile, false);
                fin.localPosition = new Vector3(-length * 0.26f, 0f, 0f);
                fin.localRotation = Quaternion.Euler(45f + k * 90f, 0f, 0f);
                decor.Box("Blade", fin, new Vector3(0f, radius * 1.25f, 0f), new Vector3(length * 0.22f, radius * 1.1f, 0.03f), nose);
            }
            Material flameMaterial = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("MissileFlame", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(1f, 0.62f, 0.2f));
                m.SetFloat("_Intensity", 4f);
            }, decor.Folder);
            ProceduralMesh.Place("Flame", missile, ProceduralMesh.Lathe("Gimmicks", "MissileFlame", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.75f, 0.12f), new Vector2(0.55f, 0.5f), new Vector2(0f, 1f),
            }, 12), new Vector3(-length * 0.34f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(radius, length * 0.42f, radius), flameMaterial);
            var tail = new GameObject("Tail").transform;
            tail.SetParent(missile, false);
            tail.localPosition = new Vector3(-length * 0.55f, 0f, 0f);
            // 飛ぶ間の煙の帯（尾から出し、ワールドに残って広がりながら薄れる）と、光線の上できらめく塵。
            ParticleSystem trail = new ParticleFx
            {
                Name = "MissileSmoke", LifetimeMin = 0.9f, LifetimeMax = 1.6f, SpeedMin = 0f, SpeedMax = 0.4f, SizeMin = 0.3f, SizeMax = 0.5f,
                GrowFrom = 0.6f, GrowTo = 2.4f, Softness = 0.7f, EdgeNoise = 0.4f, Turbulence = 0.6f, Rate = 70f, MaxParticles = 220,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.95f, 0.93f, 0.9f), 0f), (new Color(0.6f, 0.6f, 0.62f), 1f) }, new[] { (0f, 0f), (0.7f, 0.08f), (0.35f, 0.6f), (0f, 1f) }),
            }.Build(trap, Vector3.zero, Quaternion.identity, decor.Folder);
            ParticleSystem.MainModule trailMain = trail.main;
            trailMain.prewarm = false;
            ParticleSystem.EmissionModule trailEmission = trail.emission;
            trailEmission.enabled = false;
            ParticleSystem motes = new ParticleFx
            {
                Name = "InfraredMotes", LifetimeMin = 0.3f, LifetimeMax = 0.7f, SpeedMin = 0f, SpeedMax = 0.15f, SizeMin = 0.04f, SizeMax = 0.08f,
                BoxSize = new Vector3(Mathf.Max(0.5f, spec.BeamSize.x - 0.6f), gap * 2.6f, 0.2f), Additive = true, Intensity = 2.5f, Rate = 14f, MaxParticles = 60,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.45f, 0.35f), 0f), (new Color(1f, 0.15f, 0.1f), 1f) }, new[] { (0f, 0f), (1f, 0.3f), (0f, 1f) }),
            }.Build(trap, new Vector3(0f, 0f, -0.1f), Quaternion.identity, decor.Folder);
            // 撃った後の待ちの円形のゲージ（発射装置の周り。ユーザーの要望、2026-10-26）。
            RingGaugeView reload = SandboxWireSceneBuilder.CreateRingGauge("ReloadGauge", trap, "RELOAD", 1.5f, new Color(1f, 0.3f, 0.25f, 0.85f), sceneCamera: false);
            view.Configure(upper, missile, lamp.GetComponent<Renderer>(), lineMaterial, reload, launcher);
            view.ConfigureTripwireDetail(new Renderer[] { lower }, motes, trail, tail);
        }

        // ピッチャーマシン（2026-11-21 の方針: 実際の現象に寄せる）。手本は 2 つのタイヤのピッチングマシン（上下に並んだタイヤが逆向きに
        // 高速で回り、間に送り込んだボールを挟んで打ち出す。上下の回る速さの差で回転がかかる。ボールは筒からタイヤの間へ送る）。
        // 頭（タイヤ 2 つ・モーター・枠）を打ち出す向きに傾け、後ろの上にボールの入った縦の筒（マガジン）と、タイヤの間へ下る送りのレール。
        // 照明塔の柱から腕で支える（柱は発射口から横に 3m。StadiumStage）。左へ打ち出すマシンは打ち出す向きの軸で裏返す（上のタイヤが上）。
        // ボールは大きい（半径 spec.Radius）ので、マシンも大きく作る。当たり（ボール）は今までどおり。
        private static void CreatePitchingMachine(Transform trap, TrapSpec spec, StageTrapView view, Material lineMaterial, StageDecor decor)
        {
            const float PoleOffset = 3f;
            float facing = spec.Facing;
            float angle = Mathf.Atan2(spec.LaunchVelocity.y, spec.LaunchVelocity.x) * Mathf.Rad2Deg;
            var head = new GameObject("Head").transform;
            head.SetParent(trap, false);
            head.localRotation = Quaternion.Euler(0f, 0f, angle);
            head.localScale = new Vector3(1f, facing, 1f);
            Material rubber = decor.Lit("PitchingTire", new Color(0.06f, 0.06f, 0.07f));
            rubber.SetFloat("_Smoothness", 0.2f);
            Material hub = decor.Lit("PitchingHub", new Color(0.78f, 0.8f, 0.84f));
            hub.SetFloat("_Metallic", 0.85f);
            hub.SetFloat("_Smoothness", 0.75f);
            Material casing = decor.Lit("PitchingMachine", new Color(0.2f, 0.45f, 0.85f));
            casing.SetFloat("_Smoothness", 0.55f);
            Material mark = decor.Unlit("PitchingTireMark", new Color(0.9f, 0.9f, 0.9f));
            Material rail = decor.Lit("PitchingRail", new Color(0.6f, 0.62f, 0.66f));
            rail.SetFloat("_Metallic", 0.8f);
            Material leather = decor.Lit("Baseball", new Color(0.97f, 0.96f, 0.92f));
            Material seam = decor.Unlit("BaseballSeam", new Color(0.85f, 0.1f, 0.1f));
            Material clear = decor.Transparent("PitchingMagazine", new Color(0.8f, 0.9f, 1f, 0.18f));
            const float WheelRadius = 0.62f;
            float offset = spec.Radius + WheelRadius - 0.12f;
            var wheels = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1f : -1f;
                var wheel = new GameObject(i == 0 ? "WheelUpper" : "WheelLower").transform;
                wheel.SetParent(head, false);
                wheel.localPosition = new Vector3(0f, s * offset, 0f);
                decor.Cylinder("Tire", wheel, Vector3.zero, new Vector3(WheelRadius * 2f, 0.22f, WheelRadius * 2f), rubber).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                decor.Cylinder("Hub", wheel, new Vector3(0f, 0f, -0.01f), new Vector3(0.55f, 0.24f, 0.55f), hub).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * Mathf.PI * 2f / 3f;
                    decor.Box("Mark", wheel, new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(a) * 0.42f, -0.23f), new Vector3(0.16f, 0.05f, 0.01f), mark)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
                }
                wheels[i] = wheel;
                // タイヤの後ろのモーター（回さない）。
                decor.Box("Motor", head, new Vector3(0f, s * offset, 0.42f), new Vector3(0.5f, 0.5f, 0.44f), casing);
            }
            // 頭の枠（タイヤの後ろの板と、上下の覆い）。
            decor.Box("Frame", head, new Vector3(-0.1f, 0f, 0.3f), new Vector3(1.1f, offset * 2f + 0.9f, 0.08f), casing);
            foreach (float s in new[] { -1f, 1f })
            {
                decor.Box("Guard", head, new Vector3(-0.1f, s * (offset + WheelRadius + 0.08f), 0.05f), new Vector3(1.5f, 0.1f, 0.6f), casing);
            }
            // 送りのレール（頭の後ろの上から、タイヤの間へ下る 2 本の棒）と、ボールの入った縦の筒（ワールドで縦）。
            var chute = new[] { new Vector3(-1.55f, 1.55f, 0f), new Vector3(-1.2f, 0.9f, 0f), new Vector3(-0.85f, 0.35f, 0f), new Vector3(-0.55f, 0.04f, 0f) };
            foreach (float z in new[] { -0.55f, 0.55f })
            {
                var path = new Vector3[chute.Length];
                for (int i = 0; i < chute.Length; i++)
                {
                    path[i] = chute[i] + new Vector3(0f, -spec.Radius * 0.7f, z);
                }
                ProceduralMesh.Place("FeedRail", head, ProceduralMesh.Tube("Gimmicks", "PitchingFeedRail", path, new[] { 0.05f, 0.05f, 0.05f, 0.05f }, 6),
                    Vector3.zero, Quaternion.identity, Vector3.one, rail);
            }
            Vector3 magazineFoot = trap.InverseTransformPoint(head.TransformPoint(chute[0]));
            var magazine = new GameObject("Magazine").transform;
            magazine.SetParent(trap, false);
            magazine.localPosition = magazineFoot + new Vector3(0f, spec.Radius * 2.1f, 0f);
            decor.Cylinder("Tube", magazine, Vector3.zero, new Vector3(spec.Radius * 2.3f, spec.Radius * 2.1f, spec.Radius * 2.3f), clear);
            decor.Cylinder("Cap", magazine, new Vector3(0f, spec.Radius * 2.15f, 0f), new Vector3(spec.Radius * 2.45f, 0.1f, spec.Radius * 2.45f), casing);
            CreateBaseball(decor, "WaitingBall", magazine, new Vector3(0f, -spec.Radius * 0.05f, 0.05f), spec.Radius, leather, seam);
            CreateBaseball(decor, "WaitingBall", magazine, new Vector3(0f, spec.Radius * 1.55f, 0.05f), spec.Radius, leather, seam);
            Transform feedBall = CreateBaseball(decor, "FeedBall", head, chute[0], spec.Radius, leather, seam);
            // 照明塔の柱からの腕と筋交い（柱の面から頭の後ろへ）。
            Vector3 headBack = trap.InverseTransformPoint(head.TransformPoint(new Vector3(-0.6f, 0f, 0.3f)));
            var poleFace = new Vector3(-facing * PoleOffset, headBack.y - 0.2f, 0.3f);
            Material arm = decor.Lit("PitchingArm", new Color(0.32f, 0.34f, 0.37f));
            arm.SetFloat("_Metallic", 0.6f);
            Strut(decor, "MountArm", trap, poleFace, headBack, 0.22f, arm);
            Strut(decor, "MountBrace", trap, poleFace + new Vector3(0f, -1.6f, 0f), Vector3.Lerp(poleFace, headBack, 0.75f), 0.14f, arm);
            decor.Box("MountPlate", trap, poleFace + new Vector3(facing * 0.06f, -0.6f, 0f), new Vector3(0.12f, 2.2f, 0.7f), arm);
            // 打ち出すボール（StageTrapView が道のりの位置へ動かす）。
            Transform ball = CreateBaseball(decor, "Ball", trap, Vector3.zero, spec.Radius, leather, seam);
            view.Configure(null, ball, null, lineMaterial);
            view.ConfigurePitcherDetail(wheels, feedBall, chute);
        }

        // 野球のボール（白い革と、交わる 2 本の赤い縫い目）。
        private static Transform CreateBaseball(StageDecor decor, string name, Transform parent, Vector3 localPosition, float radius, Material leather, Material seam)
        {
            var ball = new GameObject(name).transform;
            ball.SetParent(parent, false);
            ball.localPosition = localPosition;
            decor.Sphere("Leather", ball, Vector3.zero, Vector3.one * radius * 2f, leather);
            var seamA = new GameObject("SeamA").transform;
            seamA.SetParent(ball, false);
            seamA.localRotation = Quaternion.Euler(62f, 0f, 20f);
            Band("Seam", seamA, Vector3.zero, radius * 0.985f, radius * 1.02f, radius * 0.06f, seam);
            var seamB = new GameObject("SeamB").transform;
            seamB.SetParent(ball, false);
            seamB.localRotation = Quaternion.Euler(-62f, 90f, 20f);
            Band("Seam", seamB, Vector3.zero, radius * 0.985f, radius * 1.02f, radius * 0.06f, seam);
            return ball;
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

        /// <summary>
        /// ステージの主の光（太陽・月の平行光源）。ギミックの点の光（宇宙船の船内の照明など）を取り違えないように、平行光源だけから探す
        /// （FindFirstObjectByType は順番が決まらず、船内の照明に主の光の設定を上書きしていた。2026-11-21）。
        /// </summary>
        internal static Light MainLight()
        {
            foreach (Light candidate in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (candidate.type == LightType.Directional)
                {
                    return candidate;
                }
            }
            return null;
        }

        // 光、空（Skybox/Procedural）、環境光、霧。ステージのシーンを最初に読み込むので、このシーンの設定が試合とメニューの背景で使われる。
        private static void ApplyLighting(StageTheme theme, StageDecor decor)
        {
            Light light = StageSceneBuilder.MainLight();
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
                authoring.ConfigureElectric(zone.Electric);
                authoring.ConfigureRound(zone.Round);
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
                // いつも効いている危険（周期と効く長さが同じ。プラズマ球）は、いつも出しておく。
                bool periodic = zone.PeriodSeconds > 0f && zone.ActiveSeconds < zone.PeriodSeconds;
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
                case StageZoneKind.Launcher when zone.Look == StageZoneView.ZoneLook.Toaster:
                    return CreateToaster(parent, root, zone, decor);
                case StageZoneKind.Launcher when zone.Look == StageZoneView.ZoneLook.Catapult:
                    return CreateCatapult(parent, root, zone, decor);
                case StageZoneKind.Gust when zone.Look == StageZoneView.ZoneLook.Pneumatic:
                    return CreatePneumaticTube(parent, root, zone, decor);
                case StageZoneKind.Warp when zone.Look == StageZoneView.ZoneLook.Wormhole:
                    return CreateWormhole(parent, root, zone, color, decor);
                case StageZoneKind.LowGravity when zone.Look == StageZoneView.ZoneLook.Microgravity:
                    return CreateMicrogravity(parent, zone, decor);
                case StageZoneKind.CoilGun:
                    return CreateCoilAccelerator(parent, zone, color, decor);
                case StageZoneKind.Launcher:
                    if (zone.PeriodSeconds > 0f)
                    {
                        return CreateGeyser(parent, root, zone, color, decor);
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
                case StageZoneKind.Updraft when zone.Look == StageZoneView.ZoneLook.DustDevil:
                    return CreateDustDevil(parent, zone, decor);
                case StageZoneKind.Updraft:
                    return decor.Quad("Column", parent, new Vector3(0f, 0f, 1f), size, decor.Transparent(kind, WithAlpha(color, 0.12f)));
                case StageZoneKind.Gust when zone.Look == StageZoneView.ZoneLook.Steam:
                    // 周期のある湯気（やかん）と、いつも噴く噴気（噴気孔。とても強い風。2026-11-20 の要望）。
                    return zone.PeriodSeconds > 0f ? CreateKettleSteam(parent, root, zone, decor) : CreateFumarole(parent, zone, decor);
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
                    if (zone.Look == StageZoneView.ZoneLook.Plasma)
                    {
                        return CreatePlasmaGlobe(parent, zone, decor);
                    }
                    if (zone.Look == StageZoneView.ZoneLook.MagmaRock)
                    {
                        return CreateRollingBomb(parent, root, zone, decor);
                    }
                    if (zone.Look == StageZoneView.ZoneLook.Laser)
                    {
                        return CreateLaserGrid(parent, root, zone, decor);
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

        // ワープゲートの渦（ワープゲート + ZoneLook.Wormhole。2026-11-24 の要望: 前の紫の楕円の見た目を精巧に）: 楕円の渦の面
        // （Airerage/Wormhole。半径 1 の円盤をゲートの大きさに拡大。キャラクターの少し奥）と、出口の小さな渦（逆回り。動かない）。
        // 回りながら揺れる輪と吸い込まれる輪、動く向きへの撓み、ワープの波紋は WormholeView。当たりは渦と同じ楕円（StageZone.Round）。
        private static GameObject CreateWormhole(Transform parent, Transform root, ZoneSpec zone, Color color, StageDecor decor)
        {
            var mouth = new GameObject("Wormhole").transform;
            mouth.SetParent(parent, false);
            Mesh disc = ProceduralMesh.Disc("SpaceStation", "WarpDisc", 10, 64);
            Material portal = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WarpPortal", "Airerage/Wormhole", m =>
            {
                m.SetColor("_Color", new Color(color.r * 1.7f, color.g * 1.5f, color.b * 2.4f, 1f));
                m.SetColor("_Mid", color);
                m.SetFloat("_Inward", 1f);
            }, decor.Folder);
            GameObject face = ProceduralMesh.Place("Portal", mouth, disc, new Vector3(0f, 0f, 0.3f), Quaternion.identity,
                new Vector3(zone.Size.x * 0.5f, zone.Size.y * 0.5f, 1f), portal);
            face.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            MeshFilter exit = null;
            const float exitRadius = 0.7f;
            if (zone.ExitMouth)
            {
                Material outflow = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WarpExit", "Airerage/Wormhole", m =>
                {
                    m.SetColor("_Color", new Color(color.r * 1.7f, color.g * 1.6f, color.b * 2.4f, 1f));
                    m.SetColor("_Mid", Color.Lerp(color, Color.white, 0.25f));
                    m.SetFloat("_Inward", -1f);
                    m.SetFloat("_Opacity", 0.6f);
                }, decor.Folder);
                // 出口は動かない（動くゲートでも、ギミックの物体の子にする）。
                Vector3 local = new Vector3(zone.Target.x - zone.Center.x, zone.Target.y - zone.Center.y, 0.3f);
                GameObject exitFace = ProceduralMesh.Place("ExitMouth", root, disc, local, Quaternion.identity, Vector3.one * exitRadius, outflow);
                exitFace.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                exit = exitFace.GetComponent<MeshFilter>();
            }
            Material line = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WarpRing", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 2.4f);
            }, decor.Folder);
            mouth.gameObject.AddComponent<WormholeView>().Configure(face.GetComponent<MeshFilter>(), exit, line, color, zone.Size * 0.5f, exitRadius);
            return mouth.gameObject;
        }

        // 宇宙船の中の無重力（無重力 + ZoneLook.Microgravity。2026-11-21 のギミックの精巧化 第 5 弾）: 手本は ISS と天宮の船内の映像。
        // 床と天井の手すり（支柱で壁から浮かせた管）、天井の照明の帯、漂う物（白い荷物の袋・工具・ペン・手順書・食品の袋・束ねた
        // ケーブル）と水の玉（Airerage/WaterDrop。後ろの景色を逆さに映す）。漂う物の動きは MicrogravityView（表示だけ）。
        // 物は遊ぶ面より奥（z 0.7〜1.4。奥の壁の手前）に置き、キャラクターの手前を隠さない。
        private static GameObject CreateMicrogravity(Transform parent, ZoneSpec zone, StageDecor decor)
        {
            Vector2 half = zone.Size * 0.5f;
            var cabin = new GameObject("Microgravity").transform;
            cabin.SetParent(parent, false);
            const string meshes = "SpaceStation";
            // 手すり（床と天井の近く。支柱で奥の壁から少し浮かせる）と照明の帯。
            Material rail = decor.Lit("CabinHandrail", new Color(0.78f, 0.78f, 0.74f));
            rail.SetFloat("_Smoothness", 0.55f);
            Material light = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("CabinLight", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f));
                m.SetFloat("_Intensity", 1.6f);
            }, decor.Folder);
            float railLength = zone.Size.x - 2.4f;
            foreach (float y in new[] { -half.y + 0.45f, half.y - 0.45f })
            {
                decor.Cylinder("Handrail", cabin, new Vector3(0f, y, 1.42f), new Vector3(0.07f, railLength * 0.5f, 0.07f), rail)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                for (float x = -railLength * 0.5f + 0.3f; x <= railLength * 0.5f - 0.29f; x += 2f)
                {
                    decor.Box("HandrailPost", cabin, new Vector3(x, y, 1.55f), new Vector3(0.06f, 0.06f, 0.26f), rail);
                }
            }
            for (float x = -half.x + 2f; x <= half.x - 1.99f; x += 3f)
            {
                decor.Box("CabinLight", cabin, new Vector3(x, half.y - 0.08f, 0.9f), new Vector3(1.6f, 0.06f, 0.5f), light);
            }
            // 船内の照明（ISS の船内は LED の照明で明るい。外の光は船体に遮られるので、中を照らす点の光を 2 つ。影は落とさない）。
            foreach (float x in new[] { -half.x * 0.5f, half.x * 0.5f })
            {
                var lamp = new GameObject("CabinLamp").AddComponent<Light>();
                lamp.transform.SetParent(cabin, false);
                lamp.transform.localPosition = new Vector3(x, half.y - 0.6f, -0.8f);
                lamp.type = LightType.Point;
                lamp.color = new Color(0.9f, 0.95f, 1f);
                lamp.range = 9f;
                lamp.intensity = 2.2f;
                lamp.shadows = LightShadows.None;
            }
            // 漂う物（ISS の映像でよく見る物）。
            Material bag = decor.Lit("CargoBag", new Color(0.88f, 0.87f, 0.82f));
            Material strap = decor.Lit("CargoStrap", new Color(0.18f, 0.24f, 0.4f));
            Material tool = decor.Lit("ToolSteel", new Color(0.62f, 0.64f, 0.68f));
            tool.SetFloat("_Metallic", 0.85f);
            tool.SetFloat("_Smoothness", 0.7f);
            Material foil = decor.Lit("FoodFoil", new Color(0.8f, 0.8f, 0.84f));
            foil.SetFloat("_Metallic", 0.9f);
            foil.SetFloat("_Smoothness", 0.8f);
            Material paper = decor.Lit("Checklist", new Color(0.95f, 0.95f, 0.92f));
            Material ink = decor.Lit("ChecklistInk", new Color(0.12f, 0.14f, 0.2f));
            Material pen = decor.Lit("Pen", new Color(0.1f, 0.2f, 0.65f));
            Material cable = decor.Lit("Cable", new Color(0.06f, 0.06f, 0.07f));
            var items = new List<Transform>();
            Transform Item(string name)
            {
                var item = new GameObject(name).transform;
                item.SetParent(cabin, false);
                item.localPosition = new Vector3(decor.Range(-half.x + 1.2f, half.x - 1.2f), decor.Range(-half.y + 1f, half.y - 1f), decor.Range(0.75f, 1.3f));
                item.localRotation = Quaternion.Euler(decor.Range(0f, 360f), decor.Range(0f, 360f), decor.Range(0f, 360f));
                items.Add(item);
                return item;
            }
            for (int i = 0; i < 2; i++)
            {
                Transform cargo = Item("CargoBag");
                decor.Box("Bag", cargo, Vector3.zero, new Vector3(0.9f, 0.56f, 0.5f), bag);
                decor.Box("Strap", cargo, Vector3.zero, new Vector3(0.92f, 0.08f, 0.52f), strap);
                decor.Box("Handle", cargo, new Vector3(0f, 0.31f, 0f), new Vector3(0.34f, 0.05f, 0.08f), strap);
            }
            Transform wrench = Item("Wrench");
            decor.Box("Handle", wrench, Vector3.zero, new Vector3(0.6f, 0.07f, 0.03f), tool);
            ProceduralMesh.Place("Head", wrench, Torus(meshes, "WrenchHead", 0.08f, 0.026f), new Vector3(0.33f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f),
                Vector3.one, tool);
            Transform penItem = Item("Pen");
            decor.Cylinder("Body", penItem, Vector3.zero, new Vector3(0.035f, 0.12f, 0.035f), pen);
            Transform checklist = Item("Checklist");
            decor.Box("Sheet", checklist, Vector3.zero, new Vector3(0.45f, 0.6f, 0.01f), paper);
            for (int line = 0; line < 4; line++)
            {
                decor.Box("Line", checklist, new Vector3(-0.03f, 0.18f - line * 0.1f, -0.008f), new Vector3(0.33f, 0.018f, 0.002f), ink);
            }
            Transform food = Item("FoodPacket");
            decor.Box("Packet", food, Vector3.zero, new Vector3(0.24f, 0.33f, 0.06f), foil);
            Transform loop = Item("CableBundle");
            ProceduralMesh.Place("Loop", loop, Torus(meshes, "CableLoop", 0.24f, 0.026f), Vector3.zero, Quaternion.identity, Vector3.one, cable);
            ProceduralMesh.Place("Loop", loop, Torus(meshes, "CableLoop", 0.24f, 0.026f), new Vector3(0.04f, 0f, 0.03f),
                Quaternion.Euler(0f, 25f, 0f), Vector3.one, cable);
            // 水の玉（大中小。後ろの景色を逆さに映す）。
            Material water = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WaterDrop", "Airerage/WaterDrop", m =>
            {
                m.SetFloat("_Invert", 1.6f);
            }, decor.Folder);
            var drops = new List<Transform>();
            foreach (float size in new[] { 0.8f, 0.55f, 0.32f })
            {
                Transform drop = decor.Sphere("WaterDrop", cabin, new Vector3(decor.Range(-half.x + 1.5f, half.x - 1.5f), decor.Range(-half.y + 1.2f, half.y - 1.2f),
                    decor.Range(0.75f, 1.2f)), Vector3.one * size, water).transform;
                drops.Add(drop);
            }
            cabin.gameObject.AddComponent<MicrogravityView>().Configure(items.ToArray(), drops.ToArray(), half - new Vector2(0.15f, 0.25f),
                new Vector2(0.7f, 1.35f));
            return cabin.gameObject;
        }

        // 輪（ドーナツ形。半径 radius、管の太さ tube。軸は Y）。
        private static Mesh Torus(string stage, string name, float radius, float tube)
        {
            const int steps = 16;
            var profile = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float angle = -Mathf.PI * 0.5f + i * Mathf.PI * 2f / steps;
                profile[i] = new Vector2(radius + Mathf.Cos(angle) * tube, Mathf.Sin(angle) * tube);
            }
            return ProceduralMesh.Lathe(stage, name, profile, 32, caps: false);
        }

        // コイルガン（StageZoneKind.CoilGun。2026-11-24 の要望。前は 2026-11-21 の電磁コイルの加速器）: 手本は多段のコイルガン（電磁石の
        // コイルを順に通電して、中の物を撃ち出すリニアモーター）。銅の巻き線のコイル 3 つ（内側の光る輪）を砲身の向きに並べ、
        // 上下と奥のレールでつなぎ、上のレールにコンデンサーの列とケーブル。コイルの内径が範囲の高さ。砲身ごと照準の向きへ回り、
        // ため具合でコイルが順に光り、撃ち出す先の軌道を線で見せる（CoilAcceleratorView）。
        private static GameObject CreateCoilAccelerator(Transform parent, ZoneSpec zone, Color color, StageDecor decor)
        {
            const string meshes = "SpaceStation";
            Vector2 axis = zone.Direction.sqrMagnitude > 0f ? zone.Direction.normalized : Vector2.right;
            float bore = zone.Size.y * 0.5f;
            const float tube = 0.2f;
            float length = zone.Size.x;
            var rig = new GameObject("CoilAccelerator").transform;
            rig.SetParent(parent, false);
            rig.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);
            Material copper = decor.Lit("CoilCopper", new Color(0.72f, 0.42f, 0.22f));
            copper.SetFloat("_Metallic", 0.9f);
            copper.SetFloat("_Smoothness", 0.6f);
            Material frame = decor.Lit("CoilFrame", new Color(0.22f, 0.24f, 0.28f));
            frame.SetFloat("_Metallic", 0.7f);
            frame.SetFloat("_Smoothness", 0.5f);
            Material capacitor = decor.Lit("CoilCapacitor", new Color(0.12f, 0.2f, 0.42f));
            capacitor.SetFloat("_Smoothness", 0.6f);
            Material cable = decor.Lit("CoilCable", new Color(0.05f, 0.05f, 0.06f));
            Quaternion ring = Quaternion.Euler(0f, 0f, 90f);
            float[] stations = { -length * 0.5f + 0.45f, 0f, length * 0.5f - 0.45f };
            var liners = new Renderer[stations.Length];
            var sparks = new ParticleSystem[stations.Length];
            for (int i = 0; i < stations.Length; i++)
            {
                var coil = new GameObject($"Coil{i + 1}").transform;
                coil.SetParent(rig, false);
                coil.localPosition = new Vector3(stations[i], 0f, 0f);
                // 巻き線（太い輪）と、両側の押さえの輪、内側の光る輪。
                ProceduralMesh.Place("Winding", coil, Torus(meshes, "CoilWinding", bore + tube, tube), Vector3.zero, ring, new Vector3(1f, 1.6f, 1f), copper);
                foreach (float side in new[] { -1f, 1f })
                {
                    ProceduralMesh.Place("Flange", coil, Torus(meshes, "CoilFlange", bore + tube, tube * 0.55f), new Vector3(side * tube * 1.5f, 0f, 0f), ring,
                        Vector3.one, frame);
                }
                Material liner = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("CoilLiner", "Airerage/Emissive", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.35f, 0.85f, 1f));
                    m.SetFloat("_Intensity", 0.4f);
                }, decor.Folder);
                liners[i] = ProceduralMesh.Place("Liner", coil, Torus(meshes, "CoilLiner", bore + 0.02f, 0.035f), Vector3.zero, ring, Vector3.one, liner)
                    .GetComponent<Renderer>();
                sparks[i] = new ParticleFx
                {
                    Name = "Sparks", LifetimeMin = 0.15f, LifetimeMax = 0.4f, SpeedMin = 4f, SpeedMax = 9f, SizeMin = 0.03f, SizeMax = 0.07f, ConeAngle = 80f,
                    ConeRadius = bore, Additive = true, Intensity = 4f, Stretch = 0.08f, MaxParticles = 80,
                    Colors = ParticleFx.Ramp(new[] { (new Color(0.75f, 0.95f, 1f), 0f), (new Color(0.3f, 0.6f, 1f), 1f) }, new[] { (1f, 0f), (0f, 1f) }),
                }.Build(coil, Vector3.zero, Quaternion.LookRotation(Vector3.right), decor.Folder);
            }
            // 上下と奥のレール（コイルの外側でつなぐ。手前には置かない）。
            float outer = bore + tube * 2.2f;
            foreach (Vector3 at in new[] { new Vector3(0f, outer, 0f), new Vector3(0f, -outer, 0f), new Vector3(0f, 0f, outer) })
            {
                decor.Box("Rail", rig, at, new Vector3(length + 0.5f, 0.16f, 0.16f), frame);
            }
            // コンデンサーの列（上のレールの上）と、コイルへのケーブル。
            for (int i = 0; i < stations.Length; i++)
            {
                float x = stations[i] + 0.35f;
                decor.Cylinder("Capacitor", rig, new Vector3(x, outer + 0.32f, 0.25f), new Vector3(0.26f, 0.22f, 0.26f), capacitor);
                decor.Cylinder("CapacitorTop", rig, new Vector3(x, outer + 0.55f, 0.25f), new Vector3(0.2f, 0.02f, 0.2f), frame);
                ProceduralMesh.Place("Cable", rig, ProceduralMesh.Tube(meshes, $"CoilCable{i}", new[]
                {
                    new Vector3(x, outer + 0.1f, 0.25f), new Vector3(x - 0.15f, outer + 0.02f, 0.3f), new Vector3(stations[i] + 0.05f, bore + tube * 1.8f, 0.2f),
                }, new[] { 0.025f, 0.025f, 0.025f }, 6), Vector3.zero, Quaternion.identity, Vector3.one, cable);
            }
            // 砲口の火花（撃ち出した瞬間。砲身の向きへ）と、撃ち出す先の軌道の線。
            ParticleSystem muzzle = new ParticleFx
            {
                Name = "MuzzleBurst", LifetimeMin = 0.2f, LifetimeMax = 0.5f, SpeedMin = 8f, SpeedMax = 18f, SizeMin = 0.04f, SizeMax = 0.1f, ConeAngle = 25f,
                ConeRadius = bore * 0.6f, Additive = true, Intensity = 5f, Stretch = 0.1f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (Color.white, 0f), (new Color(0.35f, 0.85f, 1f), 0.4f), (new Color(0.2f, 0.4f, 1f), 1f) },
                    new[] { (1f, 0f), (0f, 1f) }),
            }.Build(rig, new Vector3(length * 0.5f, 0f, 0f), Quaternion.LookRotation(Vector3.right), decor.Folder);
            var trajectory = new GameObject("Trajectory").AddComponent<LineRenderer>();
            trajectory.transform.SetParent(parent, false);
            trajectory.useWorldSpace = true;
            trajectory.sharedMaterial = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("CoilTrajectory", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 2f);
            }, decor.Folder);
            trajectory.widthMultiplier = 0.14f;
            trajectory.numCapVertices = 2;
            trajectory.shadowCastingMode = ShadowCastingMode.Off;
            trajectory.receiveShadows = false;
            trajectory.enabled = false;
            rig.gameObject.AddComponent<CoilAcceleratorView>().Configure(liners, sparks, muzzle, trajectory, length * 0.5f, axis, zone.Power,
                new Color(0.35f, 0.85f, 1f));
            return rig.gameObject;
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
        // ふくらみながら昇って薄れる）。湯気は注ぎ口の先（ZoneSpec.Source）から出し、判定の風の範囲も注ぎ口の先から上（2026-11-20 の指摘:
        // 湯気が地面から生えている）。湯気の幅も上で範囲の幅くらい。
        private static GameObject CreateKettleSteam(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            var visual = new GameObject("SteamPlume");
            visual.transform.SetParent(parent, false);
            var fx = new GameObject("SteamFx").transform;
            fx.SetParent(root, false);
            // 注ぎ口の先から、風の向き（注ぎ口の向きに少し傾けて上へ）に出す。
            Vector2 direction = zone.Direction.normalized;
            Quaternion up = Quaternion.LookRotation(new Vector3(direction.x, direction.y, 0f), Vector3.back);
            float speed = size.y / 1.6f;
            Vector3 spout = zone.LocalSource;
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
            // 塵が昇る高さを、運ばれる人が届く高さ（範囲の上の端 ＋ 上向きの速さの上限で飛び出す分）に合わせる（2026-11-24 の指摘: 見た目の
            // わりに飛ぶ高さが低い）。重力はキャラクターと同じ 30 m/s²。
            float reach = size.y + zone.Limit * zone.Limit / (2f * 30f);
            const float riseSpeed = 7f;
            float life = Mathf.Clamp(reach / riseSpeed, 1.2f, 4f);
            new ParticleFx
            {
                Name = "DustCore", Rate = 40f, LifetimeMin = life * 0.85f, LifetimeMax = life * 1.1f, SpeedMin = riseSpeed * 0.85f, SpeedMax = riseSpeed * 1.15f,
                SizeMin = 0.7f, SizeMax = 1.3f,
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
                Name = "Debris", Rate = 10f, LifetimeMin = life * 0.7f, LifetimeMax = life * 1.05f, SpeedMin = riseSpeed * 0.7f, SpeedMax = riseSpeed * 1.2f,
                SizeMin = 0.06f, SizeMax = 0.16f,
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

        // プラズマ球（いつも効く電気の危険 + ZoneLook.Plasma。2026-11-20 の方針: 手本はプラズマボール。ガラスの球の真ん中の電極から、
        // 気体が光る細い筋がガラスへ伸び、熱でゆらゆら昇る〈PlasmaGlobeView〉。色は封入した気体の色〈ネオンの赤紫と、アルゴンなどの
        // 青紫〉。黒い台に載る）。ガラス・ほのかに光る中の気体・電極・黒い台。判定はガラスの球（範囲の上の丸み CapHeight が球の上半分。
        // 2026-11-20 の要望: 触れるとダメージ・真ん中にワイヤーがつながる）。
        private static GameObject CreatePlasmaGlobe(Transform parent, ZoneSpec zone, StageDecor decor)
        {
            float radius = Mathf.Min(zone.Size.x, zone.Size.y) * 0.5f;
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
            Material metal = decor.Lit("PlasmaBase", new Color(0.05f, 0.05f, 0.06f));
            metal.SetFloat("_Smoothness", 0.75f);
            metal.SetFloat("_Metallic", 0.8f);
            // 黒い台（下ほど広い円錐台。ガラスの球の下の端が少し沈む）と、台の上の首。台の下の端は柱の上面（LabStage.PlasmaBaseHeight）。
            float neck = -radius * 0.92f;
            ProceduralMesh.Place("Base", globe, ProceduralMesh.Lathe("Lab", "PlasmaBase", new[]
            {
                new Vector2(0.62f, 0f), new Vector2(0.6f, 0.08f), new Vector2(0.48f, 0.3f), new Vector2(0.36f, 0.42f), new Vector2(0.3f, 0.45f),
                new Vector2(0f, 0.45f),
            }, 24), new Vector3(0f, neck - 0.45f, 0f), Quaternion.identity, Vector3.one, metal);
            decor.Cylinder("Stem", globe, new Vector3(0f, -radius * 0.55f, 0f), new Vector3(radius * 0.12f, radius * 0.38f, radius * 0.12f), metal);
            Material line = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("PlasmaFilament", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 3f);
                m.SetFloat("_Core", 1.5f);
            }, decor.Folder);
            globe.gameObject.AddComponent<PlasmaGlobeView>().Configure(line, radius * 0.95f, 8);
            return globe.gameObject;
        }

        // 転がる噴石（危険 + ZoneLook.MagmaRock。2026-11-20 の要望と方針: 奥の山から手前へ転がってくる。手本は、噴火で弾道を描いて飛ぶ
        // 火山弾〈USGS の用語集・UNDRR〉と、斜面を落ちる岩〈落石の研究: 自由落下・跳ね・転がり・滑りを組み合わせ、斜面に当たるたびに
        // 跳ね返る〉。噴石の見た目は USGS のパン皮状火山弾〈冷えた皮がひび割れ、割れ目の奥がまだ赤く光る。Airerage/MagmaRock〉）。
        // 動きは RollingBombView（予告で尾根の上に警告の記号と土煙 → 尾根の向こうから飛び出す → 斜面で跳ねて転がる → 跳ねて遊ぶ面を
        // 横切る）。判定は遊ぶ面を横切る所（柱の範囲。噴石の大きさ）だけ。奥の地面の高さ（ZoneSpec.Backdrop）を格子に焼いて渡す。
        // 返す物（効いている間だけ出す物）は空（噴石は予告の間も動くので、RollingBombView が出し入れする）。
        private static GameObject CreateRollingBomb(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            float radius = zone.Size.y * 0.5f;
            var crossing = new GameObject("BombCrossing");
            crossing.transform.SetParent(parent, false);
            var rig = new GameObject("RollingBombRig").transform;
            rig.SetParent(root, false);
            var bomb = new GameObject("Bomb").transform;
            bomb.SetParent(rig, false);
            Material crust = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("MagmaRock", "Airerage/MagmaRock", m =>
            {
                m.SetFloat("_CellScale", 1.6f);
                m.SetFloat("_CrackWidth", 0.12f);
            }, decor.Folder);
            GameObject body = decor.Sphere("Body", bomb, Vector3.zero, new Vector3(radius * 2f, radius * 1.74f, radius * 1.86f), crust);
            body.transform.localRotation = Quaternion.Euler(decor.Range(0f, 40f), decor.Range(0f, 360f), decor.Range(-20f, 20f));
            // 少しいびつな塊（こぶ）。
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = Random3(decor) * radius * 0.55f;
                decor.Sphere("Lump", bomb, at, Vector3.one * radius * decor.Range(0.9f, 1.2f), crust);
            }
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            ParticleSystem smoke = new ParticleFx
            {
                Name = "TrailSmoke", Rate = 30f, LifetimeMin = 1.2f, LifetimeMax = 2f, SpeedMin = 0.5f, SpeedMax = 1.5f, SizeMin = radius * 0.5f,
                SizeMax = radius * 0.8f, GrowFrom = 0.7f, GrowTo = 2.6f, ConeAngle = 40f, ConeRadius = radius * 0.6f, EdgeNoise = 0.6f, Softness = 0.9f,
                Turbulence = 0.6f, Spin = 30f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.32f, 0.24f, 0.2f), 0f), (new Color(0.2f, 0.18f, 0.18f), 1f) },
                    new[] { (0f, 0f), (0.55f, 0.12f), (0.3f, 0.6f), (0f, 1f) }),
            }.Build(rig, Vector3.zero, up, decor.Folder);
            ParticleSystem embers = new ParticleFx
            {
                Name = "TrailEmbers", Rate = 28f, LifetimeMin = 0.6f, LifetimeMax = 1.2f, SpeedMin = 1f, SpeedMax = 3f, SizeMin = 0.05f, SizeMax = 0.12f,
                Gravity = 1f, ConeAngle = 70f, ConeRadius = radius * 0.7f, Additive = true, Intensity = 3f, MaxParticles = 80,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.75f, 0.3f), 0f), (new Color(0.85f, 0.2f, 0.03f), 1f) }, new[] { (1f, 0f), (0f, 1f) }),
            }.Build(rig, Vector3.zero, up, decor.Folder);
            ParticleSystem dust = new ParticleFx
            {
                Name = "ImpactDust", LifetimeMin = 1f, LifetimeMax = 1.8f, SpeedMin = 3f, SpeedMax = 7f, SizeMin = radius * 0.5f, SizeMax = radius * 0.9f,
                GrowFrom = 0.6f, GrowTo = 2.4f, ConeAngle = 60f, ConeRadius = radius * 0.8f, EdgeNoise = 0.6f, Softness = 0.9f, Turbulence = 0.5f,
                Gravity = 0.15f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.4f, 0.32f, 0.28f), 0f), (new Color(0.26f, 0.22f, 0.21f), 1f) },
                    new[] { (0f, 0f), (0.7f, 0.08f), (0.35f, 0.6f), (0f, 1f) }),
            }.Build(rig, Vector3.zero, up, decor.Folder);
            ParticleSystem sparks = new ParticleFx
            {
                Name = "ImpactEmbers", LifetimeMin = 0.6f, LifetimeMax = 1.3f, SpeedMin = 4f, SpeedMax = 9f, SizeMin = 0.06f, SizeMax = 0.14f,
                Gravity = 1.5f, ConeAngle = 70f, ConeRadius = radius * 0.6f, Stretch = 0.02f, Additive = true, Intensity = 3.5f, MaxParticles = 120,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.8f, 0.35f), 0f), (new Color(0.9f, 0.25f, 0.04f), 1f) }, new[] { (1f, 0f), (0f, 1f) }),
            }.Build(rig, Vector3.zero, up, decor.Folder);
            ParticleSystem rumble = new ParticleFx
            {
                Name = "RidgeRumble", LifetimeMin = 1.5f, LifetimeMax = 2.5f, SpeedMin = 1.5f, SpeedMax = 3f, SizeMin = 1.6f, SizeMax = 2.6f, GrowFrom = 0.6f,
                GrowTo = 2.2f, ConeAngle = 25f, ConeRadius = 1.5f, EdgeNoise = 0.6f, Softness = 0.9f, Turbulence = 0.5f, MaxParticles = 60,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.36f, 0.28f, 0.24f), 0f), (new Color(0.24f, 0.21f, 0.2f), 1f) },
                    new[] { (0f, 0f), (0.6f, 0.15f), (0.3f, 0.6f), (0f, 1f) }),
            }.Build(rig, Vector3.zero, up, decor.Folder);
            // 予告が始まるまでは出さない（RollingBombView が出し入れする）。
            bomb.gameObject.SetActive(false);
            var view = rig.gameObject.AddComponent<RollingBombView>();
            view.Configure(root.GetComponent<StageZoneView>(), bomb, radius, new[] { smoke, embers }, new[] { dust, sparks }, rumble,
                AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat"));
            if (zone.Backdrop != null)
            {
                // 奥の地面の高さを 1m ごとの格子に焼く（柱の横の範囲と、奥行き 14〜74m）。
                const float step = 1f;
                float minX = zone.Center.x - zone.Size.x * 0.5f - 2f;
                const float minZ = 14f;
                int columns = Mathf.CeilToInt(zone.Size.x + 4f) + 1;
                const int rows = 61;
                var heights = new float[columns * rows];
                for (int j = 0; j < rows; j++)
                {
                    for (int i = 0; i < columns; i++)
                    {
                        heights[j * columns + i] = zone.Backdrop(minX + i * step, minZ + j * step);
                    }
                }
                view.ConfigureTerrain(heights, columns, rows, minX, minZ, step);
            }
            return crossing;
        }

        // トースターの動く物（周期のある発射台 + ZoneLook.Toaster。2026-11-20 の方針: 手本はポップアップ式のトースター〈レバーを下げると
        // ニクロム線の電熱線が赤く光ってパンを焼き、時間が来るとばねで台が跳ね上がってパンが飛び出す。HowStuffWorks・Wikipedia〉）。
        // 本体の模型は KitchenStage.Toaster（乗れる地形）。2 枚のパン、正面のレバー、溝の電熱線の光と溝の上の熱の光、電源の灯。
        // 予告（焼いている間）は光がだんだん強くなって熱気がゆらぎ、効き始めにパンが跳ね上がってパンくずが散る（ToasterView・
        // HazardEffectRig）。判定は溝の上に乗っている所（パンと一緒に跳ね上がる）。返す物（効いている間だけ出す物）は空。
        private static GameObject CreateToaster(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            var popped = new GameObject("ToastPop");
            popped.transform.SetParent(parent, false);
            var rig = new GameObject("ToasterRig").transform;
            rig.SetParent(root, false);
            Vector3 top = zone.LocalSource;
            float width = zone.Size.x + 0.4f;
            Material crust = decor.Lit("ToastCrust", new Color(0.42f, 0.22f, 0.08f));
            Material crumb = decor.Lit("ToastCrumb", new Color(0.86f, 0.6f, 0.3f));
            var toast = new Transform[2];
            float[] slots = { -0.45f, 0.45f };
            for (int i = 0; i < 2; i++)
            {
                var slice = new GameObject($"Toast{i + 1}").transform;
                slice.SetParent(rig, false);
                // 溝の中で焼いている位置（パンの上の端が溝の口の少し下）。
                slice.localPosition = new Vector3(0f, top.y - 0.83f, top.z + slots[i]);
                // 食パン: 四角い下と、山なりの上（皮は焼き色の濃い茶、中は小麦色）。
                decor.Box("Crust", slice, Vector3.zero, new Vector3(2.0f, 1.5f, 0.2f), crust);
                decor.Box("Crumb", slice, Vector3.zero, new Vector3(1.8f, 1.3f, 0.22f), crumb);
                decor.Cylinder("CrustTop", slice, new Vector3(0f, 0.72f, 0f), new Vector3(2.0f, 0.1f, 0.7f), crust).transform.localRotation =
                    Quaternion.Euler(90f, 0f, 0f);
                decor.Cylinder("CrumbTop", slice, new Vector3(0f, 0.68f, 0f), new Vector3(1.8f, 0.11f, 0.5f), crumb).transform.localRotation =
                    Quaternion.Euler(90f, 0f, 0f);
                toast[i] = slice;
            }
            Material black = decor.Lit("ToasterBlack", new Color(0.05f, 0.05f, 0.06f));
            var lever = new GameObject("Lever").transform;
            lever.SetParent(rig, false);
            lever.localPosition = new Vector3(width * 0.5f - 0.45f, top.y - 1.75f, top.z - 1.35f);
            decor.Box("Knob", lever, Vector3.zero, new Vector3(0.5f, 0.18f, 0.3f), black);
            Material element = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("ToasterElement", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(1f, 0.32f, 0.06f));
                m.SetFloat("_Intensity", 0f);
            }, decor.Folder);
            Material heat = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("ToasterHeat", "Airerage/SoftParticle", m =>
            {
                m.SetFloat("_Softness", 1f);
                m.SetFloat("_Noise", 0.25f);
                m.SetFloat("_Intensity", 0f);
                m.SetColor("_Tint", new Color(1f, 0.4f, 0.08f, 0.55f));
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            }, decor.Folder);
            var elements = new System.Collections.Generic.List<Renderer>();
            foreach (float z in slots)
            {
                elements.Add(decor.Box("Element", rig, new Vector3(0f, top.y + 0.03f, top.z + z), new Vector3(width - 0.8f, 0.02f, 0.12f), element)
                    .GetComponent<Renderer>());
            }
            elements.Add(decor.Quad("HeatGlow", rig, new Vector3(0f, top.y + 0.35f, top.z - 0.6f), new Vector2(width + 0.6f, 1.1f), heat).GetComponent<Renderer>());
            elements.Add(decor.Sphere("PowerLamp", rig, new Vector3(-width * 0.5f + 1.1f, top.y - 1.65f, top.z - 1.22f), Vector3.one * 0.12f, element)
                .GetComponent<Renderer>());
            rig.gameObject.AddComponent<ToasterView>().Configure(root.GetComponent<StageZoneView>(), toast, lever, elements.ToArray());
            // 焼いている間の熱気（ゆらぐ薄いもや）と、跳ね上がったときのパンくずと湯気。
            Quaternion up = Quaternion.Euler(-90f, 0f, 0f);
            var fx = new GameObject("ToasterFx").transform;
            fx.SetParent(root, false);
            ParticleSystem shimmer = new ParticleFx
            {
                Name = "Heat", LifetimeMin = 0.8f, LifetimeMax = 1.4f, SpeedMin = 1f, SpeedMax = 2f, SizeMin = 0.4f, SizeMax = 0.7f, GrowFrom = 0.6f,
                GrowTo = 2f, ConeAngle = 10f, ConeRadius = 0.9f, EdgeNoise = 0.5f, Softness = 0.95f, Turbulence = 0.6f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.85f, 0.7f), 0f), (new Color(0.9f, 0.88f, 0.85f), 1f) },
                    new[] { (0f, 0f), (0.12f, 0.25f), (0f, 1f) }),
            }.Build(fx, top + new Vector3(0f, 0.05f, 0f), up, decor.Folder);
            ParticleSystem crumbs = new ParticleFx
            {
                Name = "Crumbs", LifetimeMin = 0.6f, LifetimeMax = 1.1f, SpeedMin = 2f, SpeedMax = 5f, SizeMin = 0.04f, SizeMax = 0.09f, Gravity = 1f,
                ConeAngle = 35f, ConeRadius = 0.9f, Softness = 0.3f, MaxParticles = 60,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.7f, 0.45f, 0.2f), 0f), (new Color(0.5f, 0.3f, 0.12f), 1f) }, new[] { (1f, 0f), (1f, 0.8f), (0f, 1f) }),
            }.Build(fx, top + new Vector3(0f, 0.1f, -0.2f), up, decor.Folder);
            ParticleSystem puff = new ParticleFx
            {
                Name = "Puff", LifetimeMin = 0.8f, LifetimeMax = 1.3f, SpeedMin = 1.5f, SpeedMax = 3f, SizeMin = 0.4f, SizeMax = 0.7f, GrowFrom = 0.6f,
                GrowTo = 2.2f, ConeAngle = 20f, ConeRadius = 0.8f, EdgeNoise = 0.55f, Softness = 0.9f, Turbulence = 0.6f, MaxParticles = 30,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.95f, 0.93f, 0.9f), 0f), (new Color(0.85f, 0.84f, 0.82f), 1f) },
                    new[] { (0f, 0f), (0.3f, 0.12f), (0.15f, 0.6f), (0f, 1f) }),
            }.Build(fx, top + new Vector3(0f, 0.1f, -0.1f), up, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), null,
                new[] { shimmer }, new[] { 6f }, new[] { crumbs, puff }, new[] { 30, 8 }, new ParticleSystem[0], new float[0]);
            return popped;
        }

        // レーザーの網（危険 + ZoneLook.Laser。2026-11-20 の方針: レーザーの光は、きれいな空気の中では横から見えず、煙やちりの小さな粒が
        // 光を散らす所だけが見える〈チンダル現象。ブリタニカ・Wikipedia〉）。両端に、下のキャットウォークに立てた発光装置（暗い金属の箱と赤いレンズ）。
        // 効いている間は判定の高さに細い赤い光線を 3 本（返す物。StageZoneView が効いている間だけ出す）と、光線の上できらめくちり。予告の
        // 間は照準の弱い光線とレンズの点滅（LaserGridView）。判定は光線の帯（範囲の高さ）のまま。
        private static GameObject CreateLaserGrid(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 half = zone.Size * 0.5f;
            Material beam = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("LaserBeam", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", 4f);
                m.SetFloat("_Core", 1.6f);
            }, decor.Folder);
            // 効いている間の見た目は、StageZoneView が柱の幅に横へ引き伸ばす（PlaceOnColumn）ので、光線は幅 1（−0.5〜0.5）で作る
            // （範囲の幅で作ると、さらに幅の倍だけ伸びてステージの端から端まで出ていた。2026-11-21 の指摘）。
            var beams = new GameObject("LaserBeams");
            beams.transform.SetParent(parent, false);
            var lines = new LineRenderer[3];
            Color red = new Color(1f, 0.08f, 0.05f);
            for (int i = 0; i < lines.Length; i++)
            {
                float y = (i - 1) * half.y * 0.6f;
                lines[i] = Line($"Beam{i + 1}", beams.transform, beam, new Vector3(-0.5f, y, -0.2f), new Vector3(0.5f, y, -0.2f), red, 0.08f);
            }
            var rig = new GameObject("LaserRig").transform;
            rig.SetParent(root, false);
            LineRenderer aim = Line("AimBeam", rig, beam, new Vector3(-half.x, 0f, -0.2f), new Vector3(half.x, 0f, -0.2f), new Color(1f, 0.15f, 0.1f, 0.5f), 0.03f);
            aim.enabled = false;
            Material housing = decor.Lit("LaserHousing", new Color(0.12f, 0.13f, 0.16f));
            housing.SetFloat("_Metallic", 0.6f);
            housing.SetFloat("_Smoothness", 0.6f);
            Material lens = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("LaserLens", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(1f, 0.1f, 0.06f));
                m.SetFloat("_Intensity", 0.8f);
            }, decor.Folder);
            var lenses = new System.Collections.Generic.List<Renderer>();
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * (half.x + 0.4f);
                decor.Box("Emitter", rig, new Vector3(x, 0f, 0f), new Vector3(0.8f, 1.3f, 0.9f), housing);
                lenses.Add(decor.Box("Lens", rig, new Vector3(x - side * 0.41f, 0f, -0.1f), new Vector3(0.04f, 0.9f, 0.5f), lens).GetComponent<Renderer>());
                // 下のキャットウォーク（上面 y = 6.5）に立てた支柱（上には動く足場が通るので、下から支える）。
                float stand = zone.Center.y - 0.65f - 6.5f;
                if (stand > 0f)
                {
                    decor.Box("Stand", rig, new Vector3(x, -0.65f - stand * 0.5f, 0.2f), new Vector3(0.14f, stand, 0.14f), housing);
                    decor.Box("Foot", rig, new Vector3(x, -0.65f - stand + 0.04f, 0.2f), new Vector3(0.6f, 0.08f, 0.6f), housing);
                }
            }
            rig.gameObject.AddComponent<LaserGridView>().Configure(root.GetComponent<StageZoneView>(), lines, aim, lenses.ToArray());
            // 光線の上できらめくちり（効いている間だけ）。
            var fx = new GameObject("LaserFx").transform;
            fx.SetParent(root, false);
            ParticleSystem motes = new ParticleFx
            {
                Name = "Motes", LifetimeMin = 0.25f, LifetimeMax = 0.6f, SpeedMin = 0f, SpeedMax = 0.2f, SizeMin = 0.05f, SizeMax = 0.11f,
                BoxSize = new Vector3(zone.Size.x, zone.Size.y * 0.8f, 0.3f), Additive = true, Intensity = 3f, MaxParticles = 300,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.45f, 0.35f), 0f), (new Color(1f, 0.15f, 0.1f), 1f) }, new[] { (0f, 0f), (1f, 0.3f), (0f, 1f) }),
            }.Build(fx, new Vector3(0f, 0f, -0.2f), Quaternion.identity, decor.Folder);
            fx.gameObject.AddComponent<HazardEffectRig>().Configure(root.GetComponent<StageZoneView>(), null,
                new ParticleSystem[0], new float[0], new[] { motes }, new[] { 40 }, new[] { motes }, new[] { 220f });
            return beams;
        }

        // 部品の棒（from から to まで。太さ thickness の四角い棒）。
        private static GameObject Strut(StageDecor decor, string name, Transform parent, Vector3 from, Vector3 to, float thickness, Material material)
        {
            Vector3 along = to - from;
            GameObject bar = decor.Box(name, parent, (from + to) * 0.5f, new Vector3(thickness, along.magnitude, thickness), material);
            bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
            return bar;
        }

        // 輪の形の金具（縦の軸の周りの帯。内の半径 inner、外の半径 outer、高さ height。真ん中は抜けている）。
        private static void Band(string name, Transform parent, Vector3 center, float inner, float outer, float height, Material material)
        {
            Mesh mesh = ProceduralMesh.Lathe("Gimmicks", $"Band_{Mathf.RoundToInt(inner * 100f)}_{Mathf.RoundToInt(outer * 100f)}_{Mathf.RoundToInt(height * 100f)}", new[]
            {
                new Vector2(inner, -height * 0.5f), new Vector2(outer, -height * 0.5f), new Vector2(outer, height * 0.5f), new Vector2(inner, height * 0.5f),
                new Vector2(inner, -height * 0.5f),
            }, 32, caps: false);
            ProceduralMesh.Place(name, parent, mesh, center, Quaternion.identity, Vector3.one, material);
        }

        // 気送管（いつも吹く上向きの風 + ZoneLook.Pneumatic。2026-11-21 の方針: 実際の現象に寄せる。手本は病院・工場の気送管〈エアシューター〉:
        // 送風機が管の中の空気を押し、筒〈キャリア〉が管を勢いよく進み、受け口の手前で逆向きの空気でゆっくりになる〈気送管の特許の説明〉）。
        // 丸いガラスの管と継ぎ目の金具、下の口（入口）の床の送風機の羽根と格子、上の受け口のラッパ形の口、管の後ろの送風機の箱とダクト。
        // 管を昇る筒は遊ぶ面より奥（キャラクターを隠さない）。空気に乗って昇る塵と紙。当たり（風）は管の中（範囲のまま）。
        // 前は LabStage の飾りの四角いガラスの箱だった。
        private static GameObject CreatePneumaticTube(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            Vector2 size = zone.Size;
            float radius = size.x * 0.5f;
            float bottom = -size.y * 0.5f;
            float top = size.y * 0.5f;
            // 下の入口の高さ（LabStage の管の内側の壁の下の端。人はここから管に入る）。
            const float Intake = 2.6f;
            var tube = new GameObject("PneumaticTube").transform;
            tube.SetParent(parent, false);
            Material glass = decor.Transparent("TubeGlassRound", new Color(0.7f, 0.86f, 1f, 0.12f));
            Material metal = decor.Lit("TubeMetal", new Color(0.55f, 0.58f, 0.62f));
            metal.SetFloat("_Metallic", 0.8f);
            metal.SetFloat("_Smoothness", 0.7f);
            Material dark = decor.Lit("TubeDark", new Color(0.08f, 0.09f, 0.1f));
            dark.SetFloat("_Metallic", 0.5f);
            dark.SetFloat("_Smoothness", 0.5f);
            Material capColor = decor.Lit("CarrierCap", new Color(0.12f, 0.36f, 0.85f));
            Material felt = decor.Lit("CarrierFelt", new Color(0.22f, 0.22f, 0.24f));
            Material shell = decor.Transparent("CarrierShell", new Color(0.85f, 0.95f, 1f, 0.32f));
            Material paper = decor.Lit("CarrierPaper", new Color(0.92f, 0.9f, 0.84f));
            float glassBottom = bottom + Intake;
            float glassTop = top - 0.9f;
            decor.Cylinder("Glass", tube, new Vector3(0f, (glassBottom + glassTop) * 0.5f, 0f), new Vector3(radius * 2f, (glassTop - glassBottom) * 0.5f, radius * 2f), glass);
            // 継ぎ目の金具（5m ごと）と、入口の上の太い金具。
            for (float y = glassBottom + 5f; y < glassTop - 1f; y += 5f)
            {
                Band("Flange", tube, new Vector3(0f, y, 0f), radius - 0.02f, radius + 0.14f, 0.16f, metal);
            }
            Band("IntakeCollar", tube, new Vector3(0f, glassBottom, 0f), radius - 0.05f, radius + 0.2f, 0.32f, metal);
            // 上の受け口（ラッパ形に開いた口。厚みのある殻で、内側も見える。真ん中は抜けている）。
            ProceduralMesh.Place("BellMouth", tube, ProceduralMesh.Lathe("Gimmicks", "TubeBellMouth", new[]
            {
                new Vector2(radius - 0.04f, 0f), new Vector2(radius + 0.1f, 0f), new Vector2(radius + 0.16f, 0.35f), new Vector2(radius + 0.3f, 0.64f),
                new Vector2(radius + 0.48f, 0.88f), new Vector2(radius + 0.38f, 0.9f), new Vector2(radius + 0.2f, 0.68f), new Vector2(radius + 0.06f, 0.38f),
                new Vector2(radius - 0.04f, 0f),
            }, 32, caps: false), new Vector3(0f, glassTop, 0f), Quaternion.identity, Vector3.one, metal);
            // 床の送風機: ダクトの輪、回る羽根（PneumaticTubeView）、上の格子。
            Band("FanDuct", tube, new Vector3(0f, bottom + 0.09f, 0f), radius - 0.4f, radius - 0.15f, 0.18f, dark);
            var fan = new GameObject("Fan").transform;
            fan.SetParent(tube, false);
            fan.localPosition = new Vector3(0f, bottom + 0.1f, 0f);
            decor.Cylinder("Hub", fan, Vector3.zero, new Vector3(0.42f, 0.06f, 0.42f), metal);
            for (int i = 0; i < 5; i++)
            {
                var blade = new GameObject("Blade").transform;
                blade.SetParent(fan, false);
                blade.localRotation = Quaternion.Euler(0f, i * 72f, 0f);
                decor.Box("Vane", blade, new Vector3((radius - 0.45f) * 0.5f + 0.2f, 0f, 0f), new Vector3(radius - 0.6f, 0.03f, 0.34f), metal)
                    .transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            }
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 0.42f;
                float half = Mathf.Sqrt(Mathf.Max(0.01f, (radius - 0.2f) * (radius - 0.2f) - x * x));
                decor.Box("Grille", tube, new Vector3(x, bottom + 0.2f, 0f), new Vector3(0.04f, 0.04f, half * 2f), dark);
            }
            // 管の後ろの送風機の箱と、床へのダクト（遊ぶ面より奥）。
            decor.Box("Blower", tube, new Vector3(0f, bottom + 1.1f, radius + 1.3f), new Vector3(2.4f, 2.2f, 1.6f), dark);
            decor.Box("BlowerVent", tube, new Vector3(0f, bottom + 1.4f, radius + 0.48f), new Vector3(1.6f, 0.9f, 0.04f), metal);
            decor.Cylinder("BlowerDuct", tube, new Vector3(0f, bottom + 0.35f, radius + 0.25f), new Vector3(1.1f, 0.6f, 1.1f), metal)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // 昇る筒（キャリア）: 透明の胴、両端の青いふたとフェルトの帯、中に丸めた書類。管の奥側を昇る。
            var carriers = new Transform[3];
            for (int i = 0; i < carriers.Length; i++)
            {
                var carrier = new GameObject($"Carrier{i + 1}").transform;
                carrier.SetParent(tube, false);
                carrier.localPosition = new Vector3(0f, glassBottom, 0.85f);
                decor.Cylinder("Shell", carrier, Vector3.zero, new Vector3(0.84f, 0.6f, 0.84f), shell);
                decor.Cylinder("Papers", carrier, Vector3.zero, new Vector3(0.5f, 0.48f, 0.5f), paper);
                foreach (float end in new[] { -1f, 1f })
                {
                    decor.Cylinder("Cap", carrier, new Vector3(0f, end * 0.62f, 0f), new Vector3(0.88f, 0.07f, 0.88f), capColor);
                    decor.Cylinder("Felt", carrier, new Vector3(0f, end * 0.44f, 0f), new Vector3(0.9f, 0.04f, 0.9f), felt);
                }
                carriers[i] = carrier;
                carrier.gameObject.SetActive(false);
            }
            // 空気に乗って昇る塵と、ひらひら昇る紙。
            float span = glassTop - glassBottom;
            new ParticleFx
            {
                Name = "TubeDust", LifetimeMin = 0.6f, LifetimeMax = 1.2f, SizeMin = 0.04f, SizeMax = 0.1f, Stretch = 0.6f,
                BoxSize = new Vector3(radius * 1.4f, span * 0.95f, 1.6f), WorldVelocity = new Vector3(0f, 17f, 0f), VelocityJitter = 0.35f,
                Rate = 30f, MaxParticles = 120, Turbulence = 0.4f,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.95f, 0.97f, 1f), 0f), (new Color(0.85f, 0.9f, 1f), 1f) }, new[] { (0f, 0f), (0.55f, 0.2f), (0.35f, 0.8f), (0f, 1f) }),
            }.Build(tube, new Vector3(0f, (glassBottom + glassTop) * 0.5f, 0.3f), Quaternion.identity, decor.Folder);
            new ParticleFx
            {
                Name = "TubePaper", LifetimeMin = 2.4f, LifetimeMax = 3.2f, SizeMin = 0.18f, SizeMax = 0.3f, Spin = 420f, Softness = 0.08f,
                BoxSize = new Vector3(radius * 1.2f, 1.5f, 1.2f), WorldVelocity = new Vector3(0f, 11f, 0f), VelocityJitter = 0.3f,
                Rate = 1.6f, MaxParticles = 12, Turbulence = 1.4f,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.95f, 0.94f, 0.9f), 0f), (new Color(0.9f, 0.9f, 0.86f), 1f) }, new[] { (0f, 0f), (1f, 0.08f), (1f, 0.85f), (0f, 1f) }),
            }.Build(tube, new Vector3(0f, bottom + 1.8f, 0.5f), Quaternion.identity, decor.Folder);
            tube.gameObject.AddComponent<PneumaticTubeView>().Configure(root.GetComponent<StageZoneView>(), carriers, fan, glassBottom - 0.3f, glassTop - 0.7f);
            return tube.gameObject;
        }

        // かぼちゃの投石機（周期のある発射台 + ZoneLook.Catapult。2026-11-21 の方針: 汎用の発射台の見た目を変えるので、効果と配置も
        // 見た目に合わせる。前はいつも効く発射台〈台だけ〉だった）。手本はマンゴネル: ねじった綱の束（トーション）を巻いた軸、腕と先のかご、
        // 腕が当たって止まる緩衝材つきの横木〈横木の位置で放つ角度が決まる〉、腕を引き下ろす巻き上げ機と歯止め。動きは CatapultView。
        // 当たり（発射台）はかごの所（範囲のまま）。予告の間に引き絞り、効き始めに腕が跳ね上がる（StageZone の周期）。
        // 放る向き（範囲の向き）が左上なので、かごは軸の右、腕は左回りに振り上がる。右上へ放つ投石機は全体を左右に裏返す。
        private static GameObject CreateCatapult(Transform parent, Transform root, ZoneSpec zone, StageDecor decor)
        {
            var release = new GameObject("CatapultRelease");
            release.transform.SetParent(parent, false);
            var frame = new GameObject("Catapult").transform;
            frame.SetParent(parent, false);
            float side = zone.Direction.x <= 0f ? 1f : -1f;
            frame.localScale = new Vector3(side, 1f, 1f);
            float floor = -zone.Size.y * 0.5f;
            // 夜の霧の中でも形が読めるように、明るめの木と鉄（2026-11-21 の撮影で暗く沈んでいた）。
            Material wood = decor.Lit("CatapultWood", new Color(0.62f, 0.42f, 0.25f));
            Material woodDark = decor.Lit("CatapultWoodDark", new Color(0.42f, 0.28f, 0.17f));
            Material iron = decor.Lit("CatapultIron", new Color(0.32f, 0.32f, 0.35f));
            iron.SetFloat("_Metallic", 0.7f);
            iron.SetFloat("_Smoothness", 0.45f);
            Material rope = decor.Lit("CatapultRope", new Color(0.72f, 0.62f, 0.44f));
            Material pad = decor.Lit("CatapultPad", new Color(0.5f, 0.3f, 0.42f));
            const float Depth = 0.95f;
            var pivot = new Vector3(-2.25f, floor + 0.85f, 0f);
            var toBucket = new Vector2(2.25f, -0.7f);
            float armLength = toBucket.magnitude;
            float rest = Mathf.Atan2(toBucket.y, toBucket.x) * Mathf.Rad2Deg;
            const float Stop = 100f;
            var stopDirection = new Vector3(Mathf.Cos(Stop * Mathf.Deg2Rad), Mathf.Sin(Stop * Mathf.Deg2Rad), 0f);
            var swing = new Vector3(-stopDirection.y, stopDirection.x, 0f);
            Vector3 crossbar = pivot + stopDirection * 1.6f + swing * 0.26f;
            var drumAt = new Vector3(1.05f, floor + 0.55f, 0f);
            foreach (float z in new[] { -Depth, Depth })
            {
                var at = new Vector3(0f, 0f, z);
                decor.Box("Skid", frame, new Vector3(-1.2f, floor + 0.11f, z), new Vector3(5f, 0.22f, 0.24f), woodDark);
                // 軸を支える A の形の柱、横木を支える柱と筋交い、巻き上げ機の柱。
                Strut(decor, "Upright", frame, new Vector3(-3.1f, floor + 0.2f, z), pivot + new Vector3(0f, 0.14f, z), 0.2f, wood);
                Strut(decor, "Upright", frame, new Vector3(-1.4f, floor + 0.2f, z), pivot + new Vector3(0f, 0.14f, z), 0.2f, wood);
                Strut(decor, "Post", frame, new Vector3(-3.55f, floor + 0.2f, z), crossbar + at, 0.18f, wood);
                Strut(decor, "Brace", frame, pivot + new Vector3(-0.15f, 0.1f, z), crossbar + at + new Vector3(0.05f, -0.25f, 0f), 0.12f, woodDark);
                decor.Box("WinchPost", frame, new Vector3(drumAt.x, floor + 0.38f, z), new Vector3(0.18f, 0.52f, 0.18f), wood);
                // 継ぎ目の鉄の帯。
                decor.Box("Strap", frame, pivot + new Vector3(0f, 0.02f, z), new Vector3(0.34f, 0.34f, 0.26f), iron);
            }
            foreach (float x in new[] { -3.45f, -1.45f, 1.05f })
            {
                decor.Box("Tie", frame, new Vector3(x, floor + 0.13f, 0f), new Vector3(0.2f, 0.2f, Depth * 2f + 0.24f), woodDark);
            }
            // 軸と、ねじった綱の束（トーション）。
            decor.Cylinder("Axle", frame, pivot, new Vector3(0.2f, Depth + 0.16f, 0.2f), iron).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (float z in new[] { -0.5f, 0.5f })
            {
                decor.Cylinder("Skein", frame, pivot + new Vector3(0f, 0f, z), new Vector3(0.56f, 0.2f, 0.56f), rope).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                decor.Box("SkeinLever", frame, pivot + new Vector3(0f, 0f, z * 1.55f), new Vector3(0.9f, 0.1f, 0.1f), iron).transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            }
            // 横木と、真ん中の緩衝材（布の袋）。
            decor.Cylinder("Crossbar", frame, crossbar, new Vector3(0.24f, Depth + 0.1f, 0.24f), wood).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            decor.Cylinder("Cushion", frame, crossbar, new Vector3(0.44f, 0.36f, 0.44f), pad).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // 腕とかご（CatapultView が軸の周りに回す）。
            var arm = new GameObject("Arm").transform;
            arm.SetParent(frame, false);
            arm.localPosition = pivot;
            arm.localRotation = Quaternion.Euler(0f, 0f, rest);
            decor.Box("Beam", arm, new Vector3((armLength - 0.6f) * 0.5f, 0f, 0f), new Vector3(armLength + 0.1f, 0.2f, 0.26f), wood);
            foreach (float x in new[] { 0.45f, armLength * 0.62f })
            {
                decor.Box("Band", arm, new Vector3(x, 0f, 0f), new Vector3(0.08f, 0.24f, 0.3f), iron);
            }
            var bucket = new Vector3(armLength, 0.12f, 0f);
            decor.Box("BucketBase", arm, bucket, new Vector3(1.1f, 0.1f, 0.9f), woodDark);
            foreach (float z in new[] { -0.45f, 0.45f })
            {
                decor.Box("BucketSide", arm, bucket + new Vector3(0f, 0.2f, z), new Vector3(1.1f, 0.42f, 0.06f), wood);
            }
            foreach (float x in new[] { -0.55f, 0.55f })
            {
                decor.Box("BucketEnd", arm, bucket + new Vector3(x, 0.2f, 0f), new Vector3(0.06f, 0.42f, 0.9f), wood);
            }
            var hook = new GameObject("RopeHook").transform;
            hook.SetParent(arm, false);
            hook.localPosition = new Vector3(armLength - 0.4f, -0.12f, 0f);
            // かごのかぼちゃ（腕の子）と、放ったかぼちゃ（CatapultView がワールドで飛ばす）。
            var payload = new GameObject("Payload").transform;
            payload.SetParent(arm, false);
            payload.localPosition = bucket + new Vector3(0f, 0.08f, 0f);
            var thrown = new GameObject("ThrownPumpkin").transform;
            thrown.SetParent(frame, false);
            foreach (Transform holder in new[] { payload, thrown })
            {
                GameObject pumpkin = ThirdPartyProps.Place(decor, ThirdPartyProps.Halloween, "pumpkin_orange_small", holder.position, 1.3f, 30f,
                    Color.white, decor.Folder);
                if (pumpkin != null)
                {
                    pumpkin.transform.SetParent(holder, true);
                    pumpkin.transform.localPosition = Vector3.zero;
                }
            }
            // 巻き上げ機（胴・綱の巻き・手回しの輪の輻）と、腕へ掛けた綱。
            var drum = new GameObject("Drum").transform;
            drum.SetParent(frame, false);
            drum.localPosition = drumAt;
            decor.Cylinder("DrumCore", drum, Vector3.zero, new Vector3(0.36f, Depth, 0.36f), wood).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            decor.Cylinder("RopeWrap", drum, Vector3.zero, new Vector3(0.44f, 0.34f, 0.44f), rope).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (float z in new[] { -Depth - 0.08f, Depth + 0.08f })
            {
                for (int k = 0; k < 4; k++)
                {
                    decor.Box("Spoke", drum, new Vector3(0f, 0f, z), new Vector3(0.95f, 0.06f, 0.06f), wood).transform.localRotation = Quaternion.Euler(0f, 0f, k * 45f);
                }
                decor.Cylinder("Ratchet", drum, new Vector3(0f, 0f, z * 0.92f), new Vector3(0.5f, 0.03f, 0.5f), iron).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var ropeLine = new GameObject("WinchRope").AddComponent<LineRenderer>();
            ropeLine.transform.SetParent(frame, false);
            ropeLine.sharedMaterial = decor.Unlit("CatapultRopeLine", new Color(0.55f, 0.46f, 0.32f));
            ropeLine.useWorldSpace = true;
            ropeLine.positionCount = 8;
            ropeLine.startWidth = ropeLine.endWidth = 0.05f;
            ropeLine.shadowCastingMode = ShadowCastingMode.Off;
            ropeLine.receiveShadows = false;
            for (int i = 0; i < 8; i++)
            {
                ropeLine.SetPosition(i, Vector3.Lerp(drum.position, hook.position, i / 7f));
            }
            frame.gameObject.AddComponent<CatapultView>().Configure(root.GetComponent<StageZoneView>(), arm, rest, Stop, drum, payload, thrown,
                new Vector3(-5f * side, 21f, 15f), ropeLine, hook);
            return release;
        }

        // ドーナツフライヤー（StageTrapKind.DonutFryer。2026-11-24 の要望）: 左の冷蔵庫の上に置いた業務用のフライヤー（ステンレスの油槽・
        // 揚げ油・生地を落とすホッパー・操作盤・ドーナツが滑り出るシュート）。手本は、生地を輪の形に押し出して油に落とし、揚がったら
        // シュートへ送り出すドーナツフライヤー。出ているドーナツ（揚げ色の生地・アイシング・トッピング）は DonutFryerView が道のりに
        // 沿って動かす（転がる・着地でつぶれる）。物体の位置がシュートの出口。
        private static void CreateDonutFryer(Transform trap, StageTrapAuthoring authoring, TrapSpec spec, StageDecor decor)
        {
            const string meshes = "Kitchen";
            Material steel = decor.Lit("FryerSteel", new Color(0.78f, 0.8f, 0.84f));
            steel.SetFloat("_Metallic", 0.85f);
            steel.SetFloat("_Smoothness", 0.72f);
            Material dark = decor.Lit("FryerDark", new Color(0.18f, 0.19f, 0.22f));
            Material oil = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("FryerOil", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(0.95f, 0.66f, 0.18f));
                m.SetFloat("_Intensity", 0.9f);
            }, decor.Folder);
            Material lamp = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("FryerLamp", "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", new Color(1f, 0.35f, 0.2f));
                m.SetFloat("_Intensity", 2.2f);
            }, decor.Folder);
            // 冷蔵庫の上（上の面 y ≈ 1.1、手前の面 z ≈ 1.3）に、シュートの出口から見て左奥に置く。
            var fryer = new GameObject("Fryer").transform;
            fryer.SetParent(trap, false);
            fryer.localPosition = new Vector3(-3.2f, -2.2f, 2.6f);
            decor.Box("Body", fryer, new Vector3(0f, 1.05f, 0f), new Vector3(4.4f, 2.1f, 2.4f), steel);
            decor.Box("Base", fryer, new Vector3(0f, 0.08f, 0f), new Vector3(4.6f, 0.16f, 2.6f), dark);
            decor.Box("Rim", fryer, new Vector3(0f, 2.14f, 0f), new Vector3(4.5f, 0.1f, 2.5f), steel);
            decor.Box("Oil", fryer, new Vector3(0f, 2.06f, 0f), new Vector3(4.1f, 0.06f, 2.1f), oil);
            // 生地を落とすホッパー（逆さの円錐）と、操作盤のランプ。
            ProceduralMesh.Place("Hopper", fryer, ProceduralMesh.Lathe(meshes, "FryerHopper", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.75f, 0.9f), new Vector2(0.78f, 1.25f), new Vector2(0f, 1.25f),
            }, 24), new Vector3(-0.9f, 2.5f, 0f), Quaternion.identity, Vector3.one, steel);
            decor.Box("Panel", fryer, new Vector3(0f, 1.1f, -1.22f), new Vector3(2.4f, 0.9f, 0.06f), dark);
            for (int i = 0; i < 3; i++)
            {
                decor.Box("Lamp", fryer, new Vector3(-0.7f + i * 0.7f, 1.15f, -1.27f), new Vector3(0.22f, 0.22f, 0.04f), lamp);
            }
            // シュート（油槽の右の縁から、出口へ下りながら手前へ）。
            GameObject chute = decor.Box("Chute", trap, new Vector3(-0.9f, -0.15f, 1.3f), new Vector3(2.6f, 0.1f, 2.4f), steel);
            chute.transform.localRotation = Quaternion.Euler(-25f, 0f, -12f);
            // 油の泡と、シュートから飛ぶ油のしずく、立ちのぼる湯気。
            ParticleSystem bubbles = new ParticleFx
            {
                Name = "OilBubbles", Rate = 8f, LifetimeMin = 0.3f, LifetimeMax = 0.6f, SpeedMin = 0.2f, SpeedMax = 0.6f, SizeMin = 0.06f, SizeMax = 0.16f,
                BoxSize = new Vector3(3.8f, 0.05f, 1.8f), GrowFrom = 0.4f, GrowTo = 1.2f, Softness = 0.2f, MaxParticles = 80,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.9f, 0.6f), 0f), (new Color(1f, 0.95f, 0.8f), 1f) }, new[] { (0.8f, 0f), (0f, 1f) }),
            }.Build(fryer, new Vector3(0f, 2.1f, 0f), Quaternion.Euler(-90f, 0f, 0f), decor.Folder);
            new ParticleFx
            {
                Name = "FryerSteam", Rate = 5f, LifetimeMin = 1.6f, LifetimeMax = 2.4f, SpeedMin = 0.6f, SpeedMax = 1.2f, SizeMin = 0.6f, SizeMax = 1f,
                GrowFrom = 0.6f, GrowTo = 2.2f, ConeAngle = 12f, ConeRadius = 1.4f, EdgeNoise = 0.6f, Softness = 0.95f, Turbulence = 0.4f, MaxParticles = 30,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 1f, 1f), 0f), (new Color(0.95f, 0.95f, 0.97f), 1f) },
                    new[] { (0f, 0f), (0.22f, 0.25f), (0f, 1f) }),
            }.Build(fryer, new Vector3(0.4f, 2.2f, 0f), Quaternion.Euler(-90f, 0f, 0f), decor.Folder);
            ParticleSystem drips = new ParticleFx
            {
                Name = "OilDrips", LifetimeMin = 0.4f, LifetimeMax = 0.8f, SpeedMin = 2f, SpeedMax = 4f, SizeMin = 0.06f, SizeMax = 0.12f, Gravity = 1f,
                ConeAngle = 35f, ConeRadius = 0.4f, Softness = 0.3f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (new Color(1f, 0.8f, 0.3f), 0f), (new Color(0.95f, 0.65f, 0.15f), 1f) }, new[] { (1f, 0f), (0f, 1f) }),
            }.Build(trap, Vector3.zero, Quaternion.Euler(-60f, 90f, 0f), decor.Folder);
            // ドーナツ（番号ごと。揚げ色の生地の輪・前の面のアイシング・トッピング）。穴をカメラへ向けて転がる。
            StageTrap core = authoring.ToTrap();
            float radius = spec.Radius;
            Mesh dough = Torus(meshes, "DonutDough", radius * 0.62f, radius * 0.38f);
            Mesh icing = Torus(meshes, "DonutIcing", radius * 0.62f, radius * 0.385f);
            Material doughMaterial = decor.Lit("DonutDough", new Color(0.82f, 0.52f, 0.24f));
            doughMaterial.SetFloat("_Smoothness", 0.35f);
            Color[] glazes = { new Color(1f, 0.55f, 0.75f), new Color(0.36f, 0.19f, 0.1f), new Color(0.98f, 0.95f, 0.9f), new Color(0.56f, 0.76f, 0.36f) };
            Color[] sprinkleColors = { new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.75f, 1f), new Color(1f, 0.35f, 0.4f), new Color(0.6f, 1f, 0.5f), Color.white };
            var donuts = new Transform[core.DonutSlots];
            for (int i = 0; i < donuts.Length; i++)
            {
                var donut = new GameObject($"Donut{i + 1}").transform;
                donut.SetParent(trap, false);
                Quaternion faceCamera = Quaternion.Euler(90f, 0f, 0f);
                ProceduralMesh.Place("Dough", donut, dough, Vector3.zero, faceCamera, Vector3.one, doughMaterial);
                Material glaze = decor.Lit($"DonutGlaze{i % glazes.Length}", glazes[i % glazes.Length]);
                glaze.SetFloat("_Smoothness", 0.8f);
                // アイシングは前の面だけ（奥行きを薄くして手前へずらす）。
                ProceduralMesh.Place("Icing", donut, icing, new Vector3(0f, 0f, -radius * 0.12f), faceCamera, new Vector3(1f, 0.55f, 1f), glaze);
                if (i % glazes.Length != 1)
                {
                    for (int k = 0; k < 14; k++)
                    {
                        float angle = k * Mathf.PI * 2f / 14f + decor.Range(-0.15f, 0.15f);
                        float at = radius * decor.Range(0.42f, 0.82f);
                        GameObject sprinkle = decor.Box("Sprinkle", donut, new Vector3(Mathf.Cos(angle) * at, Mathf.Sin(angle) * at, -radius * 0.38f),
                            new Vector3(radius * 0.16f, radius * 0.05f, radius * 0.05f), decor.Lit($"Sprinkle{k % sprinkleColors.Length}", sprinkleColors[k % sprinkleColors.Length]));
                        sprinkle.transform.localRotation = Quaternion.Euler(0f, 0f, decor.Range(0f, 180f));
                    }
                }
                donut.gameObject.SetActive(false);
                donuts[i] = donut;
            }
            trap.gameObject.AddComponent<DonutFryerView>().Configure(donuts, bubbles, drips);
        }

        // 鬼火の輪（ステージの仕掛け StageTrapKind.WispRing。2026-11-21 の要望: 1 つだけ、6 地点の間を速く飛び回り、くぐった人の周りを
        // 回ってコインを渡し、上空へ消えてしばらくしてまた現れる。前は 2 つの加速リングだった）。鬼火は墓地や沼の地面から数メートルの所に浮かぶ
        // 小さな青白い火の玉で、煙を出さず、ゆらゆら漂う〈記録と伝承のまとめ〉。輪の形に連なる鬼火（WispRingView が動かす）、飛んだ跡に残る
        // 鬼火の粒、くぐられたときに散る粒。当たり（くぐる）は輪の大きさ（Core の StageTraps.StepWispRings）。
        private static void CreateWispRingTrap(Transform trap, TrapSpec spec, StageDecor decor)
        {
            var ring = new GameObject("Ring").transform;
            ring.SetParent(trap, false);
            Mesh card = ProceduralMesh.GlowCard("Gimmicks", "GlowCard");
            Material flame = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WispRingFlame", "Airerage/Wisp", m =>
            {
                m.SetColor("_Color", new Color(0.9f, 1.7f, 1.4f));
                m.SetFloat("_Core", 7f);
            }, decor.Folder);
            var radii = new Vector2(spec.RingSize.x * 0.5f + 0.15f, spec.RingSize.y * 0.5f + 0.15f);
            const int Count = 9;
            var wisps = new Transform[Count];
            for (int i = 0; i < Count; i++)
            {
                float angle = i * Mathf.PI * 2f / Count;
                GameObject wisp = ProceduralMesh.Place($"Wisp{i + 1}", ring, card, new Vector3(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y, 0f),
                    Quaternion.identity, Vector3.one * (0.62f + 0.18f * Mathf.Sin(i * 2.7f)), flame);
                wisp.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                wisp.GetComponent<Renderer>().receiveShadows = false;
                wisps[i] = wisp.transform;
            }
            Gradient pale = ParticleFx.Ramp(new[] { (new Color(0.6f, 1f, 0.95f), 0f), (new Color(0.45f, 0.75f, 1f), 1f) }, new[] { (0f, 0f), (0.8f, 0.25f), (0f, 1f) });
            ParticleSystem trail = new ParticleFx
            {
                Name = "WispTrail", LifetimeMin = 0.6f, LifetimeMax = 1.2f, SpeedMin = 0f, SpeedMax = 0.4f, SizeMin = 0.12f, SizeMax = 0.3f, Additive = true,
                Intensity = 2.4f, BoxSize = new Vector3(radii.x * 1.6f, radii.y * 1.6f, 0.4f), Rate = 34f, MaxParticles = 80, Turbulence = 0.5f, Colors = pale,
            }.Build(ring, new Vector3(0f, 0f, -0.1f), Quaternion.identity, decor.Folder);
            ParticleSystem.MainModule trailMain = trail.main;
            trailMain.prewarm = false;
            ParticleSystem burst = new ParticleFx
            {
                Name = "WispBurst", LifetimeMin = 0.5f, LifetimeMax = 1.0f, SpeedMin = 0f, SpeedMax = 0f, SizeMin = 0.14f, SizeMax = 0.34f, Additive = true,
                Intensity = 3f, BoxSize = new Vector3(0.6f, 0.6f, 0.1f), Radial = -6f, MaxParticles = 60, Turbulence = 0.8f, Colors = pale,
            }.Build(trap, new Vector3(0f, 0f, -0.2f), Quaternion.identity, decor.Folder);
            trap.gameObject.AddComponent<WispRingView>().Configure(ring, wisps, radii, burst, trail);
        }

        // 荷役の網（動く足場 StagePlatformKind.Hoist の見た目。2026-11-21 の要望: ワイヤーがつながり、ぶら下がると吊り具が伸びて網が下がり
        // 〈バンジーのように少し跳ね返る〉、外すとゆっくり戻る。前は跳ね返す物〈光る板〉→ 吊った網だった）。手本は港の荷役の網〈カーゴネット〉:
        // 四隅の輪を 4 本足の吊り具でクレーンの鉤に掛け、吊り具の下に枠〈スプレッダー〉を入れると網が張ったまま広がる。落ちてきた物を受けた網は
        // 深く沈んでから跳ね返す。クレーンの梁の下のトロリー（動かない。beamBottom）から伸びるワイヤー、滑車つきの鉤のブロック、4 本足の吊り具、
        // 鉄の枠、縦横の綱の網目。visual（足場と一緒に動く）には鉤から下を入れ、トロリーは足場の物体（動かない）に置く。ワイヤーの長さと、
        // 網目の沈み・吊り具の揺れは CargoNetView。
        internal static void BuildCargoNet(StageDecor decor, Transform visual, PlatformSpec spec, float beamBottom)
        {
            var half = new Vector2(spec.Size.x * 0.5f + 0.1f, 1.1f);
            float netY = spec.Size.y * 0.5f - 0.15f;
            const float HookAbove = 3.9f;
            var hoist = new GameObject("Hoist").transform;
            hoist.SetParent(visual, false);
            hoist.localPosition = new Vector3(0f, netY + HookAbove, 0f);
            Material steel = decor.Lit("NetSteel", new Color(0.86f, 0.62f, 0.12f));
            steel.SetFloat("_Metallic", 0.6f);
            steel.SetFloat("_Smoothness", 0.5f);
            Material dark = decor.Lit("NetHookDark", new Color(0.12f, 0.12f, 0.13f));
            dark.SetFloat("_Metallic", 0.7f);
            Material cable = decor.Unlit("NetCable", new Color(0.18f, 0.18f, 0.2f));
            Material ropeMat = decor.Unlit("NetRope", new Color(0.16f, 0.4f, 0.26f));
            Material edgeMat = decor.Unlit("NetEdgeRope", new Color(0.12f, 0.3f, 0.2f));
            // 梁の下のトロリー（動かない。足場の物体の子）と、そこから鉤のブロックへ伸びるワイヤー（CargoNetView が両端を合わせる）。
            Transform anchorRoot = visual.parent;
            var trolley = decor.Box("Trolley", anchorRoot, new Vector3(0f, beamBottom - spec.Center.y - 0.25f, 0f), new Vector3(1.2f, 0.5f, 1.4f), dark).transform;
            var cableLine = new GameObject("HoistCable").AddComponent<LineRenderer>();
            cableLine.transform.SetParent(anchorRoot, false);
            cableLine.sharedMaterial = cable;
            cableLine.useWorldSpace = true;
            cableLine.positionCount = 2;
            cableLine.SetPosition(0, trolley.position + Vector3.down * 0.25f);
            cableLine.SetPosition(1, hoist.position + Vector3.up * 0.9f);
            cableLine.startWidth = cableLine.endWidth = 0.07f;
            cableLine.shadowCastingMode = ShadowCastingMode.Off;
            // 滑車のついた鉤のブロックと、J の形の鉤。
            decor.Box("HookBlock", hoist, new Vector3(0f, 0.75f, 0f), new Vector3(0.6f, 0.7f, 0.42f), steel);
            decor.Cylinder("Sheave", hoist, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.24f, 0.5f), dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Mesh hookMesh = ProceduralMesh.Tube("Gimmicks", "CraneHook", new[]
            {
                new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.1f, 0f), new Vector3(0.05f, -0.12f, 0f), new Vector3(0.22f, -0.2f, 0f), new Vector3(0.3f, -0.05f, 0f),
            }, new[] { 0.08f, 0.09f, 0.085f, 0.07f, 0.05f }, 8);
            ProceduralMesh.Place("Hook", hoist, hookMesh, Vector3.zero, Quaternion.identity, Vector3.one, dark);
            decor.Sphere("MasterLink", hoist, new Vector3(0.05f, -0.15f, 0f), new Vector3(0.22f, 0.3f, 0.08f), dark);
            // 鉄の枠（網の縁）と、四隅から鉤への 4 本足の吊り具。
            float frameY = -HookAbove;
            var corners = new[] { new Vector3(-half.x, frameY, -half.y), new Vector3(half.x, frameY, -half.y), new Vector3(half.x, frameY, half.y), new Vector3(-half.x, frameY, half.y) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % 4];
                Strut(decor, "Spreader", hoist, a, b, 0.1f, steel);
                decor.Sphere("Shackle", hoist, a + new Vector3(0f, 0.08f, 0f), Vector3.one * 0.16f, dark);
                var leg = new GameObject("SlingLeg").AddComponent<LineRenderer>();
                leg.transform.SetParent(hoist, false);
                leg.sharedMaterial = cable;
                leg.useWorldSpace = false;
                leg.positionCount = 2;
                leg.SetPosition(0, a + new Vector3(0f, 0.1f, 0f));
                leg.SetPosition(1, new Vector3(0.05f, -0.2f, 0f));
                leg.startWidth = leg.endWidth = 0.045f;
                leg.shadowCastingMode = ShadowCastingMode.Off;
            }
            // 網目（縦横の綱。CargoNetView が点を沈める）。網の面は枠と同じ高さ。
            var net = new GameObject("Net").transform;
            net.SetParent(hoist, false);
            net.localPosition = new Vector3(0f, frameY, 0f);
            var alongX = new List<LineRenderer>();
            var alongZ = new List<LineRenderer>();
            const int Rows = 7;
            const int Columns = 11;
            for (int r = 0; r < Rows; r++)
            {
                float z = Mathf.Lerp(-half.y, half.y, r / (float)(Rows - 1));
                alongX.Add(NetRope($"RopeX{r + 1}", net, r == 0 || r == Rows - 1 ? edgeMat : ropeMat, 16, i => new Vector3(Mathf.Lerp(-half.x, half.x, i / 15f), 0f, z),
                    r == 0 || r == Rows - 1 ? 0.06f : 0.035f));
            }
            for (int c = 0; c < Columns; c++)
            {
                float x = Mathf.Lerp(-half.x, half.x, c / (float)(Columns - 1));
                alongZ.Add(NetRope($"RopeZ{c + 1}", net, c == 0 || c == Columns - 1 ? edgeMat : ropeMat, 9, i => new Vector3(x, 0f, Mathf.Lerp(-half.y, half.y, i / 8f)),
                    c == 0 || c == Columns - 1 ? 0.06f : 0.035f));
            }
            visual.gameObject.AddComponent<CargoNetView>().Configure(alongX.ToArray(), alongZ.ToArray(), half, 0f, hoist, cableLine, trolley);
        }

        private static LineRenderer NetRope(string name, Transform parent, Material material, int count, Func<int, Vector3> point, float width)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                line.SetPosition(i, point(i));
            }
            line.startWidth = line.endWidth = width;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static LineRenderer Line(string name, Transform parent, Material material, Vector3 from, Vector3 to, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        // 単位球の中のばらばらな向き（飾りの決まった乱数）。
        private static Vector3 Random3(StageDecor decor)
        {
            return new Vector3(decor.Range(-1f, 1f), decor.Range(-0.6f, 0.6f), decor.Range(-1f, 1f)).normalized;
        }

        // 噴気孔（いつも吹く風 + ZoneLook.Steam。2026-11-20 の方針: 手本は USGS の噴気孔と、圧力の高い噴気〈ごうごうと音を立てて噴く〉の
        // 映像。地面の穴から白い湯気〈ほとんど水蒸気〉が勢いよく噴き上がる。口のすぐ外は透明に近く〈まだ冷えて白くなっていない〉、
        // 少し上で白い塊になってふくらみ、上で渦を巻いてゆっくり広がる。口のまわりの岩に硫黄の黄色い結晶が付く）。黒い口・まわりの岩・
        // 黄色い硫黄・勢いよく噴く湯気（柱の上まで）・上でたなびく湯気。判定は湯気の柱の範囲（とても強い風。2026-11-20 の要望）。
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
            // 勢いよく噴く湯気: 柱の高さを 1 秒ほどで昇る（風の範囲の上まで）。
            float jet = size.y / 1.05f;
            new ParticleFx
            {
                Name = "Steam", Rate = 75f, LifetimeMin = 0.9f, LifetimeMax = 1.35f, SpeedMin = jet * 0.8f, SpeedMax = jet * 1.15f, SizeMin = 0.35f,
                SizeMax = 0.6f, GrowFrom = 0.5f, GrowTo = 4.2f, ConeAngle = 6f, ConeRadius = size.x * 0.16f, EdgeNoise = 0.55f, Softness = 0.9f,
                Turbulence = 1f, Spin = 30f, MaxParticles = 220,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.95f, 0.95f, 0.93f), 0f), (new Color(0.85f, 0.85f, 0.84f), 1f) },
                    new[] { (0f, 0f), (0.04f, 0.07f), (0.5f, 0.22f), (0.28f, 0.65f), (0f, 1f) }),
            }.Build(vent, new Vector3(0f, floor + 0.1f, 0.3f), along, decor.Folder);
            // 柱の上でゆっくり広がってたなびく湯気。
            new ParticleFx
            {
                Name = "Billow", Rate = 9f, LifetimeMin = 1.6f, LifetimeMax = 2.4f, SpeedMin = 0.8f, SpeedMax = 1.8f, SizeMin = 1.2f, SizeMax = 2f,
                GrowFrom = 0.7f, GrowTo = 2.2f, ConeAngle = 35f, ConeRadius = size.x * 0.4f, EdgeNoise = 0.6f, Softness = 0.92f, Turbulence = 0.6f,
                Spin = 20f, MaxParticles = 40,
                Colors = ParticleFx.Ramp(new[] { (new Color(0.9f, 0.9f, 0.88f), 0f), (new Color(0.78f, 0.77f, 0.76f), 1f) },
                    new[] { (0f, 0f), (0.22f, 0.25f), (0.12f, 0.6f), (0f, 1f) }),
            }.Build(vent, new Vector3(direction.x * size.y * 0.75f, floor + size.y * 0.8f, 0.3f), along, decor.Folder);
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
                && zone.Look != StageZoneView.ZoneLook.Steam && zone.Look != StageZoneView.ZoneLook.Pneumatic
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
