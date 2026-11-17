using System;
using System.Collections.Generic;
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
    /// - 足場: 地形の箱の見た目を外し、同じ大きさの面取りした箱（StageBlock。V1）に。材質 Airerage/StageSurface（V3・V7・V8・V10・V22）。
    ///   小さな浮いたワイヤーの刺さらない足場は、見た目だけゆっくり上下する（V21）。
    /// - 光・空・霧（V11・V15）: グラデーションの空（Airerage/GradientSky）、環境光、遠くを空の色に溶かす霧。
    /// - 遠景（V14・V17）: 近・中・遠の 3 層の影の形（塔・浮かぶ島）、塔の先の点滅する灯り、ゆっくり回る輪、横切る飛行船。
    /// - 光の格子（V18）: 撃破の線のさらに下に広がる格子の板。
    /// - 撃破の線の膜（V19）: 撃破の範囲の 4 辺の外に光の膜（キャラクターが近いほど濃い）。今までの赤い枠の線は外す。
    /// - 仕上げの画面処理（V13）: 光る物だけのにじみ（ブルーム）、色の調整、画面の端を少し暗く（ステージの Volume）。
    /// 光の柱（V12）は拠点の表示（BankView）、触れたときの輪（V22）とキャラクターの位置は SurfaceImpactFeed（試合のシーン）。
    /// </summary>
    internal static class StageDressing
    {
        private const string MaterialRoot = "Assets/_Project/Materials/Stages";

        // 小さな浮いた足場（呼吸させる）の大きさの上限（m）。
        private const float BreathMaxWidth = 4f;
        private const float BreathMaxHeight = 1f;

        // 撃破の線の膜の幅（m。線から外へ）。
        private const float MembraneWidth = 6f;

        /// <summary>ステージのシーン（開いている）を look で飾る。地形・撃破の範囲を作った後に呼ぶ。</summary>
        public static void Apply(StageLook look, Vector2 boundsMin, Vector2 boundsMax)
        {
            string folder = $"{MaterialRoot}/{look.Id}/Look";
            ApplyLighting(look, folder);
            DressTerrain(look, folder);
            CreateMembranes(look, folder, boundsMin, boundsMax);
            CreateGrid(look, folder, boundsMin);
            CreateBackdrop(look, folder, boundsMin);
            CreateVolume(look, folder);
        }

        /// <summary>足場の材質（ワイヤーが刺さるか）。</summary>
        public static Material BlockMaterial(StageLook look, string folder, bool attachable)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(attachable ? "BlockAttachable" : "Block", "Airerage/StageSurface", m =>
            {
                m.SetColor("_BaseColor", attachable ? look.AttachableSide : look.BlockSide);
                m.SetColor("_TopColor", attachable ? look.AttachableTop : look.BlockTop);
                m.SetColor("_ShadeColor", look.ShadeTint);
                m.SetColor("_RimColor", look.RimColor);
                m.SetFloat("_RimStrength", look.RimStrength);
                m.SetFloat("_HeightLow", look.HeightLow);
                m.SetFloat("_HeightHigh", look.HeightHigh);
                m.SetColor("_TrimColor", look.TrimColor);
                m.SetColor("_ImpactColor", look.ImpactColor);
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
                m.SetFloat("_SunSize", 2600f);
                m.SetFloat("_SunGlow", 0.25f);
                // 地平線のもやは目の高さより下へ（足場の後ろは青い空にして、白い上の面が溶けないように）。
                m.SetFloat("_HorizonHeight", look.HorizonHeight);
            }, folder);
            RenderSettings.skybox = sky;
            RenderSettings.sun = light;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor = look.AmbientGround;
            RenderSettings.fog = true;
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
                m.SetColor("_LineColor", look.GridColor);
                m.SetFloat("_Spacing", 10f);
                m.SetFloat("_FadeStart", 60f);
                m.SetFloat("_FadeEnd", 360f);
            }, folder);
            GameObject grid = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Plane, "LightGrid", material, new Vector3(140f, 1f, 140f));
            grid.transform.position = new Vector3(0f, boundsMin.y - look.GridDepth, 360f);
            NoShadows(grid);
        }

        // 遠景（V14・V17）: 遠い塔の並び、中くらいの距離の浮かぶ島（塔・灯り・回る輪）、近くの柱（遊ぶ所の真後ろは空ける）、横切る飛行船。
        private static void CreateBackdrop(StageLook look, string folder, Vector2 boundsMin)
        {
            float hazeTop = boundsMin.y - 8f;
            Material far = BackdropMaterial("BackdropFar", look.FarColor, look, folder, hazeTop, 0f);
            Material mid = BackdropMaterial("BackdropMid", look.MidColor, look, folder, hazeTop, 0f);
            Material near = BackdropMaterial("BackdropNear", look.NearColor, look, folder, hazeTop, 0f);
            Material beacon = BackdropMaterial("BackdropBeacon", look.BeaconColor, look, folder, hazeTop, 1.1f);
            var random = new Random(look.Seed + 1);
            var root = new GameObject("Backdrop").transform;

            // 遠い層: 下のもやから立つ塔の並び（段のある頭）。
            Transform farRoot = Child(root, "Far");
            // 塔はかたまりで置き、かたまりの間をあける（等間隔に並べると単調になる）。
            float cursor = -800f;
            while (cursor < 800f)
            {
                int cluster = random.Next(2, 6);
                for (int i = 0; i < cluster && cursor < 800f; i++)
                {
                    float width = Range(random, 10f, 34f);
                    float x = cursor + width * 0.5f;
                    float z = Range(random, 440f, 580f);
                    // 真ん中（遊ぶ所の後ろ）ほど低く、外ほど高い（地平線の近くに沈めて、足場の後ろをすっきりさせる）。ときどき高い塔。
                    float side = Mathf.Clamp01(Mathf.Abs(x) / 600f);
                    float top = Range(random, -25f, 15f) + side * Range(random, 10f, 60f);
                    if (random.NextDouble() < 0.12 + side * 0.2)
                    {
                        top += Range(random, 40f, 90f);
                    }
                    Tower(farRoot, far, beacon, new Vector3(x, top, z), width, top + 260f, random, beaconChance: 0.35f);
                    cursor += width + Range(random, 2f, 8f);
                }
                cursor += Range(random, 30f, 90f);
            }

            // 中くらいの層: 浮かぶ島（板と、上の塔、下の段々の底）。いくつかの塔の先に回る輪。
            Transform midRoot = Child(root, "Mid");
            for (int i = 0; i < 8; i++)
            {
                // 左右の外側か高い所だけに置く（遊ぶ所の後ろに平たい板が重なると、足場と見分けにくい）。
                bool high = i == 3 || i == 4;
                float x = high ? (i == 3 ? -1f : 1f) * Range(random, 60f, 110f) : (i < 4 ? -1f : 1f) * Range(random, 170f, 480f);
                float y = high ? Range(random, 70f, 95f) : Range(random, 5f, 55f);
                float z = Range(random, 280f, 360f);
                FloatingIsland(midRoot, mid, beacon, new Vector3(x, y, z), random, ring: i == 1 || i == 6);
            }

            // 近い層: 左右の外側と下だけに、大きな柱と浮かぶ板（遊ぶ所の真後ろを空けて、足場と見分けやすく）。
            Transform nearRoot = Child(root, "Near");
            float[] nearX = { -230f, -165f, -110f, 110f, 165f, 230f };
            for (int i = 0; i < nearX.Length; i++)
            {
                float z = Range(random, 90f, 125f);
                float top = Range(random, -40f, 15f);
                Tower(nearRoot, near, beacon, new Vector3(nearX[i] + Range(random, -8f, 8f), top, z), Range(random, 10f, 18f), top + 220f, random,
                    beaconChance: 0.6f);
            }
            // 遊ぶ所の下には何も置かない（下に床があるように見えないように。落ちたら撃破）。

            // 横切る飛行船（V17）。
            Transform ships = Child(root, "Airships");
            for (int i = 0; i < 3; i++)
            {
                var ship = Airship(ships, far, beacon, new Vector3(-600f + i * 420f, Range(random, 50f, 95f), Range(random, 300f, 380f)),
                    Range(random, 34f, 52f));
                float speed = Range(random, 4f, 8f) * (i % 2 == 0 ? 1f : -1f);
                ship.AddComponent<BackdropMotion>().ConfigureDrift(new Vector3(speed, 0f, 0f), -760f, 760f);
                if (speed < 0f)
                {
                    ship.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
        }

        private static Material BackdropMaterial(string name, Color color, StageLook look, string folder, float hazeTop, float blink)
        {
            return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(name, "Airerage/StageBackdrop", m =>
            {
                m.SetColor("_BaseColor", color);
                m.SetColor("_HazeColor", look.HazeColor);
                m.SetFloat("_HazeTop", hazeTop);
                m.SetFloat("_HazeBottom", hazeTop - 150f);
                m.SetFloat("_Blink", blink);
                m.SetFloat("_BandSpacing", 9f);
                m.SetFloat("_BandStrength", blink > 0f ? 0f : 0.07f);
            }, folder);
        }

        // 塔: 下から top まで伸びる箱と、少し細い頭の段。先に灯りを付けることがある。
        private static void Tower(Transform parent, Material body, Material beacon, Vector3 topCenter, float width, float height, Random random,
            float beaconChance)
        {
            var tower = new GameObject("Tower").transform;
            tower.SetParent(parent, false);
            tower.position = topCenter;
            float depth = width * Range(random, 0.6f, 1.1f);
            Box(tower, "Body", body, new Vector3(0f, -height * 0.5f, 0f), new Vector3(width, height, depth));
            float capHeight = Range(random, 4f, 14f);
            float capWidth = width * Range(random, 0.45f, 0.75f);
            Box(tower, "Cap", body, new Vector3(0f, capHeight * 0.5f, 0f), new Vector3(capWidth, capHeight, depth * 0.7f));
            if (random.NextDouble() < beaconChance)
            {
                Box(tower, "Beacon", beacon, new Vector3(0f, capHeight + 1.2f, 0f), new Vector3(1.8f, 1.8f, 1.8f));
            }
        }

        // 浮かぶ島: 板の上に塔、下に段々の底。ring なら一番高い塔の先に、ゆっくり回る輪。
        private static void FloatingIsland(Transform parent, Material body, Material beacon, Vector3 center, Random random, bool ring)
        {
            var island = new GameObject("Island").transform;
            island.SetParent(parent, false);
            island.position = center;
            float width = Range(random, 34f, 70f);
            float depth = Range(random, 16f, 26f);
            Box(island, "Slab", body, Vector3.zero, new Vector3(width, 6f, depth));
            Box(island, "Keel", body, new Vector3(0f, -6f, 0f), new Vector3(width * 0.62f, 6f, depth * 0.7f));
            Box(island, "KeelTip", body, new Vector3(0f, -11f, 0f), new Vector3(width * 0.28f, 4f, depth * 0.4f));
            int towers = random.Next(1, 4);
            float tallest = 0f;
            float tallestX = 0f;
            for (int t = 0; t < towers; t++)
            {
                float x = Range(random, -width * 0.35f, width * 0.35f);
                float h = Range(random, 10f, 38f);
                float w = Range(random, 4f, 10f);
                Box(island, "Tower", body, new Vector3(x, 3f + h * 0.5f, Range(random, -3f, 3f)), new Vector3(w, h, w));
                if (h > tallest)
                {
                    tallest = h;
                    tallestX = x;
                }
            }
            Box(island, "Beacon", beacon, new Vector3(tallestX, 3f + tallest + 1f, 0f), new Vector3(1.4f, 1.4f, 1.4f));
            if (ring)
            {
                var spinner = new GameObject("Ring").transform;
                spinner.SetParent(island, false);
                spinner.localPosition = new Vector3(tallestX, 3f + tallest * 0.75f, 0f);
                const int Pieces = 14;
                float radius = Range(random, 7f, 11f);
                for (int p = 0; p < Pieces; p++)
                {
                    float angle = p * 360f / Pieces;
                    GameObject piece = Box(spinner, "Piece", body, Quaternion.Euler(0f, angle, 0f) * new Vector3(radius, 0f, 0f),
                        new Vector3(0.9f, 0.9f, radius * 2f * Mathf.PI / Pieces * 1.05f));
                    piece.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                }
                spinner.localRotation = Quaternion.Euler(Range(random, -20f, 20f), 0f, Range(random, -12f, 12f));
                spinner.gameObject.AddComponent<BackdropMotion>().ConfigureSpin(Vector3.up, Range(random, 8f, 16f));
            }
        }

        // 飛行船: 細長い胴、先の丸み、尾のひれ、腹の灯り（右向き）。
        private static GameObject Airship(Transform parent, Material body, Material beacon, Vector3 position, float length)
        {
            var ship = new GameObject("Airship");
            ship.transform.SetParent(parent, false);
            ship.transform.position = position;
            float girth = length * 0.16f;
            Box(ship.transform, "Hull", body, Vector3.zero, new Vector3(length, girth, girth * 1.1f));
            GameObject nose = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Sphere, "Nose", body, new Vector3(girth * 1.4f, girth, girth * 1.1f));
            nose.transform.SetParent(ship.transform, false);
            nose.transform.localPosition = new Vector3(length * 0.5f, 0f, 0f);
            NoShadows(nose);
            Box(ship.transform, "Fin", body, new Vector3(-length * 0.46f, girth * 0.7f, 0f), new Vector3(length * 0.12f, girth * 0.9f, 0.6f));
            Box(ship.transform, "Gondola", body, new Vector3(length * 0.08f, -girth * 0.65f, 0f), new Vector3(length * 0.3f, girth * 0.35f, girth * 0.5f));
            Box(ship.transform, "Light", beacon, new Vector3(length * 0.08f, -girth * 0.9f, 0f), new Vector3(length * 0.26f, 0.5f, 0.6f));
            return ship;
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
