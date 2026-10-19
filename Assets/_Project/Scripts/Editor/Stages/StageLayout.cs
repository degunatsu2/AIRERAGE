using System;
using System.Collections.Generic;
using Airerage.Core.Simulation;
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
