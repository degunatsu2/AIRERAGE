using System;
using System.Collections.Generic;
using Degunatsu.Core.Simulation;
using UnityEngine;

namespace Degunatsu.Runtime.Simulation
{
    /// <summary>
    /// シーンに置くギミック（ジャンプ台、上昇気流、加速リング。Core の StageZone）。範囲は、この物体の位置を中心に、
    /// 大きさ _size の長方形（Z = 0 の面、ワールド座標。回転と拡大縮小は使わない）。見た目は StageZoneView が描く。
    /// 調整値は、シーンを作るコード（エディタのビルダー）で決める【仮】。
    /// </summary>
    public sealed class StageZoneAuthoring : MonoBehaviour
    {
        [SerializeField] private StageZoneKind _kind = StageZoneKind.JumpPad;
        [Tooltip("範囲の大きさ（m）")]
        [SerializeField] private Vector2 _size = new Vector2(2f, 0.4f);
        [Tooltip("向き（上昇気流では使わない）")]
        [SerializeField] private Vector2 _direction = Vector2.up;
        [Tooltip("ジャンプ台・加速リングでは速さ（m/s）、上昇気流では加速度（m/s²）")]
        [SerializeField] private float _power = 24f;
        [Tooltip("上昇気流の上向きの速さの上限（m/s）")]
        [SerializeField] private float _limit = 10f;

        public StageZoneKind Kind => _kind;
        public Vector2 Size => _size;
        public Vector2 Direction => _direction;

        /// <summary>設定する（シーンのビルダー用）。</summary>
        public void Configure(StageZoneKind kind, Vector2 size, Vector2 direction, float power, float limit = 0f)
        {
            _kind = kind;
            _size = size;
            _direction = direction;
            _power = power;
            _limit = limit;
        }

        /// <summary>Core のギミックにする。値が不正なら ArgumentException。</summary>
        public StageZone ToZone()
        {
            Vector3 center = transform.position;
            Vector2 half = _size * 0.5f;
            var direction = new System.Numerics.Vector2(_direction.x, _direction.y);
            return new StageZone(_kind, center.x - half.x, center.y - half.y, center.x + half.x, center.y + half.y, direction, _power, _limit);
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
            Gizmos.color = _kind == StageZoneKind.JumpPad ? new Color(0.3f, 1f, 0.4f)
                : _kind == StageZoneKind.Updraft ? new Color(0.5f, 0.85f, 1f) : new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawWireCube(transform.position, new Vector3(_size.x, _size.y, 0f));
        }
    }
}
