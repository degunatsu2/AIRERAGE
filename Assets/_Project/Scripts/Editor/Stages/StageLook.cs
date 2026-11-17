using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ステージの見た目の台本（D-97 の V11 色の台本。2026-11-17 のユーザーの決定）: 空・霧・光・足場・ギミック・遠景・格子・撃破の線の膜・
    /// 仕上げの画面処理の色と強さを、ステージごとに 1 か所にまとめる。StageDressing がこの値でステージを飾る。今は ATHLETIC と
    /// SKYLINE NIGHT（D-97 の進め方: 飾りつけの自動化 → ATHLETIC で試作 → 夜の雰囲気の確認 → 全ステージ）。
    /// 世界観は現代日本（遠景は今の街。浮かぶ島などは置かない）、対戦そのものは近未来風（光る線・膜・格子・光の柱）。
    /// </summary>
    internal sealed class StageLook
    {
        /// <summary>材質を置くフォルダの名前（Assets/_Project/Materials/Stages/{Id}/Look）。</summary>
        public string Id;
        /// <summary>遠景の形を決める乱数の種。</summary>
        public int Seed;

        // V15 空と霧。
        public Color SkyZenith;
        public Color SkySky;
        public Color SkyHorizon;
        public Color SkyBelow;
        /// <summary>空の地平線のもやの高さ（視線の上下の向き。負なら目の高さより下）。</summary>
        public float HorizonHeight;
        public Color SunColor;
        public Vector3 SunDirection;
        /// <summary>夜空の星の明るさ（0 なら出さない）。</summary>
        public float Stars;
        public Color FogColor;
        public float FogStart;
        public float FogEnd;

        // 光（主の光と環境光）。主の光は左上の手前から（上の面 > 正面 > 左の面 > 右の面の順に明るく、立体に見える）。
        public Color LightColor;
        public float LightIntensity;
        public Vector3 LightEuler;
        public Color AmbientSky;
        public Color AmbientEquator;
        public Color AmbientGround;

        // 足場（V1・V3・V7・V8・V10・V22）。
        public Color BlockSide;
        public Color BlockTop;
        public Color AttachableSide;
        public Color AttachableTop;
        public Color ShadeTint;
        public Color RimColor;
        public float RimStrength;
        public Color TrimColor;
        public Color ImpactColor;
        public float HeightLow;
        public float HeightHigh;
        /// <summary>面取りの幅（m。薄い足場では短い辺に合わせて細くなる）。</summary>
        public float Bevel;
        /// <summary>足場の呼吸の上下の幅（m。V21。小さな浮いた足場だけ）。</summary>
        public float BreathAmplitude;
        /// <summary>環境光の強さ（小さいほど、光の当たる面と当たらない面の差が大きい）。</summary>
        public float Ambient;
        /// <summary>つやの強さ。</summary>
        public float Specular;
        /// <summary>面取りの面が光を受けて縁が光る強さ。</summary>
        public float EdgeGlint;
        /// <summary>横の面の上下の明暗の強さ。</summary>
        public float LocalGradient;
        /// <summary>箱の下の側の陰の強さ。</summary>
        public float Occlusion;

        // ギミック（ジャンプ台の台・加速リングの枠・看板の枠）とアンカー。足場と同じ材質。
        public Color GimmickSide;
        public Color GimmickTop;
        public Color AnchorSide;
        public Color AnchorTop;
        public Color AnchorCore;
        /// <summary>光る線と光る面の強さ（1 より大きいとにじむ）。</summary>
        public float GlowIntensity;
        /// <summary>ワイヤーが刺さる足場のオレンジの縁の強さ。</summary>
        public float AttachableRimIntensity;
        public Color WindColor;

        // 遠景（V14・V17）。
        public Color NearColor;
        public Color MidColor;
        public Color FarColor;
        /// <summary>塔の上の航空障害灯の色（赤く点滅する）。</summary>
        public Color BeaconColor;
        public Color HazeColor;
        /// <summary>塔の横の面の階の区切りの帯（昼）・窓の明かり（夜）の色と強さ、明かりの点いた区画の割合。</summary>
        public Color BandColor;
        public float BandStrength;
        public float BandFill;
        /// <summary>飛行船の色（運営の撮影の飛行船。1 隻）。</summary>
        public Color AirshipColor;

        // 光の格子（V18）と撃破の線の膜（V19）。
        /// <summary>光の格子を置くか（街のステージは下に道路があるので置かない）。</summary>
        public bool Grid;
        public Color GridColor;
        /// <summary>格子を置く深さ（撃破の線の下の端から、さらに下へ m）。</summary>
        public float GridDepth;
        public Color MembraneColor;

        // 仕上げの画面処理（V13）。
        public float BloomThreshold;
        public float BloomIntensity;
        public float BloomScatter;
        public float Contrast;
        public float Saturation;
        public float Vignette;

        /// <summary>ATHLETIC（空のアスレチック）: 明るい昼の空、白い上の面と青みの影、水色の光る線、遠くに今の街の塔。</summary>
        public static StageLook Athletic()
        {
            return new StageLook
            {
                Id = "Athletic",
                Seed = 20261117,
                SkyZenith = new Color(0.13f, 0.32f, 0.68f),
                SkySky = new Color(0.36f, 0.58f, 0.88f),
                SkyHorizon = new Color(0.86f, 0.93f, 0.99f),
                SkyBelow = new Color(0.6f, 0.74f, 0.9f),
                HorizonHeight = -0.1f,
                SunColor = new Color(1.25f, 1.15f, 0.95f),
                SunDirection = new Vector3(-0.5f, 0.42f, 1f),
                Stars = 0f,
                FogColor = new Color(0.6f, 0.74f, 0.92f),
                FogStart = 120f,
                FogEnd = 1100f,
                LightColor = new Color(1f, 0.95f, 0.86f),
                LightIntensity = 1.35f,
                LightEuler = new Vector3(50f, 32f, 0f),
                AmbientSky = new Color(0.62f, 0.74f, 0.95f),
                AmbientEquator = new Color(0.5f, 0.57f, 0.7f),
                AmbientGround = new Color(0.28f, 0.32f, 0.44f),
                BlockSide = new Color(0.52f, 0.58f, 0.68f),
                BlockTop = new Color(0.64f, 0.68f, 0.74f),
                AttachableSide = new Color(0.22f, 0.34f, 0.5f),
                AttachableTop = new Color(0.6f, 0.65f, 0.73f),
                ShadeTint = new Color(0.84f, 0.92f, 1.2f),
                RimColor = new Color(0.78f, 0.9f, 1f),
                RimStrength = 0.3f,
                TrimColor = new Color(0.55f, 2.1f, 2.6f),
                ImpactColor = new Color(0.22f, 0.42f, 0.52f),
                HeightLow = -2f,
                HeightHigh = 22f,
                Bevel = 0.22f,
                BreathAmplitude = 0.05f,
                Ambient = 0.68f,
                Specular = 0.35f,
                EdgeGlint = 0.6f,
                LocalGradient = 0.65f,
                Occlusion = 0.5f,
                GimmickSide = new Color(0.48f, 0.53f, 0.62f),
                GimmickTop = new Color(0.66f, 0.7f, 0.76f),
                AnchorSide = new Color(0.72f, 0.56f, 0.12f),
                AnchorTop = new Color(0.86f, 0.74f, 0.34f),
                AnchorCore = new Color(1f, 0.82f, 0.3f),
                GlowIntensity = 2.2f,
                AttachableRimIntensity = 1.9f,
                WindColor = new Color(0.62f, 0.9f, 1.3f),
                NearColor = new Color(0.3f, 0.4f, 0.58f),
                MidColor = new Color(0.46f, 0.58f, 0.8f),
                FarColor = new Color(0.56f, 0.68f, 0.86f),
                BeaconColor = new Color(2.6f, 0.35f, 0.3f),
                HazeColor = new Color(0.7f, 0.81f, 0.94f),
                BandColor = Color.white,
                BandStrength = 0.07f,
                BandFill = 0f,
                AirshipColor = new Color(0.86f, 0.9f, 0.96f),
                Grid = true,
                GridColor = new Color(0.2f, 0.62f, 0.95f),
                GridDepth = 46f,
                MembraneColor = new Color(1.7f, 0.32f, 0.5f),
                BloomThreshold = 1.1f,
                BloomIntensity = 0.75f,
                BloomScatter = 0.62f,
                Contrast = 10f,
                Saturation = 8f,
                Vignette = 0.18f,
            };
        }

        /// <summary>
        /// SKYLINE NIGHT（夜のビル街）: 濃い紺の夜空と星、街明かりで地平線が赤紫にかすむ。月明かりの青い光、ビルの上の面だけ淡く光を受け、
        /// 光る線とネオンがにじむ。遠くの街は窓の明かりと赤い航空障害灯。下には道路があるので光の格子は置かない。
        /// </summary>
        public static StageLook CityNight()
        {
            return new StageLook
            {
                Id = "CityNight",
                Seed = 20261118,
                SkyZenith = new Color(0.008f, 0.012f, 0.045f),
                SkySky = new Color(0.025f, 0.035f, 0.11f),
                SkyHorizon = new Color(0.2f, 0.09f, 0.26f),
                SkyBelow = new Color(0.06f, 0.04f, 0.1f),
                HorizonHeight = -0.08f,
                SunColor = Color.black,
                SunDirection = new Vector3(-0.5f, 0.42f, 1f),
                Stars = 0.9f,
                FogColor = new Color(0.08f, 0.055f, 0.14f),
                FogStart = 70f,
                FogEnd = 620f,
                LightColor = new Color(0.62f, 0.7f, 0.95f),
                LightIntensity = 0.8f,
                LightEuler = new Vector3(50f, 32f, 0f),
                AmbientSky = new Color(0.16f, 0.17f, 0.34f),
                AmbientEquator = new Color(0.14f, 0.1f, 0.22f),
                AmbientGround = new Color(0.05f, 0.04f, 0.07f),
                BlockSide = new Color(0.2f, 0.21f, 0.27f),
                BlockTop = new Color(0.34f, 0.36f, 0.44f),
                AttachableSide = new Color(0.17f, 0.19f, 0.27f),
                AttachableTop = new Color(0.32f, 0.35f, 0.44f),
                ShadeTint = new Color(0.92f, 0.88f, 1.15f),
                RimColor = new Color(0.3f, 0.38f, 0.7f),
                RimStrength = 0.45f,
                TrimColor = new Color(0.4f, 1.9f, 2.6f),
                ImpactColor = new Color(0.25f, 0.4f, 0.6f),
                HeightLow = -10f,
                HeightHigh = 20f,
                Bevel = 0.22f,
                BreathAmplitude = 0.05f,
                Ambient = 0.85f,
                Specular = 0.5f,
                EdgeGlint = 0.35f,
                LocalGradient = 0.55f,
                Occlusion = 0.45f,
                GimmickSide = new Color(0.12f, 0.13f, 0.18f),
                GimmickTop = new Color(0.4f, 0.43f, 0.55f),
                AnchorSide = new Color(0.62f, 0.46f, 0.1f),
                AnchorTop = new Color(1f, 0.85f, 0.4f),
                AnchorCore = new Color(1f, 0.8f, 0.3f),
                GlowIntensity = 2.6f,
                AttachableRimIntensity = 1.45f,
                WindColor = new Color(0.5f, 0.75f, 1.4f),
                NearColor = new Color(0.05f, 0.055f, 0.1f),
                MidColor = new Color(0.07f, 0.075f, 0.13f),
                FarColor = new Color(0.09f, 0.085f, 0.16f),
                BeaconColor = new Color(3.2f, 0.35f, 0.3f),
                HazeColor = new Color(0.12f, 0.08f, 0.18f),
                BandColor = new Color(1f, 0.78f, 0.45f),
                BandStrength = 0.55f,
                BandFill = 0.35f,
                AirshipColor = new Color(0.16f, 0.17f, 0.24f),
                Grid = false,
                GridColor = new Color(0.4f, 0.3f, 1.2f),
                GridDepth = 46f,
                MembraneColor = new Color(1.8f, 0.3f, 0.85f),
                BloomThreshold = 0.9f,
                BloomIntensity = 0.8f,
                BloomScatter = 0.5f,
                Contrast = 12f,
                Saturation = 6f,
                Vignette = 0.26f,
            };
        }
    }
}
