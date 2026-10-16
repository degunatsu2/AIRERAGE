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
    /// - ミラーボール: 回る球と、回りながら色が変わる光の筋。消える前の 1 秒は点滅する。
    /// - セーフティボックス: 金庫の箱。上に「X/300」（今の枚数/上限）を出す。150 枚で金色、300 枚で虹色に光る（ユーザーの決定、
    ///   2026-10-16）。攻撃を受けると白く光って揺れる。
    /// - 投射物（コイントス）: 回る小さなコイン。
    /// 物が出たとき・セーフティボックスが消えたとき（ばらまき）に音を鳴らす（_audio があれば）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class WorldObjectsView : MonoBehaviour
    {
        private const float BlinkSeconds = 1f;
        private const float FlashSeconds = 0.2f;
        private const int RaySegments = 16;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color Gold = new Color(1f, 0.8f, 0.2f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("見た目の見本（ミラーボール、セーフティボックス、投射物の順）。シーンでは隠しておく")]
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
            }
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
                float radius = i % 2 == 0 ? 1.6f + 0.25f * Mathf.Sin(Time.time * 6f + i) : 0.65f;
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
            if (safe.Damage > shown.Damage)
            {
                shown.FlashAt = Time.time;
            }
            shown.Damage = safe.Damage;

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
            float flash = 1f - (Time.time - shown.FlashAt) / FlashSeconds;
            if (flash > 0f)
            {
                color = Color.Lerp(color, Color.white, flash);
                shown.Visual.transform.position = shown.Anchor + new Vector3(Mathf.Sin(Time.time * 90f) * 0.08f * flash, 0f, 0f);
            }
            if (shown.Glow != SafeGlow.None)
            {
                shown.Visual.transform.localScale *= 1f + 0.05f * pulse;
            }
            _block.SetColor(BaseColorId, color);
            foreach (Renderer renderer in shown.Renderers)
            {
                renderer.SetPropertyBlock(_block);
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
            }
        }
    }
}
