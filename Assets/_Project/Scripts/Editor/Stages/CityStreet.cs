using System.Collections.Generic;
using Airerage.Runtime.Presentation;
using UnityEditor;
using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ビル街（SKYLINE の昼夜）の手前の大通り（2026-11-25 の要望: 電灯を半分にして置き直し、アセットで歩道と車道を作り、歩道に人影、
    /// 車道にまばらな車。実際の街並みを参考に。docs/15 6.70）。谷の底の道路から奥へまっすぐ延びる、日本の市街地の 4 車線の幹線道路:
    /// - 横断の構成（道路構造令の第 4 種第 1 級に近い値）: 車線 3.25 m × 4、路肩 0.5 m、縁石（歩車道境界ブロック）0.15 m の段、
    ///   植樹帯 1.5 m（街路樹と植え込み）、歩道 4.3 m（インターロッキングブロック。視覚障害者誘導用ブロックの黄色い帯）。
    /// - 標示: 中央線（黄色の実線。はみ出し禁止）、車線境界線（白の破線。線 5 m・間 5 m）、車道外側線（白の実線）、手前の横断歩道
    ///   （ゼブラ。しま 0.45 m・間 0.45 m）と停止線、横断歩道の予告のダイヤマーク。
    /// - 道路照明: 高さ約 10.5 m の LED の道路照明灯（細い鋼管の柱・水平の腕・平たい灯具。網は LampPost()）を千鳥配置（左右交互。
    ///   片側 52 m ごと。前の半分の数）。信号は Kenney City Kit Roads（CC0）。
    /// - 舗装の写真の絵: 車道は Poly Haven の clean_asphalt、歩道は concrete_pavers（CC0）。夜は濡れた路面のネオンの照り返し。
    /// - 街路樹: Quaternius の CommonTree（CC0）。
    /// - 車と人影: 形は下の網（作り直しても同じ）。動きは StreetTrafficView（左側通行・フェード・夜の灯り）。
    /// 見た目だけ（当たりはない。撃破の範囲のずっと下）。
    /// </summary>
    internal static class CityStreet
    {
        /// <summary>
        /// 道の高さ（地上の大通りの面。ワールドの y）と、縁石の高さ（m）。2026-11-25 の要望（ステージは非常に高い高層ビルの上）で、遊ぶ屋上
        /// （y 0〜15）から約 110〜125 m 下に下げた（間は雲海。CityClouds）。
        /// </summary>
        public const float Ground = -110f;
        public const float CurbHeight = 0.15f;

        /// <summary>
        /// 手前の端（横断歩道）と奥の端（ワールドの z）。2026-11-26 の要望（大通りをもっと奥まで伸ばし、車や人の通りが見えるように）で、
        /// 西新宿の道路の地面の手前（380）から、西新宿の高層ビル群の間の谷間を抜けて霧に溶ける所（カメラの遠い端の手前）まで伸ばした。
        /// </summary>
        public const float NearZ = 21f;
        public const float FarZ = 960f;

        /// <summary>車線の幅・車道の端（路肩の外。中心からの x）・植樹帯の外の端・歩道の外の端（m）。</summary>
        public const float LaneWidth = 3.25f;
        public const float RoadEdge = 7f;
        public const float CurbWidth = 0.18f;
        public const float PlantingOuter = 8.68f;
        public const float SidewalkOuter = 13f;

        /// <summary>道路照明の数（奥の端まで）・間隔（片側 52 m、左右交互で 26 m ごと）・最初の位置（z）。</summary>
        public const int LampCount = 35;
        public const float LampSpacing = 26f;
        public const float LampStart = 34f;

        /// <summary>横断歩道（z の範囲）と停止線（手前へ来る車線の z）。</summary>
        public const float CrosswalkNear = 23.5f;
        public const float CrosswalkFar = 27.5f;
        public const float StopLineZ = 29.5f;

        /// <summary>使い回す車と人影の数（重くならない程度に。大通りを奥まで伸ばしたので、前の 14 台・18 人から増やした）。</summary>
        public const int CarPool = 32;
        public const int PersonPool = 40;

        private const string MeshStage = "City";
        private const string Roads = "Kenney/CityRoads";
        private const string Trees = "Quaternius/Nature";

        public static void Build(StageDecor decor, bool night)
        {
            var root = new GameObject("Avenue").transform;
            root.SetParent(decor.Root, false);
            BuildRoad(decor, root, night);
            BuildSidewalks(decor, root, night);
            BuildLamps(decor, night);
            BuildTrees(decor, night);
            BuildSignals(decor, night);
            BuildTraffic(decor, root, night);
        }

        // 車道（アスファルトの写真の絵。夜は濡れた路面）と標示。
        private static void BuildRoad(StageDecor decor, Transform root, bool night)
        {
            float length = FarZ - NearZ;
            Texture2D asphalt = TerrainSculpt.PhotoTexture("clean_asphalt", false);
            Texture2D asphaltNormal = TerrainSculpt.PhotoTexture("clean_asphalt", true);
            // 写真の絵は 1 枚で約 3 m 四方。
            var tiling = new Vector2(RoadEdge * 2f / 3f, length / 3f);
            Material road = night
                ? SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("AvenueAsphaltWet", "Airerage/StageBackdrop", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.09f, 0.09f, 0.11f));
                    m.SetTexture("_MainTex", asphalt);
                    m.SetTextureScale("_MainTex", tiling);
                    m.SetFloat("_TexBlend", 0.55f);
                    m.SetFloat("_HazeTop", -1000f);
                    m.SetFloat("_HazeBottom", -1100f);
                    m.SetFloat("_Blink", 0f);
                    m.SetFloat("_BandStrength", 0f);
                    m.SetFloat("_BandFill", 0f);
                    m.SetFloat("_LightShade", 0.3f);
                    m.SetFloat("_FogAmount", 1f);
                    m.SetFloat("_WetReflect", 1.6f);
                }, decor.Folder)
                : SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("AvenueAsphalt", ThirdPartyProps.PropShader, m =>
                {
                    m.SetTexture("_BaseMap", asphalt);
                    m.SetTextureScale("_BaseMap", tiling);
                    m.SetTexture("_BumpMap", asphaltNormal);
                    m.SetFloat("_BumpScale", 0.6f);
                    m.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.84f));
                    m.SetFloat("_Smoothness", 0.18f);
                }, decor.Folder);
            GameObject surface = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "Roadway", road, new Vector3(RoadEdge * 2f, length, 1f));
            surface.transform.SetParent(root, false);
            surface.transform.SetPositionAndRotation(new Vector3(0f, Ground + 0.01f, (NearZ + FarZ) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            surface.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Material paint = ActorMaterial("AvenueMarkings", decor, 0.25f);
            ProceduralMesh.Place("Markings", root, Markings(), Vector3.zero, Quaternion.identity, Vector3.one, paint)
                .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 縁石・植樹帯（土と植え込み）・歩道（インターロッキングブロックの写真の絵）。
        private static void BuildSidewalks(StageDecor decor, Transform root, bool night)
        {
            float length = FarZ - NearZ;
            float walkWidth = SidewalkOuter - PlantingOuter;
            Texture2D pavers = TerrainSculpt.PhotoTexture("concrete_pavers", false);
            Texture2D paversNormal = TerrainSculpt.PhotoTexture("concrete_pavers", true);
            // Unity の箱の上の面は u が x、v が z。写真の絵は 1 枚で約 2 m 四方。
            var tiling = new Vector2(walkWidth / 2f, length / 2f);
            Material walk = night
                ? SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("AvenuePaversWet", "Airerage/StageBackdrop", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.15f));
                    m.SetTexture("_MainTex", pavers);
                    m.SetTextureScale("_MainTex", tiling);
                    m.SetFloat("_TexBlend", 0.6f);
                    m.SetFloat("_HazeTop", -1000f);
                    m.SetFloat("_HazeBottom", -1100f);
                    m.SetFloat("_Blink", 0f);
                    m.SetFloat("_BandStrength", 0f);
                    m.SetFloat("_BandFill", 0f);
                    m.SetFloat("_LightShade", 0.3f);
                    m.SetFloat("_FogAmount", 1f);
                    m.SetFloat("_WetReflect", 0.8f);
                }, decor.Folder)
                : SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("AvenuePavers", ThirdPartyProps.PropShader, m =>
                {
                    m.SetTexture("_BaseMap", pavers);
                    m.SetTextureScale("_BaseMap", tiling);
                    m.SetTexture("_BumpMap", paversNormal);
                    m.SetFloat("_BumpScale", 0.8f);
                    m.SetColor("_BaseColor", new Color(1.05f, 1.02f, 0.98f));
                    m.SetFloat("_Smoothness", 0.12f);
                }, decor.Folder);
            Material curb = decor.Lit("AvenueCurb", night ? new Color(0.2f, 0.2f, 0.22f) : new Color(0.74f, 0.74f, 0.72f));
            Material soil = decor.Lit("AvenueSoil", night ? new Color(0.05f, 0.04f, 0.035f) : new Color(0.24f, 0.19f, 0.14f));
            Material hedge = decor.Lit("AvenueHedge", night ? new Color(0.03f, 0.07f, 0.05f) : new Color(0.16f, 0.32f, 0.16f));
            float middle = (NearZ + FarZ) * 0.5f;
            foreach (float side in new[] { -1f, 1f })
            {
                float curbX = side * (RoadEdge + CurbWidth * 0.5f);
                decor.Box("Curb", root, new Vector3(curbX, Ground + CurbHeight * 0.5f, middle), new Vector3(CurbWidth, CurbHeight, length), curb);
                float plantInner = RoadEdge + CurbWidth;
                float plantX = side * (plantInner + PlantingOuter) * 0.5f;
                decor.Box("PlantingSoil", root, new Vector3(plantX, Ground + CurbHeight * 0.4f, middle),
                    new Vector3(PlantingOuter - plantInner, CurbHeight * 0.8f, length), soil);
                // 植え込み（つつじなどの低い生け垣。10 m ごとに 4 m の切れ目）。
                for (float z = NearZ + 6f; z < FarZ - 4f; z += 14f)
                {
                    decor.Box("Hedge", root, new Vector3(plantX, Ground + CurbHeight + 0.25f, z + 7f), new Vector3(PlantingOuter - plantInner - 0.3f, 0.55f, 10f), hedge);
                }
                float walkX = side * (PlantingOuter + SidewalkOuter) * 0.5f;
                GameObject sidewalk = decor.Box("Sidewalk", root, new Vector3(walkX, Ground + CurbHeight * 0.5f, middle), new Vector3(walkWidth, CurbHeight, length), walk);
                sidewalk.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // 道路照明（千鳥配置。左右交互に 26 m ごと＝片側 52 m ごと）。日本の LED の道路照明灯に寄せた細い灰色の鋼管の柱・水平の腕・
        // 平たい灯具（網は LampPost()。Kenney の街灯は柱が太すぎた〈約 0.9 m〉ため、2026-11-25 に作り直した）。腕は車道へ張り出す。
        // 夜は灯具の下が灯り（_Lights）、路面に光だまり。
        private static void BuildLamps(StageDecor decor, bool night)
        {
            Material lampMaterial = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("AvenueLamp", "Airerage/StreetActor", m =>
            {
                m.SetFloat("_Smoothness", 0.55f);
                m.SetFloat("_Metallic", 0.4f);
                m.SetFloat("_Fade", 1f);
                m.SetFloat("_StepRate", 0f);
                m.SetFloat("_Lights", night ? 1f : 0f);
                m.SetColor("_LampColor", HdrColor(new Color(1f, 0.95f, 0.86f), 4f));
            }, decor.Folder);
            Material pool = night ? SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("AvenueLampPool", "Airerage/GlowStar", m =>
            {
                m.SetFloat("_Points", 0f);
                m.SetFloat("_Inner", 0.5f);
                m.SetFloat("_Sharp", 1.5f);
                m.SetFloat("_Halo", 0.9f);
                m.SetFloat("_Core", 0.1f);
                m.SetFloat("_Intensity", 0.6f);
                m.SetColor("_Color", new Color(1f, 0.9f, 0.74f, 0.36f));
            }, decor.Folder) : null;
            Mesh lamp = LampPost();
            float plantX = (RoadEdge + CurbWidth + PlantingOuter) * 0.5f;
            for (int i = 0; i < LampCount; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float z = LampStart + i * LampSpacing;
                float x = side * plantX;
                ProceduralMesh.Place("AvenueLamp", decor.Root, lamp, new Vector3(x, Ground + CurbHeight, z), Quaternion.Euler(0f, side < 0f ? 0f : 180f, 0f), Vector3.one, lampMaterial);
                if (night)
                {
                    float headX = x - side * LampReach;
                    GameObject light = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "AvenueLampPool", pool, new Vector3(9f, 9f, 1f));
                    light.transform.SetParent(decor.Root, false);
                    light.transform.SetPositionAndRotation(new Vector3(headX, Ground + 0.03f, z), Quaternion.Euler(90f, 0f, 0f));
                    light.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        /// <summary>道路照明灯の高さ（灯具の高さ。m）と、腕の張り出し（柱から灯具の中心まで。m）。</summary>
        public const float LampHeight = 10.5f;
        public const float LampReach = 2.8f;

        // 道路照明灯の網（柱は原点、腕は +X へ）: 基礎のカバー・少しすぼまる鋼管の柱・水平の腕・平たい LED の灯具（下の面が夜に灯る）。
        private static Mesh LampPost()
        {
            var mesh = new MeshParts();
            var steel = new Color(0.6f, 0.62f, 0.64f, 0f);
            var dark = new Color(0.22f, 0.23f, 0.25f, 0f);
            mesh.Box(new Vector3(0f, 0.3f, 0f), new Vector3(0.42f, 0.6f, 0.42f), dark);
            mesh.Hexa(new[]
            {
                new Vector3(-0.13f, 0.6f, -0.13f), new Vector3(0.13f, 0.6f, -0.13f), new Vector3(0.13f, 0.6f, 0.13f), new Vector3(-0.13f, 0.6f, 0.13f),
                new Vector3(-0.08f, LampHeight + 0.1f, -0.08f), new Vector3(0.08f, LampHeight + 0.1f, -0.08f), new Vector3(0.08f, LampHeight + 0.1f, 0.08f), new Vector3(-0.08f, LampHeight + 0.1f, 0.08f),
            }, steel);
            mesh.Box(new Vector3(LampReach * 0.5f - 0.2f, LampHeight + 0.12f, 0f), new Vector3(LampReach - 0.2f, 0.1f, 0.1f), steel);
            mesh.Box(new Vector3(LampReach, LampHeight + 0.1f, 0f), new Vector3(0.95f, 0.13f, 0.38f), dark);
            mesh.Box(new Vector3(LampReach, LampHeight + 0.025f, 0f), new Vector3(0.82f, 0.03f, 0.3f), new Color(0.9f, 0.9f, 0.86f, 0f), 7f);
            return mesh.Save(MeshStage, "AvenueLampPost");
        }

        // 街路樹（植樹帯に約 14 m ごと。灯りの近くは空ける）。
        private static void BuildTrees(StageDecor decor, bool night)
        {
            string[] models = { "CommonTree_1", "CommonTree_3", "CommonTree_5" };
            Color tint = night ? new Color(0.22f, 0.26f, 0.3f) : new Color(0.84f, 0.88f, 0.82f);
            float plantX = (RoadEdge + CurbWidth + PlantingOuter) * 0.5f;
            int index = 0;
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z = NearZ + 15f; z < FarZ - 10f; z += 14f)
                {
                    if (NearLamp(side, z))
                    {
                        continue;
                    }
                    ThirdPartyProps.PlaceMtl(decor, Trees, models[index % models.Length], new Vector3(side * plantX, Ground + CurbHeight, z),
                        decor.Range(0f, 360f), decor.Range(1.05f, 1.3f), tint, decor.Folder);
                    index++;
                }
            }
        }

        private static bool NearLamp(float side, float z)
        {
            for (int i = 0; i < LampCount; i++)
            {
                float lampSide = i % 2 == 0 ? -1f : 1f;
                if (lampSide == side && Mathf.Abs(LampStart + i * LampSpacing - z) < 5f)
                {
                    return true;
                }
            }
            return false;
        }

        // 横断歩道の両端の信号（Kenney の信号）。
        private static void BuildSignals(StageDecor decor, bool night)
        {
            Color tint = night ? new Color(0.45f, 0.46f, 0.55f) : Color.white;
            float x = PlantingOuter - 0.4f;
            ThirdPartyProps.Place(decor, ThirdPartyProps.CityRoads, "traffic-light", new Vector3(-x, Ground + CurbHeight, CrosswalkFar + 1.5f), 11f, 90f, tint, decor.Folder);
            ThirdPartyProps.Place(decor, ThirdPartyProps.CityRoads, "traffic-light", new Vector3(x, Ground + CurbHeight, CrosswalkNear - 1.5f), 11f, -90f, tint, decor.Folder);
        }

        // 車と人影（StreetTrafficView が動かす板を、決まった数だけ作っておく）。
        private static void BuildTraffic(StageDecor decor, Transform root, bool night)
        {
            var go = new GameObject("StreetTraffic");
            go.transform.SetParent(root, false);
            var view = go.AddComponent<StreetTrafficView>();
            Material carMaterial = ActorMaterial("StreetCar", decor, 0.6f);
            Material personMaterial = ActorMaterial("StreetPerson", decor, 0.12f);
            Material beamMaterial = night ? SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("CarBeam", "Airerage/GlowStar", m =>
            {
                m.SetFloat("_Points", 0f);
                m.SetFloat("_Inner", 0.4f);
                m.SetFloat("_Sharp", 1.4f);
                m.SetFloat("_Halo", 0.8f);
                m.SetFloat("_Core", 0.1f);
                m.SetFloat("_Intensity", 1.1f);
            }, decor.Folder) : null;
            var cars = new MeshRenderer[CarPool];
            var beams = new MeshRenderer[CarPool];
            for (int i = 0; i < CarPool; i++)
            {
                cars[i] = ActorRenderer($"Car{i}", go.transform, carMaterial);
                if (night)
                {
                    // 前照灯が前の路面に落とす光（車の前 4〜13 m の楕円）。
                    GameObject beam = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, "HeadlightPool", beamMaterial, new Vector3(3f, 10f, 1f));
                    beam.transform.SetParent(cars[i].transform, false);
                    beam.transform.localPosition = new Vector3(0f, 0.04f, 8f);
                    beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    beams[i] = beam.GetComponent<MeshRenderer>();
                    beams[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    beams[i].enabled = false;
                }
            }
            var people = new MeshRenderer[PersonPool];
            for (int i = 0; i < PersonPool; i++)
            {
                people[i] = ActorRenderer($"Person{i}", go.transform, personMaterial);
            }
            CarKind[] kinds = CarKinds();
            var carMeshes = new Mesh[kinds.Length];
            var lengths = new float[kinds.Length];
            var weights = new float[kinds.Length];
            var painted = new bool[kinds.Length];
            var large = new bool[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                carMeshes[i] = kinds[i].Build();
                lengths[i] = kinds[i].Length;
                weights[i] = kinds[i].Weight;
                painted[i] = kinds[i].Painted;
                large[i] = kinds[i].Large;
            }
            // 左側通行: 奥へ（+Z）は -X の車線、手前へ（-Z）は +X の車線。外側が走行車線、内側が追越車線。
            float inner = LaneWidth * 0.5f;
            float outer = LaneWidth * 1.5f;
            float[] laneX = { -outer, -inner, outer, inner };
            int[] laneDirection = { 1, 1, -1, -1 };
            bool[] laneOuter = { true, false, true, false };
            // 歩道の 2 本の歩く線（植樹帯寄りと建物寄り）。
            float walkInner = PlantingOuter + 1f;
            float walkOuter = SidewalkOuter - 1.2f;
            float[] walkX = { -walkInner, -walkOuter, walkInner, walkOuter };
            Mesh[] personMeshes = { Person("PersonSuit", false, false), Person("PersonSkirt", true, false), Person("PersonBag", false, true) };
            view.Configure(Ground + 0.01f, Ground + CurbHeight, NearZ + 2f, FarZ - 10f, laneX, laneDirection, laneOuter, walkX,
                cars, beams, carMeshes, lengths, weights, painted, large, people, personMeshes, night);
        }

        private static MeshRenderer ActorRenderer(string name, Transform parent, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.enabled = false;
            return renderer;
        }

        private static Material ActorMaterial(string name, StageDecor decor, float smoothness)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/StreetActor", m =>
            {
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_Fade", 1f);
                m.SetFloat("_StepRate", 0f);
                m.SetFloat("_Lights", 0f);
            }, decor.Folder);
        }

        private static Color HdrColor(Color color, float intensity)
        {
            return new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1f);
        }

        // ---- 標示の網 ----

        private static readonly Color White = new Color(0.92f, 0.92f, 0.9f, 0f);
        private static readonly Color Yellow = new Color(0.95f, 0.72f, 0.12f, 0f);

        // 道路の標示（白・黄の平らな板。車道の面の少し上）と、歩道の黄色い誘導ブロックの帯。
        private static Mesh Markings()
        {
            var mesh = new MeshParts();
            float y = Ground + 0.02f;
            float start = CrosswalkFar + 3f;
            // 中央線（黄色の実線）。横断歩道の手前で止める。
            mesh.Flat(new Vector2(-0.075f, start), new Vector2(0.075f, FarZ), y, Yellow);
            foreach (float side in new[] { -1f, 1f })
            {
                // 車線境界線（白の破線。線 5 m・間 5 m）。
                for (float z = start; z + 5f <= FarZ; z += 10f)
                {
                    mesh.Flat(new Vector2(side * LaneWidth - 0.075f, z), new Vector2(side * LaneWidth + 0.075f, z + 5f), y, White);
                }
                // 車道外側線（白の実線）。
                float edge = side * (LaneWidth * 2f + 0.075f);
                mesh.Flat(new Vector2(edge - 0.075f, NearZ), new Vector2(edge + 0.075f, FarZ), y, White);
                // 視覚障害者誘導用ブロック（黄色の帯。歩道の建物寄り）。
                float tactile = side * (SidewalkOuter - 0.9f);
                mesh.Flat(new Vector2(tactile - 0.15f, NearZ), new Vector2(tactile + 0.15f, FarZ), Ground + CurbHeight + 0.006f, Yellow);
            }
            // 横断歩道（ゼブラ。しまは道路の向きに長く、幅 0.45 m・間 0.45 m で車道を横切って並ぶ）。
            for (float x = -RoadEdge + 0.3f; x + 0.45f <= RoadEdge - 0.3f; x += 0.9f)
            {
                mesh.Flat(new Vector2(x, CrosswalkNear), new Vector2(x + 0.45f, CrosswalkFar), y, White);
            }
            // 停止線（手前へ来る車線。横断歩道の奥）。
            mesh.Flat(new Vector2(0.15f, StopLineZ), new Vector2(LaneWidth * 2f, StopLineZ + 0.45f), y, White);
            // 横断歩道の予告のダイヤマーク（手前へ来る車線に 2 つずつ）。
            foreach (float z in new[] { 60f, 80f })
            {
                foreach (float laneCenter in new[] { LaneWidth * 0.5f, LaneWidth * 1.5f })
                {
                    mesh.Diamond(new Vector2(laneCenter, z), 1.5f, 5f, 0.15f, y, White);
                }
            }
            return mesh.Save(MeshStage, "AvenueMarkings");
        }

        // ---- 車の網 ----

        /// <summary>車の種類（大きさは日本の車の寸法に近い値。m）。</summary>
        private sealed class CarKind
        {
            public string Name;
            public float Length;
            public float Width;
            public float Belt;
            public float Roof;
            public float CabinBack;
            public float CabinFront;
            public float RoofBack;
            public float RoofFront;
            public float Weight;
            public bool Painted = true;
            public bool Large;
            public Color Body = Color.white;
            public System.Action<MeshParts, CarKind> Extra;

            public Mesh Build()
            {
                var mesh = new MeshParts();
                if (Extra == null || !Large)
                {
                    CarBody(mesh, this);
                }
                Extra?.Invoke(mesh, this);
                return mesh.Save(MeshStage, "Car" + Name);
            }
        }

        private static readonly Color Glass = new Color(0.07f, 0.09f, 0.12f, 0f);
        private static readonly Color Tire = new Color(0.04f, 0.04f, 0.045f, 0f);
        private static readonly Color Trim = new Color(0.12f, 0.12f, 0.13f, 0f);
        private static readonly Color Lamp = new Color(0.85f, 0.88f, 0.9f, 0f);
        private static readonly Color Tail = new Color(0.45f, 0.05f, 0.05f, 0f);

        private static CarKind[] CarKinds()
        {
            return new[]
            {
                // セダン・コンパクト（ハッチバック）・軽のトールワゴン・ミニバン（車体の色は日本の人気の色の割合で塗り分ける）。
                new CarKind { Name = "Sedan", Length = 4.7f, Width = 1.8f, Belt = 0.95f, Roof = 1.45f, CabinBack = -1.35f, CabinFront = 0.9f, RoofBack = -1f, RoofFront = 0.25f, Weight = 0.22f },
                new CarKind { Name = "Compact", Length = 4f, Width = 1.7f, Belt = 0.95f, Roof = 1.52f, CabinBack = -1.85f, CabinFront = 0.7f, RoofBack = -1.75f, RoofFront = -0.1f, Weight = 0.2f },
                new CarKind { Name = "Kei", Length = 3.4f, Width = 1.48f, Belt = 0.95f, Roof = 1.78f, CabinBack = -1.62f, CabinFront = 0.85f, RoofBack = -1.58f, RoofFront = 0.45f, Weight = 0.2f },
                new CarKind { Name = "Minivan", Length = 4.7f, Width = 1.7f, Belt = 1f, Roof = 1.85f, CabinBack = -2.25f, CabinFront = 1.2f, RoofBack = -2.2f, RoofFront = 0.6f, Weight = 0.14f },
                // タクシー（背の高いセダン。濃い藍色。屋根の行灯が夜に灯る）。
                new CarKind
                {
                    Name = "Taxi", Length = 4.4f, Width = 1.7f, Belt = 0.98f, Roof = 1.72f, CabinBack = -2f, CabinFront = 0.95f, RoofBack = -1.9f, RoofFront = 0.35f,
                    Weight = 0.1f, Painted = false, Body = new Color(0.07f, 0.09f, 0.2f),
                    Extra = (m, k) => m.Box(new Vector3(0f, k.Roof + 0.14f, -0.75f), new Vector3(0.5f, 0.2f, 0.22f), new Color(0.95f, 0.92f, 0.8f, 0f), 7f),
                },
                // 小型トラック（白。平らな荷台）・箱型トラック（白い荷箱）・路線バス（白に色の帯。夜は窓が灯る）。
                new CarKind { Name = "Truck", Length = 4.7f, Width = 1.7f, Weight = 0.06f, Painted = false, Large = true, Extra = (m, k) => Truck(m, k, false) },
                new CarKind { Name = "BoxTruck", Length = 6.2f, Width = 1.95f, Weight = 0.05f, Painted = false, Large = true, Extra = (m, k) => Truck(m, k, true) },
                new CarKind { Name = "Bus", Length = 10.5f, Width = 2.49f, Weight = 0.03f, Painted = false, Large = true, Extra = Bus },
            };
        }

        // 乗用車の体: 下の車体（前後を少しすぼめる）・窓の箱（上ほど細い）・屋根・タイヤ・バンパー・灯火。
        private static void CarBody(MeshParts mesh, CarKind k)
        {
            float half = k.Length * 0.5f;
            float w = k.Width * 0.5f;
            Color body = k.Painted ? new Color(1f, 1f, 1f, 1f) : new Color(k.Body.r, k.Body.g, k.Body.b, 0f);
            const float Bottom = 0.25f;
            mesh.Hexa(new[]
            {
                new Vector3(-w, Bottom, -half), new Vector3(w, Bottom, -half), new Vector3(w, Bottom, half), new Vector3(-w, Bottom, half),
                new Vector3(-w, k.Belt, -half + 0.12f), new Vector3(w, k.Belt, -half + 0.12f), new Vector3(w, k.Belt, half - 0.18f), new Vector3(-w, k.Belt, half - 0.18f),
            }, body);
            mesh.Hexa(new[]
            {
                new Vector3(-w + 0.05f, k.Belt, k.CabinBack), new Vector3(w - 0.05f, k.Belt, k.CabinBack), new Vector3(w - 0.05f, k.Belt, k.CabinFront), new Vector3(-w + 0.05f, k.Belt, k.CabinFront),
                new Vector3(-w + 0.16f, k.Roof, k.RoofBack), new Vector3(w - 0.16f, k.Roof, k.RoofBack), new Vector3(w - 0.16f, k.Roof, k.RoofFront), new Vector3(-w + 0.16f, k.Roof, k.RoofFront),
            }, Glass);
            mesh.Box(new Vector3(0f, k.Roof + 0.025f, (k.RoofBack + k.RoofFront) * 0.5f), new Vector3(k.Width - 0.3f, 0.05f, k.RoofFront - k.RoofBack), body);
            Wheels(mesh, k.Length, k.Width);
            Lights(mesh, k.Length, k.Width, k.Belt - 0.15f);
        }

        private static void Wheels(MeshParts mesh, float length, float width)
        {
            const float Radius = 0.32f;
            float axle = length * 0.5f - Mathf.Max(0.75f, length * 0.17f);
            foreach (float x in new[] { -1f, 1f })
            {
                foreach (float z in new[] { -axle, axle })
                {
                    mesh.Box(new Vector3(x * (width * 0.5f - 0.12f), Radius, z), new Vector3(0.24f, Radius * 2f, Radius * 2f), Tire);
                }
            }
        }

        private static void Lights(MeshParts mesh, float length, float width, float height)
        {
            float half = length * 0.5f;
            foreach (float x in new[] { -1f, 1f })
            {
                mesh.Box(new Vector3(x * (width * 0.5f - 0.27f), height, half - 0.16f), new Vector3(0.34f, 0.12f, 0.06f), Lamp, 1f);
                mesh.Box(new Vector3(x * (width * 0.5f - 0.2f), height + 0.03f, -half + 0.1f), new Vector3(0.3f, 0.13f, 0.06f), Tail, 2f);
            }
            mesh.Box(new Vector3(0f, 0.38f, half - 0.04f), new Vector3(width, 0.18f, 0.1f), Trim);
            mesh.Box(new Vector3(0f, 0.38f, -half + 0.04f), new Vector3(width, 0.18f, 0.1f), Trim);
        }

        // トラック: 前のキャブ（白）と、平らな荷台（あおり）か荷箱。
        private static void Truck(MeshParts mesh, CarKind k, bool box)
        {
            float half = k.Length * 0.5f;
            float w = k.Width * 0.5f;
            var white = new Color(0.9f, 0.9f, 0.88f, 0f);
            const float CabLength = 1.6f;
            float cabBack = half - CabLength;
            mesh.Box(new Vector3(0f, 0.75f, half - CabLength * 0.5f), new Vector3(k.Width, 1f, CabLength), white);
            mesh.Hexa(new[]
            {
                new Vector3(-w + 0.04f, 1.25f, cabBack + 0.1f), new Vector3(w - 0.04f, 1.25f, cabBack + 0.1f), new Vector3(w - 0.04f, 1.25f, half - 0.2f), new Vector3(-w + 0.04f, 1.25f, half - 0.2f),
                new Vector3(-w + 0.1f, 2.05f, cabBack + 0.1f), new Vector3(w - 0.1f, 2.05f, cabBack + 0.1f), new Vector3(w - 0.1f, 2.05f, half - 0.45f), new Vector3(-w + 0.1f, 2.05f, half - 0.45f),
            }, Glass);
            mesh.Box(new Vector3(0f, 2.08f, cabBack + (CabLength - 0.45f) * 0.5f), new Vector3(k.Width - 0.2f, 0.06f, CabLength - 0.55f), white);
            float bedLength = k.Length - CabLength - 0.1f;
            float bedCenter = -half + bedLength * 0.5f;
            if (box)
            {
                mesh.Box(new Vector3(0f, 1.85f, bedCenter), new Vector3(k.Width + 0.05f, 2.3f, bedLength), new Color(0.82f, 0.83f, 0.84f, 0f));
            }
            else
            {
                mesh.Box(new Vector3(0f, 0.75f, bedCenter), new Vector3(k.Width, 0.12f, bedLength), new Color(0.75f, 0.76f, 0.77f, 0f));
                foreach (float x in new[] { -1f, 1f })
                {
                    mesh.Box(new Vector3(x * (w - 0.03f), 0.98f, bedCenter), new Vector3(0.06f, 0.4f, bedLength), white);
                }
                mesh.Box(new Vector3(0f, 0.98f, -half + 0.03f), new Vector3(k.Width, 0.4f, 0.06f), white);
            }
            mesh.Box(new Vector3(0f, 0.45f, bedCenter), new Vector3(0.6f, 0.25f, bedLength), Trim);
            Wheels(mesh, k.Length, k.Width);
            Lights(mesh, k.Length, k.Width, 0.65f);
        }

        // 路線バス: 白い箱の車体、横の窓の帯（夜は車内の灯りで光る）、色の帯、前の大きな窓。
        private static void Bus(MeshParts mesh, CarKind k)
        {
            float half = k.Length * 0.5f;
            float w = k.Width * 0.5f;
            var white = new Color(0.92f, 0.92f, 0.9f, 0f);
            mesh.Box(new Vector3(0f, 1.7f, 0f), new Vector3(k.Width, 2.7f, k.Length), white);
            mesh.Box(new Vector3(0f, 3.1f, 0f), new Vector3(k.Width - 0.2f, 0.1f, k.Length - 1.2f), new Color(0.8f, 0.8f, 0.8f, 0f));
            foreach (float x in new[] { -1f, 1f })
            {
                // 窓の帯（車内が灯る）と、窓の下の色の帯。
                mesh.Box(new Vector3(x * (w + 0.01f), 2.05f, -0.4f), new Vector3(0.04f, 1.05f, k.Length - 2.6f), new Color(0.1f, 0.12f, 0.14f, 0f), 7f);
                mesh.Box(new Vector3(x * (w + 0.012f), 1.2f, 0f), new Vector3(0.04f, 0.22f, k.Length - 0.2f), new Color(0.15f, 0.48f, 0.36f, 0f));
            }
            mesh.Box(new Vector3(0f, 1.95f, half + 0.01f), new Vector3(k.Width - 0.2f, 1.5f, 0.04f), Glass);
            mesh.Box(new Vector3(0f, 2.85f, half + 0.02f), new Vector3(1.4f, 0.25f, 0.04f), new Color(0.05f, 0.05f, 0.05f, 0f), 7f);
            Wheels(mesh, k.Length, k.Width);
            Lights(mesh, k.Length, k.Width, 0.75f);
        }

        // ---- 人影の網 ----

        // 人影（高さ約 1.7 m。前は +Z。足・腕は付け根で振る〈uv の部品の番号と付け根の高さ〉）。服の上は _BaseColor で塗り分ける。
        private static Mesh Person(string name, bool skirt, bool bag)
        {
            var mesh = new MeshParts();
            var cloth = new Color(1f, 1f, 1f, 1f);
            var trousers = new Color(0.1f, 0.11f, 0.14f, 0f);
            var skin = new Color(0.78f, 0.62f, 0.5f, 0f);
            var hair = new Color(0.06f, 0.05f, 0.05f, 0f);
            var shoe = new Color(0.05f, 0.05f, 0.05f, 0f);
            const float Hip = 0.88f;
            const float Shoulder = 1.4f;
            foreach (float side in new[] { -1f, 1f })
            {
                float leg = side < 0f ? 3f : 4f;
                float arm = side < 0f ? 5f : 6f;
                float legWidth = skirt ? 0.1f : 0.13f;
                mesh.Box(new Vector3(side * 0.085f, Hip * 0.5f + 0.04f, 0f), new Vector3(legWidth, Hip - 0.08f, 0.14f), skirt ? skin : trousers, leg, Hip);
                mesh.Box(new Vector3(side * 0.085f, 0.04f, 0.03f), new Vector3(0.11f, 0.08f, 0.26f), shoe, leg, Hip);
                mesh.Box(new Vector3(side * 0.255f, Shoulder - 0.29f, 0f), new Vector3(0.085f, 0.58f, 0.1f), cloth, arm, Shoulder - 0.02f);
                mesh.Box(new Vector3(side * 0.255f, Shoulder - 0.64f, 0f), new Vector3(0.07f, 0.12f, 0.08f), skin, arm, Shoulder - 0.02f);
            }
            if (bag)
            {
                // 右手に提げたかばん（腕と一緒に振れる）。
                mesh.Box(new Vector3(0.29f, Shoulder - 0.8f, 0f), new Vector3(0.1f, 0.3f, 0.36f), new Color(0.08f, 0.07f, 0.06f, 0f), 6f, Shoulder - 0.02f);
            }
            if (skirt)
            {
                mesh.Hexa(new[]
                {
                    new Vector3(-0.24f, Hip - 0.36f, -0.17f), new Vector3(0.24f, Hip - 0.36f, -0.17f), new Vector3(0.24f, Hip - 0.36f, 0.17f), new Vector3(-0.24f, Hip - 0.36f, 0.17f),
                    new Vector3(-0.17f, Hip + 0.02f, -0.11f), new Vector3(0.17f, Hip + 0.02f, -0.11f), new Vector3(0.17f, Hip + 0.02f, 0.11f), new Vector3(-0.17f, Hip + 0.02f, 0.11f),
                }, new Color(0.16f, 0.15f, 0.2f, 0f));
            }
            // 胴（腰から肩へ広がる）・首・頭（髪）。
            mesh.Hexa(new[]
            {
                new Vector3(-0.17f, Hip, -0.1f), new Vector3(0.17f, Hip, -0.1f), new Vector3(0.17f, Hip, 0.1f), new Vector3(-0.17f, Hip, 0.1f),
                new Vector3(-0.22f, Shoulder, -0.11f), new Vector3(0.22f, Shoulder, -0.11f), new Vector3(0.22f, Shoulder, 0.11f), new Vector3(-0.22f, Shoulder, 0.11f),
            }, cloth);
            mesh.Box(new Vector3(0f, Shoulder + 0.04f, 0f), new Vector3(0.09f, 0.08f, 0.09f), skin);
            mesh.Box(new Vector3(0f, Shoulder + 0.18f, 0.01f), new Vector3(0.18f, 0.22f, 0.2f), skin);
            mesh.Box(new Vector3(0f, Shoulder + 0.27f, -0.015f), new Vector3(0.2f, 0.1f, 0.22f), hair);
            return mesh.Save(MeshStage, name);
        }

        /// <summary>
        /// 平らな面の多面体を集めて 1 つの網にする道具。頂点の色（不透明度 1 は _BaseColor で塗り分ける所）と、UV（x が部品の番号、
        /// y が手足の付け根の高さ）を Airerage/StreetActor が読む。面は外向き（面の中心が体の中心から離れる向き）に表を向ける。
        /// </summary>
        private sealed class MeshParts
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Color> _colors = new List<Color>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _triangles = new List<int>();

            public void Box(Vector3 center, Vector3 size, Color color, float part = 0f, float pivot = 0f)
            {
                Vector3 h = size * 0.5f;
                Hexa(new[]
                {
                    center + new Vector3(-h.x, -h.y, -h.z), center + new Vector3(h.x, -h.y, -h.z), center + new Vector3(h.x, -h.y, h.z), center + new Vector3(-h.x, -h.y, h.z),
                    center + new Vector3(-h.x, h.y, -h.z), center + new Vector3(h.x, h.y, -h.z), center + new Vector3(h.x, h.y, h.z), center + new Vector3(-h.x, h.y, h.z),
                }, color, part, pivot);
            }

            /// <summary>8 つの角（下の 4 つ: -X-Z, +X-Z, +X+Z, -X+Z、上の 4 つも同じ順）の凸な六面体。</summary>
            public void Hexa(Vector3[] c, Color color, float part = 0f, float pivot = 0f)
            {
                Vector3 center = Vector3.zero;
                foreach (Vector3 corner in c)
                {
                    center += corner / 8f;
                }
                Face(c[0], c[1], c[2], c[3], center, color, part, pivot);
                Face(c[4], c[5], c[6], c[7], center, color, part, pivot);
                Face(c[3], c[2], c[6], c[7], center, color, part, pivot);
                Face(c[0], c[1], c[5], c[4], center, color, part, pivot);
                Face(c[0], c[3], c[7], c[4], center, color, part, pivot);
                Face(c[1], c[2], c[6], c[5], center, color, part, pivot);
            }

            /// <summary>平らな長方形（XZ の面。min・max は x と z）を高さ y に置く（上向き）。</summary>
            public void Flat(Vector2 min, Vector2 max, float y, Color color)
            {
                var a = new Vector3(min.x, y, min.y);
                var b = new Vector3(max.x, y, min.y);
                var c = new Vector3(max.x, y, max.y);
                var d = new Vector3(min.x, y, max.y);
                Face(a, b, c, d, new Vector3((min.x + max.x) * 0.5f, y - 1f, (min.y + max.y) * 0.5f), color, 0f, 0f);
            }

            /// <summary>ひし形の輪郭（中心・幅・長さ・線の太さ。上向きの平らな線 4 本）。</summary>
            public void Diamond(Vector2 center, float width, float length, float line, float y, Color color)
            {
                var points = new[]
                {
                    new Vector2(center.x, center.y - length * 0.5f), new Vector2(center.x + width * 0.5f, center.y),
                    new Vector2(center.x, center.y + length * 0.5f), new Vector2(center.x - width * 0.5f, center.y),
                };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[(i + 1) % 4];
                    Vector2 across = new Vector2(-(b - a).y, (b - a).x).normalized * (line * 0.5f);
                    Face(new Vector3(a.x - across.x, y, a.y - across.y), new Vector3(b.x - across.x, y, b.y - across.y),
                        new Vector3(b.x + across.x, y, b.y + across.y), new Vector3(a.x + across.x, y, a.y + across.y),
                        new Vector3(center.x, y - 1f, center.y), color, 0f, 0f);
                }
            }

            private void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 bodyCenter, Color color, float part, float pivot)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-10f)
                {
                    normal = Vector3.Cross(c - a, d - a);
                }
                Vector3 outward = (a + b + c + d) * 0.25f - bodyCenter;
                bool flip = Vector3.Dot(normal, outward) < 0f;
                normal = (flip ? -normal : normal).normalized;
                int start = _vertices.Count;
                foreach (Vector3 corner in flip ? new[] { a, d, c, b } : new[] { a, b, c, d })
                {
                    _vertices.Add(corner);
                    _normals.Add(normal);
                    _colors.Add(color);
                    _uvs.Add(new Vector2(part, pivot));
                }
                _triangles.Add(start);
                _triangles.Add(start + 1);
                _triangles.Add(start + 2);
                _triangles.Add(start);
                _triangles.Add(start + 2);
                _triangles.Add(start + 3);
            }

            public Mesh Save(string stage, string name)
            {
                string folder = $"Assets/_Project/Meshes/Stages/{stage}";
                string path = $"{folder}/{name}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    SandboxWireSceneBuilder.EnsureFolder(folder);
                    mesh = new Mesh { name = name };
                    AssetDatabase.CreateAsset(mesh, path);
                }
                mesh.Clear();
                mesh.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }
    }
}
