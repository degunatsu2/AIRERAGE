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
                DisplayName = "SWEET KITCHEN",
                Description = "The oven blasts fire every 40 seconds. Ride the roll cakes across the sky, and dive into the sink drain to be flushed up to the shelf.",
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
                        .Every(40f, 3f, 10f, 0f, 3f).Column(22f),
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
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-41.5f, 3f, 0f), new Vector3(43.5f, 3f, 0f) },
                Decorate = Decorate,
            };
        }

        // ロールケーキ: 横に寝かせた円柱（スポンジ）と、手前の面のクリームの渦巻き。
        private static void RollCakeLooks(StageDecor decor, Transform visual, PlatformSpec spec)
        {
            GameObject sponge = decor.Cylinder("Sponge", visual, Vector3.zero, new Vector3(spec.Size.y, spec.Size.x * 0.5f, spec.Size.y),
                decor.Lit("RollSponge", new Color(0.95f, 0.75f, 0.45f)));
            sponge.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Material cream = decor.Unlit("RollCream", new Color(1f, 0.97f, 0.92f));
            for (int i = 0; i < 3; i++)
            {
                float r = 0.35f + i * 0.35f;
                GameObject ring = decor.Cylinder("Swirl", visual, new Vector3(-spec.Size.x * 0.5f - 0.02f, 0f, 0f), new Vector3(r * 2f, 0.03f, r * 2f), cream);
                ring.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            decor.Box("Frosting", visual, new Vector3(0f, spec.Size.y * 0.5f - 0.05f, 0f), new Vector3(spec.Size.x * 0.95f, 0.15f, spec.Size.y * 0.8f),
                decor.Lit("RollFrosting", new Color(1f, 0.85f, 0.9f)));
        }

        // 奥の壁とタイル、大きなオーブン（火柱の口）、コンロのつまみ、窓、調理台の奥行き、吊るしたおたま。
        private static void Decorate(StageDecor decor)
        {
            Material tile = decor.Lit("Tile", new Color(0.92f, 0.95f, 0.97f));
            Material tileLine = decor.Lit("TileLine", new Color(0.78f, 0.82f, 0.86f));
            Material oven = decor.Lit("Oven", new Color(0.25f, 0.24f, 0.26f));
            Material ovenGlow = decor.Unlit("OvenGlow", new Color(1f, 0.55f, 0.15f));
            Material steel = decor.Lit("Steel", new Color(0.75f, 0.77f, 0.8f));
            Material wood = decor.Lit("CabinetWood", new Color(0.62f, 0.42f, 0.3f));
            Material marble = decor.Lit("Marble", new Color(0.96f, 0.94f, 0.92f));
            Material floor = decor.Lit("KitchenFloor", new Color(0.55f, 0.35f, 0.28f));
            Material window = decor.Unlit("Window", new Color(0.75f, 0.9f, 1f));
            Material sinkWater = decor.Lit("SinkBack", new Color(0.55f, 0.78f, 0.92f));

            // 奥の壁のタイル。
            decor.Box("BackWall", new Vector3(0f, 18f, 16f), new Vector3(150f, 70f, 2f), tile);
            for (float y = -8f; y <= 50f; y += 4f)
            {
                decor.Box("TileLine", new Vector3(0f, y, 14.9f), new Vector3(150f, 0.08f, 0.1f), tileLine);
            }
            // 大きなオーブン（コンロの奥。口が光る）と、扉の窓。
            decor.Box("OvenBody", new Vector3(0f, 6f, 9f), new Vector3(26f, 22f, 8f), oven);
            decor.Box("OvenMouth", new Vector3(0f, 8f, 4.9f), new Vector3(20f, 12f, 0.2f), ovenGlow);
            for (int i = 0; i < 5; i++)
            {
                decor.Cylinder("Knob", new Vector3(-8f + i * 4f, 16f, 4.8f), new Vector3(1.2f, 0.2f, 1.2f), steel).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            // 調理台の奥行き（大理石と木）。
            decor.Box("CounterBackWest", new Vector3(-37f, -6f, 4.5f), new Vector3(22f, 16f, 5f), wood);
            decor.Box("CounterBackEast", new Vector3(37f, -6f, 4.5f), new Vector3(22f, 16f, 5f), wood);
            decor.Box("MarbleBackWest", new Vector3(-37f, 1.5f, 4.5f), new Vector3(22f, 1f, 5f), marble);
            decor.Box("MarbleBackEast", new Vector3(37f, 1.5f, 4.5f), new Vector3(22f, 1f, 5f), marble);
            // シンクの奥と蛇口。
            decor.Box("SinkBack", new Vector3(38f, -3f, 2.2f), new Vector3(8f, 6f, 0.4f), sinkWater);
            decor.Box("FaucetNeck", new Vector3(38f, 9.5f, 1.5f), new Vector3(0.5f, 3f, 0.5f), steel);
            decor.Box("FaucetSpout", new Vector3(38f, 11f, 0.8f), new Vector3(0.5f, 0.5f, 1.8f), steel);
            // やかんとトースター。
            decor.Sphere("Kettle", new Vector3(33f, 3.2f, 1.2f), new Vector3(2.2f, 2.2f, 2.2f), decor.Lit("Kettle", new Color(0.85f, 0.25f, 0.25f)));
            decor.Box("Toaster", new Vector3(-35.5f, 3.2f, 1.2f), new Vector3(3.2f, 2.4f, 2.4f), steel);
            // 排水溝の管（奥の側の半透明の管と、口の格子）。
            Material pipeGlass = decor.Transparent("DrainPipe", new Color(0.6f, 0.85f, 1f, 0.16f));
            decor.Box("DrainPipeLow", new Vector3(45.8f, -0.65f, 1.4f), new Vector3(7f, 3.3f, 1.2f), pipeGlass);
            decor.Box("DrainPipeUp", new Vector3(47.9f, 7.65f, 1.4f), new Vector3(2.9f, 19.9f, 1.2f), pipeGlass);
            for (int i = 0; i < 4; i++)
            {
                decor.Box("DrainGrate", new Vector3(42.15f, -2f + i * 0.8f, -1.6f), new Vector3(0.12f, 0.12f, 0.6f), steel);
            }
            // 窓と床（遠く下）。
            decor.Quad("WindowWest", new Vector3(-30f, 26f, 14.8f), new Vector2(12f, 9f), window);
            decor.Quad("WindowEast", new Vector3(30f, 26f, 14.8f), new Vector2(12f, 9f), window);
            decor.Box("Floor", new Vector3(0f, -22f, 10f), new Vector3(150f, 2f, 40f), floor);
            // 吊るしたおたまの柄（アンカーの上）。
            foreach (Vector2 hook in new[] { new Vector2(-15f, 25f), new Vector2(15f, 25f), new Vector2(0f, 36f), new Vector2(-28f, 27f), new Vector2(28f, 27f) })
            {
                decor.Box("LadleHandle", new Vector3(hook.x, hook.y + 3f, 0.6f), new Vector3(0.15f, 6f, 0.15f), steel);
            }
        }
    }
}
