using System;
using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;
using TerrainBox = Airerage.Editor.SandboxWireSceneBuilder.TerrainBox;

namespace Airerage.Editor.Stages
{
    /// <summary>
    /// ステージ 1 つの配置（docs/15 6.11）: 地形、アンカー、ギミック、コインの出現地点、100 枚のコインの候補、拠点の候補、撃破の範囲、
    /// 出現位置、見た目（空・光・材質・背景の飾り）。StageSceneBuilder がこれからステージのシーンを作る。値はすべて【仮】。
    /// </summary>
    internal sealed class StageLayout
    {
        /// <summary>アセットとシーンの名前のもと（"CityDay" → Stage_CityDay.unity、CityDay.asset）。</summary>
        public string Id;

        public string DisplayName;
        public string Description;
        public Color Color;

        public TerrainBox[] Terrain;
        public Vector2[] Anchors;
        public ZoneSpec[] Zones;

        /// <summary>動く足場と扉（ユーザーの要望、2026-10-26。なくてもよい）。</summary>
        public PlatformSpec[] Platforms = new PlatformSpec[0];
        public (Vector2 Position, int Value)[] Coins;
        public Vector2[] Jackpots;
        public Vector2[] Banks;
        public Vector2 BoundsMin;
        public Vector2 BoundsMax;

        /// <summary>出現位置（番号の順。2 人なら最初の 2 つ）。</summary>
        public Vector3[] Spawns;

        public StageTheme Theme;

        /// <summary>背景の飾り（見た目だけ。当たりはない）を作る。</summary>
        public Action<StageDecor> Decorate;

        public string SceneName => "Stage_" + Id;
    }

    /// <summary>ギミック 1 つ（Core の StageZone の値と名前）。周期・出口・柱の幅・動きは必要な種類だけ。</summary>
    internal sealed class ZoneSpec
    {
        public readonly string Name;
        public readonly StageZoneKind Kind;
        public readonly Vector2 Center;
        public readonly Vector2 Size;
        public readonly Vector2 Direction;
        public readonly float Power;
        public readonly float Limit;
        public float PeriodSeconds;
        public float ActiveSeconds;
        public float PhaseSeconds;
        public float JitterSeconds;
        public float WarningSeconds;
        public Vector2 Target;
        public float Width;
        public Vector2 Travel;
        public float TravelSeconds;

        /// <summary>予告の間に警告音を鳴らすか（危険・吹雪）。</summary>
        public bool WarningSound = true;

        public ZoneSpec(string name, StageZoneKind kind, Vector2 center, Vector2 size, Vector2 direction, float power, float limit = 0f)
        {
            Name = name;
            Kind = kind;
            Center = center;
            Size = size;
            Direction = direction;
            Power = power;
            Limit = limit;
        }

        /// <summary>周期のあるギミックにする（秒）。jitterSeconds は周期ごとにランダムに足す長さの上限、warningSeconds は予告の長さ。</summary>
        public ZoneSpec Every(float periodSeconds, float activeSeconds, float phaseSeconds, float jitterSeconds = 0f, float warningSeconds = 0f)
        {
            PeriodSeconds = periodSeconds;
            ActiveSeconds = activeSeconds;
            PhaseSeconds = phaseSeconds;
            JitterSeconds = jitterSeconds;
            WarningSeconds = warningSeconds;
            return this;
        }

        /// <summary>危険の柱の幅を決める（m）。</summary>
        public ZoneSpec Column(float width)
        {
            Width = width;
            return this;
        }

        /// <summary>予告の警告音を鳴らさない（表示だけ）。</summary>
        public ZoneSpec Silent()
        {
            WarningSound = false;
            return this;
        }

        /// <summary>travel だけ行って戻る動きにする（1 往復 seconds 秒）。</summary>
        public ZoneSpec Moving(Vector2 travel, float seconds)
        {
            Travel = travel;
            TravelSeconds = seconds;
            return this;
        }

        /// <summary>ワープゲートの出口を決める。</summary>
        public ZoneSpec To(Vector2 target)
        {
            Target = target;
            return this;
        }

        /// <summary>床の上の薄いギミック（ジャンプ台・スピードレーン・発射台・氷）を、上面 top の上に置く。</summary>
        public static ZoneSpec OnFloor(string name, StageZoneKind kind, float minX, float maxX, float top, Vector2 direction, float power,
            float height = 0.3f)
        {
            return new ZoneSpec(name, kind, new Vector2((minX + maxX) * 0.5f, top + height * 0.5f), new Vector2(maxX - minX, height), direction, power);
        }
    }

    /// <summary>
    /// 動く足場・扉 1 つ（Core の StagePlatform の値と名前と見た目。ユーザーの要望、2026-10-26）。Center は道のりの元の位置（扉は閉じた
    /// ときの中心）。Visual がなければ、大きさの箱を作る（Attachable なら縁をオレンジに光らせる）。
    /// </summary>
    internal sealed class PlatformSpec
    {
        public readonly string Name;
        public readonly Vector2 Center;
        public readonly Vector2 Size;
        public readonly bool Attachable;
        public StagePlatformKind Kind = StagePlatformKind.Path;
        public StagePlatformAuthoring.Key[] Keys = { new StagePlatformAuthoring.Key(0f, Vector2.zero) };
        public float PeriodSeconds = 10f;
        public float PhaseSeconds;
        public float ActiveFromSeconds;
        public float ActiveToSeconds;
        public bool Smooth;
        public float OpenHeight;
        public float OpenSeconds;
        public float CloseSeconds;

