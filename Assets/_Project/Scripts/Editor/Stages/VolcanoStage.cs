using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 火山の火口（CRATER）。左右の火口の縁（段になった岩）と、溶岩の湖の真ん中の岩の柱、浮かぶ岩。溶岩に落ちると撃破。
    /// 固有のギミック（ユーザーの要望、2026-10-19 に変え、2026-10-20 に間隔と太さを変えた）: 溶岩流（Hazard。溶岩の湖のランダムな場所から、
    /// 黒曜石の段の高さまで噴き出す。威力 30（その枚数のコインを落とす）で、高く吹き飛ばす。噴く前に当たる柱の全体に警告と警告音）、壁の間欠泉（周期のある Launcher。縁の上の段と
    /// 下の段の内側の壁から、火口の内側へ斜めに一斉に噴き出し、強い風よりも強く吹き飛ばす）、噴火口（Launcher。縁から火口の真ん中へ
    /// 飛ばす）、噴石（Bumper）。
    /// </summary>
    internal static class VolcanoStage
    {
        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.36f, 0.3f, 0.28f),
                Attachable = new Color(0.17f, 0.16f, 0.22f),
                Anchor = new Color(1f, 0.82f, 0.3f),
                SkyTint = new Color(1f, 0.42f, 0.25f),
                Ground = new Color(0.25f, 0.06f, 0.03f),
                Exposure = 0.85f,
                AtmosphereThickness = 1.3f,
                SunSize = 0.06f,
                Light = new Color(1f, 0.62f, 0.4f),
                LightIntensity = 1.1f,
                LightEuler = new Vector3(30f, -50f, 0f),
                AmbientSky = new Color(0.55f, 0.3f, 0.25f),
                AmbientEquator = new Color(0.45f, 0.22f, 0.15f),
                AmbientGround = new Color(0.6f, 0.2f, 0.05f),
                FogColor = new Color(0.45f, 0.18f, 0.12f),
                FogStart = 45f,
                FogEnd = 150f,
            };
            theme.ZoneColors[StageZoneKind.Hazard] = new Color(1f, 0.42f, 0.06f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.3f, 0.1f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.9f, 0.25f, 0.08f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(1f, 0.7f, 0.45f);

            return new StageLayout
            {
                Id = "Volcano",
                DisplayName = "CRATER",
                Description = "Inside an active crater. Lava bursts from random spots, steam geysers blast from the walls, molten rocks bounce you away.",
                DescriptionJa = "活火山の火口の中。溶岩が噴き出し、壁の間欠泉から蒸気が吹き上がり、溶けた岩に弾き飛ばされる。",
                Color = new Color(1f, 0.45f, 0.15f),
                Terrain = new[]
                {
                    // 火口の縁（外へ行くほど高い段）。
                    new TerrainBox("RimWestLow", -48f, -30f, -34f, 2f, attachable: true),
                    new TerrainBox("RimWestMid", -48f, 2f, -41f, 8f, attachable: true),
                    new TerrainBox("RimWestTop", -48f, 8f, -45f, 16f, attachable: true),
                    new TerrainBox("RimEastLow", 34f, -30f, 48f, 2f, attachable: true),
                    new TerrainBox("RimEastMid", 41f, 2f, 48f, 8f, attachable: true),
                    new TerrainBox("RimEastTop", 45f, 8f, 48f, 16f, attachable: true),
                    // 溶岩の湖の真ん中の岩の柱（いちばん広い足場）。
                    new TerrainBox("CenterSpire", -6f, -30f, 6f, 0f, attachable: true),
                    // 浮かぶ岩（接続できない灰の岩と、接続できる黒曜石）。
                    new TerrainBox("FloatRockWest", -24f, 4f, -19f, 5f),
                    new TerrainBox("FloatRockEast", 19f, 4f, 24f, 5f),
                    new TerrainBox("ObsidianWest", -10.5f, 13f, -6.5f, 13.8f, attachable: true),
                    new TerrainBox("ObsidianEast", 6.5f, 13f, 10.5f, 13.8f, attachable: true),
                    new TerrainBox("SummitWest", -9f, 22f, -5f, 22.6f, attachable: true),
                    new TerrainBox("SummitEast", 5f, 22f, 9f, 22.6f, attachable: true),
                },
                Anchors = new[]
                {
                    new Vector2(0f, 30f), new Vector2(-15f, 26f), new Vector2(15f, 26f), new Vector2(-30f, 20f), new Vector2(30f, 20f),
                    new Vector2(-36f, 12f), new Vector2(36f, 12f), new Vector2(-13f, 7f), new Vector2(13f, 7f),
                },
                // 左右は縁の外へ広めに（ユーザーの要望、2026-10-19）。
                BoundsMin = new Vector2(-60f, -22f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 柱のジャンプ台と、真上の加速リング。
                    ZoneSpec.OnFloor("SpirePad", StageZoneKind.JumpPad, -1f, 1f, 0f, Vector2.up, 28f, 0.4f),
                    new ZoneSpec("SpireRing", StageZoneKind.BoostRing, new Vector2(0f, 16f), new Vector2(3f, 3.5f), Vector2.up, 20f),
                    // 浮かぶ岩と縁の中段のジャンプ台。
                    ZoneSpec.OnFloor("FloatPadWest", StageZoneKind.JumpPad, -22.5f, -20.5f, 5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("FloatPadEast", StageZoneKind.JumpPad, 20.5f, 22.5f, 5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("RimPadWest", StageZoneKind.JumpPad, -44f, -42f, 8f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("RimPadEast", StageZoneKind.JumpPad, 42f, 44f, 8f, Vector2.up, 24f, 0.4f),
                    // 縁から火口の真ん中へ向かう加速リング。
                    new ZoneSpec("RimRingWest", StageZoneKind.BoostRing, new Vector2(-28f, 13f), new Vector2(2.4f, 3f), new Vector2(1f, 0.15f), 18f),
                    new ZoneSpec("RimRingEast", StageZoneKind.BoostRing, new Vector2(28f, 13f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.15f), 18f),
                    // 溶岩流: 溶岩の湖（柱の左右）のランダムな場所から、幅 6m の柱が黒曜石の段（y = 13）まで 3 秒噴き出す（25 秒ごと。
                    // 左右が半分ずれる。ユーザーの要望、2026-10-20: 頻度を 1/5、太さを 1.5 倍、長さを 3 倍、もう一段上まで）。噴く 2 秒前から
                    // 当たる柱の全体に警告。当たると威力 30 の枚数のコインを落とし、上へ 40 m/s で吹き飛ぶ（2026-10-21: 威力を 60 から 30 に、
                    // 吹き飛ぶ高さを約 3 倍に。速さ 23 → 40 m/s）。
                    new ZoneSpec("LavaFlowWest", StageZoneKind.Hazard, new Vector2(-20f, -3.5f), new Vector2(28f, 33f), Vector2.up, 30f, 40f)
                        .Every(25f, 3f, 0f, 0f, 2f).Column(6f),
                    new ZoneSpec("LavaFlowEast", StageZoneKind.Hazard, new Vector2(20f, -3.5f), new Vector2(28f, 33f), Vector2.up, 30f, 40f)
                        .Every(25f, 3f, 12.5f, 0f, 2f).Column(6f),
                    // 壁の間欠泉: 縁の上の段（x = ±45）の上のほうと、下の段（x = ±34）の下のほうの内側の壁から、火口の内側へ斜めに噴く
                    // （4 つが一斉に、20 秒ごとに 3.6 秒。ユーザーの要望、2026-10-20。2 秒前から口に警告）。触れると速度をまるごと噴く向きにする
                    // （強い風より強い）。
                    new ZoneSpec("GeyserHighWest", StageZoneKind.Launcher, new Vector2(-42.5f, 14f), new Vector2(5f, 5f), new Vector2(1f, 0.35f), 34f)
                        .Every(20f, 3.6f, 0f, 0f, 2f),
                    new ZoneSpec("GeyserHighEast", StageZoneKind.Launcher, new Vector2(42.5f, 14f), new Vector2(5f, 5f), new Vector2(-1f, 0.35f), 34f)
                        .Every(20f, 3.6f, 0f, 0f, 2f),
                    new ZoneSpec("GeyserLowWest", StageZoneKind.Launcher, new Vector2(-31.5f, -6.5f), new Vector2(5f, 5f), new Vector2(1f, 0.8f), 32f)
                        .Every(20f, 3.6f, 0f, 0f, 2f),
                    new ZoneSpec("GeyserLowEast", StageZoneKind.Launcher, new Vector2(31.5f, -6.5f), new Vector2(5f, 5f), new Vector2(-1f, 0.8f), 32f)
                        .Every(20f, 3.6f, 0f, 0f, 2f),
                    // 噴火口: 縁の低い段から、火口の真ん中の柱へ飛ばす。
                    ZoneSpec.OnFloor("VentWest", StageZoneKind.Launcher, -37f, -35f, 2f, new Vector2(0.7f, 1f), 32f),
                    ZoneSpec.OnFloor("VentEast", StageZoneKind.Launcher, 35f, 37f, 2f, new Vector2(-0.7f, 1f), 32f),
                    // 噴石（浮かぶ溶けた岩）: 当たると外へ弾かれる。
                    new ZoneSpec("BombWest", StageZoneKind.Bumper, new Vector2(-20f, 18f), new Vector2(1.8f, 1.8f), Vector2.up, 18f),
                    new ZoneSpec("BombEast", StageZoneKind.Bumper, new Vector2(20f, 18f), new Vector2(1.8f, 1.8f), Vector2.up, 18f),
                    new ZoneSpec("BombHighWest", StageZoneKind.Bumper, new Vector2(-33f, 24f), new Vector2(2f, 2f), Vector2.up, 18f),
                    new ZoneSpec("BombHighEast", StageZoneKind.Bumper, new Vector2(33f, 24f), new Vector2(2f, 2f), Vector2.up, 18f),
                    new ZoneSpec("BombTop", StageZoneKind.Bumper, new Vector2(0f, 33f), new Vector2(2.2f, 2.2f), Vector2.up, 18f),
                },
                Coins = new (Vector2, int)[]
                {
                    // 柱のジャンプ台の上と、加速リングの上。
                    (new Vector2(0f, 3.5f), 1), (new Vector2(0f, 10.5f), 1), (new Vector2(0f, 13.5f), 1), (new Vector2(0f, 20f), 1), (new Vector2(0f, 23f), 1),
                    // 噴火口で飛ぶ道。
                    (new Vector2(-30f, 9.8f), 1), (new Vector2(-20f, 13.6f), 1), (new Vector2(30f, 9.8f), 1), (new Vector2(20f, 13.6f), 1),
                    // 浮かぶ岩のジャンプ台の上、浮かぶ岩と柱の間、縁の中段のジャンプ台の上。
                    (new Vector2(-21.5f, 9f), 1), (new Vector2(-21.5f, 12.5f), 1), (new Vector2(21.5f, 9f), 1), (new Vector2(21.5f, 12.5f), 1),
                    (new Vector2(-14f, 4f), 1), (new Vector2(-14f, 12f), 1), (new Vector2(-14f, 17f), 1),
                    (new Vector2(14f, 4f), 1), (new Vector2(14f, 12f), 1), (new Vector2(14f, 17f), 1),
                    // 間欠泉の通り道。
                    (new Vector2(-33.5f, 17.5f), 1), (new Vector2(33.5f, 17.5f), 1), (new Vector2(-26f, -1f), 1), (new Vector2(26f, -1f), 1),
                    (new Vector2(-43f, 12f), 1), (new Vector2(43f, 12f), 1),
                    // 3 枚: 頂の岩の上、縁のいちばん上、リングの上の高い所。
                    (new Vector2(-7f, 25f), 3), (new Vector2(7f, 25f), 3), (new Vector2(-46.5f, 19f), 3), (new Vector2(46.5f, 19f), 3), (new Vector2(0f, 27f), 3),
                    // 20 枚: 溶岩の上（溶岩流が噴く所。壁の間欠泉か壁ジャンプで戻る）、いちばん上、ステージの端。
                    (new Vector2(-14f, -6f), 20), (new Vector2(14f, -6f), 20), (new Vector2(-28f, -5f), 20), (new Vector2(28f, -5f), 20),
                    (new Vector2(0f, 38f), 20), (new Vector2(-46f, 30f), 20), (new Vector2(46f, 30f), 20),
                    // 追加（総量を 2 倍に。ユーザーの要望、2026-10-21）。浮かぶ岩の下の溶岩の上（溶岩流から隠れきれない隠れ家。20 枚と 3 枚 2 つを固める）。
                    (new Vector2(-21.5f, 2f), 20), (new Vector2(21.5f, 2f), 20), (new Vector2(-18.5f, 2.5f), 3), (new Vector2(18.5f, 2.5f), 3),
                    (new Vector2(-24.5f, 2.5f), 3), (new Vector2(24.5f, 2.5f), 3),
                    // 柱の脇の溶岩の上、縁の低い段の壁の前、噴火口で飛ぶ道、黒曜石の上、縁の加速リングの上、間欠泉の先。
                    (new Vector2(-8f, -3f), 1), (new Vector2(8f, -3f), 1), (new Vector2(-31f, -1f), 1), (new Vector2(31f, -1f), 1),
                    (new Vector2(-37.5f, 6.5f), 3), (new Vector2(37.5f, 6.5f), 3), (new Vector2(-17f, 8f), 1), (new Vector2(17f, 8f), 1),
                    (new Vector2(-10.5f, 15.5f), 3), (new Vector2(10.5f, 15.5f), 3), (new Vector2(-26f, 15f), 1), (new Vector2(26f, 15f), 1),
                    (new Vector2(-33f, 11f), 1), (new Vector2(33f, 11f), 1),
                    // 頂の岩の横、高い所、縁のいちばん上の先。
                    (new Vector2(-12f, 24f), 1), (new Vector2(12f, 24f), 1), (new Vector2(-25f, 28f), 1), (new Vector2(25f, 28f), 1),
                    (new Vector2(-42.5f, 20f), 1), (new Vector2(42.5f, 20f), 1),
                    // 20 枚: 縁の上の上空、縁の外、真ん中の上空。
                    (new Vector2(-40f, 24f), 20), (new Vector2(40f, 24f), 20), (new Vector2(-52f, 8f), 20), (new Vector2(52f, 8f), 20),
                    (new Vector2(0f, 32f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 42f), new Vector2(-46f, 36f), new Vector2(46f, 36f), new Vector2(-14f, -9f), new Vector2(14f, -9f),
                    new Vector2(-28f, -9f), new Vector2(28f, -9f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7f), new Vector2(-28f, 6f), new Vector2(28f, 6f), new Vector2(-36f, 16f), new Vector2(36f, 16f),
                    new Vector2(-8f, 18f), new Vector2(8f, 18f),
                },
                Spawns = new[] { new Vector3(-3.5f, 1f, 0f), new Vector3(3.5f, 1f, 0f), new Vector3(-38.5f, 3f, 0f), new Vector3(38.5f, 3f, 0f) },
                Decorate = Decorate,
            };
        }

        // 溶岩の湖、火口の壁、遠くの山、火の粉。
        private static void Decorate(StageDecor decor)
        {
            Material lava = decor.Unlit("Lava", new Color(1f, 0.38f, 0.05f));
            Material lavaHot = decor.Unlit("LavaHot", new Color(1f, 0.75f, 0.2f));
            Material rock = decor.Lit("Rock", new Color(0.22f, 0.17f, 0.16f));
            Material ash = decor.Lit("Ash", new Color(0.32f, 0.27f, 0.25f));
            Material ember = decor.Unlit("Ember", new Color(1f, 0.55f, 0.15f));

            decor.Box("LavaLake", new Vector3(0f, -20f, 5f), new Vector3(140f, 2f, 40f), lava);
            // 溶岩流が噴く湖の、明るい溶岩の帯。
            decor.Box("BubblingLavaWest", new Vector3(-20f, -18.9f, 0f), new Vector3(28f, 0.3f, 4f), lavaHot);
            decor.Box("BubblingLavaEast", new Vector3(20f, -18.9f, 0f), new Vector3(28f, 0.3f, 4f), lavaHot);

            // 火口の壁（ステージの奥を囲む高い岩）。
            for (float x = -70f; x <= 70f; x += 10f)
            {
                float height = 22f + Mathf.Abs(x) * 0.35f + decor.Range(-4f, 4f);
                decor.Box("CraterWall", new Vector3(x, height * 0.5f - 20f, decor.Range(26f, 32f)), new Vector3(11f, height, 8f), decor.Random.NextDouble() < 0.5 ? rock : ash);
            }
            // 縁と柱の奥行き（見た目だけ）。
            decor.Box("RimWestBack", new Vector3(-41f, -6f, 6f), new Vector3(14f, 32f, 8f), rock);
            decor.Box("RimEastBack", new Vector3(41f, -6f, 6f), new Vector3(14f, 32f, 8f), rock);
            decor.Box("SpireBack", new Vector3(0f, -10f, 4f), new Vector3(10f, 20f, 4f), rock);
            // 岩の割れ目の溶岩の光。
            for (int i = 0; i < 14; i++)
            {
                float x = decor.Random.NextDouble() < 0.5 ? decor.Range(-47f, -35f) : decor.Range(35f, 47f);
                decor.Quad("Crack", new Vector3(x, decor.Range(-18f, 1f), -2.02f), new Vector2(decor.Range(0.15f, 0.35f), decor.Range(1.5f, 4f)), lava);
            }

            // 遠くの火山（45° 回した箱で尖った山にする）。
            for (int i = 0; i < 5; i++)
            {
                float size = decor.Range(40f, 70f);
                GameObject peak = decor.Box("Mountain", new Vector3(-120f + i * 60f + decor.Range(-10f, 10f), -25f, 110f + decor.Range(0f, 30f)),
                    new Vector3(size, size, 20f), rock);
                peak.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }
            // 火の粉。
            for (int i = 0; i < 40; i++)
            {
                float s = decor.Range(0.12f, 0.3f);
                decor.Sphere("Ember", new Vector3(decor.Range(-45f, 45f), decor.Range(-15f, 40f), decor.Range(4f, 20f)), new Vector3(s, s, s), ember);
            }
        }
    }
}
