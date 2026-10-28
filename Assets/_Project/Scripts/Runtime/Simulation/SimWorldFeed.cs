using Airerage.Core.Simulation;

namespace Airerage.Runtime.Simulation
{
    /// <summary>ISimulationFeed を SimWorld から作るときの共通の読み方。</summary>
    public static class SimWorldFeed
    {
        /// <summary>world のキャラクター index の分身（THIEF の E）が出ていれば、その状態。</summary>
        public static bool TryGetClone(SimWorld world, int index, out CharacterState clone)
        {
            if (world == null || index < 0 || index >= world.CharacterCount || !world.HasClone(index))
            {
                clone = default;
                return false;
            }
            clone = world.GetClone(index);
            return true;
        }
    }
}
