namespace SkullDive.Core
{
    /// <summary>
    /// 乱数の抽象化。エンジン内のランダム要素は必ずここを通す。
    /// シード固定で完全に再現できることが、リプレイ・デイリーパズル・サーバ側の検証すべての前提になる。
    /// </summary>
    public interface IRng
    {
        /// <summary>0 以上 maxExclusive 未満の整数。maxExclusive &lt;= 1 のときは 0。</summary>
        int NextInt(int maxExclusive);

        /// <summary>[0,1) の実数。</summary>
        double NextDouble();
    }

    /// <summary>xorshift32。UnityEngine.Random に依存しないので、サーバでもテストでも同じ結果になる。</summary>
    public sealed class XorShiftRng : IRng
    {
        private uint _state;

        public XorShiftRng(int seed)
        {
            // xorshift は状態のエントロピーが低いと最初の数出力が偏る。
            // 1, 2, 3 のような小さいシードをそのまま入れると NextDouble() の 1 発目が
            // 極端に小さい値に寄り、確率判定が事実上常に真になってしまう。
            // シードを splitmix32 で撹拌してから使うことでこれを防ぐ。
            _state = Scramble((uint)seed);
        }

        private static uint Scramble(uint seed)
        {
            unchecked
            {
                uint z = seed + 0x9E3779B9u;
                z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
                z = (z ^ (z >> 13)) * 0xC2B2AE35u;
                z ^= z >> 16;
                // 0 は xorshift の吸収状態なので回避する。
                return z == 0u ? 0x9E3779B9u : z;
            }
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            // 剰余バイアスを避けるための棄却サンプリング。
            uint bound = (uint)maxExclusive;
            uint limit = uint.MaxValue - (uint.MaxValue % bound) - 1u;
            uint value;
            do
            {
                value = NextUInt();
            } while (value > limit);
            return (int)(value % bound);
        }

        public double NextDouble()
        {
            return (NextUInt() >> 8) / (double)(1 << 24);
        }
    }
}