        /// <summary>箱の色（なければステージの地形の色）。</summary>
        public Color? Color;

        /// <summary>見た目を作る（visual は足場と一緒に動く子。原点が足場の中心）。なければ箱。</summary>
        public Action<StageDecor, Transform, PlatformSpec> Visual;

        public PlatformSpec(string name, Vector2 center, Vector2 size, bool attachable)
        {
            Name = name;
            Center = center;
            Size = size;
            Attachable = attachable;
        }

        /// <summary>道のり（秒とずれの並び。最初は 0 秒・ずれ 0）を決める。</summary>
        public PlatformSpec Path(float periodSeconds, params (float Seconds, Vector2 Offset)[] keys)
        {
            Kind = StagePlatformKind.Path;
            PeriodSeconds = periodSeconds;
            Keys = Array.ConvertAll(keys, k => new StagePlatformAuthoring.Key(k.Seconds, k.Offset));
            return this;
        }

        /// <summary>
        /// from から to まで、control へ引かれる弧（2 次のベジェ曲線）を travelSeconds 秒で進み、周期 periodSeconds の残りはない
        /// （ロールケーキ）。元の位置は from。
        /// </summary>
        public static PlatformSpec Arc(string name, Vector2 size, bool attachable, Vector2 from, Vector2 control, Vector2 to, float travelSeconds,
            float periodSeconds, float phaseSeconds = 0f, int segments = 24)
        {
            var spec = new PlatformSpec(name, from, size, attachable)
            {
                PeriodSeconds = periodSeconds,
                PhaseSeconds = phaseSeconds,
                ActiveFromSeconds = 0f,
                ActiveToSeconds = travelSeconds,
            };
            var keys = new StagePlatformAuthoring.Key[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector2 point = (1f - t) * (1f - t) * from + 2f * (1f - t) * t * control + t * t * to;
                keys[i] = new StagePlatformAuthoring.Key(travelSeconds * t, point - from);
            }
            spec.Keys = keys;
            return spec;
        }

        /// <summary>扉にする（closed は Center。openHeight だけ上に開く）。</summary>
        public static PlatformSpec Door(string name, Vector2 center, Vector2 size, float openHeight, float openSeconds, float closeSeconds)
        {
            return new PlatformSpec(name, center, size, true)
            {
                Kind = StagePlatformKind.Door,
                OpenHeight = openHeight,
                OpenSeconds = openSeconds,
                CloseSeconds = closeSeconds,
            };
        }

        /// <summary>ゆっくり動き出して止まるようにする。</summary>
        public PlatformSpec Eased()
        {
            Smooth = true;
            return this;
        }

        /// <summary>周期のずれを決める（秒）。</summary>
        public PlatformSpec Phase(float seconds)
        {
            PhaseSeconds = seconds;
            return this;
        }

        public PlatformSpec Tinted(Color color)
        {
            Color = color;
            return this;
        }

        public PlatformSpec Looks(Action<StageDecor, Transform, PlatformSpec> visual)
        {
            Visual = visual;
            return this;
        }
    }

    /// <summary>ステージの見た目（空、光、霧、材質の色、ギミックの色）。</summary>
    internal sealed class StageTheme
    {
        public Color Terrain = new Color(0.55f, 0.55f, 0.58f);
        public Color Attachable = new Color(0.25f, 0.6f, 0.75f);
        public Color Anchor = new Color(0.95f, 0.85f, 0.2f);

        // 空（Skybox/Procedural）。
        public Color SkyTint = new Color(0.5f, 0.5f, 0.5f);
        public Color Ground = new Color(0.37f, 0.35f, 0.34f);
        public float Exposure = 1.3f;
        public float AtmosphereThickness = 1f;
        public float SunSize = 0.04f;

        // 光。
        public Color Light = new Color(1f, 0.96f, 0.88f);
        public float LightIntensity = 1.2f;
        public Vector3 LightEuler = new Vector3(50f, -30f, 0f);
        public Color AmbientSky = new Color(0.55f, 0.62f, 0.75f);
        public Color AmbientEquator = new Color(0.42f, 0.42f, 0.45f);
        public Color AmbientGround = new Color(0.25f, 0.24f, 0.23f);

        // 霧（背景の飾りを遠くに見せる）。
        public bool Fog = true;
        public Color FogColor = new Color(0.7f, 0.78f, 0.88f);
        public float FogStart = 50f;
        public float FogEnd = 170f;

        /// <summary>ギミックの線と台の色（なければ StageSceneBuilder の標準の色）。</summary>
        public readonly Dictionary<StageZoneKind, Color> ZoneColors = new Dictionary<StageZoneKind, Color>();
    }
}
