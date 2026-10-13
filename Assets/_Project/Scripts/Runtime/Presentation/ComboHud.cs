using System.Collections.Generic;
using Degunatsu.Core.Combat;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 操作するキャラクターのコンボの表示（ユーザーの指示、2026-10-13。docs/11_combat_plan.md T3-7）。
    /// - 相手ごとに 1 つ。同時に最大 3 つ（画面の左に縦に並べる）。2 ヒット以上つながったら出す。
    /// - 大きな数字でコンボの数と、コンボ中に攻撃で得たコイン（今は威力の合計【仮】。コインは Phase 4）を出し、
    ///   その下に、相手ののけぞりが終わるまでの残りをゲージで示す（格闘ゲームでよくある表示）。
    /// - コンボが切れたら（相手ののけぞりが終わったら）、最後の数を少しの間残して消す。
    /// 状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class ComboHud : MonoBehaviour
    {
        public const int MaxShown = 3;

        [SerializeField] private SimulationRunner _runner;
        [Tooltip("コンボが切れた後に表示を残す時間（秒）")]
        [SerializeField] private float _lingerSeconds = 1.2f;
        [SerializeField] private Color _countColor = new Color(1f, 0.85f, 0.25f);
        [SerializeField] private Color _coinColor = new Color(1f, 0.95f, 0.55f);
        [SerializeField] private Color _gaugeColor = new Color(1f, 0.45f, 0.3f);

        private readonly List<Entry> _entries = new List<Entry>();
        private GUIStyle _big;
        private GUIStyle _small;

        /// <summary>今表示しているコンボ（テスト用。新しい順）。</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        public sealed class Entry
        {
            public int Target;
            public int Count;
            public int Coins;
            public float HitstunLeft;
            public float EndedAt = -1f;

            public bool Active => EndedAt < 0f;
        }

        private void Awake()
        {
            if (_runner == null)
            {
                Debug.LogError($"{name}: SimulationRunner が設定されていません", this);
                enabled = false;
            }
        }

        private void Update()
        {
            // 操作するキャラクター（番号 0）が当てているコンボを、相手（ダミー）ごとに更新する。
            for (int i = 0; i < _runner.DummyCount; i++)
            {
                CharacterState target = _runner.GetDummyState(i);
                CombatState c = target.Combat;
                bool active = target.Action.Kind == ActionKind.Hitstun && c.HasLastAttacker && c.LastAttacker == 0 && c.ComboCount >= 2;
                Entry entry = Find(i);
                if (active)
                {
                    if (entry == null || !entry.Active || c.ComboCount < entry.Count)
                    {
                        _entries.Remove(entry);
                        entry = new Entry { Target = i };
                        _entries.Insert(0, entry);
                    }
                    entry.Count = c.ComboCount;
                    entry.Coins = c.ComboDamage;
                    entry.HitstunLeft = target.Action.Duration > 0
                        ? Mathf.Clamp01((float)(target.Action.Duration - target.Action.Ticks) / target.Action.Duration)
                        : 0f;
                }
                else if (entry != null && entry.Active)
                {
                    entry.EndedAt = Time.time;
                    entry.HitstunLeft = 0f;
                }
            }
            _entries.RemoveAll(e => !e.Active && Time.time - e.EndedAt > _lingerSeconds);
            while (_entries.Count > MaxShown)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
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
                _big = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
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
            Shadowed(new Rect(x, y, 130f, 64f), entry.Count.ToString(), _big, _countColor, alpha);
            Shadowed(new Rect(x + 128f, y + 8f, 160f, 30f), "COMBO", _small, Color.white, alpha);
            Shadowed(new Rect(x + 128f, y + 34f, 200f, 26f), $"vs DUMMY {entry.Target + 1}", _small, new Color(0.8f, 0.8f, 0.85f), alpha * 0.8f);
            // コイン（今は威力の合計【仮】）。大きめの数字で。
            Shadowed(new Rect(x, y + 58f, 300f, 50f), $"+{entry.Coins} COIN", _big, _coinColor, alpha, 0.7f);

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
