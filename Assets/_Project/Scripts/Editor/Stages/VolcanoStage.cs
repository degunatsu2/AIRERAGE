using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 火山の火口（CRATER）。左右の火口の縁（段になった岩）と、溶岩の湖の真ん中の岩の柱、浮かぶ岩。溶岩に落ちると撃破。
    /// 固有のギミック（ユーザーの要望、2026-10-19 に変え、2026-10-20 に間隔と太さを変えた）: 溶岩流（Hazard。溶岩の湖のランダムな場所から、
    /// 黒曜石の段の高さまで噴き出す。威力 30（その枚数のコインを落とす）で、高く吹き飛ばす。噴く前に当たる柱の全体に警告と警告音）、壁の間欠泉（周期のある Launcher。縁の上の段と
    /// 下の段の内側の壁から、火口の内側へ斜めに一斉に噴き出し、強い風よりも強く吹き飛ばす）、噴火口（Launcher。縁から火口の真ん中へ
    /// 飛ばす）、噴石（Bumper）。
    /// </summary>
    internal static class VolcanoStage
    {
        // 溶岩の噴泉の頂きの丸み（m。柱の幅 6m の半分の半円。見た目の冠と判定を合わせる。2026-11-20 の方針）。
        private const float LavaCrown = 3f;

        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.36f, 0.3f, 0.28f),
                Attachable = new Color(0.17f, 0.16f, 0.22f),
                Anchor = new Color(1f, 0.82f, 0.3f),
                SkyTint = new Color(1f, 0.42f, 0.25f),
                Ground = new Color(0.25f, 0.06f, 0.03f),
                Exposure = 0.85f,
                AtmosphereThickness = 1.3f,
                SunSize = 0.06f,
                Light = new Color(1f, 0.62f, 0.4f),
                LightIntensity = 1.1f,
                LightEuler = new Vector3(30f, -50f, 0f),
                AmbientSky = new Color(0.55f, 0.3f, 0.25f),
                AmbientEquator = new Color(0.45f, 0.22f, 0.15f),
                AmbientGround = new Color(0.6f, 0.2f, 0.05f),
                FogColor = new Color(0.45f, 0.18f, 0.12f),
                FogStart = 45f,
                FogEnd = 150f,
            };
            theme.ZoneColors[StageZoneKind.Hazard] = new Color(1f, 0.42f, 0.06f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(1f, 0.3f, 0.1f);
            theme.ZoneColors[StageZoneKind.Bumper] = new Color(0.9f, 0.25f, 0.08f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(1f, 0.7f, 0.45f);

            return new StageLayout
            {
                Id = "Volcano",
                Music = "Volcano",
                DisplayName = "CRATER",
                Description = "Inside an active crater. Lava bursts from random spots, steam geysers blast from the walls, molten rocks bounce you away.",
                DescriptionJa = "活火山の火口の中。溶岩が噴き出し、壁の間欠泉から蒸気が吹き上がり、溶けた岩に弾き飛ばされる。",
                Color = new Color(1f, 0.45f, 0.15f),
                Terrain = new[]
                {
                    // 火口の縁（外へ行くほど高い段）。
                    new TerrainBox("RimWestLow", -48f, -30f, -34f, 2f, attachable: true),
                    new TerrainBox("RimWestMid", -48f, 2f, -41f, 8f, attachable: true),
                    new TerrainBox("RimWestTop", -48f, 8f, -45f, 16f, attachable: true),
                    new TerrainBox("RimEastLow", 34f, -30f, 48f, 2f, attachable: true),
                    new TerrainBox("RimEastMid", 41f, 2f, 48f, 8f, attachable: true),
                    new TerrainBox("RimEastTop", 45f, 8f, 48f, 16f, attachable: true),
                    // 溶岩の湖の真ん中の岩の柱（いちばん広い足場）。
                    new TerrainBox("CenterSpire", -6f, -30f, 6f, 0f, attachable: true),
                    // 浮かぶ岩（接続できない灰の岩と、接続できる黒曜石）。
                    new TerrainBox("FloatRockWest", -24f, 4f, -19f, 5f),
                    new TerrainBox("FloatRockEast", 19f, 4f, 24f, 5f),
                    new TerrainBox("ObsidianWest", -10.5f, 13f, -6.5f, 13.8f, attachable: true),
                    new TerrainBox("ObsidianEast", 6.5f, 13f, 10.5f, 13.8f, attachable: true),
                    new TerrainBox("SummitWest", -9f, 22f, -5f, 22.6f, attachable: true),
                    new TerrainBox("SummitEast", 5f, 22f, 9f, 22.6f, attachable: true),
                },
                Anchors = new[]
                {
                    new Vector2(0f, 30f), new Vector2(-15f, 26f), new Vector2(15f, 26f), new Vector2(-30f, 20f), new Vector2(30f, 20f),
                    new Vector2(-36f, 12f), new Vector2(36f, 12f), new Vector2(-13f, 7f), new Vector2(13f, 7f),
                },
                // 左右は縁の外へ広めに（ユーザーの要望、2026-10-19）。
                BoundsMin = new Vector2(-60f, -22f),
                BoundsMax = new Vector2(60f, 46f),
                Theme = theme,
                Zones = new[]
                {
                    // 柱のジャンプ台と、真上の加速リング。
                    ZoneSpec.OnFloor("SpirePad", StageZoneKind.JumpPad, -1f, 1f, 0f, Vector2.up, 28f, 0.4f),
                    new ZoneSpec("SpireRing", StageZoneKind.BoostRing, new Vector2(0f, 16f), new Vector2(3f, 3.5f), Vector2.up, 20f),
                    // 浮かぶ岩と縁の中段のジャンプ台。
                    ZoneSpec.OnFloor("FloatPadWest", StageZoneKind.JumpPad, -22.5f, -20.5f, 5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("FloatPadEast", StageZoneKind.JumpPad, 20.5f, 22.5f, 5f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("RimPadWest", StageZoneKind.JumpPad, -44f, -42f, 8f, Vector2.up, 24f, 0.4f),
                    ZoneSpec.OnFloor("RimPadEast", StageZoneKind.JumpPad, 42f, 44f, 8f, Vector2.up, 24f, 0.4f),
                    // 縁から火口の真ん中へ向かう加速リング。
                    new ZoneSpec("RimRingWest", StageZoneKind.BoostRing, new Vector2(-28f, 13f), new Vector2(2.4f, 3f), new Vector2(1f, 0.15f), 18f),
                    new ZoneSpec("RimRingEast", StageZoneKind.BoostRing, new Vector2(28f, 13f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.15f), 18f),
                    // 溶岩流: 溶岩の湖（柱の左右）のランダムな場所から、幅 6m の柱が黒曜石の段（y = 13）まで 3 秒噴き出す（25 秒ごと。
                    // 左右が半分ずれる。ユーザーの要望、2026-10-20: 頻度を 1/5、太さを 1.5 倍、長さを 3 倍、もう一段上まで）。噴く 2 秒前から
                    // 当たる柱の全体に警告。当たると威力 30 の枚数のコインを落とし、上へ 40 m/s で吹き飛ぶ（2026-10-21: 威力を 60 から 30 に、
                    // 吹き飛ぶ高さを約 3 倍に。速さ 23 → 40 m/s）。
                    new ZoneSpec("LavaFlowWest", StageZoneKind.Hazard, new Vector2(-20f, -3.5f), new Vector2(28f, 33f), Vector2.up, 30f, 40f)
                        .Every(25f, 3f, 0f, 0f, 2f).Column(6f).Styled(StageZoneView.ZoneLook.Lava).Capped(LavaCrown),
                    new ZoneSpec("LavaFlowEast", StageZoneKind.Hazard, new Vector2(20f, -3.5f), new Vector2(28f, 33f), Vector2.up, 30f, 40f)
                        .Every(25f, 3f, 12.5f, 0f, 2f).Column(6f).Styled(StageZoneView.ZoneLook.Lava).Capped(LavaCrown),
                    // 壁の間欠泉: 縁の上の段（x = ±45）の上のほうと、下の段（x = ±34）の下のほうの内側の壁から、火口の内側へ斜めに噴く
                    // （4 つが一斉に、20 秒ごとに 3.6 秒。ユーザーの要望、2026-10-20。2 秒前から口に警告）。触れると速度をまるごと噴く向きにする
                    // （強い風より強い）。
                    new ZoneSpec("GeyserHighWest", StageZoneKind.Launcher, new Vector2(-42.5f, 14f), new Vector2(5f, 5f), new Vector2(1f, 0.35f), 34f)
                        .Every(20f, 3.6f, 0f, 0f, 2f).Styled(StageZoneView.ZoneLook.Steam),
                    new ZoneSpec("GeyserHighEast", StageZoneKind.Launcher, new Vector2(42.5f, 14f), new Vector2(5f, 5f), new Vector2(-1f, 0.35f), 34f)
                        .Every(20f, 3.6f, 0f, 0f, 2f).Styled(StageZoneView.ZoneLook.Steam),
                    new ZoneSpec("GeyserLowWest", StageZoneKind.Launcher, new Vector2(-31.5f, -6.5f), new Vector2(5f, 5f), new Vector2(1f, 0.8f), 32f)
                        .Every(20f, 3.6f, 0f, 0f, 2f).Styled(StageZoneView.ZoneLook.Steam),
                    new ZoneSpec("GeyserLowEast", StageZoneKind.Launcher, new Vector2(31.5f, -6.5f), new Vector2(5f, 5f), new Vector2(-1f, 0.8f), 32f)
                        .Every(20f, 3.6f, 0f, 0f, 2f).Styled(StageZoneView.ZoneLook.Steam),
                    // 噴火口: 縁の低い段から、火口の真ん中の柱へ飛ばす。
                    ZoneSpec.OnFloor("VentWest", StageZoneKind.Launcher, -37f, -35f, 2f, new Vector2(0.7f, 1f), 32f),
                    ZoneSpec.OnFloor("VentEast", StageZoneKind.Launcher, 35f, 37f, 2f, new Vector2(-0.7f, 1f), 32f),
                    // 噴石（浮かぶ溶けた岩）: 当たると外へ弾かれる。
                    new ZoneSpec("BombWest", StageZoneKind.Bumper, new Vector2(-20f, 18f), new Vector2(1.8f, 1.8f), Vector2.up, 18f),
                    new ZoneSpec("BombEast", StageZoneKind.Bumper, new Vector2(20f, 18f), new Vector2(1.8f, 1.8f), Vector2.up, 18f),
                    new ZoneSpec("BombHighWest", StageZoneKind.Bumper, new Vector2(-33f, 24f), new Vector2(2f, 2f), Vector2.up, 18f),
                    new ZoneSpec("BombHighEast", StageZoneKind.Bumper, new Vector2(33f, 24f), new Vector2(2f, 2f), Vector2.up, 18f),
                    new ZoneSpec("BombTop", StageZoneKind.Bumper, new Vector2(0f, 33f), new Vector2(2.2f, 2.2f), Vector2.up, 18f),
                },
                Coins = new (Vector2, int)[]
                {
                    // 柱のジャンプ台の上と、加速リングの上。
                    (new Vector2(0f, 3.5f), 1), (new Vector2(0f, 10.5f), 1), (new Vector2(0f, 13.5f), 1), (new Vector2(0f, 20f), 1), (new Vector2(0f, 23f), 1),
                    // 噴火口で飛ぶ道。
                    (new Vector2(-30f, 9.8f), 1), (new Vector2(-20f, 13.6f), 1), (new Vector2(30f, 9.8f), 1), (new Vector2(20f, 13.6f), 1),
                    // 浮かぶ岩のジャンプ台の上、浮かぶ岩と柱の間、縁の中段のジャンプ台の上。
                    (new Vector2(-21.5f, 9f), 1), (new Vector2(-21.5f, 12.5f), 1), (new Vector2(21.5f, 9f), 1), (new Vector2(21.5f, 12.5f), 1),
                    (new Vector2(-14f, 4f), 1), (new Vector2(-14f, 12f), 1), (new Vector2(-14f, 17f), 1),
                    (new Vector2(14f, 4f), 1), (new Vector2(14f, 12f), 1), (new Vector2(14f, 17f), 1),
                    // 間欠泉の通り道。
                    (new Vector2(-33.5f, 17.5f), 1), (new Vector2(33.5f, 17.5f), 1), (new Vector2(-26f, -1f), 1), (new Vector2(26f, -1f), 1),
                    (new Vector2(-43f, 12f), 1), (new Vector2(43f, 12f), 1),
                    // 3 枚: 頂の岩の上、縁のいちばん上、リングの上の高い所。
                    (new Vector2(-7f, 25f), 3), (new Vector2(7f, 25f), 3), (new Vector2(-46.5f, 19f), 3), (new Vector2(46.5f, 19f), 3), (new Vector2(0f, 27f), 3),
                    // 20 枚: 溶岩の上（溶岩流が噴く所。壁の間欠泉か壁ジャンプで戻る）、いちばん上、ステージの端。
                    (new Vector2(-14f, -6f), 20), (new Vector2(14f, -6f), 20), (new Vector2(-28f, -5f), 20), (new Vector2(28f, -5f), 20),
                    (new Vector2(0f, 38f), 20), (new Vector2(-46f, 30f), 20), (new Vector2(46f, 30f), 20),
                    // 追加（総量を 2 倍に。ユーザーの要望、2026-10-21）。浮かぶ岩の下の溶岩の上（溶岩流から隠れきれない隠れ家。20 枚と 3 枚 2 つを固める）。
                    (new Vector2(-21.5f, 2f), 20), (new Vector2(21.5f, 2f), 20), (new Vector2(-18.5f, 2.5f), 3), (new Vector2(18.5f, 2.5f), 3),
                    (new Vector2(-24.5f, 2.5f), 3), (new Vector2(24.5f, 2.5f), 3),
                    // 柱の脇の溶岩の上、縁の低い段の壁の前、噴火口で飛ぶ道、黒曜石の上、縁の加速リングの上、間欠泉の先。
                    (new Vector2(-8f, -3f), 1), (new Vector2(8f, -3f), 1), (new Vector2(-31f, -1f), 1), (new Vector2(31f, -1f), 1),
                    (new Vector2(-37.5f, 6.5f), 3), (new Vector2(37.5f, 6.5f), 3), (new Vector2(-17f, 8f), 1), (new Vector2(17f, 8f), 1),
                    (new Vector2(-10.5f, 15.5f), 3), (new Vector2(10.5f, 15.5f), 3), (new Vector2(-26f, 15f), 1), (new Vector2(26f, 15f), 1),
                    (new Vector2(-33f, 11f), 1), (new Vector2(33f, 11f), 1),
                    // 頂の岩の横、高い所、縁のいちばん上の先。
                    (new Vector2(-12f, 24f), 1), (new Vector2(12f, 24f), 1), (new Vector2(-25f, 28f), 1), (new Vector2(25f, 28f), 1),
                    (new Vector2(-42.5f, 20f), 1), (new Vector2(42.5f, 20f), 1),
                    // 20 枚: 縁の上の上空、縁の外、真ん中の上空。
                    (new Vector2(-40f, 24f), 20), (new Vector2(40f, 24f), 20), (new Vector2(-52f, 8f), 20), (new Vector2(52f, 8f), 20),
                    (new Vector2(0f, 32f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 42f), new Vector2(-46f, 36f), new Vector2(46f, 36f), new Vector2(-14f, -9f), new Vector2(14f, -9f),
                    new Vector2(-28f, -9f), new Vector2(28f, -9f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7f), new Vector2(-28f, 6f), new Vector2(28f, 6f), new Vector2(-36f, 16f), new Vector2(36f, 16f),
                    new Vector2(-8f, 18f), new Vector2(8f, 18f),
                },
                Spawns = new[] { new Vector3(-3.5f, 1f, 0f), new Vector3(3.5f, 1f, 0f), new Vector3(-38.5f, 3f, 0f), new Vector3(38.5f, 3f, 0f),
                    // 5〜8 人目（チーム対戦。docs/19 T9-4。散らして置く D-84）。
                    new Vector3(-46.75f, 17f, 0f), new Vector3(46.75f, 17f, 0f), new Vector3(-7f, 23.6f, 0f), new Vector3(7f, 23.6f, 0f) },
                Decorate = Decorate,
                // 背景とオブジェクトのクオリティアップ（D-97）。
                Look = StageLook.FromTheme("Volcano", theme, StageMood.Lava),
            };
        }

        // 溶岩の湖、火口の壁、遠くの山、火の粉。
        // 火口の壁の高さ（x, z → y。網と、裾に置く物の高さに使う）。
        private static float CraterHeight(float x, float z)
        {
            float crest = 26f + Mathf.Abs(x) * 0.32f + (TerrainSculpt.Ridged(x * 0.035f, z * 0.035f, 11, 5) - 0.4f) * 24f;
            float rise = TerrainSculpt.Smooth(17f, 38f, z) * (1f - 0.45f * TerrainSculpt.Smooth(46f, 72f, z));
            float rough = (TerrainSculpt.Fbm(x * 0.18f, z * 0.18f, 5, 4) - 0.5f) * 5f;
            return -21.5f + (crest + rough) * rise;
        }

        private static void Decorate(StageDecor decor)
        {
            Material lava = decor.Unlit("Lava", new Color(1f, 0.38f, 0.05f));
            Material lavaHot = decor.Unlit("LavaHot", new Color(1f, 0.75f, 0.2f));
            // 縁と柱の奥行きの岩は、写真の絵の岩（2026-11-19 の要望: 地面の写真の絵）。
            Material rock = TerrainSculpt.GroundMaterial(decor, "RockGround", new Color(0.22f, 0.17f, 0.16f), TerrainSculpt.Photos.Rock,
                new Color(0.3f, 0.25f, 0.23f), TerrainSculpt.Photos.Rock, 0.2f);
            Material ember = decor.Unlit("Ember", new Color(1f, 0.55f, 0.15f));

            decor.Box("LavaLake", new Vector3(0f, -20f, 5f), new Vector3(140f, 2f, 40f), lava);
            // 溶岩流が噴く湖の、明るい溶岩の帯。
            decor.Box("BubblingLavaWest", new Vector3(-20f, -18.9f, 0f), new Vector3(28f, 0.3f, 4f), lavaHot);
            decor.Box("BubblingLavaEast", new Vector3(20f, -18.9f, 0f), new Vector3(28f, 0.3f, 4f), lavaHot);

            // 火口の壁（ステージの奥を囲む、ぎざぎざの岩の尾根。雑音で起伏をつけた高さの網。2026-11-18 の要望: 地面のモデリング）。
            // 溶岩の湖から立ち上がり、外ほど高い。根元は溶岩で赤く光り、割れ目の筋も光る。上の平らな所は灰。
            Material craterRock = TerrainSculpt.RockMaterial(decor, "CraterRock", m =>
            {
                m.SetColor("_RockColor", new Color(0.3f, 0.23f, 0.21f));
                m.SetColor("_RockDark", new Color(0.1f, 0.075f, 0.07f));
                m.SetColor("_TopColor", new Color(0.42f, 0.38f, 0.36f));
                m.SetFloat("_TopSlope", 0.72f);
                m.SetFloat("_TopHeight", 8f);
                m.SetFloat("_TopAmount", 0.7f);
                m.SetFloat("_Strata", 0.45f);
                m.SetFloat("_NoiseScale", 0.16f);
                m.SetColor("_GlowColor", new Color(3.2f, 0.9f, 0.12f));
                m.SetFloat("_GlowHeight", -19f);
                m.SetFloat("_GlowFade", 3.5f);
                m.SetFloat("_Cracks", 1.1f);
                m.SetFloat("_Ambient", 0.9f);
                TerrainSculpt.ApplyPhotos(m, TerrainSculpt.Photos.Rock, TerrainSculpt.Photos.Rock, 0.16f, 0.6f, 0.6f);
            });
            TerrainSculpt.Place(decor, "Volcano", "CraterRidge", new TerrainSculpt.Field(-130f, 130f, 16f, 72f, 1f, CraterHeight), craterRock);
            // 火口の壁の裾の焦げた枯れ木（Quaternius の Stylized Nature MegaKit）と、溶岩の湖の縁の岩（Kenney の Nature Kit）。
            // CC0。2026-11-19 にユーザーが追加。
            Color charred = new Color(0.32f, 0.24f, 0.22f);
            for (int i = 0; i < 9; i++)
            {
                float x = -108f + i * 27f + decor.Range(-5f, 5f);
                if (Mathf.Abs(x) < 50f)
                {
                    continue;
                }
                ThirdPartyProps.PlaceMtl(decor, "Quaternius/Nature", $"DeadTree_{1 + i % 5}", new Vector3(x, CraterHeight(x, 27f) - 0.5f, 27f),
                    decor.Range(0f, 360f), decor.Range(0.9f, 1.3f), charred, decor.Folder);
            }
            string[] rocks = { "rock_tallA", "rock_tallB", "rock_largeB", "rock_largeD", "rock_tallC" };
            foreach (float x in new[] { -66f, -58f, -52f, 52f, 59f, 67f, -24f, 26f })
            {
                ThirdPartyProps.PlaceMtl(decor, "Kenney/Nature", rocks[decor.Random.Next(rocks.Length)], new Vector3(x, -20f, decor.Range(14f, 22f)),
                    decor.Range(0f, 360f), decor.Range(5f, 9f), new Color(0.45f, 0.32f, 0.3f), decor.Folder);
            }
            // 縁と柱の奥行き（見た目だけ）。
            decor.Box("RimWestBack", new Vector3(-41f, -6f, 6f), new Vector3(14f, 32f, 8f), rock);
            decor.Box("RimEastBack", new Vector3(41f, -6f, 6f), new Vector3(14f, 32f, 8f), rock);
            decor.Box("SpireBack", new Vector3(0f, -10f, 4f), new Vector3(10f, 20f, 4f), rock);
            // 岩の割れ目の溶岩の光。
            for (int i = 0; i < 14; i++)
            {
                float x = decor.Random.NextDouble() < 0.5 ? decor.Range(-47f, -35f) : decor.Range(35f, 47f);
                decor.Quad("Crack", new Vector3(x, decor.Range(-18f, 1f), -2.02f), new Vector2(decor.Range(0.15f, 0.35f), decor.Range(1.5f, 4f)), lava);
            }

            // 遠くの火山（火口のくぼみのある円錐が裾でつながる高さの網。霧で空の地平線の色に溶ける）。
            Material farRock = TerrainSculpt.RockMaterial(decor, "FarVolcanoRock", m =>
            {
                m.SetColor("_RockColor", new Color(0.32f, 0.25f, 0.23f));
                m.SetColor("_RockDark", new Color(0.13f, 0.1f, 0.09f));
                m.SetColor("_TopColor", new Color(0.5f, 0.45f, 0.42f));
                m.SetFloat("_TopSlope", 0.8f);
                m.SetFloat("_TopHeight", 40f);
                m.SetFloat("_TopAmount", 0.5f);
                m.SetFloat("_Strata", 0.3f);
                m.SetFloat("_NoiseScale", 0.05f);
                m.SetColor("_GlowColor", Color.black);
                m.SetFloat("_Cracks", 0f);
                m.SetFloat("_Ambient", 0.9f);
                TerrainSculpt.ApplyPhotos(m, TerrainSculpt.Photos.Rock, TerrainSculpt.Photos.Rock, 0.05f, 0.45f, 0.4f);
            });
            TerrainSculpt.Place(decor, "Volcano", "FarVolcanoes", new TerrainSculpt.Field(-340f, 340f, 90f, 270f, 4f, (x, z) =>
            {
                float cones = TerrainSculpt.Volcano(x, z, -210f, 175f, 105f, 125f, 13f) + TerrainSculpt.Volcano(x, z, -55f, 205f, 150f, 150f, 17f)
                    + TerrainSculpt.Volcano(x, z, 95f, 165f, 85f, 105f, 11f) + TerrainSculpt.Volcano(x, z, 235f, 195f, 125f, 135f, 14f);
                float gullies = (TerrainSculpt.Ridged(x * 0.03f, z * 0.03f, 41, 4) - 0.45f) * 14f * TerrainSculpt.Smooth(0f, 30f, cones);
                float hills = TerrainSculpt.Fbm(x * 0.012f, z * 0.012f, 47, 3) * 18f;
                return -26f + cones + gullies + hills;
            }), farRock);
            // 火の粉。
            for (int i = 0; i < 40; i++)
            {
                float s = decor.Range(0.12f, 0.3f);
                decor.Sphere("Ember", new Vector3(decor.Range(-45f, 45f), decor.Range(-15f, 40f), decor.Range(4f, 20f)), new Vector3(s, s, s), ember);
            }
        }
    }
}
