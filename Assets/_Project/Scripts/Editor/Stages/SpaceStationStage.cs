using Airerage.Core.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// 宇宙ステーション（ORBITAL STATION）。真ん中のコアモジュール、左右のモジュールとドッキングポート、上のトラス、上の左右の宇宙船。
    /// 固有のギミック: 無重力（上の左右の、中がくりぬかれた宇宙船の中だけ。重力が 0.12 倍で、ほとんど落ちない。ユーザーの要望、
    /// 2026-10-19）、ワープゲート（左右の端のゲートは反対の端へつながる。いちばん下の小さなゲートは端から端まで行き来し、通った人を
    /// トラスの上へ戻す）、マスドライバー（Launcher。コアの両端から左右のモジュールへ飛ばす）。宇宙なので風はない。
    /// </summary>
    internal static class SpaceStationStage
    {
        public static StageLayout Create()
        {
            var theme = new StageTheme
            {
                Terrain = new Color(0.32f, 0.4f, 0.62f),
                Attachable = new Color(0.72f, 0.75f, 0.8f),
                Anchor = new Color(1f, 0.85f, 0.25f),
                SkyTint = new Color(0.05f, 0.05f, 0.1f),
                Ground = new Color(0.01f, 0.01f, 0.02f),
                Exposure = 0.04f,
                AtmosphereThickness = 0.2f,
                SunSize = 0.02f,
                Light = new Color(1f, 0.98f, 0.95f),
                LightIntensity = 1.5f,
                LightEuler = new Vector3(25f, -60f, 0f),
                AmbientSky = new Color(0.25f, 0.27f, 0.38f),
                AmbientEquator = new Color(0.15f, 0.16f, 0.24f),
                AmbientGround = new Color(0.08f, 0.1f, 0.18f),
                Fog = false,
            };
            theme.ZoneColors[StageZoneKind.LowGravity] = new Color(0.55f, 0.45f, 1f);
            theme.ZoneColors[StageZoneKind.Warp] = new Color(0.75f, 0.4f, 1f);
            theme.ZoneColors[StageZoneKind.Launcher] = new Color(0.3f, 1f, 0.85f);
            theme.ZoneColors[StageZoneKind.Updraft] = new Color(0.5f, 1f, 0.75f);

            // マスドライバー: コアの端から、上へ 19 m/s・横へ 15 m/s（低重力の高さに入らずに、隣のモジュールの上に降りる）。
            var driverWest = new Vector2(-15f, 19f);
            var driverEast = new Vector2(15f, 19f);
            float driverPower = driverEast.magnitude;

            return new StageLayout
            {
                Id = "SpaceStation",
                DisplayName = "ORBITAL STATION",
                Description = "Zero-g inside the hollow starships, warp gates that wrap around the edges, a roaming rescue gate below.",
                Color = new Color(0.65f, 0.45f, 1f),
                Terrain = new[]
                {
                    // モジュール（外板は接続できる）。
                    new TerrainBox("CoreModule", -8f, -4f, 8f, 0f, attachable: true),
                    new TerrainBox("ModuleWest", -30f, 2f, -18f, 5f, attachable: true),
                    new TerrainBox("ModuleEast", 18f, 2f, 30f, 5f, attachable: true),
                    new TerrainBox("DockWest", -44f, 10f, -36f, 13f, attachable: true),
                    new TerrainBox("DockEast", 36f, 10f, 44f, 13f, attachable: true),
                    // 上のトラス。
                    new TerrainBox("Truss", -14f, 22f, 14f, 22.6f, attachable: true),
                    // 上の左右の宇宙船（中がくりぬかれていて、中は無重力。両端が開いていて入れる。外板は接続できる）。
                    new TerrainBox("ShipFloorWest", -37f, 23f, -21f, 24f, attachable: true),
                    new TerrainBox("ShipRoofWest", -36f, 31f, -22f, 32f, attachable: true),
                    new TerrainBox("ShipTailWest", -37f, 29f, -36f, 31f, attachable: true),
                    new TerrainBox("ShipNoseWest", -22f, 29f, -21f, 31f, attachable: true),
                    new TerrainBox("ShipFloorEast", 21f, 23f, 37f, 24f, attachable: true),
                    new TerrainBox("ShipRoofEast", 22f, 31f, 36f, 32f, attachable: true),
                    new TerrainBox("ShipTailEast", 36f, 29f, 37f, 31f, attachable: true),
                    new TerrainBox("ShipNoseEast", 21f, 29f, 22f, 31f, attachable: true),
                },
                Anchors = new[]
                {
                    new Vector2(0f, 32f), new Vector2(-20f, 18f), new Vector2(20f, 18f), new Vector2(-40f, 24f), new Vector2(40f, 24f),
                    new Vector2(-12f, 10f), new Vector2(12f, 10f), new Vector2(0f, 12f),
                },
                // 左右は広めに（ユーザーの要望、2026-10-19）。端のワープゲートは範囲の内側に置く。
                BoundsMin = new Vector2(-60f, -22f),
                BoundsMax = new Vector2(60f, 50f),
                Theme = theme,
                Zones = new[]
                {
                    // 無重力: 宇宙船の中だけ（重力 0.12 倍、落ちる速さは 3 m/s まで）。
                    new ZoneSpec("ZeroGravityWest", StageZoneKind.LowGravity, new Vector2(-29f, 27.5f), new Vector2(15f, 7f), Vector2.up, 0.12f, 3f),
                    new ZoneSpec("ZeroGravityEast", StageZoneKind.LowGravity, new Vector2(29f, 27.5f), new Vector2(15f, 7f), Vector2.up, 0.12f, 3f),
                    // 左右の端のワープゲート（反対の端の内側へ。速度はそのまま）。
                    new ZoneSpec("WarpGateWest", StageZoneKind.Warp, new Vector2(-57f, 4f), new Vector2(2f, 6f), Vector2.up, 1f).To(new Vector2(54.5f, 4f)),
                    new ZoneSpec("WarpGateEast", StageZoneKind.Warp, new Vector2(57f, 4f), new Vector2(2f, 6f), Vector2.up, 1f).To(new Vector2(-54.5f, 4f)),
                    // いちばん下の小さなゲート: 端から端まで 12 秒で 1 往復する。通った人をトラスの上へ戻す。
                    new ZoneSpec("RescueGate", StageZoneKind.Warp, new Vector2(-40f, -17.5f), new Vector2(5f, 2.5f), Vector2.up, 1f).To(new Vector2(0f, 30f))
                        .Moving(new Vector2(80f, 0f), 12f),
                    // マスドライバー。
                    ZoneSpec.OnFloor("MassDriverWest", StageZoneKind.Launcher, -8f, -6.4f, 0f, driverWest, driverPower),
                    ZoneSpec.OnFloor("MassDriverEast", StageZoneKind.Launcher, 6.4f, 8f, 0f, driverEast, driverPower),
                    // ジャンプ台。
                    ZoneSpec.OnFloor("ModulePadWest", StageZoneKind.JumpPad, -25f, -23f, 5f, Vector2.up, 22f, 0.4f),
                    ZoneSpec.OnFloor("ModulePadEast", StageZoneKind.JumpPad, 23f, 25f, 5f, Vector2.up, 22f, 0.4f),
                    ZoneSpec.OnFloor("DockPadWest", StageZoneKind.JumpPad, -41f, -39f, 13f, Vector2.up, 18f, 0.4f),
                    ZoneSpec.OnFloor("DockPadEast", StageZoneKind.JumpPad, 39f, 41f, 13f, Vector2.up, 18f, 0.4f),
                    ZoneSpec.OnFloor("CorePad", StageZoneKind.JumpPad, -1f, 1f, 0f, Vector2.up, 20f, 0.4f),
                    // 加速リング。
                    new ZoneSpec("CoreRing", StageZoneKind.BoostRing, new Vector2(0f, 12f), new Vector2(2.4f, 3f), Vector2.right, 18f),
                    new ZoneSpec("OrbitRingWest", StageZoneKind.BoostRing, new Vector2(-34f, 18f), new Vector2(2.4f, 3f), new Vector2(1f, 0.2f), 16f),
                    new ZoneSpec("OrbitRingEast", StageZoneKind.BoostRing, new Vector2(34f, 18f), new Vector2(2.4f, 3f), new Vector2(-1f, 0.2f), 16f),
                },
                Coins = new (Vector2, int)[]
                {
                    // モジュールのジャンプ台の上。
                    (new Vector2(-24f, 10f), 1), (new Vector2(-24f, 16f), 1), (new Vector2(24f, 10f), 1), (new Vector2(24f, 16f), 1),
                    // コアの上と、リングで飛ぶ道。
                    (new Vector2(-4f, 3f), 1), (new Vector2(4f, 3f), 1), (new Vector2(0f, 5f), 1), (new Vector2(-6f, 12f), 1), (new Vector2(6f, 12f), 1),
                    // トラスの上、ドッキングポートの上。
                    (new Vector2(-8f, 24.5f), 1), (new Vector2(8f, 24.5f), 1), (new Vector2(-40f, 16f), 1), (new Vector2(40f, 16f), 1),
                    // マスドライバーで飛ぶ道、端のゲートの近く。
                    (new Vector2(-16f, 6.5f), 1), (new Vector2(16f, 6.5f), 1),
                    (new Vector2(-41f, 6f), 1), (new Vector2(41f, 6f), 1),
                    // 宇宙船の中（無重力で漂いながら取る）。
                    (new Vector2(-33f, 26f), 1), (new Vector2(-25f, 29f), 1), (new Vector2(33f, 26f), 1), (new Vector2(25f, 29f), 1),
                    // 3 枚: 宇宙船の中の真ん中、トラスの上の高い所、モジュールの間の下（取ったら下の動くゲートでトラスの上へ戻る）。
                    (new Vector2(-29f, 27.5f), 3), (new Vector2(29f, 27.5f), 3), (new Vector2(-14f, 26f), 3), (new Vector2(14f, 26f), 3),
                    (new Vector2(-13f, -8f), 3), (new Vector2(13f, -8f), 3),
                    // 20 枚: 外側のモジュールの外の下（落ちたら助からない）、いちばん上、ステージの端の高い所。
                    (new Vector2(-36f, -6f), 20), (new Vector2(36f, -6f), 20), (new Vector2(0f, 44f), 20), (new Vector2(-46f, 30f), 20), (new Vector2(46f, 30f), 20),
                    // 追加（総量を 2 倍に。ユーザーの要望、2026-10-21）。宇宙船の中（無重力の隠れ家。20 枚と 3 枚を固める）。
                    (new Vector2(-33f, 29.5f), 20), (new Vector2(33f, 29.5f), 20), (new Vector2(-25f, 25.5f), 3), (new Vector2(25f, 25.5f), 3),
                    (new Vector2(-29f, 30.5f), 1), (new Vector2(29f, 30.5f), 1),
                    // コアの下（20 枚と 3 枚。取ったら下の動くゲートでトラスの上へ戻る）、モジュールの下。
                    (new Vector2(0f, -7f), 20), (new Vector2(-5f, -6.5f), 3), (new Vector2(5f, -6.5f), 3), (new Vector2(-3f, -9f), 1),
                    (new Vector2(3f, -9f), 1), (new Vector2(-24f, -1f), 3), (new Vector2(24f, -1f), 3), (new Vector2(-20f, -3f), 1),
                    (new Vector2(20f, -3f), 1),
                    // ドッキングポートの下と外、トラスの上、コアのリングの上、軌道のリングの先、マスドライバーで飛ぶ道、高い所。
                    (new Vector2(-36f, 8f), 1), (new Vector2(36f, 8f), 1), (new Vector2(-46f, 12f), 1), (new Vector2(46f, 12f), 1),
                    (new Vector2(-3f, 25f), 1), (new Vector2(3f, 25f), 1), (new Vector2(-3f, 15f), 1), (new Vector2(3f, 15f), 1),
                    (new Vector2(-30f, 19f), 1), (new Vector2(30f, 19f), 1), (new Vector2(-12f, 8f), 1), (new Vector2(12f, 8f), 1),
                    (new Vector2(-16f, 34f), 1), (new Vector2(16f, 34f), 1), (new Vector2(-6f, 38f), 1), (new Vector2(6f, 38f), 1),
                    // 20 枚: ステージの端の上空。
                    (new Vector2(-50f, 20f), 20), (new Vector2(50f, 20f), 20),
                },
                Jackpots = new[]
                {
                    new Vector2(0f, 46f), new Vector2(-46f, 40f), new Vector2(46f, 40f), new Vector2(-36f, -8f), new Vector2(36f, -8f),
                    new Vector2(-13f, -12f), new Vector2(13f, -12f),
                },
                Banks = new[]
                {
                    new Vector2(0f, 7.5f), new Vector2(-24f, 21f), new Vector2(24f, 21f), new Vector2(-40f, 19.5f), new Vector2(40f, 19.5f),
                    new Vector2(-12f, 17f), new Vector2(12f, 17f),
                },
                Spawns = new[] { new Vector3(-4f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(-27f, 6f, 0f), new Vector3(27f, 6f, 0f) },
                Decorate = Decorate,
            };
        }

        // 星、惑星、モジュールの奥行きと窓、トラスの骨組み、太陽電池パネルの面、ドッキングの灯り。
        private static void Decorate(StageDecor decor)
        {
            Material star = decor.Unlit("Star", Color.white);
            Material starBlue = decor.Unlit("StarBlue", new Color(0.7f, 0.8f, 1f));
            Material planet = decor.Lit("Planet", new Color(0.25f, 0.5f, 0.85f));
            Material hull = decor.Lit("Hull", new Color(0.62f, 0.65f, 0.7f));
            Material panel = decor.Lit("Panel", new Color(0.12f, 0.2f, 0.55f));
            Material window = decor.Unlit("Window", new Color(0.55f, 0.95f, 1f));
            Material red = decor.Unlit("LightRed", new Color(1f, 0.2f, 0.2f));
            Material green = decor.Unlit("LightGreen", new Color(0.2f, 1f, 0.4f));

            for (int i = 0; i < 220; i++)
            {
                float s = decor.Range(0.3f, 1.1f);
                decor.Sphere("Star", new Vector3(decor.Range(-260f, 260f), decor.Range(-140f, 200f), decor.Range(180f, 260f)), new Vector3(s, s, s),
                    decor.Random.NextDouble() < 0.2 ? starBlue : star);
            }
            decor.Sphere("Planet", new Vector3(90f, -150f, 320f), new Vector3(300f, 300f, 300f), planet);

            // モジュールの奥行き（見た目だけ）と窓。
            (float MinX, float MinY, float MaxX, float MaxY)[] modules =
            {
                (-8f, -4f, 8f, 0f), (-30f, 2f, -18f, 5f), (18f, 2f, 30f, 5f), (-44f, 10f, -36f, 13f), (36f, 10f, 44f, 13f),
            };
            foreach (var m in modules)
            {
                var center = new Vector3((m.MinX + m.MaxX) * 0.5f, (m.MinY + m.MaxY) * 0.5f, 5f);
                decor.Box("ModuleBody", center, new Vector3(m.MaxX - m.MinX - 0.6f, m.MaxY - m.MinY - 0.4f, 6f), hull);
                for (float x = m.MinX + 1.5f; x < m.MaxX - 1f; x += 2.5f)
                {
                    decor.Quad("Porthole", new Vector3(x, center.y, -2.02f), new Vector2(0.8f, 0.6f), window);
                }
                decor.Sphere("DockLight", new Vector3(m.MinX + 0.3f, m.MaxY + 0.2f, -1.8f), new Vector3(0.3f, 0.3f, 0.3f), red);
                decor.Sphere("DockLight", new Vector3(m.MaxX - 0.3f, m.MaxY + 0.2f, -1.8f), new Vector3(0.3f, 0.3f, 0.3f), green);
            }
            // コアから伸びる支柱（奥）、トラスの骨組み、太陽電池パネルの面。
            decor.Box("SpineWest", new Vector3(-22f, 6f, 6f), new Vector3(28f, 0.5f, 0.5f), hull);
            decor.Box("SpineEast", new Vector3(22f, 6f, 6f), new Vector3(28f, 0.5f, 0.5f), hull);
            decor.Box("Pylon", new Vector3(0f, 11f, 5f), new Vector3(0.8f, 22f, 0.8f), hull);
            for (float x = -13f; x <= 13f; x += 2f)
            {
                GameObject strut = decor.Box("TrussStrut", new Vector3(x, 21.2f, 0.5f), new Vector3(0.15f, 2.4f, 0.15f), hull);
                strut.transform.rotation = Quaternion.Euler(0f, 0f, x % 4f == 1f || x % 4f == -3f ? 35f : -35f);
            }
            // 宇宙船（中の奥の壁、窓、後ろの噴射口、外の翼）。
            Material interior = decor.Lit("ShipInterior", new Color(0.16f, 0.18f, 0.28f));
            Material thruster = decor.Unlit("Thruster", new Color(0.4f, 0.85f, 1f));
            foreach (float side in new[] { -1f, 1f })
            {
                float cx = 29f * side;
                decor.Box("ShipInteriorWall", new Vector3(cx, 27.5f, 1.8f), new Vector3(15f, 6.98f, 0.4f), interior);
                decor.Box("ShipBody", new Vector3(cx, 27.5f, 4f), new Vector3(16f, 9f, 4f), hull);
                for (float x = -5f; x <= 5f; x += 2.5f)
                {
                    decor.Quad("ShipWindow", new Vector3(cx + x, 31.5f, -2.02f), new Vector2(1.2f, 0.4f), window);
                }
                decor.Sphere("ShipThruster", new Vector3(cx + 8.6f * side, 30f, 0f), new Vector3(0.8f, 1.8f, 1.8f), thruster);
                decor.Box("ShipWing", new Vector3(cx, 23.3f, 3.5f), new Vector3(10f, 0.3f, 8f), panel);
            }
        }
    }
}
