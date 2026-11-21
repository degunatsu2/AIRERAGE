using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 恐怖の館（VAMPIRE MANOR。ユーザーの案、2026-10-26）。館の外が舞台: 真ん中に吸血鬼の館（玄関のポーチ、バルコニー、屋根、鐘楼）、
    /// 左に寂れた墓場（墓石と納骨堂と枯れ木。丘の下は洞窟で、納骨堂の床から入れる）、右におどろおどろしいカカシの立つトマト農園。
    /// 上空の左右に牢屋。館と墓場・農園の間は底のない谷。
    /// ギミック（ユーザーの要望、2026-10-26）: 悪魔の手（墓場と農園の地面を這い回り、捕まると握られて時空に吸い込まれ、その側の牢屋へ
    /// 移される）、牢屋（10 秒に 1 回、左右の格子が開いて通れる）、落雷（墓場と農園のランダムな場所。予告は表示だけで音はない）。
    /// 追加の案（残したもの）: カボチャの発射台、鬼火の輪（加速リング）、空飛ぶ棺のふた（動く足場）、カカシの腕、ガーゴイル（アンカー）、
    /// ポーチのジャンプ台。ユーザーの要望で足したもの（2026-10-26）: 納骨堂の屋根のジャンプ台、右の端を牢屋の高さまで上下する鎖の昇降台。消したもの（ユーザーの要望）: 井戸（ワープ）、堀（水）、赤い満月の光（低重力）、棺桶・幽霊・トマト（バンパー）、
    /// コウモリの群れ（風）。
    /// </summary>
    internal static class ManorStage
    {
        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.32f, 0.3f, 0.36f),
                Attachable = new Color(0.45f, 0.36f, 0.5f),
                Anchor = new Color(0.85f, 0.95f, 0.6f),
                SkyTint = new Color(0.12f, 0.08f, 0.2f),
                Ground = new Color(0.05f, 0.03f, 0.06f),
                Exposure = 0.35f,
                AtmosphereThickness = 0.9f,
                SunSize = 0.08f,
                Light = new Color(0.7f, 0.75f, 1f),
                LightIntensity = 0.85f,
                LightEuler = new Vector3(35f, 30f, 0f),
                AmbientSky = new Color(0.32f, 0.3f, 0.45f),
                AmbientEquator = new Color(0.22f, 0.2f, 0.3f),
                AmbientGround = new Color(0.12f, 0.1f, 0.14f),
                FogColor = new Color(0.16f, 0.14f, 0.22f),
                FogStart = 30f,
                FogEnd = 110f,
            };
            theme.ZoneColors[StageZoneKind.Hazard] = new Color(0.8f, 0.85f, 1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.55f, 0.1f);
            theme.ZoneColors[StageZoneKind.Grab] = new Color(0.6f, 0.12f, 0.3f);
            // 牢屋（左右の上空）: 中の広さは 8.8m × 5m。左右の格子が 10 秒に 1 回、3 秒だけ上に開く【仮】。
            var prisonWest = new Vector2(-40f, 29.2f);
            var prisonEast = new Vector2(40f, 29.2f);

            return new StageLayout
            {
                Id = "Manor",
                DisplayName = "VAMPIRE MANOR",
                Music = "Manor",
                Description = "Outside a vampire's manor. Demon hands crawl the graveyard and the farm: get caught and you are dragged into the prison in the sky.",
                DescriptionJa = "吸血鬼の館の外。墓場と農園を悪魔の手が這い回り、捕まると上空の牢屋へ引きずり込まれる。",
                Color = new Color(0.75f, 0.2f, 0.35f),
                Terrain = new[]
                {
                    // 左の墓場の丘（下は洞窟。左右の端が開いていて、納骨堂の床の穴から中へ上がれる。ユーザーの要望、2026-10-26）。
                    new TerrainBox("GraveHillCrustWest", -48f, -1.6f, -31.8f, 0f),
                    new TerrainBox("GraveHillCrustEast", -30.2f, -1.6f, -24f, 0f),
                    new TerrainBox("GraveHillBase", -48f, -30f, -24f, -5.4f),
                    new TerrainBox("TombstoneA", -44.5f, 0f, -43.5f, 1.6f, attachable: true),
                    new TerrainBox("TombstoneB", -39f, 0f, -38f, 2f, attachable: true),
                    new TerrainBox("CryptPillarWest", -34f, 0f, -33.4f, 5f, attachable: true),
                    new TerrainBox("CryptPillarEast", -28.6f, 0f, -28f, 5f, attachable: true),
                    new TerrainBox("CryptRoof", -34.5f, 5f, -27.5f, 5.6f, attachable: true),
                    new TerrainBox("DeadTreeBranch", -46f, 12f, -40f, 12.5f, attachable: true),
                    // 真ん中の館（玄関のポーチ、バルコニー、屋根、鐘楼、煙突）。
                    new TerrainBox("Porch", -14f, -30f, 14f, 2f),
                    // バルコニー（真ん中に 1 つ。ポーチのジャンプ台の上を空け、跳んでも頭がぶつからない。ユーザーの要望、2026-10-26）。
                    // 2026-11-19: 館のポーチの屋根の幅（柱の外側まで）に合わせて x ±7 に広げた（ジャンプ台 x ±9〜11 の上は空けたまま）。
                    new TerrainBox("Balcony", -7f, 10f, 7f, 10.6f, attachable: true),
                    new TerrainBox("RoofWest", -14f, 20f, -2f, 20.8f, attachable: true),
                    new TerrainBox("RoofEast", 2f, 20f, 14f, 20.8f, attachable: true),
                    new TerrainBox("BellTower", -2f, 20f, 2f, 32f, attachable: true),
                    new TerrainBox("BellTowerTop", -3f, 32f, 3f, 32.6f),
                    new TerrainBox("Chimney", 9f, 20.8f, 11f, 24f),
                    // 右のトマト農園（カカシ 2 体と、納屋の屋根）。
                    new TerrainBox("FarmField", 24f, -30f, 48f, 0f),
                    new TerrainBox("ScarecrowPoleA", 30.8f, 0f, 31.2f, 6f),
                    new TerrainBox("ScarecrowArmsA", 29f, 4.6f, 33f, 5f, attachable: true),
                    new TerrainBox("ScarecrowPoleB", 40.8f, 0f, 41.2f, 6f),
                    new TerrainBox("ScarecrowArmsB", 39f, 4.6f, 43f, 5f, attachable: true),
                    new TerrainBox("BarnRoof", 42f, 12f, 48f, 12.6f, attachable: true),
                    // 上空の牢屋（石の床と天井。ワイヤーはつながらない。左右の格子は周期で開く扉）。
                    new TerrainBox("PrisonFloorWest", -45f, 27f, -35f, 27.6f),
                    new TerrainBox("PrisonRoofWest", -44.4f, 32.6f, -35.6f, 33.2f),
                    new TerrainBox("PrisonFloorEast", 35f, 27f, 45f, 27.6f),
                    new TerrainBox("PrisonRoofEast", 35.6f, 32.6f, 44.4f, 33.2f),
                },
                // ガーゴイル（屋根の端より外の、少し下。ユーザーの要望、2026-10-26）、枯れ木の枝先、カカシの頭、鐘。
                Anchors = new[]
                {
                    new Vector2(-17f, 18f), new Vector2(17f, 18f), new Vector2(0f, 38f), new Vector2(-42f, 15f), new Vector2(31f, 7.5f),
                    new Vector2(41f, 7.5f), new Vector2(-24f, 28f), new Vector2(24f, 28f), new Vector2(-30f, 18f),
                },
                // 館と墓場・農園の間の谷の底が撃破の線。
                BoundsMin = new Vector2(-60f, -12f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 落雷: 墓場と農園のランダムな場所に、10〜16 秒ごとに落ちる（1.5 秒前に予告。予告は表示だけで、音は鳴らさない。ユーザーの
                    // 要望、2026-10-26）。当たると威力 12 の枚数のコインを落とす。牢屋には届かない。
                    new ZoneSpec("LightningGrave", StageZoneKind.Hazard, new Vector2(-36f, 13f), new Vector2(24f, 26f), Vector2.up, 12f, 16f)
                        .Every(10f, 0.4f, 4f, 6f, 1.5f).Column(1.6f).Silent().Styled(StageZoneView.ZoneLook.Lightning),
                    new ZoneSpec("LightningFarm", StageZoneKind.Hazard, new Vector2(36f, 13f), new Vector2(24f, 26f), Vector2.up, 12f, 16f)
                        .Every(10f, 0.4f, 9f, 6f, 1.5f).Column(1.6f).Silent().Styled(StageZoneView.ZoneLook.Lightning),
                    // 悪魔の手（ユーザーの要望、2026-10-26）: 墓場と農園の地面を往復し（片道 8 秒。端で 1.5 秒待ち、次に向かう方へ矢印を出す。2026-11-20 の要望）、触れた人を握って、1.2 秒でその側の牢屋へ移す。
                    new ZoneSpec("DemonHandWest", StageZoneKind.Grab, new Vector2(-45f, 1.2f), new Vector2(2.6f, 2.4f), Vector2.up, 1.2f)
                        .To(prisonWest).Moving(new Vector2(19f, 0f), 19f, 1.5f),
                    new ZoneSpec("DemonHandEast", StageZoneKind.Grab, new Vector2(45f, 1.2f), new Vector2(2.6f, 2.4f), Vector2.up, 1.2f)
                        .To(prisonEast).Moving(new Vector2(-19f, 0f), 19f, 1.5f),
                    // カボチャの発射台: 農園から館の東のバルコニーへ。
                    // かぼちゃの投石機（2026-11-21 の方針: 見た目に合わせて効果と配置も変える。前はいつも効く発射台だった）: かごに入ると、
                    // 3.6 秒ごとに 1.2 秒かけて腕を引き絞り（予告）、腕が跳ね上がる 0.25 秒の間に館の屋根へ放る。軸と横木の分だけ右へ寄せた
                    // （前は x 26〜28。左の端が農園の崖に掛からないように）。CatapultView。
                    ZoneSpec.OnFloor("PumpkinLauncher", StageZoneKind.Launcher, 26.5f, 28.5f, 0f, new Vector2(-0.5f, 1f), 32f)
                        .Every(3.6f, 0.25f, 0f, 0f, 1.2f).Styled(StageZoneView.ZoneLook.Catapult),
                    // 館のポーチのジャンプ台と、納骨堂の屋根のジャンプ台（ユーザーの要望、2026-10-26）。
                    ZoneSpec.OnFloor("PorchPadWest", StageZoneKind.JumpPad, -11f, -9f, 2f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("PorchPadEast", StageZoneKind.JumpPad, 9f, 11f, 2f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("CryptPad", StageZoneKind.JumpPad, -32f, -30f, 5.6f, Vector2.up, 24f, 0.4f),
                },
                // 鬼火の輪（2026-11-21 の要望: 1 つだけ。左右の牢屋・館の屋根の頂点〈鐘楼の上〉・バルコニーの真ん中・左下の納骨堂の中・右下の
                // トマト農園の真上の 6 地点を、試合の種で決まる順にランダムに速く〈秒速 12m〉飛び回り、地点で 1 秒漂う。くぐった人の周りを回って
                // 30 枚のコインを渡し、上空へ消えて 20〜30 秒でまた現れる。前は 2 つの加速リングだった）。StageTraps.StepWispRings・WispRingView。
                Traps = new[]
                {
                    TrapSpec.WispRing("WispRing", WispPoints, 12f, 1f, new Vector2(2.4f, 3f), 30, 20f, 30f),
                },
                Platforms = new[]
                {
                    // 空飛ぶ棺のふた: 鐘楼の高さで、館の上を左右に行き来する（ワイヤーがつながる）。
                    new PlatformSpec("FlyingCoffin", new Vector2(-20f, 26f), new Vector2(4f, 0.5f), true)
                        .Path(14f, (0f, Vector2.zero), (6f, new Vector2(40f, 0f)), (7f, new Vector2(40f, 0f)), (13f, Vector2.zero))
                        .Eased().Tinted(new Color(0.3f, 0.18f, 0.12f)),
                    // 牢屋の格子（ユーザーの要望、2026-10-26）: 10 秒ごとに 3 秒だけ、左右が上に開く（0.4 秒で開き、0.8 秒で閉じる）。
                    PrisonBars("PrisonBarsWestOuter", -44.7f), PrisonBars("PrisonBarsWestInner", -35.3f),
                    PrisonBars("PrisonBarsEastInner", 35.3f), PrisonBars("PrisonBarsEastOuter", 44.7f),
                    // 鎖の昇降台（ユーザーの要望、2026-10-26）: 右の端で、農園の地面の高さから牢屋の高さまで 14 秒で上下する（端の赤いコインと
                    // 3 枚のコインを通る。ワイヤーがつながる）。
                    new PlatformSpec("ChainLift", new Vector2(51.5f, 0.5f), new Vector2(3.4f, 0.5f), true)
                        .Path(14f, (0f, Vector2.zero), (1.5f, Vector2.zero), (6.5f, new Vector2(0f, 27.5f)), (8.5f, new Vector2(0f, 27.5f)),
                            (13.5f, Vector2.zero))
                        .Eased().Looks(ChainLiftLooks),
                },
                Coins = new (Vector2, int)[]
                {
                    // 墓場（墓石の間、納骨堂の中と屋根の上、枯れ木の上）。納骨堂の中へは、丘の下の洞窟から上がる。
                    (new Vector2(-46f, 4f), 1), (new Vector2(-41.5f, 4.5f), 1), (new Vector2(-36f, 2f), 1), (new Vector2(-31f, 2f), 20),
                    (new Vector2(-31f, 3.8f), 3), (new Vector2(-34f, 7.5f), 1), (new Vector2(-28f, 7.5f), 1), (new Vector2(-43f, 14.5f), 3),
                    (new Vector2(-45.5f, 9f), 1), (new Vector2(-26f, 3f), 1),
                    // 丘の下の洞窟。
                    (new Vector2(-44f, -3.5f), 20), (new Vector2(-37f, -3.5f), 3), (new Vector2(-27f, -3.5f), 3), (new Vector2(-40.5f, -3.5f), 1),
                    // 墓場の上の空（落雷）。
                    (new Vector2(-38f, 18f), 1), (new Vector2(-30f, 22f), 1), (new Vector2(-36f, 25f), 3),
                    // 館のポーチと玄関。
                    (new Vector2(-6f, 4f), 1), (new Vector2(-2f, 4f), 1), (new Vector2(2f, 4f), 1), (new Vector2(6f, 4f), 1), (new Vector2(0f, 7f), 3),
                    // ジャンプ台の上と、バルコニーの上。
                    (new Vector2(-10.5f, 12.5f), 1), (new Vector2(10.5f, 12.5f), 1), (new Vector2(-3f, 13f), 1), (new Vector2(3f, 13f), 1),
                    (new Vector2(0f, 16f), 20),
                    // 屋根の上と鐘楼。
                    (new Vector2(-11f, 22.5f), 1), (new Vector2(-6f, 22.5f), 1), (new Vector2(6f, 22.5f), 1), (new Vector2(12.5f, 26f), 1),
                    (new Vector2(-4f, 28f), 1), (new Vector2(4f, 28f), 1), (new Vector2(0f, 35f), 3),
                    // 空飛ぶ棺のふたの道。
                    (new Vector2(-16f, 28f), 1), (new Vector2(16f, 28f), 1), (new Vector2(-26f, 30f), 3), (new Vector2(26f, 30f), 3),
                    // 鐘楼の上の空。
                    (new Vector2(-10f, 40f), 1), (new Vector2(10f, 40f), 1), (new Vector2(0f, 43f), 20),
                    // 牢屋の中（1 枚ずつ）と、牢屋の外。
                    (new Vector2(-40f, 31f), 1), (new Vector2(40f, 31f), 1), (new Vector2(-30f, 34f), 20), (new Vector2(30f, 34f), 20),
                    // 農園（カカシの腕の上、畑の上、納屋の屋根）。
                    (new Vector2(29.5f, 7f), 1), (new Vector2(32.5f, 7f), 1), (new Vector2(39.5f, 7f), 1), (new Vector2(42.5f, 7f), 1),
                    (new Vector2(36f, 4f), 1), (new Vector2(33.5f, 2f), 1), (new Vector2(44f, 14.5f), 3), (new Vector2(46f, 17f), 1),
                    (new Vector2(26f, 4f), 1), (new Vector2(45.5f, 20f), 20),
                    // 農園の上の空（落雷）と、カボチャの発射台の道。
                    (new Vector2(38f, 20f), 1), (new Vector2(30f, 16f), 1), (new Vector2(36f, 25f), 3), (new Vector2(20f, 12f), 1),
                    // 谷の上。
                    (new Vector2(-19f, 6f), 3), (new Vector2(19f, 6f), 3),
                    // 高い所とステージの端。
                    (new Vector2(-24f, 36f), 20), (new Vector2(24f, 36f), 20), (new Vector2(-53f, 10f), 20), (new Vector2(53f, 10f), 20),
                    (new Vector2(-52f, 24f), 3), (new Vector2(52f, 24f), 3), (new Vector2(-20f, 19f), 20), (new Vector2(20f, 19f), 20),
                    (new Vector2(-12f, 31f), 20), (new Vector2(12f, 31f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 44f), new Vector2(-50f, 32f), new Vector2(50f, 32f), new Vector2(-38f, -3.5f), new Vector2(36f, 20f),
                    new Vector2(-31f, 3f), new Vector2(0f, 9f),
                },
                Banks = new[]
                {
                    new Vector2(-8f, 24.5f), new Vector2(-9f, 14f), new Vector2(9f, 14f), new Vector2(-37.5f, 6.5f), new Vector2(37f, 9f),
                    new Vector2(0f, 39f), new Vector2(-30f, 13f), new Vector2(-32f, -3.5f),
                },
                Spawns = new[] { new Vector3(-4f, 3f, 0f), new Vector3(4f, 3f, 0f), new Vector3(-4.5f, 11.6f, 0f), new Vector3(4.5f, 11.6f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-12.75f, 3f, 0f), new Vector3(12.75f, 3f, 0f), new Vector3(-12f, 21.8f, 0f), new Vector3(12f, 21.8f, 0f) },
                Decorate = Decorate,
                // 背景とオブジェクトのクオリティアップ（D-97）。
                Look = StageLook.FromTheme("Manor", theme, StageMood.Night),
            };
        }

        // 牢屋の格子（周期で開く扉。中心 x、牢屋の床の上から天井の下まで）。
        private static PlatformSpec PrisonBars(string name, float x)
        {
            return PlatformSpec.TimedDoor(name, new Vector2(x, 30.1f), new Vector2(0.6f, 5f), 5.6f, 0.4f, 0.8f, 10f, 3f).Looks(BarsLooks);
        }

        // 鎖の昇降台: 古い木の板と鉄の縁（上へ伸びる黒い棒〈吊り上げの鎖〉は消した。2026-11-20 の要望）。
        private static void ChainLiftLooks(StageDecor decor, Transform visual, PlatformSpec spec)
        {
            decor.Box("Plank", visual, Vector3.zero, new Vector3(spec.Size.x, spec.Size.y, 4f), decor.Lit("LiftWood", new Color(0.3f, 0.2f, 0.14f)));
            Material iron = decor.Lit("LiftIron", new Color(0.2f, 0.2f, 0.22f));
            decor.Box("RimFront", visual, new Vector3(0f, -spec.Size.y * 0.25f, -2.05f), new Vector3(spec.Size.x * 1.02f, spec.Size.y * 0.4f, 0.1f), iron);
        }

        // 洞窟の通路の範囲（墓場の丘の下。床 y -5.4、天井 y -1.6、x -48〜-24）。
        private const float CaveLeft = -48f;
        private const float CaveRight = -24f;
        private const float CaveFloor = -5.4f;
        private const float CaveCeiling = -1.6f;

        // 洞窟: 奥の岩壁（雑音の高さの網を立てる）、鍾乳石と石筍（回転体）、岩、光る茸。どれも遊ぶ面より奥（z 0.6 より奥）で、当たりはない。
        private static void Cave(StageDecor decor, Material abyss)
        {
            Material rock = TerrainSculpt.GroundMaterial(decor, "CaveRock", new Color(0.16f, 0.13f, 0.17f), TerrainSculpt.Photos.Rock,
                new Color(0.2f, 0.17f, 0.2f), TerrainSculpt.Photos.Rock, 0.35f);
            Material glow = decor.Unlit("CaveFungus", new Color(0.55f, 0.95f, 0.85f));
            Material cap = decor.Unlit("CaveFungusCap", new Color(0.7f, 0.35f, 1f));
            // 奥の暗がり（岩壁の隙間から見える）。
            decor.Box("CaveBack", new Vector3(-36f, -3.5f, 4.2f), new Vector3(24f, 3.8f, 1f), abyss);
            // 奥の岩壁: 横 24m・縦 6m の網を立てて（高さが手前への出っ張り）、手前へ 0〜1.1m ごつごつ出る。横は足場（丘）の幅に収める
            // （2026-11-20 の指摘: 左右に 1m ずつはみ出していた）。
            var wall = new TerrainSculpt.Field(CaveLeft, CaveRight, -1f, 1f, 0.35f, (x, y) =>
                1.1f * TerrainSculpt.Ridged(x * 0.35f, y * 0.9f, 71, 4) * TerrainSculpt.Smooth(0f, 0.6f, Mathf.Abs(y) < 0.8f ? 1f : 0.4f));
            GameObject rockWall = TerrainSculpt.Place(decor, "Manor", "CaveWall", wall, rock);
            // 網の y（高さ）を手前（-z）へ、網の z を上下へ: X の軸の周りに 90° 回す。
            rockWall.transform.SetPositionAndRotation(new Vector3(0f, (CaveFloor + CaveCeiling) * 0.5f, 3.6f), Quaternion.Euler(-90f, 0f, 0f));
            rockWall.transform.localScale = new Vector3(1f, 1f, (CaveCeiling - CaveFloor) * 0.5f + 0.4f);
            // 鍾乳石（天井から下へ）と石筍（床から上へ）。
            Mesh spike = ProceduralMesh.Lathe("Manor", "CaveSpike",
                new[] { new Vector2(0.32f, 0f), new Vector2(0.27f, 0.25f), new Vector2(0.16f, 0.6f), new Vector2(0.07f, 0.85f), new Vector2(0f, 1f) }, 9);
            float x = CaveLeft + 1.2f;
            int i = 0;
            while (x < CaveRight - 1f)
            {
                float length = decor.Range(0.5f, 1.3f);
                float z = decor.Range(0.9f, 2.4f);
                ProceduralMesh.Place("Stalactite", decor.Root, spike, new Vector3(x, CaveCeiling + 0.05f, z), Quaternion.Euler(180f, decor.Range(0f, 360f), 0f),
                    new Vector3(decor.Range(0.8f, 1.3f), length, decor.Range(0.8f, 1.3f)), rock);
                if (i % 2 == 0)
                {
                    ProceduralMesh.Place("Stalagmite", decor.Root, spike, new Vector3(x + decor.Range(-0.6f, 0.6f), CaveFloor - 0.05f, decor.Range(1.2f, 2.6f)),
                        Quaternion.Euler(0f, decor.Range(0f, 360f), 0f), new Vector3(decor.Range(1f, 1.6f), decor.Range(0.4f, 0.9f), decor.Range(1f, 1.6f)), rock);
                }
                x += decor.Range(1.1f, 2.3f);
                i++;
            }
            // 床の岩（奥）と、ぼんやり光る茸（青緑の柄と紫の傘）。
            for (int k = 0; k < 9; k++)
            {
                float rx = decor.Range(CaveLeft + 1f, CaveRight - 1f);
                decor.Sphere("CaveRock", new Vector3(rx, CaveFloor + 0.15f, decor.Range(1.4f, 2.8f)),
                    new Vector3(decor.Range(0.6f, 1.4f), decor.Range(0.35f, 0.7f), decor.Range(0.6f, 1.2f)), rock);
            }
            // 光る茸は、洞窟の左の手前に大きい株を 1 つと、その脇に小さい株を 1 つだけ（2026-11-20 の要望: 壁に埋まっていて、並びが
            // 均等すぎた）。株は大きさの違う茸を寄せて、傘は丸い回転体。
            Mesh capMesh = ProceduralMesh.Lathe("Manor", "FungusCap", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.95f, 0.05f), new Vector2(1f, 0.18f), new Vector2(0.85f, 0.42f), new Vector2(0.5f, 0.62f), new Vector2(0f, 0.7f),
            }, 20);
            void Cluster(Vector3 at, float size, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    float a = k * 2.4f;
                    float spread = k == 0 ? 0f : size * (0.35f + 0.1f * k);
                    float scale = size * (k == 0 ? 1f : 0.75f - 0.12f * k);
                    var p = at + new Vector3(Mathf.Cos(a) * spread, 0f, Mathf.Sin(a) * spread * 0.6f);
                    float h = scale * 0.9f;
                    decor.Cylinder("FungusStem", p + Vector3.up * h * 0.5f, new Vector3(0.12f * scale, h * 0.5f, 0.12f * scale), glow)
                        .transform.rotation = Quaternion.Euler(0f, 0f, (k % 2 == 0 ? 1f : -1f) * 6f * k);
                    ProceduralMesh.Place("FungusCap", decor.Root, capMesh, p + Vector3.up * h, Quaternion.Euler(0f, a * 30f, (k % 2 == 0 ? 1f : -1f) * 5f * k),
                        Vector3.one * 0.42f * scale, cap);
                }
            }
            Cluster(new Vector3(CaveLeft + 3.2f, CaveFloor + 0.05f, 1.1f), 1.25f, 5);
            Cluster(new Vector3(CaveLeft + 5.1f, CaveFloor + 0.05f, 1.3f), 0.6f, 3);
        }

        // 格子: 黒い鉄の縦の棒と、上下の横の帯。
        private static void BarsLooks(StageDecor decor, Transform visual, PlatformSpec spec)
        {
            Material iron = decor.Lit("PrisonIron", new Color(0.12f, 0.12f, 0.14f));
            for (int i = 0; i < 3; i++)
            {
                decor.Box("Bar", visual, new Vector3(0f, 0f, -1.5f + i * 1.5f), new Vector3(0.18f, spec.Size.y, 0.18f), iron);
            }
            decor.Box("BandTop", visual, new Vector3(0f, spec.Size.y * 0.5f - 0.2f, 0f), new Vector3(spec.Size.x, 0.25f, 3.8f), iron);
            decor.Box("BandBottom", visual, new Vector3(0f, -spec.Size.y * 0.5f + 0.2f, 0f), new Vector3(spec.Size.x, 0.25f, 3.8f), iron);
            decor.Box("BandMiddle", visual, new Vector3(0f, 0f, -1.9f), new Vector3(spec.Size.x * 0.9f, 0.15f, 0.1f), iron);
        }

        private const string PropFolder = "Assets/_Project/Materials/Stages/Manor";

        // 背景の小物の色合い（夜の月明かりの中で明るすぎないように少し暗く）と、奥の森の松（黒い針葉樹に見えるように暗く）。
        /// <summary>
        /// 鬼火の輪が飛び回る 6 地点（輪の中心）: 左の牢屋の中、右の牢屋の中、館の屋根の頂点（鐘楼の上）、バルコニーの真ん中、左下の納骨堂の中、
        /// 右下のトマト農園の真上（カカシの腕より上）。
        /// </summary>
        internal static readonly Vector2[] WispPoints =
        {
            new Vector2(-40f, 30.1f), new Vector2(40f, 30.1f), new Vector2(0f, 35.2f), new Vector2(0f, 12.6f), new Vector2(-31f, 2.5f), new Vector2(36f, 7.5f),
        };

        private static readonly Color PropTint = new Color(0.62f, 0.58f, 0.68f);
        private static readonly Color ForestTint = new Color(0.16f, 0.14f, 0.2f);

        /// <summary>奥の森を置く奥行きの範囲（m）と、森の地面の高さ。</summary>
        public const float ForestNear = 14f;
        public const float ForestFar = 70f;
        public const float ForestGround = 0f;

        // 霧の板（下ほど濃く上へ薄れ、流れる）: (名前, 中心, 大きさ, 奥行き z, 濃さ, 流れ m/s)。手前の 2 枚は足もとだけの薄い地霧
        // （遊ぶ所を隠さない）、遊ぶ面のすぐ奥は濃い霧、墓場と谷には溜まった霧、奥の森は奥ほど高く濃い霧の層で根元を沈める。
        private static readonly (string Name, Vector2 Center, Vector2 Size, float Z, float Density, Vector2 Drift)[] FogLayers =
        {
            ("FogFront", new Vector2(0f, 1.2f), new Vector2(160f, 8f), -4.5f, 0.42f, new Vector2(0.5f, 0.03f)),
            ("FogFrontLow", new Vector2(0f, 0.2f), new Vector2(160f, 5f), -3.2f, 0.34f, new Vector2(-0.35f, 0.02f)),
            ("FogBehind", new Vector2(0f, 2f), new Vector2(170f, 12f), 4.6f, 0.72f, new Vector2(0.4f, 0.04f)),
            ("FogGraveyard", new Vector2(-36f, 1.4f), new Vector2(34f, 6f), 3.1f, 0.85f, new Vector2(0.25f, 0.02f)),
            ("FogAbyss", new Vector2(0f, -12f), new Vector2(64f, 16f), 6.5f, 0.95f, new Vector2(-0.3f, 0.06f)),
            ("FogForestNear", new Vector2(0f, 4f), new Vector2(230f, 16f), 16f, 0.85f, new Vector2(0.35f, 0.03f)),
            ("FogForestMid", new Vector2(0f, 6f), new Vector2(270f, 22f), 30f, 0.92f, new Vector2(-0.3f, 0.03f)),
            ("FogForestFar", new Vector2(0f, 8f), new Vector2(330f, 30f), 50f, 1.0f, new Vector2(0.28f, 0.02f)),
            ("FogForestBack", new Vector2(0f, 10f), new Vector2(400f, 40f), 72f, 1.1f, new Vector2(-0.22f, 0.02f)),
        };

        // 霧の色（月明かりの薄紫）と、地面の近くの色（青白い緑。怪しい空気）。
        private static readonly Color FogColor = new Color(0.56f, 0.5f, 0.68f, 0.62f);
        private static readonly Color FogLowColor = new Color(0.44f, 0.58f, 0.5f, 1f);

        private static void Atmosphere(StageDecor decor)
        {
            Transform root = new GameObject("Atmosphere").transform;
            root.SetParent(decor.Root, false);
            for (int i = 0; i < FogLayers.Length; i++)
            {
                var layer = FogLayers[i];
                int index = i;
                Material material = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial(layer.Name, "Airerage/FogSheet", m =>
                {
                    m.SetColor("_Color", FogColor);
                    m.SetColor("_LowColor", FogLowColor);
                    m.SetFloat("_Density", layer.Density);
                    m.SetFloat("_NoiseScale", layer.Z > 10f ? 0.045f : 0.09f);
                    m.SetVector("_Drift", new Vector4(layer.Drift.x, layer.Drift.y, 0f, 0f));
                    m.SetFloat("_TopFade", 0.75f);
                    m.SetFloat("_BottomFade", layer.Z < 0f ? 0f : 0.04f);
                    m.SetFloat("_EdgeFade", 0.1f);
                    m.SetFloat("_Seed", index + 1);
                }, PropFolder);
                GameObject sheet = decor.Quad(layer.Name, new Vector3(layer.Center.x, layer.Center.y, layer.Z), layer.Size, material);
                sheet.transform.SetParent(root, true);
                sheet.GetComponent<Renderer>().receiveShadows = false;
            }

            // 赤い満月から差す、ごく薄い光の筋（右上から左下へ。足し合わせて描く）。
            Material ray = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("MoonRay", "Airerage/CityGlow", m =>
            {
                m.SetColor("_Color", new Color(0.2f, 0.04f, 0.035f));
                m.SetFloat("_Falloff", 1.6f);
                m.SetFloat("_EdgeSoftness", 0.45f);
                m.SetFloat("_Shimmer", 0.3f);
            }, PropFolder);
            (float X, float Width)[] rays = { (12f, 7f), (26f, 11f), (41f, 6f) };
            foreach (var r in rays)
            {
                GameObject shaft = decor.Quad("MoonRay", new Vector3(r.X, 46f, 38f), new Vector2(r.Width, 120f), ray);
                shaft.transform.rotation = Quaternion.Euler(0f, 0f, 160f);
                shaft.transform.SetParent(root, true);
            }

            // 鬼火（墓場と谷にゆらゆら漂う青白い光の粒）。
            Material wisp = SandboxWireSceneBuilder.LoadOrCreateShaderMaterial("Wisp", "Airerage/Wisp", m =>
            {
                m.SetColor("_Color", new Color(0.9f, 1.7f, 1.4f));
                m.SetFloat("_Core", 10f);
            }, PropFolder);
            Wisps(root, wisp, "WispsGraveyard", new Vector3(-36f, 2.5f, 5.5f), new Vector3(24f, 4f, 5f), 1.1f);
            Wisps(root, wisp, "WispsAbyss", new Vector3(0f, -9f, 5f), new Vector3(40f, 6f, 4f), 0.7f);
        }

        // ゆっくり漂って明滅する光の粒（見た目だけ。試合の結果には関わらない）。
        private static void Wisps(Transform parent, Material material, string name, Vector3 center, Vector3 size, float rate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var particles = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 1f, 0.9f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // 館の色（こげ茶の壁・さらに暗い縁取り・屋根）、紫のアクセント、窓の紫の光（ブルームでにじむ明るさ）。
        private static readonly Color ManorWall = new Color(0.25f, 0.15f, 0.1f);
        private static readonly Color ManorTrim = new Color(0.15f, 0.09f, 0.06f);
        private static readonly Color ManorRoofTile = new Color(0.12f, 0.07f, 0.08f);
        private static readonly Color ManorAccent = new Color(0.45f, 0.17f, 0.56f);
        private static readonly Color ManorGlow = new Color(1.5f, 0.5f, 2.2f);

        /// <summary>
        /// 館（2026-11-19 の要望: 大きさを足場に合わせ、洋館のパーツをそろえる）。2 階建ての本館に、マンサード屋根（急な下の斜面と
        /// ドーマー、平らな上の屋上と手すり）、真ん中の塔屋（時計と鐘の開口と尖塔）、左右の小塔（とんがり屋根）、玄関のポーチ（柱と
        /// 2 階のバルコニー、バラスターの手すり）、1 階の左右のベイウィンドウ、2 階の真ん中のペディメント、煙突。
        /// 階の高さを足場に合わせる: ポーチの床 = 地面の足場（y 2）、ポーチの屋根 = バルコニーの足場（y 10.6）、屋上 = 左右の屋根の足場
        /// （y 20.8）、塔屋 = 鐘楼の足場。足場（z ±2）のすぐ奥に正面（z 4.5）を置き、足場と館の間は床と屋根の延長でつなぐ。
        /// 見た目だけ（当たりはない）。
        /// </summary>
        private static void ManorHouse(StageDecor decor)
        {
            Material wall = decor.Lit("ManorWall", ManorWall);
            Material trim = decor.Lit("ManorTrim", ManorTrim);
            Material roof = decor.Lit("ManorRoofTile", ManorRoofTile);
            Material accent = decor.Lit("ManorAccent", ManorAccent);
            Material stone = decor.Lit("ManorStone", new Color(0.3f, 0.27f, 0.27f));
            Material glow = decor.Unlit("ManorWindowPurple", ManorGlow);
            Mesh prism = TerrainSculpt.Prism("Manor", "Prism");
            Transform root = new GameObject("ManorHouse").transform;
            root.SetParent(decor.Root, false);
            GameObject Box(string name, Vector3 center, Vector3 size, Material material)
            {
                return decor.Box(name, root, center, size, material);
            }
            // 三角柱（底の中心 baseCenter、幅・高さ・奥行き）。切妻・ペディメント・ドーマーの屋根。
            GameObject Gable(string name, Vector3 baseCenter, float width, float height, float depth, Material material)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = baseCenter;
                go.transform.localScale = new Vector3(width, height, depth);
                go.AddComponent<MeshFilter>().sharedMesh = prism;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                return go;
            }
            // 縦に伸ばした菱形（尖塔・とんがり屋根）。下の半分は塔の中に隠れる。
            void Spire(string name, Vector3 center, float width, float depth, float stretch, Material material)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root, false);
                pivot.localPosition = center;
                pivot.localScale = new Vector3(1f, stretch, 1f);
                float side = width / Mathf.Sqrt(2f);
                decor.Box(name + "Shape", pivot, Vector3.zero, new Vector3(side, side, depth), material).transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            GameObject Disc(string name, Vector3 center, float diameter, Material material)
            {
                GameObject go = decor.Cylinder(name, root, center, new Vector3(diameter, 0.04f, diameter), material);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                return go;
            }
            // アーチの窓（縁取り・紫の光・十字の桟・窓台）。front は壁の手前の面の z。
            void ArchWindow(string name, Vector2 center, float width, float height, float front)
            {
                float z = front - 0.06f;
                Box(name + "Frame", new Vector3(center.x, center.y, z + 0.02f), new Vector3(width + 0.5f, height + 0.4f, 0.12f), trim);
                Disc(name + "ArchFrame", new Vector3(center.x, center.y + height * 0.5f, z + 0.02f), width + 0.5f, trim);
                Box(name + "Glass", new Vector3(center.x, center.y, z - 0.03f), new Vector3(width, height, 0.05f), glow);
                Disc(name + "Arch", new Vector3(center.x, center.y + height * 0.5f, z - 0.03f), width, glow);
                Box(name + "Mullion", new Vector3(center.x, center.y + width * 0.15f, z - 0.07f), new Vector3(0.12f, height + width * 0.5f, 0.06f), trim);
                Box(name + "Transom", new Vector3(center.x, center.y + height * 0.12f, z - 0.07f), new Vector3(width, 0.12f, 0.06f), trim);
                Box(name + "Sill", new Vector3(center.x, center.y - height * 0.5f - 0.2f, z - 0.2f), new Vector3(width + 0.8f, 0.22f, 0.5f), accent);
            }
            // バラスターの手すり（x0〜x1、床の高さ floor、z。上下の横木と、つぼ形の小柱）。
            void Balustrade(string name, float x0, float x1, float floor, float z, float height)
            {
                Box(name + "Rail", new Vector3((x0 + x1) * 0.5f, floor + height - 0.07f, z), new Vector3(x1 - x0, 0.14f, 0.22f), trim);
                Box(name + "Base", new Vector3((x0 + x1) * 0.5f, floor + 0.06f, z), new Vector3(x1 - x0, 0.12f, 0.2f), trim);
                int count = Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 0.42f));
                for (int i = 0; i <= count; i++)
                {
                    float x = Mathf.Lerp(x0 + 0.15f, x1 - 0.15f, i / (float)count);
                    decor.Cylinder(name + "Baluster", root, new Vector3(x, floor + height * 0.5f, z), new Vector3(0.11f, (height - 0.26f) * 0.5f, 0.11f), accent);
                    decor.Sphere(name + "BalusterBelly", root, new Vector3(x, floor + height * 0.42f, z), new Vector3(0.18f, 0.22f, 0.18f), accent);
                }
            }

            const float front = 4.5f;
            const float depth = 6.5f;
            const float halfWidth = 14f;
            float midZ = front + depth * 0.5f;
            // 土台（足場の地面の高さ y 2 まで。下は谷へ続く石積み）と本館（y 2〜16）。
            Box("ManorFoundation", new Vector3(0f, -4f, midZ), new Vector3(halfWidth * 2f + 1f, 12f, depth + 0.6f), stone);
            Box("ManorHall", new Vector3(0f, 9f, midZ), new Vector3(halfWidth * 2f, 14f, depth), wall);
            // 足場と館をつなぐ床（ポーチの床）。
            Box("ManorPorchDeck", new Vector3(0f, 1.8f, (2f + front) * 0.5f), new Vector3(halfWidth * 2f, 0.4f, front - 2f), stone);
            // 付け柱、階の帯（ポーチの屋根の高さ）、軒の蛇腹（紫の線）。
            foreach (float x in new[] { -halfWidth, -7.6f, 7.6f, halfWidth })
            {
                Box("ManorPilaster", new Vector3(x, 9f, front - 0.15f), new Vector3(0.7f, 14f, 0.3f), trim);
            }
            Box("ManorStringCourse", new Vector3(0f, 10f, front - 0.2f), new Vector3(halfWidth * 2f + 0.4f, 0.4f, 0.4f), accent);
            Box("ManorCornice", new Vector3(0f, 16.2f, front - 0.3f), new Vector3(halfWidth * 2f + 1.2f, 0.7f, 0.7f), trim);
            Box("ManorCorniceLine", new Vector3(0f, 15.7f, front - 0.35f), new Vector3(halfWidth * 2f + 0.6f, 0.16f, 0.2f), accent);

            // 1 階: 真ん中の両開きの扉と明かり取り、扉の左右の窓、左右のベイウィンドウ（3 面に張り出した出窓）。
            Box("ManorDoor", new Vector3(0f, 4.6f, front - 0.05f), new Vector3(2.6f, 5.2f, 0.15f), trim);
            Box("ManorDoorSplit", new Vector3(0f, 4.6f, front - 0.14f), new Vector3(0.1f, 5.2f, 0.05f), accent);
            Disc("ManorDoorArchFrame", new Vector3(0f, 7.2f, front - 0.04f), 3.1f, trim);
            Disc("ManorDoorFanlight", new Vector3(0f, 7.2f, front - 0.1f), 2.4f, glow);
            foreach (float x in new[] { -4.4f, 4.4f })
            {
                ArchWindow("ManorWindowLow", new Vector2(x, 5.6f), 1.5f, 3.2f, front);
            }
            foreach (float side in new[] { -1f, 1f })
            {
                float cx = side * 10.6f;
                const float bayDepth = 1.6f;
                float bayFront = front - bayDepth;
                // 正面の面と、45° の左右の面（それぞれに光る窓）。
                Box("ManorBayFront", new Vector3(cx, 5.4f, bayFront + 0.15f), new Vector3(3f, 6.4f, 0.3f), wall);
                Box("ManorBayFrontGlass", new Vector3(cx, 5.6f, bayFront - 0.02f), new Vector3(2f, 3.8f, 0.05f), glow);
                Box("ManorBayFrontMullion", new Vector3(cx, 5.6f, bayFront - 0.06f), new Vector3(0.12f, 3.8f, 0.05f), trim);
                foreach (float s2 in new[] { -1f, 1f })
                {
                    var center = new Vector3(cx + s2 * (1.5f + bayDepth * 0.5f), 5.4f, front - bayDepth * 0.5f);
                    GameObject flank = Box("ManorBaySide", center, new Vector3(bayDepth * 1.414f, 6.4f, 0.3f), wall);
                    flank.transform.localRotation = Quaternion.Euler(0f, s2 * 45f, 0f);
                    GameObject pane = Box("ManorBaySideGlass", center + new Vector3(0f, 0.2f, -0.16f), new Vector3(bayDepth * 0.9f, 3.8f, 0.05f), glow);
                    pane.transform.localRotation = Quaternion.Euler(0f, s2 * 45f, 0f);
                }
                Box("ManorBaySill", new Vector3(cx, 3.4f, bayFront + 0.6f), new Vector3(3.6f + bayDepth, 0.25f, bayDepth + 0.4f), accent);
                Box("ManorBayRoof", new Vector3(cx, 8.8f, bayFront + 0.7f), new Vector3(3.8f + bayDepth, 0.35f, bayDepth + 0.8f), roof);
                Box("ManorBayRoofEdge", new Vector3(cx, 8.6f, bayFront - 0.08f), new Vector3(3.6f, 0.14f, 0.14f), accent);
            }

            // ポーチ: 4 本の柱（柱頭つき）が 2 階のバルコニー（ポーチの屋根）を支える。バルコニーの床は足場（x ±7、y 10〜10.6）の奥へ
            // 延ばして正面につなぎ、足場の手前の縁にバラスターの手すり（低く、細く。人物を隠しすぎない）。
            foreach (float x in new[] { -6.6f, -2.2f, 2.2f, 6.6f })
            {
                decor.Cylinder("ManorPorchColumn", root, new Vector3(x, 6f, 2.6f), new Vector3(0.6f, 4f, 0.6f), stone);
                Box("ManorPorchCapital", new Vector3(x, 9.85f, 2.6f), new Vector3(0.9f, 0.3f, 0.9f), trim);
                Box("ManorPorchBase", new Vector3(x, 2.15f, 2.6f), new Vector3(0.85f, 0.3f, 0.85f), trim);
            }
            Box("ManorBalconyDeck", new Vector3(0f, 10.3f, (2f + front) * 0.5f), new Vector3(14f, 0.6f, front - 2f), stone);
            Box("ManorBalconyEdge", new Vector3(0f, 9.95f, -2.05f), new Vector3(14.2f, 0.25f, 0.1f), accent);
            Balustrade("ManorBalcony", -7f, 7f, 10.6f, -1.95f, 0.75f);
            // 2 階: 窓 4 つと、真ん中のバルコニーへの両開きの窓（フランス窓）と、その上のペディメント（三角の破風と紫の円窓）。
            foreach (float x in new[] { -10.6f, -4.4f, 4.4f, 10.6f })
            {
                ArchWindow("ManorWindowHigh", new Vector2(x, 13f), 1.4f, 2.6f, front);
            }
            Box("ManorFrenchDoorFrame", new Vector3(0f, 12.9f, front - 0.04f), new Vector3(2.8f, 4.4f, 0.12f), trim);
            Box("ManorFrenchDoorGlass", new Vector3(0f, 12.9f, front - 0.1f), new Vector3(2.2f, 3.9f, 0.05f), glow);
            Box("ManorFrenchDoorSplit", new Vector3(0f, 12.9f, front - 0.14f), new Vector3(0.1f, 3.9f, 0.05f), trim);
            Box("ManorPedimentBase", new Vector3(0f, 16.7f, front - 0.45f), new Vector3(7.6f, 0.45f, 0.9f), trim);
            Gable("ManorPediment", new Vector3(0f, 16.9f, front - 0.45f), 7.4f, 2.4f, 0.8f, wall);
            Gable("ManorPedimentCap", new Vector3(0f, 16.9f, front - 0.55f), 7.9f, 2.65f, 0.6f, trim).transform.localPosition += new Vector3(0f, 0f, 0.4f);
            Disc("ManorPedimentOculusFrame", new Vector3(0f, 17.8f, front - 0.88f), 1.4f, trim);
            Disc("ManorPedimentOculus", new Vector3(0f, 17.8f, front - 0.92f), 1f, glow);

            // マンサード屋根: 急な下の斜面（y 16〜20.6）と平らな屋上（y 20.8 = 左右の屋根の足場）。屋上の手前の縁に手すり、
            // 斜面にドーマー（小さな切妻の屋根の窓）。足場（z ±2）と屋上の間は屋根の延長でつなぐ。
            const float slopeRise = 4.6f;
            const float slopeRun = 1.6f;
            float slopeLength = Mathf.Sqrt(slopeRise * slopeRise + slopeRun * slopeRun);
            float slopeAngle = Mathf.Atan2(slopeRun, slopeRise) * Mathf.Rad2Deg;
            GameObject mansard = Box("ManorMansard", new Vector3(0f, 16f + slopeRise * 0.5f, front + slopeRun * 0.5f), new Vector3(halfWidth * 2f + 0.6f, slopeLength, 0.5f), roof);
            mansard.transform.localRotation = Quaternion.Euler(slopeAngle, 0f, 0f);
            Box("ManorMansardBack", new Vector3(0f, 18.4f, front + depth - 0.3f), new Vector3(halfWidth * 2f, 4.8f, 0.6f), roof);
            Box("ManorRoofDeck", new Vector3(0f, 20.6f, front + (slopeRun + depth) * 0.5f), new Vector3(halfWidth * 2f, 0.4f, depth - slopeRun), roof);
            Box("ManorRoofLedge", new Vector3(0f, 20.4f, (2f + front + slopeRun) * 0.5f), new Vector3(halfWidth * 2f, 0.8f, front + slopeRun - 2f), trim);
            Balustrade("ManorRoofCrestWest", -halfWidth, -2.8f, 20.8f, front + slopeRun + 0.2f, 0.9f);
            Balustrade("ManorRoofCrestEast", 2.8f, halfWidth, 20.8f, front + slopeRun + 0.2f, 0.9f);
            foreach (float x in new[] { -11f, -6.4f, 6.4f, 11f })
            {
                Box("ManorDormer", new Vector3(x, 18.1f, front + 0.2f), new Vector3(2f, 2.6f, 1.8f), wall);
                Box("ManorDormerFrame", new Vector3(x, 18f, front - 0.72f), new Vector3(1.5f, 1.9f, 0.1f), trim);
                Box("ManorDormerGlass", new Vector3(x, 18f, front - 0.78f), new Vector3(1.1f, 1.5f, 0.05f), glow);
                Gable("ManorDormerRoof", new Vector3(x, 19.4f, front + 0.1f), 2.6f, 1.1f, 2.2f, roof);
            }
            // 煙突（右は足場の煙突と同じ場所。左は奥）。
            Box("ManorChimney", new Vector3(10f, 22.4f, 3.5f), new Vector3(2f, 3.2f, 3f), stone);
            Box("ManorChimneyCap", new Vector3(10f, 24.1f, 3.5f), new Vector3(2.4f, 0.3f, 3.2f), accent);
            Box("ManorChimneyBack", new Vector3(-10f, 22.6f, 8.5f), new Vector3(1.6f, 3.6f, 1.6f), stone);
            Box("ManorChimneyBackCap", new Vector3(-10f, 24.5f, 8.5f), new Vector3(2f, 0.3f, 2f), accent);

            // 塔屋（真ん中。足場の鐘楼 x ±2 のすぐ奥。時計・鐘の開口・塔の蛇腹）と、その上の尖塔（足場の鐘楼の笠 y 32.6 から）。
            Box("ManorTower", new Vector3(0f, 24.3f, 6.2f), new Vector3(5.2f, 16.6f, 4.4f), wall);
            Box("ManorTowerLink", new Vector3(0f, 26f, 3f), new Vector3(4f, 12f, 2f), wall);
            Box("ManorTowerBand", new Vector3(0f, 21.2f, 4f), new Vector3(5.6f, 0.4f, 0.4f), accent);
            Disc("ManorClockFrame", new Vector3(0f, 24f, 3.96f), 2.6f, trim);
            Disc("ManorClock", new Vector3(0f, 24f, 3.92f), 2.1f, glow);
            Box("ManorClockHandHour", new Vector3(0.25f, 24.3f, 3.88f), new Vector3(0.12f, 0.7f, 0.04f), trim).transform.localRotation = Quaternion.Euler(0f, 0f, -40f);
            Box("ManorClockHandMinute", new Vector3(-0.1f, 24.45f, 3.86f), new Vector3(0.08f, 0.95f, 0.04f), trim).transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            foreach (float x in new[] { -1.1f, 1.1f })
            {
                ArchWindow("ManorBelfry", new Vector2(x, 28.6f), 0.9f, 2.4f, 4f);
            }
            Box("ManorTowerCornice", new Vector3(0f, 32.3f, 5.6f), new Vector3(6.4f, 0.6f, 5.4f), trim);
            Spire("ManorSpire", new Vector3(0f, 32.6f, 6.2f), 6f, 4.4f, 2.1f, roof);
            decor.Cylinder("ManorSpireFinial", root, new Vector3(0f, 41.6f, 6.2f), new Vector3(0.16f, 1.4f, 0.16f), trim);

            // 左右の小塔（本館の外。細い光る窓・紫の帯・とんがり屋根と頂の針）。ガーゴイルのアンカー（x ±17、y 18）が角に来る。
            foreach (float x in new[] { -16.7f, 16.7f })
            {
                Box("ManorTurret", new Vector3(x, 6f, 7f), new Vector3(5.2f, 32f, 5.2f), wall);
                Box("ManorTurretBand", new Vector3(x, 21.7f, 7f), new Vector3(5.7f, 0.5f, 5.7f), accent);
                Box("ManorTurretBandLow", new Vector3(x, 10f, 7f), new Vector3(5.5f, 0.35f, 5.5f), trim);
                foreach (float y in new[] { 5.6f, 13f, 18.4f })
                {
                    ArchWindow("ManorTurretWindow", new Vector2(x, y), 0.9f, 2.2f, 4.4f);
                }
                Spire("ManorTurretRoof", new Vector3(x, 22f, 7f), 6.4f, 6.4f, 1.6f, roof);
                decor.Cylinder("ManorFinial", root, new Vector3(x, 27.6f, 7f), new Vector3(0.12f, 0.9f, 0.12f), trim);
            }
        }

        /// <summary>トマト農園（x 25〜47、地面 y 0、奥行き z 2〜10）: 3 列の畝に、支柱と葉の茂みと赤い実のトマトの株。</summary>
        private static void TomatoFarm(StageDecor decor)
        {
            Material soil = decor.Lit("FarmSoil", new Color(0.16f, 0.11f, 0.08f));
            Material stake = decor.Lit("TomatoStake", new Color(0.42f, 0.32f, 0.22f));
            var leafTint = new Color(0.45f, 0.62f, 0.42f);
            var fruitTint = new Color(0.95f, 0.85f, 0.85f);
            foreach (float z in new[] { 3.4f, 5.6f, 7.8f })
            {
                decor.Box("FarmRow", new Vector3(36f, 0.12f, z), new Vector3(22f, 0.3f, 1.1f), soil);
                for (float x = 26f; x <= 46.1f; x += 2.5f)
                {
                    float jitter = decor.Range(-0.3f, 0.3f);
                    float px = x + jitter;
                    decor.Cylinder("TomatoStake", new Vector3(px, 1.15f, z + 0.05f), new Vector3(0.08f, 1.15f, 0.08f), stake);
                    float[] heights = { 0.25f, 0.85f, 1.45f };
                    for (int k = 0; k < heights.Length; k++)
                    {
                        ThirdPartyProps.Place(decor, ThirdPartyProps.Restaurant, "food_ingredient_lettuce", new Vector3(px + decor.Range(-0.2f, 0.2f), heights[k], z),
                            0.85f - k * 0.12f, decor.Range(0f, 360f), leafTint, PropFolder);
                    }
                    int fruits = 3 + decor.Random.Next(3);
                    for (int k = 0; k < fruits; k++)
                    {
                        ThirdPartyProps.Place(decor, ThirdPartyProps.Restaurant, "food_ingredient_tomato",
                            new Vector3(px + decor.Range(-0.45f, 0.45f), decor.Range(0.45f, 1.6f), z - decor.Range(0.25f, 0.45f)), decor.Range(0.3f, 0.4f),
                            decor.Range(0f, 360f), fruitTint, PropFolder);
                    }
                }
            }
        }

        /// <summary>
        /// ボロボロのカカシ: 十字の柱、縫い目の目と口の麻袋の頭、くたびれたとんがり帽子、継ぎはぎのコートと袖、裾と袖口のぼろぼろの布の切れ端、
        /// はみ出したわら。crow なら腕にカラスがとまる。
        /// </summary>
        private static void Scarecrow(StageDecor decor, float x, float z, float yaw, bool crow)
        {
            Material wood = decor.Lit("ScarecrowWood", new Color(0.3f, 0.22f, 0.15f));
            Material sack = decor.Lit("ScarecrowSack", new Color(0.55f, 0.45f, 0.3f));
            Material stitch = decor.Lit("ScarecrowStitch", new Color(0.08f, 0.06f, 0.05f));
            Material coat = decor.Lit("ScarecrowCoat", new Color(0.27f, 0.16f, 0.2f));
            Material patch = decor.Lit("ScarecrowPatch", new Color(0.42f, 0.32f, 0.22f));
            Material hat = decor.Lit("ScarecrowHat", new Color(0.12f, 0.09f, 0.1f));
            Material straw = decor.Lit("Straw", new Color(0.75f, 0.62f, 0.3f));
            var root = new GameObject("Scarecrow").transform;
            root.SetParent(decor.Root, false);
            root.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f));
            decor.Cylinder("ScarecrowPost", root, new Vector3(0f, 2.6f, 0f), new Vector3(0.18f, 2.6f, 0.18f), wood);
            decor.Box("ScarecrowBar", root, new Vector3(0f, 4f, 0f), new Vector3(4.4f, 0.2f, 0.2f), wood);
            // 頭（麻袋）と、×の目と縫い目の口。
            decor.Sphere("ScarecrowHead", root, new Vector3(0f, 5.25f, 0f), new Vector3(1.05f, 1.2f, 0.95f), sack);
            foreach (float ex in new[] { -0.22f, 0.22f })
            {
                foreach (float angle in new[] { 45f, -45f })
                {
                    decor.Box("ScarecrowEye", root, new Vector3(ex, 5.4f, -0.46f), new Vector3(0.28f, 0.05f, 0.04f), stitch).transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
            }
            decor.Box("ScarecrowMouth", root, new Vector3(0f, 5.0f, -0.45f), new Vector3(0.5f, 0.04f, 0.04f), stitch).transform.localRotation = Quaternion.Euler(0f, 0f, -6f);
            for (int i = 0; i < 5; i++)
            {
                decor.Box("ScarecrowMouthStitch", root, new Vector3(-0.2f + i * 0.1f, 5.0f, -0.46f), new Vector3(0.03f, 0.14f, 0.04f), stitch);
            }
            // くたびれたとんがり帽子（つば・山・折れた先）。
            GameObject brim = decor.Cylinder("ScarecrowHatBrim", root, new Vector3(0.05f, 5.78f, 0f), new Vector3(1.7f, 0.03f, 1.5f), hat);
            brim.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            decor.Cylinder("ScarecrowHatCrown", root, new Vector3(0.08f, 6.15f, 0f), new Vector3(0.8f, 0.35f, 0.75f), hat).transform.localRotation = Quaternion.Euler(0f, 0f, -6f);
            decor.Cylinder("ScarecrowHatTip", root, new Vector3(0.3f, 6.65f, 0f), new Vector3(0.45f, 0.25f, 0.42f), hat).transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            // コート（胴と袖）と継ぎはぎ。
            decor.Box("ScarecrowCoat", root, new Vector3(0f, 3.45f, 0f), new Vector3(1.5f, 2f, 0.7f), coat);
            foreach (int s in new[] { -1, 1 })
            {
                decor.Box("ScarecrowSleeve", root, new Vector3(s * 1.25f, 4f, 0f), new Vector3(1.6f, 0.55f, 0.55f), coat);
            }
            decor.Box("ScarecrowPatch", root, new Vector3(-0.35f, 3.7f, -0.36f), new Vector3(0.45f, 0.4f, 0.03f), patch).transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
            decor.Box("ScarecrowPatch", root, new Vector3(0.9f, 4.05f, -0.29f), new Vector3(0.35f, 0.3f, 0.03f), patch).transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
            // 裾と袖口のぼろぼろの布の切れ端（長さと角度をばらばらに）。
            for (int i = 0; i < 7; i++)
            {
                float hx = -0.65f + i * 0.22f;
                float length = decor.Range(0.45f, 1f);
                decor.Box("ScarecrowRag", root, new Vector3(hx, 2.45f - length * 0.5f, 0f), new Vector3(0.2f, length, 0.06f), coat).transform.localRotation =
                    Quaternion.Euler(0f, 0f, decor.Range(-14f, 14f));
            }
            foreach (int s in new[] { -1, 1 })
            {
                for (int i = 0; i < 3; i++)
                {
                    float length = decor.Range(0.3f, 0.65f);
                    decor.Box("ScarecrowRag", root, new Vector3(s * (1.75f + i * 0.13f), 3.72f - length * 0.5f, 0f), new Vector3(0.12f, length, 0.05f), coat).transform.localRotation =
                        Quaternion.Euler(0f, 0f, decor.Range(-18f, 18f));
                }
                // 袖口と首のわら。
                for (int i = 0; i < 4; i++)
                {
                    decor.Cylinder("ScarecrowStraw", root, new Vector3(s * (2.1f + decor.Range(0f, 0.15f)), 4f + decor.Range(-0.15f, 0.15f), 0f), new Vector3(0.05f, 0.22f, 0.05f), straw).transform.localRotation =
                        Quaternion.Euler(0f, 0f, s * decor.Range(55f, 110f));
                }
            }
            for (int i = 0; i < 5; i++)
            {
                decor.Cylinder("ScarecrowStraw", root, new Vector3(decor.Range(-0.3f, 0.3f), 4.62f, 0f), new Vector3(0.05f, 0.2f, 0.05f), straw).transform.localRotation =
                    Quaternion.Euler(0f, 0f, decor.Range(-50f, 50f));
            }
            if (crow)
            {
                // 腕にとまるカラス。
                Material black = decor.Lit("Crow", new Color(0.04f, 0.04f, 0.05f));
                decor.Sphere("CrowBody", root, new Vector3(1.6f, 4.42f, 0f), new Vector3(0.42f, 0.32f, 0.3f), black);
                decor.Sphere("CrowHead", root, new Vector3(1.82f, 4.62f, 0f), new Vector3(0.2f, 0.2f, 0.2f), black);
                decor.Box("CrowBeak", root, new Vector3(1.97f, 4.6f, 0f), new Vector3(0.14f, 0.05f, 0.05f), decor.Lit("CrowBeak", new Color(0.3f, 0.26f, 0.15f)));
                decor.Box("CrowTail", root, new Vector3(1.33f, 4.4f, 0f), new Vector3(0.3f, 0.06f, 0.16f), black).transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
            }
        }

        private static void Props(StageDecor decor)
        {
            ThirdPartyProps.Pack pack = ThirdPartyProps.Halloween;
            GameObject Put(string model, float x, float y, float z, float scale, float yaw, Color tint)
            {
                return ThirdPartyProps.Place(decor, pack, model, new Vector3(x, y, z), scale, yaw, tint, PropFolder);
            }
            // 墓場（地面 y 0、x -47〜-25、奥行き z 3〜9）の墓石。
            string[] graves = { "gravestone", "grave_A", "grave_B", "gravemarker_A", "gravemarker_B" };
            for (int i = 0; i < 11; i++)
            {
                Put(graves[i % graves.Length], decor.Range(-46.5f, -26f), 0f, decor.Range(3.2f, 9f), decor.Range(1.05f, 1.35f), decor.Range(-25f, 25f), PropTint);
            }
            // 柵（4m ずつ。ところどころ壊れている）と柱、入口の門。墓場の地面（x -48〜-24）の上に収める（門の右側が地面の外で宙に
            // 浮いていた。2026-11-18 の指摘）。
            for (int i = 0; i < 4; i++)
            {
                float x = -45.5f + i * 4f;
                Put(i == 2 ? "fence_broken" : "fence", x, 0f, 2.6f, 1f, 0f, PropTint);
                Put("fence_pillar", x - 2f, 0f, 2.6f, 1f, 0f, PropTint);
            }
            Put("fence_pillar", -31.5f, 0f, 2.6f, 1f, 0f, PropTint);
            Put("arch_gate", -29.4f, 0f, 2.6f, 1f, 0f, PropTint);
            Put("fence_pillar", -27.2f, 0f, 2.6f, 1f, 0f, PropTint);
            Put("fence_broken", -25.7f, 0f, 2.6f, 0.72f, 0f, PropTint);
            // 墓場の枯れ木（前は細い箱）と、ランタン、かぼちゃ。
            Put("tree_dead_large", -43f, 0f, 5f, 2.4f, 30f, PropTint);
            Put("tree_dead_medium", -29f, 0f, 7.5f, 2f, -40f, PropTint);
            Put("post_lantern", -48.4f, 0f, 3.2f, 1.4f, 0f, PropTint);
            Put("lantern_standing", -26.5f, 0f, 3.6f, 1.6f, 0f, PropTint);
            Put("pumpkin_orange_jackolantern", -31.5f, 0f, 3.4f, 1.2f, 15f, PropTint);
            Put("skull", -37.5f, 0f, 3.1f, 1.2f, -20f, PropTint);
            // 農園（x 25〜47）の端のかぼちゃ。
            Put("pumpkin_orange_jackolantern", 24.4f, 0f, 4.2f, 1.3f, -10f, PropTint);
            Put("pumpkin_yellow_jackolantern", 47.6f, 0f, 3.8f, 1.2f, 25f, PropTint);
            Put("pumpkin_orange", 27.5f, 0f, 6.5f, 1.1f, 40f, PropTint);
            Put("pumpkin_orange_small", 44f, 0f, 6.8f, 1.2f, -30f, PropTint);
            // 奥の森（黒い松と枯れ木）。地面は暗い土の板。
            string[] trees = { "tree_pine_orange_large", "tree_dead_large", "tree_pine_orange_medium", "tree_dead_medium", "tree_pine_yellow_large" };
            for (int i = 0; i < 46; i++)
            {
                string model = trees[i % trees.Length];
                bool pine = model.StartsWith("tree_pine");
                Put(model, decor.Range(-88f, 88f), ForestGround, decor.Range(ForestNear, ForestFar), decor.Range(2.2f, 4f), decor.Range(0f, 360f), pine ? ForestTint : PropTint * 0.6f);
            }
            decor.Box("ForestFloor", new Vector3(0f, ForestGround - 0.5f, (ForestNear + ForestFar) * 0.5f + 2f), new Vector3(190f, 1f, ForestFar - ForestNear + 12f),
                TerrainSculpt.GroundMaterial(decor, "ForestFloorGround", new Color(0.08f, 0.06f, 0.09f), TerrainSculpt.Photos.Soil,
                    new Color(0.09f, 0.07f, 0.09f), TerrainSculpt.Photos.Soil, 0.2f));
        }

        // 館の正面（窓の明かり、尖った屋根）、墓場の柵と墓石、農園のトマトの株、霧、遠くの森。赤い満月は空に描く（StageLook.ApplySky。
        // 2026-11-18 の要望で空をリアルにした。前の平たい赤い円の位置と大きさのまま）。
        private static void Decorate(StageDecor decor)
        {
            Material roof = decor.Lit("ManorRoof", new Color(0.18f, 0.12f, 0.2f));
            Material dirt = TerrainSculpt.GroundMaterial(decor, "DirtGround", new Color(0.18f, 0.13f, 0.1f), TerrainSculpt.Photos.Soil,
                new Color(0.2f, 0.15f, 0.11f), TerrainSculpt.Photos.Soil, 0.25f);
            Material abyss = decor.Lit("Abyss", new Color(0.03f, 0.02f, 0.05f));
            Material prisonStone = decor.Lit("PrisonStone", new Color(0.2f, 0.18f, 0.22f));
            Material chain = decor.Lit("Chain", new Color(0.3f, 0.3f, 0.32f));
            Material candle = decor.Unlit("Candle", new Color(1f, 0.6f, 0.25f));
            Material bone = decor.Lit("Bone", new Color(0.85f, 0.82f, 0.72f));

            // 館（遊ぶ面の奥）。2026-11-18 の要望で精巧な館に: こげ茶が基調、ところどころ紫のアクセント、窓から紫の光が漏れる。
            ManorHouse(decor);
            // 霧・月の光の筋・鬼火（2026-11-18 の要望: 手前の霧をリアルにし、怪しい空気感と雰囲気を出す）。
            Atmosphere(decor);
            // 墓場の丘の奥行き、墓石の並び、柵。
            decor.Box("GraveHillBack", new Vector3(-36f, -6f, 6f), new Vector3(24f, 12f, 8f), dirt);
            // 墓石・柵・枯れ木・門・ランタン・かぼちゃ・奥の森は KayKit Halloween Bits（CC0。2026-11-18 のユーザーの指示で受け取った
            // アセットを組み込む）。
            Props(decor);
            // 農園の奥行き、トマトの株、カカシの頭と服。
            decor.Box("FarmBack", new Vector3(36f, -6f, 6f), new Vector3(24f, 12f, 8f), dirt);
            // トマト農園とボロボロのカカシ（前の黒い球と赤い点〈トマトの株と実〉は消した。2026-11-18 の要望）。
            TomatoFarm(decor);
            Scarecrow(decor, 30.2f, 6.6f, 8f, true);
            Scarecrow(decor, 42.4f, 6.6f, -12f, false);
            decor.Box("Barn", new Vector3(45f, 6f, 4f), new Vector3(6f, 12f, 6f), decor.Lit("Barn", new Color(0.4f, 0.1f, 0.1f)));
            // 谷の奥（底のない暗がり）と、遠くの森。
            decor.Box("AbyssBack", new Vector3(0f, -20f, 8f), new Vector3(140f, 24f, 12f), abyss);
            // 丘の下の洞窟（2026-11-20 の要望: 納骨堂の下の通路を洞窟のような見た目に）: 奥のごつごつした岩壁、天井から垂れる鍾乳石と
            // 床から伸びる石筍、転がる岩、ぼんやり光る茸。ろうそくと骨はそのまま。
            Cave(decor, abyss);
            foreach (float x in new[] { -45f, -38f, -29f })
            {
                decor.Sphere("Candle", new Vector3(x, -4.9f, 1.4f), new Vector3(0.25f, 0.5f, 0.25f), candle);
            }
            decor.Box("Bones", new Vector3(-34f, -5.25f, 1.6f), new Vector3(1.6f, 0.15f, 0.4f), bone);
            // 上空の牢屋: 奥の石の壁と、天井から垂れた鎖、柱、上の小さな屋根。
            foreach (float x in new[] { -40f, 40f })
            {
                decor.Box("PrisonBack", new Vector3(x, 30.1f, 2.6f), new Vector3(10f, 5.6f, 1f), prisonStone);
                decor.Box("PrisonCap", new Vector3(x, 33.6f, 0f), new Vector3(9.4f, 0.6f, 4.2f), roof);
                decor.Box("PrisonBase", new Vector3(x, 26.6f, 0f), new Vector3(9.4f, 0.6f, 4.2f), prisonStone);
                for (int i = 0; i < 2; i++)
                {
                    decor.Box("Chain", new Vector3(x - 2f + i * 4f, 31.5f, 1.8f), new Vector3(0.12f, 2f, 0.12f), chain);
                }
            }
        }
    }
}
