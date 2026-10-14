namespace Airerage.Runtime.Simulation
{
    /// <summary>
    /// Core（System.Numerics）と Unity のベクトルの変換。変換はこの境界でだけ行う。
    /// </summary>
    public static class VectorConversion
    {
        public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v)
        {
            return new UnityEngine.Vector3(v.X, v.Y, v.Z);
        }

        public static System.Numerics.Vector3 ToNumerics(this UnityEngine.Vector3 v)
        {
            return new System.Numerics.Vector3(v.x, v.y, v.z);
        }
    }
}
