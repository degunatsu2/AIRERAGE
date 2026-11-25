using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 昼のビル街（SKYLINE DAY）と夜のビル街（SKYLINE NIGHT）。地形は同じ（ユーザーの要望、2026-10-19）で、固有のギミックとギミックの
    /// 置き場所が違う。ビルの屋上を跳び移り、ガラスの壁（接続できる）にワイヤーを掛けて谷を渡る。ビルの間の谷に落ちると撃破。
    /// 真ん中のビルの下のほうはくりぬいてあり（ユーザーの要望、2026-10-19）、中の床から左右の谷の風へ出られる。中の真ん中に拠点の候補。
    /// - 昼: 動く歩道（SpeedLane。走ったまま跳ぶと隣のビルへ）、屋上のダクト（Launcher。塔の屋上へ打ち上げる）、
    ///   ビル風（周期のある横風。左向きと右向きが交互に吹く）。中央の左の谷は下向きの風（くりぬいた中へ降りる）、右の谷は上昇気流。
    /// - 夜: ネオン看板（Bumper。当たると弾かれる。ステージの外側と、左右のビルの内側の壁にも左右対称に）、
    ///   地下鉄の通風口（周期のある上向きの風。谷の底から噴き上げる）、谷の上昇気流の代わりに、狭い谷に上昇気流。
    /// </summary>
    internal static class CityStages
    {
        // 地形（左右対称）。ビルは下へ長く伸ばす（谷の壁になる）。ガラスの壁と屋上は接続できる。
        private static readonly TerrainBox[] Terrain =
        {
            new TerrainBox("TowerWest", -46f, -30f, -35f, 15f, attachable: true),
            new TerrainBox("OfficeWest", -29f, -30f, -18f, 4f, attachable: true),
            // 真ん中のビル: 屋上の板と、くりぬいた中の床（y = -10〜-4 が空いていて、左右の谷へ出られる）。
            new TerrainBox("Plaza", -9f, -4f, 9f, 0f, attachable: true),
            new TerrainBox("PlazaBase", -9f, -30f, 9f, -10f, attachable: true),
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

        // 左右は塔の外へ広めに（横へ飛ばされてすぐ撃破されないように。ユーザーの要望、2026-10-19）。
        private static readonly Vector2 BoundsMin = new Vector2(-60f, -22f);
        private static readonly Vector2 BoundsMax = new Vector2(60f, 46f);

        /// <summary>くりぬいた中の真ん中の拠点の候補。</summary>
        private static readonly Vector2 HollowBank = new Vector2(0f, -7.5f);

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
            // 谷の上昇気流と下向きの風は同じ色（ユーザーの要望、2026-10-29）。
            var draft = new Color(0.75f, 0.92f, 1f);
            theme.ZoneColors[StageZoneKind.Updraft] = draft;

            return new StageLayout
            {
                Id = "CityDay",
                Music = "CityDay",
                DisplayName = "SKYLINE DAY",
                Description = "Rooftop parkour. Moving walkways, rooftop ducts and gusts between the towers.",
                DescriptionJa = "ビルの屋上を駆けるパルクール。動く歩道、屋上のダクト、ビルの間を吹き抜ける風。",
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
                    // 広い谷: 左は下向きの風（くりぬいた中へ速く降りる。中へ逃げないと谷の底へ）、右は上昇気流（屋上の高さまで押し上げる）。
                    // 2 つの風は同じ速さ（ユーザーの要望、2026-10-29）: 重力（30 m/s²）を含めた加速と、目指す速さを、前の 2 つ（下は 30 ＋ 45 = 75 m/s²・
                    // 落ちる上限 20 m/s、上は 60 − 30 = 30 m/s²・13 m/s）の中間の 52.5 m/s²・16.5 m/s にそろえる（下は、ふだんの落ちる上限 20 m/s までは
                    // 重力で速くなる）。
                    // 見た目も上昇気流と同じ（色と流れる筋。StageZoneView。ユーザーの要望、2026-10-29）。
                    new ZoneSpec("CanyonDowndraftWest", StageZoneKind.Gust, new Vector2(-13.5f, -9f), new Vector2(9f, 26f), Vector2.down, 22.5f, 16.5f)
                        .Tinted(draft),
                    new ZoneSpec("CanyonUpdraftEast", StageZoneKind.Updraft, new Vector2(13.5f, -9f), new Vector2(9f, 26f), Vector2.up, 82.5f, 16.5f),
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
                    // 屋上のダクト: 外側の塔の屋上へ打ち上げる（縁の下で壁に張り付かないように、屋上の縁より十分高く飛ばす）。
                    ZoneSpec.OnFloor("DuctWest", StageZoneKind.Launcher, -28.6f, -26.6f, 4f, new Vector2(-0.35f, 1f), 30f),
                    ZoneSpec.OnFloor("DuctEast", StageZoneKind.Launcher, 26.6f, 28.6f, 4f, new Vector2(0.35f, 1f), 30f),
                    // ビル風: 上空を、左向きと右向きが 5 秒ずつずれて 3.5 秒ずつ吹く（吹く 1 秒前に点滅で知らせる。強め。ユーザーの要望、2026-10-19）。
                    new ZoneSpec("BuildingWindWest", StageZoneKind.Gust, new Vector2(0f, 21f), new Vector2(68f, 10f), Vector2.left, 36f, 13f).Every(10f, 3.5f, 0f),
                    new ZoneSpec("BuildingWindEast", StageZoneKind.Gust, new Vector2(0f, 21f), new Vector2(68f, 10f), Vector2.right, 36f, 13f).Every(10f, 3.5f, 5f),
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
                    // くりぬいた中の床の上。
                    (new Vector2(-5f, -8f), 1), (new Vector2(5f, -8f), 1),
                    // 20 枚: 広い谷のくりぬいた中の入り口（左は下向きの風の中）、狭い谷（助からない）、いちばん上、ステージの端。
                    (new Vector2(-11.5f, -6.5f), 20), (new Vector2(11.5f, -6.5f), 20), (new Vector2(-32f, -5f), 20), (new Vector2(32f, -5f), 20),
                    (new Vector2(0f, 37f), 20), (new Vector2(-46f, 27f), 20), (new Vector2(46f, 27f), 20),
                    // 追加（総量を 2 倍に。ユーザーの要望、2026-10-21）。くりぬいた中（隠れ家にしやすい所。20 枚 2 つと 3 枚 2 つを固めて、取り合いを激しくする）。
                    (new Vector2(-7.5f, -6f), 20), (new Vector2(7.5f, -6f), 20), (new Vector2(-3f, -5.5f), 3), (new Vector2(3f, -5.5f), 3),
                    (new Vector2(-2.5f, -9f), 1), (new Vector2(2.5f, -9f), 1),
                    // 広い谷の中（左は下向きの風、右は上昇気流）と底の近く。
                    (new Vector2(-13.5f, -3f), 1), (new Vector2(13.5f, -3f), 1), (new Vector2(-15.5f, -15f), 20), (new Vector2(15.5f, -15f), 20),
                    // オフィスの上、渡り廊下の上、広場の上、塔の間の上、ヘリポートの先、クレーンの横、塔の屋上の階段室の上。
                    (new Vector2(-23.5f, 8f), 3), (new Vector2(23.5f, 8f), 3), (new Vector2(-25f, 10f), 1), (new Vector2(25f, 10f), 1),
                    (new Vector2(-20f, 17f), 1), (new Vector2(20f, 17f), 1), (new Vector2(-5f, 18f), 1), (new Vector2(5f, 18f), 1),
                    (new Vector2(-35f, 20f), 3), (new Vector2(35f, 20f), 3), (new Vector2(-17f, 26f), 1), (new Vector2(17f, 26f), 1),
                    (new Vector2(-10f, 30f), 3), (new Vector2(10f, 30f), 3), (new Vector2(-44f, 19f), 1), (new Vector2(44f, 19f), 1),
                    // 20 枚: 塔の外の上空、いちばん上。
                    (new Vector2(-50f, 10f), 20), (new Vector2(50f, 10f), 20), (new Vector2(0f, 44f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 41f), new Vector2(-46f, 33f), new Vector2(46f, 33f), new Vector2(-13.5f, -12f), new Vector2(13.5f, -12f),
                    new Vector2(-32f, -12f), new Vector2(32f, -12f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7f), new Vector2(-13.5f, 9.5f), new Vector2(13.5f, 9.5f), new Vector2(-39f, 22f), new Vector2(39f, 22f),
                    new Vector2(-28f, 26.5f), new Vector2(28f, 26.5f), HollowBank,
                },
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-25.5f, 5f, 0f), new Vector3(25.5f, 5f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-17.5f, 13.5f, 0f), new Vector3(17.5f, 13.5f, 0f), new Vector3(-36.25f, 16f, 0f), new Vector3(36.25f, 16f, 0f) },
                Decorate = decor => DecorateCity(decor, night: false),
                // 背景とオブジェクトのクオリティアップ（D-97）。遠くに今の街。
                Look = DayLook(theme),
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
                Music = "CityNight",
                DisplayName = "SKYLINE NIGHT",
                Description = "The same rooftops after dark. Neon signs bounce you, subway vents blast up from the canyons.",
                DescriptionJa = "夜の同じ屋上。ネオン看板に弾かれ、ビルの谷間では地下鉄の通風口が上へ吹き上げる。",
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
                    // ステージの外側（塔の外。飛ばされた人を内側へ弾き返す）と、左右のビルの内側の壁（ユーザーの要望、2026-10-19。左右対称）。
                    new ZoneSpec("NeonEdgeLowWest", StageZoneKind.Bumper, new Vector2(-52f, 8f), new Vector2(1.2f, 3.6f), Vector2.up, 22f),
                    new ZoneSpec("NeonEdgeLowEast", StageZoneKind.Bumper, new Vector2(52f, 8f), new Vector2(1.2f, 3.6f), Vector2.up, 22f),
                    new ZoneSpec("NeonEdgeHighWest", StageZoneKind.Bumper, new Vector2(-52f, 24f), new Vector2(1.2f, 3.6f), Vector2.up, 22f),
                    new ZoneSpec("NeonEdgeHighEast", StageZoneKind.Bumper, new Vector2(52f, 24f), new Vector2(1.2f, 3.6f), Vector2.up, 22f),
                    new ZoneSpec("NeonTowerWallWest", StageZoneKind.Bumper, new Vector2(-34.3f, 11.5f), new Vector2(1f, 3f), Vector2.up, 20f),
                    new ZoneSpec("NeonTowerWallEast", StageZoneKind.Bumper, new Vector2(34.3f, 11.5f), new Vector2(1f, 3f), Vector2.up, 20f),
                    new ZoneSpec("NeonOfficeWallWest", StageZoneKind.Bumper, new Vector2(-17.4f, -2f), new Vector2(1f, 3f), Vector2.up, 20f),
                    new ZoneSpec("NeonOfficeWallEast", StageZoneKind.Bumper, new Vector2(17.4f, -2f), new Vector2(1f, 3f), Vector2.up, 20f),
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
                    // くりぬいた中の床の上。
                    (new Vector2(-5f, -8f), 1), (new Vector2(5f, -8f), 1),
                    // 追加（総量を 2 倍に。ユーザーの要望、2026-10-21）。くりぬいた中（隠れ家にしやすい所。20 枚 2 つと 3 枚 2 つを固めて、取り合いを激しくする）。
                    (new Vector2(-7.5f, -6f), 20), (new Vector2(7.5f, -6f), 20), (new Vector2(-3f, -5.5f), 3), (new Vector2(3f, -5.5f), 3),
                    (new Vector2(-2.5f, -9f), 1), (new Vector2(2.5f, -9f), 1),
                    // 通風口の谷の中と、オフィスの上。
                    (new Vector2(-16f, -8f), 20), (new Vector2(16f, -8f), 20), (new Vector2(-16f, 4f), 1), (new Vector2(16f, 4f), 1),
                    (new Vector2(-24f, 6.5f), 3), (new Vector2(24f, 6.5f), 3),
                    // 渡り廊下の上、トランポリンの上、塔の間の上、クレーンの横、塔の屋上の階段室の上。
                    (new Vector2(-20f, 17f), 1), (new Vector2(20f, 17f), 1), (new Vector2(-5f, 22f), 1), (new Vector2(5f, 22f), 1),
                    (new Vector2(0f, 24f), 1), (new Vector2(-35f, 20f), 3), (new Vector2(35f, 20f), 3), (new Vector2(-17f, 26f), 1),
                    (new Vector2(17f, 26f), 1), (new Vector2(-10f, 30f), 3), (new Vector2(10f, 30f), 3), (new Vector2(-44f, 19f), 1),
                    (new Vector2(44f, 19f), 1),
                    // 20 枚: 塔の外の上空、いちばん上。
                    (new Vector2(-50f, 10f), 20), (new Vector2(50f, 10f), 20), (new Vector2(0f, 45f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 42f), new Vector2(-46f, 31f), new Vector2(46f, 31f), new Vector2(-13.5f, -18f), new Vector2(13.5f, -18f),
                    new Vector2(-32f, -17f), new Vector2(32f, -17f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7.5f), new Vector2(-21f, 8.5f), new Vector2(21f, 8.5f), new Vector2(-38f, 21f), new Vector2(38f, 21f),
                    new Vector2(-28f, 25f), new Vector2(28f, 25f), HollowBank,
                },
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-22f, 5f, 0f), new Vector3(22f, 5f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-17.5f, 13.5f, 0f), new Vector3(17.5f, 13.5f, 0f), new Vector3(-38.75f, 16f, 0f), new Vector3(38.75f, 16f, 0f) },
                Decorate = decor => DecorateCity(decor, night: true),
                // 夜の雰囲気の確認（D-97。2026-11-17 のユーザーの要望）。
                Look = NightLook(),
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
            // 背景のビルの夜の明かり: ネオン風の色で、光る強さ（ブルームでにじむ）。2026-11-18 の要望（より煌びやかに）。
            Material[] skyLights = night
                ? new[]
                {
                    decor.Unlit("SkyWarm", Glow(new Color(1f, 0.8f, 0.48f), 2.4f)),
                    decor.Unlit("SkyCool", Glow(new Color(0.72f, 0.88f, 1f), 2.2f)),
                    decor.Unlit("SkyPink", Glow(new Color(1f, 0.28f, 0.86f), 2.6f)),
                    decor.Unlit("SkyCyan", Glow(new Color(0.22f, 0.95f, 1f), 2.6f)),
                    decor.Unlit("SkyPurple", Glow(new Color(0.66f, 0.36f, 1f), 2.6f)),
                    decor.Unlit("SkyAmber", Glow(new Color(1f, 0.58f, 0.16f), 2.4f)),
                }
                : null;
            // 夜の道路は濡れたアスファルトのネオンの照り返し（2026-11-18 の要望: 奥の街の光と地続きに。西新宿の道路の地面と同じ照り返し）。
            Material street = night
                ? SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("WetStreet", "Airerage/StageBackdrop", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.07f));
                    m.SetFloat("_HazeTop", -1000f);
                    m.SetFloat("_HazeBottom", -1100f);
                    m.SetFloat("_Blink", 0f);
                    m.SetFloat("_BandStrength", 0f);
                    m.SetFloat("_BandFill", 0f);
                    m.SetFloat("_TexBlend", 0f);
                    m.SetFloat("_LightShade", 0.3f);
                    m.SetFloat("_FogAmount", 1f);
                    m.SetFloat("_WetReflect", CityWetReflect);
                }, "Assets/_Project/Materials/Stages/CityNight")
                : decor.Lit("Street", new Color(0.3f, 0.3f, 0.32f));

            // 地形のビルの奥行き（見た目だけ）と正面の窓。
            (float MinX, float MaxX, float Top)[] buildings =
            {
                (-46f, -35f, 15f), (-29f, -18f, 4f), (-9f, 9f, 0f), (18f, 29f, 4f), (35f, 46f, 15f),
            };
            foreach (var building in buildings)
            {
                float width = building.MaxX - building.MinX;
                // 超高層ビル（2026-11-25 の要望）: 地上の大通り（CityStreet.Ground）まで伸ばす（根元は雲海に隠れる）。
                float height = building.Top - CityStreet.Ground;
                decor.Box("Volume", new Vector3((building.MinX + building.MaxX) * 0.5f, building.Top - height * 0.5f, 7f),
                    new Vector3(width, height, 10f), building.Top > 10f ? glass : facade);
                for (float y = building.Top - 2f; y > -40f; y -= 2.6f)
                {
                    if (building.MinX < 0f && building.MaxX > 0f && y > -11f && y < -3f)
                    {
                        // 真ん中のビルのくりぬいた所には窓を付けない。
                        continue;
                    }
                    Material m = decor.Random.NextDouble() < 0.3 ? windowCool : window;
                    if (night && decor.Random.NextDouble() < 0.35)
                    {
                        // 夜は消えている窓もある。
                        continue;
                    }
                    decor.Quad("Windows", new Vector3((building.MinX + building.MaxX) * 0.5f, y, -2.02f), new Vector2(width - 1.2f, 0.9f), m);
                }
            }
            // くりぬいた中の奥の壁と天井の灯り（中が空いているのが分かるように）。
            decor.Box("HollowBack", new Vector3(0f, -7f, 1.8f), new Vector3(18f, 6f, 0.4f),
                decor.Lit("HollowWall", night ? new Color(0.08f, 0.08f, 0.12f) : new Color(0.32f, 0.33f, 0.36f)));
            for (float x = -6f; x <= 6f; x += 4f)
            {
                decor.Box("HollowLight", new Vector3(x, -4.15f, 0f), new Vector3(2f, 0.15f, 1.2f), decor.Unlit("HollowLamp", new Color(1f, 0.92f, 0.7f)));
            }
            // 地上（遊ぶ屋上の約 110m 下。間は雲海）: ビルの足もとの道路と、その先へまっすぐ延びる大通り（霧で薄れて、遠くの西新宿の
            // 高層ビルの足もとへ続く）。大通りは西新宿の道路の地面（CityStreetsFront から奥。StageDressing）の手前まで。
            decor.Box("Street", new Vector3(0f, CityStreet.Ground, 6f), new Vector3(110f, 0.5f, 30f), street);
            decor.Box("Avenue", new Vector3(0f, CityStreet.Ground - 0.25f, (20f + CityStreetsFront) * 0.5f), new Vector3(1600f, 0.5f, CityStreetsFront - 20f), street);
            // 足場のビルの根元を隠す雲海と、大通りの上の雲の切れ間（2026-11-25 の要望。CityClouds）。
            CityClouds.Build(decor, night);
            // 大通りの車道・歩道・植樹帯・道路照明・横断歩道と信号、行き交う車と人影（2026-11-25 の要望: 電灯を半分にして置き直し、
            // アセットで歩道と車道、歩道に人影、車道にまばらな車。実際の街並みを参考に。CityStreet。docs/15 6.70）。
            CityStreet.Build(decor, night);
            // 大通りの両側の低い街並み（見通しの奥を空っぽに見せない。高層ビルを隠さない低さ）。
            // 2026-11-25: 大通りの手前（z 30 から）にも並べて、通りの両側を建物が縁取る実際の街並みに（低いので遊ぶ画面からの見通しは隠さない）。
            for (float z = 30f; z < 300f; z += decor.Range(40f, 60f))
            {
                for (float x = -280f; x < 280f;)
                {
                    float width = decor.Range(8f, 20f);
                    float middle = x + width * 0.5f;
                    // 大通り（歩道の外の端まで）に掛からない所だけ（前は中心だけを見ていて、幅の広い建物が車道に掛かることがあった）。
                    if (Mathf.Abs(middle) - width * 0.5f > CityStreet.SidewalkOuter + 0.5f)
                    {
                        float height = decor.Range(4f, 14f) + Mathf.Abs(middle) / 280f * 12f;
                        float depth = decor.Range(10f, 22f);
                        decor.Box("Block", new Vector3(middle, CityStreet.Ground + height * 0.5f, z), new Vector3(width, height, depth),
                            decor.Random.NextDouble() < 0.5 ? facade : glass);
                        if (night && decor.Random.NextDouble() < 0.6)
                        {
                            // 夜は正面にまばらな窓の明かり（灰色の塊に見えないように）。
                            decor.Quad("BlockLights", new Vector3(middle, CityStreet.Ground + height * decor.Range(0.35f, 0.8f), z - depth * 0.5f - 0.05f),
                                new Vector2(width * decor.Range(0.3f, 0.75f), 0.8f), SkyLight(decor, skyLights));
                        }
                    }
                    x += width + decor.Range(3f, 9f);
                }
            }

            // 背景のビル群（3 列。奥ほど高く、霧で薄くなる）。真ん中（CityVistaHalfWidth の内側）は奥の 2 列を置かず、手前の列も低くして、
            // 大通りの先の西新宿の高層ビル群（PLATEAU）を見通せるようにする（2026-11-17 の回答: ビル街の背景が変わって見えない）。
            // ビルは Kenney の City Kit Commercial（CC0）のモデル（2026-11-18 の要望: 背景を無料のアセットで精巧に）。夜は窓の色の所だけが光る。
            Color buildingTint = night ? new Color(0.4f, 0.41f, 0.52f) : new Color(0.86f, 0.88f, 0.94f);
            Color windowGlow = night ? Glow(Color.white, 1.4f) : Color.black;
            string emission = night ? ThirdPartyProps.CityCommercialNightEmission : null;
            float[] rows = { 18f, 38f, 70f };
            for (int row = 0; row < rows.Length; row++)
            {
                float z = rows[row];
                for (float x = -110f; x < 110f;)
                {
                    float top = decor.Range(-2f, 22f) + row * 10f;
                    // 高さに合うモデル（高いビルは高層ビルのモデル、低いビルは中くらいのビル）を選び、高さで大きさを決める。
                    bool vista = Mathf.Abs(x + 6f) < CityVistaHalfWidth;
                    if (vista)
                    {
                        if (row > 0)
                        {
                            x += decor.Range(6f, 17f);
                            continue;
                        }
                        top = decor.Range(-14f, -5f);
                    }
                    float height = top + 30f;
                    CityModel model = PickCityModel(decor, height);
                    bool turned = decor.Random.NextDouble() < 0.5;
                    float scale = Mathf.Min(height / model.Height, 15f / Mathf.Max(model.SizeX, model.SizeZ));
                    float width = (turned ? model.SizeZ : model.SizeX) * scale;
                    float depth = (turned ? model.SizeX : model.SizeZ) * scale;
                    float centerX = x + width * 0.5f;
                    if (Mathf.Abs(centerX) - width * 0.5f < CityStreet.SidewalkOuter + 0.5f && z - depth * 0.5f < CityStreet.FarZ)
                    {
                        // 大通り（車道と歩道）の上には建てない（2026-11-25。通りを見通せるように）。
                        x += width + decor.Range(1f, 5f);
                        continue;
                    }
                    if (vista && Mathf.Abs(centerX) >= CityVistaHalfWidth && row > 0)
                    {
                        x += width + decor.Range(1f, 5f);
                        continue;
                    }
                    float realTop = -30f + model.Height * scale;
                    ThirdPartyProps.Place(decor, ThirdPartyProps.CityCommercial, model.Name, new Vector3(centerX, -30f, z), scale,
                        turned ? 270f : 180f, buildingTint, decor.Folder, emission, windowGlow);
                    if (night)
                    {
                        // 窓の明かり（正面。ネオン風の色。前の箱のビルと同じ。モデルの窓の色は屋根と同じ色の所があり光らせられないため）。
                        for (float y = realTop - 3f; y > -10f; y -= decor.Range(2.5f, 4f))
                        {
                            if (decor.Random.NextDouble() < 0.55)
                            {
                                decor.Quad("Lights", new Vector3(centerX, y, z - depth * 0.5f - 0.08f), new Vector2(width * decor.Range(0.3f, 0.8f), 0.7f),
                                    SkyLight(decor, skyLights));
                            }
                        }
                    }
                    if (night && decor.Random.NextDouble() < 0.4)
                    {
                        // 屋上の赤い航空障害灯。
                        decor.Sphere("BeaconLight", new Vector3(centerX, realTop + 0.4f, z), new Vector3(0.6f, 0.6f, 0.6f), decor.Unlit("Beacon", new Color(1f, 0.2f, 0.15f)));
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
            }
        }

        /// <summary>Kenney の City Kit Commercial のビルのモデル（名前と、元の大きさの幅・高さ）。</summary>
        private readonly struct CityModel
        {
            public readonly string Name;
            public readonly float SizeX;
            public readonly float Height;
            public readonly float SizeZ;

            public CityModel(string name, float sizeX, float height, float sizeZ)
            {
                Name = name;
                SizeX = sizeX;
                Height = height;
                SizeZ = sizeZ;
            }
        }

        // 高層ビル（細長い）と中くらいのビル。大きさ（x・高さ・z）は OBJ の頂点から測った値。
        private static readonly CityModel[] TallCityModels =
        {
            new CityModel("building-skyscraper-a", 1.36f, 2.88f, 1.36f), new CityModel("building-skyscraper-b", 1.36f, 4.48f, 1.36f),
            new CityModel("building-skyscraper-c", 1.28f, 4.08f, 1.39f), new CityModel("building-skyscraper-d", 1.28f, 5.47f, 1.39f),
            new CityModel("building-skyscraper-e", 1.29f, 4.08f, 1.24f), new CityModel("building-m", 1.24f, 3.15f, 1.24f),
            new CityModel("building-l", 1.37f, 2.27f, 1.4f),
        };

        private static readonly CityModel[] MidCityModels =
        {
            new CityModel("building-a", 0.88f, 1.29f, 0.94f), new CityModel("building-b", 0.97f, 1.29f, 0.94f), new CityModel("building-d", 0.84f, 1.29f, 0.9f),
            new CityModel("building-f", 0.84f, 1.69f, 1.03f), new CityModel("building-g", 0.97f, 1.69f, 0.92f), new CityModel("building-h", 0.88f, 1.29f, 1.01f),
            new CityModel("building-i", 1.24f, 1.68f, 1.3f), new CityModel("building-j", 2.08f, 1.69f, 1.34f), new CityModel("building-k", 2.08f, 1.47f, 0.94f),
            new CityModel("building-n", 2.32f, 2.48f, 1.82f),
        };

        // 高さ（m）に合うモデルを選ぶ。低いビルは中くらいのビルを大きくすると窓が大きくなりすぎるので、高さ 30m 以下だけに使う。
        private static CityModel PickCityModel(StageDecor decor, float height)
        {
            CityModel[] models = height > 30f ? TallCityModels : MidCityModels;
            return models[decor.Random.Next(models.Length)];
        }

        // 明るさを掛けた光る色（不透明）。
        private static Color Glow(Color color, float intensity)
        {
            return new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1f);
        }

        // 背景の夜の明かりの色を選ぶ（暖かい白が多め、ネオンの色を混ぜる）。
        private static Material SkyLight(StageDecor decor, Material[] lights)
        {
            double pick = decor.Random.NextDouble();
            return pick < 0.32 ? lights[0] : pick < 0.44 ? lights[1] : pick < 0.6 ? lights[2] : pick < 0.76 ? lights[3] : pick < 0.9 ? lights[4] : lights[5];
        }

        // 背景のビル群を置かない真ん中の幅（x の絶対値。両側のビルの列のあいだから大通りの先を見通す）。
        private const float CityVistaHalfWidth = 50f;

        // 西新宿（PLATEAU）の地面の高さ: 谷の底の道路（-24）より下に沈め、大通りの地平の上に高層ビルの上の方だけが並んで見える高さ。
        // 2026-11-18 の要望（背景の下側で建物がぶつ切り）で、道路の地面（谷の底の大通りと同じ高さ）に立たせた。大きさは少し小さく、
        // 少し奥へ（道路に立つと高層ビルの頭が画面の上からはみ出さないように）。地面は大通りの奥の端 CityStreetsFront から。
        // 2026-11-25: 地上を遊ぶ屋上の約 110m 下へ下げたのに合わせて、西新宿も同じ地上に立たせ、大きさを実物に近づけた（高層ビルの頭が
        // 雲海の上に突き出し、遊ぶ屋上と同じくらいの高さに並ぶ）。
        private const float CityPlateauGround = CityStreet.Ground;

        // 夜の濡れた道路のネオンの照り返しの強さ（大通りと西新宿の道路の地面で同じ）。
        private const float CityWetReflect = 1.1f;
        private const float CityPlateauScale = 0.9f;
        private const float CityPlateauPush = 80f;
        private const float CityStreetsFront = 380f;
        // 手前の霧の濃い大通り（霧の割合 1）から、西新宿の霧の割合へなじませる範囲（ワールドの z）。
        private const float CityFogFadeEnd = 470f;

        // 昼のビル街で、西新宿に掛ける霧の割合（谷の近くのもやより薄く、遠くの霞として少し残す）。
        private const float CityDayPlateauFog = 0.35f;

        private static StageLook DayLook(StageTheme theme)
        {
            StageLook look = StageLook.FromTheme("CityDay", theme, StageMood.Day);
            look.Skyline = true;
            PlaceTokyo(look);
            // 谷のもや（霧 60〜170m）のままだと、450m 先の西新宿は霧の色の影になる（2026-11-18 の指摘: ほぼシルエット）。
            look.CityFogAmount = CityDayPlateauFog;
            // 2026-11-25: 遊ぶ屋上の下は雲海になり、谷の底のもやは要らない。遥か下の大通りと雲の切れ間が霞に消えすぎないように、霧を遠くへ
            // （高い所から見下ろす空気遠近法は残す）。
            look.FogStart = 140f;
            look.FogEnd = 800f;
            return look;
        }

        private static StageLook NightLook()
        {
            StageLook look = StageLook.CityNight();
            PlaceTokyo(look);
            // 夜の都会の空気感（2026-11-18 の要望: ネオンをもっと発光させてぼかす）。明かりを強く、ブルームを広くにじませ、街明かりのもやを
            // 重ねる（恐怖の館・宇宙ステーションも夜の台本を土台にするので、ここでビル街の夜だけ変える）。
            look.CityNightGlow = 5.5f;
            look.WindowGlow = 4.5f;
            look.WetReflect = CityWetReflect;
            look.BloomThreshold = 0.8f;
            look.BloomIntensity = 1.25f;
            look.BloomScatter = 0.86f;
            look.CityGlowColor = new Color(0.55f, 0.16f, 0.42f);
            return look;
        }

        // 西新宿を道路の地面の上に、少し小さく奥へ置く（昼夜共通）。
        private static void PlaceTokyo(StageLook look)
        {
            look.PlateauGround = CityPlateauGround;
            look.PlateauScale = CityPlateauScale;
            look.PlateauPush = CityPlateauPush;
            look.PlateauStreets = true;
            look.PlateauStreetsFront = CityStreetsFront;
            look.CityFogFadeStart = CityStreetsFront;
            look.CityFogFadeEnd = CityFogFadeEnd;
        }
    }
}
