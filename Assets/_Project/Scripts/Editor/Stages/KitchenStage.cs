using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// キッチン（SWEET KITCHEN。PASTRY CHEF のテーマのステージ。ユーザーの案、2026-10-26）。左右の調理台、真ん中のコンロと奥の大きなオーブン、
    /// 上の棚とケーキスタンド。調理台から落ちると撃破。
    /// 固有のギミック（ユーザーの案）: オーブンの火柱（40 秒ごとに奥のオーブンから手前へ噴き出す。危険。3 秒前に予告）、ロールケーキ
    /// （左右のステージの外から 1 つずつ出て、交差するように山なりの弧を描いて、反対の外へ去る。乗れて、ワイヤーがつながる動く足場。
    /// 乗ったまま外へ運ばれると撃破）、シンクの排水溝（貯水槽の右から入ると、水流で右の棚の上まで流される。ユーザーの要望、2026-10-26）。
    /// 追加の案（残したもの）: トースター、シンク（水。5 秒で溺れる）、バター（氷）、カップケーキのベルトコンベア、やかんの湯気、ゼリーの
    /// ジャンプ台（まな板とボウルの上）、ミキサーの渦（上昇気流）、ドーナツの輪（加速リング）、吊るしたおたま（アンカー）。
    /// 消したもの（ユーザーの要望、2026-10-26）: プリンのバンパー、調理台の端のゼリーのジャンプ台、蛇口の水（下向きの風）。
    /// </summary>
    internal static class KitchenStage
    {
        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.94f, 0.9f, 0.86f),
                Attachable = new Color(0.62f, 0.42f, 0.3f),
                Anchor = new Color(0.85f, 0.85f, 0.9f),
                SkyTint = new Color(1f, 0.82f, 0.7f),
                Ground = new Color(0.5f, 0.4f, 0.35f),
                Exposure = 1.25f,
                AtmosphereThickness = 0.7f,
                Light = new Color(1f, 0.93f, 0.82f),
                LightIntensity = 1.2f,
                LightEuler = new Vector3(48f, -25f, 0f),
                AmbientSky = new Color(0.85f, 0.75f, 0.7f),
                AmbientEquator = new Color(0.7f, 0.6f, 0.55f),
                AmbientGround = new Color(0.45f, 0.36f, 0.32f),
                FogColor = new Color(0.95f, 0.85f, 0.78f),
                FogStart = 45f,
                FogEnd = 140f,
            };
            theme.ZoneColors[StageZoneKind.Hazard] = new Color(1f, 0.45f, 0.1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.65f, 0.25f);
            theme.ZoneColors[StageZoneKind.Water] = new Color(0.45f, 0.75f, 0.95f);
            theme.ZoneColors[StageZoneKind.Ice] = new Color(1f, 0.93f, 0.5f);
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(1f, 0.55f, 0.75f);
            theme.ZoneColors[StageZoneKind.Gust] = new Color(0.95f, 0.97f, 1f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(1f, 0.8f, 0.35f);
            theme.ZoneColors[StageZoneKind.JumpPad] = new Color(0.55f, 1f, 0.6f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(1f, 0.9f, 0.95f);
            const float rollTravel = 22f;
            const float rollPeriod = 30f;
            // ロールケーキ: 前の横 1.5 倍・縦 2 倍（ユーザーの要望、2026-10-26）。
            var rollSize = new Vector2(7.5f, 4.8f);
            var drainFlow = new Color(0.35f, 0.75f, 1f);

            return new StageLayout
            {
                Id = "Kitchen",
                Music = "Kitchen",
                DisplayName = "SWEET KITCHEN",
                Description = "The oven blasts fire every 40 seconds. Ride the roll cakes across the sky, and dive into the sink drain to be flushed up to the shelf.",
                DescriptionJa = "40秒ごとにオーブンから火柱が噴き出す。空を渡るロールケーキに乗り、シンクの排水溝に飛び込めば棚の上まで流される。",
                Color = new Color(1f, 0.6f, 0.75f),
                Terrain = new[]
                {
                    // 左の調理台（大理石の天板と、ワイヤーがつながる木の戸棚）。
                    new TerrainBox("CounterTopWest", -48f, 1f, -26f, 2f),
                    new TerrainBox("CabinetWest", -47f, -30f, -27f, 1f, attachable: true),
                    // 右の調理台（真ん中にシンク。シンクの右の排水溝は、調理台の下を通って右の端で上へ曲がり、棚の上に出る）。
                    new TerrainBox("CounterTopEastA", 26f, 1f, 34f, 2f),
                    new TerrainBox("CabinetEastA", 27f, -30f, 34f, 1f, attachable: true),
                    new TerrainBox("SinkBottom", 34f, -30f, 42f, -6f),
                    new TerrainBox("CounterTopEastB", 42f, 1f, 46.4f, 2f),
                    new TerrainBox("CabinetEastB", 42f, -30f, 49.8f, -2.4f, attachable: true),
                    new TerrainBox("DrainInnerWall", 46f, 2f, 46.4f, 14f),
                    new TerrainBox("DrainOuterWall", 49.4f, -2.4f, 49.8f, 18.2f),
                    new TerrainBox("DrainCap", 43f, 17.6f, 49.8f, 18.2f),
                    // 真ん中のコンロ（鉄。オーブンの火柱の下）。
                    new TerrainBox("Stove", -9f, -30f, 9f, 0f, attachable: true),
                    // 上の棚とケーキスタンド（棚は外側へずらし、やかんの湯気とトースターで飛んでも頭がぶつからない。ユーザーの要望、2026-10-26）。
                    new TerrainBox("ShelfWest", -46.4f, 14f, -37.5f, 14.6f, attachable: true),
                    new TerrainBox("ShelfEast", 37.5f, 14f, 46.4f, 14.6f, attachable: true),
                    new TerrainBox("CakeStandBase", -6f, 20f, 6f, 20.6f),
                    new TerrainBox("CakeStandTop", -3f, 25f, 3f, 25.5f),
                    // 調理台とコンロの間の、まな板とボウル（小さな足場）。
                    new TerrainBox("CuttingBoard", -21f, 6f, -15f, 6.5f, attachable: true),
                    new TerrainBox("MixingBowl", 15f, 6f, 21f, 6.5f, attachable: true),
                },
                // 吊るしたおたまとフライ返し。
                Anchors = new[]
                {
                    new Vector2(-15f, 25f), new Vector2(15f, 25f), new Vector2(0f, 36f), new Vector2(-28f, 27f), new Vector2(28f, 27f),
                    new Vector2(-42f, 23f), new Vector2(42f, 23f), new Vector2(-18f, 16f), new Vector2(18f, 16f),
                },
                BoundsMin = new Vector2(-60f, -12f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // オーブンの火柱（ユーザーの案）: 40 秒ごとに 3 秒、奥のオーブンから手前へ（コンロの上の全体。3 秒前に予告）。当たると威力 25 の
                    // 枚数のコインを落とし、上へ吹き飛ぶ。始まって 30 秒は噴かない。
                    new ZoneSpec("OvenFire", StageZoneKind.Hazard, new Vector2(0f, 8.5f), new Vector2(22f, 15f), Vector2.up, 25f, 26f)
                        .Every(40f, 3f, 10f, 0f, 3f).Column(22f).Styled(StageZoneView.ZoneLook.Flame),
                    // トースター: 7 秒ごとにポンと跳ね上げる（1.5 秒前に予告）。
                    new ZoneSpec("Toaster", StageZoneKind.Launcher, new Vector2(-35.5f, 3.2f), new Vector2(3f, 2.4f), Vector2.up, 30f).Every(7f, 0.5f, 3f, 0f, 1.5f),
                    // シンク（水）。
                    new ZoneSpec("Sink", StageZoneKind.Water, new Vector2(38f, -3f), new Vector2(8f, 6f), Vector2.zero, 5f, 20f),
                    // 排水溝の水流（ユーザーの要望、2026-10-26）: 貯水槽の右の口から調理台の下を右へ、右の端で上へ、上で左へ流して棚の上に出す。
                    new ZoneSpec("DrainIn", StageZoneKind.Gust, new Vector2(45.3f, -0.7f), new Vector2(8.2f, 3.4f), Vector2.right, 90f, 14f)
                        .Styled(StageZoneView.ZoneLook.Flow).Tinted(drainFlow),
                    new ZoneSpec("DrainUp", StageZoneKind.Gust, new Vector2(47.9f, 6.9f), new Vector2(3f, 18.6f), Vector2.up, 100f, 20f)
                        .Styled(StageZoneView.ZoneLook.Flow).Tinted(drainFlow),
                    new ZoneSpec("DrainOut", StageZoneKind.Gust, new Vector2(46.2f, 16.1f), new Vector2(6.4f, 3f), Vector2.left, 90f, 14f)
                        .Styled(StageZoneView.ZoneLook.Flow).Tinted(drainFlow),
                    // バター（氷。滑る）。
                    ZoneSpec.OnFloor("Butter", StageZoneKind.Ice, -31f, -27f, 2f, Vector2.zero, 0.06f),
                    // カップケーキのベルトコンベア（シンクとは逆、真ん中へ運ぶ）。
                    ZoneSpec.OnFloor("CupcakeConveyor", StageZoneKind.SpeedLane, 26.5f, 31f, 2f, Vector2.left, 11f),
                    // やかんの湯気: 6 秒ごとに 2 秒、上へ。
                    new ZoneSpec("KettleSteam", StageZoneKind.Gust, new Vector2(33f, 7f), new Vector2(2f, 10f), Vector2.up, 60f, 16f).Every(6f, 2f, 1f, 0f, 1f),
                    // ゼリーのジャンプ台（まな板とボウルの上）。
                    ZoneSpec.OnFloor("JellyBoard", StageZoneKind.JumpPad, -19f, -17f, 6.5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("JellyBowl", StageZoneKind.JumpPad, 17f, 19f, 6.5f, Vector2.up, 24f, 0.4f),
                    // ドーナツの輪（加速リング）: 調理台の上の空から真ん中へ。
                    new ZoneSpec("DonutWest", StageZoneKind.BoostRing, new Vector2(-26f, 15f), new Vector2(2.4f, 3f), new Vector2(1f, 0.3f), 19f),
                    new ZoneSpec("DonutEast", StageZoneKind.BoostRing, new Vector2(26f, 15f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.3f), 19f),
                    // ミキサーの渦（左の棚の上から上へ）。
                    new ZoneSpec("MixerWhirl", StageZoneKind.Updraft, new Vector2(-42f, 19f), new Vector2(4f, 9f), Vector2.up, 45f, 12f),
                },
                Platforms = new[]
                {
                    // ロールケーキ（ユーザーの案）: 左右の外から同時に出て、どちらも山なりの弧を描いて（高い弧と低い弧）真ん中で交差し、反対の外へ
                    // 去る（22 秒で渡り、8 秒いない）。乗れて、ワイヤーがつながる。高い弧は画面の端の約 19m から真ん中の 39.5m まで、低い弧は
                    // 約 20m から 30.5m まで上がる（前は端との差が小さく、まっすぐ飛んで見えた。ユーザーの要望、2026-10-26）。真ん中では、
                    // 低い弧もケーキスタンドの上を通る。排水溝のふたと棚の上に立つ人には当たらない高さ。
                    PlatformSpec.Arc("RollCakeWest", rollSize, true, new Vector2(-72f, 10f), new Vector2(0f, 69f), new Vector2(72f, 10f), rollTravel, rollPeriod)
                        .Looks(RollCakeLooks),
                    PlatformSpec.Arc("RollCakeEast", rollSize, true, new Vector2(72f, 16f), new Vector2(0f, 45f), new Vector2(-72f, 16f), rollTravel, rollPeriod)
                        .Looks(RollCakeLooks),
                },
                Coins = new (Vector2, int)[]
                {
                    // 調理台の上。
                    (new Vector2(-46f, 4.5f), 1), (new Vector2(-41f, 4f), 1), (new Vector2(-32f, 4f), 1), (new Vector2(-29f, 3.5f), 1),
                    (new Vector2(29f, 3.5f), 1), (new Vector2(32.5f, 4f), 1), (new Vector2(44f, 4f), 1),
                    // トースターの上と、やかんの湯気の上。
                    (new Vector2(-35.5f, 8f), 1), (new Vector2(-35.5f, 12.5f), 3), (new Vector2(33f, 13f), 3),
                    // シンクの中（潜る）と、その上。
                    (new Vector2(38f, -4.5f), 20), (new Vector2(38f, 3.5f), 1), (new Vector2(36f, 1f), 1), (new Vector2(40f, 1f), 1),
                    // 排水溝の中（上へ流される途中の 20 枚と、調理台の下の 3 枚）。
                    (new Vector2(47.9f, 10f), 20), (new Vector2(44.5f, -0.8f), 3),
                    // コンロの上（オーブンの火柱が噴く所）。
                    (new Vector2(-6f, 2f), 1), (new Vector2(0f, 2f), 3), (new Vector2(6f, 2f), 1), (new Vector2(-4f, 7f), 1), (new Vector2(4f, 7f), 1),
                    (new Vector2(0f, 11f), 20), (new Vector2(-8f, 13f), 1), (new Vector2(8f, 13f), 1),
                    // まな板とボウルの上と、ジャンプ台の上。
                    (new Vector2(-20.5f, 8.5f), 1), (new Vector2(-15.5f, 8.5f), 1), (new Vector2(15.5f, 8.5f), 1), (new Vector2(20.5f, 8.5f), 1),
                    (new Vector2(-18f, 14.5f), 1), (new Vector2(18f, 14.5f), 1), (new Vector2(-22.5f, 11f), 1), (new Vector2(22.5f, 11f), 1),
                    // 棚の上とミキサーの渦。
                    (new Vector2(-45f, 16.5f), 1), (new Vector2(-39f, 16.5f), 1), (new Vector2(-42f, 26f), 3), (new Vector2(-42f, 28.5f), 20),
                    (new Vector2(45f, 16.5f), 1), (new Vector2(39f, 16.5f), 1), (new Vector2(42f, 19.5f), 3),
                    // ケーキスタンドの上と、その上の空。
                    (new Vector2(-4.5f, 22.5f), 1), (new Vector2(4.5f, 22.5f), 1), (new Vector2(-6f, 27f), 3),
                    // ロールケーキの道（低い弧の上と、高い弧の上）。
                    (new Vector2(-18f, 34.5f), 1), (new Vector2(18f, 34.5f), 1), (new Vector2(-8f, 34.8f), 3), (new Vector2(8f, 34.8f), 3),
                    (new Vector2(-30f, 39.6f), 1), (new Vector2(30f, 39.6f), 1),
                    // 調理台の下（戸棚の前。落ちる前に届く）。
                    (new Vector2(-24f, -3f), 20), (new Vector2(24f, -3f), 20), (new Vector2(-12f, -4f), 3), (new Vector2(12f, -4f), 3),
                    // 高い所とステージの端。
                    (new Vector2(-25f, 42.5f), 20), (new Vector2(25f, 42.5f), 20), (new Vector2(-53f, 10f), 20),
                    (new Vector2(0f, 44f), 20), (new Vector2(-52f, 24f), 3), (new Vector2(52f, 24f), 3),
                    (new Vector2(-16f, 22f), 20), (new Vector2(16f, 22f), 20), (new Vector2(-33f, 27f), 20), (new Vector2(33f, 27f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 35.5f), new Vector2(-50f, 22f), new Vector2(50f, 22f), new Vector2(38f, -5f), new Vector2(0f, 5f),
                    new Vector2(-24f, -6f), new Vector2(24f, -6f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 17f), new Vector2(-42.5f, 16.6f), new Vector2(42.5f, 16.6f), new Vector2(-18f, 3f), new Vector2(18f, 3f),
                    new Vector2(0f, 27f), new Vector2(-30f, 26f), new Vector2(30f, 26f),
                },
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-41.5f, 3f, 0f), new Vector3(43.5f, 3f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-29.75f, 3f, 0f), new Vector3(-38.5f, 15.6f, 0f), new Vector3(38.5f, 15.6f, 0f), new Vector3(47.5f, 19.2f, 0f) },
                Decorate = Decorate,
                // 背景とオブジェクトのクオリティアップ（D-97）。
                Look = KitchenLook(theme),
            };
        }

        // ロールケーキ（2026-11-20 の要望で精巧に）: 焼き色の付いたスポンジの筒を真横に寝かせ（斜めは違和感があった。2026-11-20 の要望）、
        // 両端の面にクリームの渦巻き。上にクリームの帯と絞り、いちご、ミントの葉。
        private static void RollCakeLooks(StageDecor decor, Transform visual, PlatformSpec spec)
        {
            const string stage = "Kitchen";
            float radius = spec.Size.y * 0.5f;
            float length = spec.Size.x * 0.96f;
            Material skin = decor.Lit("RollSkin", new Color(0.72f, 0.42f, 0.18f));
            Material sponge = decor.Lit("RollSponge", new Color(1f, 0.82f, 0.5f));
            Material cream = decor.Lit("RollCream", new Color(1f, 0.98f, 0.94f));
            cream.SetFloat("_Smoothness", 0.35f);
            Material berry = decor.Lit("RollStrawberry", new Color(0.86f, 0.08f, 0.12f));
            berry.SetFloat("_Smoothness", 0.75f);
            Material leaf = decor.Lit("RollMint", new Color(0.25f, 0.65f, 0.3f));
            var cake = new GameObject("Cake").transform;
            cake.SetParent(visual, false);
            cake.localRotation = Quaternion.identity;
            // 筒（軸は X）: 外側の焼き色。
            ProceduralMesh.Place("Skin", cake, ProceduralMesh.Lathe(stage, "RollCakeSkin",
                new[] { new Vector2(radius * 0.97f, -length * 0.5f), new Vector2(radius, -length * 0.48f), new Vector2(radius, length * 0.48f),
                    new Vector2(radius * 0.97f, length * 0.5f) }, 36), Vector3.zero, Quaternion.Euler(0f, 0f, 90f), Vector3.one, skin);
            // 端の面: スポンジの円と、クリームの渦巻き（両端）。
            Mesh swirl = ProceduralMesh.Spiral(stage, "RollCakeSwirl", radius * 0.06f, radius * 0.86f, 3.2f, radius * 0.16f, 160);
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * (length * 0.5f + 0.01f);
                GameObject disc = decor.Cylinder("Face", cake, new Vector3(x, 0f, 0f), new Vector3(radius * 1.9f, 0.01f, radius * 1.9f), sponge);
                disc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                // 渦巻きの表（-Z）を端の面の外へ向ける。
                ProceduralMesh.Place("Swirl", cake, swirl, new Vector3(x + side * 0.02f, 0f, 0f), Quaternion.Euler(0f, side > 0f ? -90f : 90f, 0f), Vector3.one, cream);
            }
            // 上のクリームの帯と絞り、いちごとミント（上の面に沿って並べる）。
            decor.Box("CreamBand", cake, new Vector3(0f, radius * 0.96f, 0f), new Vector3(length * 0.9f, 0.18f, radius * 0.7f), cream);
            for (int i = 0; i < 5; i++)
            {
                float x = -length * 0.36f + i * length * 0.18f;
                decor.Sphere("Dollop", cake, new Vector3(x, radius + 0.18f, 0f), new Vector3(0.75f, 0.5f, 0.75f), cream);
                decor.Sphere("DollopTip", cake, new Vector3(x, radius + 0.45f, 0f), new Vector3(0.3f, 0.35f, 0.3f), cream);
                if (i % 2 == 0)
                {
                    decor.Sphere("Strawberry", cake, new Vector3(x + 0.3f, radius + 0.55f, -0.25f), new Vector3(0.55f, 0.65f, 0.55f), berry);
                    GameObject mint = decor.Box("Mint", cake, new Vector3(x + 0.3f, radius + 0.92f, -0.25f), new Vector3(0.35f, 0.05f, 0.2f), leaf);
                    mint.transform.localRotation = Quaternion.Euler(0f, 30f, 20f);
                }
            }
        }

        // 見た目の台本: 室内。窓の外に実在の西新宿の街並み（2026-11-18 の要望: 窓に窓枠と鍵を付けて、外にビル街の街並みが見えるように）。
        private static StageLook KitchenLook(StageTheme theme)
        {
            StageLook look = StageLook.FromTheme("Kitchen", theme, StageMood.Indoor);
            look.WindowCity = true;
            // 壁のすぐ後ろ（手前の端が壁の裏 z = 17 より奥）。地面は窓より下で、高いビルの上の階が窓の真ん中ほどに来る高さ。
            look.WindowCityCenter = new Vector3(0f, 8f, 67f);
            return look;
        }

        // 窓の中心と大きさ（奥の壁の手前の面 z = 15）。
        private static readonly Vector2[] WindowCenters = { new Vector2(-30f, 26f), new Vector2(30f, 26f) };
        private static readonly Vector2 WindowSize = new Vector2(12f, 9f);

        // 奥の壁（z 15〜17）の広さ。
        private const float WallHalfWidth = 200f;
        private const float WallBottom = -60f;
        private const float WallTop = 120f;

        // 奥の壁を窓の所だけくりぬいた箱で作り、タイルの目地の線も窓をよける。
        private static void BackWall(StageDecor decor, Material tile, Material tileLine)
        {
            float bottom = WindowCenters[0].y - WindowSize.y * 0.5f;
            float top = WindowCenters[0].y + WindowSize.y * 0.5f;
            void Wall(string name, float x0, float x1, float y0, float y1)
            {
                decor.Box(name, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 16f), new Vector3(x1 - x0, y1 - y0, 2f), tile);
            }
            Wall("BackWall", -WallHalfWidth, WallHalfWidth, WallBottom, bottom);
            Wall("BackWallTop", -WallHalfWidth, WallHalfWidth, top, WallTop);
            // 窓の高さの帯: 窓と窓の間と、両外。
            var edges = new System.Collections.Generic.List<float> { -WallHalfWidth };
            foreach (Vector2 center in WindowCenters)
            {
                edges.Add(center.x - WindowSize.x * 0.5f);
                edges.Add(center.x + WindowSize.x * 0.5f);
            }
            edges.Add(WallHalfWidth);
            for (int i = 0; i + 1 < edges.Count; i += 2)
            {
                Wall($"BackWallBand{i / 2 + 1}", edges[i], edges[i + 1], bottom, top);
            }
            for (float y = -56f; y <= WallTop - 4f; y += 4f)
            {
                if (y <= bottom || y >= top)
                {
                    decor.Box("TileLine", new Vector3(0f, y, 14.9f), new Vector3(WallHalfWidth * 2f, 0.08f, 0.1f), tileLine);
                    continue;
                }
                for (int i = 0; i + 1 < edges.Count; i += 2)
                {
                    decor.Box("TileLine", new Vector3((edges[i] + edges[i + 1]) * 0.5f, y, 14.9f), new Vector3(edges[i + 1] - edges[i], 0.08f, 0.1f), tileLine);
                }
            }
        }

        // 引き違いの窓（日本の家の窓）: アルミの窓枠（壁の厚みを埋める）、左右 2 枚の障子（左は外のレール、右は内のレールで、真ん中で
        // 重なる）、ガラス（ほとんど透明。斜めの映り込み）、重なりの所のクレセントの鍵（右の障子の框に座と弧の形のレバーとつまみ、左の
        // 障子の框に受け）、窓台。外の街は StageDressing の窓の外の街（StageLook.WindowCity）。
        private static void Window(StageDecor decor, Vector2 center)
        {
            Material frame = decor.Lit("WindowFrame", new Color(0.8f, 0.82f, 0.84f));
            Material sash = decor.Lit("WindowSash", new Color(0.7f, 0.72f, 0.75f));
            Material chrome = decor.Lit("WindowLock", new Color(0.93f, 0.93f, 0.95f));
            Material glass = decor.Transparent("WindowGlass", new Color(0.82f, 0.92f, 1f, 0.08f));
            Material glare = decor.Transparent("WindowGlare", new Color(1f, 1f, 1f, 0.13f));
            Material sill = decor.Lit("WindowSill", new Color(0.95f, 0.93f, 0.9f));
            const float frameWidth = 0.45f;
            const float frameFront = 14.5f;
            const float frameBack = 17f;
            const float bar = 0.32f;
            const float overlap = 0.12f;
            float w = WindowSize.x;
            float h = WindowSize.y;
            float frameZ = (frameFront + frameBack) * 0.5f;
            float frameDepth = frameBack - frameFront;
            string side = center.x < 0f ? "West" : "East";

            // 窓枠（上下左右。壁の厚みを埋める）。
            decor.Box($"Window{side}FrameTop", new Vector3(center.x, center.y + h * 0.5f - frameWidth * 0.5f, frameZ), new Vector3(w, frameWidth, frameDepth), frame);
            decor.Box($"Window{side}FrameBottom", new Vector3(center.x, center.y - h * 0.5f + frameWidth * 0.5f, frameZ), new Vector3(w, frameWidth, frameDepth), frame);
            decor.Box($"Window{side}FrameLeft", new Vector3(center.x - w * 0.5f + frameWidth * 0.5f, center.y, frameZ), new Vector3(frameWidth, h, frameDepth), frame);
            decor.Box($"Window{side}FrameRight", new Vector3(center.x + w * 0.5f - frameWidth * 0.5f, center.y, frameZ), new Vector3(frameWidth, h, frameDepth), frame);
            // 下のレール（2 本）。
            float innerLeft = center.x - w * 0.5f + frameWidth;
            float innerRight = center.x + w * 0.5f - frameWidth;
            float innerBottom = center.y - h * 0.5f + frameWidth;
            float innerTop = center.y + h * 0.5f - frameWidth;
            decor.Box($"Window{side}RailOuter", new Vector3(center.x, innerBottom + 0.05f, 15.9f), new Vector3(innerRight - innerLeft, 0.1f, 0.06f), sash);
            decor.Box($"Window{side}RailInner", new Vector3(center.x, innerBottom + 0.05f, 15.45f), new Vector3(innerRight - innerLeft, 0.1f, 0.06f), sash);

            // 障子 2 枚（左が外のレール、右が内のレール）。
            float sashWidth = (innerRight - innerLeft + overlap) * 0.5f;
            float sashHeight = innerTop - innerBottom;
            (string Name, float Left, float Z)[] sashes =
            {
                ("Outer", innerLeft, 15.9f),
                ("Inner", innerRight - sashWidth, 15.45f),
            };
            foreach (var s in sashes)
            {
                float cx = s.Left + sashWidth * 0.5f;
                float cy = innerBottom + sashHeight * 0.5f;
                string name = $"Window{side}Sash{s.Name}";
                decor.Box($"{name}Top", new Vector3(cx, innerTop - bar * 0.5f, s.Z), new Vector3(sashWidth, bar, 0.28f), sash);
                decor.Box($"{name}Bottom", new Vector3(cx, innerBottom + bar * 0.5f, s.Z), new Vector3(sashWidth, bar, 0.28f), sash);
                decor.Box($"{name}Left", new Vector3(s.Left + bar * 0.5f, cy, s.Z), new Vector3(bar, sashHeight, 0.28f), sash);
                decor.Box($"{name}Right", new Vector3(s.Left + sashWidth - bar * 0.5f, cy, s.Z), new Vector3(bar, sashHeight, 0.28f), sash);
                var pane = new Vector2(sashWidth - bar * 2f, sashHeight - bar * 2f);
                decor.Quad($"{name}Glass", new Vector3(cx, cy, s.Z), pane, glass);
                // 斜めの映り込み（左上に太い筋と細い筋）。ガラスの中に収まる長さ。
                GameObject wide = decor.Quad($"{name}Glare", new Vector3(cx - pane.x * 0.12f, cy + pane.y * 0.12f, s.Z - 0.02f), new Vector2(pane.x * 0.95f, 0.55f), glare);
                wide.transform.rotation = Quaternion.Euler(0f, 0f, 35f);
                GameObject thin = decor.Quad($"{name}GlareThin", new Vector3(cx + pane.x * 0.08f, cy + pane.y * 0.02f, s.Z - 0.02f), new Vector2(pane.x * 0.8f, 0.16f), glare);
                thin.transform.rotation = Quaternion.Euler(0f, 0f, 35f);
            }

            // クレセントの鍵: 右の障子（内）の左の框に座と、左へふくらむ弧のレバーとつまみ。左の障子（外）の右の框に受け。
            float meet = innerRight - sashWidth + bar * 0.5f;
            decor.Box($"Window{side}LockBase", new Vector3(meet, center.y, 15.27f), new Vector3(0.26f, 0.8f, 0.1f), chrome);
            const float radius = 0.3f;
            for (int i = 0; i < 7; i++)
            {
                float angle = Mathf.Lerp(100f, 260f, i / 6f) * Mathf.Deg2Rad;
                var position = new Vector3(meet + Mathf.Cos(angle) * radius, center.y + Mathf.Sin(angle) * radius, 15.2f);
                GameObject piece = decor.Box($"Window{side}LockCrescent", position, new Vector3(0.12f, 0.14f, 0.08f), chrome);
                piece.transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
            }
            GameObject lever = decor.Box($"Window{side}LockLever", new Vector3(meet + 0.17f, center.y - 0.2f, 15.2f), new Vector3(0.1f, 0.4f, 0.08f), chrome);
            lever.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            decor.Sphere($"Window{side}LockKnob", new Vector3(meet + 0.3f, center.y - 0.35f, 15.16f), new Vector3(0.2f, 0.2f, 0.16f), chrome);
            // 受けは左の障子（外）の重なりの框の見えている所（右の障子の框の左）。
            decor.Box($"Window{side}LockKeeper", new Vector3(meet - 0.26f, center.y, 15.72f), new Vector3(0.16f, 0.44f, 0.08f), chrome);

            // 窓台（手前へ少し出る）。
            decor.Box($"Window{side}Sill", new Vector3(center.x, center.y - h * 0.5f - 0.2f, 14.6f), new Vector3(w + 1.2f, 0.4f, 1.4f), sill);
        }

        private const string PropFolder = "Assets/_Project/Materials/Stages/Kitchen";

        // 調理台の奥の瓶・まな板と人参・皿立て・ケチャップとマスタード、オーブンの上の鍋、奥の壁の棚。
        private static void Props(StageDecor decor)
        {
            ThirdPartyProps.Pack pack = ThirdPartyProps.Restaurant;
            Color tint = Color.white;
            void Put(string model, float x, float y, float z, float scale, float yaw = 0f)
            {
                ThirdPartyProps.Place(decor, pack, model, new Vector3(x, y, z), scale, yaw, tint, PropFolder);
            }
            // 左の調理台（上の面 y 2、奥行き z 2〜7）。
            Put("jar_A_large", -46f, 2f, 5f, 7f);
            Put("jar_B_large", -42.6f, 2f, 5f, 7f, 20f);
            Put("jar_C_medium", -39.6f, 2f, 5.2f, 7f, -15f);
            Put("cuttingboard", -30.5f, 2f, 4.9f, 4.6f, 4f);
            Put("food_ingredient_carrot_chopped", -30.5f, 2.7f, 4.9f, 4.6f, 4f);
            // 右の調理台。
            Put("dishrack_plates", 29.2f, 2f, 5f, 4.4f, 10f);
            Put("ketchup", 33.6f, 2f, 5.6f, 6f);
            Put("mustard", 35.2f, 2f, 5.6f, 6f, 30f);
            // オーブンの上（上の面 y 17、奥行き z 5〜13）の鍋。
            Put("pot_A_stew", -6.5f, 17f, OvenFront + 4f, 6.5f, 8f);
            Put("pot_B", 6.5f, 17f, OvenFront + 4f, 6.5f, -12f);
            // 奥の壁（手前の面 z 15）の棚（窓より外側）。
            Put("shelf_papertowel_decorated", -52f, 14f, 13f, 6.5f);
            Put("shelf_papertowel_decorated", 52f, 14f, 13f, 6.5f);
            // 家具（Kenney の Furniture Kit。CC0。2026-11-19 にユーザーが追加）: 調理台の外の大きな冷蔵庫、調理台の上の電子レンジと
            // コーヒーメーカー、床の観葉植物。台所の大きさに合わせて大きくする（吊り戸棚は大きくすると平らな板に見えたので置かない）。
            void Furniture(string model, float x, float y, float z, float scale, float yaw = 180f)
            {
                ThirdPartyProps.PlaceMtl(decor, "Kenney/Furniture", model, new Vector3(x, y, z), yaw, scale, Color.white, PropFolder);
            }
            Furniture("kitchenFridgeLarge", -60f, -21f, 9.5f, 24f);
            Furniture("kitchenFridgeLarge", 70f, -21f, 9.5f, 24f);
            Furniture("kitchenMicrowave", -24f, 2f, 7f, 14f);
            Furniture("kitchenCoffeeMachine", 44f, 2f, 6.5f, 14f);
            Furniture("pottedPlant", -64f, -21f, 2f, 14f, 0f);
        }

        // 奥の壁とタイル、大きなオーブン（火柱の口）、コンロのつまみ、窓、調理台の奥行き、吊るしたおたま。
        private static void Decorate(StageDecor decor)
        {
            Material tile = decor.Lit("Tile", new Color(0.92f, 0.95f, 0.97f));
            Material tileLine = decor.Lit("TileLine", new Color(0.78f, 0.82f, 0.86f));
            Material steel = decor.Lit("Steel", new Color(0.75f, 0.77f, 0.8f));
            Material wood = decor.Lit("CabinetWood", new Color(0.62f, 0.42f, 0.3f));
            Material marble = decor.Lit("Marble", new Color(0.96f, 0.94f, 0.92f));
            Material floor = decor.Lit("KitchenFloor", new Color(0.55f, 0.35f, 0.28f));

            // 奥の壁のタイル（窓の所はくりぬく。窓の外に街が見える）。全体を映すカメラでも端と上が見えないように広く高く。
            BackWall(decor, tile, tileLine);
            // 大きなオーブン（コンロの奥。2026-11-20 の要望で精巧に。火柱が噴くと同時に扉が開く）。
            Oven(decor);
            // 調理台の奥行き（大理石と木）。
            decor.Box("CounterBackWest", new Vector3(-37f, -6f, 4.5f), new Vector3(22f, 16f, 5f), wood);
            decor.Box("CounterBackEast", new Vector3(37f, -6f, 4.5f), new Vector3(22f, 16f, 5f), wood);
            decor.Box("MarbleBackWest", new Vector3(-37f, 1.5f, 4.5f), new Vector3(22f, 1f, 5f), marble);
            decor.Box("MarbleBackEast", new Vector3(37f, 1.5f, 4.5f), new Vector3(22f, 1f, 5f), marble);
            // シンク（ステンレスの槽。水は槽の中だけ）と蛇口（台に立ち、下へ水が流れる）。2026-11-20 の要望: 水が手前に浮いて見える、
            // 蛇口が浮いている。
            Sink(decor, steel);
            // やかん（前の赤い球。湯気の出る所）とトースター。
            Kettle(decor, new Vector3(33f, 2f, 1.2f));
            decor.Box("Toaster", new Vector3(-35.5f, 3.2f, 1.2f), new Vector3(3.2f, 2.4f, 2.4f), steel);
            // 排水溝の管（奥の側の半透明の管と、口の格子）と、中を流れる水（2026-11-20 の要望: 水流も水に合わせて精巧に）。
            Material pipeGlass = decor.Transparent("DrainPipe", new Color(0.6f, 0.85f, 1f, 0.16f));
            decor.Box("DrainPipeLow", new Vector3(45.8f, -0.65f, 1.4f), new Vector3(7f, 3.3f, 1.2f), pipeGlass);
            decor.Box("DrainPipeUp", new Vector3(47.9f, 7.65f, 1.4f), new Vector3(2.9f, 19.9f, 1.2f), pipeGlass);
            DrainWater(decor);
            for (int i = 0; i < 4; i++)
            {
                decor.Box("DrainGrate", new Vector3(42.15f, -2f + i * 0.8f, -1.6f), new Vector3(0.12f, 0.12f, 0.6f), steel);
            }
            // 窓（引き違いの窓。窓枠・障子・ガラス・クレセントの鍵・窓台）と床（遠く下）。
            foreach (Vector2 center in WindowCenters)
            {
                Window(decor, center);
            }
            decor.Box("Floor", new Vector3(0f, -22f, 10f), new Vector3(150f, 2f, 40f), floor);
            // 背景の小物（KayKit Restaurant Bits。CC0。2026-11-18 のユーザーの指示で受け取ったアセットを組み込む）。キッチンは人の約 8 倍の
            // 大きさなので、小物も 5〜7 倍で置く。どれも遊ぶ面より奥（z 2.3 より奥）で、当たりはない。
            Props(decor);
            // 吊るしたおたまの柄（アンカーの上）。
            foreach (Vector2 hook in new[] { new Vector2(-15f, 25f), new Vector2(15f, 25f), new Vector2(0f, 36f), new Vector2(-28f, 27f), new Vector2(28f, 27f) })
            {
                decor.Box("LadleHandle", new Vector3(hook.x, hook.y + 3f, 0.6f), new Vector3(0.15f, 6f, 0.15f), steel);
            }
        }

        // オーブンの前の面の奥行き（z）。扉の蝶番は前の面の下の辺。奥の壁（z 15）の手前に収まる。
        private const float OvenFront = 6.4f;

        // オーブン（2026-11-20 の要望: ギミックが発動すると同時にオーブンの口が開き、中から炎が噴き出す。オーブンの見た目も精巧に）:
        // ステンレスの枠（左右・上・下・奥）、上の黒いガラスの操作盤（つまみ 4 つと光る表示）、中の庫内（暗い琺瑯の壁・奥の熱の光・上の
        // ヒーター・網 2 段）、下の辺の蝶番で手前へ倒れる扉（ステンレスの枠・黒いガラス・取っ手）。扉はオーブンの火柱
        // （Gimmicks/OvenFire の StageZoneView）が効いている間だけ開く。
        private static void Oven(StageDecor decor)
        {
            Material stainless = decor.Lit("OvenSteel", new Color(0.72f, 0.74f, 0.78f));
            stainless.SetFloat("_Metallic", 0.85f);
            stainless.SetFloat("_Smoothness", 0.62f);
            Material blackGlass = decor.Lit("OvenBlackGlass", new Color(0.04f, 0.04f, 0.05f));
            blackGlass.SetFloat("_Smoothness", 0.92f);
            Material enamel = decor.Lit("OvenEnamel", new Color(0.12f, 0.1f, 0.1f));
            Material heat = decor.Unlit("OvenHeat", new Color(0.75f, 0.28f, 0.06f));
            Material element = decor.Unlit("OvenElement", new Color(1f, 0.45f, 0.1f));
            Material display = decor.Unlit("OvenDisplay", new Color(1f, 0.4f, 0.08f));
            Material doorGlass = decor.Transparent("OvenDoorGlass", new Color(0.05f, 0.04f, 0.04f, 0.72f));
            Material rack = decor.Lit("OvenRack", new Color(0.8f, 0.82f, 0.85f));
            rack.SetFloat("_Metallic", 0.9f);
            rack.SetFloat("_Smoothness", 0.7f);

            const float depth = 8f;
            float back = OvenFront + depth;
            float midZ = OvenFront + depth * 0.5f;
            // 枠: 左右の側板、上（操作盤の後ろ）、下、奥。庫内は x -10〜10、y 1〜13。
            decor.Box("OvenSideWest", new Vector3(-11.5f, 6f, midZ), new Vector3(3f, 22f, depth), stainless);
            decor.Box("OvenSideEast", new Vector3(11.5f, 6f, midZ), new Vector3(3f, 22f, depth), stainless);
            decor.Box("OvenTop", new Vector3(0f, 15f, midZ), new Vector3(20f, 4f, depth), stainless);
            decor.Box("OvenBottom", new Vector3(0f, -2f, midZ), new Vector3(20f, 6f, depth), stainless);
            decor.Box("OvenKick", new Vector3(0f, -4.6f, OvenFront - 0.05f), new Vector3(26f, 0.8f, 0.2f), blackGlass);
            // 庫内: 暗い琺瑯の壁と、奥の熱の光（扉の黒いガラス越しにほんのり見える）、上のヒーター（ジグザグ）、網 2 段。
            decor.Box("CavityBack", new Vector3(0f, 7f, back - 0.4f), new Vector3(20f, 12f, 0.2f), enamel);
            decor.Quad("CavityHeat", new Vector3(0f, 7f, back - 0.55f), new Vector2(17f, 9f), heat);
            decor.Box("CavityTop", new Vector3(0f, 12.9f, midZ), new Vector3(20f, 0.2f, depth - 0.6f), enamel);
            decor.Box("CavityFloor", new Vector3(0f, 1.1f, midZ), new Vector3(20f, 0.2f, depth - 0.6f), enamel);
            for (int i = 0; i < 6; i++)
            {
                decor.Box("Element", new Vector3(0f, 12.5f, OvenFront + 1.2f + i * 1.1f), new Vector3(17f, 0.18f, 0.18f), element);
            }
            foreach (float y in new[] { 4.6f, 8.6f })
            {
                decor.Box("RackFront", new Vector3(0f, y, OvenFront + 0.9f), new Vector3(19.4f, 0.12f, 0.12f), rack);
                for (int i = 0; i < 14; i++)
                {
                    decor.Box("RackWire", new Vector3(-9f + i * 1.385f, y, midZ + 0.3f), new Vector3(0.08f, 0.08f, depth - 1.8f), rack);
                }
            }
            // 操作盤: 黒いガラスの帯、つまみ 4 つ（目盛りの印つき）、光る表示。
            decor.Box("ControlPanel", new Vector3(0f, 15f, OvenFront - 0.12f), new Vector3(25.6f, 3.4f, 0.25f), blackGlass);
            foreach (float x in new[] { -9.5f, -6f, 6f, 9.5f })
            {
                GameObject knob = decor.Cylinder("Knob", new Vector3(x, 15f, OvenFront - 0.55f), new Vector3(1.5f, 0.3f, 1.5f), stainless);
                knob.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                decor.Box("KnobMark", new Vector3(x, 15.45f, OvenFront - 0.86f), new Vector3(0.12f, 0.45f, 0.04f), blackGlass);
            }
            decor.Box("Display", new Vector3(0f, 15f, OvenFront - 0.26f), new Vector3(5.6f, 1.6f, 0.04f), blackGlass);
            for (int i = 0; i < 3; i++)
            {
                decor.Box("Digit", new Vector3(-1.5f + i * 1.5f, 15f, OvenFront - 0.3f), new Vector3(0.9f, 1.1f, 0.02f), display);
            }
            // 扉（下の辺の蝶番。開くと手前へ倒れる）: ステンレスの枠と黒いガラスの窓、上の取っ手。
            var hinge = new GameObject("OvenDoorHinge").transform;
            hinge.SetParent(decor.Root, false);
            hinge.position = new Vector3(0f, 1f, OvenFront - 0.3f);
            decor.Box("DoorFrameBottom", hinge, new Vector3(0f, 0.8f, 0f), new Vector3(20f, 1.6f, 0.5f), stainless);
            decor.Box("DoorFrameTop", hinge, new Vector3(0f, 10.9f, 0f), new Vector3(20f, 2.2f, 0.5f), stainless);
            decor.Box("DoorFrameWest", hinge, new Vector3(-9.1f, 5.8f, 0f), new Vector3(1.8f, 8.4f, 0.5f), stainless);
            decor.Box("DoorFrameEast", hinge, new Vector3(9.1f, 5.8f, 0f), new Vector3(1.8f, 8.4f, 0.5f), stainless);
            decor.Box("DoorGlass", hinge, new Vector3(0f, 5.8f, 0f), new Vector3(16.4f, 8.4f, 0.12f), doorGlass);
            decor.Cylinder("Handle", hinge, new Vector3(0f, 11.2f, -0.95f), new Vector3(0.45f, 8f, 0.45f), stainless).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            foreach (float x in new[] { -7.4f, 7.4f })
            {
                decor.Box("HandlePost", hinge, new Vector3(x, 11.2f, -0.55f), new Vector3(0.35f, 0.35f, 0.8f), stainless);
            }
            StageZoneView fire = GameObject.Find("Gimmicks/OvenFire")?.GetComponent<StageZoneView>();
            if (fire == null)
            {
                Debug.LogError("オーブンの火柱（Gimmicks/OvenFire）が見つからない。扉が開かない");
                return;
            }
            // 扉はほぼいっぱいまで開く（2026-11-20 の要望）。
            fire.ConfigureHinge(hinge, 84f);
        }

        // シンク（2026-11-20 の要望: 洗面台の水が手前に浮いている → 台を水に合わせて立体的に）: 槽はステンレス（奥の壁・底・左右の内側）、
        // 手前の縁、水の手前の面は調理台の正面と同じ奥行きに、水面は槽の中（奥行き z -2〜2）だけにする。蛇口は奥の台に立ち、首が槽の上へ
        // 曲がって、下へ水が流れる（2026-11-20 の要望: 蛇口が浮いている）。
        private static void Sink(StageDecor decor, Material steel)
        {
            Material basin = decor.Lit("SinkSteel", new Color(0.78f, 0.8f, 0.83f));
            basin.SetFloat("_Metallic", 0.85f);
            basin.SetFloat("_Smoothness", 0.7f);
            Material chrome = decor.Lit("FaucetChrome", new Color(0.86f, 0.88f, 0.92f));
            chrome.SetFloat("_Metallic", 1f);
            chrome.SetFloat("_Smoothness", 0.85f);
            const float left = 34f;
            const float right = 42f;
            const float bottom = -6f;
            const float rim = 2f;
            // 槽: 奥の壁・底・左右の内側の壁（調理台の箱の内側に貼る）。
            // 地形の面と重ならないように、どれも地形から少し離す（面が重なるとちらつく。StageLayoutTests が検査する）。
            decor.Box("BasinBack", new Vector3((left + right) * 0.5f, (bottom + rim) * 0.5f, 2.08f), new Vector3(right - left, rim - bottom, 0.1f), basin);
            decor.Box("BasinFloor", new Vector3((left + right) * 0.5f, bottom + 0.07f, 0f), new Vector3(right - left - 0.3f, 0.1f, 3.9f), basin);
            decor.Box("BasinWallWest", new Vector3(left + 0.08f, (bottom + rim) * 0.5f, 0f), new Vector3(0.1f, rim - bottom, 3.9f), basin);
            decor.Box("BasinWallEast", new Vector3(right - 0.08f, (bottom + rim) * 0.5f, 0f), new Vector3(0.1f, rim - bottom, 3.9f), basin);
            // 手前の縁（槽の口の手前の辺。調理台の天板の高さ）と、排水口。
            decor.Box("BasinLip", new Vector3((left + right) * 0.5f, rim - 0.15f, -2.05f), new Vector3(right - left + 0.2f, 0.3f, 0.12f), basin);
            decor.Cylinder("Drain", new Vector3((left + right) * 0.5f, bottom + 0.14f, 0.4f), new Vector3(1f, 0.02f, 1f), steel);
            // 水: 手前の面を調理台の正面（z -2）に、水面を槽の中だけに（前は水面の板が手前へ 8m 出ていた）。
            Transform water = GameObject.Find("Gimmicks/Sink")?.transform;
            if (water == null)
            {
                Debug.LogError("シンクの水（Gimmicks/Sink）が見つからない");
            }
            else
            {
                Transform front = water.Find("Front");
                if (front != null)
                {
                    front.localPosition = new Vector3(front.localPosition.x, front.localPosition.y, -1.98f);
                }
                Transform surface = water.Find("Surface");
                if (surface != null)
                {
                    surface.localPosition = new Vector3(surface.localPosition.x, surface.localPosition.y, 0f);
                    surface.localScale = new Vector3(right - left - 0.2f, 3.95f, 1f);
                }
            }
            // 蛇口: 奥の台（y 2、z 2〜7）に立つ台座と、上へ伸びて槽の上へ曲がる首（管）、レバー。
            var faucetBase = new Vector3(38f, 2f, 3.3f);
            decor.Cylinder("FaucetBase", faucetBase + new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.25f, 1.1f), chrome);
            Mesh neck = ProceduralMesh.Tube("Kitchen", "FaucetNeck", new[]
            {
                new Vector3(0f, 0.4f, 0f), new Vector3(0f, 3.5f, 0f), new Vector3(0f, 5.6f, -0.3f), new Vector3(0f, 6.6f, -1.2f), new Vector3(0f, 6.7f, -2f),
                new Vector3(0f, 6.2f, -2.5f), new Vector3(0f, 5.5f, -2.55f),
            }, new[] { 0.32f, 0.3f, 0.28f, 0.27f, 0.26f, 0.25f, 0.24f }, 12);
            ProceduralMesh.Place("FaucetNeck", decor.Root, neck, faucetBase, Quaternion.identity, Vector3.one, chrome);
            GameObject lever = decor.Box("FaucetLever", faucetBase + new Vector3(0.7f, 1.6f, 0.1f), new Vector3(0.16f, 1.4f, 0.16f), chrome);
            lever.transform.rotation = Quaternion.Euler(0f, 0f, -25f);
            // 下へ流れる水（蛇口の口から水面まで。流れる筋の板。下向きに流れるように上下を返す）と、水面のしぶき。
            float spoutY = faucetBase.y + 5.5f;
            float spoutZ = faucetBase.z - 2.55f;
            Material stream = StageSceneBuilder.StreamMaterial("FaucetStream", new Color(0.7f, 0.88f, 1f, 0.55f), new Color(0.35f, 0.6f, 0.9f, 0.75f),
                new Color(1f, 1f, 1f, 0.85f), 2.6f, new Vector2(5f, 3f), 0.35f, 0f, decor.Folder);
            float waterTop = 0f;
            GameObject fall = decor.Quad("FaucetWater", new Vector3(38f, (spoutY + waterTop) * 0.5f, spoutZ), new Vector2(0.42f, spoutY - waterTop), stream);
            fall.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
            Material splash = decor.Transparent("FaucetSplash", new Color(0.95f, 0.98f, 1f, 0.55f));
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                decor.Sphere("Splash", new Vector3(38f + Mathf.Cos(a) * 0.45f, waterTop + 0.12f, spoutZ + Mathf.Sin(a) * 0.3f), new Vector3(0.28f, 0.16f, 0.28f), splash);
            }
        }

        // やかん（2026-11-20 の要望: 洗面台の赤い球 → その場に合う精巧な置物。湯気〈KettleSteam〉の出る所）: 赤い琺瑯の胴（回転体）、
        // 黒いつまみのふた、上の取っ手（管）、注ぎ口（管）。bottom は底の中心。
        private static void Kettle(StageDecor decor, Vector3 bottom)
        {
            Material enamel = decor.Lit("KettleEnamel", new Color(0.82f, 0.16f, 0.14f));
            enamel.SetFloat("_Smoothness", 0.8f);
            Material black = decor.Lit("KettleBlack", new Color(0.06f, 0.06f, 0.07f));
            Material chrome = decor.Lit("KettleChrome", new Color(0.85f, 0.86f, 0.9f));
            chrome.SetFloat("_Metallic", 0.9f);
            chrome.SetFloat("_Smoothness", 0.8f);
            var kettle = new GameObject("Kettle").transform;
            kettle.SetParent(decor.Root, false);
            kettle.position = bottom;
            ProceduralMesh.Place("Body", kettle, ProceduralMesh.Lathe("Kitchen", "KettleBody", new[]
            {
                new Vector2(1.05f, 0f), new Vector2(1.25f, 0.25f), new Vector2(1.3f, 0.7f), new Vector2(1.18f, 1.25f), new Vector2(0.85f, 1.65f),
                new Vector2(0.55f, 1.82f), new Vector2(0f, 1.84f),
            }, 28), Vector3.zero, Quaternion.identity, Vector3.one, enamel);
            decor.Cylinder("Lid", kettle, new Vector3(0f, 1.86f, 0f), new Vector3(1.05f, 0.05f, 1.05f), chrome);
            decor.Sphere("LidKnob", kettle, new Vector3(0f, 2.02f, 0f), new Vector3(0.36f, 0.28f, 0.36f), black);
            Mesh handle = ProceduralMesh.Tube("Kitchen", "KettleHandle", new[]
            {
                new Vector3(-0.75f, 1.55f, 0f), new Vector3(-0.7f, 2.35f, 0f), new Vector3(0f, 2.6f, 0f), new Vector3(0.7f, 2.35f, 0f), new Vector3(0.75f, 1.55f, 0f),
            }, new[] { 0.09f, 0.1f, 0.1f, 0.1f, 0.09f }, 10);
            ProceduralMesh.Place("Handle", kettle, handle, Vector3.zero, Quaternion.identity, Vector3.one, black);
            Mesh spout = ProceduralMesh.Tube("Kitchen", "KettleSpout", new[]
            {
                new Vector3(0f, 0.55f, -1.05f), new Vector3(0f, 0.95f, -1.55f), new Vector3(0f, 1.45f, -1.85f), new Vector3(0f, 1.75f, -1.95f),
            }, new[] { 0.24f, 0.16f, 0.12f, 0.11f }, 10);
            ProceduralMesh.Place("Spout", kettle, spout, Vector3.zero, Quaternion.Euler(0f, 75f, 0f), Vector3.one, enamel);
        }

        // 排水溝の管の中を流れる水（2026-11-20 の要望: 洗面台の右の水流も水に合わせて精巧に）: 管の 3 つの区間（右へ・上へ・左へ）に、
        // 流れの向きへ流れる水の筋の板を入れる。
        private static void DrainWater(StageDecor decor)
        {
            Material flow = StageSceneBuilder.StreamMaterial("DrainStream", new Color(0.45f, 0.75f, 1f, 0.6f), new Color(0.15f, 0.42f, 0.8f, 0.8f),
                new Color(0.95f, 0.98f, 1f, 0.9f), 3.2f, new Vector2(6f, 2.2f), 0.45f, 0f, decor.Folder);
            // 区間: 中心・長さ（流れの向き）・幅・回転（板の上が流れの向きを向く）。
            (Vector3 Center, float Length, float Width, float Roll)[] parts =
            {
                (new Vector3(45.8f, -0.65f, 1.3f), 7f, 3f, -90f),
                (new Vector3(47.9f, 7.65f, 1.3f), 19.6f, 2.6f, 0f),
                (new Vector3(46.2f, 16.1f, 1.3f), 6.4f, 2.6f, 90f),
            };
            foreach (var part in parts)
            {
                GameObject water = decor.Quad("DrainWater", part.Center, new Vector2(part.Width, part.Length), flow);
                water.transform.rotation = Quaternion.Euler(0f, 0f, part.Roll);
            }
        }
    }
}
