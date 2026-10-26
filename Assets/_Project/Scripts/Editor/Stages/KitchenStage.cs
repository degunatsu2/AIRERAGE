using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// キッチン（SWEET KITCHEN。PASTRY CHEF のテーマのステージ。ユーザーの案、2026-10-26）。左右の調理台、真ん中のコンロと奥の大きなオーブン、
    /// 上の棚とケーキスタンド。調理台から落ちると撃破。
    /// 固有のギミック（ユーザーの案）: オーブンの火柱（40 秒ごとに奥のオーブンから手前へ噴き出す。危険。3 秒前に予告）、ロールケーキ
    /// （左右のステージの外から 1 つずつ出て、交差するように弧を描いて、反対の外へ去る。乗れて、ワイヤーがつながる動く足場。
    /// 乗ったまま外へ運ばれると撃破）。追加の案（取捨選択はユーザー）: トースター（周期的にポンと跳ね上げる発射台）、シンク（水。
    /// 5 秒で溺れる）と蛇口の水（下向きの周期のある風。シンクへ押し込む）、バター（氷。滑る）、カップケーキのベルトコンベア（スピード
    /// レーン）、やかんの湯気（上向きの周期のある風）、プリンのバンパー、ゼリーのジャンプ台、ミキサーの渦（上昇気流）、吊るしたおたま
    /// （アンカー）。
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
            var rollSize = new Vector2(5f, 2.4f);

            return new StageLayout
            {
                Id = "Kitchen",
                DisplayName = "SWEET KITCHEN",
                Description = "The oven blasts fire every 40 seconds. Ride the roll cakes across the sky, dodge the toaster, don't drown in the sink.",
                Color = new Color(1f, 0.6f, 0.75f),
                Terrain = new[]
                {
                    // 左の調理台（大理石の天板と、ワイヤーがつながる木の戸棚）。
                    new TerrainBox("CounterTopWest", -48f, 1f, -26f, 2f),
                    new TerrainBox("CabinetWest", -47f, -30f, -27f, 1f, attachable: true),
                    // 右の調理台（真ん中にシンク）。
                    new TerrainBox("CounterTopEastA", 26f, 1f, 34f, 2f),
                    new TerrainBox("CabinetEastA", 27f, -30f, 34f, 1f, attachable: true),
                    new TerrainBox("SinkBottom", 34f, -30f, 42f, -6f),
                    new TerrainBox("CounterTopEastB", 42f, 1f, 48f, 2f),
                    new TerrainBox("CabinetEastB", 42f, -30f, 47f, 1f, attachable: true),
                    // 真ん中のコンロ（鉄。オーブンの火柱の下）。
                    new TerrainBox("Stove", -9f, -30f, 9f, 0f, attachable: true),
                    // 上の棚とケーキスタンド。
                    new TerrainBox("ShelfWest", -46f, 14f, -34f, 14.6f, attachable: true),
                    new TerrainBox("ShelfEast", 34f, 14f, 46f, 14.6f, attachable: true),
                    new TerrainBox("CakeStandBase", -6f, 20f, 6f, 20.6f),
                    new TerrainBox("CakeStandTop", -3f, 25f, 3f, 25.5f),
                    // 調理台とコンロの間の、まな板とボウル（小さな足場）。
                    new TerrainBox("CuttingBoard", -21f, 6f, -15f, 6.5f, attachable: true),
                    new TerrainBox("MixingBowl", 15f, 6f, 21f, 6.5f, attachable: true),
                },
                // 吊るしたおたまとフライ返し。
                Anchors = new[]
                {
                    new Vector2(-15f, 30f), new Vector2(15f, 30f), new Vector2(0f, 36f), new Vector2(-28f, 34f), new Vector2(28f, 34f),
                    new Vector2(-40f, 23f), new Vector2(40f, 23f), new Vector2(-18f, 16f), new Vector2(18f, 16f),
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
                    new ZoneSpec("Toaster", StageZoneKind.Launcher, new Vector2(-36.5f, 3.2f), new Vector2(3f, 2.4f), Vector2.up, 30f).Every(7f, 0.5f, 3f, 0f, 1.5f),
                    // シンク（水）と蛇口の水（9 秒ごとに 2 秒、シンクへ押し込む）。
                    new ZoneSpec("Sink", StageZoneKind.Water, new Vector2(38f, -3f), new Vector2(8f, 6f), Vector2.zero, 5f, 20f),
                    new ZoneSpec("Faucet", StageZoneKind.Gust, new Vector2(38f, 6f), new Vector2(1.6f, 10f), Vector2.down, 70f, 14f).Every(9f, 2f, 4f, 0f, 1f),
                    // バター（氷。滑る）。
                    ZoneSpec.OnFloor("Butter", StageZoneKind.Ice, -31f, -27f, 2f, Vector2.zero, 0.06f),
                    // カップケーキのベルトコンベア（シンクとは逆、真ん中へ運ぶ）。
                    ZoneSpec.OnFloor("CupcakeConveyor", StageZoneKind.SpeedLane, 26.5f, 31f, 2f, Vector2.left, 11f),
                    // やかんの湯気: 6 秒ごとに 2 秒、上へ。
                    new ZoneSpec("KettleSteam", StageZoneKind.Gust, new Vector2(33f, 7f), new Vector2(2f, 10f), Vector2.up, 60f, 16f).Every(6f, 2f, 1f, 0f, 1f),
                    // プリンのバンパー。
                    new ZoneSpec("PuddingWest", StageZoneKind.Bumper, new Vector2(-18f, 11f), new Vector2(2.4f, 2.4f), Vector2.up, 18f),
                    new ZoneSpec("PuddingEast", StageZoneKind.Bumper, new Vector2(18f, 11f), new Vector2(2.4f, 2.4f), Vector2.up, 18f),
                    // ゼリーのジャンプ台。
                    ZoneSpec.OnFloor("JellyWest", StageZoneKind.JumpPad, -46f, -44f, 2f, Vector2.up, 26f, 0.4f),
                    ZoneSpec.OnFloor("JellyEast", StageZoneKind.JumpPad, 45.5f, 47.5f, 2f, Vector2.up, 26f, 0.4f),
                    ZoneSpec.OnFloor("JellyBoard", StageZoneKind.JumpPad, -19f, -17f, 6.5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("JellyBowl", StageZoneKind.JumpPad, 17f, 19f, 6.5f, Vector2.up, 24f, 0.4f),
                    // ドーナツの輪（加速リング）: 調理台の上の空から真ん中へ。
                    new ZoneSpec("DonutWest", StageZoneKind.BoostRing, new Vector2(-26f, 15f), new Vector2(2.4f, 3f), new Vector2(1f, 0.3f), 19f),
                    new ZoneSpec("DonutEast", StageZoneKind.BoostRing, new Vector2(26f, 15f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.3f), 19f),
                    // ミキサーの渦（左の棚の上から上へ）。
                    new ZoneSpec("MixerWhirl", StageZoneKind.Updraft, new Vector2(-40f, 19f), new Vector2(4f, 9f), Vector2.up, 45f, 12f),
                },
                Platforms = new[]
                {
                    // ロールケーキ（ユーザーの案）: 左右の外から同時に出て、片方は高い弧、もう片方は低い弧で交差し、反対の外へ去る（22 秒で渡り、
                    // 8 秒いない）。乗れて、ワイヤーがつながる。
                    PlatformSpec.Arc("RollCakeWest", rollSize, true, new Vector2(-72f, 12f), new Vector2(0f, 44f), new Vector2(72f, 12f), rollTravel, rollPeriod)
                        .Looks(RollCakeLooks),
                    PlatformSpec.Arc("RollCakeEast", rollSize, true, new Vector2(72f, 30f), new Vector2(0f, 4f), new Vector2(-72f, 30f), rollTravel, rollPeriod)
                        .Looks(RollCakeLooks),
                },
                Coins = new (Vector2, int)[]
                {
                    // 調理台の上。
                    (new Vector2(-46f, 4.5f), 1), (new Vector2(-41f, 4f), 1), (new Vector2(-33f, 4f), 1), (new Vector2(-29f, 3.5f), 1),
                    (new Vector2(29f, 3.5f), 1), (new Vector2(32.5f, 4f), 1), (new Vector2(44f, 4f), 1),
                    // トースターの上と、やかんの湯気の上。
                    (new Vector2(-36.5f, 8f), 1), (new Vector2(-36.5f, 12.5f), 3), (new Vector2(33f, 13f), 3),
                    // シンクの中（潜る）と蛇口の下。
                    (new Vector2(38f, -4.5f), 20), (new Vector2(38f, 3.5f), 1), (new Vector2(36f, 1f), 1), (new Vector2(40f, 1f), 1),
                    // コンロの上（オーブンの火柱が噴く所）。
                    (new Vector2(-6f, 2f), 1), (new Vector2(0f, 2f), 3), (new Vector2(6f, 2f), 1), (new Vector2(-4f, 7f), 1), (new Vector2(4f, 7f), 1),
                    (new Vector2(0f, 11f), 20), (new Vector2(-8f, 13f), 1), (new Vector2(8f, 13f), 1),
                    // まな板とボウルの上、プリンの周り。
                    (new Vector2(-20.5f, 8.5f), 1), (new Vector2(-15.5f, 8.5f), 1), (new Vector2(15.5f, 8.5f), 1), (new Vector2(20.5f, 8.5f), 1),
                    (new Vector2(-18f, 14.5f), 1), (new Vector2(18f, 14.5f), 1), (new Vector2(-22.5f, 11f), 1), (new Vector2(22.5f, 11f), 1),
                    // 棚の上とミキサーの渦。
                    (new Vector2(-44.5f, 16.5f), 1), (new Vector2(-36f, 16.5f), 1), (new Vector2(-40f, 25f), 3), (new Vector2(-40f, 29f), 20),
                    (new Vector2(44.5f, 16.5f), 1), (new Vector2(36f, 16.5f), 1), (new Vector2(40f, 19f), 3),
                    // ケーキスタンドの上と、その上の空。
                    (new Vector2(-4.5f, 22.5f), 1), (new Vector2(4.5f, 22.5f), 1), (new Vector2(-6f, 28f), 3), (new Vector2(0f, 32f), 1),
                    // ロールケーキの道（高い弧と低い弧）。
                    (new Vector2(-30f, 24.5f), 1), (new Vector2(-20f, 27f), 1), (new Vector2(20f, 27f), 1), (new Vector2(30f, 24.5f), 1),
                    (new Vector2(-30f, 21f), 1), (new Vector2(-18f, 19f), 1), (new Vector2(18f, 19f), 1), (new Vector2(30f, 21f), 1),
                    (new Vector2(-10f, 34f), 3), (new Vector2(10f, 34f), 3),
                    // 調理台の下（戸棚の前。落ちる前に届く）。
                    (new Vector2(-24f, -3f), 20), (new Vector2(24f, -3f), 20), (new Vector2(-12f, -4f), 3), (new Vector2(12f, -4f), 3),
                    // 高い所とステージの端。
                    (new Vector2(-25f, 38f), 20), (new Vector2(25f, 38f), 20), (new Vector2(-53f, 10f), 20), (new Vector2(53f, 10f), 20),
                    (new Vector2(0f, 41f), 20), (new Vector2(-52f, 24f), 3), (new Vector2(52f, 24f), 3),
                    (new Vector2(-16f, 22f), 20), (new Vector2(16f, 22f), 20), (new Vector2(-30f, 34.5f), 20), (new Vector2(30f, 34.5f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 42f), new Vector2(-46f, 34f), new Vector2(46f, 34f), new Vector2(38f, -5f), new Vector2(0f, 5f),
                    new Vector2(-24f, -6f), new Vector2(24f, -6f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 17f), new Vector2(-40f, 16.6f), new Vector2(40f, 16.6f), new Vector2(-18f, 3f), new Vector2(18f, 3f),
                    new Vector2(0f, 27.5f), new Vector2(-30f, 30f), new Vector2(30f, 30f),
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
            decor.Box("Toaster", new Vector3(-36.5f, 3.2f, 1.2f), new Vector3(3.2f, 2.4f, 2.4f), steel);
            // 窓と床（遠く下）。
            decor.Quad("WindowWest", new Vector3(-30f, 26f, 14.8f), new Vector2(12f, 9f), window);
            decor.Quad("WindowEast", new Vector3(30f, 26f, 14.8f), new Vector2(12f, 9f), window);
            decor.Box("Floor", new Vector3(0f, -22f, 10f), new Vector3(150f, 2f, 40f), floor);
            // 吊るしたおたまの柄（アンカーの上）。
            foreach (Vector2 hook in new[] { new Vector2(-15f, 30f), new Vector2(15f, 30f), new Vector2(0f, 36f), new Vector2(-28f, 34f), new Vector2(28f, 34f) })
            {
                decor.Box("LadleHandle", new Vector3(hook.x, hook.y + 3f, 0.6f), new Vector3(0.15f, 6f, 0.15f), steel);
            }
        }
    }
}
