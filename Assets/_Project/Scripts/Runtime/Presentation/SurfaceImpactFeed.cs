using Airerage.Core.Simulation;
using Airerage.Core.Wire;
using Airerage.Runtime.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Presentation
{
    /// <summary>
    /// ステージの見た目に、キャラクターの動きを渡す（D-97。2026-11-17 のユーザーの決定）:
    /// - V22 触れたときの反応: 着地した所と、ワイヤーがつながった所を、足場の材質（Airerage/StageSurface）の光の輪にする。
    /// - V19 撃破の線の見える化: キャラクターの位置を、撃破の線の膜（Airerage/KillMembrane）に渡す（近いほど膜が濃く出る）。
    /// 状態を読むだけで書き換えない。渡し方はシェーダーの共通の値（_AirerageImpacts など）。書くのはこの部品だけで、無効になったら
    /// 数を 0 に戻す（メニューの背景のステージに前の試合の輪が残らないように）。巻き戻しで同じ着地が 2 回見えることがあるが、見た目だけ。
    /// </summary>
    public sealed class SurfaceImpactFeed : MonoBehaviour
    {
        /// <summary>同時に出せる光の輪の数（シェーダーの配列の長さと同じ）。</summary>
        public const int MaxImpacts = 8;

        /// <summary>膜に渡すキャラクターの数の上限（シェーダーの配列の長さと同じ）。</summary>
        public const int MaxActors = 8;

        private static readonly int ImpactsId = Shader.PropertyToID("_AirerageImpacts");
        private static readonly int ImpactCountId = Shader.PropertyToID("_AirerageImpactCount");
        private static readonly int ImpactClockId = Shader.PropertyToID("_AirerageImpactClock");
        private static readonly int ActorsId = Shader.PropertyToID("_AirerageActors");
        private static readonly int ActorCountId = Shader.PropertyToID("_AirerageActorCount");

        /// <summary>光の輪の種類。</summary>
        public enum ImpactKind
        {
            None,
            /// <summary>着地（落ちる速さが LandingSpeed より速いとき）。</summary>
            Landing,
            /// <summary>ワイヤーがつながった。</summary>
            WireAttach,
        }

        [Tooltip("状態を読む相手（SimulationRunner か OnlineRunner。ISimulationFeed）")]
        [SerializeField] private MonoBehaviour _source;
        [Tooltip("これより速く落ちて着地したときだけ輪を出す（m/s。歩いて段を下りるくらいでは出さない）")]
        [SerializeField] private float _landingSpeed = 6f;

        private readonly Vector4[] _impacts = new Vector4[MaxImpacts];
        private readonly Vector4[] _actors = new Vector4[MaxActors];
        private int _impactCount;
        private int _nextImpact;
        private CharacterState[] _previous = new CharacterState[0];
        private ISimulationFeed _feed;

        /// <summary>今までに出した光の輪の数（テスト用）。</summary>
        public int ImpactsAdded { get; private set; }

        /// <summary>最後に出した光の輪の場所（テスト用）。</summary>
        public Vector3 LastImpact { get; private set; }

        /// <summary>
        /// 前のティックと今のティックの状態から、光の輪を出すかと、その場所を決める。着地は足もと（中心から halfHeight だけ下）、
        /// ワイヤーはつながった点。両方なら着地を先にする。
        /// </summary>
        public static ImpactKind Detect(in CharacterState before, in CharacterState now, float halfHeight, float landingSpeed, out Vector3 point)
        {
            if (!before.Grounded && now.Grounded && before.Velocity.Y < -landingSpeed)
            {
                point = new Vector3(now.Position.X, now.Position.Y - halfHeight, now.Position.Z);
                return ImpactKind.Landing;
            }
            if (now.Wire.Phase == WirePhase.Attached && before.Wire.Phase != WirePhase.Attached)
            {
                point = new Vector3(now.Wire.Anchor.X, now.Wire.Anchor.Y, now.Wire.Anchor.Z);
                return ImpactKind.WireAttach;
            }
            point = default;
            return ImpactKind.None;
        }

        private void Awake()
        {
            _feed = _source as ISimulationFeed;
            if (_feed == null)
            {
                Debug.LogError($"{name}: 状態を読む相手（ISimulationFeed）が設定されていません。足場の光の輪と撃破の線の膜が動きません", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_feed != null)
            {
                _feed.Ticked += OnTicked;
            }
        }

        private void OnDisable()
        {
            if (_feed != null)
            {
                _feed.Ticked -= OnTicked;
            }
            Shader.SetGlobalFloat(ImpactCountId, 0f);
            Shader.SetGlobalFloat(ActorCountId, 0f);
        }

        /// <summary>光の輪を足す（いちばん古いものから置き換える）。</summary>
        public void AddImpact(Vector3 point)
        {
            _impacts[_nextImpact] = new Vector4(point.x, point.y, point.z, Time.unscaledTime);
            _nextImpact = (_nextImpact + 1) % MaxImpacts;
            _impactCount = Mathf.Min(_impactCount + 1, MaxImpacts);
            ImpactsAdded++;
            LastImpact = point;
        }

        private void OnTicked()
        {
            int count = _feed.CharacterCount;
            if (_previous.Length != count)
            {
                _previous = new CharacterState[count];
                for (int i = 0; i < count; i++)
                {
                    _previous[i] = _feed.GetState(i);
                }
                return;
            }
            for (int i = 0; i < count; i++)
            {
                CharacterState now = _feed.GetState(i);
                float halfHeight = _feed.GetConfig(i).Movement.HalfHeight;
                if (Detect(_previous[i], now, halfHeight, _landingSpeed, out Vector3 point) != ImpactKind.None)
                {
                    AddImpact(point);
                }
                _previous[i] = now;
            }
        }

        private void LateUpdate()
        {
            Shader.SetGlobalVectorArray(ImpactsId, _impacts);
            Shader.SetGlobalFloat(ImpactCountId, _impactCount);
            Shader.SetGlobalFloat(ImpactClockId, Time.unscaledTime);
            int actors = 0;
            int count = _feed.CharacterCount;
            for (int i = 0; i < count && actors < MaxActors; i++)
            {
                CharacterState state = _feed.GetState(i);
                if (state.Combat.HasLeft)
                {
                    continue;
                }
                _actors[actors++] = new Vector4(state.Position.X, state.Position.Y, state.Position.Z, 1f);
            }
            Shader.SetGlobalVectorArray(ActorsId, _actors);
            Shader.SetGlobalFloat(ActorCountId, actors);
        }
    }
}
