using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 遠景の動き（D-97 の V17。2026-11-17 のユーザーの決定）: 運営の撮影の飛行船を、遠くでゆっくり横切らせる（範囲の端まで行ったら反対の
    /// 端へ戻る）。灯台の光のように、その場で回すこともできる（_spin。2026-11-20）。見た目だけ（遊びと関係ない。巻き戻しとも関係ない）。
    /// </summary>
    public sealed class BackdropMotion : MonoBehaviour
    {
        [Tooltip("横切る速さ（m/秒）")]
        [SerializeField] private Vector3 _velocity = new Vector3(4.5f, 0f, 0f);
        [Tooltip("横切る範囲（X の最小と最大）")]
        [SerializeField] private Vector2 _wrap = new Vector2(-720f, 720f);
        [Tooltip("その場で回る速さ（度/秒。ローカルの軸ごと）")]
        [SerializeField] private Vector3 _spin;

        /// <summary>横切る速さ（テスト用）。</summary>
        public Vector3 Velocity => _velocity;

        /// <summary>横切らせる（シーンのビルダー用）。</summary>
        public void ConfigureDrift(Vector3 velocity, float minX, float maxX)
        {
            _velocity = velocity;
            _wrap = new Vector2(minX, maxX);
        }

        /// <summary>その場で回す（シーンのビルダー用。横切らない）。</summary>
        public void ConfigureSpin(Vector3 degreesPerSecond)
        {
            _velocity = Vector3.zero;
            _spin = degreesPerSecond;
        }

        /// <summary>回る速さ（テスト用）。</summary>
        public Vector3 Spin => _spin;

        private void Update()
        {
            if (_spin != Vector3.zero)
            {
                transform.localRotation *= Quaternion.Euler(_spin * Time.deltaTime);
            }
            if (_velocity == Vector3.zero)
            {
                return;
            }
            Vector3 p = transform.position + _velocity * Time.deltaTime;
            float span = _wrap.y - _wrap.x;
            if (span > 0f)
            {
                if (p.x > _wrap.y)
                {
                    p.x -= span;
                }
                else if (p.x < _wrap.x)
                {
                    p.x += span;
                }
            }
            transform.position = p;
        }
    }
}
