using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// ワイヤーのエイムアシストの対象になるアンカーポイント（この GameObject の位置）。
    /// シミュレーションの開始時に集めるので、プレイ中に動かしても反映されない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WireAnchorPoint : MonoBehaviour
    {
        [Tooltip("エイムアシストの半径（m。0 なら WireTuning.AimAssistRadius。大きいアンカー。ユーザーの要望、2026-10-26）")]
        [SerializeField] private float _radius;

        public float Radius => _radius;

        /// <summary>エイムアシストの半径を決める（シーンのビルダー用）。</summary>
        public void Configure(float radius)
        {
            _radius = radius;
        }
    }
}
