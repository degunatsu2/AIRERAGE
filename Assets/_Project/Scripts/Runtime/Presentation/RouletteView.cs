using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ルーレットの技（AttackData.Roulette。RISK TAKER のルーレットスマッシュ）の、頭上のルーレット（ユーザーの要望、2026-10-16）。
    /// 技の発生の間は高速で回り、攻撃が出た（最初の判定のティックになった）瞬間に止まって、その目（×0.5〜×2）を示す。目がそのまま
    /// 吹き飛びと威力の倍率になる（AttackPower）。最大（×2）は JACKPOT。目は HitResolver.RouletteRoll（試合の種、攻撃した人、攻撃の
    /// 番号）で決まり、シミュレーションと同じ。止まった後は、技が終わってから LingerSeconds 秒まで残す。
    /// 見た目の動きは表示の時間で決める。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class RouletteView : MonoBehaviour
    {
        private const float SpinDegreesPerSecond = 1440f;
        private const float StopSeconds = 0.18f;
        private const float LingerSeconds = 0.7f;
        private const float WheelSize = 104f;
        private const int TextureSize = 128;

        // 目の色（×0.5、×0.8、×1、×1.2、×1.5、×2）。最大は金色。
        private static readonly Color[] SegmentColors =
        {
            new Color(0.25f, 0.27f, 0.32f),
            new Color(0.75f, 0.1f, 0.12f),
            new Color(0.08f, 0.08f, 0.1f),
            new Color(0.75f, 0.1f, 0.12f),
            new Color(0.08f, 0.08f, 0.1f),
            new Color(1f, 0.78f, 0.15f),
        };

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("ルーレットの位置を画面に写すカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("止まったときの音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;

        private readonly List<Wheel> _wheels = new List<Wheel>();
        private ISimulationFeed _feed;
        private Texture2D _texture;
        private GUIStyle _numberStyle;
        private GUIStyle _resultStyle;
        private GUIStyle _pointerStyle;

        private sealed class Wheel
        {
            public bool Active;
            public int Serial;
            public bool Stopped;
            public int Roll;
            public float Angle;
            public float StopFrom;
            public float StopTo;
            public float StoppedAt;
            public float LastSeenAt;
            public Vector3 Anchor;
        }

        /// <summary>キャラクター index の頭上でルーレットが回っているか（テスト用）。</summary>
        public bool IsSpinning(int index)
        {
            return index >= 0 && index < _wheels.Count && _wheels[index].Active && !_wheels[index].Stopped;
        }

        /// <summary>キャラクター index のルーレットが止まって目を出しているか（テスト用）。</summary>
        public bool IsStopped(int index)
        {
            return index >= 0 && index < _wheels.Count && _wheels[index].Active && _wheels[index].Stopped;
        }

        /// <summary>キャラクター index のルーレットの目（AttackData.RouletteScales の番号。出していなければ -1。テスト用）。</summary>
        public int RollOf(int index)
        {
            return IsStopped(index) ? _wheels[index].Roll : -1;
        }

        /// <summary>目 roll の文字（「×1.5」など）。</summary>
        public static string LabelOf(int roll)
        {
            return "×" + AttackData.RouletteScales[roll].ToString("0.#");
        }

        /// <summary>目 roll が最大（JACKPOT）か。</summary>
        public static bool IsJackpot(int roll)
        {
            return roll == AttackData.RouletteScales.Length - 1;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _camera == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）かカメラが設定されていません", this);
                enabled = false;
                return;
            }
            _texture = CreateWheelTexture();
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                Destroy(_texture);
            }
        }

        private void LateUpdate()
        {
            int count = _feed.CharacterCount;
            while (_wheels.Count < count)
            {
                _wheels.Add(new Wheel());
            }
            int seed = _feed.Rules != null ? _feed.Rules.Seed : 0;
            float now = Time.time;
            for (int i = 0; i < _wheels.Count; i++)
            {
                Wheel wheel = _wheels[i];
                if (i >= count)
                {
                    wheel.Active = false;
                    continue;
                }
                CharacterState state = _feed.GetState(i);
                CharacterConfig config = _feed.GetConfig(i);
                AttackData attack = state.Action.Kind == ActionKind.Attack ? config.Combat.Get(state.Action.Attack) : null;
                if (attack != null && attack.Roulette && !state.Combat.IsWaitingToRespawn)
                {
                    if (!wheel.Active || wheel.Serial != state.Combat.AttackSerial)
                    {
                        wheel.Active = true;
                        wheel.Serial = state.Combat.AttackSerial;
                        wheel.Stopped = false;
                        wheel.Roll = HitResolver.RouletteRoll(seed, i, wheel.Serial);
                        wheel.Angle = Mathf.Repeat(wheel.Serial * 97f, 360f);
                    }
                    if (!wheel.Stopped && state.Action.Ticks >= attack.FirstHitTick)
                    {
                        Stop(wheel);
                    }
                    wheel.LastSeenAt = now;
                    wheel.Anchor = new Vector3(state.Position.X, state.Position.Y + config.Movement.HalfHeight + 1.6f, 0f);
                }
                else if (wheel.Active && (!wheel.Stopped || now - wheel.LastSeenAt > LingerSeconds))
                {
                    // 止まる前に技が終わった（攻撃を受けたなど）か、止まった目を出し終えた。
                    wheel.Active = false;
                }

                if (wheel.Active && !wheel.Stopped)
                {
                    wheel.Angle = Mathf.Repeat(wheel.Angle + SpinDegreesPerSecond * Time.deltaTime, 360f);
                }
                else if (wheel.Active)
                {
                    float t = Mathf.Clamp01((now - wheel.StoppedAt) / StopSeconds);
                    float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                    wheel.Angle = Mathf.Lerp(wheel.StopFrom, wheel.StopTo, eased);
                }
            }
        }

        // 目の真ん中が上（針）に来る角度まで、少し回ってから止める。
        private void Stop(Wheel wheel)
        {
            wheel.Stopped = true;
            wheel.StoppedAt = Time.time;
            wheel.StopFrom = wheel.Angle;
            float segment = 360f / AttackData.RouletteScales.Length;
            float center = wheel.Roll * segment + segment * 0.5f;
            float start = wheel.StopFrom + 200f;
            float turns = Mathf.Ceil((start + center) / 360f);
            wheel.StopTo = turns * 360f - center;
            if (_audio != null)
            {
                _audio.Play(SoundId.RouletteStop);
                if (IsJackpot(wheel.Roll))
                {
                    _audio.Play(SoundId.ChargeMax, 0.8f);
                }
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            EnsureStyles();
            foreach (Wheel wheel in _wheels)
            {
                if (wheel.Active)
                {
                    DrawWheel(wheel);
                }
            }
        }

        private void DrawWheel(Wheel wheel)
        {
            Vector3 p = _camera.WorldToScreenPoint(wheel.Anchor);
            if (p.z <= 0f)
            {
                return;
            }
            var center = new Vector2(p.x, Screen.height - p.y);
            float size = WheelSize;
            float pop = wheel.Stopped ? 1f + 0.12f * Mathf.Clamp01(1f - (Time.time - wheel.StoppedAt - StopSeconds) / 0.25f) : 1f;
            size *= pop;
            var rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(wheel.Angle, center);
            GUI.DrawTexture(rect, _texture);
            GUI.matrix = saved;

            int segments = AttackData.RouletteScales.Length;
            float segment = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i * segment + segment * 0.5f + wheel.Angle) * Mathf.Deg2Rad;
                var at = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * size * 0.32f;
                bool picked = wheel.Stopped && i == wheel.Roll;
                _numberStyle.normal.textColor = IsJackpot(i) ? new Color(0.25f, 0.12f, 0f) : picked ? new Color(1f, 0.95f, 0.5f) : Color.white;
                GUI.Label(new Rect(at.x - 22f, at.y - 10f, 44f, 20f), LabelOf(i), _numberStyle);
            }

            // 針（上）。
            GUI.color = Color.white;
            GUI.Label(new Rect(center.x - 15f, rect.y - 22f, 30f, 26f), "▼", _pointerStyle);

            if (!wheel.Stopped || Time.time - wheel.StoppedAt < StopSeconds)
            {
                return;
            }
            // 止まった目を大きく。最大は虹色の JACKPOT!。
            bool jackpot = IsJackpot(wheel.Roll);
            Color color = jackpot ? Color.HSVToRGB(Mathf.Repeat(Time.time * 1.2f, 1f), 0.6f, 1f) : new Color(1f, 0.92f, 0.45f);
            var result = new Rect(center.x - 120f, rect.yMax + 2f, 240f, 40f);
            // 出た瞬間は白く光り、すぐに本来の色へ（TextFlash）。
            GuidePlate.DrawText(result, jackpot ? LabelOf(wheel.Roll) + " JACKPOT!" : LabelOf(wheel.Roll), _resultStyle,
                TextFlash.Apply(color, Time.time - wheel.StoppedAt - StopSeconds));
        }

        private void EnsureStyles()
        {
            if (_numberStyle != null)
            {
                return;
            }
            _numberStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _resultStyle = TextFlash.Style(28, FontStyle.Bold, TextAnchor.MiddleCenter);
            _pointerStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _pointerStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
        }

        // 目ごとに塗り分けた円盤（外側は金の縁、真ん中は小さな軸）。上から時計回りに目の番号の順。
        private static Texture2D CreateWheelTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[TextureSize * TextureSize];
            float half = TextureSize * 0.5f;
            int segments = AttackData.RouletteScales.Length;
            var rim = new Color(1f, 0.82f, 0.3f);
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / half;
                    float degrees = Mathf.Repeat(Mathf.Atan2(dx, dy) * Mathf.Rad2Deg, 360f);
                    float inSegment = Mathf.Repeat(degrees, 360f / segments);
                    Color color = SegmentColors[Mathf.Min(segments - 1, (int)(degrees / (360f / segments)))];
                    if (inSegment < 1.5f || r > 0.88f)
                    {
                        color = rim;
                    }
                    if (r < 0.14f)
                    {
                        color = rim;
                    }
                    color.a = Mathf.Clamp01((1f - r) * half);
                    pixels[y * TextureSize + x] = color;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
