using Airerage.Core.Simulation;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.DebugTools
{
    /// <summary>
    /// キャラクターの軌跡（直近の数秒）を線で表示する（docs/06_prototype_plan.md T1-9）。ティックごとの位置を使う。
    /// 古い位置ほど薄くする。位置をリセットしたら消す。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrailView : MonoBehaviour
    {
        [SerializeField] private SimulationRunner _runner;
        [Tooltip("残すティック数（60 = 1秒）")]
        [SerializeField] private int _maxPoints = 180;
        [SerializeField] private Color _color = new Color(0f, 0.75f, 0.75f, 0.7f);

        private LineRenderer _line;
        private Vector3[] _ring;
        private int _count;
        private int _next;

        /// <summary>今の点の数（テスト用）。</summary>
        public int PointCount => _count;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _ring = new Vector3[Mathf.Max(2, _maxPoints)];
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(_color, 0f), new GradientColorKey(_color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(_color.a, 1f) });
            _line.colorGradient = gradient;
            Clear();
            if (_runner == null)
            {
                Debug.LogError($"{name}: SimulationRunner が設定されていません", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_runner != null)
            {
                _runner.PlayerTicked += OnPlayerTicked;
                _runner.PlayerReset += Clear;
            }
        }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.PlayerTicked -= OnPlayerTicked;
                _runner.PlayerReset -= Clear;
            }
        }

        private void OnPlayerTicked(CharacterState state)
        {
            _ring[_next] = state.Position.ToUnity();
            _next = (_next + 1) % _ring.Length;
            if (_count < _ring.Length)
            {
                _count++;
            }

            // 古い順に並べ直して線にする。
            int start = (_next - _count + _ring.Length) % _ring.Length;
            _line.positionCount = _count;
            for (int i = 0; i < _count; i++)
            {
                _line.SetPosition(i, _ring[(start + i) % _ring.Length]);
            }
        }

        private void Clear()
        {
            _count = 0;
            _next = 0;
            _line.positionCount = 0;
        }
    }
}
