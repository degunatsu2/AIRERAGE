using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Objects;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ワールドに置かれた物と投射物の表示（docs/13 T5-6、docs/14）。種類ごとの見本を複製して、物の番号ごとに 1 つずつ出す。
    /// - ミラーボール: 回る球と、回りながら色が変わる光の筋。消える前の 1 秒は点滅する（2026-10-16 の要望で大きくした）。
    /// - セーフティボックス: 金庫の箱。上に「X/300」（今の枚数/上限）と、耐久の HP バーを出す。150 枚で金色、300 枚で虹色に光る
    ///   （ユーザーの決定、2026-10-16）。攻撃を受けると白く光って揺れる。周りの円形のゲージで次のチャージまでを示し、チャージした
    ///   瞬間に光って音が鳴る。150 枚に着いたら少し派手な音で強く光り、300 枚に着いたらとても派手な音で光り続ける（チャージは止まり、
    ///   ゲージは消える。ユーザーの要望、2026-10-16）。
    /// - 投射物（コイントス）: 回る小さなコイン。
    /// - 突風（ウインドクロークを解いたとき）: 進む向きに並んだ、弧の形の風の線。
    /// - 風の帯（スカイロケット）: 長方形の枠と、向きへ流れる風の線。消える前の 1 秒は点滅する。
    /// 物が出たとき・セーフティボックスが消えたとき（ばらまき）に音を鳴らす（_audio があれば）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class WorldObjectsView : MonoBehaviour
    {
        private const float BlinkSeconds = 1f;
        private const float FlashSeconds = 0.2f;
        private const int RaySegments = 16;
        private const int RingPoints = 48;
        private const float RingRadius = 1.25f;

        // ミラーボールの光の筋の長さ（m。2026-10-16 の要望で、球と一緒に 1.6 倍に大きくした）。
        private const float RayOuterRadius = 2.6f;
        private const float RayInnerRadius = 1.05f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color Gold = new Color(1f, 0.8f, 0.2f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("見た目の見本（ミラーボール、セーフティボックス、投射物、突風、風の帯の順）。シーンでは隠しておく")]
        [SerializeField] private GameObject[] _templates = new GameObject[0];
        [Tooltip("セーフティボックスの枚数を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("物が出た・壊れたときの音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;

        private readonly Dictionary<int, Shown> _shown = new Dictionary<int, Shown>();
        private readonly List<int> _gone = new List<int>();
        private readonly HashSet<int> _present = new HashSet<int>();
        private ISimulationFeed _feed;
        private MaterialPropertyBlock _block;
        private GUIStyle _label;
        private bool _primed;

        private sealed class Shown
        {
            public GameObject Visual;
            public WorldObjectKind Kind;
            public Renderer[] Renderers;
            public LineRenderer Rays;
            public Color BaseColor;
            public float BornAt;
            public int Damage;
            public float FlashAt = -10f;
            public string Label = string.Empty;
            public SafeGlow Glow;
            public Vector3 Anchor;
            public LineRenderer Ring;
            public LineRenderer RingBack;
            public int Coins = -1;
            public float ChargeAt = -10f;
            public float ChargeSeconds = 0.3f;
            public float ChargeStrength;
            public SafeEvent LastEvent;
            public float Health = 1f;
            public float RingProgress = -1f;
            public LineRenderer Outline;
            public LineRenderer[] Streaks = new LineRenderer[0];
        }

        /// <summary>セーフティボックスのチャージの演出の種類（表示用）。</summary>
        public enum SafeEvent
        {
            None,

            /// <summary>ふつうのチャージ（光と小さな音）。</summary>
            Charge,

            /// <summary>150 枚に着いた（強い光と少し派手な音）。</summary>
            Gold,

            /// <summary>上限の 300 枚に着いた（とても派手な音。光り続ける）。</summary>
            Max,
        }

        /// <summary>セーフティボックスの光り方（表示用）。</summary>
        public enum SafeGlow
        {
            None,
            Gold,
            Rainbow,
        }

        /// <summary>表示している物の数（テスト用）。</summary>
        public int ShownCount => _shown.Count;

        /// <summary>番号 id のセーフティボックスの枚数の文字と光り方（テスト用。表示していなければ false）。</summary>
        public bool TryGetSafe(int id, out string label, out SafeGlow glow)
        {
            bool found = _shown.TryGetValue(id, out Shown shown) && shown.Kind == WorldObjectKind.Safe;
            label = found ? shown.Label : string.Empty;
            glow = found ? shown.Glow : SafeGlow.None;
            return found;
        }

        /// <summary>
        /// 番号 id のセーフティボックスの、最後のチャージの演出・耐久の残り（0〜1）・次のチャージまでのゲージ（0〜1。上限なら -1）
        /// （テスト用。表示していなければ false）。
        /// </summary>
        public bool TryGetSafeState(int id, out SafeEvent lastEvent, out float health, out float ringProgress)
        {
            bool found = _shown.TryGetValue(id, out Shown shown) && shown.Kind == WorldObjectKind.Safe;
            lastEvent = found ? shown.LastEvent : SafeEvent.None;
            health = found ? shown.Health : 0f;
            ringProgress = found ? shown.RingProgress : -1f;
            return found;
        }

        /// <summary>チャージで枚数が before から after になったときの演出（増えていなければ None）。</summary>
        public static SafeEvent EventOf(int before, int after, SafeTuning tuning)
        {
            if (after <= before)
            {
                return SafeEvent.None;
            }
            if (before < tuning.MaxCoins && after >= tuning.MaxCoins)
            {
                return SafeEvent.Max;
            }
            return before < tuning.GoldCoins && after >= tuning.GoldCoins ? SafeEvent.Gold : SafeEvent.Charge;
        }

        /// <summary>セーフティボックスの枚数の文字（「X/300」）。</summary>
        public static string SafeLabel(int coins, int max)
        {
            return $"{coins}/{max}";
        }

        /// <summary>枚数に合う光り方（金色の枚数以上で金色、上限で虹色）。</summary>
        public static SafeGlow GlowOf(int coins, SafeTuning tuning)
        {
            return coins >= tuning.MaxCoins ? SafeGlow.Rainbow : coins >= tuning.GoldCoins ? SafeGlow.Gold : SafeGlow.None;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            int kinds = System.Enum.GetValues(typeof(WorldObjectKind)).Length;
            if (_feed == null || _templates.Length != kinds || System.Array.IndexOf(_templates, null) >= 0)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、{kinds} 種類の見た目の見本が設定されていません", this);
                enabled = false;
                return;
            }
            foreach (GameObject template in _templates)
            {
                template.SetActive(false);
            }
            _block = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            WorldObjects objects = _feed.Objects;
            _present.Clear();
            if (objects != null)
            {
                foreach (WorldObject item in objects.Items)
                {
                    _present.Add(item.Id);
                    if (!_shown.TryGetValue(item.Id, out Shown shown))
                    {
                        shown = Create(item);
                        _shown.Add(item.Id, shown);
                        PlaySpawn(item.Kind);
                    }
                    UpdateShown(shown, item);
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
                Shown shown = _shown[id];
                if (shown.Kind == WorldObjectKind.Safe && _primed && _audio != null)
                {
                    _audio.Play(SoundId.SafeBreak);
                }
                Destroy(shown.Visual);
                _shown.Remove(id);
            }
            // 最初のフレーム（シーンを開いたとき、巻き戻しで物が戻ったとき）には、出た音を鳴らさない。
            _primed = true;
        }

        private Shown Create(in WorldObject item)
        {
            GameObject template = _templates[(int)item.Kind - 1];
            GameObject visual = Instantiate(template, transform);
            visual.name = $"{item.Kind}#{item.Id}";
            visual.SetActive(true);
            var shown = new Shown
            {
                Visual = visual,
                Kind = item.Kind,
                Renderers = visual.GetComponentsInChildren<MeshRenderer>(),
                BornAt = Time.time,
                Damage = item.Damage,
            };
            Transform rays = visual.transform.Find("Rays");
            shown.Rays = rays != null ? rays.GetComponent<LineRenderer>() : null;
            Transform ring = visual.transform.Find("ChargeRing");
            shown.Ring = ring != null ? ring.GetComponent<LineRenderer>() : null;
            Transform ringBack = visual.transform.Find("ChargeRingBack");
            shown.RingBack = ringBack != null ? ringBack.GetComponent<LineRenderer>() : null;
            Transform outline = visual.transform.Find("Outline");
            shown.Outline = outline != null ? outline.GetComponent<LineRenderer>() : null;
            var streaks = new List<LineRenderer>();
            for (int i = 0; visual.transform.Find("Streak" + i) is Transform streak; i++)
            {
                streaks.Add(streak.GetComponent<LineRenderer>());
            }
            shown.Streaks = streaks.ToArray();
            if (item.Kind == WorldObjectKind.Safe)
            {
                // 置いたときに預けた枚数は、チャージとして数えない。
                shown.Coins = SkillRules.SafeCoins(item);
            }
            shown.BaseColor = shown.Renderers.Length > 0 ? shown.Renderers[0].sharedMaterial.GetColor(BaseColorId) : Color.white;
            return shown;
        }

        private void PlaySpawn(WorldObjectKind kind)
        {
            if (!_primed || _audio == null)
            {
                return;
            }
            switch (kind)
            {
                case WorldObjectKind.MirrorBall:
                    _audio.Play(SoundId.MirrorBall, 0.9f);
                    break;
                case WorldObjectKind.Safe:
                    _audio.Play(SoundId.SafePlace);
                    break;
                case WorldObjectKind.Projectile:
                    _audio.Play(SoundId.CoinToss, 0.8f, 0.05f);
                    break;
                case WorldObjectKind.Gust:
                    _audio.Play(SoundId.Gust);
                    break;
                case WorldObjectKind.WindZone:
                    _audio.Play(SoundId.SkyRocket);
                    break;
            }
        }

        private void UpdateShown(Shown shown, in WorldObject item)
        {
            float age = Time.time - shown.BornAt;
            float pop = Mathf.Clamp01(age / 0.2f);
            Transform t = shown.Visual.transform;
            shown.Anchor = new Vector3(item.Position.X, item.Position.Y, 0f);
            t.position = shown.Anchor;
            t.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, 1f - (1f - pop) * (1f - pop));
            switch (item.Kind)
            {
                case WorldObjectKind.MirrorBall:
                    UpdateMirrorBall(shown, item);
                    break;
                case WorldObjectKind.Safe:
                    UpdateSafe(shown, item);
                    break;
                case WorldObjectKind.Projectile:
                    t.rotation = Quaternion.Euler(0f, Time.time * 720f, 90f);
                    break;
                case WorldObjectKind.Gust:
                    UpdateGust(shown, item);
                    break;
                case WorldObjectKind.WindZone:
                    UpdateWindZone(shown, item);
                    break;
            }
        }

        // 突風: 進む向きに並んだ弧（「)」の形）。生まれてからの時間で薄くなる。
        private static void UpdateGust(Shown shown, in WorldObject gust)
        {
            var velocity = new Vector2(gust.Velocity.X, gust.Velocity.Y);
            Vector2 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector2.right;
            var side = new Vector2(-dir.y, dir.x);
            float fade = Mathf.Clamp01(1f - (Time.time - shown.BornAt) / 0.6f);
            for (int s = 0; s < shown.Streaks.Length; s++)
            {
                LineRenderer line = shown.Streaks[s];
                const int points = 9;
                line.positionCount = points;
                float back = s * 0.7f;
                float bend = 0.5f + 0.15f * s;
                for (int k = 0; k < points; k++)
                {
                    float u = k / (float)(points - 1) * 2f - 1f;
                    Vector2 p = -dir * (back + bend * (1f - u * u)) + side * (u * (1.1f - 0.15f * s));
                    line.SetPosition(k, shown.Anchor + new Vector3(p.x, p.y, -0.6f));
                }
                var color = new Color(0.75f, 1f, 0.55f, 0.8f * fade * (1f - 0.2f * s));
                line.startColor = color;
                line.endColor = color;
            }
        }

        // 風の帯: 置いた点から向きへ伸びる長方形の枠と、向きへ流れる線。
        private void UpdateWindZone(Shown shown, in WorldObject zone)
        {
            Airerage.Core.Skills.WindZoneTuning tuning = _feed.GetConfig(zone.Owner).Skills.WindZone;
            shown.Visual.transform.localScale = Vector3.one;
            var dir = new Vector2(zone.Velocity.X, zone.Velocity.Y);
            var side = new Vector2(-dir.y, dir.x);
            Vector3 origin = shown.Anchor;
            float secondsLeft = (zone.ExpireTick - _feed.CurrentTick) / (float)SimTime.TicksPerSecond;
            bool visible = secondsLeft > BlinkSeconds || Mathf.Repeat(Time.time * 8f, 1f) < 0.6f;
            if (shown.Outline != null)
            {
                shown.Outline.enabled = visible;
                shown.Outline.loop = true;
                shown.Outline.positionCount = 4;
                Vector2 w = side * tuning.HalfWidth;
                Vector2 l = dir * tuning.Length;
                shown.Outline.SetPosition(0, origin + ToWorld(-w));
                shown.Outline.SetPosition(1, origin + ToWorld(-w + l));
                shown.Outline.SetPosition(2, origin + ToWorld(w + l));
                shown.Outline.SetPosition(3, origin + ToWorld(w));
            }
            for (int s = 0; s < shown.Streaks.Length; s++)
            {
                LineRenderer line = shown.Streaks[s];
                line.enabled = visible;
                float lane = (s + 0.5f) / shown.Streaks.Length * 2f - 1f;
                float head = Mathf.Repeat(Time.time * 12f + s * 3.7f, tuning.Length + 2f) - 1f;
                float tail = Mathf.Max(0f, head - 2.2f);
                head = Mathf.Clamp(head, 0f, tuning.Length);
                line.positionCount = 2;
                Vector2 offset = side * (lane * tuning.HalfWidth * 0.85f);
                line.SetPosition(0, origin + ToWorld(offset + dir * tail));
                line.SetPosition(1, origin + ToWorld(offset + dir * head));
            }
        }

        private static Vector3 ToWorld(Vector2 v)
        {
            return new Vector3(v.x, v.y, -0.5f);
        }

        private void UpdateMirrorBall(Shown shown, in WorldObject ball)
        {
            Transform t = shown.Visual.transform;
            t.rotation = Quaternion.Euler(0f, Time.time * 140f, 0f);
            float secondsLeft = (ball.ExpireTick - _feed.CurrentTick) / (float)SimTime.TicksPerSecond;
            bool visible = secondsLeft > BlinkSeconds || Mathf.Repeat(Time.time * 8f, 1f) < 0.6f;
            foreach (Renderer renderer in shown.Renderers)
            {
                renderer.enabled = visible;
            }
            if (shown.Rays == null)
            {
                return;
            }
            shown.Rays.enabled = visible;
            shown.Rays.positionCount = RaySegments * 2;
            shown.Rays.loop = true;
            float spin = Time.time * 1.6f;
            for (int i = 0; i < RaySegments * 2; i++)
            {
                float angle = spin + Mathf.PI * i / RaySegments;
                float radius = i % 2 == 0 ? RayOuterRadius + 0.4f * Mathf.Sin(Time.time * 6f + i) : RayInnerRadius;
                shown.Rays.SetPosition(i, shown.Anchor + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -0.6f));
            }
            Color color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.5f, 1f), 0.6f, 1f);
            color.a = 0.75f;
            shown.Rays.startColor = color;
            shown.Rays.endColor = color;
        }

        private void UpdateSafe(Shown shown, in WorldObject safe)
        {
            SafeTuning tuning = _feed.GetConfig(safe.Owner).Skills.Safe;
            int coins = SkillRules.SafeCoins(safe);
            shown.Label = SafeLabel(coins, tuning.MaxCoins);
            shown.Glow = GlowOf(coins, tuning);
            shown.Health = Mathf.Clamp01(1f - (float)safe.Damage / tuning.Durability);
            if (safe.Damage > shown.Damage)
            {
                shown.FlashAt = Time.time;
            }
            shown.Damage = safe.Damage;
            UpdateCharge(shown, coins, tuning);
            UpdateRing(shown, safe, tuning);

            Color color = shown.BaseColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            switch (shown.Glow)
            {
                case SafeGlow.Gold:
                    color = Color.Lerp(Gold, Color.white, 0.25f * pulse);
                    break;
                case SafeGlow.Rainbow:
                    color = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.8f, 1f), 0.65f, 1f);
                    break;
            }
            // チャージの光（150 枚と 300 枚は強く、長く）。
            float charge = 1f - (Time.time - shown.ChargeAt) / shown.ChargeSeconds;
            if (charge > 0f)
            {
                color = Color.Lerp(color, new Color(1f, 0.95f, 0.6f), Mathf.Clamp01(charge * shown.ChargeStrength));
                shown.Visual.transform.localScale *= 1f + 0.12f * charge * shown.ChargeStrength;
            }
            float flash = 1f - (Time.time - shown.FlashAt) / FlashSeconds;
            if (flash > 0f)
            {
                color = Color.Lerp(color, Color.white, flash);
                shown.Visual.transform.position = shown.Anchor + new Vector3(Mathf.Sin(Time.time * 90f) * 0.08f * flash, 0f, 0f);
            }
            if (shown.Glow == SafeGlow.Rainbow)
            {
                // 上限: 光り続ける（大きく脈打つ）。
                shown.Visual.transform.localScale *= 1.06f + 0.06f * pulse;
            }
            else if (shown.Glow != SafeGlow.None)
            {
                shown.Visual.transform.localScale *= 1f + 0.05f * pulse;
            }
            _block.SetColor(BaseColorId, color);
            foreach (Renderer renderer in shown.Renderers)
            {
                renderer.SetPropertyBlock(_block);
            }
        }

        // チャージで枚数が増えたら、光らせて音を鳴らす（巻き戻しで減ったときは何もしない）。
        private void UpdateCharge(Shown shown, int coins, SafeTuning tuning)
        {
            SafeEvent happened = shown.Coins >= 0 ? EventOf(shown.Coins, coins, tuning) : SafeEvent.None;
            shown.Coins = coins;
            if (happened == SafeEvent.None)
            {
                return;
            }
            shown.LastEvent = happened;
            shown.ChargeAt = Time.time;
            switch (happened)
            {
                case SafeEvent.Max:
                    shown.ChargeSeconds = 0.9f;
                    shown.ChargeStrength = 2.5f;
                    PlaySafe(SoundId.SafeMax);
                    break;
                case SafeEvent.Gold:
                    shown.ChargeSeconds = 0.6f;
                    shown.ChargeStrength = 1.8f;
                    PlaySafe(SoundId.SafeGold);
                    break;
                default:
                    shown.ChargeSeconds = 0.3f;
                    shown.ChargeStrength = 1f;
                    PlaySafe(SoundId.SafeCharge);
                    break;
            }
        }

        private void PlaySafe(SoundId id)
        {
            if (_primed && _audio != null)
            {
                _audio.Play(id, id == SoundId.SafeCharge ? 0.7f : 1f);
            }
        }

        // 次のチャージまでの円形のゲージ（上から時計回りに伸びる）。上限に着いたら出さない。
        private void UpdateRing(Shown shown, in WorldObject safe, SafeTuning tuning)
        {
            bool full = SkillRules.IsSafeFull(safe, tuning);
            shown.RingProgress = full ? -1f : SkillRules.SafeChargeProgress(safe, tuning, _feed.CurrentTick);
            if (shown.RingBack != null)
            {
                shown.RingBack.enabled = !full;
                if (!full)
                {
                    SetArc(shown.RingBack, shown.Anchor, 1f);
                }
            }
            if (shown.Ring == null)
            {
                return;
            }
            shown.Ring.enabled = !full && shown.RingProgress > 0f;
            if (shown.Ring.enabled)
            {
                SetArc(shown.Ring, shown.Anchor, shown.RingProgress);
            }
        }

        private static void SetArc(LineRenderer line, Vector3 center, float fraction)
        {
            int points = Mathf.Max(2, Mathf.CeilToInt(RingPoints * fraction) + 1);
            line.positionCount = points;
            for (int k = 0; k < points; k++)
            {
                float angle = Mathf.PI * 2f * fraction * k / (points - 1);
                line.SetPosition(k, center + new Vector3(Mathf.Sin(angle) * RingRadius, Mathf.Cos(angle) * RingRadius, -0.7f));
            }
        }

        private void OnGUI()
        {
            if (_camera == null || _shown.Count == 0)
            {
                return;
            }
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                _label.normal.textColor = Color.white;
            }
            foreach (Shown shown in _shown.Values)
            {
                if (shown.Kind != WorldObjectKind.Safe)
                {
                    continue;
                }
                Vector3 p = _camera.WorldToScreenPoint(shown.Anchor + new Vector3(0f, 1.3f, 0f));
                if (p.z <= 0f)
                {
                    continue;
                }
                var rect = new Rect(p.x - 60f, Screen.height - p.y - 14f, 120f, 28f);
                Color color = shown.Glow == SafeGlow.Rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 0.8f + 0.3f, 1f), 0.5f, 1f)
                    : shown.Glow == SafeGlow.Gold ? Gold : Color.white;
                GuidePlate.DrawText(rect, shown.Label, _label, color);
                DrawHealth(new Rect(p.x - 40f, rect.yMax + 1f, 80f, 8f), shown.Health);
            }
        }

        // 耐久の HP バー（残りが減ると緑 → 黄 → 赤）。
        private static void DrawHealth(Rect rect, float health)
        {
            Color saved = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), Texture2D.whiteTexture);
            GUI.color = health > 0.5f ? new Color(0.35f, 0.95f, 0.35f) : health > 0.25f ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.25f, 0.2f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * health, rect.height), Texture2D.whiteTexture);
            GUI.color = saved;
        }
    }
}
