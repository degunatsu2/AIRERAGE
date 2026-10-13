using System;
using System.Collections.Generic;
using Degunatsu.Core.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Simulation
{
    /// <summary>
    /// シーンのステージの範囲（docs/11_combat_plan.md T3-3）。キャラクターの中心がこの長方形（Z = 0 の面、ワールド座標）の
    /// 外に出たら撃破になる。シーンに1つだけ置く。なければ撃破はない（Sandbox_Wire など）。
    /// </summary>
    public sealed class KnockoutBounds : MonoBehaviour
    {
        [SerializeField] private Vector2 _min = new Vector2(-30f, -15f);
        [SerializeField] private Vector2 _max = new Vector2(30f, 30f);

        public Vector2 Min => _min;
        public Vector2 Max => _max;

        /// <summary>範囲を設定する（シーンのビルダー用）。</summary>
        public void Configure(Vector2 min, Vector2 max)
        {
            _min = min;
            _max = max;
        }

        /// <summary>
        /// シーンのステージの範囲を探す。なければ null。2つ以上あるか、範囲が不正なら、理由を errors に追加して null を返す
        /// （どれを使うかを勝手に選ばない）。
        /// </summary>
        public static StageBounds? Find(ICollection<string> errors)
        {
            KnockoutBounds[] found = FindObjectsByType<KnockoutBounds>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (found.Length == 0)
            {
                return null;
            }
            if (found.Length > 1)
            {
                errors.Add($"ステージの範囲（KnockoutBounds）が {found.Length} つある。1つにする（撃破をなしにした）");
                return null;
            }
            try
            {
                return new StageBounds(found[0]._min.x, found[0]._min.y, found[0]._max.x, found[0]._max.y);
            }
            catch (ArgumentException e)
            {
                errors.Add($"ステージの範囲（{found[0].name}）が不正: {e.Message}（撃破をなしにした）");
                return null;
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f);
            Vector2 center = (_min + _max) * 0.5f;
            Gizmos.DrawWireCube(new Vector3(center.x, center.y, 0f), new Vector3(_max.x - _min.x, _max.y - _min.y, 0f));
        }
    }
}
