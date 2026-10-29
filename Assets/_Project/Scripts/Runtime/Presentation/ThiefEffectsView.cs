using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// THIEF の技の表示（docs/14 1.6。ユーザーの案、2026-10-28）。状態を読むだけで、書き換えない。
    /// - 煙球（地上技）: 落ちていく暗い灰色の玉。
    /// - 煙（煙球の着弾、分身が消えたとき）: ふちのやわらかい煙のかたまり（コードで描いた絵の板）を重ねた、気体のような雲
    ///   （ユーザーの要望、2026-10-29）。かたまりはゆっくり回って広がり、消える前に薄くなる。かたまりの内側は不透明で、真ん中・内側の輪 5 つ・
    ///   外側の輪 10 個をすき間なく並べるので、煙の円の中の物（後ろの物も）は見えない（2 回目の要望、2026-10-29）。
    ///   カメラの前（_smokeDepth）に、z = 0 の面の円と画面で重なる位置・大きさで出す。出た瞬間に音。
    /// - タレントスナッチの弾（Q）: こげ茶の小さな弾と短い尾。撃った瞬間に音。
    /// - 置いたギミック（Q で奪ったもの）: ステージの本物の見た目を写す（PlacedGimmickLooks。ユーザーの要望、2026-10-29）。本物が
    ///   見つからなければ、範囲の枠と流れる印。
    /// - 置いた仕掛け: 本物の見た目（発射装置・赤外線・ランプ・ミサイル、ピッチャーマシン・ボール）を写し、本物と同じように動かす（赤外線は
    ///   ゆっくり明滅し、触れられたら予告の斜線と警報、撃ったらミサイル、待ちの間は消えて RELOAD のゲージ。ボールは止まったら点滅）。
    /// - 透明（E）: 足元に薄い影だけを出す（体と周りの表示は CharacterView が隠す。分身は CharacterView.RenderClone）。
    /// - 技・ギミックを奪った瞬間に、こげ茶の光と「STEAL!」。バンクハイスト（R）でコインを得た瞬間の光は、ほかのキャラクターの必殺技と
    ///   同じ虹色の光（CharacterView）。
    /// </summary>
    public sealed class ThiefEffectsView : MonoBehaviour
    {
        private const float Z = -0.5f;
        private const int SmokePuffs = 16;
        private const int PuffTextureSize = 128;
        private const float SmokeGrowSeconds = 0.3f;
        private const float SmokeFadeSeconds = 1.2f;
        private const float ShotTrailLength = 1.6f;
        private const float BlinkPerSecond = 6f;
        private const float ShadowAlpha = 0.28f;

        private static readonly Color ShotColor = new Color(0.36f, 0.2f, 0.1f);
        private static readonly Color StealColor = new Color(0.62f, 0.4f, 0.2f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("煙を z = 0 の面に重ねるのに使うカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("線の見本（隠しておく）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("煙のひとかたまりの見本（SpriteRenderer。絵は起動時に描く。隠しておく）")]
        [SerializeField] private SpriteRenderer _puffTemplate;
        [Tooltip("煙球の見本（隠しておく）")]
        [SerializeField] private GameObject _ballTemplate;
        [Tooltip("タレントスナッチの弾の見本（隠しておく）")]
        [SerializeField] private GameObject _shotTemplate;
        [Tooltip("透明の間の足元の影の見本（隠しておく）")]
        [SerializeField] private GameObject _shadowTemplate;
        [Tooltip("奪った瞬間の光の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private BurstEffectView _stealTemplate;
        [Tooltip("音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("煙を出すカメラの前の深さ（z。地形の手前の面より前）")]
        [SerializeField] private float _smokeDepth = -4f;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private readonly List<Character> _characters = new List<Character>();
        private readonly PlacedGimmickLooks _looks = new PlacedGimmickLooks();
        private readonly List<Rect> _areas = new List<Rect>();
        private readonly List<Vector2> _icons = new List<Vector2>();
        private ISimulationFeed _feed;
        private Sprite _puffSprite;
        private bool _primed;

        private sealed class Shown
        {
            public WorldObjectKind Kind;
            public GameObject Visual;
            public float BornAt;
            public bool Copied;
            public SpriteRenderer[] Puffs;
            public Vector3[] PuffOffsets;
            public float[] PuffSizes;
            public float[] PuffSpins;
            public float[] PuffShades;
            public LineRenderer Trail;
            public LineRenderer Frame;
            public LineRenderer Marks;
            public Transform Beam;
            public Renderer BeamRenderer;
            public Renderer Lamp;
            public Transform Missile;
            public Transform Launcher;
            public RingGaugeView Reload;
            public WarningOverlay Overlay;
            public Vector2 BeamSize;
            public bool Arming;
            public bool Flying;
        }

        private sealed class Character
        {
            public GameObject Shadow;
            public BurstEffectView Steal;
            public int Steals = -1;
        }

        /// <summary>出している煙の数（テスト用）。</summary>
        public int SmokeCount => CountOf(WorldObjectKind.Smoke);

        /// <summary>出している置いたギミックと仕掛けの数（テスト用）。</summary>
        public int PlacedCount => CountOf(WorldObjectKind.PlacedZone) + CountOf(WorldObjectKind.PlacedTrap);

        /// <summary>置いたギミックと仕掛けのうち、ステージの本物の見た目を写して出している数（テスト用）。</summary>
        public int CopiedCount
        {
            get
            {
                int count = 0;
                foreach (Shown shown in _shown.Values)
                {
                    count += shown.Copied ? 1 : 0;
                }
                return count;
            }
        }

        /// <summary>出しているタレントスナッチの弾の数（テスト用）。</summary>
        public int ShotCount => CountOf(WorldObjectKind.SnatchShot);

        /// <summary>キャラクター index の足元に、透明の影を出しているか（テスト用）。</summary>
        public bool ShowsShadow(int index) => index >= 0 && index < _characters.Count && _characters[index].Shadow.activeSelf;

        /// <summary>奪って STEAL! を出した回数（テスト用）。</summary>
        public int StealBursts { get; private set; }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            GameObject[] templates = { _ballTemplate, _shotTemplate, _shadowTemplate };
            if (_feed == null || _lineTemplate == null || _puffTemplate == null || System.Array.IndexOf(templates, null) >= 0 || _stealTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            _puffTemplate.gameObject.SetActive(false);
            foreach (GameObject template in templates)
            {
                template.SetActive(false);
            }
            _stealTemplate.gameObject.SetActive(false);
            _puffSprite = CreatePuffSprite();
        }

        private void OnDestroy()
        {
            if (_puffSprite != null)
            {
                Destroy(_puffSprite.texture);
                Destroy(_puffSprite);
            }
        }

        private void LateUpdate()
        {
            RenderObjects();
            RenderCharacters();
            _primed = true;
        }

        // ---------------------------------------------------------------- 置かれた物と投射物

        private void RenderObjects()
        {
            WorldObjects objects = _feed.Objects;
            _present.Clear();
            if (objects != null)
            {
                foreach (WorldObject item in objects.Items)
                {
                    if (item.Kind <= WorldObjectsView.LastKind || item.Ended)
                    {
                        continue;
                    }
                    _present.Add(item.Id);
                    if (!_shown.TryGetValue(item.Id, out Shown shown) || shown.Kind != item.Kind)
                    {
                        if (shown != null)
                        {
                            Destroy(shown.Visual);
                        }
                        shown = Create(item);
                        _shown[item.Id] = shown;
                        PlaySpawn(item.Kind);
                    }
                    if (shown.Visual != null)
                    {
                        UpdateShown(shown, item);
                    }
                }
            }
            _gone.Clear();
            foreach (KeyValuePair<int, Shown> pair in _shown)
            {
                if (!_present.Contains(pair.Key))
                {
                    _gone.Add(pair.Key);
                }
            }
            foreach (int id in _gone)
            {
                Destroy(_shown[id].Visual);
                _shown.Remove(id);
            }
        }

        private Shown Create(in WorldObject item)
        {
            var shown = new Shown { Kind = item.Kind, BornAt = Time.time };
            switch (item.Kind)
            {
                case WorldObjectKind.SmokeBall:
                    shown.Visual = Root(item);
                    Child(_ballTemplate, shown.Visual.transform);
                    break;
                case WorldObjectKind.Smoke:
                    shown.Visual = Root(item);
                    CreatePuffs(shown, item.Id);
                    break;
                case WorldObjectKind.SnatchShot:
                    shown.Visual = Root(item);
                    Child(_shotTemplate, shown.Visual.transform);
                    shown.Trail = Line(shown.Visual.transform, "Trail", ShotColor, 0.12f);
                    break;
                case WorldObjectKind.PlacedZone:
                    CreatePlacedZone(shown, item);
                    break;
                case WorldObjectKind.PlacedTrap:
                    CreatePlacedTrap(shown, item);
                    break;
                case WorldObjectKind.TrapBall:
                    shown.Visual = PlacedGimmickLooks.CopyBall(_looks.TrapSource(_feed.Traps, item.Variant), transform);
                    shown.Copied = shown.Visual != null;
                    if (shown.Visual == null)
                    {
                        Debug.LogWarning($"{name}: 置いたピッチャーマシン（仕掛け {item.Variant}）のボールの見た目が見つかりません", this);
                    }
                    break;
            }
            return shown;
        }

        private GameObject Root(in WorldObject item)
        {
            var root = new GameObject($"Thief{item.Kind}{item.Id}");
            root.transform.SetParent(transform, false);
            return root;
        }

        private void UpdateShown(Shown shown, in WorldObject item)
        {
            var position = new Vector3(item.Position.X, item.Position.Y, Z);
            switch (item.Kind)
            {
                case WorldObjectKind.SmokeBall:
                {
                    shown.Visual.transform.position = position;
                    float radius = SmokeBallRadius(item.Owner);
                    shown.Visual.transform.localScale = Vector3.one * radius * 2f;
                    break;
                }
                case WorldObjectKind.Smoke:
                    UpdateSmoke(shown, item);
                    break;
                case WorldObjectKind.SnatchShot:
                    UpdateShot(shown, item, position);
                    break;
                case WorldObjectKind.PlacedZone:
                    if (!shown.Copied)
                    {
                        UpdateZoneFrame(shown, item);
                    }
                    break;
                case WorldObjectKind.PlacedTrap:
                    UpdatePlacedTrap(shown, item);
                    break;
                case WorldObjectKind.TrapBall:
                {
                    Transform ball = shown.Visual.transform;
                    ball.position = new Vector3(item.Position.X, item.Position.Y, ball.position.z);
                    bool visible = !ThiefRules.IsBlinking(item) || Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.6f;
                    shown.Visual.SetActive(visible);
                    break;
                }
            }
        }

        private float SmokeBallRadius(int owner)
        {
            return owner >= 0 && owner < _feed.CharacterCount ? _feed.GetConfig(owner).Skills.Thief.SmokeBallRadius : 0.3f;
        }

        // ---------------------------------------------------------------- 煙

        // 煙のかたまりの絵: 内側は不透明、ふちはやわらかく、ゆらいだ形で薄くなる（明るさにもむら）。白で描き、色は板ごとに掛ける。
        private static Sprite CreatePuffSprite()
        {
            const int size = PuffTextureSize;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SmokePuff",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float noise = Fbm(u * 2.3f + 5.1f, v * 2.3f + 2.7f);
                    float edge = 1f - Mathf.SmoothStep(0.55f, 1f, r + (noise - 0.5f) * 0.3f);
                    float alpha = Mathf.Clamp01(edge * (1.6f + (noise - 0.5f) * 0.4f));
                    float shade = Mathf.Lerp(0.8f, 1f, noise);
                    pixels[y * size + x] = new Color(shade, shade, shade, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        // 重ねたゆらぎ（0〜1）。
        private static float Fbm(float x, float y)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += Mathf.PerlinNoise(x, y) * amplitude;
                total += amplitude;
                x = x * 2.03f + 1.7f;
                y = y * 2.03f + 3.1f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        // 煙: 真ん中の大きなかたまり、内側の輪（5 つ）、外側の輪（10 個）。輪の中は同じ間隔に少しずらして並べ、円の中にすき間を作らない
        // （ずらし方は物の番号で決める）。
        private void CreatePuffs(Shown shown, int id)
        {
            var random = new System.Random(id * 7919 + 17);
            shown.Puffs = new SpriteRenderer[SmokePuffs];
            shown.PuffOffsets = new Vector3[SmokePuffs];
            shown.PuffSizes = new float[SmokePuffs];
            shown.PuffSpins = new float[SmokePuffs];
            shown.PuffShades = new float[SmokePuffs];
            const int innerCount = 5;
            float innerBase = (float)random.NextDouble() * Mathf.PI * 2f;
            float outerBase = (float)random.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < SmokePuffs; i++)
            {
                bool inner = i >= 1 && i <= innerCount;
                int outerCount = SmokePuffs - 1 - innerCount;
                float angle = inner
                    ? innerBase + Mathf.PI * 2f * (i - 1) / innerCount + ((float)random.NextDouble() * 2f - 1f) * 0.3f
                    : outerBase + Mathf.PI * 2f * (i - 1 - innerCount) / outerCount + ((float)random.NextDouble() * 2f - 1f) * 0.15f;
                float distance = i == 0 ? 0f : inner ? 0.3f + (float)random.NextDouble() * 0.12f : 0.74f + (float)random.NextDouble() * 0.06f;
                // 奥のかたまりほど少し後ろ（重なりの順）。
                shown.PuffOffsets[i] = new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, 0.02f * i);
                shown.PuffSizes[i] = i == 0 ? 1.55f : inner ? 1.2f + (float)random.NextDouble() * 0.15f : 1.1f + (float)random.NextDouble() * 0.15f;
                shown.PuffSpins[i] = ((float)random.NextDouble() * 2f - 1f) * 25f;
                // 上ほど明るく、下ほど少し暗い灰色。
                shown.PuffShades[i] = Mathf.Lerp(0.66f, 0.86f, (shown.PuffOffsets[i].y + 0.7f) / 1.4f) + ((float)random.NextDouble() - 0.5f) * 0.06f;
                SpriteRenderer puff = Instantiate(_puffTemplate, shown.Visual.transform);
                puff.gameObject.SetActive(true);
                puff.sprite = _puffSprite;
                puff.transform.localRotation = Quaternion.Euler(0f, 0f, (float)random.NextDouble() * 360f);
                shown.Puffs[i] = puff;
            }
        }

        // 煙の位置: z = 0 の面の円（中心 center、半径 radius）と画面で重なるように、カメラの前（_smokeDepth）へ寄せて縮める。
        // 出た瞬間に広がり、ゆっくり回って少しずつ外へふくらみ、消える前の SmokeFadeSeconds で薄くなる。
        private void UpdateSmoke(Shown shown, in WorldObject smoke)
        {
            var center = new Vector3(smoke.Position.X, smoke.Position.Y, 0f);
            float radius = smoke.End.X;
            float scale = 1f;
            Vector3 placed = new Vector3(center.x, center.y, _smokeDepth);
            if (_camera != null && !_camera.orthographic && _camera.transform.position.z < _smokeDepth)
            {
                Vector3 eye = _camera.transform.position;
                scale = (_smokeDepth - eye.z) / (0f - eye.z);
                placed = eye + (center - eye) * scale;
            }
            float age = Time.time - shown.BornAt;
            float left = (smoke.ExpireTick - _feed.CurrentTick) * SimTime.DeltaTime;
            float life = Mathf.Max(0.1f, (smoke.ExpireTick - smoke.SpawnTick) * SimTime.DeltaTime);
            float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / SmokeGrowSeconds), 3f);
            float fade = Mathf.Clamp01(left / SmokeFadeSeconds);
            float spread = 1f + 0.12f * Mathf.Clamp01(age / life) + 0.1f * (1f - fade);
            float size = radius * scale * (0.35f + 0.65f * grow);
            shown.Visual.transform.position = placed;
            for (int i = 0; i < shown.Puffs.Length; i++)
            {
                SpriteRenderer puff = shown.Puffs[i];
                float billow = 1f + 0.05f * Mathf.Sin(Time.time * 1.7f + i * 1.3f);
                Vector3 offset = shown.PuffOffsets[i];
                puff.transform.localPosition = new Vector3(offset.x * size * spread, offset.y * size * spread, offset.z);
                puff.transform.localScale = Vector3.one * size * shown.PuffSizes[i] * billow;
                puff.transform.Rotate(0f, 0f, shown.PuffSpins[i] * Time.deltaTime, Space.Self);
                float shade = shown.PuffShades[i];
                puff.color = new Color(shade, shade, shade + 0.02f, fade);
            }
        }

        private void UpdateShot(Shown shown, in WorldObject shot, Vector3 position)
        {
            Transform ball = shown.Visual.transform.GetChild(0);
            ball.position = position;
            float radius = shot.Owner >= 0 && shot.Owner < _feed.CharacterCount ? _feed.GetConfig(shot.Owner).Skills.Thief.SnatchRadius : 0.3f;
            ball.localScale = Vector3.one * radius * 2f;
            Vector3 back = new Vector3(shot.Velocity.X, shot.Velocity.Y, 0f).normalized;
            float length = Mathf.Min(ShotTrailLength, shot.ChargeMilli / 1000f);
            shown.Trail.positionCount = 2;
            shown.Trail.SetPosition(0, position);
            shown.Trail.SetPosition(1, position - back * length);
        }

        // ---------------------------------------------------------------- 置いたギミック

        // 本物の見た目を写す（見つからなければ、範囲の枠と流れる印）。
        private void CreatePlacedZone(Shown shown, in WorldObject item)
        {
            IReadOnlyList<StageZone> zones = _feed.Zones;
            StageZoneAuthoring source = _looks.ZoneSource(zones, item.Stored);
            if (source != null)
            {
                StageZone placed = ThiefRules.PlacedZone(zones[item.Stored], item);
                shown.Visual = PlacedGimmickLooks.CopyZone(source, placed, item.Variant == 1, transform);
                shown.Copied = shown.Visual != null;
            }
            if (shown.Visual == null)
            {
                shown.Visual = Root(item);
                shown.Frame = Line(shown.Visual.transform, "Frame", Color.white, 0.08f);
                shown.Frame.loop = true;
                shown.Marks = Line(shown.Visual.transform, "Marks", Color.white, 0.1f);
            }
        }

        // 本物の見た目が見つからないとき: 範囲の枠と、向きへ流れる 3 つの山形（効いていない間は薄く、予告の間は点滅）。
        private void UpdateZoneFrame(Shown shown, in WorldObject item)
        {
            IReadOnlyList<StageZone> zones = _feed.Zones;
            if (zones == null || item.Stored < 0 || item.Stored >= zones.Count)
            {
                shown.Visual.SetActive(false);
                return;
            }
            StageZone zone = ThiefRules.PlacedZone(zones[item.Stored], item);
            int tick = _feed.CurrentTick;
            int seed = _feed.Rules != null ? _feed.Rules.Seed : 0;
            bool active = zone.IsActive(tick, seed);
            float alpha = active ? 0.95f : Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.5f ? 0.6f : 0.25f;
            var color = new Color(1f, 1f, 1f, alpha);
            SetColor(shown.Frame, color);
            SetColor(shown.Marks, color);
            float z = Z + 0.1f;
            shown.Frame.positionCount = 4;
            shown.Frame.SetPosition(0, new Vector3(zone.MinX, zone.MinY, z));
            shown.Frame.SetPosition(1, new Vector3(zone.MaxX, zone.MinY, z));
            shown.Frame.SetPosition(2, new Vector3(zone.MaxX, zone.MaxY, z));
            shown.Frame.SetPosition(3, new Vector3(zone.MinX, zone.MaxY, z));
            NVector2 direction = zone.Direction.LengthSquared() > 1e-4f ? NVector2.Normalize(zone.Direction) : new NVector2(0f, 1f);
            var along = new Vector3(direction.X, direction.Y, 0f);
            var side = new Vector3(-direction.Y, direction.X, 0f);
            var center = new Vector3((zone.MinX + zone.MaxX) * 0.5f, (zone.MinY + zone.MaxY) * 0.5f, z);
            float reach = Mathf.Abs(along.x) * (zone.MaxX - zone.MinX) * 0.5f + Mathf.Abs(along.y) * (zone.MaxY - zone.MinY) * 0.5f;
            const float width = 0.5f;
            float flow = Mathf.Repeat(Time.time * (active ? 1.2f : 0.3f), 1f);
            const int chevrons = 3;
            shown.Marks.positionCount = chevrons * 3;
            for (int i = 0; i < chevrons; i++)
            {
                float t = Mathf.Repeat((i + flow) / chevrons, 1f) * 2f - 1f;
                Vector3 tip = center + along * (t * reach * 0.8f + width * 0.5f);
                Vector3 tail = tip - along * width;
                shown.Marks.SetPosition(i * 3, tail + side * width);
                shown.Marks.SetPosition(i * 3 + 1, tip);
                shown.Marks.SetPosition(i * 3 + 2, tail - side * width);
            }
        }

        // ---------------------------------------------------------------- 置いた仕掛け

        // 本物の見た目を写し、発射口が置いた位置に来るように動かす（左右の反転は写すときに当てる）。
        private void CreatePlacedTrap(Shown shown, in WorldObject item)
        {
            IReadOnlyList<StageTrap> traps = _feed.Traps;
            StageTrapAuthoring source = _looks.TrapSource(traps, item.Stored);
            if (source == null)
            {
                Debug.LogWarning($"{name}: 置いた仕掛け（仕掛け {item.Stored}）の見た目が見つかりません", this);
                return;
            }
            bool mirrored = item.Variant == 1;
            shown.Visual = PlacedGimmickLooks.CopyTrap(source, mirrored, transform);
            shown.Copied = true;
            Transform root = shown.Visual.transform;
            StageTrap trap = traps[item.Stored];
            Vector3 origin = source.transform.position;
            var toMuzzle = new Vector3(trap.Muzzle.X - origin.x, trap.Muzzle.Y - origin.y, 0f);
            if (mirrored)
            {
                toMuzzle.x = -toMuzzle.x;
            }
            root.position = new Vector3(item.Position.X, item.Position.Y, origin.z) - toMuzzle;
            shown.Beam = root.Find("Beam");
            shown.BeamRenderer = shown.Beam != null ? shown.Beam.GetComponent<Renderer>() : null;
            shown.BeamSize = shown.Beam != null ? (Vector2)shown.Beam.localScale : Vector2.one;
            Transform lamp = root.Find("Lamp");
            shown.Lamp = lamp != null ? lamp.GetComponent<Renderer>() : null;
            shown.Missile = root.Find("Missile");
            shown.Launcher = root.Find("Launcher");
            Transform reload = root.Find("ReloadGauge");
            shown.Reload = reload != null ? reload.GetComponent<RingGaugeView>() : null;
            Transform ball = root.Find("Ball");
            if (ball != null)
            {
                // ピッチャーマシンのボールは、打ち出したボール（TrapBall）ごとに写す。
                ball.gameObject.SetActive(false);
            }
            if (shown.Missile != null)
            {
                shown.Missile.gameObject.SetActive(false);
            }
            if (trap.Kind == StageTrapKind.Tripwire)
            {
                var overlay = new GameObject("Warning");
                overlay.transform.SetParent(root, false);
                shown.Overlay = overlay.AddComponent<WarningOverlay>();
                shown.Overlay.Initialize(_lineTemplate.sharedMaterial, -0.7f);
            }
        }

        // 置いた赤外線: ゆっくり明滅し、触れられたら予告（斜線と警告の記号、ランプの点滅、警報）、撃ったらミサイル、当たったら爆発の音、
        // 待ちの間は赤外線を消して RELOAD のゲージ（StageTrapView と同じ見せ方）。
        private void UpdatePlacedTrap(Shown shown, in WorldObject item)
        {
            IReadOnlyList<StageTrap> traps = _feed.Traps;
            if (traps == null || item.Stored < 0 || item.Stored >= traps.Count)
            {
                return;
            }
            StageTrap trap = traps[item.Stored];
            if (trap.Kind != StageTrapKind.Tripwire)
            {
                return;
            }
            int tick = _feed.CurrentTick;
            bool flying = ThiefRules.TryGetPlacedMissile(trap, item, tick, out bool arming, out NVector2 missile);
            flying &= !arming;
            bool waiting = item.ChargeMilli == 0;
            bool reloading = !waiting && !arming && !flying;
            ThiefRules.PlacedBeam(trap, item, out NVector2 min, out NVector2 max);
            var beamCenter = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, 0f);
            if (shown.Beam != null)
            {
                shown.Beam.position = new Vector3(beamCenter.x, beamCenter.y, shown.Beam.position.z);
                float speed = arming ? 8f : 1.2f;
                shown.BeamRenderer.enabled = !reloading && Mathf.Repeat(Time.time * speed, 1f) < (arming ? 0.5f : 0.92f);
            }
            if (shown.Lamp != null)
            {
                shown.Lamp.enabled = !arming || Mathf.Repeat(Time.time * BlinkPerSecond, 1f) < 0.5f;
            }
            if (shown.Missile != null)
            {
                shown.Missile.gameObject.SetActive(flying);
                if (flying)
                {
                    NVector2 aim = ThiefRules.PlacedTrapAim(trap, item);
                    shown.Missile.position = new Vector3(missile.X, missile.Y, shown.Missile.position.z);
                    shown.Missile.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.Y, aim.X) * Mathf.Rad2Deg);
                }
            }
            if (shown.Reload != null)
            {
                int start = (int)item.ChargeMilli + trap.ArmTicks + trap.FlightTicks;
                float leftFraction = trap.CooldownTicks > 0 ? Mathf.Clamp01((start + trap.CooldownTicks - tick) / (float)trap.CooldownTicks) : 0f;
                if (reloading && leftFraction > 0f)
                {
                    shown.Reload.Show(shown.Launcher != null ? shown.Launcher.position : shown.Visual.transform.position, leftFraction);
                }
                else if (shown.Reload.IsShowing)
                {
                    shown.Reload.Hide();
                }
            }
            if (shown.Overlay != null)
            {
                if (arming)
                {
                    _areas.Clear();
                    _icons.Clear();
                    float halfHeight = Mathf.Max(0.6f, (max.Y - min.Y) * 0.5f);
                    _areas.Add(new Rect(min.X, beamCenter.y - halfHeight, max.X - min.X, halfHeight * 2f));
                    _icons.Add(new Vector2(beamCenter.x, beamCenter.y + 1.6f));
                    shown.Overlay.Show(_areas, _icons, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f)));
                }
                else
                {
                    shown.Overlay.Hide();
                }
            }
            if (_primed && _audio != null)
            {
                if (arming && !shown.Arming)
                {
                    _audio.Play(SoundId.Alarm, 0.8f);
                }
                if (flying && !shown.Flying)
                {
                    _audio.Play(SoundId.MissileFire, 0.9f);
                }
                if (shown.Flying && !flying && item.HitMask != 0)
                {
                    _audio.Play(SoundId.MissileExplode, 0.9f);
                }
            }
            shown.Arming = arming;
            shown.Flying = flying;
        }

        private void PlaySpawn(WorldObjectKind kind)
        {
            if (!_primed || _audio == null)
            {
                return;
            }
            switch (kind)
            {
                case WorldObjectKind.Smoke:
                    _audio.Play(SoundId.Smoke, 0.9f, 0.05f);
                    break;
                case WorldObjectKind.SnatchShot:
                    _audio.Play(SoundId.Snatch, 0.8f, 0.05f);
                    break;
                case WorldObjectKind.TrapBall:
                    _audio.Play(SoundId.Pitch, 0.8f, 0.04f);
                    break;
                case WorldObjectKind.PlacedZone:
                case WorldObjectKind.PlacedTrap:
                    _audio.Play(SoundId.SafePlace, 0.7f, 0.04f);
                    break;
            }
        }

        private int CountOf(WorldObjectKind kind)
        {
            int count = 0;
            foreach (Shown shown in _shown.Values)
            {
                count += shown.Kind == kind ? 1 : 0;
            }
            return count;
        }

        // ---------------------------------------------------------------- キャラクター

        private void RenderCharacters()
        {
            int count = _feed.CharacterCount;
            while (_characters.Count < count)
            {
                _characters.Add(CreateCharacter(_characters.Count));
            }
            for (int i = 0; i < _characters.Count; i++)
            {
                Character character = _characters[i];
                if (i >= count)
                {
                    character.Shadow.SetActive(false);
                    character.Steals = -1;
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                Vector3 center = new Vector3(state.Position.X, state.Position.Y, 0f);

                bool invisible = state.Combat.InvisibleTicksLeft > 0 && !state.Combat.IsWaitingToRespawn;
                character.Shadow.SetActive(invisible);
                if (invisible)
                {
                    character.Shadow.transform.position = center + new Vector3(0f, -config.Movement.HalfHeight + 0.03f, 0f);
                    character.Shadow.transform.localScale = new Vector3(config.Movement.HalfWidth * 2.6f, 0.05f, config.Movement.HalfWidth * 1.6f);
                }

                if (character.Steals >= 0 && state.Combat.StealCount > character.Steals)
                {
                    character.Steal.Play(center + Vector3.up * (config.Movement.HalfHeight + 0.4f));
                    StealBursts++;
                }
                character.Steals = state.Combat.StealCount;
            }
        }

        private Character CreateCharacter(int index)
        {
            var character = new Character
            {
                Shadow = Child(_shadowTemplate, transform),
                Steal = Instantiate(_stealTemplate, transform),
            };
            character.Shadow.name = $"InvisibleShadowP{index + 1}";
            character.Shadow.SetActive(false);
            character.Steal.name = $"StealBurstP{index + 1}";
            character.Steal.gameObject.SetActive(true);
            return character;
        }

        // ---------------------------------------------------------------- 部品

        private static GameObject Child(GameObject template, Transform parent)
        {
            GameObject copy = Instantiate(template, parent);
            copy.SetActive(true);
            return copy;
        }

        private LineRenderer Line(Transform parent, string lineName, Color color, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, parent);
            line.name = lineName;
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = width;
            SetColor(line, color);
            return line;
        }

        private static void SetColor(LineRenderer line, Color color)
        {
            line.startColor = color;
            line.endColor = color;
        }

        /// <summary>透明の影の不透明さ（シーンのビルダーが材質を作るのに使う）。</summary>
        public static float ShadowOpacity => ShadowAlpha;

        /// <summary>奪った瞬間の光の色（シーンのビルダーが使う）。</summary>
        public static Color StealBurstColor => StealColor;
    }
}
