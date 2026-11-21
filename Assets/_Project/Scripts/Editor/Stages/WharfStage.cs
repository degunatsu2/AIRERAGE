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
                Music = "Wharf",
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
                    // 灯台（2026-11-20 の要望: すぐ奥に灯台の模型を置き、真ん中に柱の形の足場。てっぺんはバルコニー〈灯台の展望台〉に合わせた
                    // 床。前は x -49〜-45、今は展望台の幅に合わせて -50〜-44）。
                    // 柱は細く（幅 2 → 1m。後ろの灯台がよく見えるように。2026-11-20 の要望）。
                    new TerrainBox("Lighthouse", -47.5f, 1f, -46.5f, 22f, attachable: true),
                    new TerrainBox("LighthouseDeck", LighthouseX - GalleryRadius, 22f, LighthouseX + GalleryRadius, 22.6f, attachable: true),
                    // 積み上げたコンテナ（鉄。ワイヤーがつながる）。
                    new TerrainBox("StackWestLow", -42f, 1f, -36f, 3.6f, attachable: true),
                    new TerrainBox("StackWestHigh", -42f, 3.6f, -36f, 6.2f, attachable: true),
                    new TerrainBox("StackEastLow", 36f, 1f, 42f, 3.6f, attachable: true),
                    new TerrainBox("StackEastHigh", 36f, 3.6f, 42f, 6.2f, attachable: true),
                    // ガントリークレーン（脚と上の梁。梁の真ん中に穴があり、外へ抜けられる。ユーザーの要望、2026-10-26）。梁は左の荷台の
                    // いちばん上（24.5m）まで下げた（前は 30m。2026-11-20 の要望）。
                    new TerrainBox("CraneLegWest", -26f, 1f, -25f, BeamBottom, attachable: true),
                    new TerrainBox("CraneLegEast", 25f, 1f, 26f, BeamBottom, attachable: true),
                    new TerrainBox("CraneBeamWest", -27f, BeamBottom, -3f, BeamTop, attachable: true),
                    new TerrainBox("CraneBeamEast", 3f, BeamBottom, 27f, BeamTop, attachable: true),
                    // 梁の上に積んだコンテナ（左右に 2 つずつと、その上に 1 つ。鉄。ワイヤーがつながる。2026-11-20 の要望: 天井の上が寂しい）。
                    new TerrainBox("BeamContainerWestOuter", -24f, BeamTop, -18f, BeamTop + 2.6f, attachable: true),
                    new TerrainBox("BeamContainerWestInner", -12f, BeamTop, -6f, BeamTop + 2.6f, attachable: true),
                    new TerrainBox("BeamContainerWestTop", -23.5f, BeamTop + 2.6f, -18.5f, BeamTop + 5.2f, attachable: true),
                    new TerrainBox("BeamContainerEastOuter", 18f, BeamTop, 24f, BeamTop + 2.6f, attachable: true),
                    new TerrainBox("BeamContainerEastInner", 6f, BeamTop, 12f, BeamTop + 2.6f, attachable: true),
                    new TerrainBox("BeamContainerEastTop", 18.5f, BeamTop + 2.6f, 23.5f, BeamTop + 5.2f, attachable: true),
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
                    new Vector2(5f, -2.1f), new Vector2(-27.5f, 1.9f), new Vector2(-14f, BeamBottom - 4f), new Vector2(14f, BeamBottom - 4f),
                    new Vector2(-47f, 27f), new Vector2(0f, BeamTop + 5.6f), new Vector2(-37f, 17f), new Vector2(37f, 17f),
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
                    // 荷役の網（2026-11-21 の要望）: クレーンの梁からワイヤーと 4 本足の吊り具で吊った、鉄の枠に張った網。重なった人を跳ね返し
                    // （前の跳ね返す物と同じ 16 m/s）、真ん中にワイヤーがつながる。ぶら下がると吊り具が速く伸びて 4m 下がり（バンジーのように少し
                    // 行きすぎて跳ね返る）、外すとゆっくり戻る（StagePlatformKind.Hoist・CargoNetView）。
                    new PlatformSpec("NetWest", new Vector2(-18f, 14f), new Vector2(3f, 1f), true)
                    {
                        Visual = (decor, visual, spec) => StageSceneBuilder.BuildCargoNet(decor, visual, spec, BeamBottom),
                    }.Hoist(4f, 16f, 1.8f),
                    new PlatformSpec("NetEast", new Vector2(18f, 14f), new Vector2(3f, 1f), true)
                    {
                        Visual = (decor, visual, spec) => StageSceneBuilder.BuildCargoNet(decor, visual, spec, BeamBottom),
                    }.Hoist(4f, 16f, 1.8f),
                    // クレーンのコンテナ（ユーザーの案）: 屋根と床の 2 枚が一緒に運ばれる（左右は開いていて、両側から入れる）。
                    ContainerPlate("ContainerRoof", spotA + new Vector2(0f, plateOffset), spotA, spotC, spotB, containerRed, roof: true),
                    ContainerPlate("ContainerFloor", spotA - new Vector2(0f, plateOffset), spotA, spotC, spotB, containerRed, roof: false),
                    // フォークリフトの荷台: 左の岸壁で、灯台のてっぺんより少し上（上面 24.5m）まで上下する（14 秒で 1 往復。ユーザーの要望、
                    // 2026-10-26）。
                    Pallet("ForkliftPallet", -31f, 0f),
                    // 灯台の左の昇降台（右の荷台と周期が逆。上と下が入れ替わる。2026-11-20 の要望）。
                    Pallet("LighthousePallet", WestPalletX, 7f),
                    // クレーンの右の脚の両側のボラード（ユーザーの要望、2026-10-26）: 9 秒ごとに、岸壁の上で 3.3 秒待ち、1.2 秒で上へ、上で
                    // 3.3 秒待ち、1.2 秒で下へ。内側と外側は半分ずらす（片方が上なら、もう片方は下）。中心がアンカー（ワイヤーはつないだまま
                    // 運ばれる）。内側は梁の下（乗っても頭がぶつからない高さ）まで、外側は梁の上面の高さまで上がる。
                    Bollard("BollardInner", new Vector2(24.45f, 1.46f), BeamBottom - 2.4f, 0f),
                    Bollard("BollardOuter", new Vector2(27.5f, 1.46f), BeamTop - 0.45f, 4.5f),
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
                    // クレーンの脚と梁（梁を下げた分だけ下げ、梁の上はコンテナの間と上。2026-11-20 の要望）。
                    (new Vector2(-24f, 12f), 1), (new Vector2(24f, 12f), 1), (new Vector2(-22f, 18f), 1), (new Vector2(22f, 18f), 1),
                    (new Vector2(-15f, BeamTop + 1.5f), 1), (new Vector2(15f, BeamTop + 1.5f), 1), (new Vector2(0f, BeamBottom - 1.3f), 20),
                    (new Vector2(-14f, BeamBottom - 2f), 3), (new Vector2(14f, BeamBottom - 2f), 3),
                    (new Vector2(-9f, BeamTop + 4f), 3), (new Vector2(9f, BeamTop + 4f), 3),
                    // 梁の上に積んだコンテナのいちばん上の赤いコイン（前は梁の左右の空中に浮いていた）。
                    (new Vector2(-21f, BeamTop + 6.7f), 20), (new Vector2(21f, BeamTop + 6.7f), 20),
                    // 灯台と倉庫の屋根。
                    (new Vector2(-47f, 24.5f), 3), (new Vector2(-45f, 14f), 1), (new Vector2(-43f, 20f), 1), (new Vector2(44f, 14.5f), 3),
                    (new Vector2(40f, 14.5f), 1), (new Vector2(47f, 17f), 1),
                    // 高い所とステージの端。
                    (new Vector2(-53f, 10f), 20), (new Vector2(53f, 10f), 20),
                    (new Vector2(-53f, 24f), 3), (new Vector2(53f, 24f), 3), (new Vector2(-30f, 20f), 20), (new Vector2(30f, 20f), 20),
                    (new Vector2(-40f, 22f), 20), (new Vector2(40f, 22f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, BeamTop + 8.6f), new Vector2(-47f, 32f), new Vector2(47f, 32f), new Vector2(-13f, -10f), new Vector2(13f, -10f),
                    new Vector2(0f, 14f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 4.5f), new Vector2(-39f, 9.5f), new Vector2(39f, 9.5f), new Vector2(-46.5f, 27.5f), new Vector2(43f, 17f),
                    new Vector2(0f, BeamTop + 2.1f), new Vector2(-24f, 17f), new Vector2(24f, 17f),
                },
                Spawns = new[] { new Vector3(-21.5f, 2f, 0f), new Vector3(21.5f, 2f, 0f), new Vector3(-44f, 2f, 0f), new Vector3(44f, 2f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-34.25f, 2f, 0f), new Vector3(39.75f, 13.6f, 0f), new Vector3(-3.75f, -2f, 0f), new Vector3(4f, -2f, 0f) },
                Decorate = Decorate,
                // 背景とオブジェクトのクオリティアップ（D-97）。
                Look = StageLook.FromTheme("Wharf", theme, StageMood.Day),
            };
        }

        // 灯台の中心（x）と奥行き（z。塔の手前の面が遊ぶ面のすぐ奥 z 2.5 に来る）、展望台の半径。
        private const float LighthouseX = -47f;
        private const float LighthouseZ = 5.8f;
        // 展望台の半径（床の左の端 x -50 が左の撃破の線 x -60 から 10m 以上離れるように。StageLayoutTests）。
        private const float GalleryRadius = 3f;

        // 右の岸壁（右端 x = 48）の右端のボラードの位置（端ぎりぎり。柱の右の縁が岸壁の端から 0.2m 出る。右の海から、浮いたまま
        // 岸壁の角に遮られずにつなげる）。
        private const float EastEdgeBollardX = 47.85f;

        // フォークリフトの荷台の上がる高さ（m。上面が灯台のてっぺん 22.6m より少し上の 24.5m）。
        private const float PalletRise = 23f;

        // クレーンの梁の下の面と上の面（下の面は荷台のいちばん上 24.5m にそろえる）。
        private const float BeamBottom = 24.5f;
        private const float BeamTop = 25.9f;

        // 灯台の左の昇降台の位置（x）。
        private const float WestPalletX = -53f;

        // 上下する荷台（14 秒で 1 往復。phase 秒ずらす）。
        private static PlatformSpec Pallet(string name, float x, float phase)
        {
            return new PlatformSpec(name, new Vector2(x, 1.25f), new Vector2(3.2f, 0.5f), true)
                .Path(14f, (0f, Vector2.zero), (1.5f, Vector2.zero), (6.5f, new Vector2(0f, PalletRise)), (8.5f, new Vector2(0f, PalletRise)),
                    (13.5f, Vector2.zero))
                .Eased().Phase(phase).Tinted(new Color(0.85f, 0.65f, 0.3f));
        }

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
        /// <summary>
        /// 湾の向こうの工業港（Kenney の City Kit Industrial: 倉庫・工場・煙突・タンク・給水塔・コンテナ）と、湾に浮かぶ船
        /// （Kenney の Watercraft: 貨物船・大きな船・タグボート・漁船・帆船・ブイ）。CC0。2026-11-19 にユーザーが追加。見た目だけ。
        /// </summary>
        private static void Harbor(StageDecor decor, Material concrete)
        {
            Color tint = new Color(0.9f, 0.92f, 0.96f);
            string folder = decor.Folder;
            const float landTop = -3f;
            decor.Box("HarborLand", new Vector3(0f, landTop - 2f, 118f), new Vector3(460f, 4f, 30f), concrete);
            string[] buildings = { "building-a", "building-b", "building-c", "building-e", "building-g", "building-j", "building-m", "building-q" };
            for (float x = -210f; x < 210f;)
            {
                string model = buildings[decor.Random.Next(buildings.Length)];
                float scale = decor.Range(8f, 10f);
                ThirdPartyProps.Place(decor, ThirdPartyProps.CityIndustrial, model, new Vector3(x, landTop, 122f + decor.Range(-3f, 3f)), scale, 180f, tint, folder);
                x += 2.2f * scale + decor.Range(2f, 8f);
            }
            foreach (float x in new[] { -150f, -64f, 38f, 120f })
            {
                ThirdPartyProps.Place(decor, ThirdPartyProps.CityIndustrial, "chimney-large", new Vector3(x, landTop, 128f), 10f, 0f, tint, folder);
            }
            foreach (float x in new[] { -96f, 76f, 160f })
            {
                ThirdPartyProps.Place(decor, ThirdPartyProps.CityIndustrial, "detail-tank-large", new Vector3(x, landTop, 110f), 9f, 30f, tint, folder);
            }
            ThirdPartyProps.Place(decor, ThirdPartyProps.CityIndustrial, "water-tower", new Vector3(-20f, landTop, 128f), 9f, 0f, tint, folder);
            // 岸壁のコンテナの山（2 段）。
            string[] containers = { "shipping-container-a", "shipping-container-b", "shipping-container-c" };
            for (float x = -120f; x < 130f; x += decor.Range(10f, 22f))
            {
                int stack = decor.Random.Next(1, 3);
                for (int level = 0; level < stack; level++)
                {
                    ThirdPartyProps.Place(decor, ThirdPartyProps.CityIndustrial, containers[decor.Random.Next(containers.Length)],
                        new Vector3(x, landTop + level * 0.35f * 9f, 106.5f), 9f, 90f, tint, folder);
                }
            }
            // 湾の船（水面 y -6。船底を少し沈める。横を向けて見せる）。
            void Boat(string model, float x, float z, float scale, float yaw)
            {
                ThirdPartyProps.Place(decor, ThirdPartyProps.Watercraft, model, new Vector3(x, -6f - 0.25f * scale, z), scale, yaw, tint, folder);
            }
            Boat("ship-cargo-a", -78f, 66f, 3.2f, 90f);
            // 右の船は貨物船の別の型（前の大きな帆船は中世の西洋すぎた。2026-11-20 の要望）。
            Boat("ship-cargo-c", 70f, 80f, 3f, -84f);
            Boat("boat-tug-a", -34f, 48f, 2.4f, 70f);
            Boat("boat-fishing-small", 28f, 44f, 2f, -100f);
            Boat("boat-sail-a", -130f, 84f, 2.6f, 80f);
            Boat("ship-cargo-b", 150f, 92f, 3f, 95f);
            foreach (float x in new[] { -46f, -18f, 18f, 46f })
            {
                ThirdPartyProps.Place(decor, ThirdPartyProps.Watercraft, "buoy", new Vector3(x, -6.2f, 30f), 1.6f, 0f, tint, folder);
            }
        }

        private static void Decorate(StageDecor decor)
        {
            // 海は水のシェーダーの水面（空を映し、さざ波と太陽のきらめき。2026-11-18 の要望: 水の表現をリアルに）。
            Material sea = decor.Water("SeaWater", new Color(0.18f, 0.46f, 0.6f), new Color(0.03f, 0.13f, 0.25f), false, -5f, true);
            Material ice = decor.Lit("Ice", new Color(0.7f, 0.88f, 0.96f));
            Material snow = decor.Lit("Snow", new Color(0.93f, 0.95f, 0.98f));
            Material concrete = TerrainSculpt.GroundMaterial(decor, "ConcreteGround", new Color(0.5f, 0.52f, 0.55f), TerrainSculpt.Photos.Concrete,
                new Color(0.55f, 0.56f, 0.58f), TerrainSculpt.Photos.Concrete, 0.25f);
            Material bollard = decor.Lit("Bollard", new Color(0.15f, 0.15f, 0.17f));
            Material warehouse = decor.Lit("Warehouse", new Color(0.55f, 0.6f, 0.68f));

            // 奥の海（遊ぶ面より奥だけ）。
            // 海は奥の山の向こうまで続け、霧で空の地平線の色に溶かす（水平線。2026-11-18 の要望: 最後面は空、地平線の処理）。
            decor.Box("Sea", new Vector3(0f, -22f, 211f), new Vector3(1000f, 34f, 418f), sea);
            // 遠くの氷山のステージ（FROST COAST）: 浮かぶ氷、氷山、船。
            decor.Box("FarFloeA", new Vector3(-30f, -4.5f, 95f), new Vector3(10f, 3f, 8f), ice);
            decor.Box("FarFloeB", new Vector3(-10f, -4.2f, 100f), new Vector3(14f, 3.6f, 9f), ice);
            decor.Box("FarIceberg", new Vector3(10f, 2f, 98f), new Vector3(10f, 16f, 10f), ice);
            decor.Box("FarIcebergPeak", new Vector3(12f, 12f, 98f), new Vector3(5f, 6f, 6f), snow);
            decor.Box("FarFloeC", new Vector3(26f, -3.5f, 96f), new Vector3(8f, 5f, 7f), ice);
            // 遠くの船も貨物船に（前は木の船体と帆。2026-11-20 の要望）。
            ThirdPartyProps.Place(decor, ThirdPartyProps.Watercraft, "ship-cargo-b", new Vector3(42f, -6f - 0.6f, 100f), 2.4f, -96f,
                new Color(0.9f, 0.92f, 0.96f), decor.Folder);
            // 遠くの雪山（雑音の高さの網。2026-11-18 の要望: 地面のモデリング。前は 45° 回した箱）。
            TerrainSculpt.PlaceSnowRange(decor, "Wharf", 140f, 310f, -12f, 57);
            Harbor(decor, concrete);
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
            // 内側のボラードは梁の下まで（溝は梁の中で止める。梁の上の面と重ねない）、外側は梁の上まで。
            foreach ((float x, float top) in new[] { (24.45f, BeamBottom + 0.3f), (27.5f, BeamTop) })
            {
                decor.Box("BollardRail", new Vector3(x, (1.2f + top) * 0.5f, 1.2f), new Vector3(0.3f, top - 1.2f, 0.3f), rail);
            }
            foreach (float x in new[] { -32.4f, -29.6f, WestPalletX - 1.4f, WestPalletX + 1.4f })
            {
                // 灯台の左の荷台の柱は海の中から立つ。
                float bottom = x < -50f ? -8f : 1f;
                decor.Box("PalletMast", new Vector3(x, (bottom + 25.2f) * 0.5f, 2.4f), new Vector3(0.4f, 25.2f - bottom, 0.4f), rail);
            }
            foreach (float x in new[] { -5f, 5f })
            {
                decor.Cylinder("TugBollard", new Vector3(x, -2.55f, 0f), new Vector3(0.5f, 0.45f, 0.5f), bollard);
            }
            // 灯台の模型（柱の足場のすぐ奥）と、回る光。
            Lighthouse(decor);
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
            Transform target = GameObject.Find("Platforms/ContainerFloor/Visual")?.transform;
            var rig = new GameObject("Forklift");
            rig.transform.SetParent(decor.Root, false);
            Forklift(decor, rig.transform, out Transform body, out Transform mast, out Transform carriage);
            rig.AddComponent<ForkliftRig>().Configure(target, body, mast, carriage, 1f, 9f, 5.4f, PlateThickness * 0.5f + 0.1f);
        }

        // 灯台の模型（2026-11-20 の要望: 精巧な灯台をすぐ奥に置き、頂上の展望台と足場を合わせ、光も模型に合わせた光源に）:
        // 石の土台 → 赤白の縞の塔（上ほど細い）→ 張り出した展望台と手すり → ガラスの灯室と枠 → 中の明かり（レンズ）→ 丸い屋根と換気の玉と
        // 避雷針。窓と扉は塔の手前の左右（真ん中は柱の足場が前に立つ）。光は灯室の真ん中から 2 本の筋が回る（BackdropMotion）。
        private static void Lighthouse(StageDecor decor)
        {
            const string stage = "Wharf";
            var root = new GameObject("Lighthouse").transform;
            root.SetParent(decor.Root, false);
            root.position = new Vector3(LighthouseX, 1f, LighthouseZ);
            Material stone = decor.Lit("LighthouseStone", new Color(0.52f, 0.5f, 0.48f));
            Material white = decor.Lit("LighthouseWhite", new Color(0.93f, 0.93f, 0.9f));
            Material red = decor.Lit("LighthouseRed", new Color(0.78f, 0.12f, 0.1f));
            Material iron = decor.Lit("LighthouseIron", new Color(0.12f, 0.13f, 0.15f));
            iron.SetFloat("_Smoothness", 0.6f);
            Material roof = decor.Lit("LighthouseRoof", new Color(0.55f, 0.08f, 0.07f));
            roof.SetFloat("_Smoothness", 0.55f);
            Material glass = decor.Transparent("LighthouseGlass", new Color(1f, 0.95f, 0.7f, 0.22f));
            Material lamp = decor.Unlit("LighthouseLamp", new Color(1f, 0.93f, 0.7f));
            Material window = decor.Lit("LighthouseWindow", new Color(0.08f, 0.1f, 0.14f));

            // 土台（八角形の石）。
            ProceduralMesh.Place("Plinth", root, ProceduralMesh.Lathe(stage, "LighthousePlinth",
                new[] { new Vector2(4f, -0.05f), new Vector2(4f, 0.9f), new Vector2(3.6f, 1.25f) }, 8), Vector3.zero, Quaternion.Euler(0f, 22.5f, 0f), Vector3.one, stone);
            // 塔: 半径 3.3 → 2.35（高さ 1.2 → 21）。縞は 4 本（白・赤の交互）。
            const float towerBottom = 1.2f;
            const float towerTop = 21f;
            const int bands = 6;
            for (int i = 0; i < bands; i++)
            {
                float y0 = Mathf.Lerp(towerBottom, towerTop, (float)i / bands);
                float y1 = Mathf.Lerp(towerBottom, towerTop, (float)(i + 1) / bands);
                Mesh band = ProceduralMesh.Lathe(stage, $"LighthouseBand{i + 1}",
                    new[] { new Vector2(TowerRadius(y0), y0), new Vector2(TowerRadius(y1), y1) }, 40);
                ProceduralMesh.Place($"Band{i + 1}", root, band, Vector3.zero, Quaternion.identity, Vector3.one, i % 2 == 0 ? white : red);
            }
            // 窓と扉（塔の手前の左右。面に沿って少し回す）。
            foreach (float side in new[] { -1f, 1f })
            {
                for (int k = 0; k < 3; k++)
                {
                    float y = 5.5f + k * 5.2f;
                    float r = TowerRadius(y);
                    float angle = side * 32f;
                    var at = new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad) * r, y, -Mathf.Cos(angle * Mathf.Deg2Rad) * r);
                    GameObject w = decor.Box("Window", root, at, new Vector3(0.7f, 1.2f, 0.25f), window);
                    w.transform.localRotation = Quaternion.Euler(0f, -angle, 0f);
                    GameObject sill = decor.Box("Sill", root, at + new Vector3(0f, -0.7f, 0f), new Vector3(1f, 0.15f, 0.4f), stone);
                    sill.transform.localRotation = Quaternion.Euler(0f, -angle, 0f);
                }
            }
            // 展望台（張り出した床と、下の受け）。上の面は足場の上の面（y 22.6）にそろえる。
            float galleryY = 22.6f - 1f;
            ProceduralMesh.Place("Gallery", root, ProceduralMesh.Lathe(stage, "LighthouseGallery",
                new[] { new Vector2(TowerRadius(towerTop), galleryY - 1.6f), new Vector2(GalleryRadius - 0.3f, galleryY - 0.6f), new Vector2(GalleryRadius, galleryY - 0.6f),
                    new Vector2(GalleryRadius, galleryY) }, 40), Vector3.zero, Quaternion.identity, Vector3.one, iron);
            // 手すり（展望台の縁をぐるりと。手前の足場に立つ人より奥）: 支柱と、上の横木。
            const int posts = 28;
            float railR = GalleryRadius - 0.12f;
            for (int i = 0; i < posts; i++)
            {
                float a = i * Mathf.PI * 2f / posts;
                var p = new Vector3(Mathf.Cos(a) * railR, galleryY + 0.55f, Mathf.Sin(a) * railR);
                decor.Box("RailPost", root, p, new Vector3(0.07f, 1.1f, 0.07f), iron);
                float a2 = (i + 1) * Mathf.PI * 2f / posts;
                var q = new Vector3(Mathf.Cos(a2) * railR, galleryY + 1.1f, Mathf.Sin(a2) * railR);
                var top = new Vector3(p.x, galleryY + 1.1f, p.z);
                GameObject rail = decor.Box("Rail", root, (top + q) * 0.5f, new Vector3(0.09f, 0.09f, Vector3.Distance(top, q) + 0.05f), iron);
                rail.transform.localRotation = Quaternion.LookRotation(q - top);
            }
            // 灯室: 下の腰壁・ガラスの筒・縦の枠・上の帯。
            const float roomBottom = 22.6f - 1f;
            const float roomRadius = 1.75f;
            ProceduralMesh.Place("LanternBase", root, ProceduralMesh.Lathe(stage, "LighthouseLanternBase",
                new[] { new Vector2(roomRadius + 0.1f, roomBottom), new Vector2(roomRadius + 0.1f, roomBottom + 0.7f) }, 32), Vector3.zero, Quaternion.identity, Vector3.one, red);
            ProceduralMesh.Place("LanternGlass", root, ProceduralMesh.Lathe(stage, "LighthouseLanternGlass",
                new[] { new Vector2(roomRadius, roomBottom + 0.7f), new Vector2(roomRadius, roomBottom + 3.3f) }, 32), Vector3.zero, Quaternion.identity, Vector3.one, glass);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                decor.Box("Mullion", root, new Vector3(Mathf.Cos(a) * roomRadius, roomBottom + 2f, Mathf.Sin(a) * roomRadius), new Vector3(0.09f, 2.6f, 0.09f), iron);
            }
            ProceduralMesh.Place("LanternRing", root, ProceduralMesh.Lathe(stage, "LighthouseLanternRing",
                new[] { new Vector2(roomRadius + 0.15f, roomBottom + 3.3f), new Vector2(roomRadius + 0.15f, roomBottom + 3.6f) }, 32), Vector3.zero, Quaternion.identity, Vector3.one, iron);
            // 中の明かり（段のあるレンズの形）。
            float lampY = roomBottom + 2f;
            ProceduralMesh.Place("Lens", root, ProceduralMesh.Lathe(stage, "LighthouseLens",
                new[] { new Vector2(0.35f, -0.75f), new Vector2(0.62f, -0.45f), new Vector2(0.72f, 0f), new Vector2(0.62f, 0.45f), new Vector2(0.35f, 0.75f) }, 20),
                new Vector3(0f, lampY, 0f), Quaternion.identity, Vector3.one, lamp);
            // 丸い屋根と換気の玉、避雷針。
            float roofY = roomBottom + 3.6f;
            ProceduralMesh.Place("Dome", root, ProceduralMesh.Lathe(stage, "LighthouseDome",
                new[] { new Vector2(2.15f, roofY), new Vector2(1.95f, roofY + 0.45f), new Vector2(1.5f, roofY + 1.2f), new Vector2(0.8f, roofY + 1.75f), new Vector2(0f, roofY + 1.95f) }, 32),
                Vector3.zero, Quaternion.identity, Vector3.one, roof);
            decor.Sphere("Vent", root, new Vector3(0f, roofY + 2.2f, 0f), Vector3.one * 0.55f, iron);
            decor.Cylinder("Rod", root, new Vector3(0f, roofY + 3.1f, 0f), new Vector3(0.06f, 0.75f, 0.06f), iron);
            // 光: 灯室の真ん中から 2 本の光の筋が、ゆっくり回る（手前を向くと薄くなる）。
            var pivot = new GameObject("BeamPivot").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0f, lampY, 0f);
            Material beam = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("LighthouseBeam", "Airerage/LightBeam", m =>
            {
                // まぶしさを抑える（2026-11-20 の 2 回目の指摘）: 色を控えめに、カメラから 50m より近い所は薄く。
                m.SetColor("_Color", new Color(0.55f, 0.52f, 0.38f, 1f));
                m.SetFloat("_Falloff", 1.8f);
                m.SetFloat("_FacingFade", 1f);
                m.SetFloat("_NearFade", 50f);
            }, decor.Folder);
            Mesh cone = ProceduralMesh.Lathe(stage, "LighthouseBeamCone", new[] { new Vector2(0.02f, 0f), new Vector2(1f, 1f) }, 24);
            foreach (float yaw in new[] { 0f, 180f })
            {
                var arm = new GameObject("Beam").transform;
                arm.SetParent(pivot, false);
                arm.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, -90f);
                ProceduralMesh.Place("Cone", arm, cone, Vector3.zero, Quaternion.identity, new Vector3(2.4f, 46f, 2.4f), beam)
                    .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            pivot.gameObject.AddComponent<BackdropMotion>().ConfigureSpin(new Vector3(0f, 36f, 0f));
            // 灯室の点の光は外した（2026-11-20 の 2 回目の指摘: まぶしい。すぐ近くの面とキャラクターに強い照り返しを作る恐れ）。
        }

        // 塔の半径（土台からの高さ y の所。上ほど細い）。
        private static float TowerRadius(float y)
        {
            return Mathf.Lerp(3.3f, 2.35f, Mathf.InverseLerp(1.2f, 21f, y));
        }

        // 赤いコンテナを運ぶ大きなフォークリフト（2026-11-20 の要望で精巧に）: 黄色い車体（面取りした箱を重ねる）・黒と黄色の縞の
        // 重り・運転席の柱と屋根のガード・座席とハンドル・前後のタイヤとホイール・前の明かりと上の回転灯・マスト（2 本の溝の柱と
        // 油圧の筒と鎖。ForkliftRig が高さを伸ばすので縦の部品だけ）・爪の台（背板と L 字の爪 2 本）。
        private static void Forklift(StageDecor decor, Transform rig, out Transform body, out Transform mast, out Transform carriage)
        {
            Material yellow = decor.Lit("ForkliftYellow", new Color(0.95f, 0.72f, 0.12f));
            yellow.SetFloat("_Smoothness", 0.55f);
            Material dark = decor.Lit("ForkliftDark", new Color(0.15f, 0.15f, 0.17f));
            Material steel = decor.Lit("ForkliftSteel", new Color(0.55f, 0.57f, 0.6f));
            steel.SetFloat("_Metallic", 0.7f);
            steel.SetFloat("_Smoothness", 0.6f);
            Material tire = decor.Lit("ForkliftTire", new Color(0.06f, 0.06f, 0.07f));
            Material stripeBlack = decor.Lit("ForkliftStripe", new Color(0.05f, 0.05f, 0.05f));
            Material glass = decor.Transparent("ForkliftGlass", new Color(0.55f, 0.7f, 0.8f, 0.3f));
            Material headlight = decor.Unlit("ForkliftHeadlight", new Color(1f, 0.95f, 0.8f));
            Material beacon = decor.Unlit("ForkliftBeacon", new Color(1f, 0.5f, 0.05f));

            body = new GameObject("Body").transform;
            body.SetParent(rig, false);
            body.position = new Vector3(-12f, 3f, 9f);
            // 車体（下の太い台、上の細い台、前の傾き）。
            decor.Box("Chassis", body, new Vector3(0f, -0.4f, 0f), new Vector3(6.2f, 1.8f, 4.4f), yellow);
            decor.Box("Deck", body, new Vector3(0.6f, 0.75f, 0f), new Vector3(4.6f, 0.5f, 4.2f), yellow);
            decor.Box("Skirt", body, new Vector3(0f, -1.25f, 0f), new Vector3(6.4f, 0.3f, 4.5f), dark);
            // 重り（後ろ。黒と黄色の斜めの縞）。
            decor.Box("Counterweight", body, new Vector3(2.75f, 0.35f, 0f), new Vector3(1.2f, 2.4f, 4.3f), yellow);
            for (int i = 0; i < 5; i++)
            {
                GameObject stripe = decor.Box("WeightStripe", body, new Vector3(3.36f, -0.45f + i * 0.45f, -1.2f + (i % 2) * 0.1f), new Vector3(0.05f, 0.18f, 1.6f), stripeBlack);
                stripe.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            }
            // 運転席: 4 本の柱と屋根のガード（格子）、座席、ハンドル、背もたれ。
            foreach (float px in new[] { -1.2f, 1.6f })
            {
                foreach (float pz in new[] { -1.8f, 1.8f })
                {
                    decor.Box("GuardPost", body, new Vector3(px, 2.4f, pz), new Vector3(0.18f, 2.9f, 0.18f), dark);
                }
            }
            decor.Box("GuardFrame", body, new Vector3(0.2f, 3.9f, 0f), new Vector3(3.1f, 0.16f, 3.9f), dark);
            for (int i = 0; i < 6; i++)
            {
                decor.Box("GuardSlat", body, new Vector3(-1.1f + i * 0.55f, 3.9f, 0f), new Vector3(0.1f, 0.12f, 3.8f), dark);
            }
            decor.Box("Seat", body, new Vector3(0.9f, 1.35f, 0f), new Vector3(1f, 0.35f, 1.2f), dark);
            decor.Box("SeatBack", body, new Vector3(1.4f, 1.9f, 0f), new Vector3(0.25f, 1f, 1.2f), dark);
            GameObject wheel = decor.Cylinder("SteeringWheel", body, new Vector3(-0.4f, 2.1f, 0f), new Vector3(0.7f, 0.04f, 0.7f), dark);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 55f);
            decor.Box("SteeringColumn", body, new Vector3(-0.6f, 1.55f, 0f), new Vector3(0.12f, 1.1f, 0.12f), dark).transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            decor.Box("Windscreen", body, new Vector3(-1.25f, 2.6f, 0f), new Vector3(0.04f, 1.6f, 3.4f), glass);
            // タイヤ（前は大きく 2 本ずつ、後ろは小さく）とホイール。
            foreach (float wx in new[] { -1.9f, 2.1f })
            {
                float size = wx < 0f ? 1.9f : 1.5f;
                foreach (float wz in new[] { -2.35f, 2.35f })
                {
                    GameObject t = decor.Cylinder("Tire", body, new Vector3(wx, -1.25f, wz), new Vector3(size, 0.35f, size), tire);
                    t.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    GameObject hub = decor.Cylinder("Hub", body, new Vector3(wx, -1.25f, wz + Mathf.Sign(wz) * 0.36f), new Vector3(size * 0.55f, 0.04f, size * 0.55f), steel);
                    hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
            // 明かり（前）と回転灯（屋根の上）。
            foreach (float hz in new[] { -1.5f, 1.5f })
            {
                decor.Sphere("Headlight", body, new Vector3(-3.15f, 0.3f, hz), new Vector3(0.12f, 0.35f, 0.35f), headlight);
            }
            decor.Cylinder("BeaconBase", body, new Vector3(0.6f, 4.05f, 0f), new Vector3(0.35f, 0.08f, 0.35f), dark);
            decor.Sphere("Beacon", body, new Vector3(0.6f, 4.3f, 0f), new Vector3(0.38f, 0.42f, 0.38f), beacon);

            // マスト（原点が中心の高さ 1 の箱。ForkliftRig が高さを伸ばす）: 2 本の溝の柱、真ん中の油圧の筒、鎖。
            mast = new GameObject("Mast").transform;
            mast.SetParent(rig, false);
            mast.position = new Vector3(-12f, 4f, 5.4f);
            foreach (float mx in new[] { -1.15f, 1.15f })
            {
                decor.Box("Upright", mast, new Vector3(mx, 0f, 0f), new Vector3(0.26f, 1f, 0.8f), steel);
                decor.Box("Flange", mast, new Vector3(mx - Mathf.Sign(mx) * 0.2f, 0f, -0.36f), new Vector3(0.3f, 1f, 0.1f), steel);
                decor.Box("Chain", mast, new Vector3(mx * 0.55f, 0f, 0.15f), new Vector3(0.12f, 1f, 0.08f), dark);
            }
            decor.Cylinder("Cylinder", mast, new Vector3(0f, 0f, 0.25f), new Vector3(0.36f, 0.5f, 0.36f), steel);

            // 爪の台（コンテナの下）: 背板と横の桁、L 字の爪 2 本。
            carriage = new GameObject("Carriage").transform;
            carriage.SetParent(rig, false);
            carriage.position = new Vector3(-12f, 4.4f, 3.4f);
            decor.Box("Backrest", carriage, new Vector3(0f, 0.75f, 1.85f), new Vector3(3f, 1.5f, 0.15f), dark);
            for (int i = 0; i < 4; i++)
            {
                decor.Box("BackrestBar", carriage, new Vector3(-1.1f + i * 0.73f, 0.75f, 1.75f), new Vector3(0.12f, 1.5f, 0.12f), steel);
            }
            decor.Box("Plate", carriage, new Vector3(0f, 0.1f, 1.8f), new Vector3(3.2f, 0.5f, 0.3f), steel);
            foreach (float fx in new[] { -3f, 3f })
            {
                decor.Box("Fork", carriage, new Vector3(fx, -0.08f, 0f), new Vector3(0.4f, 0.16f, 4f), yellow);
                decor.Box("ForkHeel", carriage, new Vector3(fx, 0.35f, 1.85f), new Vector3(0.4f, 0.9f, 0.18f), yellow);
            }
        }
    }
}
