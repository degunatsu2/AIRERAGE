using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 雪原と海（FROST COAST）。左は雪原と尾根、右は海に浮かぶ氷と氷山、右の端に船。海に落ちると 5 秒浮いていられ、その後は溺れて撃破
    /// （海の判定。ユーザーの要望、2026-10-26。強く叩き落とされると撃破の線まで沈む）。
    /// 固有のギミック: 氷（滑る。止まりにくく曲がりにくい）、スキージャンプ（尾根のスピードレーンから発射台で海の上へ。着いた氷の上を
    /// 滑ったまま跳ぶ）、海の波（周期のある上向きの風。落ちた人を氷の上へ押し上げる）。ユーザーの要望（2026-10-19）で、船の中を
    /// 通り抜けられるようにし、雪原の下を右からくりぬいた洞窟（中につららが落ちる。落ちる前に警告。奥に拠点の候補）を作り、上空の風を
    /// なくして、30〜40 秒に 1 回の吹雪（Blizzard。5 秒前に警告と警告音。何かの影に隠れていないと 2 秒凍る）にした。
    /// </summary>
    internal static class SnowSeaStage
    {
        // 氷の滑りやすさ（このティックの横の速さの変化の倍率）【仮】。止まっていても動いていても、もっと滑るように 0.15 → 0.05
        // （ユーザーの要望、2026-10-21。地上の加速 80 m/s² の 5% = 4 m/s²。走る速さ 8 m/s から止まるまで 2 秒）。
        private const float IceGrip = 0.05f;

        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.9f, 0.93f, 0.97f),
                Attachable = new Color(0.55f, 0.8f, 0.92f),
                Anchor = new Color(1f, 0.75f, 0.25f),
                SkyTint = new Color(0.62f, 0.75f, 0.95f),
                Ground = new Color(0.55f, 0.6f, 0.68f),
                Exposure = 1.45f,
                AtmosphereThickness = 0.8f,
                Light = new Color(1f, 0.98f, 0.95f),
                LightIntensity = 1.25f,
                LightEuler = new Vector3(38f, 25f, 0f),
                AmbientSky = new Color(0.7f, 0.8f, 0.95f),
                AmbientEquator = new Color(0.62f, 0.68f, 0.78f),
                AmbientGround = new Color(0.45f, 0.5f, 0.58f),
                FogColor = new Color(0.82f, 0.88f, 0.96f),
                FogStart = 40f,
                FogEnd = 140f,
            };
            theme.ZoneColors[StageZoneKind.Ice] = new Color(0.72f, 0.92f, 1f);
            theme.ZoneColors[StageZoneKind.Gust] = new Color(1f, 1f, 1f);
            theme.ZoneColors[StageZoneKind.SpeedLane] = new Color(0.35f, 0.75f, 1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.4f, 0.3f);
            theme.ZoneColors[StageZoneKind.Hazard] = new Color(0.75f, 0.93f, 1f);
            theme.ZoneColors[StageZoneKind.Blizzard] = new Color(0.92f, 0.97f, 1f);
            var blue = new Vector2(1f, 0f);

            return new StageLayout
            {
                Id = "SnowSea",
                DisplayName = "FROST COAST",
                Description = "Slippery ice floes on a freezing sea. Ski-jump off the ridge, ride the waves, hide from the blizzard.",
                DescriptionJa = "凍てつく海に浮かぶ、滑りやすい氷。尾根からスキージャンプ、波に乗り、吹雪から身を隠せ。",
                Color = new Color(0.6f, 0.9f, 1f),
                Terrain = new[]
                {
                    // 雪原と尾根（雪は接続できない）。雪原の下は右からくりぬいた洞窟（y = -3〜2。左の端は貫かない）。
                    new TerrainBox("Snowfield", -48f, 2f, -24f, 6f),
                    new TerrainBox("SnowfieldEnd", -48f, -3f, -46.5f, 2f),
                    new TerrainBox("SnowfieldBase", -48f, -30f, -24f, -3f),
                    new TerrainBox("Ridge", -48f, 6f, -38f, 12f),
                    // 海に浮かぶ氷（接続できる）。
                    new TerrainBox("IceFloeWest", -18f, -30f, -11f, 0.5f, attachable: true),
                    new TerrainBox("IceFloeCenter", -5f, -30f, 5f, 1f, attachable: true),
                    new TerrainBox("Iceberg", 12f, -30f, 20f, 12f, attachable: true),
                    new TerrainBox("IceFloeEast", 26f, -30f, 32f, 2f, attachable: true),
                    // 船（甲板は接続できない。マストは接続できる）。船の中（y = -1.5〜2）は両端が開いていて、通り抜けられる。
                    new TerrainBox("ShipHull", 37f, 2f, 47f, 3f),
                    new TerrainBox("ShipKeel", 37f, -30f, 47f, -1.5f),
                    // 船首（船の中の床の左の端。見た目だけで判定がなかった。ユーザーの指摘、2026-10-21）。
                    new TerrainBox("ShipBow", 35f, -30f, 37f, -1.5f),
                    new TerrainBox("Mast", 41.7f, 3f, 42.3f, 16f, attachable: true),
                    new TerrainBox("CrowsNest", 40.5f, 16f, 43.5f, 16.4f),
                    // 宙に張り出した氷の棚とアーチ。
                    new TerrainBox("IceShelfWest", -16f, 14f, -10f, 14.6f, attachable: true),
                    new TerrainBox("IceArch", -4f, 24f, 4f, 24.6f, attachable: true),
                    new TerrainBox("IceShelfEast", 24f, 16f, 30f, 16.6f, attachable: true),
                },
                Anchors = new[]
                {
                    new Vector2(0f, 29f), new Vector2(-20f, 22f), new Vector2(20f, 25f), new Vector2(-30f, 16f), new Vector2(34f, 22f),
                    new Vector2(-8f, 9f), new Vector2(8f, 9f), new Vector2(-21f, 13f),
                },
                // 海に落ちたら撃破（海面は y = -6）。左右は広めに（端の赤いコインを取っても撃破されないように。ユーザーの要望、2026-10-19）。
                BoundsMin = new Vector2(-60f, -10f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 氷。
                    ZoneSpec.OnFloor("FrozenPond", StageZoneKind.Ice, -31f, -25f, 6f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("FloeWestIce", StageZoneKind.Ice, -18f, -11f, 0.5f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("FloeCenterIce", StageZoneKind.Ice, -5f, 5f, 1f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("IcebergIce", StageZoneKind.Ice, 12f, 20f, 12f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("FloeEastIce", StageZoneKind.Ice, 26f, 32f, 2f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("ShelfWestIce", StageZoneKind.Ice, -16f, -10f, 14.6f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("ArchIce", StageZoneKind.Ice, -4f, 4f, 24.6f, Vector2.zero, IceGrip),
                    ZoneSpec.OnFloor("ShelfEastIce", StageZoneKind.Ice, 24f, 30f, 16.6f, Vector2.zero, IceGrip),
                    // スキージャンプ: 尾根のゲレンデ（スピードレーン）から、縁の発射台で海の上の氷へ飛ぶ。
                    ZoneSpec.OnFloor("SkiSlope", StageZoneKind.SpeedLane, -47f, -40f, 12f, blue, 18f),
                    ZoneSpec.OnFloor("SkiJump", StageZoneKind.Launcher, -40f, -38.2f, 12f, new Vector2(1f, 0.45f), 22f),
                    // ジャンプ台。
                    ZoneSpec.OnFloor("SnowPad", StageZoneKind.JumpPad, -36f, -34f, 6f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("FloePad", StageZoneKind.JumpPad, -1f, 1f, 1f, Vector2.up, 26f, 0.4f),
                    ZoneSpec.OnFloor("IcebergPad", StageZoneKind.JumpPad, 15f, 17f, 12f, Vector2.up, 22f, 0.4f),
                    ZoneSpec.OnFloor("ShipPad", StageZoneKind.JumpPad, 38f, 40f, 3f, Vector2.up, 24f, 0.4f),
                    // 加速リング。
                    new ZoneSpec("FloeRing", StageZoneKind.BoostRing, new Vector2(0f, 15f), new Vector2(3f, 3.5f), Vector2.up, 18f),
                    new ZoneSpec("SeaRingWest", StageZoneKind.BoostRing, new Vector2(-21f, 8f), new Vector2(2.4f, 3f), new Vector2(1f, 0.1f), 18f),
                    new ZoneSpec("SeaRingEast", StageZoneKind.BoostRing, new Vector2(34.5f, 10f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.1f), 18f),
                    // 波: 氷の間の海から、順に 1.5 秒ずつ噴き上げる（5 秒ごと）。
                    Wave("WaveWest", -24f, -18f, 0f),
                    Wave("WaveMid", -11f, -5f, 1.25f),
                    Wave("WaveEast", 5f, 12f, 2.5f),
                    Wave("WaveFar", 20f, 26f, 3.75f),
                    Wave("WaveShip", 32f, 35f, 0.6f), // 船首（x = 35〜37）の上には吹かない（2026-10-21）。
                    // 洞窟のつらら: 天井のランダムな場所から、幅 1.6m のつららが 3.5 秒ごとに落ちる（1.2 秒前から当たる柱の全体に警告。
                    // 警告音は鳴らさない。ユーザーの要望、2026-10-20）。
                    // 威力 15（その枚数のコインを落とす）。吹き飛ばす速さは今までどおり 15.5 m/s（2026-10-21）。
                    new ZoneSpec("Icicles", StageZoneKind.Hazard, new Vector2(-35.5f, -0.5f), new Vector2(20f, 5f), Vector2.down, 15f, 15.5f)
                        .Every(3.5f, 0.5f, 1f, 0f, 1.2f).Column(1.6f).Silent(),
                    // 吹雪: 30〜40 秒ごとに 1.5 秒、左上から吹きつける。風上 14m の中に地形がない（何かの影に隠れていない）と 2 秒凍る。
                    // 5 秒前から、当たる場所（影の外）にだけ警告（黄色の斜線と記号。ユーザーの要望、2026-10-20）と警告音。
                    // 凍ると威力 20 の枚数のコインを落とす（凍るのは 2 秒。StageZones.BlizzardFreezeTicks。2026-10-21）。
                    // 海（ユーザーの要望、2026-10-26）: 着水すると 5 秒浮いていられ、その後は溺れて沈む。24 m/s 以上で着水すると撃破の線まで沈む
                    // （ふつうに落ちる速さの上限は 20 m/s。強く叩き落とされたときだけ）。底は撃破の線（y = -10）。
                    new ZoneSpec("Sea", StageZoneKind.Water, new Vector2(0f, -8f), new Vector2(120f, 4f), Vector2.zero, 5f, 24f),
                    new ZoneSpec("Blizzard", StageZoneKind.Blizzard, new Vector2(0f, 18f), new Vector2(120f, 56f), new Vector2(1f, -0.5f), 20f, 14f)
                        .Every(30f, 1.5f, 0f, 10f, 5f),
                },
                Coins = new (Vector2, int)[]
                {
                    // スキージャンプの道。
                    (new Vector2(-34f, 14.2f), 1), (new Vector2(-28f, 13.4f), 1), (new Vector2(-22f, 10.3f), 1),
                    // 氷の上と、真ん中のジャンプ台の上。
                    (new Vector2(-16f, 2.6f), 1), (new Vector2(-13f, 2.6f), 1), (new Vector2(-3f, 3f), 1), (new Vector2(3f, 3f), 1),
                    (new Vector2(0f, 5f), 1), (new Vector2(0f, 11.5f), 1), (new Vector2(0f, 19.5f), 1),
                    // 氷山、東の氷、船。
                    (new Vector2(13.5f, 14f), 1), (new Vector2(16f, 17f), 1), (new Vector2(29f, 4f), 1),
                    (new Vector2(39f, 7f), 1), (new Vector2(44f, 5f), 1), (new Vector2(42f, 18.5f), 1),
                    // 波の上。
                    (new Vector2(-21f, 2f), 1), (new Vector2(-8f, 3f), 1), (new Vector2(8.5f, 3f), 1), (new Vector2(23f, 3f), 1), (new Vector2(34.5f, 4f), 1),
                    // 氷の棚の上。
                    (new Vector2(-13f, 16.6f), 1), (new Vector2(27f, 18.6f), 1),
                    // 3 枚: アーチの上、ゲレンデの上、見張り台の上、吹雪の中。
                    (new Vector2(0f, 26.8f), 3), (new Vector2(-43f, 15f), 3), (new Vector2(42f, 21f), 3), (new Vector2(16f, 23f), 3), (new Vector2(-46f, 18f), 3),
                    // 洞窟の中と、船の中。
                    (new Vector2(-40f, -1.5f), 1), (new Vector2(-30f, -1.5f), 1), (new Vector2(42f, 0.2f), 1),
                    // 20 枚: 海の中（波で戻れる）、いちばん上、ステージの端。
                    (new Vector2(-21f, -8f), 20), (new Vector2(-8f, -8f), 20), (new Vector2(8.5f, -8f), 20), (new Vector2(23f, -8f), 20),
                    (new Vector2(0f, 36f), 20), (new Vector2(-47f, 24f), 20), (new Vector2(47f, 27f), 20),
                    // 追加（総量を 2 倍に。ユーザーの要望、2026-10-21）。雪原の下の洞窟（隠れ家にしやすい所。つららの下に 20 枚 2 つと 3 枚 2 つを固めて、取り合いを激しくする）。
                    (new Vector2(-43f, -1f), 20), (new Vector2(-28.5f, -1f), 20), (new Vector2(-39f, 0.5f), 3), (new Vector2(-33f, 0.5f), 3),
                    (new Vector2(-45f, -2f), 1), (new Vector2(-26.5f, 0f), 1),
                    // 船の中（もう 1 つの隠れ家）。
                    (new Vector2(39.5f, 0.2f), 3), (new Vector2(44.5f, 0.2f), 20),
                    // 雪原のジャンプ台の上、氷の上、氷山の脇、東の氷の上、船の上。
                    (new Vector2(-35f, 10f), 1), (new Vector2(-35f, 17.5f), 1), (new Vector2(-11f, 5f), 1), (new Vector2(-5f, 7f), 1),
                    (new Vector2(5f, 7f), 1), (new Vector2(10f, 6f), 1), (new Vector2(22f, 8f), 1), (new Vector2(27f, 5f), 1),
                    (new Vector2(35f, 14f), 3), (new Vector2(38f, 17f), 1), (new Vector2(45f, 13f), 1),
                    // 氷の棚とアーチの周り、高い所。
                    (new Vector2(-20f, 18f), 3), (new Vector2(20f, 20f), 3), (new Vector2(-8f, 28f), 3), (new Vector2(8f, 28f), 3),
                    (new Vector2(-12f, 22f), 1), (new Vector2(12f, 24f), 1), (new Vector2(-28f, 22f), 1), (new Vector2(25f, 21f), 1),
                    (new Vector2(30f, 26f), 1), (new Vector2(0f, 32f), 1),
                    // 20 枚: 上空、ステージの端。
                    (new Vector2(-24f, 30f), 20), (new Vector2(24f, 30f), 20), (new Vector2(-52f, 10f), 20), (new Vector2(52f, 8f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 40f), new Vector2(-46f, 32f), new Vector2(46f, 34f), new Vector2(-8f, -9f), new Vector2(8.5f, -9f),
                    new Vector2(23f, -9f), new Vector2(34.5f, -8f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 8f), new Vector2(-30f, 10f), new Vector2(-14.5f, 6f), new Vector2(19.5f, 17.5f), new Vector2(29f, 8f),
                    new Vector2(44f, 10f), new Vector2(-41f, 17f),
                    // 雪原の下の洞窟の奥（ユーザーの要望、2026-10-19）。
                    new Vector2(-36f, -1f),
                },
                Spawns = new[] { new Vector3(-3f, 2f, 0f), new Vector3(3f, 2f, 0f), new Vector3(-33f, 7f, 0f), new Vector3(29f, 3f, 0f) },
                Decorate = Decorate,
            };
        }

        // 波（海の中から海面の上まで。周期 5 秒、1.5 秒噴く）。
        private static ZoneSpec Wave(string name, float minX, float maxX, float phase)
        {
            return new ZoneSpec(name, StageZoneKind.Gust, new Vector2((minX + maxX) * 0.5f, -3f), new Vector2(maxX - minX, 14f), Vector2.up, 80f, 17f)
                .Every(5f, 1.5f, phase);
        }

        // 海、雪の山、遠くの氷山、針葉樹、雪の粒。
        private static void Decorate(StageDecor decor)
        {
            Material sea = decor.Lit("Sea", new Color(0.12f, 0.32f, 0.52f));
            Material foam = decor.Unlit("Foam", new Color(0.92f, 0.97f, 1f));
            Material snow = decor.Lit("Snow", new Color(0.93f, 0.95f, 0.98f));
            Material ice = decor.Lit("Ice", new Color(0.65f, 0.85f, 0.95f));
            Material pine = decor.Lit("Pine", new Color(0.14f, 0.32f, 0.24f));
            Material wood = decor.Lit("Wood", new Color(0.45f, 0.3f, 0.2f));
            Material sail = decor.Lit("Sail", new Color(0.92f, 0.9f, 0.85f));
            Material flake = decor.Unlit("Snowflake", Color.white);

            // 奥の海（遊ぶ面より奥だけ。手前は海の判定の半透明の水で、沈んでいく人と水の中のコインが見える。2026-10-26）。
            decor.Box("Sea", new Vector3(0f, -23f, 31f), new Vector3(150f, 34f, 58f), sea);
            for (int i = 0; i < 18; i++)
            {
                decor.Quad("Foam", new Vector3(decor.Range(-24f, 48f), -5.95f, decor.Range(-12f, 30f)), new Vector2(decor.Range(1f, 4f), 0.12f), foam);
            }
            // 雪原と尾根の奥行き、針葉樹（45° 回した箱）。
            decor.Box("SnowfieldBack", new Vector3(-36f, -2f, 8f), new Vector3(24f, 16f, 12f), snow);
            // 洞窟の奥の氷の壁と、天井の小さなつらら（飾り）。
            decor.Box("CaveWall", new Vector3(-35.25f, -0.5f, 1.8f), new Vector3(22.5f, 5f, 0.4f), decor.Lit("CaveIce", new Color(0.45f, 0.65f, 0.82f)));
            for (float x = -45.5f; x < -24.5f; x += 1.4f)
            {
                float length = decor.Range(0.3f, 0.7f);
                GameObject icicle = decor.Box("CeilingIcicle", new Vector3(x, 2f - length * 0.35f, decor.Range(-1.5f, 1.5f)), new Vector3(0.25f, length, 0.25f), ice);
                icicle.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            }
            for (int i = 0; i < 9; i++)
            {
                float size = decor.Range(1.6f, 3f);
                GameObject tree = decor.Box("Pine", new Vector3(decor.Range(-47f, -26f), 6f + size * 0.5f, decor.Range(3f, 12f)),
                    new Vector3(size, size, size), pine);
                tree.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }
            // 船の飾り（木の船体と見張り台、帆、船首）。地形の箱を少し大きな木の箱で覆う（上の面も地形より少し上にする。同じ高さだと
            // カメラが動くたびに地形の白とちらつく。ユーザーの指摘、2026-10-21）。
            decor.Box("ShipDeckSkin", new Vector3(42f, 2.5f, 0f), new Vector3(10.02f, 1.02f, 4.2f), wood);
            decor.Box("ShipKeelSkin", new Vector3(42f, -6.24f, 0f), new Vector3(10.02f, 9.52f, 4.2f), wood);
            // 船の中（奥の板と、灯り）。
            decor.Box("ShipHold", new Vector3(42f, 0.25f, 1.8f), new Vector3(10f, 3.5f, 0.4f), decor.Lit("Hold", new Color(0.25f, 0.17f, 0.12f)));
            decor.Sphere("HoldLamp", new Vector3(42f, 1.6f, 0f), new Vector3(0.4f, 0.4f, 0.4f), decor.Unlit("Lamp", new Color(1f, 0.85f, 0.5f)));
            decor.Box("CrowsNestSkin", new Vector3(42f, 16.2f, 0f), new Vector3(3.02f, 0.42f, 4.2f), wood);
            decor.Box("ShipBow", new Vector3(36f, -3.74f, 0f), new Vector3(2.02f, 4.52f, 4.2f), wood);
            decor.Quad("Sail", new Vector3(42f, 10f, 0.4f), new Vector2(6f, 8f), sail);
            // 遠くの雪山と氷山。
            for (int i = 0; i < 6; i++)
            {
                float size = decor.Range(35f, 60f);
                GameObject mountain = decor.Box("Mountain", new Vector3(-130f + i * 50f + decor.Range(-8f, 8f), -20f, 120f + decor.Range(0f, 30f)),
                    new Vector3(size, size, 20f), snow);
                mountain.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            }
            for (int i = 0; i < 8; i++)
            {
                float width = decor.Range(4f, 12f);
                float height = decor.Range(3f, 14f);
                decor.Box("FarIceberg", new Vector3(decor.Range(-80f, 90f), -6f + height * 0.5f, decor.Range(25f, 70f)), new Vector3(width, height, width), ice);
            }
            // 雪の粒。
            for (int i = 0; i < 60; i++)
            {
                float s = decor.Range(0.1f, 0.22f);
                decor.Sphere("Snowflake", new Vector3(decor.Range(-48f, 48f), decor.Range(-4f, 40f), decor.Range(-6f, 20f)), new Vector3(s, s, s), flake);
            }
        }
    }
}
