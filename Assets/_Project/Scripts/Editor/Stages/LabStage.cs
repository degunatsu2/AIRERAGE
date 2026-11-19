using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 研究所（SECRET LAB。ユーザーの案、2026-10-26）。左右の棟の実験室（中に 20 枚のコインと拠点の候補）と、真ん中のエレベーターの縦穴、
    /// その上の制御甲板。下は底のない谷。
    /// ギミック（ユーザーの要望、2026-10-26）: 扉の上の内側と外側のボタン（ワイヤーをつないでいる間だけ扉が上に開き、離すとゆっくり
    /// 閉じる。扉そのものにはワイヤーがつながらない。閉じる先に人がいると閉じない）、赤外線レーダー（壁と壁をつなぐ半透明の赤い線が
    /// 3 本。触れると、その線の発射装置から、線に沿って高速のミサイルが飛ぶ。威力 60 で、当たるととても吹き飛ぶ。撃つ前に予告）。
    /// 追加の案（残したもの）: エレベーター、気送管（左右の端の管の中だけ強い上向きの風）、ロボットアーム、レーザーの網（縦穴を横切る
    /// 周期のある危険）、ベルトコンベア、粒子加速器のリング、プラズマ球（バンパー）。消したもの（ユーザーの要望）: 転送装置（ワープ）、
    /// 反重力の実験室（低重力）、冷却水のプール（水）、警備のレーザー（ランダムな柱）。
    /// </summary>
    internal static class LabStage
    {
        // 扉: 幅 1m・高さ 4.2m。ボタンにつなぐと 0.45 秒で全開（4.2m 上へ）、離すと 3.6 秒で閉じきる（前の 1/3 の速さ。ユーザーの
        // 要望、2026-10-26）【仮】。
        private const float DoorHeight = 4.2f;
        private const float DoorOpenSeconds = 0.45f;
        private const float DoorCloseSeconds = 3.6f;

        // 赤外線とミサイル: 速さ 55 m/s、威力 60、吹き飛び 42 m/s、触れてから 0.5 秒の予告、飛び終わってから 4 秒で、また撃てる【仮】。
        private const float MissileSpeed = 55f;
        private const int MissilePower = 60;
        private const float MissileKnockback = 42f;
        private const float MissileArmSeconds = 0.5f;
        private const float MissileCooldownSeconds = 4f;

        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.8f, 0.83f, 0.87f),
                Attachable = new Color(0.22f, 0.5f, 0.72f),
                Anchor = new Color(1f, 0.85f, 0.25f),
                SkyTint = new Color(0.3f, 0.36f, 0.45f),
                Ground = new Color(0.12f, 0.14f, 0.18f),
                Exposure = 0.7f,
                AtmosphereThickness = 0.6f,
                Light = new Color(0.92f, 0.97f, 1f),
                LightIntensity = 1.25f,
                LightEuler = new Vector3(55f, -20f, 0f),
                AmbientSky = new Color(0.55f, 0.62f, 0.72f),
                AmbientEquator = new Color(0.42f, 0.46f, 0.52f),
                AmbientGround = new Color(0.2f, 0.24f, 0.28f),
                FogColor = new Color(0.32f, 0.38f, 0.46f),
                FogStart = 40f,
                FogEnd = 130f,
            };
            theme.ZoneColors[StageZoneKind.Gust] = new Color(0.6f, 0.95f, 1f);
            theme.ZoneColors[StageZoneKind.Hazard] = new Color(1f, 0.15f, 0.25f);
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(1f, 0.8f, 0.2f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.7f, 0.4f, 1f);

            return new StageLayout
            {
                Id = "Lab",
                DisplayName = "SECRET LAB",
                Music = "Lab",
                Description = "Hook the button above a door to open it. Don't cross the infrared beams: missiles fly along them. Ride the elevator, shoot up the pneumatic tubes.",
                DescriptionJa = "扉の上のボタンにワイヤーをつなぐと扉が開く。赤外線に触れるとミサイルが飛んでくる。エレベーターに乗り、気送管で上へ飛ばされる。",
                Color = new Color(0.3f, 0.95f, 0.85f),
                Terrain = new[]
                {
                    // 左右の棟の床（下は底のない谷）。
                    new TerrainBox("WingFloorWest", -49f, -2f, -20f, 0f),
                    new TerrainBox("WingFloorEast", 20f, -2f, 49f, 0f),
                    // 実験室（入口は扉だけ。扉は天井の上の細い穴へ上がる。穴の壁にはワイヤーがつながらない。扉の上の内側と外側にボタン）。
                    new TerrainBox("RoomWallWest", -42.5f, 0f, -42f, 7.6f, attachable: true),
                    new TerrainBox("RoomCeilingWest", -42.5f, 7f, -27.4f, 7.6f, attachable: true),
                    new TerrainBox("DoorSlotInnerWest", -27.4f, DoorHeight, -27f, 9f),
                    new TerrainBox("DoorSlotOuterWest", -26f, DoorHeight, -25.6f, 9f),
                    new TerrainBox("DoorSlotCapWest", -27.4f, 9f, -25.6f, 9.6f),
                    new TerrainBox("RoomWallEast", 42f, 0f, 42.5f, 7.6f, attachable: true),
                    new TerrainBox("RoomCeilingEast", 27.4f, 7f, 42.5f, 7.6f, attachable: true),
                    new TerrainBox("DoorSlotInnerEast", 27f, DoorHeight, 27.4f, 9f),
                    new TerrainBox("DoorSlotOuterEast", 25.6f, DoorHeight, 26f, 9f),
                    new TerrainBox("DoorSlotCapEast", 25.6f, 9f, 27.4f, 9.6f),
                    // 気送管（左右の端。中は強い上向きの風。下の右側が入口）。
                    new TerrainBox("TubeOuterWest", -49.4f, 0f, -49f, 30f, attachable: true),
                    new TerrainBox("TubeInnerWest", -45.6f, 2.6f, -45.2f, 30f, attachable: true),
                    new TerrainBox("TubeLedgeWest", -45.2f, 29.4f, -38f, 30f, attachable: true),
                    new TerrainBox("TubeOuterEast", 49f, 0f, 49.4f, 30f, attachable: true),
                    new TerrainBox("TubeInnerEast", 45.2f, 2.6f, 45.6f, 30f, attachable: true),
                    new TerrainBox("TubeLedgeEast", 38f, 29.4f, 45.2f, 30f, attachable: true),
                    // 縦穴の左右のキャットウォーク、実験室の上の換気口、制御甲板（真ん中はエレベーターの穴）。
                    new TerrainBox("CatwalkWest", -19f, 6f, -8f, 6.5f, attachable: true),
                    new TerrainBox("CatwalkEast", 8f, 6f, 19f, 6.5f, attachable: true),
                    new TerrainBox("VentWest", -36f, 16f, -28f, 16.6f, attachable: true),
                    new TerrainBox("VentEast", 28f, 16f, 36f, 16.6f, attachable: true),
                    new TerrainBox("DeckWest", -16f, 22f, -4f, 22.6f, attachable: true),
                    new TerrainBox("DeckEast", 4f, 22f, 16f, 22.6f, attachable: true),
                    // 制御甲板の両端の、赤外線の装置の柱（ワイヤーはつながらない）。
                    new TerrainBox("DeckPostWest", -16.6f, 22f, -16f, 26.5f),
                    new TerrainBox("DeckPostEast", 16f, 22f, 16.6f, 26.5f),
                    // 縦穴の上の輪。
                    new TerrainBox("TopRing", -3f, 33f, 3f, 33.5f, attachable: true),
                },
                Anchors = new[]
                {
                    new Vector2(0f, 43f), new Vector2(-24f, 26f), new Vector2(24f, 26f), new Vector2(-36f, 24f), new Vector2(36f, 24f),
                    new Vector2(-8f, 15.5f), new Vector2(8f, 15.5f), new Vector2(-18f, 34f), new Vector2(18f, 34f),
                },
                // 谷の底が撃破の線。
                BoundsMin = new Vector2(-60f, -12f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 気送管: 管の中だけ、強い上向きの風（上の棚へ）。
                    new ZoneSpec("TubeWest", StageZoneKind.Gust, new Vector2(-47.3f, 15.25f), new Vector2(3.4f, 30.5f), Vector2.up, 75f, 22f),
                    new ZoneSpec("TubeEast", StageZoneKind.Gust, new Vector2(47.3f, 15.25f), new Vector2(3.4f, 30.5f), Vector2.up, 75f, 22f),
                    // キャットウォークのジャンプ台。
                    ZoneSpec.OnFloor("CatwalkPadWest", StageZoneKind.JumpPad, -14.5f, -12.5f, 6.5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("CatwalkPadEast", StageZoneKind.JumpPad, 12.5f, 14.5f, 6.5f, Vector2.up, 24f, 0.4f),
                    // ベルトコンベア: 制御甲板の上を、外の端へ運ぶ。
                    ZoneSpec.OnFloor("ConveyorWest", StageZoneKind.SpeedLane, -16f, -4.5f, 22.6f, Vector2.left, 9f),
                    ZoneSpec.OnFloor("ConveyorEast", StageZoneKind.SpeedLane, 4.5f, 16f, 22.6f, Vector2.right, 9f),
                    // レーザーの網: 縦穴を横切る線が、8 秒ごとに 1.2 秒（1.5 秒前に警告）。当たると威力 10 の枚数のコインを落とし、上へ弾かれる。
                    new ZoneSpec("LaserGrid", StageZoneKind.Hazard, new Vector2(0f, 12f), new Vector2(32f, 0.8f), Vector2.up, 10f, 14f)
                        .Every(8f, 1.2f, 2f, 0f, 1.5f).Column(32f),
                    // 粒子加速器のリング: 換気口から縦穴へ向かって加速する。
                    new ZoneSpec("AcceleratorWest", StageZoneKind.BoostRing, new Vector2(-22f, 19f), new Vector2(2.4f, 3f), new Vector2(1f, 0.2f), 20f),
                    new ZoneSpec("AcceleratorEast", StageZoneKind.BoostRing, new Vector2(22f, 19f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.2f), 20f),
                    // プラズマ球（バンパー）。
                    new ZoneSpec("PlasmaWest", StageZoneKind.Bumper, new Vector2(-12f, 29f), new Vector2(2f, 2f), Vector2.up, 16f),
                    new ZoneSpec("PlasmaEast", StageZoneKind.Bumper, new Vector2(12f, 29f), new Vector2(2f, 2f), Vector2.up, 16f),
                },
                Platforms = new[]
                {
                    // 実験室の扉（ユーザーの要望、2026-10-26）: 扉の上の内側と外側のボタンにワイヤーをつないでいる間だけ開く。
                    PlatformSpec.Door("DoorWest", new Vector2(-26.5f, DoorHeight * 0.5f), new Vector2(1f, DoorHeight), DoorHeight, DoorOpenSeconds,
                        DoorCloseSeconds, 1).Looks(DoorLooks),
                    PlatformSpec.Door("DoorEast", new Vector2(26.5f, DoorHeight * 0.5f), new Vector2(1f, DoorHeight), DoorHeight, DoorOpenSeconds,
                        DoorCloseSeconds, 2).Looks(DoorLooks),
                    PlatformSpec.Button("ButtonWestInside", new Vector2(-27.7f, 5.6f), new Vector2(0.6f, 0.8f), 1),
                    PlatformSpec.Button("ButtonWestOutside", new Vector2(-25.3f, 5.6f), new Vector2(0.6f, 0.8f), 1),
                    PlatformSpec.Button("ButtonEastInside", new Vector2(27.7f, 5.6f), new Vector2(0.6f, 0.8f), 2),
                    PlatformSpec.Button("ButtonEastOutside", new Vector2(25.3f, 5.6f), new Vector2(0.6f, 0.8f), 2),
                    // エレベーター: 下で 3 秒、上（制御甲板の穴をふさぐ）で 3 秒止まる。16 秒で 1 往復。
                    new PlatformSpec("Elevator", new Vector2(0f, 1.7f), new Vector2(8f, 0.6f), true)
                        .Path(16f, (0f, Vector2.zero), (3f, Vector2.zero), (7.5f, new Vector2(0f, 20.6f)), (10.5f, new Vector2(0f, 20.6f)), (15f, Vector2.zero))
                        .Eased().Tinted(new Color(0.3f, 0.34f, 0.4f)),
                    // ロボットアーム: キャットウォークの上を横に行き来する（縦穴には入らない）。
                    new PlatformSpec("ArmWest", new Vector2(-19f, 15f), new Vector2(5f, 0.5f), true)
                        .Path(10f, (0f, Vector2.zero), (4f, new Vector2(11.5f, 0f)), (5f, new Vector2(11.5f, 0f)), (9f, Vector2.zero))
                        .Eased().Tinted(new Color(1f, 0.6f, 0.15f)),
                    new PlatformSpec("ArmEast", new Vector2(19f, 15f), new Vector2(5f, 0.5f), true)
                        .Path(10f, (0f, Vector2.zero), (4f, new Vector2(-11.5f, 0f)), (5f, new Vector2(-11.5f, 0f)), (9f, Vector2.zero))
                        .Eased().Phase(5f).Tinted(new Color(1f, 0.6f, 0.15f)),
                },
                // 赤外線レーダーとミサイル発射装置（ユーザーの要望、2026-10-26）: 実験室の屋根の上（気送管の壁の発射装置から扉の穴の壁まで）を
                // 左右に 1 本ずつと、制御甲板の上（両端の柱の間）に 1 本。屋根の上や甲板の上に立つと触れる。
                Traps = new[]
                {
                    TrapSpec.Tripwire("InfraredWest", new Vector2(-43.6f, 8.4f), new Vector2(-27.4f, 8.4f), 0.3f, MissileSpeed, MissilePower,
                        MissileKnockback, MissileArmSeconds, MissileCooldownSeconds),
                    TrapSpec.Tripwire("InfraredEast", new Vector2(43.6f, 8.4f), new Vector2(27.4f, 8.4f), 0.3f, MissileSpeed, MissilePower,
                        MissileKnockback, MissileArmSeconds, MissileCooldownSeconds),
                    TrapSpec.Tripwire("InfraredDeck", new Vector2(-15.4f, 24.2f), new Vector2(16f, 24.2f), 0.3f, MissileSpeed, MissilePower,
                        MissileKnockback, MissileArmSeconds, MissileCooldownSeconds),
                },
                Coins = new (Vector2, int)[]
                {
                    // 実験室の中（扉を開けないと入れない。20 枚 2 つと 3 枚 2 つ）。
                    (new Vector2(-38.5f, 1.5f), 20), (new Vector2(-30.5f, 1.5f), 20), (new Vector2(-40.5f, 4.5f), 3), (new Vector2(-29.5f, 5f), 3),
                    (new Vector2(-34.5f, 6f), 1),
                    (new Vector2(38.5f, 1.5f), 20), (new Vector2(30.5f, 1.5f), 20), (new Vector2(40.5f, 4.5f), 3), (new Vector2(29.5f, 5f), 3),
                    (new Vector2(34.5f, 6f), 1),
                    // 気送管の中。
                    (new Vector2(-47.3f, 9f), 1), (new Vector2(-47.3f, 18f), 20), (new Vector2(47.3f, 9f), 1), (new Vector2(47.3f, 18f), 20),
                    // 縦穴のいちばん下（エレベーターが下にいる間に取る）と、棟の床の端。
                    (new Vector2(0f, 4f), 3), (new Vector2(-22f, 3f), 1), (new Vector2(22f, 3f), 1),
                    // キャットウォークの上と、レーザーの網のすぐ下。
                    (new Vector2(-17f, 8f), 1), (new Vector2(-10f, 8f), 1), (new Vector2(-13.5f, 10.8f), 1),
                    (new Vector2(17f, 8f), 1), (new Vector2(10f, 8f), 1), (new Vector2(13.5f, 10.8f), 1),
                    // エレベーターの縦穴。
                    (new Vector2(0f, 6.5f), 1), (new Vector2(0f, 9.5f), 1), (new Vector2(0f, 15f), 1), (new Vector2(0f, 18.5f), 1),
                    // 制御甲板の上（赤外線の上と下）。
                    (new Vector2(-12.5f, 23.6f), 1), (new Vector2(-8f, 23.6f), 1), (new Vector2(8f, 23.6f), 1), (new Vector2(12.5f, 23.6f), 1),
                    (new Vector2(-14.5f, 26f), 3), (new Vector2(14.5f, 26f), 3),
                    // 縦穴の上の輪の周り。
                    (new Vector2(-6f, 30.5f), 3), (new Vector2(6f, 30.5f), 3), (new Vector2(-3f, 36.5f), 1), (new Vector2(3f, 36.5f), 1),
                    (new Vector2(0f, 38.5f), 1), (new Vector2(0f, 42f), 20), (new Vector2(0f, 35f), 20),
                    // 実験室の屋根の上（赤外線の上）。
                    (new Vector2(-40f, 9.8f), 1), (new Vector2(-31.5f, 10.2f), 1), (new Vector2(40f, 9.8f), 1), (new Vector2(31.5f, 10.2f), 1),
                    // 換気口の上。
                    (new Vector2(-35f, 18.5f), 1), (new Vector2(-29f, 18.5f), 1), (new Vector2(-32f, 21f), 3),
                    (new Vector2(35f, 18.5f), 1), (new Vector2(29f, 18.5f), 1), (new Vector2(32f, 21f), 3),
                    // 気送管の上の棚と、通路。
                    (new Vector2(-43.5f, 31.6f), 1), (new Vector2(-39.5f, 31.6f), 1), (new Vector2(43.5f, 31.6f), 1), (new Vector2(39.5f, 31.6f), 1),
                    (new Vector2(-43.85f, 4f), 1), (new Vector2(43.85f, 4f), 1),
                    // ロボットアームの道と、加速器のリングの先。
                    (new Vector2(-13f, 17.5f), 1), (new Vector2(13f, 17.5f), 1), (new Vector2(-25.5f, 19.5f), 1), (new Vector2(25.5f, 19.5f), 1),
                    (new Vector2(-13.5f, 19.8f), 20), (new Vector2(13.5f, 19.8f), 20),
                    // 高い所とステージの端。
                    (new Vector2(-20f, 29f), 20), (new Vector2(20f, 29f), 20), (new Vector2(-53f, 12f), 20), (new Vector2(53f, 12f), 20),
                    (new Vector2(-53f, 26f), 3), (new Vector2(53f, 26f), 3),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 40f), new Vector2(-47.3f, 25f), new Vector2(47.3f, 25f), new Vector2(-37f, 5.5f), new Vector2(37f, 5.5f),
                    new Vector2(0f, 27.5f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 30.5f), new Vector2(-34.5f, 3f), new Vector2(34.5f, 3f), new Vector2(-13.5f, 13.5f), new Vector2(13.5f, 13.5f),
                    new Vector2(-41.5f, 34f), new Vector2(41.5f, 34f), new Vector2(0f, 25.5f),
                },
                Spawns = new[] { new Vector3(-23f, 1f, 0f), new Vector3(23f, 1f, 0f), new Vector3(-17f, 7.5f, 0f), new Vector3(17f, 7.5f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-39.75f, 1f, 0f), new Vector3(39.75f, 1f, 0f), new Vector3(-10f, 7.5f, 0f), new Vector3(10f, 7.5f, 0f) },
                Decorate = Decorate,
                // 背景とオブジェクトのクオリティアップ（D-97）。
                Look = StageLook.FromTheme("Lab", theme, StageMood.Indoor),
            };
        }

        // 扉: 灰色の鉄の板と、黄色と黒の縞、黄色のランプ（扉そのものにはワイヤーがつながらない。上のボタンで開く）。
        private static void DoorLooks(StageDecor decor, Transform visual, PlatformSpec spec)
        {
            decor.Box("Door", visual, Vector3.zero, new Vector3(spec.Size.x, spec.Size.y, 3.6f), decor.Lit("LabDoor", new Color(0.42f, 0.46f, 0.52f)));
            Material stripe = decor.Unlit("DoorStripe", new Color(1f, 0.82f, 0.1f));
            for (int i = 0; i < 4; i++)
            {
                GameObject band = decor.Box("Stripe", visual, new Vector3(0f, -spec.Size.y * 0.5f + 0.35f + i * 0.5f, -1.85f), new Vector3(spec.Size.x * 0.9f, 0.16f, 0.05f), stripe);
                band.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            }
            decor.Sphere("Lamp", visual, new Vector3(0f, spec.Size.y * 0.5f - 0.4f, -1.85f), new Vector3(0.3f, 0.3f, 0.1f), decor.Unlit("DoorLamp", new Color(1f, 0.7f, 0.15f)));
        }

        // 研究所の奥の壁、実験室の培養槽、エレベーターの縦穴の枠、気送管のガラス、冷却水の奥、配管とモニター。
        private const string SciFiPack = "Quaternius/SciFi";

        // 壁の区画（柱の間隔 m。Quaternius の板は 4m × 3m なので、この幅に 1 枚を合わせる）と、左右の区画の数、壁のいちばん下。
        private const float Bay = 12f;
        private const int BayHalfCount = 6;
        private const float WallBottom = -19f;

        // 横の段の板（下から）。同じ段は同じ板にそろえる。
        private static readonly string[] Rows =
        {
            "WallBand_Straight", "WallAstra_Straight", "WallAstra_Straight", "WallWindow_Straight",
            "WallAstra_Straight", "WallWindow_Straight", "WallAstra_Straight", "TopCables_Straight",
        };

        /// <summary>
        /// 研究所の奥: SF の壁の板を、柱の格子の区画に 1 枚ずつ・横の段ごとに同じ種類で並べ（腰の帯・板・窓の帯・天井の配線）、
        /// 左右の棟の床を奥へ延ばして、区画の真ん中に計算機の作業台と樽、天井に照明を置く。見た目だけ（当たりはない）。
        /// </summary>
        private static void SciFiInterior(StageDecor decor, Material fallback)
        {
            System.Func<string, Material> materials = SciFiMaterials(decor);
            // 壁は柱の間隔（Bay）ごとの区画にちょうど 1 枚の板を入れ、横の段（Rows）ごとに同じ種類の板をそろえる（2026-11-19 の要望:
            // 区画ごとにちぐはぐで「完成していないスライドパズル」のようだった。研究所のマップによくある、柱の格子と横の帯で整理した壁）。
            // 下から: 腰の帯 → 板 2 段 → 窓の帯 → 板 → 窓の帯 → 天井の配線。板の向きは表がカメラを向くように（WallYaw）。
            float scale = Bay / 4f;
            float height = 3f * scale;
            for (int row = 0; row < Rows.Length; row++)
            {
                float y = WallBottom + row * height;
                for (float x = -BayHalfCount * Bay + Bay * 0.5f; x < BayHalfCount * Bay; x += Bay)
                {
                    PlaceAgainstWall(decor, Rows[row], new Vector3(x, y, 0f), Vector3.one * scale, 13f, materials, fallback);
                }
            }
            // 柱（区画の境目ごと。1 段の高さにそろえた柱を段ごとに積む）。
            for (float x = -BayHalfCount * Bay; x <= BayHalfCount * Bay + 0.1f; x += Bay)
            {
                for (int row = 0; row < Rows.Length; row++)
                {
                    PlaceAgainstWall(decor, "Column_Large_Straight", new Vector3(x, WallBottom + row * height, 0f), new Vector3(1.2f, height / 10f, 1.2f), 12.4f, materials, fallback);
                }
            }
            // 左右の棟の床を奥へ（遊ぶ面の床は z ±2 まで）。その上の小物。
            Material floor = decor.Lit("LabBackFloor", new Color(0.2f, 0.22f, 0.26f));
            foreach (float side in new[] { -1f, 1f })
            {
                decor.Box("BackFloor", new Vector3(side * 34.5f, -1f, 7.5f), new Vector3(29f, 2f, 11f), floor);
                // 小物は区画の真ん中にそろえて少なめに（左右対称）: 計算機の作業台 2 台と、樽 2 本。
                (string Model, float X, float Z, float Scale, float Yaw)[] props =
                {
                    ("Prop_Computer", 28.8f, 10.5f, 1.6f, 180f), ("Prop_Computer", 31.2f, 10.5f, 1.6f, 180f),
                    ("Prop_Barrel_Large", 41.2f, 10.5f, 1.7f, 0f), ("Prop_Barrel_Large", 42.8f, 10.5f, 1.7f, 0f),
                };
                foreach (var prop in props)
                {
                    ThirdPartyProps.PlaceMulti(decor, SciFiPack, prop.Model, new Vector3(side * prop.X, 0f, prop.Z),
                        Quaternion.Euler(0f, side > 0f ? prop.Yaw : -prop.Yaw, 0f), Vector3.one * prop.Scale, materials, fallback);
                }
            }
            // 天井の照明（区画の真ん中ごと）。
            for (float x = -BayHalfCount * Bay + Bay * 0.5f; x < BayHalfCount * Bay; x += Bay)
            {
                ThirdPartyProps.PlaceMulti(decor, SciFiPack, "Prop_Light_Wide", new Vector3(x, 47f, 10f), Quaternion.identity, Vector3.one * 2f, materials, fallback);
            }
        }

        // 板・柱をカメラへ向けて置き、奥の面（いちばん奥の z）が back になるように前後をそろえる。
        private static void PlaceAgainstWall(StageDecor decor, string model, Vector3 position, Vector3 scale, float back,
            System.Func<string, Material> materials, Material fallback)
        {
            GameObject go = ThirdPartyProps.PlaceMulti(decor, SciFiPack, model, position, Quaternion.Euler(0f, WallYaw, 0f), scale, materials, fallback);
            if (go == null)
            {
                return;
            }
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            if (!first)
            {
                // 横は板の真ん中を x に、縦は下の端を y に、奥は back にそろえる。
                go.transform.position += new Vector3(position.x - bounds.center.x, position.y - bounds.min.y, back - bounds.max.z);
            }
        }

        // 板の表をカメラ（-z）へ向ける回転（度）。
        private const float WallYaw = -90f;

        // SF の材質（OBJ の材質の名前ごと。色・法線・発光の絵。研究所らしく白と青を基調に、光は水色）。
        private static System.Func<string, Material> SciFiMaterials(StageDecor decor)
        {
            var cache = new System.Collections.Generic.Dictionary<string, Material>();
            Material Make(string name, string baseMap, string normalMap, string emissionMap, Color tint, Color emission, float metallic, float smoothness, float cutoff)
            {
                return SandboxWireSceneBuilder.LoadOrCreateShaderMaterial($"SciFi_{name}", ThirdPartyProps.PropShader, m =>
                {
                    m.SetTexture("_BaseMap", baseMap != null ? ThirdPartyProps.LoadTexture(SciFiPack, baseMap, false) : null);
                    m.SetColor("_BaseColor", tint);
                    m.SetTexture("_BumpMap", normalMap != null ? ThirdPartyProps.LoadTexture(SciFiPack, normalMap, true) : null);
                    m.SetFloat("_BumpScale", 1f);
                    m.SetTexture("_EmissionMap", emissionMap != null ? ThirdPartyProps.LoadTexture(SciFiPack, emissionMap, false) : null);
                    m.SetColor("_EmissionColor", emission);
                    m.SetFloat("_Metallic", metallic);
                    m.SetFloat("_Smoothness", smoothness);
                    m.SetFloat("_Cutoff", cutoff);
                }, decor.Folder);
            }
            var cyan = new Color(0.5f * 2.4f, 1f * 2.4f, 1.15f * 2.4f, 1f);
            Material Resolve(string name)
            {
                switch (name)
                {
                    case "MI_Trim_01": return Make(name, "T_Trim_01_BaseColor", "T_Trim_01_Normal", "T_Trim_01_Emissive", Color.white, cyan, 0.5f, 0.5f, 0f);
                    case "MI_Trim_02":
                    case "MI_Trim_02_Rails": return Make("MI_Trim_02", "T_Trim_02_BaseColor", "T_Trim_02_Normal", null, Color.white, Color.black, 0.4f, 0.45f, 0f);
                    case "MI_Trim_02_Variation": return Make(name, "T_Trim_02_BaseColor_Blue", "T_Trim_02_Normal", null, Color.white, Color.black, 0.4f, 0.45f, 0f);
                    case "MI_Trim_03": return Make(name, "T_Trim_03_BaseColor", "T_Trim_03_Normal", null, Color.white, Color.black, 0.2f, 0.4f, 0f);
                    case "MI_Trim_03_Dark": return Make(name, "T_Trim_03_BaseColor", "T_Trim_03_Normal", null, new Color(0.32f, 0.34f, 0.4f), Color.black, 0.3f, 0.4f, 0f);
                    case "MI_Trim_03_Cables": return Make(name, "T_Trim_03_Cables", null, null, Color.white, Color.black, 0.1f, 0.35f, 0f);
                    case "MI_Trim_03_Cables_Blue": return Make(name, "T_Trim_03_Cables", null, null, new Color(0.55f, 0.75f, 1f), Color.black, 0.1f, 0.35f, 0f);
                    case "MI_PaddedWall": return Make(name, "T_PaddedWall_BaseColor", "T_PaddedWall_Normal", "T_PaddingWall_Emissive", Color.white, cyan, 0.2f, 0.35f, 0f);
                    case "M_Decal_White":
                    case "MI_Decal_Red":
                    case "MI_Decal_Line": return Make(name, "T_Decals", null, null, Color.white, Color.black, 0f, 0.3f, 0.5f);
                    case "M_Decal_Screen": return Make(name, "T_Decals", null, "T_Decals", Color.white, new Color(0.5f, 1.4f, 1.8f, 1f), 0f, 0.6f, 0.5f);
                    case "M_Glass": return Make(name, null, null, null, new Color(0.12f, 0.22f, 0.3f), new Color(0.05f, 0.18f, 0.24f, 1f), 0.6f, 0.92f, 0f);
                    case "M_Black": return Make(name, null, null, null, new Color(0.03f, 0.03f, 0.04f), Color.black, 0f, 0.3f, 0f);
                    case "M_Light": return Make(name, null, null, null, Color.white, new Color(2.6f, 2.8f, 3f, 1f), 0f, 0.5f, 0f);
                    case "M_LightFade_Red": return Make(name, null, null, null, new Color(1f, 0.2f, 0.2f), new Color(2.4f, 0.3f, 0.3f, 1f), 0f, 0.5f, 0f);
                    default: return null;
                }
            }
            return name =>
            {
                if (!cache.TryGetValue(name, out Material material))
                {
                    material = Resolve(name);
                    cache[name] = material;
                }
                return material;
            };
        }

        private static void Decorate(StageDecor decor)
        {
            Material wall = decor.Lit("LabWall", new Color(0.62f, 0.67f, 0.72f));
            Material panel = decor.Lit("LabPanel", new Color(0.48f, 0.53f, 0.6f));
            Material pipe = decor.Lit("Pipe", new Color(0.55f, 0.58f, 0.6f));
            Material glass = decor.Transparent("TankGlass", new Color(0.6f, 0.95f, 1f, 0.18f));
            Material liquid = decor.Unlit("TankLiquid", new Color(0.25f, 1f, 0.55f));
            Material screen = decor.Unlit("Monitor", new Color(0.2f, 0.65f, 1f));
            Material pit = decor.Lit("LabPit", new Color(0.08f, 0.09f, 0.12f));
            Material warning = decor.Unlit("Warning", new Color(1f, 0.82f, 0.1f));

            // 奥の壁（後ろの埋め）と、その手前の SF の壁の板・柱・床の小物（Quaternius の Modular SciFi MegaKit。CC0。2026-11-18 の要望:
            // 背景を無料のアセットで精巧に）。
            decor.Box("BackWall", new Vector3(0f, 16f, 14f), new Vector3(140f, 70f, 2f), wall);
            SciFiInterior(decor, panel);
            // 谷の奥（遊ぶ面より奥だけ）。
            decor.Box("PitBack", new Vector3(0f, -20f, 8f), new Vector3(140f, 28f, 12f), pit);
            // 実験室の培養槽と、モニター。
            foreach (float x in new[] { -39f, -35f, -31f, 31f, 35f, 39f })
            {
                decor.Cylinder("TankLiquid", new Vector3(x, 2.8f, 2.6f), new Vector3(1.6f, 2.4f, 1.6f), liquid);
                decor.Cylinder("TankGlass", new Vector3(x, 2.9f, 2.6f), new Vector3(1.9f, 2.7f, 1.9f), glass);
            }
            foreach (float x in new[] { -34.5f, 34.5f })
            {
                decor.Quad("Monitor", new Vector3(x, 5.6f, 1.9f), new Vector2(4f, 1.6f), screen);
            }
            // エレベーターの縦穴の枠（見た目だけ。当たりはない）と、警告の縞。
            foreach (float x in new[] { -4.6f, 4.6f })
            {
                decor.Box("ShaftRail", new Vector3(x, 12f, 2.5f), new Vector3(0.4f, 26f, 0.4f), pipe);
            }
            for (int i = 0; i < 6; i++)
            {
                GameObject band = decor.Box("ShaftStripe", new Vector3(-3f + i * 1.2f, 21.7f, -2.1f), new Vector3(0.5f, 0.12f, 0.05f), warning);
                band.transform.rotation = Quaternion.Euler(0f, 0f, 35f);
            }
            // 気送管のガラス。
            foreach (float x in new[] { -47.3f, 47.3f })
            {
                decor.Box("TubeGlass", new Vector3(x, 15f, 0f), new Vector3(3.4f, 30f, 3.6f), glass);
            }
            // 天井の配管。
            for (int i = 0; i < 5; i++)
            {
                decor.Cylinder("CeilingPipe", new Vector3(0f, 44f - i * 1.3f, 10f + i), new Vector3(0.6f, 70f, 0.6f), pipe)
                    .transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }
    }
}
