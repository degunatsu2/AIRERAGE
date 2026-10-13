using System;
using System.Collections.Generic;
using Degunatsu.Core.Economy;
using Degunatsu.Core.Physics;
using Degunatsu.Core.Simulation;
using NVector3 = System.Numerics.Vector3;

namespace Degunatsu.Net
{
    /// <summary>
    /// オンラインの試合のステージ（docs/11_combat_plan.md T3-8）: 地形、出現位置、ステージの範囲（外に出ると撃破）、ギミック、
    /// コインの出現地点（docs/12 T4-4）。
    /// 全員が同じシーンから作るので、通信では送らない（違うビルドどうしでは、状態のチェックサムの比較で不一致として見つかる）。
    /// </summary>
    public sealed class OnlineStage
    {
        // 出現位置が人数より少ないときに、横に並べる間隔（m）。
        public const float SpawnSpacing = 2.5f;

        private readonly NVector3[] _spawns;

        /// <param name="spawns">出現位置（1つ以上）。人数より少なければ、最初の位置を中心に横に並べる。</param>
        public OnlineStage(ICollisionQuery terrain, IReadOnlyList<NVector3> spawns, StageBounds? bounds = null, IReadOnlyList<StageZone> zones = null,
            IReadOnlyList<CoinSpawnPoint> coinSpawns = null)
        {
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            if (spawns == null || spawns.Count == 0)
            {
                throw new ArgumentException("出現位置が1つもありません", nameof(spawns));
            }
            _spawns = new NVector3[spawns.Count];
            for (int i = 0; i < spawns.Count; i++)
            {
                _spawns[i] = spawns[i];
            }
            Bounds = bounds;
            Zones = zones ?? Array.Empty<StageZone>();
            CoinSpawns = coinSpawns ?? Array.Empty<CoinSpawnPoint>();
        }

        public ICollisionQuery Terrain { get; }
        public StageBounds? Bounds { get; }
        public IReadOnlyList<StageZone> Zones { get; }
        public IReadOnlyList<CoinSpawnPoint> CoinSpawns { get; }
        public IReadOnlyList<NVector3> Spawns => _spawns;

        /// <summary>プレイヤー player（全員で playerCount 人）の出現位置。</summary>
        public NVector3 SpawnPosition(int player, int playerCount)
        {
            if (_spawns.Length >= playerCount)
            {
                return _spawns[player];
            }
            return OnlineSession.SpawnPosition(_spawns[0], player, playerCount);
        }

        /// <summary>出現したときの向き。出現位置が人数分あれば、出現位置の中心の方を向く。なければ交互に向かい合う。</summary>
        public int SpawnFacing(int player, int playerCount)
        {
            if (_spawns.Length < playerCount)
            {
                return player % 2 == 0 ? 1 : -1;
            }
            float center = 0f;
            for (int p = 0; p < playerCount; p++)
            {
                center += _spawns[p].X;
            }
            center /= playerCount;
            return _spawns[player].X <= center ? 1 : -1;
        }
    }
}
