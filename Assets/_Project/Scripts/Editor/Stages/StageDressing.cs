using System;
using System.Collections.Generic;
using System.Linq;
using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using Airerage.Runtime.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ステージの飾りつけを自動で行う（D-97 の V27。2026-11-17 のユーザーの決定: V1・V3・V7・V8・V10〜V15・V17〜V19・V21・V22。進め方は
    /// この自動化 → ATHLETIC で試作 → 全ステージ）。地形の当たりの箱（BoxCollider）はそのままにして見た目だけを差し替え、ほかの飾りには
    /// 当たりを付けない（地形として読み込まれるのは今までの箱だけ。遊びは変わらない）。色と強さはステージの台本（StageLook）から。
    /// - 足場: 地形の箱の見た目を外し、同じ大きさの面取りした箱（StageBlock。V1）に。材質 Airerage/StageSurface（V3・V7・V8・V10・V22。
    ///   つやと面取りの光で立体感を出す）。小さな浮いたワイヤーの刺さらない足場は、見た目だけゆっくり上下する（V21）。ワイヤーが刺さる
    ///   足場のオレンジの縁は光る面にし、上の面を覆っていた板は正面の縁の細い帯にする（上の面の陰影を見せる）。
    /// - ギミック（2026-11-17 の要望: 足場と同じ質感に）: ジャンプ台は面取りした台と光る板、加速リングは面取りした枠、看板は枠と光る面、
    ///   通風口・加速の床は面取りした台、風は光の柱（Airerage/WindColumn）、線（矢印・筋・輪）は光る線（Airerage/GlowLine）。
    ///   アンカーは面取りした菱形と光る芯。
    /// - 光・空・霧（V11・V15）: グラデーションの空（Airerage/GradientSky。夜は星）、環境光、遠くを空の色に溶かす霧。
    /// - 遠景（V14・V17）: 今の街（世界観は現代日本）。遠い列と中くらいの列のビルのかたまり、左右の近いビル、屋上の航空障害灯、
    ///   運営の撮影の飛行船 1 隻（浮かぶ島などは置かない。2026-11-17 の要望）。
    /// - 光の格子（V18）: 撃破の線のさらに下に広がる格子の板（下に道路のある街のステージには置かない）。
    /// - 撃破の線の膜（V19）: 撃破の範囲の 4 辺の外に光の膜（キャラクターが近いほど濃い）。今までの赤い枠の線は外す。
    /// - 仕上げの画面処理（V13）: 光る物だけのにじみ（ブルーム）、色の調整、画面の端を少し暗く（ステージの Volume）。
    /// 光の柱（V12）と拠点の光る線は拠点の表示（BankView）、触れたときの輪（V22）とキャラクターの位置は SurfaceImpactFeed（試合のシーン）。
    /// </summary>
    internal static class StageDressing
    {
        private const string MaterialRoot = "Assets/_Project/Materials/Stages";

        // 小さな浮いた足場（呼吸させる）の大きさの上限（m）。
        private const float BreathMaxWidth = 4f;
        private const float BreathMaxHeight = 1f;

        // 撃破の線の膜の幅（m。線から外へ）。
        private const float MembraneWidth = 6f;

        // ワイヤーが刺さる足場の上の面の、正面の縁の光る帯の奥行き（m）。
        private const float TopRimDepth = 0.14f;

        /// <summary>
        /// これより奥（遊ぶ面 z = 0 から、手前の面までの距離。m）にある背景の飾りは、影を受けず落とさない。影はカメラから決まった距離
        /// （PC_RPAsset の 50m）の球の中だけに描かれるので、奥の壁ではキャラクターの周りの円の中だけ影が出ていた（2026-11-17 の指摘:
        /// キッチンの奥の壁のタイルの線が、プレイヤーの周りの円の中だけ太い。線の影が円の中だけ出ていた）。
        /// </summary>
        public const float BackgroundShadowDepth = 1f;

        /// <summary>ステージのシーン（開いている）を look で飾る。地形・ギミック・撃破の範囲・飾りを作った後に呼ぶ。</summary>
        public static void Apply(StageLook look, Vector2 boundsMin, Vector2 boundsMax)
        {
            string folder = FolderOf(look);
            look.ApplySky();
            FitHorizon(look, boundsMax);
            ApplyLighting(look, folder);
            DressTerrain(look, folder);
            DressAttachableGlow(look, folder);
            DressAnchors(look, folder);
            DressGimmicks(look, folder);
            DressPlatforms(look, folder);
            CreateMembranes(look, folder, boundsMin, boundsMax);
            QuietBackgroundShadows();
            if (look.Grid)
            {
                CreateGrid(look, folder, boundsMin);
            }
            if (look.Skyline)
            {
                CreateBackdrop(look, folder, boundsMin);
            }
            if (look.WindowCity)
            {
                CreateWindowCity(look, folder);
            }
            if (look.FarGround)
            {
                CreateFarGround(look, folder);
            }
            CreateVolume(look, folder);
        }

        // 奥の背景の飾りは影を受けず落とさない（BackgroundShadowDepth）。遊ぶ面にかかる飾り（調理台・床など）はそのまま。
        private static void QuietBackgroundShadows()
        {
            GameObject decor = GameObject.Find("Decor");
            if (decor == null)
            {
                return;
            }
            foreach (Renderer renderer in decor.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.bounds.min.z >= BackgroundShadowDepth)
                {
                    renderer.receiveShadows = false;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        /// <summary>材質を置くフォルダ。</summary>
        public static string FolderOf(StageLook look)
        {
            return $"{MaterialRoot}/{look.Id}/Look";
        }

        /// <summary>足場の材質（ワイヤーが刺さるか）。</summary>
        public static Material BlockMaterial(StageLook look, string folder, bool attachable)
        {
            return SurfaceMaterial(attachable ? "BlockAttachable" : "Block", look, folder, attachable ? look.AttachableSide : look.BlockSide,
                attachable ? look.AttachableTop : look.BlockTop);
        }

        // 足場と同じ材質（面取りした箱に使う）。色だけを変える。
        private static Material SurfaceMaterial(string name, StageLook look, string folder, Color side, Color top)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/StageSurface", m =>
            {
                m.SetColor("_BaseColor", side);
                m.SetColor("_TopColor", top);
                m.SetColor("_ShadeColor", look.ShadeTint);
                m.SetColor("_RimColor", look.RimColor);
                m.SetFloat("_RimStrength", look.RimStrength);
                m.SetFloat("_HeightLow", look.HeightLow);
                m.SetFloat("_HeightHigh", look.HeightHigh);
                m.SetFloat("_LocalGradient", look.LocalGradient);
                m.SetFloat("_Occlusion", look.Occlusion);
                m.SetFloat("_Ambient", look.Ambient);
                m.SetFloat("_Specular", look.Specular);
                m.SetFloat("_Shininess", 48f);
                m.SetFloat("_EdgeGlint", look.EdgeGlint);
                m.SetColor("_TrimColor", look.TrimColor);
                // 触れたときの光の輪はかなり控えめに（2026-11-17 のユーザーの要望）: 暗い色・細い輪・短い時間・小さな広がり（約 2.7m）。
                m.SetColor("_ImpactColor", look.ImpactColor);
                m.SetFloat("_ImpactSpeed", 6f);
                m.SetFloat("_ImpactSeconds", 0.45f);
                m.SetFloat("_ImpactWidth", 0.16f);
            }, folder);
        }

        private static Material EmissiveMaterial(string name, Color color, float intensity, string folder)
        {
            color.a = 1f;
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/Emissive", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Intensity", intensity);
            }, folder);
        }

        // 主の光、空、環境光、霧（V11・V15）。ステージのシーンを最初に読み込むので、試合とメニューの背景で使われる。
        private static void ApplyLighting(StageLook look, string folder)
        {
            Light light = Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                throw new InvalidOperationException("ステージのシーンに光がない");
            }
            light.color = look.LightColor;
            light.intensity = look.LightIntensity;
            light.transform.rotation = Quaternion.Euler(look.LightEuler);
            light.shadows = LightShadows.Soft;
            Material sky = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("Sky", "Airerage/GradientSky", m =>
            {
                m.SetColor("_ZenithColor", look.SkyZenith);
                m.SetColor("_SkyColor", look.SkySky);
                m.SetColor("_HorizonColor", look.SkyHorizon);
                m.SetColor("_BelowColor", look.SkyBelow);
                m.SetColor("_SunColor", look.SunColor);
                m.SetVector("_SunDirection", look.SunDirection);
                m.SetFloat("_SunSize", look.SunSize);
                m.SetFloat("_SunGlow", look.SunGlow);
                m.SetFloat("_Stars", look.Stars);
                // 空模様（2026-11-18 の要望）。
                m.SetFloat("_CloudCover", look.CloudCover);
                m.SetColor("_CloudLit", look.CloudLit);
                m.SetColor("_CloudShade", look.CloudShade);
                m.SetColor("_CloudUnder", look.CloudUnder);
                m.SetFloat("_CloudTop", look.CloudTop);
                m.SetFloat("_CloudScale", look.CloudScale);
                m.SetFloat("_CloudSpeed", look.CloudSpeed);
                m.SetFloat("_CloudSoftness", look.CloudSoftness);
                m.SetFloat("_Cirrus", look.Cirrus);
                m.SetColor("_CirrusColor", look.CirrusColor);
                m.SetColor("_MoonColor", look.MoonColor);
                m.SetVector("_MoonDirection", look.MoonDirection);
                m.SetFloat("_MoonSize", look.MoonSize);
                m.SetFloat("_MilkyWay", look.MilkyWay);
                m.SetColor("_HorizonGlow", look.HorizonGlow);
                m.SetFloat("_HorizonGlowHeight", look.HorizonGlowHeight);
                m.SetFloat("_Nebula", look.Nebula);
                m.SetColor("_NebulaA", look.NebulaA);
                m.SetColor("_NebulaB", look.NebulaB);
                m.SetFloat("_Planet", look.Planet ? 1f : 0f);
                m.SetVector("_PlanetDirection", look.PlanetDirection);
                m.SetFloat("_PlanetRadius", look.PlanetRadius);
                m.SetFloat("_Embers", look.Embers);
                // 地平線のもやは目の高さより下へ（足場の後ろは空の色にして、上の面が溶けないように）。
                m.SetFloat("_HorizonHeight", look.HorizonHeight);
            }, folder);
            RenderSettings.skybox = sky;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;
            RenderSettings.fog = look.Fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.FogColor;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;
        }

        // 地形の箱の見た目を、面取りした箱（StageBlock）に差し替える。当たりの箱（BoxCollider と WireAttachable）は残す。
        private static void DressTerrain(StageLook look, string folder)
        {
            GameObject terrain = GameObject.Find("Terrain");
            if (terrain == null)
            {
                throw new InvalidOperationException("ステージのシーンに地形（Terrain）がない");
            }
            Material plain = BlockMaterial(look, folder, false);
            Material attachable = BlockMaterial(look, folder, true);
            var visuals = new GameObject("TerrainVisuals").transform;
            var random = new Random(look.Seed);
            foreach (Transform box in terrain.transform)
            {
                var collider = box.GetComponent<BoxCollider>();
                if (collider == null)
                {
                    continue;
                }
                bool canAttach = box.GetComponent<WireAttachable>() != null;
                // 作った直後のコライダーの bounds は、物理の位置の同期の前で古いことがあるので、変形と大きさから求める。
                var bounds = new Bounds(box.TransformPoint(collider.center), Vector3.Scale(box.lossyScale, collider.size));
                Object.DestroyImmediate(box.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(box.GetComponent<MeshFilter>());

                var go = new GameObject(box.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(visuals, false);
                go.transform.position = bounds.center;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = canAttach ? attachable : plain;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                bool small = bounds.size.x <= BreathMaxWidth && bounds.size.y <= BreathMaxHeight;
                float breath = !canAttach && small ? look.BreathAmplitude : 0f;
                float phase = (float)(random.NextDouble() * Math.PI * 2.0);
                float seconds = 3.6f + (float)random.NextDouble() * 1.6f;
                go.AddComponent<StageBlock>().Configure(bounds.size, look.Bevel, phase, canAttach, breath, seconds);
            }
        }

        // ワイヤーが刺さる足場のオレンジの縁（AttachableGlow）: 縁を光る面に（にじむ）、上の面を覆っていた板は正面の縁の細い帯に、
        // 正面に重ねた薄いオレンジの板は外す、縁を回る光の粒は光る線に。
        private static void DressAttachableGlow(StageLook look, string folder)
        {
            AttachableGlow glow = Object.FindFirstObjectByType<AttachableGlow>();
            if (glow == null)
            {
                return;
            }
            Material rim = EmissiveMaterial("AttachableRim", SandboxWireSceneBuilder.AttachableGlowColor, look.AttachableRimIntensity, folder);
            Material line = GlowLineMaterial(look, folder);
            var serialized = new SerializedObject(glow);
            SerializedProperty rims = serialized.FindProperty("_rims");
            for (int i = 0; i < rims.arraySize; i++)
            {
                var renderer = (Renderer)rims.GetArrayElementAtIndex(i).objectReferenceValue;
                if (renderer == null)
                {
                    continue;
                }
                renderer.sharedMaterial = rim;
                if (renderer.name == "TopRim")
                {
                    Transform t = renderer.transform;
                    Vector3 scale = t.localScale;
                    float depth = scale.z / 0.98f;
                    t.localScale = new Vector3(scale.x, scale.y, TopRimDepth);
                    t.localPosition = new Vector3(t.localPosition.x, t.localPosition.y, -depth * 0.5f + TopRimDepth * 0.5f);
                }
            }
            // 正面に重ねた薄いオレンジの板は外す（描画は光の量に比例する空間で重ねるので、暗い面ではごく薄くしても赤茶色に濁り、正面の
            // 陰影が見えなくなった。刺さる足場は、にじむオレンジの縁と上の縁の帯、縁を回る光の粒で見分けられる）。
            var surfaces = new List<GameObject>();
            foreach (Renderer renderer in glow.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == "Surface")
                {
                    surfaces.Add(renderer.gameObject);
                }
            }
            foreach (GameObject surface in surfaces)
            {
                Object.DestroyImmediate(surface);
            }
            SerializedProperty sparks = serialized.FindProperty("_sparks");
            for (int i = 0; i < sparks.arraySize; i++)
            {
                var spark = (LineRenderer)sparks.GetArrayElementAtIndex(i).objectReferenceValue;
                if (spark != null)
                {
                    spark.sharedMaterial = line;
                }
            }
        }

        // アンカー: 面取りした菱形（金色）と、正面の光る芯。大きいアンカーの菱形も同じ材質に。
        private static void DressAnchors(StageLook look, string folder)
        {
            Material body = SurfaceMaterial("AnchorBlock", look, folder, look.AnchorSide, look.AnchorTop);
            Material core = EmissiveMaterial("AnchorCore", look.AnchorCore, look.GlowIntensity * 0.8f, folder);
            foreach (WireAnchorPoint anchor in Object.FindObjectsByType<WireAnchorPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform target = anchor.GetComponent<MeshRenderer>() != null ? anchor.transform : anchor.transform.Find("Diamond");
                if (target == null || target.GetComponent<MeshRenderer>() == null)
                {
                    continue;
                }
                Vector3 size = target.localScale;
                target.localScale = Vector3.one;
                target.GetComponent<MeshRenderer>().sharedMaterial = body;
                target.gameObject.AddComponent<StageBlock>().Configure(size, Mathf.Min(size.x, size.y) * 0.18f, 0f, true, 0f, 4f);
                GameObject glow = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, "Core", core,
                    new Vector3(size.x * 0.42f, size.y * 0.42f, 0.04f));
                glow.transform.SetParent(target, false);
                glow.transform.localPosition = new Vector3(0f, 0f, -size.z * 0.5f - 0.03f);
                NoShadows(glow);
            }
        }

        // ギミックの見た目を足場と同じ質感に（2026-11-17 の要望）。ギミックの範囲と効き方（StageZoneAuthoring）は変えない。
        private static void DressGimmicks(StageLook look, string folder)
        {
            Material line = GlowLineMaterial(look, folder);
            Material frame = SurfaceMaterial("GimmickFrame", look, folder, look.GimmickSide, look.GimmickTop);
            // 風（2026-11-17 の要望: 塊に見えないリアルな風）: 範囲は塗らず、気流の筋と運ばれる粒だけ（Airerage/WindColumn）。
            Material wind = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("Wind", "Airerage/WindColumn", m =>
            {
                m.SetColor("_Color", look.WindColor);
                m.SetFloat("_Strength", 0.6f);
                m.SetFloat("_LaneSpacing", 0.75f);
                m.SetFloat("_Width", 0.03f);
                m.SetFloat("_Period", 7f);
                m.SetFloat("_Length", 2.6f);
                m.SetFloat("_Speed", 9f);
                // まっすぐ流れ、少し光る（2026-11-17 の 2 回目の要望）。
                m.SetFloat("_Sway", 0f);
                m.SetFloat("_Dust", 0.6f);
                m.SetFloat("_Shade", 0.1f);
                m.SetFloat("_Glow", 2.4f);
            }, folder);
            foreach (StageZoneView view in Object.FindObjectsByType<StageZoneView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var serialized = new SerializedObject(view);
                var zone = view.GetComponent<StageZoneAuthoring>();
                var lookProperty = serialized.FindProperty("_look");
                bool plain = zone != null && (lookProperty == null || lookProperty.enumValueIndex == (int)StageZoneView.ZoneLook.Default);
                bool windShader = plain && (zone.Kind == StageZoneKind.Updraft || zone.Kind == StageZoneKind.Gust) && Find(view.transform, "Column") != null;
                // 線（印・輪・筋）は光る線に。予告の斜線と記号は今までの材質のまま（光る線の材質で描くと光の塊になった。2026-11-17 の指摘）。
                // 風の範囲を光の柱で描くときは、線の筋は描かない。
                view.ConfigureGlow(line, windLines: !windShader);
                EditorUtility.SetDirty(view);
                if (!plain)
                {
                    continue;
                }
                Color color = serialized.FindProperty("_color").colorValue;
                switch (zone.Kind)
                {
                    case StageZoneKind.JumpPad:
                        DressPad(view.transform, zone, frame, EmissiveMaterial("Glow" + Hex(color), color, look.GlowIntensity, folder));
                        break;
                    case StageZoneKind.Updraft:
                    case StageZoneKind.Gust:
                        DressWind(view.transform, zone, wind);
                        break;
                    case StageZoneKind.BoostRing:
                        DressRing(view.transform, zone, frame, EmissiveMaterial("Glow" + Hex(color), color, look.GlowIntensity, folder));
                        break;
                    case StageZoneKind.Bumper:
                        DressSign(view.transform, zone, frame, EmissiveMaterial("Glow" + Hex(color), color, look.GlowIntensity * 0.75f, folder));
                        break;
                    case StageZoneKind.SpeedLane:
                        ToBlock(Find(view.transform, "Lane"), frame, 0.04f);
                        break;
                    case StageZoneKind.Launcher:
                        ToBlock(Find(view.transform, "Vent"), frame, 0.08f);
                        break;
                }
            }
        }

        // 動く足場と扉（StagePlatformAuthoring の見た目の Body）: 同じ大きさの面取りした箱に（色はステージで決めた色のまま）。
        // ワイヤーが刺さる足場の正面の縁（RimTop など）は、にじむオレンジの光る面に。見た目を作り込んだ足場（Body のないもの）はそのまま。
        private static void DressPlatforms(StageLook look, string folder)
        {
            Material rim = EmissiveMaterial("AttachableRim", SandboxWireSceneBuilder.AttachableGlowColor, look.AttachableRimIntensity, folder);
            foreach (StagePlatformAuthoring platform in Object.FindObjectsByType<StagePlatformAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform visual = platform.transform.Find("Visual");
                if (visual == null)
                {
                    continue;
                }
                bool rims = false;
                foreach (Transform part in visual)
                {
                    if (part.name.StartsWith("Rim"))
                    {
                        part.GetComponent<Renderer>().sharedMaterial = rim;
                        rims = true;
                    }
                }
                Transform body = visual.Find("Body");
                var renderer = body != null ? body.GetComponent<MeshRenderer>() : null;
                if (renderer == null || body.GetComponent<MeshFilter>().sharedMesh == null || body.GetComponent<MeshFilter>().sharedMesh.name != "Cube")
                {
                    continue;
                }
                Color color = renderer.sharedMaterial.HasProperty("_BaseColor") ? renderer.sharedMaterial.GetColor("_BaseColor") : look.BlockSide;
                Material material = SurfaceMaterial("Platform" + Hex(color), look, folder, CapColor(color, 0.66f),
                    CapColor(Color.Lerp(color, Color.white, 0.25f), 0.78f));
                ToBlock(body, material, look.Bevel);
                body.GetComponent<StageBlock>().Configure(body.GetComponent<StageBlock>().Size, look.Bevel, 0f, rims, 0f, 4f);
            }
        }

        // いちばん明るい成分が max を超えないように、色合いを保って暗くする。
        private static Color CapColor(Color c, float max)
        {
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float k = m > max ? max / m : 1f;
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        // ジャンプ台: 床に置いた面取りした台と、その上の光る板（色はギミックの色）。
        private static void DressPad(Transform root, StageZoneAuthoring zone, Material frame, Material glow)
        {
            Transform pad = Find(root, "Pad");
            if (pad == null)
            {
                return;
            }
            Transform parent = pad.parent;
            Object.DestroyImmediate(pad.gameObject);
            float floor = -zone.Size.y * 0.5f;
            var baseBlock = new GameObject("PadBase", typeof(MeshFilter), typeof(MeshRenderer));
            baseBlock.transform.SetParent(parent, false);
            baseBlock.transform.localPosition = new Vector3(0f, floor + 0.13f, 0f);
            baseBlock.GetComponent<MeshRenderer>().sharedMaterial = frame;
            baseBlock.AddComponent<StageBlock>().Configure(new Vector3(zone.Size.x + 0.5f, 0.26f, 2f), 0.08f, 0f, true, 0f, 4f);
            GameObject plate = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, "PadGlow", glow, new Vector3(zone.Size.x, 0.05f, 1.5f));
            plate.transform.SetParent(parent, false);
            plate.transform.localPosition = new Vector3(0f, floor + 0.285f, 0f);
            NoShadows(plate);
        }

        // 風: 平らな薄い色の板を、両脇と両端がやわらかく消える光の柱に。板の上の向き（uv.y）を風の向きに回す。
        private static void DressWind(Transform root, StageZoneAuthoring zone, Material wind)
        {
            Transform column = Find(root, "Column");
            if (column == null)
            {
                return;
            }
            Vector2 direction = zone.Kind == StageZoneKind.Updraft || zone.Direction == Vector2.zero ? Vector2.up : zone.Direction.normalized;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            bool sideways = Mathf.Abs(direction.x) > Mathf.Abs(direction.y);
            column.localScale = sideways ? new Vector3(zone.Size.y, zone.Size.x, 1f) : new Vector3(zone.Size.x, zone.Size.y, 1f);
            column.localRotation = Quaternion.Euler(0f, 0f, angle);
            column.GetComponent<Renderer>().sharedMaterial = wind;
        }

        // 加速リング: 輪の外に面取りした短い箱を並べた枠と、上下左右の光る節（輪の線は StageZoneView が光る線で描く）。
        private static void DressRing(Transform root, StageZoneAuthoring zone, Material frame, Material glow)
        {
            var ring = new GameObject("RingFrame").transform;
            ring.SetParent(root, false);
            const int Pieces = 18;
            float rx = zone.Size.x * 0.5f + 0.18f;
            float ry = zone.Size.y * 0.5f + 0.18f;
            for (int i = 0; i < Pieces; i++)
            {
                float a0 = i * Mathf.PI * 2f / Pieces;
                float a1 = (i + 1) * Mathf.PI * 2f / Pieces;
                var p0 = new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry);
                var p1 = new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry);
                Vector2 mid = (p0 + p1) * 0.5f;
                Vector2 along = p1 - p0;
                var piece = new GameObject("Piece", typeof(MeshFilter), typeof(MeshRenderer));
                piece.transform.SetParent(ring, false);
                piece.transform.localPosition = new Vector3(mid.x, mid.y, 0f);
                piece.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
                piece.GetComponent<MeshRenderer>().sharedMaterial = frame;
                piece.AddComponent<StageBlock>().Configure(new Vector3(along.magnitude * 1.14f, 0.22f, 0.5f), 0.06f, 0f, true, 0f, 4f);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                GameObject node = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, "Node", glow, new Vector3(0.26f, 0.26f, 0.56f));
                node.transform.SetParent(ring, false);
                node.transform.localPosition = new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f);
                node.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                NoShadows(node);
            }
        }

        // 看板（バンパー）: 面取りした枠と、正面の光る面（ネオン）。丸い物（噴石など）は光る面だけ。
        private static void DressSign(Transform root, StageZoneAuthoring zone, Material frame, Material glow)
        {
            Transform body = Find(root, "Body");
            if (body == null)
            {
                return;
            }
            body.GetComponent<Renderer>().sharedMaterial = glow;
            if (Mathf.Approximately(zone.Size.x, zone.Size.y))
            {
                return;
            }
            Vector3 size = body.localScale;
            body.localScale = new Vector3(size.x, size.y, 0.25f);
            body.localPosition = new Vector3(body.localPosition.x, body.localPosition.y, -0.42f);
            var back = new GameObject("SignFrame", typeof(MeshFilter), typeof(MeshRenderer));
            back.transform.SetParent(body.parent, false);
            back.transform.localPosition = new Vector3(body.localPosition.x, body.localPosition.y, 0.1f);
            back.GetComponent<MeshRenderer>().sharedMaterial = frame;
            back.AddComponent<StageBlock>().Configure(new Vector3(size.x + 0.3f, size.y + 0.3f, 0.8f), 0.1f, 0f, true, 0f, 4f);
        }

        // 箱の見た目を、同じ大きさの面取りした箱に（大きさは変形の大きさから。変形は 1 倍に戻す）。
        private static void ToBlock(Transform box, Material material, float bevel)
        {
            if (box == null || box.GetComponent<MeshFilter>() == null)
            {
                return;
            }
            Vector3 size = box.localScale;
            box.localScale = Vector3.one;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            box.gameObject.AddComponent<StageBlock>().Configure(size, bevel, 0f, true, 0f, 4f);
        }

        private static Material GlowLineMaterial(StageLook look, string folder)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("GlowLine", "Airerage/GlowLine", m =>
            {
                m.SetFloat("_Intensity", look.GlowIntensity);
                m.SetFloat("_Core", 0.6f);
            }, folder);
        }

        // 撃破の範囲の 4 辺の外の光の膜（V19）。今までの赤い枠の線（KnockoutBounds の Frame）は外す。
        private static void CreateMembranes(StageLook look, string folder, Vector2 min, Vector2 max)
        {
            GameObject bounds = GameObject.Find("KnockoutBounds");
            if (bounds == null)
            {
                throw new InvalidOperationException("ステージのシーンに撃破の範囲（KnockoutBounds）がない");
            }
            Transform frame = bounds.transform.Find("Frame");
            if (frame != null)
            {
                Object.DestroyImmediate(frame.gameObject);
            }
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("KillMembrane", "Airerage/KillMembrane",
                m => m.SetColor("_Color", look.MembraneColor), folder);
            var root = new GameObject("Membranes").transform;
            root.SetParent(bounds.transform, false);
            float w = MembraneWidth;
            float height = max.y - min.y;
            float width = max.x - min.x + w * 2f;
            // 板の uv.x は撃破の線（0）から外（1）へ。Quad の uv.x は自分の +X の向きなので、向きに合わせて回す。
            AddMembrane(root, "Left", material, new Vector3(min.x - w * 0.5f, (min.y + max.y) * 0.5f, 0f), new Vector2(w, height), 180f);
            AddMembrane(root, "Right", material, new Vector3(max.x + w * 0.5f, (min.y + max.y) * 0.5f, 0f), new Vector2(w, height), 0f);
            AddMembrane(root, "Bottom", material, new Vector3((min.x + max.x) * 0.5f, min.y - w * 0.5f, 0f), new Vector2(w, width), -90f);
            AddMembrane(root, "Top", material, new Vector3((min.x + max.x) * 0.5f, max.y + w * 0.5f, 0f), new Vector2(w, width), 90f);
        }

        private static void AddMembrane(Transform root, string name, Material material, Vector3 center, Vector2 size, float angle)
        {
            GameObject go = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, name, material, new Vector3(size.x, size.y, 1f));
            go.transform.SetParent(root, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            NoShadows(go);
        }

        // はるか下に広がる光の格子（V18）。
        private static void CreateGrid(StageLook look, string folder, Vector2 boundsMin)
        {
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("Grid", "Airerage/StageGrid", m =>
            {
                m.SetColor("_LineColor", look.GridColor * look.GridStrength);
                m.SetFloat("_LineWidth", 1.1f);
                m.SetFloat("_Spacing", 10f);
                m.SetFloat("_FadeStart", 60f);
                m.SetFloat("_FadeEnd", 360f);
            }, folder);
            GameObject grid = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Plane, "LightGrid", material, new Vector3(140f, 1f, 140f));
            grid.transform.position = new Vector3(0f, boundsMin.y - look.GridDepth, 360f);
            NoShadows(grid);
        }

        // 遠景（V14・V17）: 今の街（世界観は現代日本）。遠い列・中くらいの列のビルのかたまり（真ん中ほど低く、外ほど高い）、左右の近いビル、
        // 屋上の航空障害灯（赤く点滅）、運営の撮影の飛行船 1 隻。遊ぶ所の真後ろと下は空ける。
        private static void CreateBackdrop(StageLook look, string folder, Vector2 boundsMin)
        {
            float hazeTop = boundsMin.y - 8f;
            Material far = BackdropMaterial("BackdropFar", look.FarColor, look, folder, hazeTop, 0f, true);
            Material mid = BackdropMaterial("BackdropMid", look.MidColor, look, folder, hazeTop, 0f, true);
            Material near = BackdropMaterial("BackdropNear", look.NearColor, look, folder, hazeTop, 0f, true);
            Material beacon = BackdropMaterial("BackdropBeacon", look.BeaconColor, look, folder, hazeTop, 0.5f, false);
            Material ship = BackdropMaterial("BackdropAirship", look.AirshipColor, look, folder, hazeTop, 0f, false);
            var random = new Random(look.Seed + 1);
            var root = new GameObject("Backdrop").transform;

            // 真ん中の奥は実在の東京（PLATEAU の西新宿の高層ビル群と周りの街。2026-11-17 のユーザーの決定。docs/15 6.51）。作った箱の
            // ビルは、その外側（遠い列）と手前の低い列だけ。
            Material plateau = PlateauMaterial(look, folder, hazeTop);
            Transform tokyo = Child(root, "Tokyo");
            bool city = CreatePlateauCity(tokyo, far, plateau != null ? plateau : mid, beacon, look);
            if (city && look.PlateauStreets)
            {
                // 建物を道路の地面の上に立たせる（根元を切らない。2026-11-18 の要望）。
                CreatePlateauStreets(tokyo, look, folder, hazeTop);
            }
            if (look.CityGlowColor.maxColorComponent > 0f)
            {
                CreateCityGlow(Child(root, "CityGlow"), look, folder);
            }
            Skyline(Child(root, "Far"), far, beacon, random, 440f, 580f, -25f, 15f, 60f, 0.35f, city ? PlateauHalfWidth : 0f);
            Skyline(Child(root, "Mid"), mid, beacon, random, 240f, 320f, -45f, -15f, 45f, 0.45f, city ? PlateauMidKeepOut : 0f);

            // 近い列: 左右の外側だけに大きなビル（遊ぶ所の真後ろを空けて、足場と見分けやすく）。
            Transform nearRoot = Child(root, "Near");
            float[] nearX = city ? new[] { -230f, -165f, 165f, 230f } : new[] { -230f, -165f, -110f, 110f, 165f, 230f };
            for (int i = 0; i < nearX.Length; i++)
            {
                float z = Range(random, 90f, 125f);
                float top = Range(random, -40f, 15f);
                Building(nearRoot, near, beacon, new Vector3(nearX[i] + Range(random, -8f, 8f), top, z), Range(random, 12f, 20f), top + 220f, random, 0.7f);
            }
            // 遊ぶ所の下には何も置かない（下に床があるように見えないように。落ちたら撃破）。

            // 運営の撮影の飛行船（1 隻。V17。2026-11-17 の要望）。
            GameObject airship = Airship(Child(root, "Airships"), ship, beacon, new Vector3(-420f, 62f, 270f), 46f);
            airship.AddComponent<BackdropMotion>().ConfigureDrift(new Vector3(4.5f, 0f, 0f), -720f, 720f);
        }

        // 実在の東京（国土交通省 Project PLATEAU の 3D 都市モデル。東京都 23 区 2020 年度。CC BY 4.0 互換・出典の表示が必要）。
        // 西新宿の高層ビル（LOD2。地面から 50m 以上の 151 棟）は、形を減らさず外壁の写真のアトラスつき（tools/assets/plateau_textured.py。
        // 2026-11-18 の要望: ほぼシルエットに見えない、ある程度リアルな背景）。周りの街（LOD1。25m 以上）は高さの箱（plateau_skyline.py）。
        private const string PlateauFolder = "Assets/_Project/ThirdParty/PLATEAU";
        private const string PlateauAtlasPath = PlateauFolder + "/Shinjuku_LOD2_Atlas.jpg";
        private const string PlateauNightPath = PlateauFolder + "/Shinjuku_LOD2_AtlasNight.jpg";
        private const string PlateauGroundPath = PlateauFolder + "/Shinjuku_Ground.jpg";

        // 道路の地図の範囲（中心からの m。tools/assets/plateau_roads.py の --half と同じ）と、地面の板の左右の広さと奥の端（ワールド）。
        private const float PlateauGroundHalf = 1200f;
        private const float StreetsHalfWidth = 1500f;
        private const float StreetsBack = 1150f;

        // 夜の街明かりのもや: 遠景のビルの足もとから上へ薄れる光の層を 2 枚（奥に高く濃い層、街の手前に低く薄い層）。足し合わせて描き、
        // ブルームでにじむ（2026-11-18 の要望: 夜の都会の空気感）。
        private static void CreateCityGlow(Transform root, StageLook look, string folder)
        {
            float ground = look.PlateauGround;
            float back = ShinjukuZ + look.PlateauPush + 160f;
            (string Name, float Z, float Width, float Height, float Strength, float Falloff)[] layers =
            {
                ("Back", back, 2800f, 320f, 1f, 2.2f),
                ("Front", look.PlateauStreetsFront, 2200f, 120f, 0.45f, 1.6f),
            };
            foreach (var layer in layers)
            {
                Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial($"CityGlow{layer.Name}", "Airerage/CityGlow", m =>
                {
                    m.SetColor("_Color", look.CityGlowColor * layer.Strength);
                    m.SetFloat("_Falloff", layer.Falloff);
                    m.SetFloat("_EdgeSoftness", 0.22f);
                    m.SetFloat("_Shimmer", 0.15f);
                }, folder);
                GameObject glow = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, layer.Name, material, new Vector3(layer.Width, layer.Height, 1f));
                glow.transform.SetParent(root, false);
                glow.transform.position = new Vector3(0f, ground + layer.Height * 0.5f, layer.Z);
                NoShadows(glow);
            }
        }

        // 実在の街の地面（道路の地図を貼った板。昼は歩道の灰色に車道、夜は街灯の橙の道路）。地図は実際の位置に合わせ、外はくり返す。
        private static void CreatePlateauStreets(Transform root, StageLook look, string folder, float hazeTop)
        {
            Texture2D day = LoadAtlas(PlateauGroundPath, 2048, TextureWrapMode.Repeat, 8);
            if (day == null)
            {
                Debug.LogWarning($"PLATEAU の道路の地図がない（{PlateauGroundPath}）。地面を置かない");
                return;
            }
            float front = look.PlateauStreetsFront;
            float width = StreetsHalfWidth * 2f;
            float depth = StreetsBack - front;
            var center = new Vector3(0f, look.PlateauGround + 0.02f, (front + StreetsBack) * 0.5f);
            float heroZ = ShinjukuZ + look.PlateauPush;
            // 板の UV（0〜1）から地図の UV へ: 東は +x、北は +z（建物の OBJ と同じ。奥行きは PlateauDepthScale に縮めてある）。地図の左が西、
            // 上が北。
            float span = PlateauGroundHalf * 2f;
            var tiling = new Vector2(width / (span * look.PlateauScale), depth / (span * PlateauDepthScale));
            var offset = new Vector2(0.5f + center.x / (span * look.PlateauScale) - tiling.x * 0.5f,
                0.5f + (center.z - heroZ) / (span * PlateauDepthScale) - tiling.y * 0.5f);
            Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("BackdropStreets", "Airerage/StageBackdrop", m =>
            {
                m.SetColor("_BaseColor", look.MidColor);
                m.SetColor("_HazeColor", look.HazeColor);
                m.SetFloat("_HazeTop", hazeTop);
                m.SetFloat("_HazeBottom", hazeTop - 150f);
                m.SetFloat("_Blink", 0f);
                m.SetFloat("_BandStrength", 0f);
                m.SetFloat("_BandFill", 0f);
                m.SetTexture("_MainTex", day);
                m.SetTextureScale("_MainTex", tiling);
                m.SetTextureOffset("_MainTex", offset);
                m.SetFloat("_TexBlend", 1f);
                m.SetColor("_TexTint", look.CityTexTint);
                m.SetFloat("_LightShade", 0f);
                m.SetFloat("_FogAmount", look.CityFogAmount);
                m.SetFloat("_FogFadeStart", look.CityFogFadeStart);
                m.SetFloat("_FogFadeEnd", look.CityFogFadeEnd);
                // 夜の道路は光らせない（橙に光る道路が大通りとのつなぎ目を目立たせていた。2026-11-18 の指摘）。
                m.SetTexture("_NightTex", null);
                m.SetFloat("_NightStrength", 0f);
                m.SetFloat("_NightSides", 0f);
                // 夜は濡れた道路のネオンの照り返し（手前の大通りと同じ）。
                m.SetFloat("_WetReflect", look.WetReflect);
            }, folder);
            GameObject streets = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "Streets", material, new Vector3(width, depth, 1f));
            streets.transform.SetParent(root, false);
            streets.transform.SetPositionAndRotation(center, Quaternion.Euler(90f, 0f, 0f));
            NoShadows(streets);
        }

        // 西新宿の外壁の写真の材質（遠景の材質に、写真・主の光の向きの陰影・夜は暗くして窓の明かり）。写真がなければ null。
        private static Material PlateauMaterial(StageLook look, string folder, float hazeTop)
        {
            Texture2D atlas = LoadAtlas(PlateauAtlasPath);
            if (atlas == null)
            {
                Debug.LogWarning($"PLATEAU の外壁の写真がない（{PlateauAtlasPath}）。色だけで塗る");
                return null;
            }
            Texture2D nightLights = look.CityNightGlow > 0f ? LoadAtlas(PlateauNightPath) : null;
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("BackdropCity", "Airerage/StageBackdrop", m =>
            {
                m.SetColor("_BaseColor", look.MidColor);
                m.SetColor("_HazeColor", look.HazeColor);
                m.SetFloat("_HazeTop", hazeTop);
                m.SetFloat("_HazeBottom", hazeTop - 150f);
                m.SetFloat("_Blink", 0f);
                m.SetFloat("_BandSpacing", 4.5f);
                // 写真の窓のまま（階の帯と作った窓の明かりは重ねない。夜の明かりは写真の窓に合わせた絵 _NightTex）。
                m.SetFloat("_BandStrength", 0f);
                m.SetColor("_BandColor", look.BandColor);
                m.SetFloat("_BandFill", 0f);
                m.SetTexture("_MainTex", atlas);
                m.SetFloat("_TexBlend", 1f);
                m.SetColor("_TexTint", look.CityTexTint);
                m.SetFloat("_LightShade", 0.6f);
                m.SetFloat("_FogAmount", look.CityFogAmount);
                m.SetFloat("_FogFadeStart", look.CityFogFadeStart);
                m.SetFloat("_FogFadeEnd", look.CityFogFadeEnd);
                // 夜は、写真の窓に合わせて作った明かりの絵を、縦の面だけ光らせる（窓と同期。ネオン風。2026-11-18 の要望）。
                m.SetTexture("_NightTex", nightLights);
                m.SetFloat("_NightStrength", nightLights != null ? look.CityNightGlow : 0f);
                m.SetFloat("_NightSides", 1f);
            }, folder);
        }

        /// <summary>奥の地面の板の奥の端（ワールドの z。カメラの描く奥行き 1000m より手前）と、左右の広さ。</summary>
        public const float FarGroundBack = 880f;
        private const float FarGroundHalfWidth = 1600f;

        /// <summary>空の地平線を合わせる目の位置（いちばん高いカメラ。ステージの上の端より少し上、いつもの距離 30m）。</summary>
        public const float HorizonEyeAbove = 6f;
        public const float HorizonEyeZ = -30f;

        /// <summary>
        /// 空の地平線を、奥の地面の奥の端より少し下にする（2026-11-18 の要望: いちばん奥の背景の小物までを地面として、空はその根元まで
        /// しっかり描く）。前は地平線のもや（地面に見える色）が奥の山やビルの根元より上から始まっていた。カメラが高いほど奥の端は低く見える
        /// ので、いちばん高いカメラから見た奥の端に合わせる（低いカメラでは、端の上はいつも空）。雲の帯の頂の高さは今の見た目のまま保つ。
        /// 奥の地面がないステージ（宇宙・屋内）はそのまま。
        /// </summary>
        public static void FitHorizon(StageLook look, Vector2 boundsMax)
        {
            float groundY;
            float back;
            if (look.FarGround)
            {
                groundY = look.FarGroundY;
                back = FarGroundBack;
            }
            else if (look.Skyline && look.PlateauStreets)
            {
                groundY = look.PlateauGround;
                back = Mathf.Min(StreetsBack, FarGroundBack);
            }
            else
            {
                return;
            }
            float eyeY = boundsMax.y + HorizonEyeAbove;
            float edge = (groundY - eyeY) / (back - HorizonEyeZ);
            float horizon = edge - 0.012f;
            if (horizon < look.HorizonHeight)
            {
                look.CloudTop += look.HorizonHeight - horizon;
                look.HorizonHeight = horizon;
            }
        }

        // 奥の地面の板（霧で遠くほど空の地平線の色に溶ける。光を受けない色）。当たりも影もない。
        private static void CreateFarGround(StageLook look, string folder)
        {
            Material material = look.FarGroundWater
                ? StageDecor.WaterMaterial("FarSea", Color.Lerp(look.FarGroundColor, new Color(0.25f, 0.55f, 0.65f), 0.6f), look.FarGroundColor, false,
                    look.FarGroundY, true, look.SkySky, look.SkyHorizon, folder)
                : SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("FarGround", "Universal Render Pipeline/Unlit", m =>
                {
                    m.SetColor("_BaseColor", look.FarGroundColor);
                }, folder);
            float depth = FarGroundBack - look.FarGroundNear;
            GameObject ground = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "FarGround", material, new Vector3(FarGroundHalfWidth * 2f, depth, 1f));
            ground.transform.SetPositionAndRotation(new Vector3(0f, look.FarGroundY, look.FarGroundNear + depth * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            NoShadows(ground);
        }

        // 窓の外の街の広さ（m）: 道路の地面の左右と奥の端、周りの街（LOD1）のくり返しの列。
        private const float WindowStreetsHalfWidth = 900f;
        private const float WindowStreetsBack = 900f;
        // 周りの街（LOD1）を西新宿より後ろへずらす距離（m。手前の端が窓の壁より前に出ないように）。
        private const float WindowAroundPush = 60f;

        /// <summary>
        /// 窓の外の街（キッチン。2026-11-18 の要望: 窓に窓枠と鍵、外にビル街の街並み）。実在の西新宿（PLATEAU。外壁の写真つき）を縮尺の
        /// 模型にして壁のすぐ後ろに置き、周りの街（LOD1）を左右と奥へくり返して地平線まで埋め、道路の地面を敷く。遠くに置くと、窓が小さく
        /// カメラから遠いので、カメラが少し動くだけで窓から見える所が大きく変わり、空か地面しか見えなくなる（近い模型なら、カメラの高さに
        /// よって上の階から足もとまでが見える）。奥行きは少し縮める（窓から見える奥行きに収める）。足もとは空の色のもやに溶かす。影は落とさない。
        /// </summary>
        private static void CreateWindowCity(StageLook look, string folder)
        {
            Mesh shinjuku = LoadObjMesh($"{PlateauFolder}/Shinjuku_LOD2.obj");
            Mesh tokyo = LoadObjMesh($"{PlateauFolder}/Tokyo_LOD1.obj");
            Texture2D atlas = LoadAtlas(PlateauAtlasPath);
            Texture2D ground = LoadAtlas(PlateauGroundPath, 2048, TextureWrapMode.Repeat, 8);
            if (shinjuku == null || tokyo == null || atlas == null)
            {
                Debug.LogWarning($"PLATEAU の街のモデルか外壁の写真がない（{PlateauFolder}）。窓の外に街を置かない");
                return;
            }
            Vector3 center = look.WindowCityCenter;
            float scale = look.WindowCityScale;
            float depthScale = scale * look.WindowCityDepth;
            float hazeTop = center.y + 34f * scale / 0.15f;
            float hazeBottom = center.y - 60f;
            var root = new GameObject("WindowCity").transform;

            void Haze(Material m, float fog)
            {
                m.SetColor("_HazeColor", look.WindowCityHaze);
                m.SetFloat("_HazeTop", hazeTop);
                m.SetFloat("_HazeBottom", hazeBottom);
                m.SetFloat("_Blink", 0f);
                m.SetFloat("_FogAmount", fog);
                m.SetFloat("_FogFadeStart", 0f);
                m.SetFloat("_FogFadeEnd", 0f);
                m.SetTexture("_NightTex", null);
                m.SetFloat("_NightStrength", 0f);
                m.SetFloat("_NightSides", 0f);
            }

            Material hero = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WindowCity", "Airerage/StageBackdrop", m =>
            {
                m.SetColor("_BaseColor", new Color(0.62f, 0.66f, 0.72f));
                m.SetFloat("_BandStrength", 0f);
                m.SetFloat("_BandFill", 0f);
                m.SetTexture("_MainTex", atlas);
                m.SetTextureScale("_MainTex", Vector2.one);
                m.SetTextureOffset("_MainTex", Vector2.zero);
                m.SetFloat("_TexBlend", 1f);
                m.SetColor("_TexTint", look.WindowCityTint);
                m.SetFloat("_LightShade", 0.6f);
                Haze(m, look.WindowCityFog);
            }, folder);
            Material around = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WindowCityAround", "Airerage/StageBackdrop", m =>
            {
                m.SetColor("_BaseColor", new Color(0.6f, 0.64f, 0.7f));
                // 階の帯（窓の並び）。帯の間隔は縮尺に合わせる（実物の 1 階 3.5m）。
                m.SetFloat("_BandSpacing", 3.5f * scale);
                m.SetFloat("_BandStrength", 0.12f);
                m.SetColor("_BandColor", new Color(0.35f, 0.42f, 0.52f));
                m.SetFloat("_BandFill", 0f);
                m.SetTexture("_MainTex", null);
                m.SetFloat("_TexBlend", 0f);
                m.SetFloat("_LightShade", 0.6f);
                Haze(m, Mathf.Min(1f, look.WindowCityFog + 0.15f));
            }, folder);

            var heroScale = new Vector3(scale, scale, depthScale);
            PlaceMesh(root, "Shinjuku", shinjuku, hero, center, heroScale);
            // 周りの街: 本来の並びを西新宿の後ろへ少しずらして 1 つ、左右と奥へ向きを 180° ずつ変えてくり返す（同じ並びに見えないように）。
            Vector3 tile = Vector3.Scale(tokyo.bounds.size, heroScale);
            Transform aroundRoot = Child(root, "Around");
            for (int row = 0; row < 2; row++)
            {
                for (int column = -1; column <= 1; column++)
                {
                    bool turned = ((row + column) & 1) != 0;
                    var position = new Vector3(center.x + column * tile.x, center.y, center.z + WindowAroundPush + row * tile.z);
                    Transform copy = PlaceMesh(aroundRoot, $"Around{row}{column + 1}", tokyo, around, position, heroScale);
                    copy.rotation = Quaternion.Euler(0f, turned ? 180f : 0f, 0f);
                }
            }

            if (ground != null)
            {
                // 道路の地面（西新宿の道路の地図。建物と同じ縮尺。外はくり返す）。奥は霧と空のもやに溶かす。
                float front = center.z - (shinjuku.bounds.extents.z + 40f) * depthScale;
                float width = WindowStreetsHalfWidth * 2f;
                float depth = WindowStreetsBack - front;
                var streetsCenter = new Vector3(center.x, center.y + 0.02f, (front + WindowStreetsBack) * 0.5f);
                float span = PlateauGroundHalf * 2f;
                var tiling = new Vector2(width / (span * scale), depth / (span * depthScale));
                var offset = new Vector2(0.5f + (streetsCenter.x - center.x) / (span * scale) - tiling.x * 0.5f,
                    0.5f + (streetsCenter.z - center.z) / (span * depthScale) - tiling.y * 0.5f);
                Material streets = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WindowCityStreets", "Airerage/StageBackdrop", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.5f, 0.52f, 0.55f));
                    m.SetFloat("_BandStrength", 0f);
                    m.SetFloat("_BandFill", 0f);
                    m.SetTexture("_MainTex", ground);
                    m.SetTextureScale("_MainTex", tiling);
                    m.SetTextureOffset("_MainTex", offset);
                    m.SetFloat("_TexBlend", 1f);
                    m.SetColor("_TexTint", look.WindowCityTint);
                    m.SetFloat("_LightShade", 0f);
                    Haze(m, Mathf.Min(1f, look.WindowCityFog + 0.3f));
                }, folder);
                GameObject plane = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "Streets", streets, new Vector3(width, depth, 1f));
                plane.transform.SetParent(root, false);
                plane.transform.SetPositionAndRotation(streetsCenter, Quaternion.Euler(90f, 0f, 0f));
                NoShadows(plane);
            }
        }

        // 外壁の写真のアトラスなどを読む（縮めずに、ミップマップと異方性で遠くでもちらつかない読み込みの設定に合わせる）。
        private static Texture2D LoadAtlas(string path, int size = 4096, TextureWrapMode wrap = TextureWrapMode.Clamp, int aniso = 4)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                return null;
            }
            if (importer.maxTextureSize != size || !importer.mipmapEnabled || importer.anisoLevel != aniso || importer.wrapMode != wrap
                || !importer.sRGBTexture)
            {
                importer.maxTextureSize = size;
                importer.mipmapEnabled = true;
                importer.anisoLevel = aniso;
                importer.wrapMode = wrap;
                importer.sRGBTexture = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // 実在の街の奥行きの縮め方（遠景の帯に収める）と、置く奥行き。大きさ・地面の高さ・奥へずらす距離は台本（StageLook.PlateauScale ほか）。
        private const float PlateauDepthScale = 0.1f;
        private const float ShinjukuZ = 400f;
        private const float TokyoZ = 640f;

        /// <summary>作った箱のビルを置かない真ん中の幅（x の絶対値。西新宿の高層ビル群の幅）。</summary>
        public const float PlateauHalfWidth = 760f;

        // 手前の低い列も、真ん中のこの幅には置かない（西新宿の高層ビル群を手前の箱で隠さない）。
        private const float PlateauMidKeepOut = 300f;

        // 西新宿（手前・濃い色）と周りの街（奥・遠い色）を置き、高いビルの屋上に航空障害灯を付ける。モデルがなければ false。
        private static bool CreatePlateauCity(Transform root, Material far, Material mid, Material beacon, StageLook look)
        {
            float ground = look.PlateauGround;
            Mesh shinjuku = LoadObjMesh($"{PlateauFolder}/Shinjuku_LOD2.obj");
            Mesh tokyo = LoadObjMesh($"{PlateauFolder}/Tokyo_LOD1.obj");
            if (shinjuku == null || tokyo == null)
            {
                Debug.LogWarning($"PLATEAU の街のモデルがない（{PlateauFolder}）。作った箱のビルだけで遠景を作る");
                return false;
            }
            var scale = new Vector3(look.PlateauScale, look.PlateauScale, PlateauDepthScale);
            Transform hero = PlaceMesh(root, "Shinjuku", shinjuku, mid, new Vector3(0f, ground, ShinjukuZ + look.PlateauPush), scale);
            PlaceMesh(root, "Surroundings", tokyo, far, new Vector3(0f, ground, TokyoZ + look.PlateauPush), scale);
            // 航空障害灯: 25m の升目ごとのいちばん高い所のうち、上から 14 か所（150m より高く、互いに 60m 以上離す）。
            var tops = new Dictionary<Vector2Int, Vector3>();
            foreach (Vector3 v in shinjuku.vertices)
            {
                var cell = new Vector2Int(Mathf.FloorToInt(v.x / 25f), Mathf.FloorToInt(v.z / 25f));
                if (!tops.TryGetValue(cell, out Vector3 top) || v.y > top.y)
                {
                    tops[cell] = v;
                }
            }
            var chosen = new List<Vector3>();
            foreach (Vector3 top in tops.Values.Where(t => t.y > 150f).OrderByDescending(t => t.y))
            {
                if (chosen.Count >= 14)
                {
                    break;
                }
                if (chosen.All(c => Vector2.Distance(new Vector2(c.x, c.z), new Vector2(top.x, top.z)) > 60f))
                {
                    chosen.Add(top);
                }
            }
            Transform lights = Child(root, "Beacons");
            foreach (Vector3 top in chosen)
            {
                GameObject light = Box(lights, "Beacon", beacon, Vector3.zero, new Vector3(2.4f, 2.4f, 2.4f));
                light.transform.position = hero.TransformPoint(top) + Vector3.up * 1.6f;
            }
            return true;
        }

        // OBJ のモデルからメッシュを読む（材質は作らず、法線は計算し、頂点を読めるように読み込みの設定を合わせる）。
        private static Mesh LoadObjMesh(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                return null;
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.isReadable
                || importer.importNormals != ModelImporterNormals.Calculate || importer.importCameras || importer.importLights)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.isReadable = true;
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalSmoothingAngle = 20f;
                importer.importCameras = false;
                importer.importLights = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
        }

        private static Transform PlaceMesh(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            NoShadows(go);
            return go.transform;
        }

        // ビルの列: かたまりで置き、かたまりの間をあける（等間隔に並べると単調になる）。真ん中ほど低く、外ほど高く、ときどき高いビル。
        // keepOut: 真ん中のこの幅（x の絶対値）には置かない（実在の街を見せる所）。
        private static void Skyline(Transform parent, Material body, Material beacon, Random random, float zMin, float zMax, float lowTop, float highTop,
            float sideRise, float beaconChance, float keepOut)
        {
            float cursor = -800f;
            while (cursor < 800f)
            {
                int cluster = random.Next(2, 6);
                for (int i = 0; i < cluster && cursor < 800f; i++)
                {
                    float width = Range(random, 10f, 34f);
                    float x = cursor + width * 0.5f;
                    if (Mathf.Abs(x) < keepOut)
                    {
                        cursor += width + Range(random, 2f, 8f);
                        continue;
                    }
                    float side = Mathf.Clamp01(Mathf.Abs(x) / 600f);
                    float top = Range(random, lowTop, highTop) + side * Range(random, 10f, sideRise);
                    if (random.NextDouble() < 0.1 + side * 0.2)
                    {
                        top += Range(random, 40f, 90f);
                    }
                    Building(parent, body, beacon, new Vector3(x, top, Range(random, zMin, zMax)), width, top + 260f, random, beaconChance);
                    cursor += width + Range(random, 2f, 8f);
                }
                cursor += Range(random, 30f, 90f);
            }
        }

        // ビル: 下から top まで伸びる箱に、屋上の形を 1 つ（段の付いた頭・ヘリポートの平たい台・アンテナ・何もなし）。
        // 高いビルの屋上には赤い航空障害灯。
        private static void Building(Transform parent, Material body, Material beacon, Vector3 topCenter, float width, float height, Random random,
            float beaconChance)
        {
            var building = new GameObject("Building").transform;
            building.SetParent(parent, false);
            building.position = topCenter;
            float depth = width * Range(random, 0.6f, 1.1f);
            Box(building, "Body", body, new Vector3(0f, -height * 0.5f, 0f), new Vector3(width, height, depth));
            float roof = 0f;
            int kind = random.Next(0, 4);
            if (kind == 0)
            {
                float capHeight = Range(random, 4f, 14f);
                Box(building, "Setback", body, new Vector3(0f, capHeight * 0.5f, 0f), new Vector3(width * Range(random, 0.5f, 0.75f), capHeight, depth * 0.7f));
                roof = capHeight;
            }
            else if (kind == 1)
            {
                Box(building, "Helipad", body, new Vector3(0f, 0.8f, 0f), new Vector3(width * 0.8f, 1.6f, depth * 0.8f));
                roof = 1.6f;
            }
            else if (kind == 2)
            {
                float mast = Range(random, 8f, 20f);
                Box(building, "Antenna", body, new Vector3(width * 0.2f, mast * 0.5f, 0f), new Vector3(0.8f, mast, 0.8f));
                roof = mast;
            }
            if (random.NextDouble() < beaconChance)
            {
                Box(building, "Beacon", beacon, new Vector3(kind == 2 ? width * 0.2f : 0f, roof + 0.9f, 0f), new Vector3(1.6f, 1.6f, 1.6f));
            }
        }

        // 飛行船（運営の撮影）: 細長い胴、先の丸み、尾のひれ、撮影のゴンドラとカメラ、腹の赤い灯り（右向き）。
        private static GameObject Airship(Transform parent, Material body, Material beacon, Vector3 position, float length)
        {
            var ship = new GameObject("Airship");
            ship.transform.SetParent(parent, false);
            ship.transform.position = position;
            float girth = length * 0.2f;
            GameObject hull = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Capsule, "Hull", body, new Vector3(girth, length * 0.5f, girth));
            hull.transform.SetParent(ship.transform, false);
            hull.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            NoShadows(hull);
            Box(ship.transform, "FinTop", body, new Vector3(-length * 0.42f, girth * 0.55f, 0f), new Vector3(length * 0.12f, girth * 0.7f, 0.5f));
            Box(ship.transform, "FinSide", body, new Vector3(-length * 0.42f, 0f, 0f), new Vector3(length * 0.12f, 0.5f, girth * 1.4f));
            Box(ship.transform, "Gondola", body, new Vector3(length * 0.06f, -girth * 0.62f, 0f), new Vector3(length * 0.22f, girth * 0.28f, girth * 0.36f));
            Box(ship.transform, "Camera", body, new Vector3(length * 0.16f, -girth * 0.86f, 0f), new Vector3(girth * 0.2f, girth * 0.2f, girth * 0.2f));
            Box(ship.transform, "Light", beacon, new Vector3(-length * 0.04f, -girth * 0.78f, 0f), new Vector3(0.9f, 0.9f, 0.9f));
            return ship;
        }

        private static Material BackdropMaterial(string name, Color color, StageLook look, string folder, float hazeTop, float blink, bool bands)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/StageBackdrop", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetColor("_HazeColor", look.HazeColor);
                m.SetFloat("_HazeTop", hazeTop);
                m.SetFloat("_HazeBottom", hazeTop - 150f);
                m.SetFloat("_Blink", blink);
                m.SetFloat("_BandSpacing", 4.5f);
                m.SetFloat("_BandStrength", bands ? look.BandStrength : 0f);
                m.SetColor("_BandColor", look.BandColor);
                m.SetFloat("_BandFill", bands ? look.BandFill : 0f);
                // 夜の窓の明かりはネオンの色を混ぜて光らせる（2026-11-18 の要望）。
                m.SetFloat("_NeonMix", look.NeonMix);
                m.SetFloat("_WindowGlow", look.WindowGlow);
            }, folder);
        }

        // 仕上げの画面処理（V13）: ステージの Volume（どこでも効く）。設定はステージのフォルダのアセット（作り直しても GUID を保つ）。
        private static void CreateVolume(StageLook look, string folder)
        {
            string path = $"{folder}/PostProcess.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                SandboxWireSceneBuilder.EnsureFolder(folder);
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            Bloom bloom = Get<Bloom>(profile);
            bloom.threshold.Override(look.BloomThreshold);
            bloom.intensity.Override(look.BloomIntensity);
            bloom.scatter.Override(look.BloomScatter);
            bloom.highQualityFiltering.Override(true);
            ColorAdjustments color = Get<ColorAdjustments>(profile);
            color.contrast.Override(look.Contrast);
            color.saturation.Override(look.Saturation);
            Vignette vignette = Get<Vignette>(profile);
            vignette.intensity.Override(look.Vignette);
            vignette.smoothness.Override(0.45f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcess");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component))
            {
                return component;
            }
            component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // root の子孫から名前で探す（なければ null）。
        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }
            return null;
        }

        private static string Hex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static GameObject Box(Transform parent, string name, Material material, Vector3 localPosition, Vector3 size)
        {
            GameObject go = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Cube, name, material, size);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            NoShadows(go);
            return go;
        }

        private static void NoShadows(GameObject go)
        {
            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static float Range(Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
    }
}
