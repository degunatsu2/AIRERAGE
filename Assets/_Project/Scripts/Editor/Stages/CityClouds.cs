using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ビル街（SKYLINE の昼夜）の雲海（2026-11-25 の要望: ステージは非常に高い高層ビルの上。足場のビルの根元を雲で隠し、遥か下に大通りが
    /// 見えるように。他のゲームの天空・高層ビルのステージを参考に。docs/15 6.71）。シェーダーは Airerage/CloudSea。
    /// - 雲の層（水平な板 3 枚。上ほど細かく薄い）: 遊ぶ屋上（y 0〜15）と撃破の線（y −22）のすぐ下から、地上（CityStreet.Ground）までの間を
    ///   埋める。重ねた層がカメラの動きで少しずつずれて厚みに見える（視差）。大通りの上（z 95〜520）は雲を薄くして切れ間にし、遥か下の
    ///   大通りと行き交う車と人影をのぞかせる。その先の西新宿の高層ビルは、頭が雲海の上に突き出す（実際の雲海の写真の構図）。
    /// - 雲の土手（縦の板）: 足場のビルの手前と後ろに、上の縁がもくもくした雲を置いて根元を隠す（ほぼ水平に見る遊ぶ画面では、水平な層は
    ///   薄い線にしか見えないため）。奥の土手は大通りの切れ間を空けて左右に分ける。
    /// - 昼は日の当たる面が白く影が青みの灰色、夜は月明かりの暗い青紫で、下から街の明かり（ネオンのピンク）に照らされる。
    /// 表示だけ（当たりはない。撃破の線より下）。
    /// </summary>
    internal static class CityClouds
    {
        /// <summary>雲の層のいちばん上の高さ（撃破の線 −22 のすぐ下。ワールドの y）。</summary>
        public const float SeaTop = -27f;

        /// <summary>大通りの上の雲の切れ間（中心からの半分の幅と、奥行きの範囲。ワールドの z）。</summary>
        public const float ClearHalfWidth = 34f;
        public const float ClearStart = 95f;
        public const float ClearEnd = 520f;

        private static readonly (string Name, float Y, float Scale, float Coverage, float Opacity, Vector2 Drift)[] Layers =
        {
            ("CloudSeaTop", SeaTop, 0.011f, 0.47f, 0.9f, new Vector2(1.4f, 0.35f)),
            ("CloudSeaMid", -37f, 0.0075f, 0.4f, 0.94f, new Vector2(0.9f, 0.25f)),
            ("CloudSeaLow", -52f, 0.005f, 0.33f, 0.97f, new Vector2(0.55f, 0.15f)),
        };

        // 雲の土手: 名前・奥行き z・中心の x・幅・上の縁の高さ・こぶの高さ・下の端。
        private static readonly (string Name, float Z, float X, float Width, float Top, float Billow, float Bottom)[] Banks =
        {
            ("CloudBankFront", -7f, 0f, 300f, -25f, 7f, -95f),
            // 足場のビルの後ろは、大通りの切れ間（|x| < 32）を空けて左右に分ける（空撮で切れ間から遥か下の大通りが見えるように）。
            ("CloudBankBehindWest", 17f, -116f, 168f, -22f, 9f, -95f),
            ("CloudBankBehindEast", 17f, 116f, 168f, -22f, 9f, -95f),
            ("CloudBankRowWest", 62f, -150f, 240f, -19f, 12f, -100f),
            ("CloudBankRowEast", 62f, 150f, 240f, -19f, 12f, -100f),
            ("CloudBankFarWest", 140f, -190f, 300f, -21f, 14f, -100f),
            ("CloudBankFarEast", 140f, 190f, 300f, -21f, 14f, -100f),
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
                int seed = i + 1;
                Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(layer.Name, "Airerage/CloudSea", m =>
                {
                    Common(m, top, shade, glow, seed);
                    m.SetFloat("_Mode", 0f);
                    m.SetFloat("_Opacity", layer.Opacity);
                    m.SetFloat("_Coverage", layer.Coverage);
                    m.SetFloat("_Softness", 0.16f);
                    m.SetFloat("_Scale", layer.Scale);
                    m.SetVector("_Drift", new Vector4(layer.Drift.x, layer.Drift.y, 0f, 0f));
                    m.SetFloat("_ClearCenterX", 0f);
                    m.SetFloat("_ClearHalfWidth", ClearHalfWidth);
                    m.SetFloat("_ClearStart", ClearStart);
                    m.SetFloat("_ClearEnd", ClearEnd);
                    m.SetFloat("_ClearSoft", 30f);
                    m.SetFloat("_NearFade", 6f);
                }, decor.Folder);
                material.renderQueue = 2980 - i;
                GameObject plane = SandboxWireSceneBuilder.CreateVisual(PrimitiveType.Quad, layer.Name, material, new Vector3(3400f, 2800f, 1f));
                plane.transform.SetParent(root, false);
                plane.transform.SetPositionAndRotation(new Vector3(0f, layer.Y, 1300f), Quaternion.Euler(90f, 0f, 0f));
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
                    m.SetFloat("_Opacity", 0.97f);
                    m.SetFloat("_Softness", 0.12f);
                    m.SetFloat("_Scale", 0.03f);
                    m.SetVector("_Drift", new Vector4(1.2f, 0.2f, 0f, 0f));
                    m.SetFloat("_BankTop", bank.Top);
                    m.SetFloat("_BankBillow", bank.Billow);
                    m.SetFloat("_NearFade", 4f);
                    m.SetFloat("_ClearHalfWidth", 0f);
                    m.SetFloat("_ClearStart", 100000f);
                    m.SetFloat("_ClearEnd", 100000f);
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
