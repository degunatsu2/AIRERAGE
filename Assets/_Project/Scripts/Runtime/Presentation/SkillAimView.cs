using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 照準の方向に出す技を長押しで狙っている間（AttackData.AimHold。CombatState.Holding）の、照準の方向への矢印（ユーザーの要望、2026-10-29。
    /// 見本は LoL のスキルショットの表示）。水色の半透明の太い帯の上に、先へ行くほど太く明るくなる芯と、細長い矢じり（塗りと縁）。長さは
    /// 技の届く距離（短すぎる・長すぎるものは MinLength〜MaxLength に収める）。地形に隠れないように、キャラクターの面（z = 0）の矢印と画面で
    /// 重なる位置・太さで、カメラの前（Depth。THIEF の煙より手前）に描く。
    /// 見せるのは、この PC で操作しているキャラクターだけ（ネットワーク対戦では相手の狙いは見えない）。ローカル対戦（操作している人が
    /// 決まっていない）では、CPU 以外の全員の分を出す。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class SkillAimView : MonoBehaviour
    {
        /// <summary>矢印の長さの下限と上限（m）。</summary>
        public const float MinLength = 2.5f;
        public const float MaxLength = 12f;

        /// <summary>
        /// 矢印の見た目（JUGGERNAUT のフラックの弾道の予測線も同じ見た目にする。ユーザーの要望、2026-10-29）: 帯の太さ、矢じりの根元の
        /// 半分の幅と長さの上限（m）、芯の太さ（根もと・先）、縁の太さ、色。
        /// </summary>
        public const float BandWidth = 0.7f;
        public const float HeadHalfWidth = 0.5f;
        public const float MaxHeadLength = 1.6f;
        public const float ShaftStartWidth = 0.06f;
        public const float ShaftEndWidth = 0.2f;
        public const float EdgeWidth = 0.07f;
        public static readonly Color Cyan = new Color(0.35f, 0.9f, 1f);

        private const float Depth = -4.5f;
        private const float StartOffset = 0.6f;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("線の見本（隠しておく。キャラクターごとに複製する）")]
        [SerializeField] private LineRenderer _lineTemplate;
        [Tooltip("矢印をカメラの前へ寄せるためのカメラ（なければ、キャラクターの面の少し手前に描く）")]
        [SerializeField] private Camera _camera;

        private readonly List<Arrow> _arrows = new List<Arrow>();
        private ISimulationFeed _feed;

        private sealed class Arrow
        {
            public LineRenderer Band;
            public LineRenderer Shaft;
            public LineRenderer Head;
            public LineRenderer Edges;
            public bool Shown;
            public Vector3 Tip;
        }

        /// <summary>キャラクター index の狙いの矢印を出しているか（テスト用）。</summary>
        public bool ShowsArrow(int index) => index >= 0 && index < _arrows.Count && _arrows[index].Shown;

        /// <summary>キャラクター index の矢印の先（キャラクターの面 z = 0 での位置。テスト用）。</summary>
        public Vector3 TipOf(int index) => index >= 0 && index < _arrows.Count ? _arrows[index].Tip : Vector3.zero;

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
        }

        private void LateUpdate()
        {
            int count = _feed.CharacterCount;
            while (_arrows.Count < count)
            {
                _arrows.Add(Create(_arrows.Count));
            }
            int local = _feed.LocalCharacter;
            for (int i = 0; i < _arrows.Count; i++)
            {
                Arrow arrow = _arrows[i];
                bool shown = false;
                if (i < count && (local < 0 ? !(_feed is ICpuRoster cpus && cpus.IsCpu(i)) : i == local))
                {
                    CharacterState state = _feed.GetState(i);
                    CharacterConfig config = _feed.GetConfig(i);
                    AttackData holding = state.Combat.Holding ? config.Combat.Get(state.Combat.HoldSlot) : null;
                    shown = holding != null && holding.AimHold && !state.Combat.IsWaitingToRespawn;
                    if (shown)
                    {
                        System.Numerics.Vector2 aim = SkillRules.AimDirection(state.Combat.SkillAim, state.Facing, config.Wire.AimDeadzone);
                        float offsetY = holding.Effect == SkillEffect.Missile ? config.Skills.Missile.OffsetY : 0f;
                        var origin = new Vector3(state.Position.X, state.Position.Y + offsetY, 0f);
                        Draw(arrow, origin, new Vector3(aim.X, aim.Y, 0f), ArrowLength(holding, config), _camera);
                    }
                }
                SetShown(arrow, shown);
            }
        }

        /// <summary>技 attack（調整値 config）の矢印の長さ（m）: 技の届く距離を MinLength〜MaxLength に収めたもの。</summary>
        public static float ArrowLength(AttackData attack, CharacterConfig config)
        {
            float reach;
            switch (attack.Effect)
            {
                case SkillEffect.TalentSnatch:
                    reach = config.Skills.Thief.SnatchRange;
                    break;
                case SkillEffect.WindZone:
                    reach = config.Skills.WindZone.Length;
                    break;
                case SkillEffect.Mine:
                    reach = config.Skills.Juggernaut.MineRange;
                    break;
                case SkillEffect.PlaceGimmick:
                    reach = config.Movement.HalfWidth + config.Skills.Thief.PlaceGap + 2f;
                    break;
                default:
                    // 照準の方向へ動く技は、その動きの距離。それ以外（遠くまで飛ぶ物など）は上限。
                    reach = 0f;
                    foreach (AttackMotion motion in attack.Motions)
                    {
                        if (motion.AlongAim)
                        {
                            reach += Mathf.Abs(motion.VelocityX) * motion.Ticks * Airerage.Core.Common.SimTime.DeltaTime;
                        }
                    }
                    if (reach <= 0f)
                    {
                        reach = MaxLength;
                    }
                    break;
            }
            return Mathf.Clamp(reach, MinLength, MaxLength);
        }

        /// <summary>矢印の脈打ち（明るさに掛ける。0.7〜1）。</summary>
        public static float Pulse => 0.85f + 0.15f * Mathf.Sin(Time.time * 8f);

        /// <summary>
        /// キャラクターの面（z = 0）で決めた点を、画面で同じ所に見えるまま、カメラの前（Depth。THIEF の煙や地形より手前）へ寄せる写し方。
        /// 線の太さは Scale を掛けると、キャラクターの面での太さと同じに見える。カメラがなければ、面の少し手前（fallbackZ）に置く。
        /// </summary>
        public readonly struct TowardCamera
        {
            private readonly Vector3 _eye;
            private readonly bool _projected;
            private readonly float _fallbackZ;

            public readonly float Scale;

            public TowardCamera(Camera camera, float fallbackZ = -0.7f)
            {
                _projected = camera != null && !camera.orthographic && camera.transform.position.z < Depth;
                _eye = _projected ? camera.transform.position : Vector3.zero;
                Scale = _projected ? (Depth - _eye.z) / (0f - _eye.z) : 1f;
                _fallbackZ = fallbackZ;
            }

            public Vector3 Place(Vector3 point)
            {
                return _projected ? _eye + (point - _eye) * Scale : new Vector3(point.x, point.y, _fallbackZ);
            }
        }

        // 太い帯・芯・矢じり（塗り）・矢じりの縁。少し脈打つ。点はキャラクターの面で決め、カメラの前へ寄せる（太さも同じ割合で細くする）。
        private static void Draw(Arrow arrow, Vector3 origin, Vector3 direction, float length, Camera camera)
        {
            float pulse = Pulse;
            var side = new Vector3(-direction.y, direction.x, 0f);
            Vector3 start = origin + direction * StartOffset;
            Vector3 tip = start + direction * length;
            float headLength = Mathf.Min(MaxHeadLength, length * 0.35f);
            Vector3 headBase = tip - direction * headLength;
            arrow.Tip = tip;

            var toward = new TowardCamera(camera);
            float scale = toward.Scale;
            Vector3 Place(Vector3 point) => toward.Place(point);

            SetLine(arrow.Band, Place(start), Place(headBase), scale);
            Paint(arrow.Band, BandStartAlpha * pulse, BandEndAlpha * pulse);

            SetLine(arrow.Shaft, Place(start), Place(headBase + direction * 0.05f), scale);
            Paint(arrow.Shaft, ShaftStartAlpha * pulse, ShaftEndAlpha * pulse);

            SetLine(arrow.Head, Place(headBase), Place(tip), scale);
            Paint(arrow.Head, HeadStartAlpha * pulse, HeadEndAlpha * pulse);

            // 矢じりの縁（後ろへ流れる細長い形。翼の先は帯より外）。
            arrow.Edges.positionCount = 3;
            arrow.Edges.SetPosition(0, Place(headBase + side * HeadHalfWidth - direction * 0.25f));
            arrow.Edges.SetPosition(1, Place(tip));
            arrow.Edges.SetPosition(2, Place(headBase - side * HeadHalfWidth - direction * 0.25f));
            arrow.Edges.widthMultiplier = scale;
            Paint(arrow.Edges, EdgeAlpha * pulse, EdgeAlpha * pulse);
        }

        private static void SetLine(LineRenderer line, Vector3 from, Vector3 to, float widthScale)
        {
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.widthMultiplier = widthScale;
        }

        /// <summary>帯・芯・矢じり・縁の濃さ（根もと・先。脈打ちを掛ける前）。</summary>
        public const float BandStartAlpha = 0.16f;
        public const float BandEndAlpha = 0.28f;
        public const float ShaftStartAlpha = 0.25f;
        public const float ShaftEndAlpha = 0.85f;
        public const float HeadStartAlpha = 0.45f;
        public const float HeadEndAlpha = 0.65f;
        public const float EdgeAlpha = 0.9f;

        /// <summary>線を水色で、根もとと先の濃さで塗る。</summary>
        public static void Paint(LineRenderer line, float startAlpha, float endAlpha)
        {
            line.startColor = new Color(Cyan.r, Cyan.g, Cyan.b, startAlpha);
            line.endColor = new Color(Cyan.r, Cyan.g, Cyan.b, endAlpha);
        }

        private static void SetShown(Arrow arrow, bool shown)
        {
            arrow.Shown = shown;
            arrow.Band.enabled = shown;
            arrow.Shaft.enabled = shown;
            arrow.Head.enabled = shown;
            arrow.Edges.enabled = shown;
        }

        private Arrow Create(int index)
        {
            // 帯は一定の太さ、芯は先ほど太く、矢じりは根元の幅から先で 0（塗った三角形）、縁は細い線。
            var arrow = new Arrow
            {
                Band = NewLine(index, "Band", AnimationCurve.Constant(0f, 1f, BandWidth)),
                Shaft = NewLine(index, "Shaft", new AnimationCurve(new Keyframe(0f, ShaftStartWidth), new Keyframe(1f, ShaftEndWidth))),
                Head = NewLine(index, "Head", new AnimationCurve(new Keyframe(0f, HeadHalfWidth * 2f), new Keyframe(1f, 0f))),
                Edges = NewLine(index, "Edges", AnimationCurve.Constant(0f, 1f, EdgeWidth)),
            };
            SetShown(arrow, false);
            return arrow;
        }

        private LineRenderer NewLine(int index, string lineName, AnimationCurve width)
        {
            LineRenderer line = Instantiate(_lineTemplate, transform);
            line.name = $"AimArrowP{index + 1}{lineName}";
            line.gameObject.SetActive(true);
            line.useWorldSpace = true;
            line.numCapVertices = 0;
            line.numCornerVertices = 2;
            line.widthCurve = width;
            line.widthMultiplier = 1f;
            return line;
        }
    }
}
