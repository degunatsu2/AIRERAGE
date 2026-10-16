using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Data;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 自分の特殊攻撃 2 つと必殺技の表示（docs/13_character_plan.md T5-4）。画面の下の中央に、斜めの札を 3 枚並べる
    /// （左から 特殊攻撃 1・特殊攻撃 2・必殺技。キー、技の名前か絵）。
    /// - 特殊攻撃: クールタイムの間は、たまった分だけ左から色がつき、残りの秒数を出す。使えるときは縁が明るい。
    /// - 必殺技: ゲージ（0〜100%）のたまった分だけ金色がつき、％を小数第 1 位まで出す。必殺技を出せる量（キャラクターごと）の位置に
    ///   虹色の線を引く。出せるようになったら縁が虹色に光って「READY!」（ユーザーの要望、2026-10-15）。
    /// - 使えるようになった瞬間は札が白く光り、小さな音を鳴らす（_audio があれば）。技を出している間は縁が白い。
    /// - 長押しの技をためている間は「HOLD」（セーフティボックスなら預ける枚数）、効果が続いている間（置いた金庫がある）は「ACTIVE」。
    /// - 使えるようになった瞬間は、自分のキャラクターの頭上にも小さな通知（技の名前。絵があれば絵）を出す（札を見なくても分かるように。
    ///   ユーザーの要望、2026-10-15）。この PC で操作しているキャラクターのものだけなので、ほかのプレイヤーには見えない。
    /// 技の絵は、キャラクターのアセット（CharacterConfigAsset）にあれば使う（まだない）。
    /// オフライン（SimulationRunner）とオンライン（OnlineRunner）のどちらでも使う（ISimulationFeed）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class SkillHud : MonoBehaviour
    {
        public const int SlotCount = 3;
        private const int SlotWidth = 118;
        private const int SlotHeight = 60;
        private const float SlotGap = 10f;
        private const float BottomMargin = 16f;
        private const int Slant = 14;
        private const int LineWidth = Slant + 6;
        private const float ReadyFlashSeconds = 0.35f;
        // スタックのゲージ（札の左の縁に沿った細い帯。ユーザーの要望、2026-10-16）。
        private const int StackWidth = 5;
        private const int StackSpacing = 4;
        private const int StackGap = 2;

        /// <summary>頭上の通知を出している長さ（秒）。</summary>
        public const float HeadNoticeSeconds = 1.4f;

        private static readonly AttackSlot[] Slots = { AttackSlot.Special1, AttackSlot.Special2, AttackSlot.Super };
        private static readonly string[] Keys = { "Q / LB", "E / RB", "R / R3" };
        private static readonly Color Fill = new Color(0.03f, 0.05f, 0.09f, 0.82f);
        private static readonly Color Idle = new Color(0.45f, 0.5f, 0.6f, 0.9f);
        private static readonly Color SpecialColor = new Color(0.35f, 0.85f, 1f);
        private static readonly Color SuperColor = new Color(1f, 0.78f, 0.2f);

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("使えるようになったときの音（なくてもよい）")]
        [SerializeField] private GameAudio _audio;
        [Tooltip("頭上の通知の位置を画面に写すカメラ（なければ頭上の通知を出さない）")]
        [SerializeField] private Camera _camera;

        private readonly Slot[] _slots = { new Slot(), new Slot(), new Slot() };
        private readonly List<HeadNotice> _notices = new List<HeadNotice>();
        private readonly Texture2D[] _partial = new Texture2D[SlotCount];
        private readonly int[] _partialWidth = { -1, -1, -1 };
        private readonly Texture2D[] _stack = new Texture2D[SlotCount];
        private readonly int[] _stackKey = { -1, -1, -1 };
        private Color32[] _pixels;
        private ISimulationFeed _feed;
        private Texture2D _fill;
        private Texture2D _frame;
        private Texture2D _line;
        private GUIStyle _key;
        private GUIStyle _name;
        private GUIStyle _number;
        private GUIStyle _notice;

        /// <summary>札 1 枚の今の表示（テスト用）。</summary>
        public sealed class Slot
        {
            /// <summary>表示しているか（操作するキャラクターがいるか）。</summary>
            public bool Shown;

            /// <summary>使えるか（特殊攻撃はクールタイムが 0、必殺技はゲージが足りる）。</summary>
            public bool Ready;

            /// <summary>技を出している間か。</summary>
            public bool InUse;

            /// <summary>色のついた割合（0〜1。特殊攻撃はクールタイムのたまった分、必殺技はゲージの 0〜100%）。</summary>
            public float Fill;

            /// <summary>虹色の線の位置（0〜1。必殺技を出せるゲージの量。線がなければ負）。</summary>
            public float Line = -1f;

            /// <summary>中央の文字（技の名前、残りの秒数、ゲージの％）。</summary>
            public string Text = string.Empty;

            /// <summary>下の小さな文字（技の名前、「READY!」。なければ空）。</summary>
            public string Label = string.Empty;

            /// <summary>使えるようになった回数（光った回数。最初から使えるときは数えない）。</summary>
            public int ReadyCount;

            public float ReadyAt = -10f;
            public string Name = string.Empty;

            /// <summary>技の絵（なければ null。文字で出す）。</summary>
            public Texture2D Icon;

            /// <summary>続けて使える回数（スタック）。2 以上なら札の左にスタックのゲージを出す（1 なら出さない）。</summary>
            public int MaxCharges = 1;

            /// <summary>たまっているスタック（使える回数 ＋ 次の 1 つのたまった割合。0〜MaxCharges）。</summary>
            public float Stacks;

            /// <summary>スタックのゲージのたまった割合（0〜1。2 スタックで 1 つたまっていれば 0.5）。</summary>
            public float StackFill => MaxCharges > 1 ? Mathf.Clamp01(Stacks / MaxCharges) : 0f;
        }

        /// <summary>頭上の通知 1 つ。</summary>
        public sealed class HeadNotice
        {
            public string Text;
            public Texture2D Icon;
            public Color Color;
            public float At;
        }

        /// <summary>左から 特殊攻撃 1・特殊攻撃 2・必殺技 の札の表示（テスト用）。</summary>
        public Slot SlotOf(int index) => _slots[index];

        /// <summary>今出している頭上の通知（古い順。テスト用）。</summary>
        public IReadOnlyList<HeadNotice> HeadNotices => _notices;

        /// <summary>これまでに出した頭上の通知の数（テスト用）。</summary>
        public int HeadNoticeCount { get; private set; }

        /// <summary>ゲージ（0.1% 単位）を「64.3%」の形にする（小数第 1 位まで。ユーザーの要望、2026-10-15）。</summary>
        public static string PercentText(int gauge)
        {
            int value = Mathf.Max(0, gauge);
            int per = CombatTuning.GaugePerPercent;
            return $"{value / per}.{value % per * 10 / per}%";
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in new[] { _fill, _frame, _line, _partial[0], _partial[1], _partial[2], _stack[0], _stack[1], _stack[2] })
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        private void Update()
        {
            int local = _feed.LocalCharacter;
            bool shown = local >= 0 && local < _feed.CharacterCount;
            for (int i = 0; i < SlotCount; i++)
            {
                Slot slot = _slots[i];
                if (!shown)
                {
                    slot.Shown = false;
                    continue;
                }
                CharacterState state = _feed.GetState(local);
                CombatTuning combat = _feed.GetConfig(local).Combat;
                CharacterConfigAsset character = _feed.CharacterOf(local);
                AttackSlot move = Slots[i];
                // 効果中にもう一度出せる技（ウインドクローク）は、効果中は「使える」として知らせない（札に GUST と出す）。
                bool recast = move != AttackSlot.Super && ActionController.CanRecast(state, move, combat);
                bool ready = ActionController.CanUse(state, move, combat) && !recast;
                string moveName = combat.Get(move).Name;
                slot.Name = string.IsNullOrEmpty(moveName) ? (move == AttackSlot.Super ? "SUPER" : $"SPECIAL {i + 1}") : moveName;
                slot.Icon = character != null ? character.IconOf(move) : null;
                if (slot.Shown && ready && !slot.Ready)
                {
                    slot.ReadyCount++;
                    slot.ReadyAt = Time.time;
                    if (_audio != null)
                    {
                        _audio.Play(SoundId.SkillReady, 0.6f);
                    }
                    AddHeadNotice(slot, move == AttackSlot.Super ? SuperColor : SpecialColor);
                }
                slot.Shown = true;
                slot.Ready = ready;
                slot.InUse = state.Action.Kind == ActionKind.Attack && state.Action.Attack == move;
                slot.MaxCharges = move == AttackSlot.Super ? 1 : combat.MaxChargesOf(move);
                slot.Stacks = 0f;
                if (move == AttackSlot.Super)
                {
                    slot.Fill = Mathf.Clamp01(state.Combat.Gauge / (float)CombatTuning.MaxGauge);
                    slot.Line = Mathf.Clamp01(combat.SuperGaugeCost / (float)CombatTuning.MaxGauge);
                    slot.Text = PercentText(state.Combat.Gauge);
                    // コズミックレイは、ゲージがたまっていても自分のポータルが 2 つないと出せない。
                    bool needsPortals = combat.Super.Effect == Airerage.Core.Skills.SkillEffect.CosmicRay
                        && state.Combat.Gauge >= combat.SuperGaugeCost && state.Combat.PortalCount < 2;
                    slot.Label = ready ? "READY!" : needsPortals ? "PORTAL x2" : slot.Name;
                }
                else
                {
                    int total = combat.CooldownTicksOf(move);
                    int left = move == AttackSlot.Special1 ? state.Combat.Special1CooldownTicksLeft : state.Combat.Special2CooldownTicksLeft;
                    slot.Line = -1f;
                    if (state.Combat.Holding && state.Combat.HoldSlot == move)
                    {
                        // 長押しでためている（セーフティボックスなら、預ける枚数）。
                        int deposit = HoldDepositHud.DepositOf(state, _feed.GetConfig(local));
                        int max = Mathf.Max(1, combat.Get(move).HoldMaxTicks);
                        slot.Fill = Mathf.Clamp01(state.Combat.HoldTicks / (float)max);
                        bool aiming = combat.Get(move).Effect == Airerage.Core.Skills.SkillEffect.Missile;
                        slot.Text = deposit >= 0 ? $"-{deposit}" : aiming ? "AIM" : "HOLD";
                        slot.Label = "HOLD";
                    }
                    else if (left == CombatState.CooldownWhileActive)
                    {
                        // 技の効果が続いている（セーフティボックスを置いている）。クールタイムは効果が終わってから。
                        slot.Fill = 1f;
                        slot.Text = recast ? "GUST" : "ACTIVE";
                        slot.Label = recast ? "ACTIVE" : slot.Name;
                    }
                    else
                    {
                        slot.Fill = total <= 0 ? 1f : 1f - Mathf.Clamp01(left / (float)total);
                        slot.Text = ready ? slot.Name : (left * SimTime.DeltaTime).ToString("0.0");
                        slot.Label = ready ? string.Empty : slot.Name;
                    }
                    int charges = slot.MaxCharges;
                    if (charges > 1)
                    {
                        // 続けて使える技（ワープポータル）: 残りの回数と、次が戻るまで。ゲージは 使える回数 ＋ 次の 1 つのたまった割合。
                        int spent = move == AttackSlot.Special1 ? state.Combat.Special1Spent : state.Combat.Special2Spent;
                        float next = spent > 0 && total > 0 && left > 0 ? 1f - Mathf.Clamp01(left / (float)total) : 0f;
                        slot.Stacks = Mathf.Clamp(charges - spent + next, 0f, charges);
                        if (left != CombatState.CooldownWhileActive && !(state.Combat.Holding && state.Combat.HoldSlot == move))
                        {
                            slot.Label = left > 0 && ready ? $"x{charges - spent}  {left * SimTime.DeltaTime:0.0}" : $"x{charges - spent}";
                        }
                    }
                }
            }
            _notices.RemoveAll(n => Time.time - n.At >= HeadNoticeSeconds);
        }

        private void AddHeadNotice(Slot slot, Color color)
        {
            _notices.Add(new HeadNotice { Text = slot.Name, Icon = slot.Icon, Color = color, At = Time.time });
            HeadNoticeCount++;
        }

        private void OnGUI()
        {
            if (!_slots[0].Shown)
            {
                return;
            }
            EnsureResources();
            float total = SlotCount * SlotWidth + (SlotCount - 1) * SlotGap;
            float x = (Screen.width - total) * 0.5f;
            float y = Screen.height - BottomMargin - SlotHeight;
            for (int i = 0; i < SlotCount; i++)
            {
                DrawSlot(_slots[i], i, new Rect(x + i * (SlotWidth + SlotGap), y, SlotWidth, SlotHeight));
            }
            DrawHeadNotices();
        }

        private void DrawSlot(Slot slot, int index, Rect rect)
        {
            bool super = Slots[index] == AttackSlot.Super;
            Color accent = super ? SuperColor : SpecialColor;
            Color previous = GUI.color;
            GUI.color = Fill;
            GUI.DrawTexture(rect, _fill);

            // たまった分（左から。縁と同じ傾きで切る）。使えるときは薄く、たまっている途中は少し濃く。
            if (slot.Fill > 0f)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, slot.Ready ? 0.28f : 0.45f);
                GUI.DrawTexture(rect, PartialFill(index, slot.Fill));
            }

            float flash = 1f - (Time.time - slot.ReadyAt) / ReadyFlashSeconds;
            if (flash > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f * flash);
                GUI.DrawTexture(rect, _fill);
            }

            // 必殺技を出せるゲージの量の位置に、縁と同じ傾きの虹色の線。
            if (slot.Line >= 0f)
            {
                float lineX = rect.x + slot.Line * (SlotWidth - Slant) - (LineWidth - Slant) * 0.5f;
                GUI.color = Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f, 1f), 0.7f, 1f);
                GUI.DrawTexture(new Rect(lineX, rect.y, LineWidth, rect.height), _line);
            }

            if (slot.InUse)
            {
                GUI.color = Color.white;
            }
            else if (super && slot.Ready)
            {
                GUI.color = Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f, 1f), 0.6f, 1f);
            }
            else
            {
                GUI.color = slot.Ready ? accent : Idle;
            }
            GUI.DrawTexture(rect, _frame);
            if (slot.MaxCharges > 1)
            {
                // 札の左の縁に沿った、スタックの数だけ縦に分けたゲージ（下からたまる）。
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(rect.x - StackSpacing - StackWidth, rect.y, StackWidth + Slant + 1, SlotHeight),
                    StackGauge(index, slot.MaxCharges, slot.Stacks, accent));
            }
            GUI.color = previous;

            GuidePlate.DrawText(new Rect(rect.x + Slant + 6f, rect.y + 3f, rect.width - Slant - 10f, 16f), Keys[index], _key,
                new Color(1f, 1f, 1f, 0.75f));
            var center = new Rect(rect.x + 4f, rect.y + 18f, rect.width - 8f, 28f);
            bool iconShown = slot.Icon != null && slot.Ready && !super;
            if (iconShown)
            {
                GUI.DrawTexture(new Rect(center.center.x - 14f, center.center.y - 14f, 28f, 28f), slot.Icon, ScaleMode.ScaleToFit);
            }
            else
            {
                bool bold = slot.Ready && !super;
                GuidePlate.DrawText(center, slot.Text, bold ? _name : _number, slot.Ready ? Color.white : new Color(1f, 1f, 1f, 0.85f));
            }
            if (!string.IsNullOrEmpty(slot.Label))
            {
                Color labelColor = super && slot.Ready
                    ? Color.HSVToRGB(Mathf.Repeat(Time.time / 1.2f + 0.3f, 1f), 0.45f, 1f)
                    : new Color(1f, 1f, 1f, 0.55f);
                GuidePlate.DrawText(new Rect(rect.x + 2f, rect.y + rect.height - 17f, rect.width - Slant - 8f, 14f), slot.Label, _key, labelColor);
            }
        }

        // 頭上の通知: 自分のキャラクターの頭の上に、小さな札（技の名前か絵）が浮かび上がって消える。重なったら上へ積む。
        private void DrawHeadNotices()
        {
            int local = _feed.LocalCharacter;
            if (_notices.Count == 0 || _camera == null || local < 0 || local >= _feed.CharacterCount)
            {
                return;
            }
            CharacterState state = _feed.GetState(local);
            float top = state.Position.Y + _feed.GetConfig(local).Movement.HalfHeight + 0.5f;
            Vector3 p = _camera.WorldToScreenPoint(new Vector3(state.Position.X, top, 0f));
            if (p.z <= 0f)
            {
                return;
            }
            Color previous = GUI.color;
            for (int i = 0; i < _notices.Count; i++)
            {
                HeadNotice notice = _notices[i];
                float t = (Time.time - notice.At) / HeadNoticeSeconds;
                float alpha = t < 0.12f ? t / 0.12f : t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
                float rise = Mathf.Min(t / 0.25f, 1f) * 14f;
                float width = notice.Icon != null ? 30f : 16f + notice.Text.Length * 9f;
                const float height = 24f;
                float y = Screen.height - p.y - height - rise - (_notices.Count - 1 - i) * (height + 4f);
                var rect = new Rect(p.x - width * 0.5f, y, width, height);
                GUI.color = new Color(0.03f, 0.05f, 0.09f, 0.8f * alpha);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = new Color(notice.Color.r, notice.Color.g, notice.Color.b, alpha);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), Texture2D.whiteTexture);
                if (notice.Icon != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.DrawTexture(new Rect(rect.x + 3f, rect.y + 1f, 24f, 20f), notice.Icon, ScaleMode.ScaleToFit);
                }
                else
                {
                    GuidePlate.DrawText(rect, notice.Text, _notice, new Color(1f, 1f, 1f, alpha));
                }
            }
            GUI.color = previous;
        }

        // たまった分の形（札の形のうち、左から fraction の割合まで。縁と同じ傾きで切る）。幅が変わったときだけ作り直す。
        private Texture2D PartialFill(int index, float fraction)
        {
            int width = Mathf.RoundToInt(fraction * (SlotWidth - Slant));
            if (_partial[index] != null && _partialWidth[index] == width)
            {
                return _partial[index];
            }
            if (_partial[index] == null)
            {
                _partial[index] = TimerPlate.Create($"SkillSlotPartial{index}", SlotWidth, SlotHeight, (x, y) => 0f);
            }
            _pixels = _pixels ?? new Color32[SlotWidth * SlotHeight];
            for (int yFromTop = 0; yFromTop < SlotHeight; yFromTop++)
            {
                int row = (SlotHeight - 1 - yFromTop) * SlotWidth;
                float left = Slant * (1f - (yFromTop + 0.5f) / SlotHeight);
                for (int x = 0; x < SlotWidth; x++)
                {
                    bool inside = InsideSlot(x, yFromTop, 0f) && x + 0.5f <= left + width;
                    _pixels[row + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }
            _partial[index].SetPixels32(_pixels);
            _partial[index].Apply();
            _partialWidth[index] = width;
            return _partial[index];
        }

        // スタックのゲージの形（札の左の縁と同じ傾きの細い帯を、スタックの数だけ縦に分ける）。たまった区切りは明るく、たまっている途中の
        // 区切りは途中まで薄く、空の区切りは暗く。見た目が変わったときだけ作り直す。
        private Texture2D StackGauge(int index, int maxCharges, float stacks, Color accent)
        {
            int full = Mathf.FloorToInt(stacks + 1e-4f);
            float segment = (SlotHeight - StackGap * (maxCharges - 1)) / (float)maxCharges;
            int partialRows = full < maxCharges ? Mathf.RoundToInt((stacks - full) * segment) : 0;
            int key = (maxCharges * 16 + full) * 256 + partialRows;
            if (_stack[index] != null && _stackKey[index] == key)
            {
                return _stack[index];
            }
            int width = StackWidth + Slant + 1;
            if (_stack[index] == null)
            {
                _stack[index] = TimerPlate.Create($"SkillSlotStack{index}", width, SlotHeight, (x, y) => 0f);
            }
            var lit = (Color32)new Color(accent.r, accent.g, accent.b, 1f);
            var charging = (Color32)new Color(accent.r, accent.g, accent.b, 0.5f);
            var empty = (Color32)new Color(Fill.r, Fill.g, Fill.b, 0.85f);
            var none = new Color32(0, 0, 0, 0);
            var pixels = new Color32[width * SlotHeight];
            for (int yFromTop = 0; yFromTop < SlotHeight; yFromTop++)
            {
                int row = (SlotHeight - 1 - yFromTop) * width;
                float fromBottom = SlotHeight - (yFromTop + 0.5f);
                int k = Mathf.FloorToInt(fromBottom / (segment + StackGap));
                float inSegment = fromBottom - k * (segment + StackGap);
                bool gap = inSegment > segment || k >= maxCharges;
                Color32 color = gap ? none : k < full ? lit : k == full && inSegment <= partialRows ? charging : empty;
                float left = Slant * (1f - (yFromTop + 0.5f) / SlotHeight);
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f;
                    pixels[row + x] = px >= left && px <= left + StackWidth ? color : none;
                }
            }
            _stack[index].SetPixels32(pixels);
            _stack[index].Apply();
            _stackKey[index] = key;
            return _stack[index];
        }

        // 札の形（右へ傾いた平行四辺形。上の辺が Slant だけ右）の中か。inset は内側へ縮める量（ピクセル）。
        private static bool InsideSlot(int x, int yFromTop, float inset)
        {
            float px = x + 0.5f;
            float py = yFromTop + 0.5f;
            if (py < inset || py > SlotHeight - inset)
            {
                return false;
            }
            float left = Slant * (1f - py / SlotHeight);
            return px >= left + inset * 1.2f && px <= left + SlotWidth - Slant - inset * 1.2f;
        }

        private void EnsureResources()
        {
            if (_fill != null)
            {
                return;
            }
            _fill = TimerPlate.Create("SkillSlotFill", SlotWidth, SlotHeight, (x, y) => InsideSlot(x, y, 0f) ? 1f : 0f);
            _frame = TimerPlate.Create("SkillSlotFrame", SlotWidth, SlotHeight,
                (x, y) => InsideSlot(x, y, 0f) && !InsideSlot(x, y, 2.5f) ? 1f : 0f);
            // 縁と同じ傾きの細い線（幅 LineWidth の中で、上が右、下が左）。
            _line = TimerPlate.Create("SkillSlotLine", LineWidth, SlotHeight, (x, y) =>
            {
                float center = (LineWidth - Slant) * 0.5f + Slant * (1f - (y + 0.5f) / SlotHeight);
                return Mathf.Abs(x + 0.5f - center) <= 1.6f ? 1f : 0f;
            });
            _key = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _name = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _number = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _notice = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            foreach (GUIStyle style in new[] { _key, _name, _number, _notice })
            {
                style.normal.textColor = Color.white;
            }
        }
    }
}
