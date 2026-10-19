using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using UnityEngine;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// シーンに置くギミック（ジャンプ台、上昇気流、加速リング、ステージ固有のギミック。Core の StageZone）。範囲は、この物体の位置を中心に、
    /// 大きさ _size の長方形（Z = 0 の面、ワールド座標。回転と拡大縮小は使わない）。見た目は StageZoneView が描く。
    /// 調整値は、シーンを作るコード（エディタのビルダー）で決める【仮】。
    /// </summary>
    public sealed class StageZoneAuthoring : MonoBehaviour
    {
        [SerializeField] private StageZoneKind _kind = StageZoneKind.JumpPad;
        [Tooltip("範囲の大きさ（m）")]
        [SerializeField] private Vector2 _size = new Vector2(2f, 0.4f);
        [Tooltip("向き（上昇気流・バンパー・氷・低重力・ワープゲートでは使わない）")]
        [SerializeField] private Vector2 _direction = Vector2.up;
        [Tooltip("ジャンプ台・加速リング・スピードレーン・発射台・バンパーでは速さ（m/s）、上昇気流・風では加速度（m/s²）、氷・低重力では倍率（0〜1）")]
        [SerializeField] private float _power = 24f;
        [Tooltip("上昇気流・風の速さの上限、低重力の落ちる速さの上限（m/s）")]
        [SerializeField] private float _limit = 10f;
        [Tooltip("周期（秒。0 ならいつも。風・発射台・危険・吹雪）")]
        [SerializeField] private float _periodSeconds;
        [Tooltip("1 周期のうち効いている長さ（秒）")]
        [SerializeField] private float _activeSeconds;
        [Tooltip("周期のずれ（秒）")]
        [SerializeField] private float _phaseSeconds;
        [Tooltip("周期ごとにランダムに足す長さの上限（秒。0 なら決まった周期）")]
        [SerializeField] private float _jitterSeconds;
        [Tooltip("効き始める何秒前から予告するか（表示と音）")]
        [SerializeField] private float _warningSeconds;
        [Tooltip("ワープゲートの出口（ワールドの位置）")]
        [SerializeField] private Vector2 _target;
        [Tooltip("危険の柱の幅（m）")]
        [SerializeField] private float _width;
        [Tooltip("動く量（行きの分。m）")]
        [SerializeField] private Vector2 _travel;
        [Tooltip("1 往復の長さ（秒。0 なら動かない）")]
        [SerializeField] private float _travelSeconds;

        public StageZoneKind Kind => _kind;
        public Vector2 Size => _size;
        public Vector2 Direction => _direction;
        public Vector2 Target => _target;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneKind kind, Vector2 size, Vector2 direction, float power, float limit = 0f)
        {
            _kind = kind;
            _size = size;
            _direction = direction;
            _power = power;
            _limit = limit;
        }

        /// <summary>周期を決める（シーンのビルダー用。秒）。</summary>
        public void ConfigureTiming(float periodSeconds, float activeSeconds, float phaseSeconds, float jitterSeconds = 0f, float warningSeconds = 0f)
        {
            _periodSeconds = periodSeconds;
            _activeSeconds = activeSeconds;
            _phaseSeconds = phaseSeconds;
            _jitterSeconds = jitterSeconds;
            _warningSeconds = warningSeconds;
        }

        /// <summary>危険の柱の幅と、動き（行きの量と 1 往復の秒数）を決める（シーンのビルダー用）。</summary>
        public void ConfigureShape(float width, Vector2 travel, float travelSeconds)
        {
            _width = width;
            _travel = travel;
            _travelSeconds = travelSeconds;
        }

        /// <summary>ワープゲートの出口を決める（シーンのビルダー用）。</summary>
        public void ConfigureTarget(Vector2 target)
        {
            _target = target;
        }

        /// <summary>Core のギミックにする。値が不正なら ArgumentException。</summary>
        public StageZone ToZone()
        {
            Vector3 center = transform.position;
            Vector2 half = _size * 0.5f;
            var direction = new System.Numerics.Vector2(_direction.x, _direction.y);
            var timing = new StageZoneTiming(SimTime.SecondsToTicks(_periodSeconds), SimTime.SecondsToTicks(_activeSeconds),
                SimTime.SecondsToTicks(_phaseSeconds), SimTime.SecondsToTicks(_jitterSeconds), SimTime.SecondsToTicks(_warningSeconds));
            return new StageZone(_kind, center.x - half.x, center.y - half.y, center.x + half.x, center.y + half.y, direction, _power, _limit,
                timing, new System.Numerics.Vector2(_target.x, _target.y), _width, new System.Numerics.Vector2(_travel.x, _travel.y),
                SimTime.SecondsToTicks(_travelSeconds));
        }

        /// <summary>
        /// シーンのギミックを集める。不正なものは除き、理由を errors に追加する（黙って無視しない）。
        /// 並べる順は SimWorld が決める（見つかった順はシーンによって変わるため）。
        /// </summary>
        public static List<StageZone> Collect(ICollection<string> errors)
        {
            var zones = new List<StageZone>();
            foreach (StageZoneAuthoring authoring in FindObjectsByType<StageZoneAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                try
                {
                    zones.Add(authoring.ToZone());
                }
                catch (ArgumentException e)
                {
                    errors.Add($"ギミック（{authoring.name}）を除いた: {e.Message}");
                }
            }
            return zones;
        }

        private void OnDrawGizmos()
        {
            switch (_kind)
            {
                case StageZoneKind.JumpPad:
                case StageZoneKind.Launcher:
                    Gizmos.color = new Color(0.3f, 1f, 0.4f);
                    break;
                case StageZoneKind.Updraft:
                case StageZoneKind.Gust:
                    Gizmos.color = new Color(0.5f, 0.85f, 1f);
                    break;
                case StageZoneKind.Warp:
                    Gizmos.color = new Color(0.8f, 0.4f, 1f);
                    Gizmos.DrawLine(transform.position, new Vector3(_target.x, _target.y, 0f));
                    break;
                default:
                    Gizmos.color = new Color(1f, 0.6f, 0.2f);
                    break;
            }
            Gizmos.DrawWireCube(transform.position, new Vector3(_size.x, _size.y, 0f));
        }
    }
}
