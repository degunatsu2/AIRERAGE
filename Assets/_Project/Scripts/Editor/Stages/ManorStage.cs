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
    /// ポーチのジャンプ台。消したもの（ユーザーの要望）: 井戸（ワープ）、堀（水）、赤い満月の光（低重力）、棺桶・幽霊・トマト（バンパー）、
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
                Description = "Outside a vampire's manor. Demon hands crawl the graveyard and the farm: get caught and you are dragged into the prison in the sky.",
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
                    new TerrainBox("BalconyWest", -12f, 10f, -6f, 10.6f, attachable: true),
                    new TerrainBox("BalconyEast", 6f, 10f, 12f, 10.6f, attachable: true),
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
                // ガーゴイル、枯れ木の枝先、カカシの頭、鐘。
                Anchors = new[]
                {
                    new Vector2(-14f, 23f), new Vector2(14f, 23f), new Vector2(0f, 38f), new Vector2(-42f, 15f), new Vector2(31f, 7.5f),
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
                    // 館のポーチのジャンプ台。
                    ZoneSpec.OnFloor("PorchPadWest", StageZoneKind.JumpPad, -11f, -9f, 2f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("PorchPadEast", StageZoneKind.JumpPad, 9f, 11f, 2f, Vector2.up, 24f, 0.4f),
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
                    // バルコニーと、その間。
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
                Spawns = new[] { new Vector3(-4f, 3f, 0f), new Vector3(4f, 3f, 0f), new Vector3(-9f, 11.6f, 0f), new Vector3(9f, 11.6f, 0f) },
                Decorate = Decorate,
            };
        }

        // 牢屋の格子（周期で開く扉。中心 x、牢屋の床の上から天井の下まで）。
        private static PlatformSpec PrisonBars(string name, float x)
        {
            return PlatformSpec.TimedDoor(name, new Vector2(x, 30.1f), new Vector2(0.6f, 5f), 5.6f, 0.4f, 0.8f, 10f, 3f).Looks(BarsLooks);
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

        // 館の正面（窓の明かり、尖った屋根）、墓場の柵と墓石、農園のトマトの株、赤い満月、霧、遠くの森。
        private static void Decorate(StageDecor decor)
        {
            Material stone = decor.Lit("ManorStone", new Color(0.26f, 0.24f, 0.3f));
            Material roof = decor.Lit("ManorRoof", new Color(0.18f, 0.12f, 0.2f));
            Material windowLight = decor.Unlit("ManorWindow", new Color(1f, 0.8f, 0.35f));
            Material moon = decor.Unlit("BloodMoon", new Color(1f, 0.3f, 0.25f));
            Material grave = decor.Lit("Grave", new Color(0.45f, 0.45f, 0.48f));
            Material dirt = decor.Lit("Dirt", new Color(0.18f, 0.13f, 0.1f));
            Material leaf = decor.Lit("TomatoLeaf", new Color(0.15f, 0.32f, 0.12f));
            Material tomato = decor.Unlit("Tomato", new Color(0.9f, 0.12f, 0.1f));
            Material straw = decor.Lit("Straw", new Color(0.75f, 0.62f, 0.3f));
            Material fog = decor.Transparent("Fog", new Color(0.7f, 0.7f, 0.85f, 0.08f));
            Material tree = decor.Lit("DeadTree", new Color(0.12f, 0.09f, 0.08f));
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
            // 赤い満月と霧。
            decor.Sphere("Moon", new Vector3(18f, 48f, 90f), new Vector3(24f, 24f, 1f), moon);
            for (int i = 0; i < 8; i++)
            {
                decor.Quad("Fog", new Vector3(decor.Range(-50f, 50f), decor.Range(-2f, 6f), decor.Range(-4f, 8f)), new Vector2(decor.Range(14f, 30f), 4f), fog);
            }
            // 墓場の丘の奥行き、墓石の並び、柵。
            decor.Box("GraveHillBack", new Vector3(-36f, -6f, 6f), new Vector3(24f, 12f, 8f), dirt);
            for (int i = 0; i < 10; i++)
            {
                float x = decor.Range(-47f, -25f);
                decor.Box("BackGrave", new Vector3(x, 0.8f, decor.Range(2.5f, 9f)), new Vector3(0.9f, decor.Range(1f, 2f), 0.3f), grave);
            }
            for (float x = -47.5f; x <= -24.5f; x += 1.2f)
            {
                decor.Box("Fence", new Vector3(x, 1f, 2.6f), new Vector3(0.1f, 2f, 0.1f), tree);
            }
            // 枯れ木。
            decor.Box("TreeTrunk", new Vector3(-43f, 6f, 0.5f), new Vector3(1f, 12f, 1f), tree);
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
            for (int i = 0; i < 16; i++)
            {
                float h = decor.Range(14f, 30f);
                decor.Box("ForestTree", new Vector3(-90f + i * 12f + decor.Range(-3f, 3f), h * 0.5f - 6f, decor.Range(40f, 70f)), new Vector3(3f, h, 3f), tree);
            }
        }
    }
}
