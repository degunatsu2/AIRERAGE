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
        private InputAction _scroll;
        private InputAction _hold;
        private InputAction _reset;

        /// <summary>目標の距離（m）。実際の距離はこれになめらかに近づく。</summary>
        public float TargetDistance { get; private set; }

        /// <summary>初期の距離（m。ResetZoom で戻る）。</summary>
        public float DefaultDistance { get; private set; }

        public float MinDistance => _minDistance;
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
            _composer.CameraDistance = Mathf.Lerp(_composer.CameraDistance, TargetDistance, t);
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
