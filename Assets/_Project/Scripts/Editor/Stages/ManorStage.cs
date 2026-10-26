using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 恐怖の館（VAMPIRE MANOR。ユーザーの案、2026-10-26）。館の外が舞台: 真ん中に吸血鬼の館（玄関のポーチ、バルコニー、屋根、鐘楼）、
    /// 左に寂れた墓場（墓石と納骨堂と枯れ木）、右におどろおどろしいカカシの立つトマト農園。館と墓場・農園の間は堀（水の判定）。
    /// 追加の案（取捨選択はユーザー）: 落雷（墓場と農園のランダムな場所に落ちる危険。予告あり）、コウモリの群れ（鐘楼から左右へ吹く
    /// 周期のある風）、棺桶（バンパー）、井戸（ワープゲート。鐘楼の上へ）、さまよう幽霊（館の上を行き来するバンパー）、カボチャの発射台
    /// （農園から館のバルコニーへ）、熟れたトマト（バンパー）、カカシ（腕にワイヤーがつながる）、空飛ぶ棺のふた（動く足場）、赤い満月の
    /// 光（いちばん上の低重力）、ガーゴイル（アンカー）、堀（水）。
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
            theme.ZoneColors[StageZoneKind.Gust] = new Color(0.55f, 0.45f, 0.7f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.85f, 0.2f, 0.2f);
            theme.ZoneColors[StageZoneKind.Warp] = new Color(0.4f, 1f, 0.6f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.55f, 0.1f);
            theme.ZoneColors[StageZoneKind.LowGravity] = new Color(1f, 0.25f, 0.25f);
            theme.ZoneColors[StageZoneKind.Water] = new Color(0.2f, 0.35f, 0.3f);

            return new StageLayout
            {
                Id = "Manor",
                DisplayName = "VAMPIRE MANOR",
                Description = "Outside a vampire's manor: a forsaken graveyard and a tomato farm with creepy scarecrows. Lightning, bats and ghosts.",
                Color = new Color(0.75f, 0.2f, 0.35f),
                Terrain = new[]
                {
                    // 左の墓場の丘（墓石と納骨堂）。
                    new TerrainBox("GraveHill", -48f, -30f, -24f, 0f),
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
                },
                // ガーゴイル、枯れ木の枝先、カカシの頭、鐘。
                Anchors = new[]
                {
                    new Vector2(-14f, 23f), new Vector2(14f, 23f), new Vector2(0f, 38f), new Vector2(-42f, 15f), new Vector2(31f, 7.5f),
                    new Vector2(41f, 7.5f), new Vector2(-24f, 28f), new Vector2(24f, 28f), new Vector2(-30f, 18f),
                },
                // 堀の底が撃破の線。
                BoundsMin = new Vector2(-60f, -12f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 堀（水）: 館と墓場・農園の間の黒い水。
                    new ZoneSpec("Moat", StageZoneKind.Water, new Vector2(0f, -8f), new Vector2(120f, 8f), Vector2.zero, 5f, 24f),
                    // 落雷: 墓場と農園のランダムな場所に、10〜16 秒ごとに落ちる（1.5 秒前に予告）。当たると威力 12 の枚数のコインを落とす。
                    new ZoneSpec("LightningGrave", StageZoneKind.Hazard, new Vector2(-36f, 14f), new Vector2(24f, 28f), Vector2.up, 12f, 16f)
                        .Every(10f, 0.4f, 4f, 6f, 1.5f).Column(1.6f),
                    new ZoneSpec("LightningFarm", StageZoneKind.Hazard, new Vector2(36f, 14f), new Vector2(24f, 28f), Vector2.up, 12f, 16f)
                        .Every(10f, 0.4f, 9f, 6f, 1.5f).Column(1.6f),
                    // コウモリの群れ: 鐘楼から左右へ、18 秒ごとに 3 秒（2 秒前に予告）。
                    new ZoneSpec("BatsWest", StageZoneKind.Gust, new Vector2(-21f, 28f), new Vector2(38f, 8f), Vector2.left, 40f, 14f).Every(18f, 3f, 6f, 0f, 2f),
                    new ZoneSpec("BatsEast", StageZoneKind.Gust, new Vector2(21f, 28f), new Vector2(38f, 8f), Vector2.right, 40f, 14f).Every(18f, 3f, 15f, 0f, 2f),
                    // 棺桶（バンパー）。
                    new ZoneSpec("CoffinWest", StageZoneKind.Bumper, new Vector2(-41f, 1.4f), new Vector2(1.4f, 2.6f), Vector2.up, 18f),
                    new ZoneSpec("CoffinCrypt", StageZoneKind.Bumper, new Vector2(-31f, 9f), new Vector2(2.6f, 1.4f), Vector2.up, 20f),
                    // 井戸（ワープゲート）: 墓場の井戸から鐘楼の上へ。
                    new ZoneSpec("Well", StageZoneKind.Warp, new Vector2(-46f, 1.2f), new Vector2(1.4f, 2f), Vector2.up, 1f).To(new Vector2(0f, 34f)),
                    // さまよう幽霊: 館の上を左右に行き来する（触れると弾かれる）。
                    new ZoneSpec("Ghost", StageZoneKind.Bumper, new Vector2(-18f, 15.5f), new Vector2(2f, 2.4f), Vector2.up, 16f)
                        .Moving(new Vector2(36f, 0f), 16f),
                    // カボチャの発射台: 農園から館の東のバルコニーへ。
                    ZoneSpec.OnFloor("PumpkinLauncher", StageZoneKind.Launcher, 26f, 28f, 0f, new Vector2(-0.5f, 1f), 32f),
                    // 熟れたトマト（バンパー）。
                    new ZoneSpec("TomatoA", StageZoneKind.Bumper, new Vector2(36f, 1.2f), new Vector2(1.6f, 1.6f), Vector2.up, 17f),
                    new ZoneSpec("TomatoB", StageZoneKind.Bumper, new Vector2(46f, 1.2f), new Vector2(1.6f, 1.6f), Vector2.up, 17f),
                    // 鬼火の輪（加速リング）: 墓場と農園の上から館の屋根へ。
                    new ZoneSpec("WispRingWest", StageZoneKind.BoostRing, new Vector2(-24f, 22f), new Vector2(2.4f, 3f), new Vector2(1f, 0.2f), 18f),
                    new ZoneSpec("WispRingEast", StageZoneKind.BoostRing, new Vector2(24f, 22f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.2f), 18f),
                    // 赤い満月の光（いちばん上の低重力）。
                    new ZoneSpec("BloodMoon", StageZoneKind.LowGravity, new Vector2(0f, 41f), new Vector2(34f, 8f), Vector2.zero, 0.45f, 5f),
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
                },
                Coins = new (Vector2, int)[]
                {
                    // 墓場（墓石の間、納骨堂の中と屋根の上、枯れ木の上）。
                    (new Vector2(-46f, 4f), 1), (new Vector2(-41.5f, 4.5f), 1), (new Vector2(-36f, 2f), 1), (new Vector2(-31f, 2f), 20),
                    (new Vector2(-31f, 3.8f), 3), (new Vector2(-34f, 7.5f), 1), (new Vector2(-28f, 7.5f), 1), (new Vector2(-43f, 14.5f), 3),
                    (new Vector2(-45.5f, 9f), 1), (new Vector2(-26f, 3f), 1),
                    // 墓場の上の空（落雷）。
                    (new Vector2(-38f, 18f), 1), (new Vector2(-30f, 22f), 1), (new Vector2(-36f, 26f), 3),
                    // 館のポーチと玄関。
                    (new Vector2(-6f, 4f), 1), (new Vector2(-2f, 4f), 1), (new Vector2(2f, 4f), 1), (new Vector2(6f, 4f), 1), (new Vector2(0f, 7f), 3),
                    // バルコニーと、その間（幽霊の通り道）。
                    (new Vector2(-10.5f, 12.5f), 1), (new Vector2(10.5f, 12.5f), 1), (new Vector2(-3f, 13f), 1), (new Vector2(3f, 13f), 1),
                    (new Vector2(0f, 16f), 20),
                    // 屋根の上と鐘楼。
                    (new Vector2(-11f, 22.5f), 1), (new Vector2(-6f, 22.5f), 1), (new Vector2(6f, 22.5f), 1), (new Vector2(12.5f, 26f), 1),
                    (new Vector2(-4f, 28f), 1), (new Vector2(4f, 28f), 1), (new Vector2(0f, 35f), 3),
                    // 空飛ぶ棺のふたの道と、コウモリの群れの中。
                    (new Vector2(-16f, 28f), 1), (new Vector2(16f, 28f), 1), (new Vector2(-26f, 30f), 3), (new Vector2(26f, 30f), 3),
                    // 赤い満月の光の中。
                    (new Vector2(-10f, 40f), 1), (new Vector2(10f, 40f), 1), (new Vector2(0f, 43f), 20),
                    // 農園（カカシの腕の上、トマトの間、納屋の屋根）。
                    (new Vector2(29.5f, 7f), 1), (new Vector2(32.5f, 7f), 1), (new Vector2(39.5f, 7f), 1), (new Vector2(42.5f, 7f), 1),
                    (new Vector2(36f, 4f), 1), (new Vector2(33.5f, 2f), 1), (new Vector2(44f, 14.5f), 3), (new Vector2(46f, 17f), 1),
                    (new Vector2(26f, 4f), 1),
                    // 農園の上の空（落雷）と、カボチャの発射台の道。
                    (new Vector2(38f, 20f), 1), (new Vector2(30f, 16f), 1), (new Vector2(36f, 26f), 3), (new Vector2(20f, 12f), 1),
                    // 堀の中（潜る）と堀の上。
                    (new Vector2(-19f, -9f), 20), (new Vector2(19f, -9f), 20), (new Vector2(-19f, -2f), 3), (new Vector2(19f, -2f), 3),
                    // 高い所とステージの端。
                    (new Vector2(-24f, 36f), 20), (new Vector2(24f, 36f), 20), (new Vector2(-53f, 10f), 20), (new Vector2(53f, 10f), 20),
                    (new Vector2(-52f, 24f), 3), (new Vector2(52f, 24f), 3), (new Vector2(-20f, 19f), 20), (new Vector2(20f, 19f), 20),
                    (new Vector2(-12f, 31f), 20), (new Vector2(12f, 31f), 20), (new Vector2(-38f, 32f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 44f), new Vector2(-46f, 30f), new Vector2(46f, 30f), new Vector2(-19f, -10f), new Vector2(19f, -10f),
                    new Vector2(-31f, 3f), new Vector2(0f, 9f),
                },
                Banks = new[]
                {
                    new Vector2(-8f, 24.5f), new Vector2(-9f, 14f), new Vector2(9f, 14f), new Vector2(-37.5f, 6.5f), new Vector2(37f, 9f),
                    new Vector2(0f, 39f), new Vector2(-30f, 13f), new Vector2(45f, 3f),
                },
                Spawns = new[] { new Vector3(-4f, 3f, 0f), new Vector3(4f, 3f, 0f), new Vector3(-36f, 1f, 0f), new Vector3(34f, 1f, 0f) },
                Decorate = Decorate,
            };
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
            Material moatDeep = decor.Lit("MoatDeep", new Color(0.05f, 0.1f, 0.08f));

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
            // 堀の奥と、遠くの森。
            decor.Box("MoatBack", new Vector3(0f, -20f, 8f), new Vector3(140f, 24f, 12f), moatDeep);
            for (int i = 0; i < 16; i++)
            {
                float h = decor.Range(14f, 30f);
                decor.Box("ForestTree", new Vector3(-90f + i * 12f + decor.Range(-3f, 3f), h * 0.5f - 6f, decor.Range(40f, 70f)), new Vector3(3f, h, 3f), tree);
            }
        }
    }
}
