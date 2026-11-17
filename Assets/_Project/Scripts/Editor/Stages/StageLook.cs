using UnityEngine;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ステージの見た目の台本（D-97 の V11 色の台本。2026-11-17 のユーザーの決定）: 空・霧・光・足場・遠景・格子・撃破の線の膜・仕上げの
    /// 画面処理の色と強さを、ステージごとに 1 か所にまとめる。StageDressing がこの値でステージを飾る。今は ATHLETIC だけ（試作。D-97 の
    /// 進め方: 飾りつけの自動化 V27 → ATHLETIC で試作 V30 → 全ステージ）。
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
        public Color FogColor;
        public float FogStart;
        public float FogEnd;

        // 光（主の光と環境光）。
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

        // 遠景（V14・V17）。
        public Color NearColor;
        public Color MidColor;
        public Color FarColor;
        public Color BeaconColor;
        public Color HazeColor;

        // 光の格子（V18）と撃破の線の膜（V19）。
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

        /// <summary>ATHLETIC（空のアスレチック）: 明るい昼の空、白い上の面と青みの影、水色の光る線、遠くに浮かぶ塔と島。</summary>
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
                FogColor = new Color(0.6f, 0.74f, 0.92f),
                FogStart = 120f,
                FogEnd = 1100f,
                LightColor = new Color(1f, 0.96f, 0.9f),
                LightIntensity = 1.15f,
                LightEuler = new Vector3(48f, -28f, 0f),
                AmbientSky = new Color(0.62f, 0.74f, 0.95f),
                AmbientEquator = new Color(0.55f, 0.62f, 0.74f),
                AmbientGround = new Color(0.32f, 0.36f, 0.48f),
                BlockSide = new Color(0.62f, 0.68f, 0.78f),
                BlockTop = new Color(0.95f, 0.97f, 1f),
                AttachableSide = new Color(0.2f, 0.42f, 0.58f),
                AttachableTop = new Color(0.55f, 0.78f, 0.9f),
                ShadeTint = new Color(0.86f, 0.93f, 1.15f),
                RimColor = new Color(0.78f, 0.9f, 1f),
                RimStrength = 0.55f,
                TrimColor = new Color(0.55f, 2.1f, 2.6f),
                ImpactColor = new Color(1.1f, 2.2f, 2.7f),
                HeightLow = -2f,
                HeightHigh = 22f,
                Bevel = 0.22f,
                BreathAmplitude = 0.05f,
                NearColor = new Color(0.3f, 0.4f, 0.58f),
                MidColor = new Color(0.46f, 0.58f, 0.8f),
                FarColor = new Color(0.56f, 0.68f, 0.86f),
                BeaconColor = new Color(2.4f, 1.3f, 0.5f),
                HazeColor = new Color(0.7f, 0.81f, 0.94f),
                GridColor = new Color(0.2f, 0.62f, 0.95f),
                GridDepth = 46f,
                MembraneColor = new Color(1.7f, 0.32f, 0.5f),
                BloomThreshold = 1.1f,
                BloomIntensity = 0.75f,
                BloomScatter = 0.62f,
                Contrast = 8f,
                Saturation = 8f,
                Vignette = 0.18f,
            };
        }
    }
}
