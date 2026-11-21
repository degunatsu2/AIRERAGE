using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 陸上競技場（STORM STADIUM。STORM RUNNER のテーマのステージ。ユーザーの案、2026-10-26）。いちばん下はトラックが地続きで、撃破の線は
    /// 左右と上にしかない（トラックは左右の撃破の線の外まで続く。下へは落ちない）。真ん中に表彰台、左右にスタンドと照明塔、上に
    /// 大きな得点板。足場は少なく、空中のたくさんのアンカー（大きいアンカーもある）で飛び回る（ユーザーの要望、2026-10-26）。
    /// ギミック: ピッチャーマシン（照明塔の柱に付いていて、10 秒ずつずらして 20 秒ごとに大きな野球のボールを空中へ強く打ち出す。ボールは
    /// 天井と壁で大きく跳ね返りながら跳ね回り、止まったら点滅して消える。触れるとバンパーのように弾き返され、速いボールなら吹き飛ぶ。
    /// ユーザーの要望）。追加の案（残したもの）: 短距離のレーン（真ん中へ向かう
    /// スピードレーン）とハードル、棒高跳び（左右対称の発射台）、走り幅跳びの踏み切り板、スタンドのジャンプ台、つむじ風（表彰台の上を
    /// 行き来する上昇気流）、得点板へ向かう加速リング、スタンドの屋根と照明塔、得点板、旗（アンカー）。消したもの（ユーザーの要望）:
    /// 追い風と向かい風（全体の風）、転がる砲丸。
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
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(1f, 1f, 1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.85f, 0.3f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(0.85f, 0.75f, 0.55f);
            theme.ZoneColors[StageZoneKind.JumpPad] = new Color(0.3f, 0.6f, 1f);

            return new StageLayout
            {
                Id = "Stadium",
                DisplayName = "STORM STADIUM",
                Music = "Stadium",
                Description = "No bottom to fall from: knock rivals off the sides or over the scoreboard. Swing between the anchors and dodge the pitched baseballs.",
                DescriptionJa = "下に落ちる場所はない。相手を左右か得点板の上へ吹き飛ばして撃破。アンカーを渡り、飛んでくる野球のボールをかわせ。",
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
                // 旗と、照明塔と得点板の角と、空中のたくさんのアンカー（ユーザーの要望、2026-10-26）。
                Anchors = new[]
                {
                    new Vector2(-24f, 20f), new Vector2(24f, 20f), new Vector2(-14f, 26f), new Vector2(14f, 26f), new Vector2(-40f, 28f),
                    new Vector2(40f, 28f), new Vector2(0f, 13f), new Vector2(-10f, 35f), new Vector2(10f, 35f),
                    new Vector2(-6f, 20f), new Vector2(6f, 20f), new Vector2(-18f, 11f), new Vector2(18f, 11f), new Vector2(-18f, 31f),
                    new Vector2(18f, 31f), new Vector2(-28f, 15f), new Vector2(28f, 15f), new Vector2(-30f, 36f), new Vector2(30f, 36f),
                    new Vector2(-38f, 20f), new Vector2(38f, 20f), new Vector2(-52f, 16f), new Vector2(52f, 16f), new Vector2(-52f, 27f),
                    new Vector2(52f, 27f),
                },
                // 大きいアンカー（エイムアシストの半径 2.2m）。
                BigAnchors = new[]
                {
                    (new Vector2(0f, 24f), 2.2f), (new Vector2(-24f, 27f), 2.2f), (new Vector2(24f, 27f), 2.2f), (new Vector2(-46f, 33f), 2.2f),
                    (new Vector2(46f, 33f), 2.2f),
                },
                // 下の撃破の線はトラックのずっと下（届かない）。左右と上だけ（ユーザーの案）。
                BoundsMin = new Vector2(-60f, -20f),
                BoundsMax = new Vector2(60f, 40f),
                Theme = theme,
                Zones = new[]
                {
                    // 短距離のレーン（真ん中へ向かう）。
                    ZoneSpec.OnFloor("SprintLaneWest", StageZoneKind.SpeedLane, -32f, -15f, 0f, Vector2.right, 12f),
                    ZoneSpec.OnFloor("SprintLaneEast", StageZoneKind.SpeedLane, 15f, 32f, 0f, Vector2.left, 12f),
                    // 棒高跳び（左右対称。真ん中へ向かって高く跳ぶ。ユーザーの要望、2026-10-26）と、走り幅跳びの踏み切り板。
                    ZoneSpec.OnFloor("PoleVaultWest", StageZoneKind.Launcher, -23f, -21f, 0f, new Vector2(0.3f, 1f), 28f),
                    ZoneSpec.OnFloor("PoleVaultEast", StageZoneKind.Launcher, 21f, 23f, 0f, new Vector2(-0.3f, 1f), 28f),
                    ZoneSpec.OnFloor("LongJumpBoard", StageZoneKind.Launcher, -31.5f, -29.5f, 0f, new Vector2(1f, 0.55f), 24f),
                    ZoneSpec.OnFloor("LongJumpBoardEast", StageZoneKind.Launcher, 29.5f, 31.5f, 0f, new Vector2(-1f, 0.55f), 24f),
                    // スタンドの上のジャンプ台。
                    ZoneSpec.OnFloor("StandPadWest", StageZoneKind.JumpPad, -47f, -45.5f, 5f, Vector2.up, 26f, 0.4f),
                    ZoneSpec.OnFloor("StandPadEast", StageZoneKind.JumpPad, 45.5f, 47f, 5f, Vector2.up, 26f, 0.4f),
                    // つむじ風: 表彰台の上を左右に行き来する上昇気流（前の幅 2 倍・高さ 1.6 倍。得点板の 2m 下まで。ユーザーの要望、2026-10-26）。
                    new ZoneSpec("DustDevil", StageZoneKind.Updraft, new Vector2(-6f, 13f), new Vector2(6f, 26f), Vector2.up, 50f, 14f)
                        .Moving(new Vector2(12f, 0f), 12f).Styled(StageZoneView.ZoneLook.DustDevil),
                    // 得点板へ向かう加速リング。
                    new ZoneSpec("RingWest", StageZoneKind.BoostRing, new Vector2(-20f, 16f), new Vector2(2.4f, 3f), new Vector2(1f, 0.5f), 18f),
                    new ZoneSpec("RingEast", StageZoneKind.BoostRing, new Vector2(20f, 16f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.5f), 18f),
                },
                // ピッチャーマシン（ユーザーの要望、2026-10-26）: 照明塔の柱の内側に付いていて、20 秒ごとに真ん中へ打ち出す（左右で 10 秒ずらす。
                // 最初は右が 5 秒後、左が 15 秒後）。ボールは半径 0.8m（前の 2 倍）で、前より強く打ち出す。触れるとバンパーのように弾き返され、速いボールなら
                // 吹き飛んで威力 10 の枚数のコインを落とす【仮】。
                Traps = new[]
                {
                    // 2026-11-21 の方針: 2 つのタイヤのピッチングマシンにした（タイヤ・送りのレール・ボールの筒。照明塔の柱から腕で支える）。
                    // 機械が柱に埋まらないように、発射口を打ち出す向きへ 2.4m 先にした（前は (±31.4, 16)）。
                    TrapSpec.Pitcher("PitcherWest", new Vector2(-29.4f, 17.4f), new Vector2(26f, 18f), 20f, 5f, 0.8f, 10, 24f, 7f, 1.5f),
                    TrapSpec.Pitcher("PitcherEast", new Vector2(29.4f, 17.4f), new Vector2(-26f, 18f), 20f, 15f, 0.8f, 10, 24f, 7f, 1.5f),
                },
                Coins = new (Vector2, int)[]
                {
                    // レーンとハードルの上。
                    (new Vector2(-34.5f, 1.5f), 1), (new Vector2(-29.5f, 1.5f), 1), (new Vector2(-21.5f, 1.5f), 1), (new Vector2(-25.8f, 2.4f), 1),
                    (new Vector2(-17.8f, 2.4f), 1), (new Vector2(34.5f, 1.5f), 1), (new Vector2(29.5f, 1.5f), 1), (new Vector2(21.5f, 1.5f), 1),
                    (new Vector2(25.8f, 2.4f), 1), (new Vector2(17.8f, 2.4f), 1),
                    // 表彰台。
                    (new Vector2(-3f, 3f), 1), (new Vector2(3f, 2.5f), 1), (new Vector2(0f, 3.8f), 3),
                    // 棒高跳びの道（左右対称）。
                    (new Vector2(-19f, 6.5f), 1), (new Vector2(19f, 6.5f), 1), (new Vector2(-15f, 10.5f), 1), (new Vector2(15f, 10.5f), 1), (new Vector2(0f, 9f), 1),
                    (new Vector2(-9f, 13f), 3), (new Vector2(9f, 13f), 3),
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
                    new Vector2(0f, 16f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 6.5f), new Vector2(-46f, 11f), new Vector2(46f, 11f), new Vector2(-32.7f, 27.5f), new Vector2(32.7f, 27.5f),
                    new Vector2(-12f, 33f), new Vector2(-20f, 11f), new Vector2(20f, 11f),
                },
                // トラックの出現位置は、大きくなったつむじ風（左右の端で x = ±9 まで）の外（2026-10-26）。
                Spawns = new[] { new Vector3(-10.5f, 1f, 0f), new Vector3(10.5f, 1f, 0f), new Vector3(-43.5f, 3.5f, 0f), new Vector3(43.5f, 3.5f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-36.75f, 1f, 0f), new Vector3(36.75f, 1f, 0f), new Vector3(-45f, 17.6f, 0f), new Vector3(45f, 17.6f, 0f) },
                Decorate = Decorate,
                // 背景とオブジェクトのクオリティアップ（D-97）。観客席の背景があるので、遠くの街は置かない。
                Look = StageLook.FromTheme("Stadium", theme, StageMood.Day),
            };
        }

        // トラックのレーンの線と芝生、スタンドの観客、照明、得点板の画面、空の雲。
        /// <summary>
        /// Kenney の Racing Kit（CC0。2026-11-19 にユーザーが追加）の小物: スタンドの屋根の旗、スタンドの両端の旗の塔、芝生のテント、
        /// トラックと芝生の境の赤白の縁石。見た目だけ（当たりはない）。
        /// </summary>
        private static void RacingProps(StageDecor decor)
        {
            const string pack = "Kenney/Racing";
            string folder = decor.Folder;
            Color tint = Color.white;
            // 屋根の手前の縁の旗（赤・緑・チェッカーをかわるがわる）。
            string[] flags = { "flagRed", "flagGreen", "flagCheckers" };
            int index = 0;
            for (float x = -84f; x <= 84f; x += 14f)
            {
                ThirdPartyProps.PlaceMtl(decor, pack, flags[index++ % flags.Length], new Vector3(x + 0.6f, 22.4f, 43.6f), 0f, 5f, tint, folder);
            }
            // スタンドの両端の旗の塔。
            ThirdPartyProps.PlaceMtl(decor, pack, "bannerTowerRed", new Vector3(-96f, 0f, 34f), 0f, 9f, tint, folder);
            ThirdPartyProps.PlaceMtl(decor, pack, "bannerTowerGreen", new Vector3(98f, 0f, 34f), 0f, 9f, tint, folder);
            // 芝生のテント（大会の本部と救護所）。
            ThirdPartyProps.PlaceMtl(decor, pack, "tentLong", new Vector3(-30f, 0f, 20f), 0f, 5f, tint, folder);
            ThirdPartyProps.PlaceMtl(decor, pack, "tentClosed", new Vector3(-40f, 0f, 21f), 0f, 5f, tint, folder);
            ThirdPartyProps.PlaceMtl(decor, pack, "tentRoof", new Vector3(42f, 0f, 20f), 0f, 5f, tint, folder);
            ThirdPartyProps.PlaceMtl(decor, pack, "tentRoof", new Vector3(50f, 0f, 20f), 0f, 5f, tint, folder);
            ThirdPartyProps.PlaceMtl(decor, pack, "billboardLow", new Vector3(8f, 0f, 26f), 0f, 6f, tint, folder);
            // トラックと芝生の境の縁石（赤と白をかわるがわる）。
            int segment = 0;
            for (float x = -76f; x < 76f; x += 2f)
            {
                ThirdPartyProps.PlaceMtl(decor, pack, segment++ % 2 == 0 ? "barrierRed" : "barrierWhite", new Vector3(x + 2f, 0f, 3.2f), 0f, 8f, tint, folder);
            }
        }

        /// <summary>
        /// 奥のスタンド: 段々の観客席（14 段。区画ごとに座席の色を変え、区画の間に階段の通路）、前の壁の広告の板、座っている観客
        /// （体と頭の箱。色はまちまちで、4 割ほど空席）、上の屋根と鉄骨と屋根の下の照明、両端の照明塔（光る灯りの格子）。
        /// 小さな箱は 1 つの網にまとめる（TerrainSculpt.BoxBatch）。見た目だけ（当たりはない）。
        /// </summary>
        private static void Grandstand(StageDecor decor)
        {
            const int Concrete = 0, SeatBlue = 1, SeatSky = 2, SeatWhite = 3, SeatRed = 4, Skin = 5, SkinDark = 6, Steel = 7, AdA = 8, AdB = 9;
            const int ShirtFirst = 10, Shirts = 7, RoofLamp = 17;
            Material[] materials =
            {
                decor.Lit("StandConcrete", new Color(0.62f, 0.62f, 0.64f)),
                decor.Lit("SeatBlue", new Color(0.12f, 0.28f, 0.68f)),
                decor.Lit("SeatSky", new Color(0.3f, 0.56f, 0.86f)),
                decor.Lit("SeatWhite", new Color(0.88f, 0.9f, 0.92f)),
                decor.Lit("SeatRed", new Color(0.78f, 0.16f, 0.16f)),
                decor.Lit("FanSkin", new Color(0.86f, 0.67f, 0.53f)),
                decor.Lit("FanSkinDark", new Color(0.55f, 0.38f, 0.27f)),
                decor.Lit("StandSteel", new Color(0.36f, 0.38f, 0.42f)),
                decor.Unlit("AdBoardA", new Color(0.1f, 0.75f, 0.45f)),
                decor.Unlit("AdBoardB", new Color(1f, 0.62f, 0.12f)),
                decor.Lit("ShirtRed", new Color(0.82f, 0.18f, 0.18f)),
                decor.Lit("ShirtBlue", new Color(0.18f, 0.36f, 0.8f)),
                decor.Lit("ShirtWhite", new Color(0.92f, 0.92f, 0.9f)),
                decor.Lit("ShirtYellow", new Color(0.95f, 0.8f, 0.2f)),
                decor.Lit("ShirtBlack", new Color(0.12f, 0.12f, 0.14f)),
                decor.Lit("ShirtGreen", new Color(0.2f, 0.6f, 0.3f)),
                decor.Lit("ShirtGray", new Color(0.5f, 0.5f, 0.52f)),
                decor.Unlit("RoofLamp", new Color(1.6f, 1.55f, 1.35f)),
            };
            var batch = new TerrainSculpt.BoxBatch(materials.Length);
            const int Tiers = 14;
            const float Front = 31f, Depth = 1.5f, Rise = 0.95f, Bottom = 1.2f, HalfWidth = 92f;
            const float Section = 12f, Aisle = 1.4f;
            // 前の壁と広告の板。
            batch.Add(new Vector3(0f, 0.7f, Front - 0.6f), new Vector3(HalfWidth * 2f, 1.4f, 0.3f), Concrete);
            for (float x = -HalfWidth + 4f; x < HalfWidth; x += 8f)
            {
                batch.Add(new Vector3(x, 0.75f, Front - 0.78f), new Vector3(7.4f, 0.9f, 0.05f), ((int)((x + HalfWidth) / 8f) % 2 == 0) ? AdA : AdB);
            }
            for (int tier = 0; tier < Tiers; tier++)
            {
                float top = Bottom + tier * Rise;
                float z = Front + tier * Depth;
                // 段（床）。
                batch.Add(new Vector3(0f, top * 0.5f, z + Depth * 0.5f), new Vector3(HalfWidth * 2f, top, Depth), Concrete);
                for (float sx = -HalfWidth; sx < HalfWidth - Section * 0.5f; sx += Section)
                {
                    float left = sx + Aisle * 0.5f;
                    float right = Mathf.Min(sx + Section, HalfWidth) - Aisle * 0.5f;
                    int sectionIndex = Mathf.RoundToInt((sx + HalfWidth) / Section);
                    // 座席の色: 真ん中の区画は赤、ほかは青と水色をかわるがわる、上の 3 段は白（模様）。
                    int seat = Mathf.Abs(sx + Section * 0.5f) < Section ? SeatRed : tier >= Tiers - 3 ? SeatWhite : sectionIndex % 2 == 0 ? SeatBlue : SeatSky;
                    // 座面と背もたれ（区画の長さの帯）。
                    batch.Add(new Vector3((left + right) * 0.5f, top + 0.22f, z + 0.55f), new Vector3(right - left, 0.12f, 0.5f), seat);
                    batch.Add(new Vector3((left + right) * 0.5f, top + 0.45f, z + 0.85f), new Vector3(right - left, 0.5f, 0.1f), seat);
                    // 観客（6 割ほど埋まる。体と頭）。
                    for (float x = left + 0.35f; x < right - 0.2f; x += 0.68f)
                    {
                        if (decor.Random.NextDouble() > 0.6)
                        {
                            continue;
                        }
                        int shirt = ShirtFirst + decor.Random.Next(Shirts);
                        float lean = decor.Range(-0.06f, 0.06f);
                        float height = decor.Range(0.5f, 0.62f);
                        var body = new Vector3(x + lean, top + 0.28f + height * 0.5f, z + 0.62f);
                        batch.Add(body, new Vector3(0.42f, height, 0.3f), shirt);
                        bool standing = decor.Random.NextDouble() < 0.08;
                        batch.Add(body + new Vector3(0f, height * 0.5f + 0.13f + (standing ? 0.3f : 0f), 0f), new Vector3(0.21f, 0.24f, 0.22f),
                            decor.Random.NextDouble() < 0.3 ? SkinDark : Skin);
                    }
                }
                // 通路の階段（区画の間の、段の半分の高さの小さな段）。
                for (float sx = -HalfWidth + Section; sx < HalfWidth; sx += Section)
                {
                    batch.Add(new Vector3(sx, top + Rise * 0.25f, z + Depth * 0.25f), new Vector3(Aisle * 0.8f, Rise * 0.5f, Depth * 0.5f), Concrete);
                }
            }
            float back = Front + Tiers * Depth;
            float roofY = Bottom + Tiers * Rise + 7.5f;
            // 後ろの壁と、屋根を支える柱、屋根（奥から手前へ少し下がる）と鉄骨の梁、屋根の下の照明。
            batch.Add(new Vector3(0f, roofY * 0.5f, back + 0.3f), new Vector3(HalfWidth * 2f + 2f, roofY, 0.6f), Concrete);
            for (float x = -HalfWidth; x <= HalfWidth + 0.1f; x += 18.4f)
            {
                batch.Add(new Vector3(x, roofY * 0.5f, back - 0.4f), new Vector3(0.8f, roofY, 0.8f), Steel);
                // 片持ちの梁（奥の柱から手前の屋根の先まで）。
                batch.Add(new Vector3(x, roofY + 0.9f, back - 9f), new Vector3(0.5f, 1.4f, 18f), Steel, 0f, true);
            }
            batch.Add(new Vector3(0f, roofY + 0.2f, back - 9f), new Vector3(HalfWidth * 2f + 2f, 0.4f, 18.4f), Steel, 0f, true);
            for (float x = -HalfWidth + 3f; x < HalfWidth; x += 6f)
            {
                batch.Add(new Vector3(x, roofY - 0.25f, back - 17.4f), new Vector3(2.2f, 0.25f, 0.6f), RoofLamp, 0f, true);
            }
            batch.Place(decor, "Stadium", "Grandstand", materials);

            // 両端の照明塔（スタンドの外。格子の灯りが光る）。
            Material tower = decor.Lit("TowerSteel", new Color(0.4f, 0.42f, 0.46f));
            Material lamp = decor.Unlit("TowerLamp", new Color(3f, 2.9f, 2.5f));
            foreach (float side in new[] { -1f, 1f })
            {
                var foot = new Vector3(side * 104f, 0f, 60f);
                decor.Box("LightTower", foot + new Vector3(0f, 21f, 0f), new Vector3(1.4f, 42f, 1.4f), tower);
                decor.Box("LightTowerHead", foot + new Vector3(-side * 1f, 44f, -1f), new Vector3(12f, 7f, 0.8f), tower);
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        decor.Box("LightTowerLamp", foot + new Vector3(-side * 1f - 4.6f + col * 2.3f, 42f + row * 2.1f, -1.5f), new Vector3(1.8f, 1.5f, 0.3f), lamp);
                    }
                }
            }
        }

        private static void Decorate(StageDecor decor)
        {
            Material lane = decor.Unlit("LaneLine", new Color(1f, 1f, 1f));
            Material grass = TerrainSculpt.GroundMaterial(decor, "GrassGround", new Color(0.2f, 0.42f, 0.2f), TerrainSculpt.Photos.Grass,
                new Color(0.25f, 0.55f, 0.25f), TerrainSculpt.Photos.Grass, 0.3f);
            Material light = decor.Unlit("Floodlight", new Color(1f, 0.98f, 0.85f));
            Material screen = decor.Unlit("ScoreScreen", new Color(0.15f, 0.9f, 0.5f));
            Material bar = decor.Lit("HighJumpBar", new Color(1f, 0.85f, 0.2f));
            Material gold = decor.Lit("Gold", new Color(1f, 0.8f, 0.25f));

            // 奥の芝生と、トラックのレーンの線。
            decor.Box("Infield", new Vector3(0f, -0.4f, 16f), new Vector3(160f, 0.8f, 26f), grass);
            for (int i = 0; i < 4; i++)
            {
                decor.Box("Lane", new Vector3(0f, 0.02f, -1.5f + i * 1f), new Vector3(152f, 0.02f, 0.08f), lane);
            }
            // 奥のスタンド（2026-11-18 の要望: 背景を精巧に）。
            Grandstand(decor);
            RacingProps(decor);
            // 照明塔の灯り、得点板の画面、走り高跳びのバー、表彰台の金。
            decor.Box("FloodlampWest", new Vector3(-32.7f, 25.5f, 0.6f), new Vector3(5f, 1.6f, 0.4f), light);
            decor.Box("FloodlampEast", new Vector3(32.7f, 25.5f, 0.6f), new Vector3(5f, 1.6f, 0.4f), light);
            decor.Quad("Screen", new Vector3(0f, 30.5f, -2.05f), new Vector2(14f, 4f), screen);
            // 棒高跳びのボックスと立てたポール（左右対称）。
            foreach (float x in new[] { -22f, 22f })
            {
                decor.Box("VaultBox", new Vector3(x, 0.05f, 1.4f), new Vector3(2f, 0.1f, 0.8f), bar);
                GameObject pole = decor.Cylinder("VaultPole", new Vector3(x, 3.5f, 2.2f), new Vector3(0.12f, 3.5f, 0.12f), bar);
                pole.transform.rotation = Quaternion.Euler(0f, 0f, x < 0f ? -12f : 12f);
            }
            decor.Box("PodiumGold", new Vector3(0f, 2.41f, 0f), new Vector3(3f, 0.04f, 4f), gold);
            // 前の簡易な雲（白い球）は消した（2026-11-18 の要望。雲は空に描く）。
        }
    }
}
