using System.Collections.Generic;
using Airerage.Core.Combat;
using Airerage.Core.Economy;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 操作するキャラクターのコンボの表示（ユーザーの指示、2026-10-13。docs/11_combat_plan.md T3-7）。
    /// - 相手ごとに 1 つ。同時に最大 3 つ（画面の左に縦に並べる）。2 ヒット以上つながったら出す。
    /// - 大きな数字でコンボの数と、コンボが終わったときにもらえるダメージ報酬（docs/12 T4-5。コンボのダメージの 30%）を出し、
    ///   その下に、相手ののけぞりが終わるまでの残りをゲージで示す（格闘ゲームでよくある表示）。
    /// - コンボが切れたら（相手ののけぞりが終わったら）、最後の数を少しの間残して消す。
    /// オフライン（SimulationRunner。相手はダミー）とオンライン（OnlineRunner。相手はほかのプレイヤー）のどちらでも使う
    /// （ISimulationFeed。T3-8）。状態を読むだけで、書き換えない。
    /// ローカル対戦（docs/15 T6-5）では、だれのコンボでも出す（_everyone。名前は「当てた人 > 相手」）。
    /// </summary>
    public sealed class ComboHud : MonoBehaviour
    {
        public const int MaxShown = 3;

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("コンボが切れた後に表示を残す時間（秒）")]
        [SerializeField] private float _lingerSeconds = 1.2f;
        [SerializeField] private Color _countColor = new Color(1f, 0.85f, 0.25f);
        [SerializeField] private Color _coinColor = new Color(1f, 0.95f, 0.55f);
        [SerializeField] private Color _gaugeColor = new Color(1f, 0.45f, 0.3f);
        [Tooltip("この PC で操作しているキャラクターだけでなく、だれのコンボでも出す（ローカル対戦）")]
        [SerializeField] private bool _everyone;

        private readonly List<Entry> _entries = new List<Entry>();
        private ISimulationFeed _feed;
        private GUIStyle _big;
        private GUIStyle _small;

        /// <summary>今表示しているコンボ（テスト用。新しい順）。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        public sealed class Entry
        {
            /// <summary>相手のキャラクターの番号（ワールドの番号）。</summary>
            public int Target;
            public string Label;
            public int Count;
            public int Coins;
            public float HitstunLeft;
            public float EndedAt = -1f;

            /// <summary>数が最後に増えた時刻（文字を白く光らせる。TextFlash）。</summary>
            public float CountAt = -10f;

            public bool Active => EndedAt < 0f;
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

        private void Update()
        {
            // この PC で操作しているキャラクターが当てているコンボを、相手ごとに更新する。
            int local = _feed.LocalCharacter;
            int count = _feed.CharacterCount;
            for (int i = 0; i < count; i++)
            {
                if (i == local)
                {
                    continue;
                }
                CharacterState target = _feed.GetState(i);
                CombatState c = target.Combat;
                bool byShownAttacker = _everyone ? c.LastAttacker != i : local >= 0 && c.LastAttacker == local;
                bool active = target.Action.Kind == ActionKind.Hitstun && c.HasLastAttacker && byShownAttacker && c.ComboCount >= 2;
                Entry entry = Find(i);
                if (active)
                {
                    if (entry == null || !entry.Active || c.ComboCount < entry.Count)
                    {
                        _entries.Remove(entry);
                        string label = _everyone ? $"{_feed.LabelOf(c.LastAttacker)} > {_feed.LabelOf(i)}" : _feed.LabelOf(i);
                        entry = new Entry { Target = i, Label = label };
                        _entries.Insert(0, entry);
                    }
                    if (c.ComboCount != entry.Count)
                    {
                        entry.CountAt = Time.time;
                    }
                    entry.Count = c.ComboCount;
                    entry.Coins = CombatRewardRules.DamageRewardAmount(_feed.Rules, c.ComboDamage);
                    entry.HitstunLeft = target.Action.Duration > 0
                        ? Mathf.Clamp01((float)(target.Action.Duration - target.Action.Ticks) / target.Action.Duration)
                        : 0f;
                }
                else if (entry != null && entry.Active)
                {
                    End(entry);
                }
            }
            // 相手がいなくなった（オンラインの試合が終わった）コンボも終える。
            foreach (Entry entry in _entries)
            {
                if (entry.Active && (entry.Target >= count || entry.Target == local))
                {
                    End(entry);
                }
            }
            _entries.RemoveAll(e => !e.Active && Time.time - e.EndedAt > _lingerSeconds);
            while (_entries.Count > MaxShown)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
        }

        private static void End(Entry entry)
        {
            entry.EndedAt = Time.time;
            entry.HitstunLeft = 0f;
        }

        private Entry Find(int target)
        {
            foreach (Entry entry in _entries)
            {
                if (entry.Target == target)
                {
                    return entry;
                }
            }
            return null;
        }

        private void OnGUI()
        {
            if (_entries.Count == 0)
            {
                return;
            }
            if (_big == null)
            {
                _big = TextFlash.Style(56, FontStyle.Bold, TextAnchor.MiddleLeft);
                _small = TextFlash.Style(22, FontStyle.Bold, TextAnchor.MiddleLeft);
            }
            Color previous = GUI.color;
            float y = 140f;
            foreach (Entry entry in _entries)
            {
                float alpha = entry.Active ? 1f : Mathf.Clamp01(1f - (Time.time - entry.EndedAt) / _lingerSeconds);
                DrawEntry(entry, 20f, y, alpha);
                y += 140f;
            }
            GUI.color = previous;
        }

        private void DrawEntry(Entry entry, float x, float y, float alpha)
        {
            // 数が増えた瞬間は白く光り、すぐに本来の色へ（TextFlash）。
            float age = Time.time - entry.CountAt;
            Shadowed(new Rect(x, y, 130f, 64f), entry.Count.ToString(), _big, TextFlash.Apply(_countColor, age), alpha);
            Shadowed(new Rect(x + 128f, y + 8f, 160f, 30f), "COMBO", _small, Color.white, alpha);
            Shadowed(new Rect(x + 128f, y + 34f, 200f, 26f), $"vs {entry.Label}", _small, new Color(0.8f, 0.8f, 0.85f), alpha * 0.8f);
            // コイン（今は威力の合計【仮】）。大きめの数字で。
            Shadowed(new Rect(x, y + 58f, 300f, 50f), $"+{entry.Coins} COIN", _big, TextFlash.Apply(_coinColor, age), alpha, 0.7f);

            // のけぞりの残り。
            var back = new Rect(x, y + 112f, 240f, 12f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f * alpha);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            Color fill = _gaugeColor;
            fill.a = alpha;
            GUI.color = fill;
            GUI.DrawTexture(new Rect(back.x + 2f, back.y + 2f, (back.width - 4f) * entry.HitstunLeft, back.height - 4f), Texture2D.whiteTexture);
        }

        private static void Shadowed(Rect rect, string text, GUIStyle style, Color color, float alpha, float scale = 1f)
        {
            int size = style.fontSize;
            style.fontSize = Mathf.RoundToInt(size * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.8f * alpha);
            GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
            color.a *= alpha;
            GUI.color = color;
            GUI.Label(rect, text, style);
            style.fontSize = size;
        }
    }
}
