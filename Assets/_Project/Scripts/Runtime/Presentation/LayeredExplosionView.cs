using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 3 つの層を重ねた爆発の見た目（ユーザーの要望、2026-10-29。手本はスマブラの爆発の作り方）。手前から:
    /// 1. 青い縁の白い閃光と、長い光条（一瞬で消える）。
    /// 2. オレンジ色の弾ける炎（外へ飛び散って、終わりぎわに赤黒くなって消える）。
    /// 3. 黒い煙（気体。ゆっくり上へ流れて広がり、長く残る）。
    /// 見た目だけ（当たりは Core が決める）。呼ぶ側が爆発を見つけたときに Spawn を呼び、その後は時間で進む（Core の爆発の印が
    /// 消えても、煙は残る）。予測が外れて巻き戻った爆発は Cancel で消す。乱れ方は key（物の番号）で決めるので、同じ爆発は同じ見た目。
    /// </summary>
    public sealed class LayeredExplosionView : MonoBehaviour
    {
        private const float FlashSeconds = 0.12f;
        private const float FlameSeconds = 0.55f;
        private const float SmokeSeconds = 2.6f;
        private const int Rays = 10;
        private const int Flames = 12;
        private const int SmokePuffs = 10;

        // 手前から閃光・炎・煙（キャラクターの体より手前）。並びは奥行きと描く順の両方で決める。
        private const float FlashZ = -1.45f;
        private const float FlameZ = -1.3f;
        private const float SmokeZ = -1.15f;
        private const int SmokeOrder = 40;
        private const int FlameOrder = 41;
        private const int FlashOrder = 42;

        private static readonly Color FlashWhite = new Color(1f, 1f, 1f, 1f);
        private static readonly Color FlashBlue = new Color(0.35f, 0.65f, 1f, 0.95f);
        private static readonly Color FlameHot = new Color(1f, 0.93f, 0.55f, 1f);
        private static readonly Color FlameOrange = new Color(1f, 0.52f, 0.1f, 1f);
        private static readonly Color FlameEmber = new Color(0.6f, 0.12f, 0.04f, 1f);

        [Tooltip("やわらかい板の見本（SpriteRenderer。絵は起動時に描く。隠しておく）")]
        [SerializeField] private SpriteRenderer _spriteTemplate;
        [Tooltip("光条の線の見本（隠しておく）")]
        [SerializeField] private LineRenderer _lineTemplate;

        private readonly List<Explosion> _explosions = new List<Explosion>();
        private Sprite _puff;
        private Sprite _glow;

        private sealed class Explosion
        {
            public int Key;
            public Vector3 Center;
            public float Radius;
            public float Start;
            public GameObject Root;
            public SpriteRenderer FlashRim;
            public SpriteRenderer FlashCore;
            public LineRenderer[] RayRims;
            public LineRenderer[] RayCores;
            public float[] RayAngles;
            public float[] RayLengths;
            public SpriteRenderer FlameCore;
            public SpriteRenderer[] Flames;
            public Vector3[] FlameStarts;
            public Vector3[] FlameTravel;
            public float[] FlameSizes;
            public float[] FlameLives;
            public SpriteRenderer[] Smoke;
            public Vector3[] SmokeStarts;
            public Vector3[] SmokeDrift;
            public float[] SmokeSizes;
            public float[] SmokeSpins;
            public float[] SmokeDelays;
        }

        /// <summary>今出している爆発の数（煙が残っている間を含む。テスト用）。</summary>
        public int ActiveCount => _explosions.Count;

        /// <summary>これまでに出した爆発の数（テスト用）。</summary>
        public int SpawnedCount { get; private set; }

        /// <summary>閃光を出している爆発の数（テスト用）。</summary>
        public int FlashingCount
        {
            get
            {
                int count = 0;
                foreach (Explosion explosion in _explosions)
                {
                    count += explosion.FlashCore.enabled ? 1 : 0;
                }
                return count;
            }
        }

        /// <summary>煙だけが残っている爆発の数（閃光と炎が消えた後。テスト用）。</summary>
        public int SmokeOnlyCount
        {
            get
            {
                int count = 0;
                foreach (Explosion explosion in _explosions)
                {
                    count += !explosion.FlashCore.enabled && !explosion.FlameCore.enabled ? 1 : 0;
                }
                return count;
            }
        }

        private void Awake()
        {
            if (_spriteTemplate == null || _lineTemplate == null)
            {
                Debug.LogError($"{name}: 板か線の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _spriteTemplate.gameObject.SetActive(false);
            _lineTemplate.gameObject.SetActive(false);
            _puff = SoftSprites.CreatePuff(2, "ExplosionPuff");
            _glow = SoftSprites.CreateGlow("ExplosionGlow");
        }

        private void OnDestroy()
        {
            SoftSprites.Release(_puff);
            SoftSprites.Release(_glow);
        }

        /// <summary>key の爆発を、中心 center（x・y を使う）・半径 radius（m。Core の爆発の半径）で出す。同じ key がもう出ていれば何もしない。</summary>
        public void Spawn(int key, Vector3 center, float radius)
        {
            if (!enabled || Find(key) >= 0)
            {
                return;
            }
            radius = Mathf.Max(0.3f, radius);
            var random = new System.Random(key * 7919 + 17);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            var root = new GameObject($"Explosion#{key}");
            root.transform.SetParent(transform, false);
            var explosion = new Explosion
            {
                Key = key,
                Center = new Vector3(center.x, center.y, 0f),
                Radius = radius,
                Start = Time.time,
                Root = root,
            };

            // 3. 煙（いちばん奥。炎の少し後から出る）。
            explosion.Smoke = new SpriteRenderer[SmokePuffs];
            explosion.SmokeStarts = new Vector3[SmokePuffs];
            explosion.SmokeDrift = new Vector3[SmokePuffs];
            explosion.SmokeSizes = new float[SmokePuffs];
            explosion.SmokeSpins = new float[SmokePuffs];
            explosion.SmokeDelays = new float[SmokePuffs];
            for (int i = 0; i < SmokePuffs; i++)
            {
                float angle = Range(0f, Mathf.PI * 2f);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                explosion.SmokeStarts[i] = direction * Range(0f, 0.7f) * radius;
                explosion.SmokeDrift[i] = direction * Range(0.15f, 0.4f) * radius + Vector3.up * Range(0.3f, 0.55f) * radius;
                explosion.SmokeSizes[i] = Range(1.5f, 2f) * radius;
                explosion.SmokeSpins[i] = Range(-30f, 30f);
                explosion.SmokeDelays[i] = Range(0.04f, 0.14f);
                float shade = Range(0.05f, 0.14f);
                explosion.Smoke[i] = NewSprite(root.transform, $"Smoke{i}", _puff, new Color(shade, shade, shade, 0f), SmokeOrder);
            }

            // 2. 炎（真ん中の火の玉と、外へ飛び散るかたまり）。
            explosion.FlameCore = NewSprite(root.transform, "FlameCore", _puff, FlameOrange, FlameOrder);
            explosion.Flames = new SpriteRenderer[Flames];
            explosion.FlameStarts = new Vector3[Flames];
            explosion.FlameTravel = new Vector3[Flames];
            explosion.FlameSizes = new float[Flames];
            explosion.FlameLives = new float[Flames];
            for (int i = 0; i < Flames; i++)
            {
                float angle = (i + Range(-0.35f, 0.35f)) * Mathf.PI * 2f / Flames;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                explosion.FlameStarts[i] = direction * Range(0.05f, 0.25f) * radius;
                explosion.FlameTravel[i] = direction * Range(0.7f, 1.25f) * radius;
                explosion.FlameSizes[i] = Range(0.8f, 1.25f) * radius;
                explosion.FlameLives[i] = Range(0.65f, 1f) * FlameSeconds;
                explosion.Flames[i] = NewSprite(root.transform, $"Flame{i}", _puff, FlameHot, FlameOrder);
            }

            // 1. 閃光（青い縁の白い光の玉と、長い光条。光条も青い縁の上に白い芯）。
            explosion.FlashRim = NewSprite(root.transform, "FlashRim", _glow, FlashBlue, FlashOrder);
            explosion.FlashCore = NewSprite(root.transform, "FlashCore", _glow, FlashWhite, FlashOrder + 1);
            explosion.RayRims = new LineRenderer[Rays];
            explosion.RayCores = new LineRenderer[Rays];
            explosion.RayAngles = new float[Rays];
            explosion.RayLengths = new float[Rays];
            for (int i = 0; i < Rays; i++)
            {
                explosion.RayAngles[i] = (i + Range(-0.3f, 0.3f)) * Mathf.PI * 2f / Rays;
                explosion.RayLengths[i] = Range(2.4f, 4.2f) * radius;
                explosion.RayRims[i] = NewRay(root.transform, $"RayRim{i}", 0.2f * radius, FlashBlue, FlashOrder);
                explosion.RayCores[i] = NewRay(root.transform, $"RayCore{i}", 0.09f * radius, FlashWhite, FlashOrder + 1);
            }
            _explosions.Add(explosion);
            SpawnedCount++;
            Render(explosion, 0f);
        }

        /// <summary>key の爆発を消す（予測が外れて、なかったことになった爆発）。</summary>
        public void Cancel(int key)
        {
            int index = Find(key);
            if (index < 0)
            {
                return;
            }
            Destroy(_explosions[index].Root);
            _explosions.RemoveAt(index);
        }

        private int Find(int key)
        {
            for (int i = 0; i < _explosions.Count; i++)
            {
                if (_explosions[i].Key == key)
                {
                    return i;
                }
            }
            return -1;
        }

        private void LateUpdate()
        {
            for (int i = _explosions.Count - 1; i >= 0; i--)
            {
                Explosion explosion = _explosions[i];
                float age = Time.time - explosion.Start;
                if (age >= SmokeSeconds)
                {
                    Destroy(explosion.Root);
                    _explosions.RemoveAt(i);
                    continue;
                }
                Render(explosion, age);
            }
        }

        private static void Render(Explosion e, float age)
        {
            Vector3 center = e.Center;
            float r = e.Radius;

            // 1. 閃光: 一瞬で広がり、すぐ消える。光条は最初の数フレームで伸び切る。
            bool flashing = age < FlashSeconds;
            e.FlashRim.enabled = flashing;
            e.FlashCore.enabled = flashing;
            for (int i = 0; i < Rays; i++)
            {
                e.RayRims[i].enabled = flashing;
                e.RayCores[i].enabled = flashing;
            }
            if (flashing)
            {
                float t = age / FlashSeconds;
                float grow = 1f - (1f - Mathf.Min(1f, t * 3f)) * (1f - Mathf.Min(1f, t * 3f));
                float fade = 1f - t * t;
                Place(e.FlashRim, center, FlashZ + 0.02f, r * 2.9f * (0.6f + 0.4f * grow), 0f, WithAlpha(FlashBlue, FlashBlue.a * fade));
                Place(e.FlashCore, center, FlashZ, r * 2.2f * (0.6f + 0.4f * grow), 0f, WithAlpha(FlashWhite, fade));
                float reach = Mathf.Min(1f, t * 4f);
                for (int i = 0; i < Rays; i++)
                {
                    var direction = new Vector3(Mathf.Cos(e.RayAngles[i]), Mathf.Sin(e.RayAngles[i]), 0f);
                    Vector3 end = center + direction * e.RayLengths[i] * reach;
                    SetRay(e.RayRims[i], center, end, FlashZ + 0.01f, WithAlpha(FlashBlue, FlashBlue.a * fade));
                    SetRay(e.RayCores[i], center, end, FlashZ - 0.01f, WithAlpha(FlashWhite, fade));
                }
            }

            // 2. 炎: 真ん中の火の玉がふくらんで薄れ、かたまりは外へ弾けて、黄色→オレンジ→赤黒く変わって消える。
            float coreT = age / (FlameSeconds * 0.7f);
            e.FlameCore.enabled = coreT < 1f;
            if (e.FlameCore.enabled)
            {
                Color color = FlameColor(coreT);
                color.a = 1f - coreT * coreT;
                Place(e.FlameCore, center, FlameZ + 0.01f, r * (1.7f + 0.5f * coreT), age * 40f, color);
            }
            for (int i = 0; i < Flames; i++)
            {
                float t = age / e.FlameLives[i];
                SpriteRenderer flame = e.Flames[i];
                flame.enabled = t < 1f;
                if (!flame.enabled)
                {
                    continue;
                }
                float burst = 1f - (1f - t) * (1f - t);
                Vector3 at = center + e.FlameStarts[i] + e.FlameTravel[i] * burst;
                Color color = FlameColor(t);
                color.a = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
                Place(flame, at, FlameZ - 0.002f * i, e.FlameSizes[i] * (0.5f + 0.5f * burst), i * 37f + age * 90f, color);
            }

            // 3. 煙: 少し遅れて出て、ふくらみながら上へ流れ、長く残ってから薄れる。
            for (int i = 0; i < SmokePuffs; i++)
            {
                float local = age - e.SmokeDelays[i];
                SpriteRenderer puff = e.Smoke[i];
                puff.enabled = local > 0f;
                if (!puff.enabled)
                {
                    continue;
                }
                float life = SmokeSeconds - e.SmokeDelays[i];
                float t = Mathf.Clamp01(local / life);
                float spread = 1f - (1f - t) * (1f - t);
                Vector3 at = center + e.SmokeStarts[i] + e.SmokeDrift[i] * (local / SmokeSeconds) * 2f;
                float alpha = Mathf.Min(1f, local / 0.12f) * (t < 0.45f ? 1f : 1f - (t - 0.45f) / 0.55f) * 0.92f;
                Color color = puff.color;
                color.a = alpha;
                Place(puff, at, SmokeZ - 0.002f * i, e.SmokeSizes[i] * (0.55f + 0.45f * spread), e.SmokeSpins[i] * local, color);
            }
        }

        // 黄色からすぐオレンジになり、しばらくオレンジのまま弾けて、終わりぎわだけ赤黒くなる。
        private static Color FlameColor(float t)
        {
            if (t < 0.2f)
            {
                return Color.Lerp(FlameHot, FlameOrange, t / 0.2f);
            }
            return t < 0.65f ? FlameOrange : Color.Lerp(FlameOrange, FlameEmber, (t - 0.65f) / 0.35f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private static void Place(SpriteRenderer sprite, Vector3 at, float z, float size, float spin, Color color)
        {
            Transform t = sprite.transform;
            t.position = new Vector3(at.x, at.y, z);
            t.localScale = Vector3.one * size;
            t.rotation = Quaternion.Euler(0f, 0f, spin);
            sprite.color = color;
        }

        private static void SetRay(LineRenderer line, Vector3 from, Vector3 to, float z, Color color)
        {
            line.SetPosition(0, new Vector3(from.x, from.y, z));
            line.SetPosition(1, new Vector3(to.x, to.y, z));
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, 0f);
        }

        private SpriteRenderer NewSprite(Transform parent, string spriteName, Sprite sprite, Color color, int order)
        {
            SpriteRenderer renderer = Instantiate(_spriteTemplate, parent);
            renderer.name = spriteName;
            renderer.gameObject.SetActive(true);
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        // 光条: 根もとが太く、先へ細くなって消える線。
        private LineRenderer NewRay(Transform parent, string lineName, float width, Color color, int order)
        {
            LineRenderer line = Instantiate(_lineTemplate, parent);
            line.name = lineName;
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            line.widthMultiplier = width;
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, 0f);
            line.sortingOrder = order;
            line.enabled = false;
            return line;
        }
    }
}
