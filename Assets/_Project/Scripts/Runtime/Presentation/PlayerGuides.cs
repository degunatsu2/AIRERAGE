using System.Collections.Generic;
using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ほかのプレイヤーが画面の外にいるときの誘導表示（ユーザーの要望、2026-10-14）: 縁と斜めの線はプレイヤーの色、左にキャラクターの
    /// 絵（今はプレイヤーの色の四角）、右にプレイヤーネーム。自分と、復帰を待っている人（見えない）には出さない。
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

        /// <summary>このフレームに誘導表示を出している人の数（テスト用）。</summary>
        public int ShownCount { get; private set; }

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
        }

        private void Update()
        {
            int shown = 0;
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
                shown += _guides.Add(position, color, IconOf(color), _feed.LabelOf(i)) ? 1 : 0;
            }
            ShownCount = shown;
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
