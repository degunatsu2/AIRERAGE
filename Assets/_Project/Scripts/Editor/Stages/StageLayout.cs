using System;
using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Presentation;
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

        /// <summary>
        /// ステージ選択に出す説明（英語と日本語。オプションの LANGUAGE で切り替える。2026-10-27）。日本語には半角の空白を入れない
        /// （uGUI は空白で改行するので、「40 秒」の間で行が変わる）。
        /// </summary>
        public string Description;
        public string DescriptionJa;
        public Color Color;

        public TerrainBox[] Terrain;
        public Vector2[] Anchors;
        public ZoneSpec[] Zones;

        /// <summary>動く足場と扉（ユーザーの要望、2026-10-26。なくてもよい）。</summary>
        public PlatformSpec[] Platforms = new PlatformSpec[0];

        /// <summary>ステージの仕掛け（赤外線とミサイル、ピッチャーマシン。ユーザーの要望、2026-10-26。なくてもよい）。</summary>
        public TrapSpec[] Traps = new TrapSpec[0];

        /// <summary>大きいアンカー（位置とエイムアシストの半径。ユーザーの要望、2026-10-26。なくてもよい）。</summary>
        public (Vector2 Position, float Radius)[] BigAnchors = new (Vector2, float)[0];
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

        /// <summary>見た目の台本（D-97。あれば StageDressing が足場・ギミック・空・遠景・後処理を飾る。なければ今までの見た目）。</summary>
        public StageLook Look;

        /// <summary>
        /// ステージの曲のファイルの名前（`Assets/_Project/Audio/Music/` の OGG。拡張子なし。ループのタグ付き。docs/18 6 章）。
        /// なければ、試合では仮の BGM（合成）を鳴らす。
        /// </summary>
        public string Music;

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
        public float DwellSeconds;
        public float CapHeight;
        public float FallLength;

        /// <summary>
        /// 見た目の出どころ（ワールドの位置。やかんの注ぎ口など。なければ範囲の下の辺の真ん中）。判定の範囲は出どころに合わせて決める
        /// （2026-11-20 の方針: 判定を見た目に合わせる）。
        /// </summary>
        public Vector3? Source;

        /// <summary>危険が電気を帯びているか（中のアンカーポイントにつないだワイヤーが電気を帯びる。研究所のプラズマ球）。</summary>
        public bool Electric;

        /// <summary>奥の地面の高さ（x, z → y。転がる噴石が飛び出して転がる火口の壁。見た目だけ）。</summary>
        public System.Func<float, float, float> Backdrop;

        /// <summary>予告の間に警告音を鳴らすか（危険・吹雪）。</summary>
        public bool WarningSound = true;

        /// <summary>見た目の種類（雷・水の流れ）。</summary>
        public StageZoneView.ZoneLook Look = StageZoneView.ZoneLook.Default;

        /// <summary>範囲が丸いか（箱に内接する円。ワープゲートのワームホールの口）。</summary>
        public bool Round;

        /// <summary>ワープゲートの出口に小さな渦を置くか。</summary>
        public bool ExitMouth;

        /// <summary>線と台の色（なければステージの種類ごとの色）。</summary>
        public Color? Color;

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

        /// <summary>見た目の種類を決める（雷・水の流れ）。</summary>
        public ZoneSpec Styled(StageZoneView.ZoneLook look)
        {
            Look = look;
            return this;
        }

        /// <summary>
        /// ワープゲートを紫の渦にする（ZoneLook.Wormhole。2026-11-24: 前の楕円の見た目を精巧に）。判定も渦と同じ楕円（StageZone.Round）。
        /// exitMouth なら出口に小さな逆回りの渦を置く。
        /// </summary>
        public ZoneSpec Wormhole(bool exitMouth)
        {
            Look = StageZoneView.ZoneLook.Wormhole;
            Round = true;
            ExitMouth = exitMouth;
            return this;
        }

        /// <summary>このギミックだけ色を変える。</summary>
        public ZoneSpec Tinted(Color color)
        {
            Color = color;
            return this;
        }

        /// <summary>travel だけ行って戻る動きにする（1 往復 seconds 秒）。</summary>
        public ZoneSpec Moving(Vector2 travel, float seconds)
        {
            Travel = travel;
            TravelSeconds = seconds;
            return this;
        }

        /// <summary>travel だけ行って戻る動きにし、両端で dwellSeconds 秒ずつ止まって待つ（1 往復 seconds 秒に含む）。</summary>
        public ZoneSpec Moving(Vector2 travel, float seconds, float dwellSeconds)
        {
            Moving(travel, seconds);
            DwellSeconds = dwellSeconds;
            return this;
        }

        /// <summary>危険の柱の上を丸くする（丸みの高さ m。判定も見た目もこの形。2026-11-20 の方針: 見た目を自然にして判定を合わせる）。</summary>
        public ZoneSpec Capped(float height)
        {
            CapHeight = height;
            return this;
        }

        /// <summary>
        /// 危険の柱を「落ちてくる物」にする（長さ m の帯が上の端から下の端へだんだん速く落ち、帯の所だけ当たる。つらら。判定も見た目も
        /// この動き。2026-11-20 の方針）。
        /// </summary>
        public ZoneSpec Falling(float length)
        {
            FallLength = length;
            return this;
        }

        /// <summary>危険に電気を帯びさせる（中のアンカーポイントにつないだワイヤーが電気を帯び、ロープに触れた相手に電気の攻撃）。</summary>
        public ZoneSpec Electrified()
        {
            Electric = true;
            return this;
        }

        /// <summary>奥の地面の高さを決める（転がる噴石が、その地面の上を飛び出して転がる）。</summary>
        public ZoneSpec Behind(System.Func<float, float, float> height)
        {
            Backdrop = height;
            return this;
        }

        /// <summary>見た目の出どころ（ワールドの位置）を決める。</summary>
        public ZoneSpec From(Vector3 source)
        {
            Source = source;
            return this;
        }

        /// <summary>見た目の出どころの、ギミックの物体からの位置（なければ範囲の下の辺の真ん中）。</summary>
        public Vector3 LocalSource => Source.HasValue
            ? new Vector3(Source.Value.x - Center.x, Source.Value.y - Center.y, Source.Value.z)
            : new Vector3(0f, -Size.y * 0.5f, 0f);

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

        /// <summary>扉とボタンの組（ボタンで開く扉と、そのボタン。0 なら組なし）。</summary>
        public int Group;

        /// <summary>周期で開く扉の周期と、周期の初めの開いている長さ（秒。0 なら周期では開かない）。</summary>
        public float OpenEverySeconds;
        public float OpenForSeconds;

        /// <summary>箱の中心をアンカーポイントにするか（道のりを動く足場だけ。埠頭の動くボラード）と、エイムアシストの半径（0 ならふつう）。</summary>
        public bool AnchorPoint;
        public float AnchorRadius;

        /// <summary>荷役の網（Kind が Hoist）: 重なった人を跳ね返す速さ（m/s。下がる長さは OpenHeight、アンカーの半径は AnchorRadius）。</summary>
        public float BouncePower;

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

        /// <summary>
        /// 荷役の網にする（クレーンに吊った網。ワイヤーをつないでいる間は drop だけ下がり、重なった人を bouncePower で跳ね返す。真ん中は
        /// アンカーポイント〈エイムアシストの半径 anchorRadius〉）。見た目は Visual で作る。
        /// </summary>
        public PlatformSpec Hoist(float drop, float bouncePower, float anchorRadius)
        {
            Kind = StagePlatformKind.Hoist;
            OpenHeight = drop;
            BouncePower = bouncePower;
            AnchorPoint = true;
            AnchorRadius = anchorRadius;
            return this;
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

        /// <summary>
        /// 扉にする（closed は Center。openHeight だけ上に開く）。group が正なら同じ組のボタンで開き（扉にはワイヤーがつながらない）、
        /// 0 なら扉そのものにワイヤーをつなぐと開く。
        /// </summary>
        public static PlatformSpec Door(string name, Vector2 center, Vector2 size, float openHeight, float openSeconds, float closeSeconds,
            int group = 0)
        {
            return new PlatformSpec(name, center, size, group == 0)
            {
                Kind = StagePlatformKind.Door,
                OpenHeight = openHeight,
                OpenSeconds = openSeconds,
                CloseSeconds = closeSeconds,
                Group = group,
            };
        }

        /// <summary>周期で開く扉にする（every 秒ごとに、初めの openFor 秒だけ開く。ずれ phase 秒。ワイヤーはつながらない）。</summary>
        public static PlatformSpec TimedDoor(string name, Vector2 center, Vector2 size, float openHeight, float openSeconds, float closeSeconds,
            float every, float openFor, float phase = 0f)
        {
            return new PlatformSpec(name, center, size, false)
            {
                Kind = StagePlatformKind.Door,
                OpenHeight = openHeight,
                OpenSeconds = openSeconds,
                CloseSeconds = closeSeconds,
                OpenEverySeconds = every,
                OpenForSeconds = openFor,
                PhaseSeconds = phase,
            };
        }

        /// <summary>扉のボタンにする（ワイヤーをつないでいる間、同じ組の扉が開く）。</summary>
        public static PlatformSpec Button(string name, Vector2 center, Vector2 size, int group)
        {
            return new PlatformSpec(name, center, size, true)
            {
                Kind = StagePlatformKind.Button,
                Group = group,
            };
        }

        /// <summary>ゆっくり動き出して止まるようにする。</summary>
        public PlatformSpec Eased()
        {
            Smooth = true;
            return this;
        }

        /// <summary>箱の中心をアンカーポイントにする（足場と一緒に動く。radius はエイムアシストの半径。0 ならふつう）。</summary>
        public PlatformSpec Anchor(float radius = 0f)
        {
            AnchorPoint = true;
            AnchorRadius = radius;
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

    /// <summary>
    /// ステージの仕掛け 1 つ（Core の StageTrap の値と名前。ユーザーの要望、2026-10-26）。赤外線は Center が赤外線の範囲の中心、
    /// ピッチャーマシンは Center が発射口。
    /// </summary>
    internal sealed class TrapSpec
    {
        public readonly string Name;
        public readonly StageTrapKind Kind;
        public readonly Vector2 Center;
        public int Power;
        public float Knockback;

        // 赤外線とミサイル。
        public Vector2 BeamSize;
        public Vector2 MuzzleOffset;
        public Vector2 Direction;
        public float Speed;
        public float Range;
        public float ArmSeconds;
        public float CooldownSeconds;
        public Vector2 MissileSize = new Vector2(1.6f, 0.6f);

        // ピッチャーマシン。
        public Vector2 LaunchVelocity;
        public float PeriodSeconds;
        public float PhaseSeconds;
        public float Radius;
        public float MinHitSpeed;
        public float BlinkSeconds;

        /// <summary>マシンの向き（壁の左に付くなら 1、右なら -1。見た目だけ）。</summary>
        public float Facing = 1f;

        // 鬼火の輪（速さは Speed）。
        public Vector2[] Waypoints;
        public float HoverSeconds;
        public Vector2 RingSize;
        public int Reward;
        public float RespawnMinSeconds;
        public float RespawnMaxSeconds;

        // ドーナツフライヤー（跳ね方のコース。周期とずれは PeriodSeconds・PhaseSeconds、半径は Radius）。
        public DonutCourseSpec[] DonutCourses;

        private TrapSpec(string name, StageTrapKind kind, Vector2 center)
        {
            Name = name;
            Kind = kind;
            Center = center;
        }

        /// <summary>
        /// 赤外線（from から to まで。横か縦の線）と、from の端の発射口から to へ撃つミサイル。thickness は赤外線の太さ。
        /// </summary>
        public static TrapSpec Tripwire(string name, Vector2 from, Vector2 to, float thickness, float speed, int power, float knockback,
            float armSeconds, float cooldownSeconds)
        {
            Vector2 center = (from + to) * 0.5f;
            Vector2 span = to - from;
            var size = new Vector2(Mathf.Max(Mathf.Abs(span.x), thickness), Mathf.Max(Mathf.Abs(span.y), thickness));
            return new TrapSpec(name, StageTrapKind.Tripwire, center)
            {
                BeamSize = size,
                MuzzleOffset = from - center,
                Direction = span.normalized,
                Speed = speed,
                Range = span.magnitude,
                Power = power,
                Knockback = knockback,
                ArmSeconds = armSeconds,
                CooldownSeconds = cooldownSeconds,
            };
        }

        /// <summary>
        /// 鬼火の輪（waypoints の間を秒速 speed でランダムに飛び回り、地点で hover 秒漂う。くぐった人に reward 枚。respawnMin〜respawnMax 秒で
        /// また現れる）。物体は最初の地点に置く。
        /// </summary>
        public static TrapSpec WispRing(string name, Vector2[] waypoints, float speed, float hover, Vector2 ringSize, int reward, float respawnMin,
            float respawnMax)
        {
            return new TrapSpec(name, StageTrapKind.WispRing, waypoints[0])
            {
                Waypoints = waypoints,
                Speed = speed,
                HoverSeconds = hover,
                RingSize = ringSize,
                Reward = reward,
                RespawnMinSeconds = respawnMin,
                RespawnMaxSeconds = respawnMax,
            };
        }

        /// <summary>
        /// ドーナツフライヤー（出口 muzzle から period 秒ごとにドーナツを出し、landings の着地点を順に弾んで、最後の点で消える。ずれ phase 秒。
        /// 半径 radius・跳ねの高さ hop）。物体は出口に置く。
        /// </summary>
        public static TrapSpec DonutFryer(string name, Vector2 muzzle, DonutCourseSpec[] courses, float period, float phase, float radius)
        {
            return new TrapSpec(name, StageTrapKind.DonutFryer, muzzle)
            {
                DonutCourses = courses,
                PeriodSeconds = period,
                PhaseSeconds = phase,
                Radius = radius,
            };
        }

        /// <summary>ピッチャーマシン（muzzle から launchVelocity で、period 秒ごとに打ち出す。ずれ phase 秒）。</summary>
        public static TrapSpec Pitcher(string name, Vector2 muzzle, Vector2 launchVelocity, float period, float phase, float radius, int power,
            float knockback, float minHitSpeed, float blinkSeconds)
        {
            return new TrapSpec(name, StageTrapKind.Pitcher, muzzle)
            {
                LaunchVelocity = launchVelocity,
                PeriodSeconds = period,
                PhaseSeconds = phase,
                Radius = radius,
                Power = power,
                Knockback = knockback,
                MinHitSpeed = minHitSpeed,
                BlinkSeconds = blinkSeconds,
                Facing = launchVelocity.x >= 0f ? 1f : -1f,
            };
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

    /// <summary>ドーナツの跳ね方（1 つのコース）: 名前（設計書とテスト用）、着地点（中心。最後は消える所）、跳ねの高さ（m）。</summary>
    internal readonly struct DonutCourseSpec
    {
        public DonutCourseSpec(string name, float hop, params Vector2[] landings)
        {
            Name = name;
            Hop = hop;
            Landings = landings;
        }

        public string Name { get; }

        public float Hop { get; }

        public Vector2[] Landings { get; }
    }
}
