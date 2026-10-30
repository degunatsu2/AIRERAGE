using System.Collections.Generic;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 同じキャラクターの周りに出す円形ゲージの並べ方（ユーザーの要望、2026-10-30: 一部のゲージどうしが重なっていた。同時に出す数が増えるたびに、
    /// 古い物が重ならないように外側へずれ、右上の文字はタイムラインのように上へずれていく）。
    /// - 輪: いちばん新しいゲージは自分の半径。それより古いゲージは、1 つ内側のゲージの外へ（輪の太さの半分ずつと Gap だけ離して）ずらす。
    ///   もともと外側にあるゲージは動かさない。ゲージが消えたら、残りを内側へ詰め直す。
    /// - 文字: いちばん外の輪の右上を起点に縦に積む。いちばん新しいゲージの文字がいちばん下で、古いほど上（上から順に、外の輪から内の輪）。
    /// 輪の半径と文字の高さは、目標へ滑らかに動かす（RingGaugeView）。持ち主（CharacterView）が、ゲージの Show・Hide を全部呼んだ後に、
    /// 毎フレーム Arrange を呼ぶ（ここで輪を描く）。見た目だけで、状態は読まない。
    /// </summary>
    public sealed class RingGaugeStack
    {
        /// <summary>隣り合う輪の、太さのほかに空ける隙間（m。先端の光の玉が隣の輪にかからないくらい）。</summary>
        public const float Gap = 0.32f;

        /// <summary>積んだ文字の札どうしの隙間（画面の点）。</summary>
        public const float LabelGap = 4f;

        private readonly List<RingGaugeView> _members = new List<RingGaugeView>();

        // 出しているゲージ（古い順）。
        private readonly List<RingGaugeView> _order = new List<RingGaugeView>();

        public RingGaugeStack(IEnumerable<RingGaugeView> members)
        {
            foreach (RingGaugeView member in members)
            {
                if (member != null && !_members.Contains(member))
                {
                    _members.Add(member);
                    member.JoinStack(this);
                }
            }
        }

        /// <summary>出しているゲージ（古い順。テスト用）。</summary>
        public IReadOnlyList<RingGaugeView> Shown => _order;

        /// <summary>
        /// 出しているゲージを並べて描く（ゲージの Show・Hide を全部呼んだ後に、毎フレーム呼ぶ）。新しく出たゲージは、いちばん新しい側（内側）に
        /// 加える。同じフレームに出た物どうしは、登録の順に内から外へ。
        /// </summary>
        public void Arrange()
        {
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                if (_order[i] == null || !_order[i].IsShowing)
                {
                    _order.RemoveAt(i);
                }
            }
            for (int i = _members.Count - 1; i >= 0; i--)
            {
                RingGaugeView member = _members[i];
                if (member != null && member.IsShowing && !_order.Contains(member))
                {
                    _order.Add(member);
                }
            }
            if (_order.Count == 0)
            {
                return;
            }

            // 輪の半径（新しい物から外へ）と、文字の高さ（新しい物から上へ）。
            float inner = 0f;
            float innerWidth = 0f;
            float labelOffset = 0f;
            for (int k = _order.Count - 1; k >= 0; k--)
            {
                RingGaugeView gauge = _order[k];
                float radius = gauge.BaseRadius;
                if (k < _order.Count - 1)
                {
                    radius = Mathf.Max(radius, inner + (innerWidth + gauge.Width) * 0.5f + Gap);
                }
                gauge.SetStackTarget(radius, labelOffset);
                if (gauge.HasLabel)
                {
                    labelOffset += gauge.LabelHeight + LabelGap;
                }
                inner = radius;
                innerWidth = gauge.Width;
            }

            // 輪を描き（半径は目標へ滑らかに動かす）、文字の起点（いちばん外の輪の右上）を渡す。
            float outer = 0f;
            float outerWidth = 0f;
            foreach (RingGaugeView gauge in _order)
            {
                gauge.DrawInStack();
                if (gauge.Radius + gauge.Width * 0.5f > outer + outerWidth * 0.5f)
                {
                    outer = gauge.Radius;
                    outerWidth = gauge.Width;
                }
            }
            foreach (RingGaugeView gauge in _order)
            {
                gauge.SetLabelAnchor(outer, outerWidth);
            }
        }
    }
}
