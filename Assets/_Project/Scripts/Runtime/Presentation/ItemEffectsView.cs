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
        private bool _primed;

        private sealed class Shown
        {
            public WorldObjectKind Kind;
            public GameObject Root;
            public Transform Icon;
            public SpriteRenderer Glow;
            public float Spin;
        }

        private sealed class Character
        {
            public int Pickups;
            public int Uses;
            public int Drops;
            public int Bursts;
            public int Flashes;
            public int LastSlash = -1;
            public GameObject Wings;
            public SpriteRenderer LeftWing;
            public SpriteRenderer RightWing;
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
        }

        private void OnDestroy()
        {
            SoftSprites.Release(_glow);
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
                if (item.Kind != WorldObjectKind.DroppedItem && item.Kind != WorldObjectKind.ItemShot && item.Kind != WorldObjectKind.SandBall)
                {
                    continue;
                }
                _present.Add(item.Id);
                if (!_shown.TryGetValue(item.Id, out Shown shown))
                {
                    shown = Create(item);
                    _shown[item.Id] = shown;
                }
                var position = new Vector3(item.Position.X, item.Position.Y, Z);
                switch (item.Kind)
                {
                    case WorldObjectKind.DroppedItem:
                        // ゆっくり上下に揺れ、光が脈打つ。
                        float bob = item.Stored == 1 ? 0.12f + 0.08f * Mathf.Sin(time * 3f + item.Id) : 0f;
                        shown.Root.transform.position = position + new Vector3(0f, bob, 0f);
                        shown.Glow.color = WithAlpha(ItemIcons.Accent((ItemId)item.Variant), 0.45f + 0.2f * Mathf.Sin(time * 5f + item.Id));
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
                Destroy(_shown[id].Root);
                _shown.Remove(id);
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
                }
                view.Pickups = bag.PickupCount;
                view.Uses = bag.UseCount;
                view.Drops = bag.DropCount;
                view.Bursts = bag.BurstCount;
                view.Flashes = bag.FlashCount;

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

                // イカロスウイング: 飛んでいる間、背中の両側に羽（羽ばたき。残り 1 秒で点滅）。
                bool flying = ItemRules.Flying(state, Tuning);
                view.Wings.SetActive(flying);
                if (flying)
                {
                    int left = ItemRules.FlightTicksLeft(state, Tuning);
                    bool blink = left <= Tuning.WingBlinkTicks && Mathf.Repeat(time * 10f, 1f) < 0.4f;
                    float flap = 0.75f + 0.25f * Mathf.Sin(time * 22f);
                    view.Wings.transform.position = center + new Vector3(0f, 0.35f, 0.05f);
                    view.LeftWing.transform.localScale = new Vector3(-1.2f, 1.2f * flap, 1f);
                    view.RightWing.transform.localScale = new Vector3(1.2f, 1.2f * flap, 1f);
                    Color wing = new Color(1f, 1f, 1f, blink ? 0.25f : 1f);
                    view.LeftWing.color = wing;
                    view.RightWing.color = wing;
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
            return view;
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
            const int points = 48;
            LineRenderer line = NewLine(points, 0.3f);
            line.loop = true;
            _effects.Add(new Effect
            {
                Line = line, Start = Time.time, Duration = 0.45f, From = new Color(0.85f, 1f, 0.95f, 0.9f), To = new Color(0.6f, 0.95f, 0.85f, 0f),
                Width = 0.3f, Center = center, Radius = Tuning.EarRadius,
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
