using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Items;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// アイテムの見た目（ユーザーの要望、2026-11-05。docs/20_item_plan.md）。状態を読むだけで、書き換えない。見た目の部品は起動時に作る。
    /// - 落ちているアイテム: アイテムの絵の板と、主な色の淡い光（ゆっくり上下に揺れる）。
    /// - エネルギーショット: 青い炎の光弾（進む向きに回す）。砂岩ボール: 砂岩のボール（転がる向きに回る）。
    /// - 櫓落とし: 振りの弧（縦・横・斜め）、突進の残像の線、一閃の線（白から臙脂色へ消える）。
    /// - イヤーボーン: 円の縁まで広がる風の輪。イカロスウイング: 背中の両側の羽（羽ばたく。残り 1 秒で点滅）。
    /// - 詠唱: キャラクターの下に棒のゲージ（以降、詠唱するものはすべてこれを出す。ユーザーの指示、2026-11-05）。
    /// - 音: 拾った・使った・捨てた・振った・一閃・風。
    /// 追加のアイテム（2026-11-05）: グラップリングボム（刺さると赤く点滅、消えたら爆発の光）、折りたたみの足場、携帯トランポリン、
    /// 身代わり人形、瓶詰めの雷雲（雷の線）、チェーンフック（持ち主からの鎖）、凍らせた壁、反射板の盾、シャボンのバリア、砂時計で遅い人の
    /// 紫の光、大きな磁石の赤い光、帰還のリモコンの印、ポケットロケットの炎。
    /// 落ちているアイテム: 出た瞬間に強い光を放ってフェードインし、着地して消える 5 秒前から点滅して円形ゲージと残りの秒数を出し、消えると
    /// 光の粒になる（ユーザーの要望、2026-11-05）。
    /// </summary>
    public sealed class ItemEffectsView : MonoBehaviour
    {
        private const float Z = -0.35f;
        private const float DropSize = 1f;
        private const float CastBarWidth = 90f;
        private const float CastBarHeight = 10f;

        [Tooltip("状態を読む相手（SimulationRunner・LocalMatchRunner・OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("詠唱のゲージを画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("線の見本（隠しておく）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;

        // 値は全員で同じ（ItemTuning の初めの値。SimWorld.ItemTuning と同じ）。
        private static readonly ItemTuning Tuning = new ItemTuning();

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private readonly List<Character> _characters = new List<Character>();
        private readonly List<Effect> _effects = new List<Effect>();
        private ISimulationFeed _feed;
        private Sprite _glow;
        private Sprite _ballSprite;
        private readonly Dictionary<ItemId, Sprite> _sprites = new Dictionary<ItemId, Sprite>();
        private GUIStyle _castStyle;
        private GUIStyle _expiryStyle;
        private Sprite _square;
        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Dictionary<int, SlantedRingGauge> _expiryGauges = new Dictionary<int, SlantedRingGauge>();
        private bool _primed;

        // 光の粒（消えた落ちているアイテム、爆発）。
        private sealed class Particle
        {
            public SpriteRenderer Renderer;
            public Vector3 Velocity;
            public float Start;
            public float Duration;
            public Color Color;
            public float Size;
        }

        /// <summary>出た瞬間の光と、消える前の点滅を出す長さ（秒）と残り（ティック）。</summary>
        private const float SpawnFlashSeconds = 0.45f;
        private const int ExpiryWarningTicks = 300;

        private sealed class Shown
        {
            public WorldObjectKind Kind;
            public GameObject Root;
            public Transform Icon;
            public SpriteRenderer Glow;
            public float Spin;
            public float CreatedAt;
            public int Remaining = int.MaxValue;
            public Vector3 LastPosition;
            public int Owner;
            public int Strikes;
            public LineRenderer Chain;
        }

        private sealed class Character
        {
            public int Pickups;
            public int Uses;
            public int Drops;
            public int Bursts;
            public int Flashes;
            public int Reflects;
            public int Pops;
            public int Teleports;
            public int Seals;
            public int LastSlash = -1;
            public GameObject Wings;
            public SpriteRenderer LeftWing;
            public SpriteRenderer RightWing;
            public SpriteRenderer Shield;
            public SpriteRenderer Bubble;
            public SpriteRenderer Aura;
            public SpriteRenderer Mark;
        }

        // しばらく出して消える線（振りの弧・一閃・風の輪）。
        private sealed class Effect
        {
            public LineRenderer Line;
            public float Start;
            public float Duration;
            public Color From;
            public Color To;
            public float Width;
            // 風の輪: 中心と最後の半径（広がる）。0 なら広がらない。
            public Vector3 Center;
            public float Radius;
        }

        /// <summary>出している落ちているアイテムの数（テスト用）。</summary>
        public int DroppedCount => Count(WorldObjectKind.DroppedItem);

        /// <summary>出している光弾と砂岩ボールの数（テスト用）。</summary>
        public int ShotCount => Count(WorldObjectKind.ItemShot);
        public int BallCount => Count(WorldObjectKind.SandBall);

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _lineTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、線の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            _glow = SoftSprites.CreateGlow("ItemGlow");
            _square = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);
            _square.name = "ItemSquare";
        }

        private void OnDestroy()
        {
            SoftSprites.Release(_glow);
            if (_square != null)
            {
                Destroy(_square);
            }
            foreach (SlantedRingGauge gauge in _expiryGauges.Values)
            {
                gauge.Destroy();
            }
            foreach (Sprite sprite in _sprites.Values)
            {
                if (sprite != null)
                {
                    Destroy(sprite);
                }
            }
            if (_ballSprite != null)
            {
                Destroy(_ballSprite.texture);
                Destroy(_ballSprite);
            }
        }

        private int Count(WorldObjectKind kind)
        {
            int count = 0;
            foreach (Shown shown in _shown.Values)
            {
                count += shown.Kind == kind ? 1 : 0;
            }
            return count;
        }

        private void LateUpdate()
        {
            RenderObjects();
            RenderCharacters();
            RenderEffects();
            RenderParticles();
            _primed = true;
        }

        // ---------------------------------------------------------------- 物

        private void RenderObjects()
        {
            _present.Clear();
            WorldObjects objects = _feed.Objects;
            float time = Time.time;
            for (int i = 0; i < objects.Count; i++)
            {
                WorldObject item = objects[i];
                if (!IsItemObject(item.Kind))
                {
                    continue;
                }
                _present.Add(item.Id);
                if (!_shown.TryGetValue(item.Id, out Shown shown))
                {
                    shown = Create(item);
                    shown.CreatedAt = _primed ? time : -10f;
                    _shown[item.Id] = shown;
                }
                var position = new Vector3(item.Position.X, item.Position.Y, Z);
                shown.LastPosition = position;
                shown.Owner = item.Owner;
                switch (item.Kind)
                {
                    case WorldObjectKind.DroppedItem:
                        RenderDropped(item, shown, position, time);
                        break;
                    case WorldObjectKind.ItemBomb:
                        shown.Root.transform.position = position;
                        bool armed = item.Stored != 0;
                        shown.Glow.color = armed && Mathf.Repeat(time * 8f, 1f) < 0.5f ? new Color(1f, 0.2f, 0.1f, 0.85f) : new Color(1f, 0.6f, 0.2f, 0.35f);
                        break;
                    case WorldObjectKind.ItemPlatform:
                    case WorldObjectKind.Trampoline:
                    case WorldObjectKind.FrozenWall:
                        shown.Root.transform.position = position;
                        shown.Icon.localScale = new Vector3(item.End.X * 2f, item.End.Y * 2f, 1f);
                        float fade = item.ExpireTick - _feed.CurrentTick < 60 && Mathf.Repeat(time * 8f, 1f) < 0.5f ? 0.35f : 1f;
                        var tint = shown.Icon.GetComponent<SpriteRenderer>();
                        tint.color = WithAlpha(tint.color, (item.Kind == WorldObjectKind.FrozenWall ? 0.55f : 0.95f) * fade);
                        if (item.Kind == WorldObjectKind.Trampoline && item.Damage > shown.Strikes)
                        {
                            shown.Strikes = item.Damage;
                            shown.Glow.transform.localScale = new Vector3(item.End.X * 4f, item.End.X * 2f, 1f);
                        }
                        break;
                    case WorldObjectKind.Decoy:
                        shown.Root.transform.position = position;
                        shown.Icon.localScale = new Vector3(item.End.X * 2.4f * (item.Stored < 0 ? -1f : 1f), item.End.Y * 2.2f, 1f);
                        shown.Icon.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, item.Damage > 0 ? 0.6f : 0.9f);
                        break;
                    case WorldObjectKind.ThunderCloud:
                        shown.Root.transform.position = position;
                        shown.Icon.localScale = new Vector3(item.End.X * 2.6f, 1.4f, 1f);
                        if (item.Damage > shown.Strikes)
                        {
                            shown.Strikes = item.Damage;
                            AddBolt(position, item.End.Y);
                            Play(SoundId.Thunder);
                        }
                        break;
                    case WorldObjectKind.ChainHook:
                        shown.Root.transform.position = position;
                        shown.Icon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(item.Velocity.Y, item.Velocity.X) * Mathf.Rad2Deg - 90f);
                        if (shown.Chain != null && item.Owner >= 0 && item.Owner < _feed.CharacterCount)
                        {
                            CharacterState owner = _feed.GetState(item.Owner);
                            shown.Chain.SetPosition(0, new Vector3(owner.Position.X, owner.Position.Y, Z));
                            shown.Chain.SetPosition(1, position);
                        }
                        break;
                    case WorldObjectKind.ItemShot:
                        shown.Root.transform.position = position;
                        shown.Icon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(item.Velocity.Y, item.Velocity.X) * Mathf.Rad2Deg - 90f);
                        shown.Glow.color = new Color(0.4f, 0.65f, 1f, 0.6f + 0.2f * Mathf.Sin(time * 30f));
                        break;
                    case WorldObjectKind.SandBall:
                        shown.Root.transform.position = position;
                        float radius = item.End.X > 0f ? item.End.X : Tuning.BallRadius;
                        shown.Icon.localScale = Vector3.one * radius * 2f;
                        shown.Spin -= item.Velocity.X / Mathf.Max(0.1f, radius) * Mathf.Rad2Deg * Time.deltaTime;
                        shown.Icon.localRotation = Quaternion.Euler(0f, 0f, shown.Spin);
                        float speed = item.Velocity.Length();
                        shown.Glow.color = new Color(1f, 0.85f, 0.5f, speed >= Tuning.BallMinHarmSpeed ? 0.5f : 0f);
                        break;
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
                Shown gone = _shown[id];
                if (gone.Kind == WorldObjectKind.DroppedItem && gone.Remaining <= 3)
                {
                    // 拾われずに時間切れで消えた: 光の粒になって消える。
                    Burst(gone.LastPosition, gone.Glow.color, 14, 2.4f, 0.7f, 0.18f);
                }
                else if (gone.Kind == WorldObjectKind.ItemBomb)
                {
                    // 爆発（オレンジの光と粒）。
                    Burst(gone.LastPosition, new Color(1f, 0.55f, 0.15f, 1f), 18, 7f, 0.5f, 0.35f);
                    AddRing(gone.LastPosition, Tuning.BombBlastRadius, new Color(1f, 0.8f, 0.4f, 0.95f), new Color(1f, 0.3f, 0.1f, 0f));
                    Play(SoundId.MissileExplode);
                }
                if (_expiryGauges.TryGetValue(id, out SlantedRingGauge gauge))
                {
                    gauge.Destroy();
                    _expiryGauges.Remove(id);
                }
                if (gone.Chain != null)
                {
                    Destroy(gone.Chain.gameObject);
                }
                Destroy(gone.Root);
                _shown.Remove(id);
            }
        }

        private static bool IsItemObject(WorldObjectKind kind)
        {
            switch (kind)
            {
                case WorldObjectKind.DroppedItem:
                case WorldObjectKind.ItemShot:
                case WorldObjectKind.SandBall:
                case WorldObjectKind.ItemBomb:
                case WorldObjectKind.ItemPlatform:
                case WorldObjectKind.Trampoline:
                case WorldObjectKind.Decoy:
                case WorldObjectKind.ThunderCloud:
                case WorldObjectKind.ChainHook:
                case WorldObjectKind.FrozenWall:
                    return true;
                default:
                    return false;
            }
        }

        // 落ちているアイテム: ゆっくり上下に揺れ、光が脈打つ。出た瞬間は強く光ってフェードインし、消える 5 秒前から点滅して円形ゲージを出す。
        private void RenderDropped(in WorldObject item, Shown shown, Vector3 position, float time)
        {
            float bob = item.Stored == 1 ? 0.12f + 0.08f * Mathf.Sin(time * 3f + item.Id) : 0f;
            shown.Root.transform.position = position + new Vector3(0f, bob, 0f);
            Color accent = ItemIcons.Accent((ItemId)item.Variant);
            float age = time - shown.CreatedAt;
            var icon = shown.Icon.GetComponent<SpriteRenderer>();
            if (age < SpawnFlashSeconds)
            {
                float t = age / SpawnFlashSeconds;
                shown.Glow.transform.localScale = Vector3.one * DropSize * Mathf.Lerp(6f, 2.2f, t);
                shown.Glow.color = Color.Lerp(new Color(1f, 1f, 1f, 1f), WithAlpha(accent, 0.55f), t);
                icon.color = new Color(1f, 1f, 1f, t);
            }
            else
            {
                shown.Glow.transform.localScale = Vector3.one * DropSize * 2.2f;
                shown.Glow.color = WithAlpha(accent, 0.45f + 0.2f * Mathf.Sin(time * 5f + item.Id));
                icon.color = Color.white;
            }
            int remaining = item.ExpireTick == int.MaxValue ? int.MaxValue : item.ExpireTick - _feed.CurrentTick;
            shown.Remaining = remaining;
            if (remaining <= ExpiryWarningTicks)
            {
                bool off = Mathf.Repeat(time * 6f, 1f) < 0.35f;
                icon.color = new Color(1f, 1f, 1f, off ? 0.25f : 1f);
                if (!_expiryGauges.TryGetValue(item.Id, out SlantedRingGauge gauge))
                {
                    gauge = new SlantedRingGauge(this, $"ItemExpiry{item.Id}", _lineTemplate.sharedMaterial, 20);
                    _expiryGauges[item.Id] = gauge;
                }
                gauge.Draw(shown.Root.transform.position, DropSize * 0.85f, 0.08f, Mathf.Clamp01(remaining / (float)ExpiryWarningTicks), accent, Z - 0.05f);
            }
        }

        // 光の粒を count 個、center から外へ飛ばす（speed m/s、duration 秒、大きさ size m）。
        private void Burst(Vector3 center, Color color, int count, float speed, float duration, float size)
        {
            for (int k = 0; k < count; k++)
            {
                float angle = (k + Random.value * 0.5f) * Mathf.PI * 2f / count;
                var go = new GameObject("ItemParticle");
                go.transform.SetParent(transform, false);
                go.transform.position = center;
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = _glow;
                _particles.Add(new Particle
                {
                    Renderer = renderer, Velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed * (0.6f + 0.4f * Random.value),
                    Start = Time.time, Duration = duration, Color = WithAlpha(color, 1f), Size = size,
                });
            }
        }

        private void RenderParticles()
        {
            float now = Time.time;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                float t = (now - p.Start) / p.Duration;
                if (t >= 1f)
                {
                    Destroy(p.Renderer.gameObject);
                    _particles.RemoveAt(i);
                    continue;
                }
                p.Renderer.transform.position += p.Velocity * Time.deltaTime;
                p.Renderer.transform.localScale = Vector3.one * p.Size * (1f - 0.6f * t);
                p.Renderer.color = WithAlpha(Color.Lerp(Color.white, p.Color, t), 1f - t);
            }
        }

        private Shown Create(in WorldObject item)
        {
            var root = new GameObject($"Item{item.Kind}{item.Id}");
            root.transform.SetParent(transform, false);
            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(root.transform, false);
            SpriteRenderer glow = glowObject.AddComponent<SpriteRenderer>();
            glow.sprite = _glow;
            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(root.transform, false);
            iconObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            SpriteRenderer icon = iconObject.AddComponent<SpriteRenderer>();
            switch (item.Kind)
            {
                case WorldObjectKind.DroppedItem:
                    icon.sprite = SpriteOf((ItemId)item.Variant);
                    iconObject.transform.localScale = Vector3.one * DropSize;
                    glowObject.transform.localScale = Vector3.one * DropSize * 2.2f;
                    break;
                case WorldObjectKind.ItemShot:
                    icon.sprite = SpriteOf(ItemId.EnergyShot);
                    iconObject.transform.localScale = Vector3.one * Tuning.ShotRadius * 3f;
                    glowObject.transform.localScale = Vector3.one * Tuning.ShotRadius * 5f;
                    break;
                case WorldObjectKind.ItemBomb:
                    icon.sprite = SpriteOf(ItemId.GrappleBomb);
                    iconObject.transform.localScale = Vector3.one * Tuning.BombRadius * 3f;
                    glowObject.transform.localScale = Vector3.one * Tuning.BombRadius * 5f;
                    break;
                case WorldObjectKind.ItemPlatform:
                    icon.sprite = _square;
                    icon.color = new Color(1f, 0.85f, 0.25f, 0.95f);
                    glow.color = new Color(1f, 0.9f, 0.4f, 0.35f);
                    glowObject.transform.localScale = new Vector3(item.End.X * 3f, item.End.Y * 6f, 1f);
                    break;
                case WorldObjectKind.Trampoline:
                    icon.sprite = _square;
                    icon.color = new Color(0.3f, 0.75f, 1f, 0.95f);
                    glow.color = new Color(0.4f, 0.85f, 1f, 0.4f);
                    glowObject.transform.localScale = new Vector3(item.End.X * 3f, item.End.X * 1.5f, 1f);
                    break;
                case WorldObjectKind.FrozenWall:
                    icon.sprite = _square;
                    icon.color = new Color(0.6f, 0.9f, 1f, 0.55f);
                    glow.color = new Color(0.6f, 0.95f, 1f, 0.35f);
                    glowObject.transform.localScale = new Vector3(item.End.X * 6f, item.End.Y * 2.4f, 1f);
                    break;
                case WorldObjectKind.Decoy:
                    icon.sprite = SpriteOf(ItemId.DecoyDoll);
                    glow.color = new Color(1f, 1f, 1f, 0f);
                    break;
                case WorldObjectKind.ThunderCloud:
                    icon.sprite = _glow;
                    icon.color = new Color(0.2f, 0.2f, 0.28f, 0.9f);
                    glow.color = new Color(1f, 0.95f, 0.4f, 0.25f);
                    glowObject.transform.localScale = new Vector3(item.End.X * 3f, 2f, 1f);
                    break;
                case WorldObjectKind.ChainHook:
                {
                    icon.sprite = SpriteOf(ItemId.ChainHook);
                    iconObject.transform.localScale = Vector3.one * 0.9f;
                    glow.color = new Color(0.8f, 0.8f, 0.85f, 0.3f);
                    glowObject.transform.localScale = Vector3.one * 1.2f;
                    LineRenderer chain = NewLine(2, 0.08f);
                    chain.startColor = new Color(0.75f, 0.75f, 0.8f, 1f);
                    chain.endColor = new Color(0.75f, 0.75f, 0.8f, 1f);
                    var shownHook = new Shown { Kind = item.Kind, Root = root, Icon = iconObject.transform, Glow = glow, Chain = chain };
                    return shownHook;
                }
                default:
                    icon.sprite = BallSprite();
                    glowObject.transform.localScale = Vector3.one * Tuning.BallRadius * 4f;
                    break;
            }
            return new Shown { Kind = item.Kind, Root = root, Icon = iconObject.transform, Glow = glow };
        }

        private Sprite SpriteOf(ItemId id)
        {
            if (_sprites.TryGetValue(id, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }
            Texture2D texture = ItemIcons.Get(id);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            sprite.name = $"ItemSprite{id}";
            _sprites[id] = sprite;
            return sprite;
        }

        // 砂岩ボール（丸。五角形の模様のサッカーボール）。
        private Sprite BallSprite()
        {
            if (_ballSprite != null)
            {
                return _ballSprite;
            }
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SandBall", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            var sand = new Color(0.86f, 0.72f, 0.48f, 1f);
            var dark = new Color(0.55f, 0.4f, 0.24f, 1f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    Color c = Color.clear;
                    if (r < 0.48f)
                    {
                        c = r > 0.45f ? dark : sand;
                        float angle = Mathf.Atan2(v, u);
                        if (r < 0.12f || (r > 0.28f && r < 0.4f && Mathf.Cos(5f * angle) > 0.75f))
                        {
                            c = dark;
                        }
                    }
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _ballSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            return _ballSprite;
        }

        // ---------------------------------------------------------------- キャラクター

        private void RenderCharacters()
        {
            int count = _feed.CharacterCount;
            while (_characters.Count < count)
            {
                _characters.Add(CreateCharacter(_characters.Count));
            }
            float time = Time.time;
            for (int i = 0; i < _characters.Count; i++)
            {
                Character view = _characters[i];
                if (i >= count)
                {
                    view.Wings.SetActive(false);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                ItemBag bag = state.Items;
                var center = new Vector3(state.Position.X, state.Position.Y, Z);
                if (_primed)
                {
                    if (bag.PickupCount > view.Pickups)
                    {
                        Play(SoundId.ItemPickup);
                    }
                    if (bag.UseCount > view.Uses)
                    {
                        Play(SoundId.ItemUse);
                    }
                    if (bag.DropCount > view.Drops)
                    {
                        Play(SoundId.ItemDiscard);
                    }
                    if (bag.BurstCount > view.Bursts)
                    {
                        Play(SoundId.Gust);
                        AddRing(center);
                    }
                    if (bag.FlashCount > view.Flashes)
                    {
                        Play(SoundId.Zap);
                        AddFlash(bag);
                    }
                    if (bag.ReflectCount > view.Reflects)
                    {
                        Play(SoundId.ShieldBlock);
                        Burst(center + new Vector3(state.Facing * Tuning.ReflectorReach, 0f, 0f), new Color(0.85f, 0.9f, 1f, 1f), 8, 4f, 0.3f, 0.2f);
                    }
                    if (bag.BarrierPopCount > view.Pops)
                    {
                        Play(SoundId.Splash);
                        Burst(center, new Color(0.7f, 0.9f, 1f, 1f), 16, 4f, 0.45f, 0.25f);
                    }
                    if (bag.TeleportCount > view.Teleports)
                    {
                        Play(SoundId.Warp);
                        Burst(center, new Color(0.6f, 1f, 0.8f, 1f), 12, 3f, 0.4f, 0.25f);
                    }
                    if (bag.SealCount > view.Seals)
                    {
                        Play(SoundId.Bank);
                        Burst(center, new Color(1f, 0.3f, 0.25f, 1f), 12, 3f, 0.5f, 0.25f);
                    }
                }
                view.Pickups = bag.PickupCount;
                view.Uses = bag.UseCount;
                view.Drops = bag.DropCount;
                view.Bursts = bag.BurstCount;
                view.Flashes = bag.FlashCount;
                view.Reflects = bag.ReflectCount;
                view.Pops = bag.BarrierPopCount;
                view.Teleports = bag.TeleportCount;
                view.Seals = bag.SealCount;
                RenderStatuses(view, state, center, time);

                // 櫓落としの振り（振りが始まるティックごとに弧を出す）。
                if (bag.Act == (byte)ItemId.Yaguraotoshi && bag.ActTicks < Tuning.SwordSlashesTicks)
                {
                    int slash = bag.ActTicks / Tuning.SwordSlashTicks;
                    if (slash != view.LastSlash)
                    {
                        view.LastSlash = slash;
                        AddSlash(center, new Vector2(bag.ActAim.X, bag.ActAim.Y), slash);
                        Play(SoundId.ItemSlash);
                    }
                }
                else
                {
                    view.LastSlash = -1;
                }

                // イカロスウイング: 飛んでいる間、背中の両側に羽（羽ばたき。点滅はしない。ユーザーの指示、2026-11-05）。
                bool flying = ItemRules.Flying(state, Tuning) && bag.Act == (byte)ItemId.IcarusWing;
                view.Wings.SetActive(flying);
                if (flying)
                {
                    float flap = 0.75f + 0.25f * Mathf.Sin(time * 22f);
                    view.Wings.transform.position = center + new Vector3(0f, 0.35f, 0.05f);
                    view.LeftWing.transform.localScale = new Vector3(-1.2f, 1.2f * flap, 1f);
                    view.RightWing.transform.localScale = new Vector3(1.2f, 1.2f * flap, 1f);
                    view.LeftWing.color = Color.white;
                    view.RightWing.color = Color.white;
                }
            }
        }

        private Character CreateCharacter(int index)
        {
            var wings = new GameObject($"Wings{index + 1}");
            wings.transform.SetParent(transform, false);
            SpriteRenderer Wing(string wingName, float x)
            {
                var go = new GameObject(wingName);
                go.transform.SetParent(wings.transform, false);
                go.transform.localPosition = new Vector3(x, 0f, 0f);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = SpriteOf(ItemId.IcarusWing);
                return renderer;
            }
            var view = new Character { Wings = wings, LeftWing = Wing("Left", -0.55f), RightWing = Wing("Right", 0.55f) };
            wings.SetActive(false);
            SpriteRenderer Status(string statusName, Sprite sprite)
            {
                var go = new GameObject($"{statusName}{index + 1}");
                go.transform.SetParent(transform, false);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.enabled = false;
                return renderer;
            }
            view.Shield = Status("Reflector", SpriteOf(ItemId.Reflector));
            view.Bubble = Status("Bubble", _glow);
            view.Aura = Status("Aura", _glow);
            view.Mark = Status("Mark", SpriteOf(ItemId.ReturnRemote));
            return view;
        }

        // 状態の見た目（反射板の盾、シャボンのバリア、遅い人の紫・磁石の赤・ロケットの炎の光、帰還のリモコンの印）。
        private void RenderStatuses(Character view, in CharacterState state, Vector3 center, float time)
        {
            ItemBag bag = state.Items;
            bool active = !state.Combat.IsWaitingToRespawn;
            view.Shield.enabled = active && bag.ReflectTicks > 0;
            if (view.Shield.enabled)
            {
                view.Shield.transform.position = center + new Vector3(state.Facing * Tuning.ReflectorReach, 0f, -0.1f);
                view.Shield.transform.localScale = new Vector3(1.4f, 2.2f, 1f);
                view.Shield.color = new Color(1f, 1f, 1f, 0.7f + 0.3f * Mathf.Sin(time * 20f));
            }
            view.Bubble.enabled = active && bag.BarrierTicks > 0;
            if (view.Bubble.enabled)
            {
                view.Bubble.transform.position = center + new Vector3(0f, 0f, -0.12f);
                view.Bubble.transform.localScale = Vector3.one * (3.2f + 0.15f * Mathf.Sin(time * 4f));
                view.Bubble.color = Color.HSVToRGB(Mathf.Repeat(time * 0.4f, 1f), 0.35f, 1f) * new Color(1f, 1f, 1f, 0.45f);
            }
            Color aura = Color.clear;
            if (bag.SlowTicks > 0)
            {
                aura = new Color(0.65f, 0.4f, 1f, 0.55f);
            }
            else if (bag.MagnetTicks > 0)
            {
                aura = new Color(1f, 0.3f, 0.3f, 0.35f + 0.2f * Mathf.Sin(time * 12f));
            }
            else if (bag.Act == (byte)ItemId.PocketRocket)
            {
                aura = new Color(1f, 0.55f, 0.15f, 0.8f);
            }
            view.Aura.enabled = active && aura.a > 0f;
            if (view.Aura.enabled)
            {
                Vector3 offset = bag.Act == (byte)ItemId.PocketRocket ? new Vector3(-bag.ActAim.X, -bag.ActAim.Y, 0f) * 0.8f : Vector3.zero;
                view.Aura.transform.position = center + offset + new Vector3(0f, 0f, 0.05f);
                view.Aura.transform.localScale = Vector3.one * (bag.MagnetTicks > 0 ? Tuning.BigMagnetRadius * 0.5f : 3f);
                view.Aura.color = aura;
            }
            view.Mark.enabled = bag.MarkTicks > 0;
            if (view.Mark.enabled)
            {
                view.Mark.transform.position = new Vector3(bag.Mark.X, bag.Mark.Y, Z);
                view.Mark.transform.localScale = Vector3.one * 0.9f;
                view.Mark.color = new Color(1f, 1f, 1f, bag.MarkTicks < 120 && Mathf.Repeat(time * 6f, 1f) < 0.4f ? 0.3f : 0.95f);
            }
        }

        // 雷雲の雷（雲から下へのぎざぎざの線）。
        private void AddBolt(Vector3 cloud, float depth)
        {
            const int points = 8;
            LineRenderer line = NewLine(points, 0.18f);
            for (int p = 0; p < points; p++)
            {
                float t = p / (float)(points - 1);
                float x = p == 0 || p == points - 1 ? 0f : (Random.value - 0.5f) * 0.8f;
                line.SetPosition(p, cloud + new Vector3(x, -depth * t, -0.05f));
            }
            _effects.Add(new Effect
            {
                Line = line, Start = Time.time, Duration = 0.25f, From = new Color(1f, 1f, 0.7f, 1f), To = new Color(1f, 0.9f, 0.3f, 0f), Width = 0.18f,
            });
        }

        // ---------------------------------------------------------------- 線の効果

        // 振りの弧（縦・横・斜め。照準の向きの前に、振りの向きの弧）。
        private void AddSlash(Vector3 center, Vector2 aim, int slash)
        {
            if (aim.sqrMagnitude < 0.01f)
            {
                aim = Vector2.right;
            }
            aim.Normalize();
            float turn = slash == 0 ? 90f : slash == 1 ? 0f : 45f;
            float baseAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg + turn;
            var front = new Vector3(aim.x, aim.y, 0f) * Tuning.SwordReach;
            const int points = 14;
            LineRenderer line = NewLine(points, 0.22f);
            for (int p = 0; p < points; p++)
            {
                float t = p / (float)(points - 1);
                float angle = (baseAngle + Mathf.Lerp(-70f, 70f, t)) * Mathf.Deg2Rad;
                // 振りの向きに細長い弧（刀の軌跡）。
                var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Tuning.SwordSlashHalfLength * 0.9f;
                line.SetPosition(p, center + front * 0.5f + offset);
            }
            _effects.Add(new Effect
            {
                Line = line, Start = Time.time, Duration = 0.22f, From = new Color(1f, 0.85f, 0.85f, 0.95f),
                To = new Color(0.65f, 0.1f, 0.15f, 0f), Width = 0.22f,
            });
        }

        // 一閃の線（移動の前と後を結ぶ。白から臙脂色へ消える）。
        private void AddFlash(in ItemBag bag)
        {
            LineRenderer line = NewLine(2, 0.6f);
            line.SetPosition(0, new Vector3(bag.ActFrom.X, bag.ActFrom.Y, Z - 0.05f));
            line.SetPosition(1, new Vector3(bag.ActTo.X, bag.ActTo.Y, Z - 0.05f));
            _effects.Add(new Effect
            {
                Line = line, Start = Time.time, Duration = 0.5f, From = new Color(1f, 1f, 1f, 1f), To = new Color(0.65f, 0.1f, 0.15f, 0f),
                Width = 0.6f,
            });
        }

        // イヤーボーンの風の輪（円の縁まで広がる）。
        private void AddRing(Vector3 center)
        {
            AddRing(center, Tuning.EarRadius, new Color(0.85f, 1f, 0.95f, 0.9f), new Color(0.6f, 0.95f, 0.85f, 0f));
        }

        private void AddRing(Vector3 center, float radius, Color from, Color to)
        {
            const int points = 48;
            LineRenderer line = NewLine(points, 0.3f);
            line.loop = true;
            _effects.Add(new Effect
            {
                Line = line, Start = Time.time, Duration = 0.45f, From = from, To = to, Width = 0.3f, Center = center, Radius = radius,
            });
        }

        private LineRenderer NewLine(int points, float width)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.positionCount = points;
            line.widthMultiplier = width;
            return line;
        }

        private void RenderEffects()
        {
            float now = Time.time;
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                Effect effect = _effects[i];
                float t = (now - effect.Start) / effect.Duration;
                if (t >= 1f)
                {
                    Destroy(effect.Line.gameObject);
                    _effects.RemoveAt(i);
                    continue;
                }
                Color color = Color.Lerp(effect.From, effect.To, t);
                effect.Line.startColor = color;
                effect.Line.endColor = color;
                effect.Line.widthMultiplier = effect.Width * (1f - 0.5f * t);
                if (effect.Radius > 0f)
                {
                    float radius = effect.Radius * (0.2f + 0.8f * Mathf.Sqrt(t));
                    int points = effect.Line.positionCount;
                    for (int p = 0; p < points; p++)
                    {
                        float angle = p * Mathf.PI * 2f / points;
                        effect.Line.SetPosition(p, effect.Center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 詠唱のゲージ

        private void OnGUI()
        {
            if (_camera == null || _feed == null)
            {
                return;
            }
            Color previous = GUI.color;
            DrawExpirySeconds();
            int count = _feed.CharacterCount;
            for (int i = 0; i < count; i++)
            {
                CharacterState state = _feed.GetState(i);
                float progress;
                if (ItemRules.TryGetCast(state, Tuning, out int total, out int elapsed))
                {
                    progress = Mathf.Clamp01((elapsed + 1f) / total);
                }
                else if (state.Combat.HeistTicks > 0
                         && _feed.GetConfig(i).Combat.Super.Effect == Airerage.Core.Skills.SkillEffect.BankHeist)
                {
                    // THIEF のバンクハイストの詠唱も同じ棒で出す（5 秒ごとに満ちてコインが増える。ユーザーの指示、2026-11-05）。
                    int five = 5 * SimTime.TicksPerSecond;
                    progress = (state.Combat.HeistTicks - 1) % five / (float)five;
                }
                else
                {
                    continue;
                }
                float half = _feed.GetConfig(i).Movement.HalfHeight;
                Vector3 screen = _camera.WorldToScreenPoint(new Vector3(state.Position.X, state.Position.Y - half - 0.25f, 0f));
                if (screen.z <= 0f)
                {
                    continue;
                }
                DrawCastBar(new Vector2(screen.x, Screen.height - screen.y), progress, _feed.ColorOf(i));
            }
            GUI.color = previous;
        }

        /// <summary>
        /// 詠唱のゲージ（棒）を、画面の点 top（上の真ん中）に描く（progress は 0〜1）。詠唱するものはすべて、キャラクターの下にこれを出す
        /// （ユーザーの指示、2026-11-05）。
        /// </summary>
        public static void DrawCastBar(Vector2 top, float progress, Color accent)
        {
            var back = new Rect(top.x - CastBarWidth * 0.5f, top.y, CastBarWidth, CastBarHeight);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(back.x - 2f, back.y - 2f, back.width + 4f, back.height + 4f), Texture2D.whiteTexture);
            GUI.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            Color fill = Color.Lerp(accent, Color.white, 0.25f);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * progress, back.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(back.x + back.width * progress - 1f, back.y - 2f, 2f, back.height + 4f), Texture2D.whiteTexture);
        }

        // 消える 5 秒前からの、落ちているアイテムの残りの秒数（円形ゲージの真ん中）。
        private void DrawExpirySeconds()
        {
            if (_expiryStyle == null)
            {
                _expiryStyle = Airerage.Runtime.UI.GameFonts.Use(new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            }
            foreach (Shown shown in _shown.Values)
            {
                if (shown.Kind != WorldObjectKind.DroppedItem || shown.Remaining > ExpiryWarningTicks)
                {
                    continue;
                }
                Vector3 screen = _camera.WorldToScreenPoint(shown.Root.transform.position + new Vector3(0f, DropSize * 1.1f, 0f));
                if (screen.z <= 0f)
                {
                    continue;
                }
                var rect = new Rect(screen.x - 30f, Screen.height - screen.y - 12f, 60f, 24f);
                int seconds = Mathf.Max(1, Mathf.CeilToInt(shown.Remaining / (float)SimTime.TicksPerSecond));
                _expiryStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), seconds.ToString(), _expiryStyle);
                _expiryStyle.normal.textColor = Color.white;
                GUI.Label(rect, seconds.ToString(), _expiryStyle);
            }
        }

        private void Play(SoundId id)
        {
            if (_audio != null)
            {
                _audio.Play(id);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
