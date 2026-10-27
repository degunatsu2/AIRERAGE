using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 埠頭（FROZEN WHARF。ユーザーの案、2026-10-26）。左右の岸壁（薄い桟橋で、下は海が続く）と、間の海に浮かぶタグボート、岸壁をまたぐ
    /// ガントリークレーン（上の梁の真ん中に穴があり、その上にアンカー）。奥の海の向こうに、氷山のステージ（FROST COAST）の氷山と船が
    /// 見える。海は水の判定（5 秒浮いていられる）。岸壁の下の海には、休める灰色のコンテナが左右に 1 つずつ浮かぶ（ワイヤーはつながらない）。
    /// 右の外と真ん中を仕切るクレーンの脚の両側には、脚の下から上まで定期的に速く動く、アンカー付きのボラードがある（つないだまま運ばれる。
    /// ユーザーの要望、2026-10-26）。
    /// 固有のギミック（ユーザーの案）: ボラード（岸壁の係船柱。アンカーポイント）、赤いコンテナ（中がくりぬかれていて左右の両側から入れる。
    /// 屋根と床に乗れて、ワイヤーがつながる。20 秒ごとに次の場所へ運ばれる。奥の岸壁を走る大きなフォークリフトが運んでいるように見せる）。
    /// 追加の案（残したもの）: フォークリフトの荷台（上下する動く足場）、ベルトコンベア、漁網（バンパー）、灯台、積み上げたコンテナ、
    /// 倉庫の屋根、加速リング。消したもの（ユーザーの要望、2026-10-26）: 波と汽笛（風）。
    /// </summary>
    internal static class WharfStage
    {
        // クレーンのコンテナ: 長さ 10m・高さ 3m（屋根と床は厚さ 0.35m。中は 2.3m）。
        private const float ContainerLength = 10f;
        private const float ContainerHeight = 3f;
        private const float PlateThickness = 0.35f;

        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.6f, 0.62f, 0.64f),
                Attachable = new Color(0.3f, 0.45f, 0.6f),
                Anchor = new Color(1f, 0.85f, 0.25f),
                SkyTint = new Color(0.6f, 0.72f, 0.9f),
                Ground = new Color(0.45f, 0.5f, 0.58f),
                Exposure = 1.3f,
                AtmosphereThickness = 0.85f,
                Light = new Color(1f, 0.97f, 0.92f),
                LightIntensity = 1.2f,
                LightEuler = new Vector3(40f, 30f, 0f),
                AmbientSky = new Color(0.65f, 0.74f, 0.88f),
                AmbientEquator = new Color(0.55f, 0.6f, 0.68f),
                AmbientGround = new Color(0.35f, 0.4f, 0.46f),
                FogColor = new Color(0.78f, 0.85f, 0.93f),
                FogStart = 60f,
                FogEnd = 220f,
            };
            theme.ZoneColors[StageZoneKind.Water] = new Color(0.12f, 0.36f, 0.58f);
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(1f, 0.75f, 0.2f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.45f, 0.85f, 0.55f);
            var containerRed = new Color(0.8f, 0.14f, 0.1f);
            // コンテナの道のり（中心）: 左の海の上 A → 高い真ん中 C → 右の海の上 B → A。それぞれ 15 秒止まり、5 秒で運ばれる（20 秒ごと）。
            var spotA = new Vector2(-12f, 6f);
            var spotC = new Vector2(0f, 18f);
            var spotB = new Vector2(12f, 6f);
            float plateOffset = ContainerHeight * 0.5f - PlateThickness * 0.5f;

            return new StageLayout
            {
                Id = "Wharf",
                DisplayName = "FROZEN WHARF",
                Description = "Bollards to hook onto, a hollow red container carried every 20 seconds, a cold sea under the piers you can only float in for a while.",
                DescriptionJa = "ワイヤーをつなげるボラードと、20秒ごとに運ばれる中が空洞の赤いコンテナ。桟橋の下の冷たい海には、少しの間しか浮いていられない。",
                Color = new Color(0.95f, 0.35f, 0.25f),
                Terrain = new[]
                {
                    // 岸壁（左右。下は海が続く薄い桟橋。海を泳いで、外と真ん中を行き来できる。ユーザーの要望、2026-10-26）。
                    new TerrainBox("QuayWest", -48f, -0.6f, -20f, 1f),
                    new TerrainBox("QuayEast", 20f, -0.6f, 48f, 1f),
                    // 岸壁の下の海に浮かぶ灰色のコンテナ（左右に 1 つずつ。休める。ワイヤーはつながらない。ユーザーの要望、2026-10-26）。
                    new TerrainBox("FloatWest", -37f, -6.4f, -31f, -3.6f),
                    new TerrainBox("FloatEast", 31f, -6.4f, 37f, -3.6f),
                    // タグボート（船体と操舵室とマストはワイヤーがつながる）。
                    new TerrainBox("TugHull", -6f, -7f, 6f, -3f, attachable: true),
                    new TerrainBox("TugCabin", -2f, -3f, 2.5f, 0f, attachable: true),
                    new TerrainBox("TugMast", 0.8f, 0f, 1.2f, 6f, attachable: true),
                    // 灯台。
                    new TerrainBox("Lighthouse", -48f, 1f, -46f, 22f, attachable: true),
                    new TerrainBox("LighthouseDeck", -49f, 22f, -45f, 22.6f, attachable: true),
                    // 積み上げたコンテナ（鉄。ワイヤーがつながる）。
                    new TerrainBox("StackWestLow", -42f, 1f, -36f, 3.6f, attachable: true),
                    new TerrainBox("StackWestHigh", -42f, 3.6f, -36f, 6.2f, attachable: true),
                    new TerrainBox("StackEastLow", 36f, 1f, 42f, 3.6f, attachable: true),
                    new TerrainBox("StackEastHigh", 36f, 3.6f, 42f, 6.2f, attachable: true),
                    // ガントリークレーン（脚と上の梁。梁の真ん中に穴があり、外へ抜けられる。ユーザーの要望、2026-10-26）。
                    new TerrainBox("CraneLegWest", -26f, 1f, -25f, 30f, attachable: true),
                    new TerrainBox("CraneLegEast", 25f, 1f, 26f, 30f, attachable: true),
                    new TerrainBox("CraneBeamWest", -27f, 30f, -3f, 31.4f, attachable: true),
                    new TerrainBox("CraneBeamEast", 3f, 30f, 27f, 31.4f, attachable: true),
                    // 倉庫の屋根。
                    new TerrainBox("WarehouseRoof", 38f, 12f, 48f, 12.6f, attachable: true),
                },
                // ボラード（岸壁の端の係船柱。ユーザーの案。右の岸壁の右端のものは、端ぎりぎりに置き、右の海から浮いたままつなげて
                // 岸壁へ上がれる。ユーザーの要望、2026-10-27）と、タグボートのボラード、クレーンの梁の下の鉤、灯台の上、梁の穴の上、左右の高い所の
                // 赤いコインの下（積み上げたコンテナ・倉庫の屋根から上へ行き来する。ユーザーの要望、2026-10-26）。右の岸壁のクレーンの脚の横の
                // ボラードは、脚に沿って動く（Platforms の BollardOuter）。
                Anchors = new[]
                {
                    new Vector2(-21f, 1.9f), new Vector2(-44f, 1.9f), new Vector2(21f, 1.9f), new Vector2(EastEdgeBollardX, 1.9f), new Vector2(-5f, -2.1f),
                    new Vector2(5f, -2.1f), new Vector2(-27.5f, 1.9f), new Vector2(-14f, 26f), new Vector2(14f, 26f),
                    new Vector2(-47f, 27f), new Vector2(0f, 37f), new Vector2(-37f, 17f), new Vector2(37f, 17f),
                },
                // 海の底が撃破の線。
                BoundsMin = new Vector2(-60f, -12f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 海（水）: 5 秒浮いていられる。24 m/s 以上で入ると底（撃破の線）まで沈む。
                    new ZoneSpec("Sea", StageZoneKind.Water, new Vector2(0f, -8.5f), new Vector2(120f, 7f), Vector2.zero, 5f, 24f),
                    // ベルトコンベア（右の岸壁。クレーンの脚の方へ）。
                    ZoneSpec.OnFloor("Conveyor", StageZoneKind.SpeedLane, 27f, 35f, 1f, Vector2.left, 10f),
                    // 漁網（バンパー）。
                    new ZoneSpec("NetWest", StageZoneKind.Bumper, new Vector2(-18f, 14f), new Vector2(3f, 1f), Vector2.up, 16f),
                    new ZoneSpec("NetEast", StageZoneKind.Bumper, new Vector2(18f, 14f), new Vector2(3f, 1f), Vector2.up, 16f),
                    // 岸壁のジャンプ台。
                    ZoneSpec.OnFloor("QuayPadWest", StageZoneKind.JumpPad, -24.5f, -22.5f, 1f, Vector2.up, 26f, 0.4f),
                    // 右は、クレーンの脚の横の動くボラードを避けて、少し左（ユーザーの要望、2026-10-26）。
                    ZoneSpec.OnFloor("QuayPadEast", StageZoneKind.JumpPad, 22f, 23.8f, 1f, Vector2.up, 26f, 0.4f),
                    // 海の上の加速リング（反対の岸壁へ）。
                    new ZoneSpec("RingWest", StageZoneKind.BoostRing, new Vector2(-8f, 12f), new Vector2(2.4f, 3f), new Vector2(1f, 0.1f), 18f),
                    new ZoneSpec("RingEast", StageZoneKind.BoostRing, new Vector2(8f, 12f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.1f), 18f),
                },
                Platforms = new[]
                {
                    // クレーンのコンテナ（ユーザーの案）: 屋根と床の 2 枚が一緒に運ばれる（左右は開いていて、両側から入れる）。
                    ContainerPlate("ContainerRoof", spotA + new Vector2(0f, plateOffset), spotA, spotC, spotB, containerRed, roof: true),
                    ContainerPlate("ContainerFloor", spotA - new Vector2(0f, plateOffset), spotA, spotC, spotB, containerRed, roof: false),
                    // フォークリフトの荷台: 左の岸壁で、灯台のてっぺんより少し上（上面 24.5m）まで上下する（14 秒で 1 往復。ユーザーの要望、
                    // 2026-10-26）。
                    new PlatformSpec("ForkliftPallet", new Vector2(-31f, 1.25f), new Vector2(3.2f, 0.5f), true)
                        .Path(14f, (0f, Vector2.zero), (1.5f, Vector2.zero), (6.5f, new Vector2(0f, PalletRise)), (8.5f, new Vector2(0f, PalletRise)),
                            (13.5f, Vector2.zero))
                        .Eased().Tinted(new Color(0.85f, 0.65f, 0.3f)),
                    // クレーンの右の脚の両側のボラード（ユーザーの要望、2026-10-26）: 9 秒ごとに、岸壁の上で 3.3 秒待ち、1.2 秒で上へ、上で
                    // 3.3 秒待ち、1.2 秒で下へ。内側と外側は半分ずらす（片方が上なら、もう片方は下）。中心がアンカー（ワイヤーはつないだまま
                    // 運ばれる）。内側は梁の下（乗っても頭がぶつからない高さ）まで、外側は梁の上面の高さまで上がる。
                    Bollard("BollardInner", new Vector2(24.45f, 1.46f), 27.6f, 0f),
                    Bollard("BollardOuter", new Vector2(27.5f, 1.46f), 30.95f, 4.5f),
                },
                Coins = new (Vector2, int)[]
                {
                    // 岸壁の上。
                    (new Vector2(-44f, 3.5f), 1), (new Vector2(-34f, 3f), 1), (new Vector2(-28f, 3f), 1), (new Vector2(-21.5f, 4f), 1),
                    (new Vector2(21.5f, 4f), 1), (new Vector2(28f, 3f), 1), (new Vector2(31f, 3f), 1), (new Vector2(34f, 3f), 1), (new Vector2(44f, 3.5f), 1),
                    // 積み上げたコンテナの上とフォークリフトの荷台の道。
                    (new Vector2(-39f, 7.2f), 3), (new Vector2(39f, 7.2f), 3), (new Vector2(-31f, 9f), 1), (new Vector2(-31f, 12f), 3),
                    (new Vector2(-31f, 16f), 1),
                    // コンテナの止まる場所（中に入ったコイン）と、運ばれる道。
                    (new Vector2(-12f, 6f), 20), (new Vector2(12f, 6f), 20), (new Vector2(0f, 18f), 20), (new Vector2(-15f, 9f), 1), (new Vector2(15f, 9f), 1),
                    (new Vector2(-6f, 13f), 1), (new Vector2(6f, 13f), 1), (new Vector2(0f, 21.5f), 3),
                    // タグボートの上と周り。
                    (new Vector2(-4f, -1.5f), 1), (new Vector2(4.5f, -1.5f), 1), (new Vector2(0f, 2f), 1), (new Vector2(1f, 8f), 3),
                    // 海の中（潜る）と海の上。
                    (new Vector2(-13f, -9f), 20), (new Vector2(13f, -9f), 20), (new Vector2(-13f, -2f), 1), (new Vector2(13f, -2f), 1),
                    (new Vector2(-17f, -3.5f), 3), (new Vector2(17f, -3.5f), 3),
                    // 岸壁の下（浮かぶコンテナの上と、その左右の海の上）。
                    (new Vector2(-34f, -2.4f), 3), (new Vector2(34f, -2.4f), 3), (new Vector2(-27.5f, -4f), 1), (new Vector2(27.5f, -4f), 1),
                    (new Vector2(-41f, -4f), 1), (new Vector2(41f, -4f), 1),
                    // 漁網の周りと加速リング。
                    (new Vector2(-18f, 16.5f), 1), (new Vector2(18f, 16.5f), 1), (new Vector2(-11f, 12f), 1), (new Vector2(11f, 12f), 1),
                    // クレーンの脚と梁。
                    (new Vector2(-24f, 12f), 1), (new Vector2(24f, 12f), 1), (new Vector2(-24f, 22f), 1), (new Vector2(24f, 22f), 1),
                    (new Vector2(-18f, 33f), 1), (new Vector2(-6f, 33f), 1), (new Vector2(6f, 33f), 1), (new Vector2(18f, 33f), 1), (new Vector2(0f, 27.5f), 20),
                    (new Vector2(-14f, 28.5f), 3), (new Vector2(14f, 28.5f), 3),
                    // 灯台と倉庫の屋根。
                    (new Vector2(-47f, 24.5f), 3), (new Vector2(-45f, 14f), 1), (new Vector2(-43f, 20f), 1), (new Vector2(44f, 14.5f), 3),
                    (new Vector2(40f, 14.5f), 1), (new Vector2(47f, 17f), 1),
                    // 高い所とステージの端。
                    (new Vector2(-34f, 30f), 20), (new Vector2(34f, 30f), 20), (new Vector2(-53f, 10f), 20), (new Vector2(53f, 10f), 20),
                    (new Vector2(-53f, 24f), 3), (new Vector2(53f, 24f), 3), (new Vector2(-30f, 20f), 20), (new Vector2(30f, 20f), 20),
                    (new Vector2(-40f, 22f), 20), (new Vector2(40f, 22f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 40f), new Vector2(-47f, 32f), new Vector2(47f, 32f), new Vector2(-13f, -10f), new Vector2(13f, -10f),
                    new Vector2(0f, 14f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 4.5f), new Vector2(-39f, 9.5f), new Vector2(39f, 9.5f), new Vector2(-46.5f, 27.5f), new Vector2(43f, 17f),
                    new Vector2(0f, 33.5f), new Vector2(-24f, 17f), new Vector2(24f, 17f),
                },
                Spawns = new[] { new Vector3(-21.5f, 2f, 0f), new Vector3(21.5f, 2f, 0f), new Vector3(-44f, 2f, 0f), new Vector3(44f, 2f, 0f) },
                Decorate = Decorate,
            };
        }

        // 右の岸壁（右端 x = 48）の右端のボラードの位置（端ぎりぎり。柱の右の縁が岸壁の端から 0.2m 出る。右の海から、浮いたまま
        // 岸壁の角に遮られずにつなげる）。
        private const float EastEdgeBollardX = 47.85f;

        // フォークリフトの荷台の上がる高さ（m。上面が灯台のてっぺん 22.6m より少し上の 24.5m）。
        private const float PalletRise = 23f;

        // クレーンの脚の横の動くボラード（下の中心 bottom から、top の高さまで。ずれ phase 秒）。
        private static PlatformSpec Bollard(string name, Vector2 bottom, float top, float phase)
        {
            var up = new Vector2(0f, top - bottom.y);
            return new PlatformSpec(name, bottom, new Vector2(0.9f, 0.9f), true)
                .Path(9f, (0f, Vector2.zero), (3.3f, Vector2.zero), (4.5f, up), (7.8f, up), (9f, Vector2.zero))
                .Eased().Phase(phase).Anchor()
                .Looks((decor, visual, spec) =>
                {
                    // 黒い係船柱と、手前の菱形のアンカー。
                    decor.Cylinder("Post", visual, Vector3.zero, new Vector3(0.8f, 0.45f, 0.8f), decor.Lit("Bollard", new Color(0.15f, 0.15f, 0.17f)));
                    GameObject diamond = decor.Box("Anchor", visual, new Vector3(0f, 0f, -0.6f), new Vector3(0.6f, 0.6f, 0.3f),
                        decor.Unlit("MovingAnchor", new Color(1f, 0.85f, 0.25f)));
                    diamond.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                });
        }

        // クレーンのコンテナの屋根か床（道のりは止まる場所の中心から、板の分だけずらす）。
        private static PlatformSpec ContainerPlate(string name, Vector2 center, Vector2 a, Vector2 c, Vector2 b, Color color, bool roof)
        {
            return new PlatformSpec(name, center, new Vector2(ContainerLength, PlateThickness), true)
                .Path(60f, (0f, Vector2.zero), (15f, Vector2.zero), (20f, c - a), (35f, c - a), (40f, b - a), (55f, b - a), (60f, Vector2.zero))
                .Eased()
                .Looks((decor, visual, spec) =>
                {
                    Material red = decor.Lit("ContainerRed", color);
                    decor.Box("Plate", visual, Vector3.zero, new Vector3(spec.Size.x, spec.Size.y, 4f), red);
                    if (!roof)
                    {
                        // 奥の壁と、波打った側面の筋（中がくりぬかれているのが見えるように、手前の壁はない）。
                        decor.Box("BackWall", visual, new Vector3(0f, ContainerHeight * 0.5f - PlateThickness * 0.5f, 1.85f),
                            new Vector3(spec.Size.x, ContainerHeight, 0.3f), red);
                        Material rib = decor.Lit("ContainerRib", color * 0.8f);
                        for (int i = 0; i < 8; i++)
                        {
                            decor.Box("Rib", visual, new Vector3(-4.4f + i * 1.25f, ContainerHeight * 0.5f - PlateThickness * 0.5f, 1.65f),
                                new Vector3(0.15f, ContainerHeight - 0.2f, 0.1f), rib);
                        }
                    }
                });
        }

        // 奥の海（遠くの氷山と浮かぶ氷と船。FROST COAST）、ボラード、灯台の光、倉庫、岸壁の奥行き、遠くの雪山。
        private static void Decorate(StageDecor decor)
        {
            Material sea = decor.Lit("Sea", new Color(0.12f, 0.3f, 0.48f));
            Material ice = decor.Lit("Ice", new Color(0.7f, 0.88f, 0.96f));
            Material snow = decor.Lit("Snow", new Color(0.93f, 0.95f, 0.98f));
            Material concrete = decor.Lit("Concrete", new Color(0.5f, 0.52f, 0.55f));
            Material bollard = decor.Lit("Bollard", new Color(0.15f, 0.15f, 0.17f));
            Material beam = decor.Unlit("LighthouseBeam", new Color(1f, 0.95f, 0.7f));
            Material wood = decor.Lit("ShipWood", new Color(0.45f, 0.3f, 0.2f));
            Material sail = decor.Lit("Sail", new Color(0.92f, 0.9f, 0.85f));
            Material warehouse = decor.Lit("Warehouse", new Color(0.55f, 0.6f, 0.68f));
            Material stripe = decor.Unlit("LighthouseStripe", new Color(0.85f, 0.15f, 0.12f));

            // 奥の海（遊ぶ面より奥だけ）。
            decor.Box("Sea", new Vector3(0f, -22f, 70f), new Vector3(220f, 34f, 136f), sea);
            // 遠くの氷山のステージ（FROST COAST）: 浮かぶ氷、氷山、船。
            decor.Box("FarFloeA", new Vector3(-30f, -4.5f, 95f), new Vector3(10f, 3f, 8f), ice);
            decor.Box("FarFloeB", new Vector3(-10f, -4.2f, 100f), new Vector3(14f, 3.6f, 9f), ice);
            decor.Box("FarIceberg", new Vector3(10f, 2f, 98f), new Vector3(10f, 16f, 10f), ice);
            decor.Box("FarIcebergPeak", new Vector3(12f, 12f, 98f), new Vector3(5f, 6f, 6f), snow);
            decor.Box("FarFloeC", new Vector3(26f, -3.5f, 96f), new Vector3(8f, 5f, 7f), ice);
            decor.Box("FarShipHull", new Vector3(42f, -2f, 100f), new Vector3(14f, 4f, 6f), wood);
            decor.Box("FarShipMast", new Vector3(42f, 6f, 100f), new Vector3(0.6f, 12f, 0.6f), wood);
            decor.Quad("FarShipSail", new Vector3(42f, 7f, 99.6f), new Vector2(7f, 8f), sail);
            for (int i = 0; i < 6; i++)
            {
                float size = decor.Range(40f, 70f);
                GameObject mountain = decor.Box("Mountain", new Vector3(-120f + i * 50f + decor.Range(-8f, 8f), -15f, 150f + decor.Range(0f, 30f)),
                    new Vector3(size, size, 20f), snow);
                mountain.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }
            // 岸壁の奥行きとボラード。
            // 桟橋の奥行き（遊ぶ面と同じ薄さ。下は海が続いて見えるように）。
            decor.Box("QuayWestBack", new Vector3(-34f, 0.2f, 6f), new Vector3(28f, 1.6f, 8f), concrete);
            decor.Box("QuayEastBack", new Vector3(34f, 0.2f, 6f), new Vector3(28f, 1.6f, 8f), concrete);
            foreach (float x in new[] { -21f, -44f, -27.5f, 21f, EastEdgeBollardX })
            {
                decor.Cylinder("Bollard", new Vector3(x, 1.45f, 0f), new Vector3(0.7f, 0.45f, 0.7f), bollard);
            }
            // 動くボラードの溝（クレーンの右の脚の両側。見た目だけ）と、フォークリフトの荷台の柱（奥）。
            Material rail = decor.Lit("BollardRail", new Color(0.25f, 0.27f, 0.3f));
            foreach (float x in new[] { 24.45f, 27.5f })
            {
                decor.Box("BollardRail", new Vector3(x, 16.2f, 1.2f), new Vector3(0.3f, 30f, 0.3f), rail);
            }
            foreach (float x in new[] { -32.4f, -29.6f })
            {
                decor.Box("PalletMast", new Vector3(x, 13.2f, 2.4f), new Vector3(0.4f, 24.2f, 0.4f), rail);
            }
            foreach (float x in new[] { -5f, 5f })
            {
                decor.Cylinder("TugBollard", new Vector3(x, -2.55f, 0f), new Vector3(0.5f, 0.45f, 0.5f), bollard);
            }
            // 灯台の縞と光。
            for (int i = 0; i < 4; i++)
            {
                decor.Box("LighthouseStripe", new Vector3(-47f, 4f + i * 5f, 0f), new Vector3(2.05f, 1.2f, 4.05f), stripe);
            }
            GameObject ray = decor.Quad("LighthouseBeam", new Vector3(-30f, 24f, 4f), new Vector2(34f, 1.2f), beam);
            ray.transform.rotation = Quaternion.Euler(0f, 0f, 6f);
            // 倉庫の壁（屋根の下。見た目だけ）。
            decor.Box("Warehouse", new Vector3(43f, 6.6f, 3f), new Vector3(10f, 10.8f, 4f), warehouse);
            // 桟橋の下の杭（奥だけ。泳いで通る所には立てない）。
            Material piling = decor.Lit("Piling", new Color(0.35f, 0.33f, 0.32f));
            foreach (float x in new[] { -46f, -35f, -23f, 23f, 35f, 46f })
            {
                decor.Cylinder("Piling", new Vector3(x, -6f, 4f), new Vector3(0.8f, 5.6f, 0.8f), piling);
            }
            // 赤いコンテナを運ぶ大きなフォークリフト（奥の岸壁の上を走り、マストを伸ばして、爪でコンテナを下から支える。ユーザーの要望、2026-10-26）。
            Material backPier = concrete;
            decor.Box("BackPier", new Vector3(0f, -1.5f, 11f), new Vector3(52f, 5f, 8f), backPier);
            Material yellow = decor.Lit("ForkliftYellow", new Color(0.95f, 0.72f, 0.12f));
            Material dark = decor.Lit("ForkliftDark", new Color(0.15f, 0.15f, 0.17f));
            Transform target = GameObject.Find("Platforms/ContainerFloor/Visual")?.transform;
            var rig = new GameObject("Forklift");
            rig.transform.SetParent(decor.Root, false);
            var body = new GameObject("Body").transform;
            body.SetParent(rig.transform, false);
            body.position = new Vector3(-12f, 3f, 9f);
            decor.Box("Chassis", body, Vector3.zero, new Vector3(6f, 2.6f, 4.4f), yellow);
            decor.Box("Cab", body, new Vector3(1f, 2.4f, 0.4f), new Vector3(2.6f, 2.4f, 2.6f), dark);
            decor.Box("Counterweight", body, new Vector3(2.4f, 0.3f, 0f), new Vector3(1.4f, 2f, 4.4f), dark);
            foreach (float wx in new[] { -2f, 2f })
            {
                GameObject wheel = decor.Cylinder("Wheel", body, new Vector3(wx, -1.3f, -2.3f), new Vector3(1.6f, 0.3f, 1.6f), dark);
                wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            GameObject mast = decor.Box("Mast", rig.transform, new Vector3(-12f, 4f, 5.4f), new Vector3(1.2f, 1f, 0.8f), dark);
            var carriage = new GameObject("Carriage").transform;
            carriage.SetParent(rig.transform, false);
            carriage.position = new Vector3(-12f, 4.4f, 3.4f);
            decor.Box("Plate", carriage, new Vector3(0f, 0.3f, 1.8f), new Vector3(3.2f, 1f, 0.3f), dark);
            foreach (float fx in new[] { -3f, 3f })
            {
                decor.Box("Fork", carriage, new Vector3(fx, -0.08f, 0f), new Vector3(0.4f, 0.16f, 4f), yellow);
            }
            rig.AddComponent<ForkliftRig>().Configure(target, body, mast.transform, carriage, 1f, 9f, 5.4f, PlateThickness * 0.5f + 0.1f);
        }
    }
}
