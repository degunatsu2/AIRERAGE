using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// カメラのズーム（ユーザーの要望。2026-10-12）。マウスホイールとゲームパッドの十字キーの上下で、追従カメラの距離を変える
    /// （LB / RB は特殊攻撃に使うため十字キーへ移した。D-56、2026-10-15）。
    /// 距離は下限と上限の間に制限し、なめらかに近づける。表示だけの機能で、シミュレーションには影響しない。
    /// 初期の距離（シーンで置いた距離）へは、マウスのホイールの押し込みか、ゲームパッドの十字キーの左か右で戻る（ResetZoom。
    /// ユーザーの要望、2026-10-15。十字キーの左右は【仮】）。
    /// アクションは AirerageControls.inputactions の "Camera" マップ（ScrollZoom / HoldZoom / ResetZoom）。
    /// 全体を見せる間（SetOverview。JUGGERNAUT のフラックの構えとメーデーの場所選び。ユーザーの案、2026-10-29）は、追う点をステージの
    /// 中心に替え、距離をステージ全体が入る距離にする（ホイールで変えた距離は覚えておき、終わったら戻る）。
    /// </summary>
    public sealed class CameraZoom : MonoBehaviour
    {
        private const string MapName = "Camera";

        [SerializeField] private CinemachinePositionComposer _composer;
        [SerializeField] private InputActionAsset _inputActions;

        [Tooltip("最も近い距離（m）【仮】")]
        [SerializeField] private float _minDistance = 12f;
        [Tooltip("最も遠い距離（m）【仮】")]
        [SerializeField] private float _maxDistance = 40f;
        [Tooltip("ホイール1目盛りで変える距離（m）【仮】")]
        [SerializeField] private float _scrollStep = 2f;
        [Tooltip("十字キーの上下を押している間に変える速さ（m/s）【仮】")]
        [SerializeField] private float _holdSpeed = 15f;
        [Tooltip("目標の距離に近づく速さ（1/s、大きいほど速い）")]
        [SerializeField] private float _sharpness = 12f;

        private InputActionMap _map;
        private CinemachineCamera _virtualCamera;
        private Transform _overviewTarget;
        private Transform _followBefore;
        private bool _overview;
        private float _overviewDistance;
        private InputAction _scroll;
        private InputAction _hold;
        private InputAction _reset;

        /// <summary>目標の距離（m）。実際の距離はこれになめらかに近づく。</summary>
        public float TargetDistance { get; private set; }

        /// <summary>初期の距離（m。ResetZoom で戻る）。</summary>
        public float DefaultDistance { get; private set; }

        public float MinDistance => _minDistance;

        /// <summary>全体を見せているか。</summary>
        public bool InOverview => _overview;
        public float MaxDistance => _maxDistance;

        private void Awake()
        {
            if (_composer == null || _inputActions == null)
            {
                Debug.LogError($"{name}: CinemachinePositionComposer か InputActionAsset が設定されていません", this);
                enabled = false;
                return;
            }
            if (!(_minDistance > 0f && _minDistance <= _maxDistance))
            {
                Debug.LogError($"{name}: 距離の下限と上限が不正です（下限 {_minDistance}、上限 {_maxDistance}）", this);
                enabled = false;
                return;
            }
            _map = _inputActions.FindActionMap(MapName, throwIfNotFound: true);
            _scroll = _map.FindAction("ScrollZoom", throwIfNotFound: true);
            _hold = _map.FindAction("HoldZoom", throwIfNotFound: true);
            _reset = _map.FindAction("ResetZoom", throwIfNotFound: true);
            DefaultDistance = Mathf.Clamp(_composer.CameraDistance, _minDistance, _maxDistance);
            TargetDistance = DefaultDistance;
            _virtualCamera = _composer.GetComponent<CinemachineCamera>();
        }

        /// <summary>
        /// 全体を見せる（on の間、カメラは center を追い、距離を distance（m）にする）。やめたら、元の追う相手と距離に戻る。
        /// </summary>
        public void SetOverview(bool on, Vector3 center, float distance)
        {
            if (_virtualCamera == null)
            {
                return;
            }
            if (on)
            {
                if (_overviewTarget == null)
                {
                    _overviewTarget = new GameObject("OverviewTarget").transform;
                }
                _overviewTarget.position = center;
                if (!_overview)
                {
                    _followBefore = _virtualCamera.Follow;
                    _virtualCamera.Follow = _overviewTarget;
                }
                _overviewDistance = Mathf.Max(distance, _minDistance);
                _overview = true;
                return;
            }
            if (_overview)
            {
                _virtualCamera.Follow = _followBefore;
                _overview = false;
            }
        }

        private void OnDestroy()
        {
            if (_overviewTarget != null)
            {
                Destroy(_overviewTarget.gameObject);
            }
        }

        private void OnEnable()
        {
            _map?.Enable();
        }

        private void OnDisable()
        {
            _map?.Disable();
        }

        private void Update()
        {
            // ホイールの値は1目盛りで ±1（Input System の既定の scrollDeltaBehavior）。上に回すと近づく。
            float scroll = _scroll.ReadValue<float>();
            // 十字キーの下で離れ、上で近づく。
            float hold = _hold.ReadValue<float>();
            ZoomIn(scroll * _scrollStep + hold * _holdSpeed * Time.unscaledDeltaTime);
            if (_reset.WasPressedThisFrame())
            {
                ResetZoom();
            }

            float t = 1f - Mathf.Exp(-_sharpness * Time.unscaledDeltaTime);
            _composer.CameraDistance = Mathf.Lerp(_composer.CameraDistance, _overview ? _overviewDistance : TargetDistance, t);
        }

        /// <summary>目標の距離を初期の距離に戻す。</summary>
        public void ResetZoom()
        {
            TargetDistance = DefaultDistance;
        }

        /// <summary>目標の距離を amount（m）だけ近づける（負なら離す）。下限と上限で止まる。</summary>
        public void ZoomIn(float amount)
        {
            TargetDistance = Mathf.Clamp(TargetDistance - amount, _minDistance, _maxDistance);
        }
    }
}
