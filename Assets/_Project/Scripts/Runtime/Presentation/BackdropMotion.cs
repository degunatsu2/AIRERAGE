using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 遠景の動き（D-97 の V17。2026-11-17 のユーザーの決定）: 遠くの輪や風車をゆっくり回す（Spin）、飛行船を横切らせる（Drift。
    /// 範囲の端まで行ったら反対の端へ戻る）。見た目だけ（遊びと関係ない。巻き戻しとも関係ない）。
    /// </summary>
    public sealed class BackdropMotion : MonoBehaviour
    {
        /// <summary>動き方。</summary>
        public enum Mode
        {
            Spin,
            Drift,
        }

        [SerializeField] private Mode _mode;
        [Tooltip("回す軸（自分の向き）")]
        [SerializeField] private Vector3 _axis = Vector3.up;
        [Tooltip("回す速さ（度/秒）")]
        [SerializeField] private float _degreesPerSecond = 6f;
        [Tooltip("横切る速さ（m/秒）")]
        [SerializeField] private Vector3 _velocity = new Vector3(6f, 0f, 0f);
        [Tooltip("横切る範囲（X の最小と最大）")]
        [SerializeField] private Vector2 _wrap = new Vector2(-400f, 400f);

        /// <summary>動き方（テスト用）。</summary>
        public Mode Kind => _mode;

        /// <summary>回す（シーンのビルダー用）。</summary>
        public void ConfigureSpin(Vector3 axis, float degreesPerSecond)
        {
            _mode = Mode.Spin;
            _axis = axis;
            _degreesPerSecond = degreesPerSecond;
        }

        /// <summary>横切らせる（シーンのビルダー用）。</summary>
        public void ConfigureDrift(Vector3 velocity, float minX, float maxX)
        {
            _mode = Mode.Drift;
            _velocity = velocity;
            _wrap = new Vector2(minX, maxX);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_mode == Mode.Spin)
            {
                transform.Rotate(_axis, _degreesPerSecond * dt, Space.Self);
                return;
            }
            Vector3 p = transform.position + _velocity * dt;
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
