using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 陸上競技場（STORM STADIUM。STORM RUNNER のテーマのステージ。ユーザーの案、2026-10-26）。いちばん下はトラックが地続きで、撃破の線は
    /// 左右と上にしかない（トラックは左右の撃破の線の外まで続く。下へは落ちない）。真ん中に表彰台、左右にスタンドと照明塔、上に
    /// 大きな得点板。
    /// 追加の案（取捨選択はユーザー）: 追い風と向かい風（競技場の全体に、左右から交互に吹く周期のある風。地面の上でも流される）、
    /// 短距離のレーン（真ん中へ向かうスピードレーン）とハードル（低い壁）、棒高跳び（発射台）、走り幅跳びの踏み切り板（発射台）、
    /// 走り高跳びのマット（ジャンプ台）、転がる砲丸（トラックの上を行き来するバンパー）、つむじ風（トラックの上を行き来する上昇気流）、
    /// スタンドの屋根と照明塔（ワイヤーがつながる）、得点板（いちばん上の足場。上の撃破の線が近い）、旗（アンカー）。
    /// </summary>
    internal static class StadiumStage
    {
        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.78f, 0.36f, 0.28f),
                Attachable = new Color(0.55f, 0.6f, 0.68f),
                Anchor = new Color(1f, 0.9f, 0.3f),
                SkyTint = new Color(0.45f, 0.6f, 0.9f),
                Ground = new Color(0.4f, 0.45f, 0.4f),
                Exposure = 1.4f,
                AtmosphereThickness = 0.9f,
                Light = new Color(1f, 0.97f, 0.9f),
                LightIntensity = 1.3f,
                LightEuler = new Vector3(45f, -35f, 0f),
                AmbientSky = new Color(0.65f, 0.72f, 0.85f),
                AmbientEquator = new Color(0.55f, 0.55f, 0.55f),
                AmbientGround = new Color(0.35f, 0.3f, 0.28f),
                FogColor = new Color(0.75f, 0.82f, 0.92f),
                FogStart = 60f,
                FogEnd = 190f,
            };
            theme.ZoneColors[StageZoneKind.Gust] = new Color(0.7f, 1f, 0.6f);
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(1f, 1f, 1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.85f, 0.3f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.55f, 0.55f, 0.6f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(0.85f, 0.75f, 0.55f);
            theme.ZoneColors[StageZoneKind.JumpPad] = new Color(0.3f, 0.6f, 1f);

            return new StageLayout
            {
                Id = "Stadium",
                DisplayName = "STORM STADIUM",
                Description = "No bottom to fall from: knock rivals off the sides of the track or over the scoreboard. Tailwinds and headwinds take turns.",
                Color = new Color(0.55f, 1f, 0.5f),
                Terrain = new[]
                {
                    // トラック（左右の撃破の線の外まで続く。下へは落ちない。ユーザーの案）。
                    new TerrainBox("Track", -76f, -6f, 76f, 0f),
                    // ハードル（脚の上のバー。下はくぐれない高さ）。
                    new TerrainBox("HurdleA", -26f, 0.6f, -25.6f, 1.1f),
                    new TerrainBox("HurdleB", -18f, 0.6f, -17.6f, 1.1f),
                    new TerrainBox("HurdleC", 17.6f, 0.6f, 18f, 1.1f),
                    new TerrainBox("HurdleD", 25.6f, 0.6f, 26f, 1.1f),
                    // 表彰台。
                    new TerrainBox("PodiumFirst", -1.5f, 0f, 1.5f, 2.4f),
                    new TerrainBox("PodiumSecond", -4.5f, 0f, -1.5f, 1.6f),
                    new TerrainBox("PodiumThird", 1.5f, 0f, 4.5f, 1f),
                    // スタンド（段）と屋根。
                    new TerrainBox("StandWest1", -50f, 0f, -42f, 2.5f),
                    new TerrainBox("StandWest2", -50f, 2.5f, -45f, 5f),
                    new TerrainBox("StandWest3", -50f, 5f, -47.5f, 7.5f),
                    new TerrainBox("StandRoofPoleWest", -50f, 7.5f, -49.6f, 16f, attachable: true),
                    new TerrainBox("StandRoofWest", -50f, 16f, -40f, 16.6f, attachable: true),
                    new TerrainBox("StandEast1", 42f, 0f, 50f, 2.5f),
                    new TerrainBox("StandEast2", 45f, 2.5f, 50f, 5f),
                    new TerrainBox("StandEast3", 47.5f, 5f, 50f, 7.5f),
                    new TerrainBox("StandRoofPoleEast", 49.6f, 7.5f, 50f, 16f, attachable: true),
                    new TerrainBox("StandRoofEast", 40f, 16f, 50f, 16.6f, attachable: true),
                    // 照明塔。
                    new TerrainBox("FloodlightPoleWest", -33f, 0f, -32.4f, 24f, attachable: true),
                    new TerrainBox("FloodlightDeckWest", -35.5f, 24f, -30f, 24.5f, attachable: true),
                    new TerrainBox("FloodlightPoleEast", 32.4f, 0f, 33f, 24f, attachable: true),
                    new TerrainBox("FloodlightDeckEast", 30f, 24f, 35.5f, 24.5f, attachable: true),
                    // 得点板（いちばん上。上の撃破の線が近い）。
                    new TerrainBox("Scoreboard", -8f, 28f, 8f, 33f, attachable: true),
                },
                // 旗と、照明塔と得点板の角。
                Anchors = new[]
                {
                    new Vector2(-24f, 20f), new Vector2(24f, 20f), new Vector2(-14f, 26f), new Vector2(14f, 26f), new Vector2(-40f, 28f),
                    new Vector2(40f, 28f), new Vector2(0f, 13f), new Vector2(-10f, 35f), new Vector2(10f, 35f),
                },
                // 下の撃破の線はトラックのずっと下（届かない）。左右と上だけ（ユーザーの案）。
                BoundsMin = new Vector2(-60f, -20f),
                BoundsMax = new Vector2(60f, 40f),
                Theme = theme,
                Zones = new[]
                {
                    // 追い風と向かい風: 競技場の全体に、24 秒ごとに 5 秒、右へ・左へ交互に吹く（3 秒前に予告）。地面の上でも流される。
                    // 最初は 6 秒後に向かい風、18 秒後に追い風（周期のずれの分だけ前に始まった周期として数えるので、ずれは 18 秒と 6 秒）。
                    new ZoneSpec("Tailwind", StageZoneKind.Gust, new Vector2(0f, 18f), new Vector2(120f, 36f), Vector2.right, 25f, 9f).Every(24f, 5f, 6f, 0f, 3f),
                    new ZoneSpec("Headwind", StageZoneKind.Gust, new Vector2(0f, 18f), new Vector2(120f, 36f), Vector2.left, 25f, 9f).Every(24f, 5f, 18f, 0f, 3f),
                    // 短距離のレーン（真ん中へ向かう）。
                    ZoneSpec.OnFloor("SprintLaneWest", StageZoneKind.SpeedLane, -32f, -15f, 0f, Vector2.right, 12f),
                    ZoneSpec.OnFloor("SprintLaneEast", StageZoneKind.SpeedLane, 15f, 32f, 0f, Vector2.left, 12f),
                    // 走り高跳びのマット、棒高跳び、走り幅跳びの踏み切り板。
                    ZoneSpec.OnFloor("HighJumpMat", StageZoneKind.JumpPad, -14f, -12f, 0f, Vector2.up, 28f, 0.4f),
                    ZoneSpec.OnFloor("PoleVault", StageZoneKind.Launcher, 12f, 14f, 0f, new Vector2(-0.4f, 1f), 30f),
                    ZoneSpec.OnFloor("LongJumpBoard", StageZoneKind.Launcher, -31.5f, -29.5f, 0f, new Vector2(1f, 0.55f), 24f),
                    ZoneSpec.OnFloor("LongJumpBoardEast", StageZoneKind.Launcher, 29.5f, 31.5f, 0f, new Vector2(-1f, 0.55f), 24f),
                    // スタンドの上のジャンプ台。
                    ZoneSpec.OnFloor("StandPadWest", StageZoneKind.JumpPad, -47f, -45.5f, 5f, Vector2.up, 26f, 0.4f),
                    ZoneSpec.OnFloor("StandPadEast", StageZoneKind.JumpPad, 45.5f, 47f, 5f, Vector2.up, 26f, 0.4f),
                    // 転がる砲丸: トラックの上を左右に行き来する（触れると弾かれる）。
                    new ZoneSpec("RollingShot", StageZoneKind.Bumper, new Vector2(-36f, 1.2f), new Vector2(2.2f, 2.2f), Vector2.up, 20f)
                        .Moving(new Vector2(72f, 0f), 18f),
                    // つむじ風: 表彰台の上を左右に行き来する上昇気流。
                    new ZoneSpec("DustDevil", StageZoneKind.Updraft, new Vector2(-6f, 8f), new Vector2(3f, 16f), Vector2.up, 50f, 14f)
                        .Moving(new Vector2(12f, 0f), 12f),
                    // 得点板へ向かう加速リング。
                    new ZoneSpec("RingWest", StageZoneKind.BoostRing, new Vector2(-20f, 16f), new Vector2(2.4f, 3f), new Vector2(1f, 0.5f), 18f),
                    new ZoneSpec("RingEast", StageZoneKind.BoostRing, new Vector2(20f, 16f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.5f), 18f),
                },
                Coins = new (Vector2, int)[]
                {
                    // レーンとハードルの上。
                    (new Vector2(-34.5f, 1.5f), 1), (new Vector2(-29.5f, 1.5f), 1), (new Vector2(-21.5f, 1.5f), 1), (new Vector2(-25.8f, 2.4f), 1),
                    (new Vector2(-17.8f, 2.4f), 1), (new Vector2(34.5f, 1.5f), 1), (new Vector2(29.5f, 1.5f), 1), (new Vector2(21.5f, 1.5f), 1),
                    (new Vector2(25.8f, 2.4f), 1), (new Vector2(17.8f, 2.4f), 1),
                    // 表彰台。
                    (new Vector2(-3f, 3f), 1), (new Vector2(3f, 2.5f), 1), (new Vector2(0f, 3.8f), 3),
                    // 走り高跳びと棒高跳びの道。
                    (new Vector2(-13f, 5f), 1), (new Vector2(-13f, 9f), 1), (new Vector2(-13f, 13f), 3), (new Vector2(9f, 8f), 1), (new Vector2(5f, 11f), 1),
                    (new Vector2(0f, 9f), 1),
                    // 走り幅跳びの道。
                    (new Vector2(-34f, 5f), 1), (new Vector2(-28f, 7f), 1), (new Vector2(34f, 5f), 1), (new Vector2(28f, 7f), 1),
                    // スタンド（段の上と屋根の上）。
                    (new Vector2(-43.5f, 4.5f), 1), (new Vector2(-46.5f, 7f), 1), (new Vector2(-48.8f, 9.5f), 3), (new Vector2(-45f, 18.5f), 3),
                    (new Vector2(-41.5f, 18.5f), 1), (new Vector2(43.5f, 4.5f), 1), (new Vector2(46.5f, 7f), 1), (new Vector2(48.8f, 9.5f), 3),
                    (new Vector2(45f, 18.5f), 3), (new Vector2(41.5f, 18.5f), 1),
                    // 照明塔。
                    (new Vector2(-31.5f, 12f), 1), (new Vector2(-31.5f, 20f), 1), (new Vector2(-35f, 26.5f), 3), (new Vector2(-31f, 30f), 20),
                    (new Vector2(31.5f, 12f), 1), (new Vector2(31.5f, 20f), 1), (new Vector2(35f, 26.5f), 3), (new Vector2(31f, 30f), 20),
                    // 加速リングと得点板の周り。
                    (new Vector2(-16f, 19f), 1), (new Vector2(16f, 19f), 1), (new Vector2(-10f, 24f), 1), (new Vector2(10f, 24f), 1),
                    (new Vector2(-10f, 30f), 3), (new Vector2(10f, 30f), 3), (new Vector2(0f, 35f), 20), (new Vector2(-5f, 26f), 1), (new Vector2(5f, 26f), 1),
                    // トラックの左右の端（撃破の線の近く）。
                    (new Vector2(-53f, 2f), 20), (new Vector2(53f, 2f), 20), (new Vector2(-53f, 12f), 3), (new Vector2(53f, 12f), 3),
                    (new Vector2(-53f, 22f), 20), (new Vector2(53f, 22f), 20),
                    // 高い所。
                    (new Vector2(-22f, 30f), 20), (new Vector2(22f, 30f), 20), (new Vector2(-24f, 10f), 20), (new Vector2(24f, 10f), 20),
                    (new Vector2(0f, 19f), 20), (new Vector2(-40f, 24f), 3), (new Vector2(40f, 24f), 3), (new Vector2(-6f, 15f), 1), (new Vector2(6f, 15f), 1),
                    (new Vector2(-12f, 21.5f), 20), (new Vector2(12f, 21.5f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 38f), new Vector2(-54f, 30f), new Vector2(54f, 30f), new Vector2(-54f, 4f), new Vector2(54f, 4f),
                    new Vector2(0f, 22f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 6.5f), new Vector2(-46f, 11f), new Vector2(46f, 11f), new Vector2(-32.7f, 27.5f), new Vector2(32.7f, 27.5f),
                    new Vector2(-12f, 33f), new Vector2(-20f, 11f), new Vector2(20f, 11f),
                },
                Spawns = new[] { new Vector3(-9f, 1f, 0f), new Vector3(9f, 1f, 0f), new Vector3(-43.5f, 3.5f, 0f), new Vector3(43.5f, 3.5f, 0f) },
                Decorate = Decorate,
            };
        }

        // トラックのレーンの線と芝生、スタンドの観客、照明、得点板の画面、空の雲。
        private static void Decorate(StageDecor decor)
        {
            Material lane = decor.Unlit("LaneLine", new Color(1f, 1f, 1f));
            Material grass = decor.Lit("Grass", new Color(0.25f, 0.55f, 0.25f));
            Material seats = decor.Lit("Seats", new Color(0.2f, 0.4f, 0.75f));
            Material crowd = decor.Lit("Crowd", new Color(0.7f, 0.5f, 0.4f));
            Material light = decor.Unlit("Floodlight", new Color(1f, 0.98f, 0.85f));
            Material screen = decor.Unlit("ScoreScreen", new Color(0.15f, 0.9f, 0.5f));
            Material cloud = decor.Lit("Cloud", new Color(0.97f, 0.97f, 1f));
            Material bar = decor.Lit("HighJumpBar", new Color(1f, 0.85f, 0.2f));
            Material gold = decor.Lit("Gold", new Color(1f, 0.8f, 0.25f));

            // 奥の芝生と、トラックのレーンの線。
            decor.Box("Infield", new Vector3(0f, -0.4f, 16f), new Vector3(160f, 0.8f, 26f), grass);
            for (int i = 0; i < 4; i++)
            {
                decor.Box("Lane", new Vector3(0f, 0.02f, -1.5f + i * 1f), new Vector3(152f, 0.02f, 0.08f), lane);
            }
            // 奥のスタンドと観客。
            for (int tier = 0; tier < 5; tier++)
            {
                decor.Box("BackStand", new Vector3(0f, 3f + tier * 3f, 32f + tier * 3f), new Vector3(170f, 3f, 3f), seats);
                for (int i = 0; i < 40; i++)
                {
                    decor.Box("Fan", new Vector3(-80f + i * 4f + decor.Range(-1f, 1f), 5f + tier * 3f, 31f + tier * 3f), new Vector3(0.8f, 1.2f, 0.5f), crowd);
                }
            }
            // 照明塔の灯り、得点板の画面、走り高跳びのバー、表彰台の金。
            decor.Box("FloodlampWest", new Vector3(-32.7f, 25.5f, 0.6f), new Vector3(5f, 1.6f, 0.4f), light);
            decor.Box("FloodlampEast", new Vector3(32.7f, 25.5f, 0.6f), new Vector3(5f, 1.6f, 0.4f), light);
            decor.Quad("Screen", new Vector3(0f, 30.5f, -2.05f), new Vector2(14f, 4f), screen);
            decor.Box("HighJumpBar", new Vector3(-13f, 3.5f, 0.6f), new Vector3(3f, 0.12f, 0.12f), bar);
            decor.Box("PodiumGold", new Vector3(0f, 2.41f, 0f), new Vector3(3f, 0.04f, 4f), gold);
            // 空の雲。
            for (int i = 0; i < 10; i++)
            {
                decor.Sphere("Cloud", new Vector3(decor.Range(-90f, 90f), decor.Range(40f, 60f), decor.Range(60f, 110f)),
                    new Vector3(decor.Range(12f, 24f), decor.Range(4f, 8f), 6f), cloud);
            }
        }
    }
}
