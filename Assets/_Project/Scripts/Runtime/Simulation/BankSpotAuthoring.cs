using System;
using System.Collections.Generic;
using Degunatsu.Core.Economy;
using UnityEngine;

namespace Degunatsu.Runtime.Simulation
{
    /// <summary>
    /// シーンに置く拠点（バンク）の候補の地点（Core の BankSpot。docs/12_match_plan.md T4-7）。位置はこの物体の位置（Z = 0 の面）。
    /// 拠点は1つで、試合が始まったときは「最初の拠点」の地点にあり、40 秒ごとに候補の地点を移る。置き場所はシーンを作るコードで決める【仮】。
    /// </summary>
    public sealed class BankSpotAuthoring : MonoBehaviour
    {
        [Tooltip("試合が始まったときの拠点か（候補のうち1つだけにする）")]
        [SerializeField] private bool _isFirst;

        public bool IsFirst => _isFirst;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(bool isFirst)
        {
            _isFirst = isFirst;
        }

        /// <summary>Core の地点にする。値が不正なら ArgumentException。</summary>
        public BankSpot ToBankSpot()
        {
            Vector3 p = transform.position;
            return new BankSpot(new System.Numerics.Vector2(p.x, p.y), _isFirst);
        }

        /// <summary>シーンの拠点の候補を集める。不正なものは除き、理由を errors に追加する。並べる順は SimWorld が決める。</summary>
        public static List<BankSpot> Collect(ICollection<string> errors)
        {
            var spots = new List<BankSpot>();
            foreach (BankSpotAuthoring authoring in FindObjectsByType<BankSpotAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                try
                {
                    spots.Add(authoring.ToBankSpot());
                }
                catch (ArgumentException e)
                {
                    errors.Add($"拠点の候補（{authoring.name}）を除いた: {e.Message}");
                }
            }
            return spots;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = _isFirst ? new Color(0.2f, 1f, 0.9f) : new Color(0.2f, 0.7f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 2f);
        }
    }
}
