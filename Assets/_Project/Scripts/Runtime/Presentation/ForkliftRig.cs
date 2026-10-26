using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// 埠頭の、コンテナを運ぶ背景のフォークリフト（見た目だけ。ユーザーの要望、2026-10-26）。運ばれるコンテナの見た目（_target）の真後ろへ
    /// 走り、マストをコンテナの高さまで伸ばし、爪でコンテナを下から支えているように見せる。車体（_body）は奥の岸壁の上、マスト（_mast）は
    /// 車体からコンテナの下まで、爪の台（_carriage）はコンテナの下に置く。コンテナが見えない間は、車体だけを残す。
    /// </summary>
    public sealed class ForkliftRig : MonoBehaviour
    {
        [Tooltip("運ばれるコンテナの見た目（動く足場の見た目）")]
        [SerializeField] private Transform _target;
        [Tooltip("車体（奥の岸壁の上を走る）")]
        [SerializeField] private Transform _body;
        [Tooltip("マスト（高さを伸ばす。原点が中心の箱）")]
        [SerializeField] private Transform _mast;
        [Tooltip("爪の台（コンテナの下）")]
        [SerializeField] private Transform _carriage;
        [Tooltip("奥の岸壁の上面の高さ（m）と、車体・マストの奥行きの位置（z）")]
        [SerializeField] private float _groundY = 1f;
        [SerializeField] private float _bodyZ = 8f;
        [SerializeField] private float _mastZ = 5f;
        [Tooltip("コンテナの中心から、支える下の面までの距離（m）")]
        [SerializeField] private float _holdBelow = 1.6f;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(Transform target, Transform body, Transform mast, Transform carriage, float groundY, float bodyZ, float mastZ,
            float holdBelow)
        {
            _target = target;
            _body = body;
            _mast = mast;
            _carriage = carriage;
            _groundY = groundY;
            _bodyZ = bodyZ;
            _mastZ = mastZ;
            _holdBelow = holdBelow;
        }

        /// <summary>マストの上の端の高さ（テスト用）。</summary>
        public float MastTop { get; private set; }

        private void LateUpdate()
        {
            if (_target == null || _body == null || _mast == null || _carriage == null)
            {
                return;
            }
            Vector3 target = _target.position;
            bool carrying = _target.gameObject.activeInHierarchy;
            float x = target.x;
            Vector3 body = _body.position;
            _body.position = new Vector3(x, body.y, _bodyZ);
            float top = carrying ? target.y - _holdBelow : _groundY + 2f;
            float height = Mathf.Max(1f, top - _groundY);
            _mast.position = new Vector3(x, _groundY + height * 0.5f, _mastZ);
            Vector3 scale = _mast.localScale;
            scale.y = height;
            _mast.localScale = scale;
            _carriage.position = new Vector3(x, top, _carriage.position.z);
            MastTop = top;
        }
    }
}
