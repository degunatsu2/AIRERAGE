using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 昼のビル街（SKYLINE DAY）と夜のビル街（SKYLINE NIGHT）。地形は同じ（ユーザーの要望、2026-10-19）で、固有のギミックとギミックの
    /// 置き場所が違う。ビルの屋上を跳び移り、ガラスの壁（接続できる）にワイヤーを掛けて谷を渡る。ビルの間の谷に落ちると撃破。
    /// - 昼: 動く歩道（SpeedLane。走ったまま跳ぶと隣のビルへ）、屋上のダクト（Launcher。塔の屋上へ打ち上げる）、
    ///   ビル風（周期のある横風。左向きと右向きが交互に吹く）。
    /// - 夜: ネオン看板（Bumper。当たると弾かれる）、地下鉄の通風口（周期のある上向きの風。谷の底から噴き上げる）、
    ///   谷の上昇気流の代わりに、狭い谷に上昇気流。
    /// </summary>
    internal static class CityStages
    {
        // 地形（左右対称）。ビルは下へ長く伸ばす（谷の壁になる）。ガラスの壁と屋上は接続できる。
        private static readonly TerrainBox[] Terrain =
        {
            new TerrainBox("TowerWest", -46f, -30f, -35f, 15f, attachable: true),
            new TerrainBox("OfficeWest", -29f, -30f, -18f, 4f, attachable: true),
            new TerrainBox("Plaza", -9f, -30f, 9f, 0f, attachable: true),
            new TerrainBox("OfficeEast", 18f, -30f, 29f, 4f, attachable: true),
            new TerrainBox("TowerEast", 35f, -30f, 46f, 15f, attachable: true),
            // 塔の内側の壁のバルコニー（屋上から塔へ上る足がかり）。
            new TerrainBox("BalconyWest", -35f, 7f, -32.6f, 7.4f, attachable: true),
            new TerrainBox("BalconyEast", 32.6f, 7f, 35f, 7.4f, attachable: true),
            // 谷の上の渡り廊下（鉄骨）。
            new TerrainBox("SkyBridgeWest", -22f, 12f, -13f, 12.5f, attachable: true),
            new TerrainBox("SkyBridgeEast", 13f, 12f, 22f, 12.5f, attachable: true),
            // ヘリポートとクレーン（高い所の足場）。
            new TerrainBox("HelipadWest", -31f, 21f, -25f, 21.5f, attachable: true),
            new TerrainBox("HelipadEast", 25f, 21f, 31f, 21.5f, attachable: true),
            new TerrainBox("CraneJib", -7f, 27f, 7f, 27.6f, attachable: true),
            // 塔の屋上の階段室（接続できない）。
            new TerrainBox("RooftopHutWest", -45f, 15f, -42f, 17f),
            new TerrainBox("RooftopHutEast", 42f, 15f, 45f, 17f),
        };

        private static readonly Vector2[] Anchors =
        {
            new Vector2(0f, 33f),
            new Vector2(-16f, 24f),
            new Vector2(16f, 24f),
            new Vector2(-38f, 26f),
            new Vector2(38f, 26f),
            new Vector2(-13.5f, 7f),
            new Vector2(13.5f, 7f),
            new Vector2(-32f, 16f),
            new Vector2(32f, 16f),
        };

        private static readonly Vector2 BoundsMin = new Vector2(-48f, -22f);
        private static readonly Vector2 BoundsMax = new Vector2(48f, 46f);

        public static StageLayout Day()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.62f, 0.63f, 0.66f),
                Attachable = new Color(0.42f, 0.62f, 0.78f),
                SkyTint = new Color(0.45f, 0.58f, 0.85f),
                Ground = new Color(0.45f, 0.47f, 0.5f),
                Exposure = 1.35f,
                AtmosphereThickness = 0.9f,
                Light = new Color(1f, 0.97f, 0.9f),
                LightIntensity = 1.3f,
                LightEuler = new Vector3(48f, -35f, 0f),
                AmbientSky = new Color(0.62f, 0.72f, 0.88f),
                AmbientEquator = new Color(0.5f, 0.52f, 0.56f),
                AmbientGround = new Color(0.3f, 0.3f, 0.32f),
                FogColor = new Color(0.72f, 0.8f, 0.92f),
            };
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(0.25f, 0.95f, 1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.55f, 0.2f);
            theme.ZoneColors[StageZoneKind.Gust] = new Color(0.92f, 0.97f, 1f);

            return new StageLayout
            {
                Id = "CityDay",
                DisplayName = "SKYLINE DAY",
                Description = "Rooftop parkour. Moving walkways, rooftop ducts and gusts between the towers.",
                Color = new Color(0.4f, 0.75f, 1f),
                Terrain = Terrain,
                Anchors = Anchors,
                BoundsMin = BoundsMin,
                BoundsMax = BoundsMax,
                Theme = theme,
                Zones = new[]
                {
                    // 広場のジャンプ台と、真上の加速リング（クレーンの下まで）。
                    ZoneSpec.OnFloor("PlazaPad", StageZoneKind.JumpPad, -1f, 1f, 0f, Vector2.up, 28f, 0.4f),
                    new ZoneSpec("PlazaRing", StageZoneKind.BoostRing, new Vector2(0f, 16f), new Vector2(3f, 3.5f), Vector2.up, 21f),
                    // 広い谷の上昇気流（落ちた人を屋上の高さまで押し上げる）。
                    new ZoneSpec("CanyonUpdraftWest", StageZoneKind.Updraft, new Vector2(-13.5f, -9f), new Vector2(9f, 26f), Vector2.up, 60f, 13f),
                    new ZoneSpec("CanyonUpdraftEast", StageZoneKind.Updraft, new Vector2(13.5f, -9f), new Vector2(9f, 26f), Vector2.up, 60f, 13f),
                    // 狭い谷の上の加速リング（塔から跳ぶと中央へ飛ばす）。
                    new ZoneSpec("GapRingWest", StageZoneKind.BoostRing, new Vector2(-32f, 11f), new Vector2(2.4f, 3f), new Vector2(1f, 0.15f), 18f),
                    new ZoneSpec("GapRingEast", StageZoneKind.BoostRing, new Vector2(32f, 11f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.15f), 18f),
                    // 塔の屋上のジャンプ台（ヘリポートとアンカーへ）。
                    ZoneSpec.OnFloor("TowerPadWest", StageZoneKind.JumpPad, -40f, -38f, 15f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("TowerPadEast", StageZoneKind.JumpPad, 38f, 40f, 15f, Vector2.up, 24f, 0.4f),
                    // 動く歩道: 広場の端は外向き（跳ぶとオフィスへ。二段ジャンプで届く）、オフィスの屋上は広場へ向かう。
                    ZoneSpec.OnFloor("PlazaWalkwayWest", StageZoneKind.SpeedLane, -8.8f, -6.3f, 0f, Vector2.left, 16f),
                    ZoneSpec.OnFloor("PlazaWalkwayEast", StageZoneKind.SpeedLane, 6.3f, 8.8f, 0f, Vector2.right, 16f),
                    ZoneSpec.OnFloor("OfficeWalkwayWest", StageZoneKind.SpeedLane, -24f, -19f, 4f, Vector2.right, 16f),
                    ZoneSpec.OnFloor("OfficeWalkwayEast", StageZoneKind.SpeedLane, 19f, 24f, 4f, Vector2.left, 16f),
                    // 屋上のダクト: 外側の塔の屋上へ打ち上げる。
                    ZoneSpec.OnFloor("DuctWest", StageZoneKind.Launcher, -28.6f, -26.6f, 4f, new Vector2(-0.35f, 1f), 28f),
                    ZoneSpec.OnFloor("DuctEast", StageZoneKind.Launcher, 26.6f, 28.6f, 4f, new Vector2(0.35f, 1f), 28f),
                    // ビル風: 上空を、左向きと右向きが 5 秒ずつずれて 3.5 秒ずつ吹く（吹く 1 秒前に点滅で知らせる）。
                    new ZoneSpec("BuildingWindWest", StageZoneKind.Gust, new Vector2(0f, 21f), new Vector2(68f, 10f), Vector2.left, 24f, 9f).Every(10f, 3.5f, 0f),
                    new ZoneSpec("BuildingWindEast", StageZoneKind.Gust, new Vector2(0f, 21f), new Vector2(68f, 10f), Vector2.right, 24f, 9f).Every(10f, 3.5f, 5f),
                },
                Coins = new (Vector2, int)[]
                {
                    // 広場のジャンプ台の上と、加速リングの上。
                    (new Vector2(0f, 4.5f), 1), (new Vector2(0f, 9.5f), 1), (new Vector2(0f, 13f), 1),
                    (new Vector2(0f, 20.5f), 1), (new Vector2(0f, 23.5f), 1),
                    // オフィスの動く歩道から広場へ跳ぶ道と、広場からオフィスへ跳ぶ道。
                    (new Vector2(-15f, 6.7f), 1), (new Vector2(-12f, 7.3f), 1), (new Vector2(15f, 6.7f), 1), (new Vector2(12f, 7.3f), 1),
                    (new Vector2(-12f, 3f), 1), (new Vector2(12f, 3f), 1),
                    // 渡り廊下の上、ダクトで飛ぶ道、塔のジャンプ台の上、ヘリポートの上。
                    (new Vector2(-17.5f, 14f), 1), (new Vector2(17.5f, 14f), 1),
                    (new Vector2(-30f, 10f), 1), (new Vector2(-32.2f, 13.6f), 1), (new Vector2(30f, 10f), 1), (new Vector2(32.2f, 13.6f), 1),
                    (new Vector2(-39f, 19f), 1), (new Vector2(39f, 19f), 1),
                    (new Vector2(-28f, 23.5f), 1), (new Vector2(28f, 23.5f), 1),
                    // 3 枚: クレーンの上、ビル風の中、塔の上空。
                    (new Vector2(0f, 30f), 3), (new Vector2(-22f, 17f), 3), (new Vector2(22f, 17f), 3),
                    (new Vector2(-41f, 24f), 3), (new Vector2(41f, 24f), 3),
                    // 20 枚: 谷の底（広い谷は上昇気流、狭い谷は助からない）、いちばん上、ステージの端。
                    (new Vector2(-13.5f, -14f), 20), (new Vector2(13.5f, -14f), 20), (new Vector2(-32f, -5f), 20), (new Vector2(32f, -5f), 20),
                    (new Vector2(0f, 37f), 20), (new Vector2(-46f, 27f), 20), (new Vector2(46f, 27f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 41f), new Vector2(-46f, 33f), new Vector2(46f, 33f), new Vector2(-13.5f, -18f), new Vector2(13.5f, -18f),
                    new Vector2(-32f, -12f), new Vector2(32f, -12f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7f), new Vector2(-13.5f, 9.5f), new Vector2(13.5f, 9.5f), new Vector2(-39f, 22f), new Vector2(39f, 22f),
                    new Vector2(-28f, 26.5f), new Vector2(28f, 26.5f),
                },
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-25.5f, 5f, 0f), new Vector3(25.5f, 5f, 0f) },
                Decorate = decor => DecorateCity(decor, night: false),
            };
        }

        public static StageLayout Night()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.2f, 0.21f, 0.26f),
                Attachable = new Color(0.12f, 0.22f, 0.38f),
                Anchor = new Color(1f, 0.85f, 0.25f),
                SkyTint = new Color(0.12f, 0.12f, 0.3f),
                Ground = new Color(0.05f, 0.05f, 0.08f),
                Exposure = 0.18f,
                AtmosphereThickness = 0.6f,
                SunSize = 0.03f,
                Light = new Color(0.55f, 0.65f, 1f),
                LightIntensity = 0.45f,
                LightEuler = new Vector3(35f, 30f, 0f),
                AmbientSky = new Color(0.22f, 0.24f, 0.42f),
                AmbientEquator = new Color(0.18f, 0.16f, 0.28f),
                AmbientGround = new Color(0.08f, 0.07f, 0.1f),
                FogColor = new Color(0.08f, 0.08f, 0.16f),
                FogStart = 60f,
                FogEnd = 220f,
            };
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(1f, 0.25f, 0.85f);
            theme.ZoneColors[StageZoneKind.Gust] = new Color(0.75f, 0.9f, 1f);
            theme.ZoneColors[StageZoneKind.JumpPad] = new Color(0.3f, 1f, 0.6f);
            theme.ZoneColors[StageZoneKind.BoostRing] = new Color(1f, 0.85f, 0.2f);

            return new StageLayout
            {
                Id = "CityNight",
                DisplayName = "SKYLINE NIGHT",
                Description = "The same rooftops after dark. Neon signs bounce you, subway vents blast up from the canyons.",
                Color = new Color(0.85f, 0.3f, 1f),
                Terrain = Terrain,
                Anchors = Anchors,
                BoundsMin = BoundsMin,
                BoundsMax = BoundsMax,
                Theme = theme,
                Zones = new[]
                {
                    // オフィスの屋上のジャンプ台（ヘリポートの下。届かない高さ）。
                    ZoneSpec.OnFloor("OfficePadWest", StageZoneKind.JumpPad, -27f, -25f, 4f, Vector2.up, 26f, 0.4f),
                    ZoneSpec.OnFloor("OfficePadEast", StageZoneKind.JumpPad, 25f, 27f, 4f, Vector2.up, 26f, 0.4f),
                    // 狭い谷の上昇気流（昼と違い、こちらで助かる）。
                    new ZoneSpec("GapUpdraftWest", StageZoneKind.Updraft, new Vector2(-32f, -7.5f), new Vector2(6f, 29f), Vector2.up, 60f, 13f),
                    new ZoneSpec("GapUpdraftEast", StageZoneKind.Updraft, new Vector2(32f, -7.5f), new Vector2(6f, 29f), Vector2.up, 60f, 13f),
                    // 地下鉄の通風口: 広い谷の底から、左右が 3 秒ずれて 2 秒ずつ噴き上げる（止んでいる間に谷へ落ちると助からない）。
                    new ZoneSpec("SubwayVentWest", StageZoneKind.Gust, new Vector2(-11.25f, -8f), new Vector2(3.5f, 28f), Vector2.up, 90f, 22f).Every(6f, 2f, 0f),
                    new ZoneSpec("SubwayVentEast", StageZoneKind.Gust, new Vector2(11.25f, -8f), new Vector2(3.5f, 28f), Vector2.up, 90f, 22f).Every(6f, 2f, 3f),
                    // 塔から跳ぶ高さと、クレーンの下の加速リング（横向き）。
                    new ZoneSpec("TowerRingWest", StageZoneKind.BoostRing, new Vector2(-31.5f, 17f), new Vector2(2.4f, 3f), Vector2.right, 20f),
                    new ZoneSpec("TowerRingEast", StageZoneKind.BoostRing, new Vector2(31.5f, 17f), new Vector2(2.4f, 3f), Vector2.left, 20f),
                    new ZoneSpec("CraneRing", StageZoneKind.BoostRing, new Vector2(0f, 22f), new Vector2(2.4f, 3f), Vector2.right, 20f),
                    // ネオン看板: 広場の上のトランポリン、オフィスの上と通風口のそばの縦長の看板（横から当たると跳ね返る）。
                    new ZoneSpec("NeonCenter", StageZoneKind.Bumper, new Vector2(0f, 12.5f), new Vector2(5f, 0.8f), Vector2.up, 20f),
                    new ZoneSpec("NeonOfficeWest", StageZoneKind.Bumper, new Vector2(-23f, 19f), new Vector2(1.2f, 3f), Vector2.up, 20f),
                    new ZoneSpec("NeonOfficeEast", StageZoneKind.Bumper, new Vector2(23f, 19f), new Vector2(1.2f, 3f), Vector2.up, 20f),
                    new ZoneSpec("NeonVentWest", StageZoneKind.Bumper, new Vector2(-15.5f, 17f), new Vector2(1f, 2.4f), Vector2.up, 20f),
                    new ZoneSpec("NeonVentEast", StageZoneKind.Bumper, new Vector2(15.5f, 17f), new Vector2(1f, 2.4f), Vector2.up, 20f),
                },
                Coins = new (Vector2, int)[]
                {
                    // 通風口の上（噴き上げに乗ると続けて取れる）。
                    (new Vector2(-11.25f, 4f), 1), (new Vector2(-11.25f, 9f), 1), (new Vector2(-11.25f, 13f), 1),
                    (new Vector2(11.25f, 4f), 1), (new Vector2(11.25f, 9f), 1), (new Vector2(11.25f, 13f), 1),
                    // オフィスのジャンプ台の上。
                    (new Vector2(-26f, 9f), 1), (new Vector2(-26f, 14f), 1), (new Vector2(26f, 9f), 1), (new Vector2(26f, 14f), 1),
                    // 広場と、トランポリンの上。
                    (new Vector2(-4f, 3f), 1), (new Vector2(4f, 3f), 1), (new Vector2(0f, 16f), 1), (new Vector2(0f, 19.5f), 1),
                    // 塔の加速リングの先、渡り廊下の上、バルコニーの上。
                    (new Vector2(-27f, 17.5f), 1), (new Vector2(27f, 17.5f), 1), (new Vector2(-18f, 14f), 1), (new Vector2(18f, 14f), 1),
                    (new Vector2(-33.8f, 9.5f), 1), (new Vector2(33.8f, 9.5f), 1),
                    // 3 枚: 縦長の看板の上、クレーンの上、塔の屋上。
                    (new Vector2(-23f, 23f), 3), (new Vector2(23f, 23f), 3), (new Vector2(0f, 30f), 3),
                    (new Vector2(-41f, 18.5f), 3), (new Vector2(41f, 18.5f), 3),
                    // 20 枚: 通風口の底、狭い谷の底、いちばん上、ステージの端。
                    (new Vector2(-11.25f, -12f), 20), (new Vector2(11.25f, -12f), 20), (new Vector2(-32f, -12f), 20), (new Vector2(32f, -12f), 20),
                    (new Vector2(0f, 38f), 20), (new Vector2(-46f, 25f), 20), (new Vector2(46f, 25f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 42f), new Vector2(-46f, 31f), new Vector2(46f, 31f), new Vector2(-13.5f, -18f), new Vector2(13.5f, -18f),
                    new Vector2(-32f, -17f), new Vector2(32f, -17f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7.5f), new Vector2(-21f, 8.5f), new Vector2(21f, 8.5f), new Vector2(-38f, 21f), new Vector2(38f, 21f),
                    new Vector2(-28f, 25f), new Vector2(28f, 25f),
                },
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-22f, 5f, 0f), new Vector3(22f, 5f, 0f) },
                Decorate = decor => DecorateCity(decor, night: true),
            };
        }

        // 背景のビル群（奥行きのある箱）と、地形のビルの正面の窓。夜は窓とネオンが光る。
        private static void DecorateCity(StageDecor decor, bool night)
        {
            Material facade = night ? decor.Lit("Facade", new Color(0.1f, 0.11f, 0.16f)) : decor.Lit("Facade", new Color(0.55f, 0.6f, 0.68f));
            Material glass = night ? decor.Lit("FacadeGlass", new Color(0.07f, 0.1f, 0.18f)) : decor.Lit("FacadeGlass", new Color(0.4f, 0.55f, 0.72f));
            Material window = night ? decor.Unlit("Window", new Color(1f, 0.85f, 0.45f)) : decor.Lit("Window", new Color(0.25f, 0.38f, 0.55f));
            Material windowCool = night ? decor.Unlit("WindowCool", new Color(0.45f, 0.85f, 1f)) : decor.Lit("WindowCool", new Color(0.32f, 0.45f, 0.6f));
            Material neonPink = decor.Unlit("NeonPink", new Color(1f, 0.25f, 0.8f));
            Material neonCyan = decor.Unlit("NeonCyan", new Color(0.2f, 0.95f, 1f));
            Material street = decor.Lit("Street", night ? new Color(0.06f, 0.06f, 0.08f) : new Color(0.3f, 0.3f, 0.32f));

            // 地形のビルの奥行き（見た目だけ）と正面の窓。
            (float MinX, float MaxX, float Top)[] buildings =
            {
                (-46f, -35f, 15f), (-29f, -18f, 4f), (-9f, 9f, 0f), (18f, 29f, 4f), (35f, 46f, 15f),
            };
            foreach (var building in buildings)
            {
                float width = building.MaxX - building.MinX;
                float height = building.Top + 30f;
                decor.Box("Volume", new Vector3((building.MinX + building.MaxX) * 0.5f, building.Top - height * 0.5f, 7f),
                    new Vector3(width, height, 10f), building.Top > 10f ? glass : facade);
                for (float y = building.Top - 2f; y > -21f; y -= 2.6f)
                {
                    Material m = decor.Random.NextDouble() < 0.3 ? windowCool : window;
                    if (night && decor.Random.NextDouble() < 0.35)
                    {
                        // 夜は消えている窓もある。
                        continue;
                    }
                    decor.Quad("Windows", new Vector3((building.MinX + building.MaxX) * 0.5f, y, -2.02f), new Vector2(width - 1.2f, 0.9f), m);
                }
            }
            // 谷の底の道路（撃破の範囲の下）。
            decor.Box("Street", new Vector3(0f, -24f, 6f), new Vector3(110f, 0.5f, 30f), street);

            // 背景のビル群（3 列。奥ほど高く、霧で薄くなる）。
            float[] rows = { 18f, 38f, 70f };
            for (int row = 0; row < rows.Length; row++)
            {
                float z = rows[row];
                for (float x = -110f; x < 110f;)
                {
                    float width = decor.Range(5f, 12f);
                    float top = decor.Range(-2f, 22f) + row * 10f;
                    var center = new Vector3(x + width * 0.5f, (top - 30f) * 0.5f, z);
                    decor.Box("Skyline", center, new Vector3(width, top + 30f, decor.Range(6f, 12f)), decor.Random.NextDouble() < 0.5 ? facade : glass);
                    if (night)
                    {
                        // 窓の明かり（前の面）と、屋上の赤い航空障害灯。
                        for (float y = top - 2f; y > -10f; y -= decor.Range(2.5f, 4f))
                        {
                            if (decor.Random.NextDouble() < 0.55)
                            {
                                decor.Quad("Lights", new Vector3(center.x, y, z - 3.05f), new Vector2(width * decor.Range(0.3f, 0.85f), 0.7f),
                                    decor.Random.NextDouble() < 0.25 ? windowCool : window);
                            }
                        }
                        if (decor.Random.NextDouble() < 0.4)
                        {
                            decor.Sphere("BeaconLight", new Vector3(center.x, top + 0.4f, z), new Vector3(0.6f, 0.6f, 0.6f), decor.Unlit("Beacon", new Color(1f, 0.2f, 0.15f)));
                        }
                    }
                    x += width + decor.Range(1f, 5f);
                }
            }

            // 屋上の飾り（給水塔、アンテナ）。夜はビルの縁のネオン。
            decor.Box("WaterTankWest", new Vector3(-37.5f, 16.2f, 2.5f), new Vector3(2f, 2.4f, 2f), facade);
            decor.Box("WaterTankEast", new Vector3(37.5f, 16.2f, 2.5f), new Vector3(2f, 2.4f, 2f), facade);
            decor.Box("AntennaWest", new Vector3(-43.5f, 22f, 1.5f), new Vector3(0.25f, 10f, 0.25f), facade);
            decor.Box("AntennaEast", new Vector3(43.5f, 22f, 1.5f), new Vector3(0.25f, 10f, 0.25f), facade);
            decor.Box("CraneMast", new Vector3(5f, 13.5f, 7f), new Vector3(0.8f, 27f, 0.8f), decor.Lit("Crane", new Color(0.85f, 0.7f, 0.25f)));
            if (night)
            {
                foreach (var building in buildings)
                {
                    decor.Box("NeonEdge", new Vector3((building.MinX + building.MaxX) * 0.5f, building.Top + 0.04f, -2.05f),
                        new Vector3(building.MaxX - building.MinX, 0.08f, 0.08f), building.MinX < 0f ? neonPink : neonCyan);
                }
                decor.Sphere("Moon", new Vector3(-50f, 48f, 110f), new Vector3(9f, 9f, 9f), decor.Unlit("Moon", new Color(0.95f, 0.95f, 0.85f)));
            }
        }
    }
}
