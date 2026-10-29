using Airerage.Core.Simulation;
using Airerage.Core.Skills;
using Airerage.Runtime.Simulation;
using Unity.Cinemachine;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ローカル対戦の共通のカメラ（docs/15 T6-5。D-67）。全員（復帰を待っている人を除く）が入る範囲の中心へ、カメラが追う点（_target）を
    /// 動かし、範囲が画面に収まるようにカメラの距離を変える（寄り引き）。距離は _minDistance〜_maxDistance で、上限を超えて離れた人は
    /// 画面の端の札（PlayerGuides）で示す。位置の追従のなめらかさは Cinemachine の減衰に任せ、距離はここでなめらかに変える。
    /// 状態を読むだけで、書き換えない（ISimulationFeed）。
    /// </summary>
    public sealed class SharedCamera : MonoBehaviour
    {
        [Tooltip("状態を読む相手（LocalMatchRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("距離を変える Cinemachine の構図")]
        [SerializeField] private CinemachinePositionComposer _composer;
        [Tooltip("カメラが追う点（全員の範囲の中心に動かす）")]
        [SerializeField] private Transform _target;
        [Tooltip("画角と縦横比を読むカメラ")]
        [SerializeField] private Camera _camera;
        [Tooltip("いちばん寄ったときの距離（m。1 人のときのカメラと同じ 30m）【仮】")]
        [SerializeField] private float _minDistance = 30f;
        [Tooltip("いちばん引いたときの距離（m）【仮】")]
        [SerializeField] private float _maxDistance = 60f;
        [Tooltip("範囲の周りの余白（m。横、縦）【仮】")]
        [SerializeField] private Vector2 _margin = new Vector2(6f, 5f);
        [Tooltip("距離を変える速さ（秒。小さいほど速く追いつく）【仮】")]
        [SerializeField] private float _zoomSeconds = 0.35f;

        private ISimulationFeed _feed;
        private float _distance;

        /// <summary>今のカメラの距離（m。テスト用）。</summary>
        public float Distance => _distance;

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null || _composer == null || _target == null || _camera == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）・構図・追う点・カメラのどれかが設定されていません", this);
                enabled = false;
                return;
            }
            _distance = _minDistance;
            _composer.CameraDistance = _distance;
        }

        private void LateUpdate()
        {
            bool overview = TryGetOverview(out Vector2 min, out Vector2 max);
            if (!overview && !TryGetBounds(out min, out max))
            {
                return;
            }
            Vector2 center = (min + max) * 0.5f;
            _target.position = new Vector3(center.x, center.y, 0f);
            Vector2 half = (max - min) * 0.5f + (overview ? Vector2.zero : _margin);
            float required = RequiredDistance(half, _camera.fieldOfView, _camera.aspect);
            float wanted = overview ? Mathf.Max(required, _minDistance) : Mathf.Clamp(required, _minDistance, _maxDistance);
            float blend = _zoomSeconds > 0f ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / _zoomSeconds) : 1f;
            _distance = Mathf.Lerp(_distance, wanted, blend);
            _composer.CameraDistance = _distance;
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

        // だれか（CPU 以外）がフラックで構えているか、メーデーで場所を選んでいれば（JUGGERNAUT）、ステージ全体を見せる範囲（なければ false）。
        private bool TryGetOverview(out Vector2 min, out Vector2 max)
        {
            min = max = Vector2.zero;
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                CharacterState state = _feed.GetState(i);
                bool wants = state.Combat.FlakStance || state.Combat.MaydayPhase == JuggernautRules.MaydaySelect;
                if (!wants || (_feed is ICpuRoster cpus && cpus.IsCpu(i)))
                {
                    continue;
                }
                StageBounds? bounds = _feed is ILocalAimSource local ? local.Bounds : null;
                JuggernautRules.TargetArea(bounds, _feed.GetConfig(i).Skills.Juggernaut, out System.Numerics.Vector2 center, out System.Numerics.Vector2 half);
                min = new Vector2(center.X - half.X, center.Y - half.Y);
                max = new Vector2(center.X + half.X, center.Y + half.Y);
                return true;
            }
            return false;
        }

        // 復帰を待っていない全員の位置の範囲（だれもいなければ false）。メーデーで吊られている人は入れない。
        private bool TryGetBounds(out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            for (int i = 0; i < _feed.CharacterCount; i++)
            {
                CharacterState state = _feed.GetState(i);
                if (state.Combat.IsWaitingToRespawn || state.Combat.IsAway)
                {
                    continue;
                }
                var position = new Vector2(state.Position.X, state.Position.Y);
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
                any = true;
            }
            return any;
        }
    }
}
