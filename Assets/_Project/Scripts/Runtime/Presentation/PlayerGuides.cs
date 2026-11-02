using System.Collections.Generic;
using Airerage.Core.Match;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ほかのプレイヤーが画面の外にいるときの誘導表示（ユーザーの要望、2026-10-14）: 縁と斜めの線はプレイヤーの色、左にキャラクターの
    /// 絵（今はプレイヤーの色の四角）、右にプレイヤーネーム。自分と、復帰を待っている人（見えない）には出さない。
    /// WANTED（所持コインが 100 枚以上。CatchUpRules）の人は、左の絵を手配書の印にし、1 秒ごとに光らせる（ユーザーの要望、2026-11-02）。
    /// 札は EdgeGuideLayer が描く。状態を読むだけで、書き換えない。
    /// </summary>
    public sealed class PlayerGuides : MonoBehaviour
    {
        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("誘導表示（矢印と札）を描く層")]
        [SerializeField] private EdgeGuideLayer _guides;

        private readonly Dictionary<Color, Texture2D> _icons = new Dictionary<Color, Texture2D>();
        private ISimulationFeed _feed;
        private Texture2D _wantedIcon;

        /// <summary>このフレームに誘導表示を出している人の数と、そのうち WANTED の人の数（テスト用）。</summary>
        public int ShownCount { get; private set; }
        public int WantedShownCount { get; private set; }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _guides == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）か誘導表示の層（EdgeGuideLayer）が設定されていません", this);
                enabled = false;
            }
        }

        private void OnDestroy()
        {
            foreach (Texture2D icon in _icons.Values)
            {
                Destroy(icon);
            }
            _icons.Clear();
            if (_wantedIcon != null)
            {
                Destroy(_wantedIcon);
            }
        }

        private void Update()
        {
            int shown = 0;
            int wantedShown = 0;
            int local = _feed.LocalCharacter;
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                CharacterState state = _feed.GetState(i);
                if (i == local || state.Combat.IsWaitingToRespawn)
                {
                    continue;
                }
                Color color = _feed.ColorOf(i);
                var position = new Vector3(state.Position.X, state.Position.Y, 0f);
                if (state.Combat.InvisibleTicksLeft > 0)
                {
                    // 透明の間（THIEF の E）は、分身を指す（分身がいなければ出さない）。
                    if (!_feed.TryGetClone(i, out CharacterState clone))
                    {
                        continue;
                    }
                    position = new Vector3(clone.Position.X, clone.Position.Y, 0f);
                }
                if (CatchUpRules.IsWanted(_feed.Rules, state.Coins.Held))
                {
                    // WANTED の人: 手配書の印で、1 秒ごとに光らせる（人ごとに種類を分けて、出てきた時刻を覚える）。
                    if (_wantedIcon == null)
                    {
                        _wantedIcon = GuideIcons.WantedPoster(48, 64);
                    }
                    bool added = _guides.Add(position, color, _wantedIcon, _feed.LabelOf(i), announce: "wanted" + i, pulse: true);
                    shown += added ? 1 : 0;
                    wantedShown += added ? 1 : 0;
                    continue;
                }
                shown += _guides.Add(position, color, IconOf(color), _feed.LabelOf(i)) ? 1 : 0;
            }
            ShownCount = shown;
            WantedShownCount = wantedShown;
        }

        private Texture2D IconOf(Color color)
        {
            if (!_icons.TryGetValue(color, out Texture2D icon))
            {
                icon = GuideIcons.Square(48, color);
                _icons.Add(color, icon);
            }
            return icon;
        }
    }
}
