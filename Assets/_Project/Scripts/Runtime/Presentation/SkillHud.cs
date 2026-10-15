using Airerage.Core.Combat;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using Airerage.Runtime.Audio;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 自分の特殊攻撃 2 つと必殺技の表示（docs/13_character_plan.md T5-4）。画面の下の中央に、斜めの札を 3 枚並べる
    /// （左から 特殊攻撃 1・特殊攻撃 2・必殺技。キー、技の名前）。
    /// - 特殊攻撃: クールタイムの間は、たまった分だけ左から色がつき、残りの秒数を出す。使えるときは縁が明るい。
    /// - 必殺技: ゲージのたまった分だけ金色がつき、ゲージの数を出す。たまったら縁が虹色に光って「READY!」。
    /// - 使えるようになった瞬間は札が白く光り、小さな音を鳴らす（_audio があれば）。技を出している間は縁が白い。
    /// オフライン（SimulationRunner）とオンライン（OnlineRunner）のどちらでも使う（ISimulationFeed）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class SkillHud : MonoBehaviour
    {
        public const int SlotCount = 3;
        private const int SlotWidth = 118;
        private const int SlotHeight = 60;
        private const float SlotGap = 10f;
        private const float BottomMargin = 16f;
        private const float Slant = 14f;
        private const float ReadyFlashSeconds = 0.35f;

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

        private readonly Slot[] _slots = { new Slot(), new Slot(), new Slot() };
        private ISimulationFeed _feed;
        private Texture2D _fill;
        private Texture2D _frame;
        private GUIStyle _key;
        private GUIStyle _name;
        private GUIStyle _number;

        /// <summary>札 1 枚の今の表示（テスト用）。</summary>
        public sealed class Slot
        {
            /// <summary>表示しているか（操作するキャラクターがいるか）。</summary>
            public bool Shown;

            /// <summary>使えるか（特殊攻撃はクールタイムが 0、必殺技はゲージが足りる）。</summary>
            public bool Ready;

            /// <summary>技を出している間か。</summary>
            public bool InUse;

            /// <summary>色のついた割合（0〜1。特殊攻撃はクールタイムのたまった分、必殺技はゲージ）。</summary>
            public float Fill;

            /// <summary>中央の文字（技の名前、残りの秒数、ゲージの数、「READY!」）。</summary>
            public string Text = string.Empty;

            /// <summary>使えるようになった回数（光った回数。最初から使えるときは数えない）。</summary>
            public int ReadyCount;

            public float ReadyAt = -10f;
            public string Name = string.Empty;
        }

        /// <summary>左から 特殊攻撃 1・特殊攻撃 2・必殺技 の札の表示（テスト用）。</summary>
        public Slot SlotOf(int index) => _slots[index];

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
            if (_fill != null)
            {
                Destroy(_fill);
                Destroy(_frame);
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
                AttackSlot move = Slots[i];
                bool ready = ActionController.CanUse(state, move, combat);
                if (slot.Shown && ready && !slot.Ready)
                {
                    slot.ReadyCount++;
                    slot.ReadyAt = Time.time;
                    if (_audio != null)
                    {
                        _audio.Play(SoundId.SkillReady, 0.6f);
                    }
                }
                slot.Shown = true;
                slot.Ready = ready;
                slot.InUse = state.Action.Kind == ActionKind.Attack && state.Action.Attack == move;
                string moveName = combat.Get(move).Name;
                slot.Name = string.IsNullOrEmpty(moveName) ? (move == AttackSlot.Super ? "SUPER" : $"SPECIAL {i + 1}") : moveName;
                if (move == AttackSlot.Super)
                {
                    slot.Fill = Mathf.Clamp01(state.Combat.Gauge / (float)combat.SuperGaugeCost);
                    slot.Text = ready ? "READY!" : state.Combat.Gauge.ToString();
                }
                else
                {
                    int total = combat.CooldownTicksOf(move);
                    int left = move == AttackSlot.Special1 ? state.Combat.Special1CooldownTicksLeft : state.Combat.Special2CooldownTicksLeft;
                    slot.Fill = total <= 0 ? 1f : 1f - Mathf.Clamp01(left / (float)total);
                    slot.Text = ready ? slot.Name : (left * SimTime.DeltaTime).ToString("0.0");
                }
            }
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
        }

        private void DrawSlot(Slot slot, int index, Rect rect)
        {
            bool super = Slots[index] == AttackSlot.Super;
            Color accent = super ? SuperColor : SpecialColor;
            Color previous = GUI.color;
            GUI.color = Fill;
            GUI.DrawTexture(rect, _fill);

            // たまった分（左から）。使えるときは薄く、たまっている途中は少し濃く。
            if (slot.Fill > 0f)
            {
                GUI.BeginGroup(new Rect(rect.x, rect.y, rect.width * slot.Fill, rect.height));
                GUI.color = new Color(accent.r, accent.g, accent.b, slot.Ready ? 0.28f : 0.45f);
                GUI.DrawTexture(new Rect(0f, 0f, rect.width, rect.height), _fill);
                GUI.EndGroup();
            }

            float flash = 1f - (Time.time - slot.ReadyAt) / ReadyFlashSeconds;
            if (flash > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.7f * flash);
                GUI.DrawTexture(rect, _fill);
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
            GUI.color = previous;

            GuidePlate.DrawText(new Rect(rect.x + Slant + 6f, rect.y + 3f, rect.width - Slant - 10f, 16f), Keys[index], _key,
                new Color(1f, 1f, 1f, 0.75f));
            GuidePlate.DrawText(new Rect(rect.x + 4f, rect.y + 18f, rect.width - 8f, 28f), slot.Text, slot.Ready ? _name : _number,
                slot.Ready ? Color.white : new Color(1f, 1f, 1f, 0.85f));
            if (!slot.Ready || super)
            {
                GuidePlate.DrawText(new Rect(rect.x + 2f, rect.y + rect.height - 17f, rect.width - Slant - 8f, 14f),
                    super && slot.Ready ? slot.Name : (super ? "GAUGE" : slot.Name), _key, new Color(1f, 1f, 1f, 0.55f));
            }
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
            _key = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _name = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _number = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _key.normal.textColor = Color.white;
            _name.normal.textColor = Color.white;
            _number.normal.textColor = Color.white;
        }
    }
}
