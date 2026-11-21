using System;
using System.Collections.Generic;
using Airerage.Core.Common;
using Airerage.Core.Simulation;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// シーンに置くステージの仕掛け（Core の StageTrap。ユーザーの要望、2026-10-26）。赤外線は、この物体の位置が赤外線の範囲の中心で、
    /// 発射口は中心からのずれ。ピッチャーマシンは、この物体の位置が発射口。見た目は StageTrapView。値はシーンを作るコード（エディタの
    /// ビルダー）で決める【仮】。
    /// </summary>
    public sealed class StageTrapAuthoring : MonoBehaviour
    {
        [SerializeField] private StageTrapKind _kind = StageTrapKind.Tripwire;

        [Header("共通")]
        [Tooltip("当たったときに落とすコインの枚数（威力）")]
        [SerializeField] private int _power = 10;
        [Tooltip("吹き飛ばす速さ（m/s）")]
        [SerializeField] private float _knockback = 20f;

        [Header("赤外線とミサイル")]
        [Tooltip("赤外線の範囲の大きさ（m）")]
        [SerializeField] private Vector2 _beamSize = new Vector2(20f, 0.3f);
        [Tooltip("発射口の、範囲の中心からのずれ（m）")]
        [SerializeField] private Vector2 _muzzleOffset = new Vector2(-10f, 0f);
        [Tooltip("ミサイルの向き")]
        [SerializeField] private Vector2 _direction = Vector2.right;
        [Tooltip("ミサイルの速さ（m/s）と飛ぶ距離（m）")]
        [SerializeField] private float _speed = 40f;
        [SerializeField] private float _range = 20f;
        [Tooltip("触れてから撃つまで（秒。予告）と、飛び終わってからまた撃てるまで（秒）")]
        [SerializeField] private float _armSeconds = 0.5f;
        [SerializeField] private float _cooldownSeconds = 3f;
        [Tooltip("ミサイルの大きさ（m）")]
        [SerializeField] private Vector2 _missileSize = new Vector2(1.4f, 0.5f);

        [Header("ピッチャーマシン")]
        [Tooltip("打ち出す速度（m/s）")]
        [SerializeField] private Vector2 _launchVelocity = new Vector2(14f, 14f);
        [Tooltip("周期とずれ（秒）")]
        [SerializeField] private float _periodSeconds = 20f;
        [SerializeField] private float _phaseSeconds;
        [Tooltip("ボールの半径（m）")]
        [SerializeField] private float _radius = 0.35f;
        [Tooltip("当たる速さの下限（m/s）")]
        [SerializeField] private float _minHitSpeed = 6f;
        [Tooltip("止まってから消えるまでの点滅（秒）")]
        [SerializeField] private float _blinkSeconds = 1.5f;

        [Header("鬼火の輪")]
        [Tooltip("飛び回る地点（ワールドの位置）")]
        [SerializeField] private Vector2[] _waypoints = new Vector2[0];
        [Tooltip("地点で漂う長さ（秒）・輪の大きさ（m）・渡すコインの枚数・また現れるまで（秒。最短と最長）。速さは _speed")]
        [SerializeField] private float _hoverSeconds = 1f;
        [SerializeField] private Vector2 _ringSize = new Vector2(2.4f, 3f);
        [SerializeField] private int _reward = 30;
        [SerializeField] private float _respawnMinSeconds = 20f;
        [SerializeField] private float _respawnMaxSeconds = 30f;

        public StageTrapKind Kind => _kind;
        public Vector2 BeamSize => _beamSize;
        public Vector2 MuzzleOffset => _muzzleOffset;
        public Vector2 Direction => _direction;
        public float Radius => _radius;
        public Vector2 MissileSize => _missileSize;
        public Vector2 RingSize => _ringSize;

        /// <summary>赤外線とミサイルにする（シーンのビルダー用）。</summary>
        public void ConfigureTripwire(Vector2 beamSize, Vector2 muzzleOffset, Vector2 direction, float speed, float range, int power, float knockback,
            float armSeconds, float cooldownSeconds, Vector2 missileSize)
        {
            _kind = StageTrapKind.Tripwire;
            _beamSize = beamSize;
            _muzzleOffset = muzzleOffset;
            _direction = direction;
            _speed = speed;
            _range = range;
            _power = power;
            _knockback = knockback;
            _armSeconds = armSeconds;
            _cooldownSeconds = cooldownSeconds;
            _missileSize = missileSize;
        }

        /// <summary>ピッチャーマシンにする（シーンのビルダー用）。</summary>
        public void ConfigurePitcher(Vector2 launchVelocity, float periodSeconds, float phaseSeconds, float radius, int power, float knockback,
            float minHitSpeed, float blinkSeconds)
        {
            _kind = StageTrapKind.Pitcher;
            _launchVelocity = launchVelocity;
            _periodSeconds = periodSeconds;
            _phaseSeconds = phaseSeconds;
            _radius = radius;
            _power = power;
            _knockback = knockback;
            _minHitSpeed = minHitSpeed;
            _blinkSeconds = blinkSeconds;
        }

        /// <summary>鬼火の輪にする（シーンのビルダー用。waypoints はワールドの位置）。</summary>
        public void ConfigureWispRing(Vector2[] waypoints, float speed, float hoverSeconds, Vector2 ringSize, int reward, float respawnMinSeconds,
            float respawnMaxSeconds)
        {
            _kind = StageTrapKind.WispRing;
            _waypoints = waypoints;
            _speed = speed;
            _hoverSeconds = hoverSeconds;
            _ringSize = ringSize;
            _reward = reward;
            _respawnMinSeconds = respawnMinSeconds;
            _respawnMaxSeconds = respawnMaxSeconds;
        }

        /// <summary>Core の仕掛けにする（秒はティックに丸める）。値が不正なら ArgumentException。</summary>
        public StageTrap ToTrap()
        {
            Vector3 p = transform.position;
            var center = new NVector2(p.x, p.y);
            if (_kind == StageTrapKind.Tripwire)
            {
                var half = new NVector2(_beamSize.x * 0.5f, _beamSize.y * 0.5f);
                return StageTrap.Tripwire(center - half, center + half, center + new NVector2(_muzzleOffset.x, _muzzleOffset.y),
                    new NVector2(_direction.x, _direction.y), _speed, _range, _power, _knockback, Ticks(_armSeconds), Ticks(_cooldownSeconds),
                    new NVector2(_missileSize.x * 0.5f, _missileSize.y * 0.5f));
            }
            if (_kind == StageTrapKind.WispRing)
            {
                var points = new NVector2[_waypoints.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = new NVector2(_waypoints[i].x, _waypoints[i].y);
                }
                return StageTrap.WispRing(points, _speed, Ticks(_hoverSeconds), new NVector2(_ringSize.x * 0.5f, _ringSize.y * 0.5f), _reward,
                    Ticks(_respawnMinSeconds), Ticks(_respawnMaxSeconds));
            }
            return StageTrap.Pitcher(center, new NVector2(_launchVelocity.x, _launchVelocity.y), Ticks(_periodSeconds), Ticks(_phaseSeconds), _radius,
                _power, _knockback, _minHitSpeed, Ticks(_blinkSeconds));
        }

        private static int Ticks(float seconds)
        {
            return Mathf.Max(0, Mathf.RoundToInt(seconds * SimTime.TicksPerSecond));
        }

        /// <summary>シーンの仕掛けをすべて集める（不正なものは errors に書いて除く）。</summary>
        public static List<StageTrap> Collect(ICollection<string> errors)
        {
            var traps = new List<StageTrap>();
            foreach (StageTrapAuthoring authoring in FindObjectsByType<StageTrapAuthoring>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                try
                {
                    traps.Add(authoring.ToTrap());
                }
                catch (ArgumentException e)
                {
                    errors.Add($"ステージの仕掛け（{authoring.name}）を除いた: {e.Message}");
                }
            }
            return traps;
        }

        private void OnDrawGizmos()
        {
            if (_kind == StageTrapKind.Tripwire)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f);
                Gizmos.DrawWireCube(transform.position, new Vector3(_beamSize.x, _beamSize.y, 0f));
                Gizmos.DrawSphere(transform.position + (Vector3)_muzzleOffset, 0.3f);
            }
            else
            {
                Gizmos.color = new Color(1f, 1f, 1f);
                Gizmos.DrawWireSphere(transform.position, _radius);
                Gizmos.DrawLine(transform.position, transform.position + (Vector3)_launchVelocity * 0.2f);
            }
        }
    }
}
