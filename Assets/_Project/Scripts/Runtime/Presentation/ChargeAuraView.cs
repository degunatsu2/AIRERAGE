using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ためる技（AttackData.ChargeLevels。RISK TAKER のオールイン）のオーラ（ユーザーの要望、2026-10-16）。
    /// - ためている間と、離して出した技の間、キャラクターの周りに揺らめくオーラを出す。色は段階で変わる:
    ///   白（+0%）→ 緑（+20%）→ 青（+50%）→ 紫（+100%）→ 金（+200%）→ 虹（+500%）。段階が高いほど太く大きい。
    /// - 段階が上がった瞬間に光って音が鳴る（最後の段階は特別な音）。
    /// - ためている間は、技が出る側（向いている方向）の前に、段階の色の矢印「>>」を出す（ユーザーの要望、2026-10-16）。
    /// - ためた技が当たった瞬間（ActionState.Connected）に、段階の色の大きな光を出す（段階が高いほど大きく、重い音）。
    ///   最後の段階（虹）で当てたときの JACKPOT の演出は、当たった側の CharacterView と CombatSoundEvents が出す。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class ChargeAuraView : MonoBehaviour
    {
        private const int Points = 40;
        private const float Z = -0.75f;

        /// <summary>段階の色（白、緑、青、紫、金。最後の段階は虹色）。段階が多いキャラクターは、金の後も金にする。</summary>
        public static readonly Color[] LevelColors =
        {
            new Color(1f, 1f, 1f),
            new Color(0.3f, 1f, 0.35f),
            new Color(0.25f, 0.55f, 1f),
            new Color(0.72f, 0.3f, 1f),
            new Color(1f, 0.8f, 0.15f),
        };

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("オーラの線の見本（隠しておく。キャラクターごとに 2 本ずつ複製する）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("段階が上がった・当たった瞬間の光の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private BurstEffectView _burstTemplate;
        [Tooltip("段階が上がった・当たった瞬間の音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;

        private readonly List<Aura> _auras = new List<Aura>();
        private ISimulationFeed _feed;
        private Gradient _gradient;
        private GradientColorKey[] _colorKeys;
        private GradientAlphaKey[] _alphaKeys;
        private readonly Gradient _solid = new Gradient();
        private readonly GradientColorKey[] _solidColors = new GradientColorKey[2];
        private readonly GradientAlphaKey[] _solidAlphas = new GradientAlphaKey[2];

        private sealed class Aura
        {
            public LineRenderer Outer;
            public LineRenderer Inner;
            public LineRenderer[] Arrows;
            public int ArrowFacing;
            public BurstEffectView Burst;
            public int Level = -1;
            public bool Rainbow;
            public int Serial = -1;
            public bool HitShown;
            public int HitBursts;
        }

        /// <summary>キャラクター index に出しているオーラの段階（出していなければ -1。テスト用）。</summary>
        public int LevelOf(int index)
        {
            return index >= 0 && index < _auras.Count ? _auras[index].Level : -1;
        }

        /// <summary>キャラクター index のオーラが虹色か（最後の段階。テスト用）。</summary>
        public bool IsRainbow(int index)
        {
            return index >= 0 && index < _auras.Count && _auras[index].Level >= 0 && _auras[index].Rainbow;
        }

        /// <summary>キャラクター index のオーラの線の、真ん中の色（テスト用。虹色の後に白へ戻るか）。</summary>
        public Color AuraColorOf(int index)
        {
            return index >= 0 && index < _auras.Count ? _auras[index].Outer.colorGradient.Evaluate(0.5f) : Color.clear;
        }

        /// <summary>キャラクター index の向きの矢印を出していれば、その向き（1 か -1。出していなければ 0。テスト用）。</summary>
        public int ArrowFacingOf(int index)
        {
            return index >= 0 && index < _auras.Count ? _auras[index].ArrowFacing : 0;
        }

        /// <summary>キャラクター index のためた技が当たって、大きな光を出した回数（テスト用）。</summary>
        public int HitBurstsOf(int index)
        {
            return index >= 0 && index < _auras.Count ? _auras[index].HitBursts : 0;
        }

        /// <summary>
        /// state の、ためている（またはためて出した）技の段階（ため技でなければ -1）。ためている間は今の長さ、出した後は離したときの長さ。
        /// </summary>
        public static int ChargeLevelOf(in CharacterState state, CombatTuning combat, out AttackData attack)
        {
            attack = null;
            if (state.Combat.IsWaitingToRespawn)
            {
                return -1;
            }
            if (state.Combat.Holding)
            {
                attack = combat.Get(state.Combat.HoldSlot);
                return attack.ChargeLevelOf(state.Combat.HoldTicks);
            }
            if (state.Action.Kind == ActionKind.Attack)
            {
                attack = combat.Get(state.Action.Attack);
                return attack.HoldMaxTicks > 0 ? attack.ChargeLevelOf(state.Action.Charge) : -1;
            }
            return -1;
        }

        /// <summary>段階 level の色（最後の段階は虹色なので、ここでは白）。</summary>
        public static Color LevelColor(int level)
        {
            return LevelColors[Mathf.Clamp(level, 0, LevelColors.Length - 1)];
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _lineTemplate == null || _burstTemplate == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か、オーラの線・光の見本が設定されていません", this);
                enabled = false;
                return;
            }
            _lineTemplate.gameObject.SetActive(false);
            _burstTemplate.gameObject.SetActive(false);
            _gradient = new Gradient();
            _colorKeys = new GradientColorKey[7];
            _alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0.7f, 1f) };
        }

        private void LateUpdate()
        {
            int count = _feed.CharacterCount;
            while (_auras.Count < count)
            {
                _auras.Add(CreateAura(_auras.Count));
            }
            for (int i = 0; i < _auras.Count; i++)
            {
                Aura aura = _auras[i];
                if (i >= count)
                {
                    Hide(aura);
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                int level = ChargeLevelOf(state, _feed.GetConfig(i).Combat, out AttackData attack);
                if (level < 0)
                {
                    Hide(aura);
                    continue;
                }
                bool top = attack.IsTopChargeLevel(level);
                var center = new Vector3(state.Position.X, state.Position.Y, 0f);
                Color color = LevelColor(level);
                if (state.Combat.Holding && aura.Level >= 0 && level > aura.Level)
                {
                    // 段階が上がった。
                    aura.Burst.Configure(color, top, 0.3f, 0.3f, 1.8f + 0.25f * level, 8, 1.2f + 0.2f * level, 0.1f, string.Empty, 0, 0f);
                    aura.Burst.Play(center);
                    if (_audio != null)
                    {
                        _audio.Play(top ? SoundId.ChargeMax : SoundId.ChargeUp, 0.8f);
                    }
                }
                aura.Level = level;
                aura.Rainbow = top;
                UpdateHit(aura, state, center, color, level, top);
                Draw(aura, center, color, level, top);
                DrawArrows(aura, state, attack, center, color, top);
            }
        }

        // ためて出した技が当たった瞬間に、段階の色の大きな光を出す（攻撃 1 回に 1 回）。
        private void UpdateHit(Aura aura, in CharacterState state, Vector3 center, Color color, int level, bool top)
        {
            if (state.Action.Kind != ActionKind.Attack)
            {
                aura.Serial = -1;
                return;
            }
            if (aura.Serial != state.Combat.AttackSerial)
            {
                aura.Serial = state.Combat.AttackSerial;
                aura.HitShown = false;
            }
            if (!state.Action.Connected || aura.HitShown || level <= 0)
            {
                return;
            }
            aura.HitShown = true;
            aura.HitBursts++;
            Vector3 front = center + new Vector3(1.6f * state.Facing, 0f, 0f);
            aura.Burst.Configure(color, top, 0.45f + 0.05f * level, 0.4f, 2.5f + 0.7f * level, 10 + 2 * level, 2f + 0.4f * level,
                0.16f + 0.03f * level, string.Empty, 0, 0.05f * level);
            aura.Burst.Play(front);
            if (_audio != null)
            {
                _audio.Play(level >= 3 ? SoundId.Fatal : SoundId.HeavyHit, 0.8f);
            }
        }

        private void Draw(Aura aura, Vector3 center, Color color, int level, bool top)
        {
            float time = Time.time;
            float rx = 0.85f + 0.12f * level;
            float ry = 1.25f + 0.15f * level;
            float amp = 0.12f + 0.05f * level;
            DrawFlame(aura.Outer, center, rx, ry, amp, time, 0f);
            DrawFlame(aura.Inner, center, rx * 0.72f, ry * 0.75f, amp * 0.7f, time * 1.3f, 5.7f);
            aura.Outer.widthMultiplier = 0.1f + 0.03f * level;
            aura.Inner.widthMultiplier = 0.06f + 0.015f * level;
            if (top)
            {
                SetRainbow(aura.Outer, time, 0.75f);
                SetRainbow(aura.Inner, time + 0.5f, 0.9f);
            }
            else
            {
                SetColor(aura.Outer, color, 0.6f);
                SetColor(aura.Inner, Color.Lerp(color, Color.white, 0.5f), 0.85f);
            }
            aura.Outer.enabled = true;
            aura.Inner.enabled = true;
        }

        // ためている間、技が出る側（判定の位置）に、前へ流れる矢印を 2 つ出す。
        private void DrawArrows(Aura aura, in CharacterState state, AttackData attack, Vector3 center, Color color, bool top)
        {
            bool show = state.Combat.Holding;
            aura.ArrowFacing = show ? state.Facing : 0;
            float offset = attack.Hits.Length > 0 ? attack.Hits[0].OffsetX : 1.5f;
            float slide = Mathf.Repeat(Time.time * 2.5f, 1f) * 0.35f;
            for (int a = 0; a < aura.Arrows.Length; a++)
            {
                LineRenderer line = aura.Arrows[a];
                line.enabled = show;
                if (!show)
                {
                    continue;
                }
                float x = (offset - 0.3f + a * 0.55f + slide) * state.Facing;
                const float half = 0.55f;
                const float depth = 0.4f;
                line.positionCount = 3;
                line.SetPosition(0, center + new Vector3(x - depth * state.Facing, half, Z));
                line.SetPosition(1, center + new Vector3(x, 0f, Z));
                line.SetPosition(2, center + new Vector3(x - depth * state.Facing, -half, Z));
                float alpha = a == 0 ? 0.95f : 0.6f;
                if (top)
                {
                    SetRainbow(line, Time.time + a * 0.3f, alpha);
                }
                else
                {
                    SetColor(line, Color.Lerp(color, Color.white, 0.2f), alpha);
                }
            }
        }

        // 揺らめく輪（炎のように、上の側ほど大きく揺れて伸びる）。
        private static void DrawFlame(LineRenderer line, Vector3 center, float rx, float ry, float amp, float time, float salt)
        {
            line.positionCount = Points;
            for (int k = 0; k < Points; k++)
            {
                float angle = Mathf.PI * 2f * k / Points;
                float up = 0.5f + 0.5f * Mathf.Cos(angle);
                float noise = Mathf.PerlinNoise(angle * 1.7f + time * 3f + salt, time * 2.2f + salt);
                float r = 1f + amp * (noise - 0.3f) * (0.6f + up);
                float lift = amp * up * up * (0.6f + 0.4f * Mathf.Sin(time * 11f + angle * 5f + salt));
                line.SetPosition(k, center + new Vector3(Mathf.Sin(angle) * rx * r, Mathf.Cos(angle) * ry * r + lift, Z));
            }
        }

        // 単色にする（キーを 2 つに戻す。startColor だけを変えると、虹色のときの途中のキーが残ってしまう。2 回目のためで白に
        // 戻らなかった不具合。ユーザーの指摘、2026-10-16）。
        private void SetColor(LineRenderer line, Color color, float alpha)
        {
            _solidColors[0] = new GradientColorKey(color, 0f);
            _solidColors[1] = new GradientColorKey(color, 1f);
            _solidAlphas[0] = new GradientAlphaKey(alpha, 0f);
            _solidAlphas[1] = new GradientAlphaKey(alpha, 1f);
            _solid.SetKeys(_solidColors, _solidAlphas);
            line.colorGradient = _solid;
        }

        private void SetRainbow(LineRenderer line, float time, float alpha)
        {
            for (int k = 0; k < _colorKeys.Length; k++)
            {
                float t = k / (float)(_colorKeys.Length - 1);
                _colorKeys[k] = new GradientColorKey(Color.HSVToRGB(Mathf.Repeat(t + time * 0.8f, 1f), 0.7f, 1f), t);
            }
            _alphaKeys[0].alpha = alpha;
            _alphaKeys[1].alpha = alpha;
            _gradient.SetKeys(_colorKeys, _alphaKeys);
            line.colorGradient = _gradient;
        }

        private static void Hide(Aura aura)
        {
            aura.Level = -1;
            aura.Rainbow = false;
            aura.Serial = -1;
            aura.ArrowFacing = 0;
            if (aura.Outer.enabled)
            {
                aura.Outer.enabled = false;
                aura.Inner.enabled = false;
            }
            foreach (LineRenderer arrow in aura.Arrows)
            {
                arrow.enabled = false;
            }
        }

        private Aura CreateAura(int index)
        {
            var aura = new Aura
            {
                Outer = Instantiate(_lineTemplate, transform),
                Inner = Instantiate(_lineTemplate, transform),
                Arrows = new[] { Instantiate(_lineTemplate, transform), Instantiate(_lineTemplate, transform) },
                Burst = Instantiate(_burstTemplate, transform),
            };
            aura.Outer.name = $"Aura{index}Outer";
            aura.Inner.name = $"Aura{index}Inner";
            aura.Arrows[0].name = $"Aura{index}Arrow0";
            aura.Arrows[1].name = $"Aura{index}Arrow1";
            aura.Burst.name = $"Aura{index}Burst";
            foreach (LineRenderer line in new[] { aura.Outer, aura.Inner, aura.Arrows[0], aura.Arrows[1] })
            {
                line.gameObject.SetActive(true);
                line.useWorldSpace = true;
                line.loop = true;
                line.enabled = false;
            }
            foreach (LineRenderer arrow in aura.Arrows)
            {
                arrow.loop = false;
                arrow.widthMultiplier = 0.14f;
            }
            aura.Burst.gameObject.SetActive(true);
            return aura;
        }
    }
}
