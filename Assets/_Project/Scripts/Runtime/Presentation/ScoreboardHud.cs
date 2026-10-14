using System.Collections.Generic;
using Degunatsu.Core.Match;
using Degunatsu.Core.Simulation;
using Degunatsu.Runtime.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Presentation
{
    /// <summary>
    /// 右上の全員の情報（ユーザーの要望、2026-10-14）。1 人 1 枚の札（GuidePlate）で、縁と斜めの線はプレイヤーの色。
    /// 左にキャラクターの絵（今はプレイヤーの色の四角と番号）、右を 4 つに分けて BANK（確定コイン）/ COIN（所持コイン）/
    /// K（撃破した数）/ F（撃破された数）。BANK の文字と数だけ黄色。
    /// いつも今の順位（MatchResults.Rank。確定コインの順）に並べ、順位が変わると札が滑って入れ替わる。確定コインが最も多い人
    /// （1 枚以上）には、札の左に王冠をつける（同じ数なら全員）。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class ScoreboardHud : MonoBehaviour
    {
        private static readonly string[] Labels = { "BANK", "COIN", "K", "F" };

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [SerializeField] private int _width = 300;
        [SerializeField] private int _rowHeight = 44;
        [SerializeField] private float _rowGap = 6f;
        [Tooltip("画面の右上からの余白（ピクセル）")]
        [SerializeField] private Vector2 _margin = new Vector2(12f, 10f);
        [Tooltip("順位が変わったときに、札が新しい位置へ滑る速さ（1 秒あたりの割合）")]
        [SerializeField] private float _slideSpeed = 10f;
        [SerializeField] private Color _bankColor = new Color(1f, 0.85f, 0.2f);

        private readonly List<float> _rowY = new List<float>();
        private ISimulationFeed _feed;
        private GuidePlate _plate;
        private Texture2D _crown;
        private GUIStyle _label;
        private GUIStyle _number;
        private GUIStyle _iconText;
        private int[] _order = new int[0];

        /// <summary>今の並び（上から、キャラクターの番号）と、王冠をつけている人の数（テスト用）。</summary>
        public IReadOnlyList<int> Order => _order;
        public int CrownCount { get; private set; }

        /// <summary>キャラクター index に王冠をつけているか（テスト用）。</summary>
        public bool HasCrown(int index)
        {
            return IsLeader(index, out _);
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
            _plate?.Dispose();
            if (_crown != null)
            {
                Destroy(_crown);
            }
        }

        private void Update()
        {
            int count = _feed.CharacterCount;
            var states = new CharacterState[count];
            for (int i = 0; i < count; i++)
            {
                states[i] = _feed.GetState(i);
            }
            _order = MatchResults.Rank(states);

            while (_rowY.Count < count)
            {
                _rowY.Add(-1f);
            }
            float blend = 1f - Mathf.Exp(-_slideSpeed * Time.unscaledDeltaTime);
            int crowns = 0;
            for (int rank = 0; rank < _order.Length; rank++)
            {
                int index = _order[rank];
                float target = _margin.y + rank * (_rowHeight + _rowGap);
                _rowY[index] = _rowY[index] < 0f ? target : Mathf.Lerp(_rowY[index], target, blend);
                crowns += IsLeader(index, out _) ? 1 : 0;
            }
            CrownCount = crowns;
        }

        // 確定コインが最も多い人（1 枚以上）か。
        private bool IsLeader(int index, out int best)
        {
            best = 0;
            int count = _feed.CharacterCount;
            for (int i = 0; i < count; i++)
            {
                best = Mathf.Max(best, _feed.GetState(i).Coins.Banked);
            }
            return best > 0 && index < count && _feed.GetState(index).Coins.Banked == best;
        }

        private void OnGUI()
        {
            int count = _feed.CharacterCount;
            if (count == 0 || _order.Length != count)
            {
                return;
            }
            if (_plate == null)
            {
                _plate = new GuidePlate(_width, _rowHeight);
                _crown = GuideIcons.Crown(64);
                _label = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                _number = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
                _iconText = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            float x = Screen.width - _margin.x - _width;
            foreach (int index in _order)
            {
                DrawRow(new Rect(x, _rowY[index], _width, _rowHeight), index);
            }
        }

        private void DrawRow(Rect rect, int index)
        {
            CharacterState state = _feed.GetState(index);
            Color color = _feed.ColorOf(index);
            _plate.DrawBackground(rect);
            _plate.DrawFrame(rect, color);

            // 左: キャラクターの絵（今はプレイヤーの色の四角と番号）。
            Rect icon = _plate.IconRect(rect);
            Color previous = GUI.color;
            GUI.color = Color.Lerp(color, Color.black, 0.45f);
            GUI.DrawTexture(icon, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(icon.x + 2f, icon.y + 2f, icon.width - 4f, icon.height - 4f), Texture2D.whiteTexture);
            GUI.color = previous;
            GuidePlate.DrawText(icon, (index + 1).ToString(), _iconText, Color.white);

            if (IsLeader(index, out _))
            {
                float size = rect.height * 0.8f;
                GUI.DrawTexture(new Rect(rect.x - size - 4f, rect.y + (rect.height - size) * 0.5f, size, size), _crown, ScaleMode.ScaleToFit);
            }

            // 右: 4 つに分けて BANK / COIN / K / F。
            Rect right = _plate.RightRect(rect);
            int[] values = { state.Coins.Banked, state.Coins.Held, state.Combat.Knockouts, state.Combat.Falls };
            float cell = right.width / Labels.Length;
            for (int c = 0; c < Labels.Length; c++)
            {
                var cellRect = new Rect(right.x + c * cell, right.y + 3f, cell, right.height - 6f);
                Color textColor = c == 0 ? _bankColor : Color.white;
                GuidePlate.DrawText(cellRect, Labels[c], _label, new Color(textColor.r, textColor.g, textColor.b, 0.85f));
                GuidePlate.DrawText(cellRect, values[c].ToString(), _number, textColor);
                if (c > 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.18f);
                    GUI.DrawTexture(new Rect(cellRect.x, cellRect.y + 4f, 1f, cellRect.height - 8f), Texture2D.whiteTexture);
                    GUI.color = previous;
                }
            }
        }
    }
}
