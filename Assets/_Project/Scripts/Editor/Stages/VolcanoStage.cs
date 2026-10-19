using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 火山の火口（CRATER）。左右の火口の縁（段になった岩）と、溶岩の湖の真ん中の岩の柱、浮かぶ岩。溶岩に落ちると撃破。
    /// 固有のギミック: 溶岩の噴き上げ（周期のある上向きの強い風。左右が交互に噴く）、噴火口（Launcher。縁から火口の真ん中へ飛ばす）、
    /// 噴石（Bumper。浮かぶ岩に当たると弾かれる）。熱の上昇気流が、縁の下に落ちた人を押し上げる。
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
            theme.ZoneColors[StageZoneKind.Gust] = new Color(1f, 0.45f, 0.1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.3f, 0.1f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.9f, 0.25f, 0.08f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(1f, 0.7f, 0.45f);

            return new StageLayout
            {
                Id = "Volcano",
                DisplayName = "CRATER",
                Description = "Inside an active crater. Lava geysers erupt in turn, vents fire you across, molten rocks bounce you away.",
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
                BoundsMin = new Vector2(-48f, -22f),
                BoundsMax = new Vector2(48f, 46f),
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
                    // 熱の上昇気流（縁の下に落ちた人を、縁の高さまで押し上げる）。
                    new ZoneSpec("HeatWest", StageZoneKind.Updraft, new Vector2(-29f, -10f), new Vector2(8f, 24f), Vector2.up, 55f, 12f),
                    new ZoneSpec("HeatEast", StageZoneKind.Updraft, new Vector2(29f, -10f), new Vector2(8f, 24f), Vector2.up, 55f, 12f),
                    // 溶岩の噴き上げ: 7 秒ごとに 2.2 秒、左右が半分ずれて噴く（噴く 1 秒前に点滅）。乗ると火口の上まで打ち上がる。
                    new ZoneSpec("LavaGeyserWest", StageZoneKind.Gust, new Vector2(-14f, -6f), new Vector2(5f, 32f), Vector2.up, 95f, 24f).Every(7f, 2.2f, 0f),
                    new ZoneSpec("LavaGeyserEast", StageZoneKind.Gust, new Vector2(14f, -6f), new Vector2(5f, 32f), Vector2.up, 95f, 24f).Every(7f, 2.2f, 3.5f),
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
                    // 浮かぶ岩のジャンプ台の上、噴き上げの中、縁の中段のジャンプ台の上。
                    (new Vector2(-21.5f, 9f), 1), (new Vector2(-21.5f, 12.5f), 1), (new Vector2(21.5f, 9f), 1), (new Vector2(21.5f, 12.5f), 1),
                    (new Vector2(-14f, 4f), 1), (new Vector2(-14f, 12f), 1), (new Vector2(-14f, 17f), 1),
                    (new Vector2(14f, 4f), 1), (new Vector2(14f, 12f), 1), (new Vector2(14f, 17f), 1),
                    (new Vector2(-43f, 12f), 1), (new Vector2(43f, 12f), 1),
                    // 3 枚: 頂の岩の上、縁のいちばん上、リングの上の高い所。
                    (new Vector2(-7f, 25f), 3), (new Vector2(7f, 25f), 3), (new Vector2(-46.5f, 19f), 3), (new Vector2(46.5f, 19f), 3), (new Vector2(0f, 27f), 3),
                    // 20 枚: 噴き上げと熱の上昇気流の底（溶岩のすぐ上）、いちばん上、ステージの端。
                    (new Vector2(-14f, -14f), 20), (new Vector2(14f, -14f), 20), (new Vector2(-29f, -15f), 20), (new Vector2(29f, -15f), 20),
                    (new Vector2(0f, 38f), 20), (new Vector2(-46f, 30f), 20), (new Vector2(46f, 30f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 42f), new Vector2(-46f, 36f), new Vector2(46f, 36f), new Vector2(-14f, -18f), new Vector2(14f, -18f),
                    new Vector2(-29f, -18f), new Vector2(29f, -18f),
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

            decor.Box("LavaLake", new Vector3(0f, -20f, 5f), new Vector3(120f, 2f, 40f), lava);
            // 噴き上げの根もとの明るい溶岩。
            decor.Box("GeyserPoolWest", new Vector3(-14f, -18.9f, 0f), new Vector3(6f, 0.3f, 4f), lavaHot);
            decor.Box("GeyserPoolEast", new Vector3(14f, -18.9f, 0f), new Vector3(6f, 0.3f, 4f), lavaHot);

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
