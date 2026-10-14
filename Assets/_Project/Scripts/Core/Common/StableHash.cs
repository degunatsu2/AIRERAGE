namespace Airerage.Core.Common
{
    /// <summary>
    /// 決定的な乱数の代わり（docs/12 T4-4・T4-7）。種と数から、PC が違っても同じ値を返す（整数の演算だけ。状態を持たない）。
    /// 試合の中のランダムな選択（100 枚のコインの場所、拠点の移動先）に使う。種はホストが決めて全員へ送る（MatchRules.Seed）。
    /// </summary>
    public static class StableHash
    {
        /// <summary>seed と a、b から、ばらついた 32bit の値を返す。</summary>
        public static uint Hash(int seed, int a, int b = 0)
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h = Mix(h ^ (uint)a * 0x85EBCA77u);
            h = Mix(h ^ (uint)b * 0xC2B2AE3Du);
            return h;
        }

        /// <summary>0 以上 count 未満の値（count は 1 以上）。</summary>
        public static int Range(int seed, int a, int b, int count)
        {
            return (int)(Hash(seed, a, b) % (uint)count);
        }

        // murmur3 の最後の混ぜ方。
        private static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }
}
