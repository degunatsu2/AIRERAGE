using UnityEngine;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの背景: ステージをゆっくり横切って映すカメラ（D-69 の (b)）。向きは変えず（横から見たまま）、位置だけをゆっくり動かす。
    /// </summary>
    public sealed class MenuCameraDrift : MonoBehaviour
    {
        [Tooltip("映す中心（ステージの真ん中のあたり）")]
        [SerializeField] private Vector3 _center = new Vector3(0f, 6f, 0f);
        [Tooltip("左右と上下に動く幅（m）")]
        [SerializeField] private Vector2 _range = new Vector2(22f, 5f);
        [Tooltip("ステージからの距離（m）")]
        [SerializeField] private float _distance = 44f;
        [Tooltip("1 往復にかかる秒数（左右）")]
        [SerializeField] private float _period = 70f;

        private void LateUpdate()
        {
            float t = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(1f, _period);
            transform.position = _center + new Vector3(Mathf.Sin(t) * _range.x, Mathf.Sin(t * 1.7f + 1f) * _range.y, -_distance);
            transform.rotation = Quaternion.identity;
        }
    }
}
