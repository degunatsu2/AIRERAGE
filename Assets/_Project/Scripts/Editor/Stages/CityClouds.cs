using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ビル街（SKYLINE の昼夜）の雲海（2026-11-25 の要望: ステージは非常に高い高層ビルの上。足場のビルの根元を雲で隠し、遥か下に大通りが
    /// 見えるように。他のゲームの天空・高層ビルのステージを参考に。docs/15 6.71）。シェーダーは Airerage/CloudSea。
    /// - 雲の層（水平な板 3 枚。上ほど細かく薄い）: 遊ぶ屋上（y 0〜15）と撃破の線（y −22）のすぐ下から、地上（CityStreet.Ground）までの間を
    ///   埋める。重ねた層がカメラの動きで少しずつずれて厚みに見える（視差）。西新宿の高層ビルは、頭が雲海の上に突き出す（実際の雲海の
    ///   写真の構図）。2026-11-25 の指摘（大通りの上だけ四角く開けた切れ間が不自然）で、決まった切れ間をなくし、雲の量を減らして、大きな
    ///   周期で雲の多い所・少ない所を作る（_Patchiness）。3 枚の層の切れ間は別々の所にあり、重なって抜けた所から遥か下の地上がのぞく。
    /// - 雲の土手（縦の板）: 足場のビルの手前と後ろに、上の縁がもくもくした雲を置いて根元を隠す（ほぼ水平に見る遊ぶ画面では、水平な層は
    ///   薄い線にしか見えないため）。奥の土手は 1 枚につなげ、上の縁を大きくうねらせて（_BankDip）、低く下がった所から奥の雲海と地上を
    ///   のぞかせる（左右に分けた真ん中の切れ間はなくした）。
    /// - 2026-11-26 の要望（手前の雲が厚すぎて大通りが全く見えない・奥の雲海は板のように薄い。手前は厚く奥は薄いまま、差を縮める）:
    ///   手前ほど雲の層を薄く（_NearThin）し、大通りへの見通しを遮る真後ろと奥の土手を少し透かして抜けを作る（_BankHoles）。奥には、
    ///   いちばん上の層と同じ模様の薄い層を少し上に 4 枚重ね、厚い所だけを描いて（積み重ねた板で体積に見せる。スタイライズドな空の
    ///   雲の層の重ね方・積み重ねた板の霧と同じ考え方）、こぶの頂が盛り上がって見えるようにする。
    /// - 昼は日の当たる面が白く影が青みの灰色、夜は月明かりの暗い青紫で、下から街の明かり（ネオンのピンク）に照らされる。
    /// 表示だけ（当たりはない。撃破の線より下）。
    /// </summary>
    internal static class CityClouds
    {
        /// <summary>雲の層のいちばん上の高さ（撃破の線 −22 のすぐ下。ワールドの y）。</summary>
        public const float SeaTop = -27f;

        // 雲の層: 名前・高さ・ノイズの細かさ・雲の量の閾値（大きいほど雲が少ない）・大きな周期のむら・不透明さ・流れ・手前の薄さ・
        // 模様の種（厚みの層は下の層と同じ種）・厚みの上乗せ（0 は下の層）。
        // 2026-11-25: 閾値を上げて雲を減らし、むらを大きくした（前は 0.47・0.4・0.33、むら 0.25）。
        // 2026-11-26: 手前（z 70 まで）ほど薄く、z 220 で元どおり。いちばん上の層の上に厚みの層を 4 枚（奥だけ）。
        private static readonly (string Name, float Y, float Scale, float Coverage, float Patchiness, float Opacity, Vector2 Drift, float NearThin,
            int Seed, float Lift)[] Layers =
        {
            ("CloudSeaTop", SeaTop, 0.011f, 0.53f, 0.75f, 0.86f, new Vector2(1.4f, 0.35f), 0.07f, 1, 0f),
            ("CloudSeaMid", -37f, 0.0075f, 0.48f, 0.7f, 0.9f, new Vector2(0.9f, 0.25f), 0.09f, 2, 0f),
            ("CloudSeaLow", -52f, 0.005f, 0.43f, 0.65f, 0.93f, new Vector2(0.55f, 0.15f), 0.1f, 3, 0f),
            ("CloudSeaShell1", SeaTop + 2.4f, 0.011f, 0.53f, 0.75f, 0.8f, new Vector2(1.4f, 0.35f), 0f, 1, 0.035f),
            ("CloudSeaShell2", SeaTop + 4.8f, 0.011f, 0.53f, 0.75f, 0.76f, new Vector2(1.4f, 0.35f), 0f, 1, 0.07f),
            ("CloudSeaShell3", SeaTop + 7.2f, 0.011f, 0.53f, 0.75f, 0.72f, new Vector2(1.4f, 0.35f), 0f, 1, 0.105f),
            ("CloudSeaShell4", SeaTop + 9.6f, 0.011f, 0.53f, 0.75f, 0.68f, new Vector2(1.4f, 0.35f), 0f, 1, 0.14f),
        };

        // 手前の薄さが効く奥行き（ワールドの z。この手前ほど薄く、奥で元どおり）と、厚みの層を出す奥行き。
        private const float NearThinStart = 70f;
        private const float NearThinEnd = 220f;
        private const float ShellNear = 170f;
        private const float ShellFar = 380f;

        // 雲の土手: 名前・奥行き z・中心の x・幅・上の縁の高さ・こぶの高さ・うねり（上の縁が大きく下がる深さ）・下の端・不透明さ・抜け。
        // 手前と真後ろの土手は、遊ぶ画面で足場のビルの根元を隠す（うねりは小さく）。奥の 2 枚はつなげて、大きくうねらせる。
        // 2026-11-26: 大通りへの見通しを遮る真後ろ（z 17）と奥（z 66）の土手を少し透かし、抜けを作った（前は不透明さ 0.97・抜けなし）。
        // 見下ろすと手前の土手（z −7）が平らな白い壁になって大通りを隠していたので、手前の土手にも抜けを作り、真後ろと奥の抜けを強めた
        // （抜けは風で流れていくので、隙間から時々遥か下がのぞく。手前の土手はいちばん厚いまま。
        // 足場のビルは根元の柱 TowerFoot が地上まで続くので、透けても浮いて見えない）。
        private static readonly (string Name, float Z, float X, float Width, float Top, float Billow, float Dip, float Bottom, float Opacity, float Holes)[] Banks =
        {
            ("CloudBankFront", -7f, 0f, 300f, -25f, 7f, 0f, -95f, 0.94f, 0.55f),
            ("CloudBankBehind", 17f, 0f, 380f, -22f, 9f, 10f, -95f, 0.84f, 0.75f),
            ("CloudBankRow", 66f, 0f, 560f, -19f, 12f, 38f, -100f, 0.88f, 0.72f),
            ("CloudBankFar", 150f, 0f, 820f, -21f, 14f, 42f, -100f, 0.97f, 0f),
        };

        public static void Build(StageDecor decor, bool night)
        {
            var root = new GameObject("CloudSea").transform;
            root.SetParent(decor.Root, false);
            // 夜は月明かりの当たる上の面を淡い紫に、影を濃い藍に、下からの街の明かり（ネオンのピンク）を強めに（雲の形が読めるように）。
            Color top = night ? new Color(0.56f, 0.5f, 0.74f) : new Color(1f, 0.99f, 0.97f);
            Color shade = night ? new Color(0.06f, 0.05f, 0.13f) : new Color(0.6f, 0.68f, 0.8f);
            Color glow = night ? new Color(0.44f, 0.12f, 0.34f) : Color.clear;
            for (int i = 0; i < Layers.Length; i++)
            {
                var layer = Layers[i];
                int seed = layer.Seed;
                Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(layer.Name, "Airerage/CloudSea", m =>
                {
                    Common(m, top, shade, glow, seed);
                    m.SetFloat("_Mode", 0f);
                    m.SetFloat("_Opacity", layer.Opacity);
                    m.SetFloat("_Coverage", layer.Coverage);
                    m.SetFloat("_Patchiness", layer.Patchiness);
                    m.SetFloat("_PatchScale", 0.22f);
                    m.SetFloat("_Softness", 0.16f);
                    m.SetFloat("_Scale", layer.Scale);
                    m.SetVector("_Drift", new Vector4(layer.Drift.x, layer.Drift.y, 0f, 0f));
                    m.SetFloat("_NearFade", 6f);
                    m.SetFloat("_NearThin", layer.NearThin);
                    m.SetFloat("_NearThinStart", NearThinStart);
                    m.SetFloat("_NearThinEnd", NearThinEnd);
                    m.SetFloat("_ShellLift", layer.Lift);
                    m.SetFloat("_ShellNear", ShellNear);
                    m.SetFloat("_ShellFar", ShellFar);
                }, decor.Folder);
                // 上の層ほど手前に描く（厚みの層はいちばん上の層の上）。
                material.renderQueue = layer.Lift > 0f ? 2977 + i : 2977 - i;
                // 厚みの層は、出す奥行き（ShellNear）より奥だけの板にする（手前の画面を余計に塗らない）。
                float nearZ = layer.Lift > 0f ? ShellNear - 10f : -100f;
                const float FarZ = 2700f;
                GameObject plane = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, layer.Name, material, new Vector3(3400f, FarZ - nearZ, 1f));
                plane.transform.SetParent(root, false);
                plane.transform.SetPositionAndRotation(new Vector3(0f, layer.Y, (nearZ + FarZ) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                NoShadows(plane);
            }
            for (int i = 0; i < Banks.Length; i++)
            {
                var bank = Banks[i];
                int seed = 11 + i;
                Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(bank.Name, "Airerage/CloudSea", m =>
                {
                    Common(m, top, shade, glow, seed);
                    m.SetFloat("_Mode", 1f);
                    m.SetFloat("_Opacity", bank.Opacity);
                    m.SetFloat("_BankHoles", bank.Holes);
                    m.SetFloat("_Softness", 0.12f);
                    m.SetFloat("_Scale", 0.03f);
                    m.SetVector("_Drift", new Vector4(1.2f, 0.2f, 0f, 0f));
                    m.SetFloat("_BankTop", bank.Top);
                    m.SetFloat("_BankBillow", bank.Billow);
                    m.SetFloat("_BankDip", bank.Dip);
                    m.SetFloat("_NearFade", 4f);
                }, decor.Folder);
                // 手前の土手ほど手前に描く（奥の土手と層の上に重なる）。
                material.renderQueue = 2990 - i;
                float height = bank.Top - bank.Bottom;
                GameObject sheet = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, bank.Name, material, new Vector3(bank.Width, height, 1f));
                sheet.transform.SetParent(root, false);
                sheet.transform.SetPositionAndRotation(new Vector3(bank.X, (bank.Top + bank.Bottom) * 0.5f, bank.Z), Quaternion.identity);
                NoShadows(sheet);
            }
        }

        private static void Common(Material m, Color top, Color shade, Color glow, int seed)
        {
            m.SetColor("_TopColor", top);
            m.SetColor("_ShadeColor", shade);
            m.SetColor("_GlowColor", glow);
            m.SetVector("_LightDir", new Vector4(-0.6f, -0.4f, 0f, 0f));
            m.SetFloat("_Seed", seed);
        }

        private static void NoShadows(GameObject go)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
