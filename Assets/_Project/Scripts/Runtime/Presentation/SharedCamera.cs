using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using Unity.Cinemachine;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ローカル対戦とシングルモードの共通のカメラ（docs/15 T6-5。D-67）。ステージ全体（撃破の範囲から _edgeInset だけ内側。JUGGERNAUT の
    /// 全体を見せるカメラと同じ範囲）が画面に収まる位置と距離に、動かさずに置く（ユーザーの要望、2026-10-30: 全員を追って寄り引きすると、
    /// 画面が毎回ぶれて照準がずれる）。範囲の外へ出た人は、画面の端の札（PlayerGuides）で示す。撃破の範囲がなければ、原点の周りの
    /// _fallbackHalfSize の範囲。画面の縦横比が変わったら（ウインドウの大きさを変えたとき）距離だけを合わせ直す。
    /// 状態を読むだけで、書き換えない（ISimulationFeed）。
    /// </summary>
    public sealed class SharedCamera : MonoBehaviour
    {
        [Tooltip("状態を読む相手（LocalMatchRunner。撃破の範囲は ILocalAimSource.Bounds）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("距離を変える Cinemachine の構図")]
        [SerializeField] private CinemachinePositionComposer _composer;
        [Tooltip("カメラが向く点（ステージ全体の中心に置く）")]
        [SerializeField] private Transform _target;
        [Tooltip("画角と縦横比を読むカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("撃破の範囲から内側へ縮める幅（m。撃破の線のすぐ手前は映さない）【仮】")]
        [SerializeField] private float _edgeInset = 1f;
        [Tooltip("撃破の範囲がないときに映す、原点の周りの範囲の半分の大きさ（m。横、縦）【仮】")]
        [SerializeField] private Vector2 _fallbackHalfSize = new Vector2(64f, 40f);

        private ILocalAimSource _local;
        private float _distance;

        /// <summary>今のカメラの距離（m。テスト用）。</summary>
        public float Distance => _distance;

        /// <summary>映しているステージの範囲の中心（テスト用）。</summary>
        public Vector2 Center { get; private set; }

        /// <summary>映しているステージの範囲の半分の大きさ（m。テスト用）。</summary>
        public Vector2 HalfSize { get; private set; }

        private void Awake()
        {
            _local = _source as ILocalAimSource;
            if (_source == null || _composer == null || _target == null || _camera == null)
            {
                Debug.LogError($"{name}: 状態を読む相手・構図・向く点・カメラのどれかが設定されていません", this);
                enabled = false;
                return;
            }
            // 動かないカメラなので、追従の減衰はいらない（始まりにカメラが滑ってこないように）。
            _composer.Damping = Vector3.zero;
            Place();
        }

        private void LateUpdate()
        {
            Place();
        }

        // ステージ全体が入る位置と距離に置く（範囲と画面の縦横比だけで決まるので、試合中は動かない）。
        private void Place()
        {
            StageBounds? bounds = _local != null ? _local.Bounds : null;
            WholeStage(bounds, _edgeInset, _fallbackHalfSize, out Vector2 center, out Vector2 half);
            Center = center;
            HalfSize = half;
            _target.position = new Vector3(center.x, center.y, 0f);
            _distance = RequiredDistance(half, _camera.fieldOfView, _camera.aspect);
            _composer.CameraDistance = _distance;
        }

        /// <summary>
        /// 映すステージ全体の範囲（撃破の範囲 bounds から inset だけ内側。なければ原点の周りの fallbackHalf）。
        /// </summary>
        public static void WholeStage(StageBounds? bounds, float inset, Vector2 fallbackHalf, out Vector2 center, out Vector2 half)
        {
            if (!bounds.HasValue)
            {
                center = Vector2.zero;
                half = fallbackHalf;
                return;
            }
            StageBounds b = bounds.Value;
            center = new Vector2((b.MinX + b.MaxX) * 0.5f, (b.MinY + b.MaxY) * 0.5f);
            half = new Vector2(Mathf.Max(1f, (b.MaxX - b.MinX) * 0.5f - inset), Mathf.Max(1f, (b.MaxY - b.MinY) * 0.5f - inset));
        }

        /// <summary>
        /// 中心から縦横に halfSize（m）の範囲が画面に収まる、カメラの距離（m）。縦の画角 verticalFieldOfView（度）と縦横比 aspect から。
        /// </summary>
        public static float RequiredDistance(Vector2 halfSize, float verticalFieldOfView, float aspect)
        {
            float tan = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            float forHeight = halfSize.y / tan;
            float forWidth = halfSize.x / (tan * Mathf.Max(0.01f, aspect));
            return Mathf.Max(forHeight, forWidth);
        }
    }
}
