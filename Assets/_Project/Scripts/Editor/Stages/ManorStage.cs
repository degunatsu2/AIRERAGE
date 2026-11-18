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
                    new TerrainBox("Balcony", -6f, 10f, 6f, 10.6f, attachable: true),
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
                    // 悪魔の手（ユーザーの要望、2026-10-26）: 墓場と農園の地面を 16 秒で往復し、触れた人を握って、1.2 秒でその側の牢屋へ移す。
                    new ZoneSpec("DemonHandWest", StageZoneKind.Grab, new Vector2(-45f, 1.2f), new Vector2(2.6f, 2.4f), Vector2.up, 1.2f)
                        .To(prisonWest).Moving(new Vector2(19f, 0f), 16f),
                    new ZoneSpec("DemonHandEast", StageZoneKind.Grab, new Vector2(45f, 1.2f), new Vector2(2.6f, 2.4f), Vector2.up, 1.2f)
                        .To(prisonEast).Moving(new Vector2(-19f, 0f), 16f),
                    // カボチャの発射台: 農園から館の東のバルコニーへ。
                    ZoneSpec.OnFloor("PumpkinLauncher", StageZoneKind.Launcher, 26f, 28f, 0f, new Vector2(-0.5f, 1f), 32f),
                    // 鬼火の輪（加速リング）: 墓場と農園の上から館の屋根へ。
                    new ZoneSpec("WispRingWest", StageZoneKind.BoostRing, new Vector2(-24f, 22f), new Vector2(2.4f, 3f), new Vector2(1f, 0.2f), 18f),
                    new ZoneSpec("WispRingEast", StageZoneKind.BoostRing, new Vector2(24f, 22f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.2f), 18f),
                    // 館のポーチのジャンプ台と、納骨堂の屋根のジャンプ台（ユーザーの要望、2026-10-26）。
                    ZoneSpec.OnFloor("PorchPadWest", StageZoneKind.JumpPad, -11f, -9f, 2f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("PorchPadEast", StageZoneKind.JumpPad, 9f, 11f, 2f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("CryptPad", StageZoneKind.JumpPad, -32f, -30f, 5.6f, Vector2.up, 24f, 0.4f),
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

        // 鎖の昇降台: 古い木の板と鉄の縁、上へ伸びる 2 本の鎖（台と一緒に動く）。
        private static void ChainLiftLooks(StageDecor decor, Transform visual, PlatformSpec spec)
        {
            decor.Box("Plank", visual, Vector3.zero, new Vector3(spec.Size.x, spec.Size.y, 4f), decor.Lit("LiftWood", new Color(0.3f, 0.2f, 0.14f)));
            Material iron = decor.Lit("LiftIron", new Color(0.2f, 0.2f, 0.22f));
            decor.Box("RimFront", visual, new Vector3(0f, -spec.Size.y * 0.25f, -2.05f), new Vector3(spec.Size.x * 1.02f, spec.Size.y * 0.4f, 0.1f), iron);
            foreach (float x in new[] { -spec.Size.x * 0.42f, spec.Size.x * 0.42f })
            {
                decor.Box("Chain", visual, new Vector3(x, 4.2f, 1.7f), new Vector3(0.12f, 8f, 0.12f), iron);
            }
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
                decor.Lit("ForestFloor", new Color(0.08f, 0.06f, 0.09f)));
        }

        // 館の正面（窓の明かり、尖った屋根）、墓場の柵と墓石、農園のトマトの株、霧、遠くの森。赤い満月は空に描く（StageLook.ApplySky。
        // 2026-11-18 の要望で空をリアルにした。前の平たい赤い円の位置と大きさのまま）。
        private static void Decorate(StageDecor decor)
        {
            Material stone = decor.Lit("ManorStone", new Color(0.26f, 0.24f, 0.3f));
            Material roof = decor.Lit("ManorRoof", new Color(0.18f, 0.12f, 0.2f));
            Material windowLight = decor.Unlit("ManorWindow", new Color(1f, 0.8f, 0.35f));
            Material dirt = decor.Lit("Dirt", new Color(0.18f, 0.13f, 0.1f));
            Material leaf = decor.Lit("TomatoLeaf", new Color(0.15f, 0.32f, 0.12f));
            Material tomato = decor.Unlit("Tomato", new Color(0.9f, 0.12f, 0.1f));
            Material straw = decor.Lit("Straw", new Color(0.75f, 0.62f, 0.3f));
            Material abyss = decor.Lit("Abyss", new Color(0.03f, 0.02f, 0.05f));
            Material prisonStone = decor.Lit("PrisonStone", new Color(0.2f, 0.18f, 0.22f));
            Material chain = decor.Lit("Chain", new Color(0.3f, 0.3f, 0.32f));
            Material candle = decor.Unlit("Candle", new Color(1f, 0.6f, 0.25f));
            Material bone = decor.Lit("Bone", new Color(0.85f, 0.82f, 0.72f));

            // 館の正面（遊ぶ面の奥）と窓、尖った屋根。
            decor.Box("ManorFacade", new Vector3(0f, 10f, 6f), new Vector3(30f, 20f, 6f), stone);
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    decor.Quad("Window", new Vector3(-10f + col * 5f, 6f + row * 8f, 2.9f), new Vector2(1.6f, 3f), windowLight);
                }
            }
            GameObject spire = decor.Box("TowerSpire", new Vector3(0f, 36f, 3f), new Vector3(4.5f, 4.5f, 4f), roof);
            spire.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            decor.Box("RoofBack", new Vector3(0f, 22f, 6f), new Vector3(30f, 4f, 6f), roof);
            // 霧・月の光の筋・鬼火（2026-11-18 の要望: 手前の霧をリアルにし、怪しい空気感と雰囲気を出す）。
            Atmosphere(decor);
            // 墓場の丘の奥行き、墓石の並び、柵。
            decor.Box("GraveHillBack", new Vector3(-36f, -6f, 6f), new Vector3(24f, 12f, 8f), dirt);
            // 墓石・柵・枯れ木・門・ランタン・かぼちゃ・奥の森は KayKit Halloween Bits（CC0。2026-11-18 のユーザーの指示で受け取った
            // アセットを組み込む）。
            Props(decor);
            // 農園の奥行き、トマトの株、カカシの頭と服。
            decor.Box("FarmBack", new Vector3(36f, -6f, 6f), new Vector3(24f, 12f, 8f), dirt);
            for (float x = 25f; x <= 47f; x += 2.2f)
            {
                decor.Sphere("TomatoPlant", new Vector3(x, 0.8f, 3f), new Vector3(1.4f, 1.6f, 1.4f), leaf);
                decor.Sphere("TomatoFruit", new Vector3(x + 0.3f, 1f, 2.3f), new Vector3(0.4f, 0.4f, 0.4f), tomato);
            }
            foreach (float x in new[] { 31f, 41f })
            {
                decor.Sphere("ScarecrowHead", new Vector3(x, 6.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), straw);
                decor.Box("ScarecrowCoat", new Vector3(x, 3.6f, 0.3f), new Vector3(2.2f, 2.4f, 0.6f), decor.Lit("ScarecrowCoat", new Color(0.35f, 0.2f, 0.25f)));
            }
            decor.Box("Barn", new Vector3(45f, 6f, 4f), new Vector3(6f, 12f, 6f), decor.Lit("Barn", new Color(0.4f, 0.1f, 0.1f)));
            // 谷の奥（底のない暗がり）と、遠くの森。
            decor.Box("AbyssBack", new Vector3(0f, -20f, 8f), new Vector3(140f, 24f, 12f), abyss);
            // 丘の下の洞窟: 奥の暗い岩壁と、ろうそく、骨。
            decor.Box("CaveBack", new Vector3(-36f, -3.5f, 3f), new Vector3(24f, 3.8f, 2f), abyss);
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
