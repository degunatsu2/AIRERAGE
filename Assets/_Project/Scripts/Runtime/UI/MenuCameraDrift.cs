using UnityEngine;

namespace Airerage.Runtime.UI
{
    /// <summary>
    /// メニューの背景: ステージをゆっくり横切って映すカメラ（D-69 の (b)）。向きは変えず（横から見たまま）、位置だけをゆっくり動かす。
    /// タイトルからメインメニューへの切り替え（D-87 の T4 スカイダイブ）では、前へ突進し（0.4 秒で 28 m）、白い光の間に少し後ろへ
    /// 移って（10 m）、元の距離へ戻る（Dive）。
    /// </summary>
    public sealed class MenuCameraDrift : MonoBehaviour
    {
        /// <summary>突進が終わって元の距離へ戻るまでの秒数。</summary>
        public const float DiveSeconds = 1.1f;

        [Tooltip("映す中心（ステージの真ん中のあたり）")]
        [SerializeField] private Vector3 _center = new Vector3(0f, 6f, 0f);
        [Tooltip("左右と上下に動く幅（m）")]
        [SerializeField] private Vector2 _range = new Vector2(22f, 5f);
        [Tooltip("ステージからの距離（m）")]
        [SerializeField] private float _distance = 44f;
        [Tooltip("1 往復にかかる秒数（左右）")]
        [SerializeField] private float _period = 70f;

        private float _diveAt = -10f;

        /// <summary>突進の途中か（テスト用）。</summary>
        public bool Diving => Time.unscaledTime - _diveAt < DiveSeconds;

        /// <summary>前へ突進して戻る（タイトルの決定）。</summary>
        public void Dive()
        {
            _diveAt = Time.unscaledTime;
        }

        /// <summary>始まってから t 秒の、前へ出る量（m。後ろは負）。</summary>
        public static float DivePush(float t)
        {
            if (t < 0f || t >= DiveSeconds)
            {
                return 0f;
            }
            if (t < 0.4f)
            {
                return 28f * UiEase.InCubic(t / 0.4f);
            }
            return -10f * (1f - UiEase.OutCubic((t - 0.4f) / (DiveSeconds - 0.4f)));
        }

        private void LateUpdate()
        {
            float t = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(1f, _period);
            float push = DivePush(Time.unscaledTime - _diveAt);
            transform.position = _center + new Vector3(Mathf.Sin(t) * _range.x, Mathf.Sin(t * 1.7f + 1f) * _range.y, -_distance + push);
            transform.rotation = Quaternion.identity;
        }
    }
}
