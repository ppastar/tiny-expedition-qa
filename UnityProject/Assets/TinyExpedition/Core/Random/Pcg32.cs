using System;

namespace TinyExpedition.Core.Random
{
    /// <summary>
    /// PCG32 (XSH-RR). 어떤 런타임에서도 같은 시드면 같은 수열을 낸다.
    /// </summary>
    public sealed class Pcg32 : IRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;
        private ulong _state;
        private readonly ulong _inc;

        public Pcg32(ulong seed, ulong stream = 54UL)
        {
            _inc = (stream << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _inc);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            // 편향 없는 범위 축소 (Lemire 방식의 거부 샘플링)
            uint bound = (uint)maxExclusive;
            uint threshold = unchecked((uint)(-(int)bound)) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        public double NextDouble()
        {
            // 53비트 정밀도
            ulong hi = NextUInt() >> 5;  // 27 bits
            ulong lo = NextUInt() >> 6;  // 26 bits
            return (hi * 67108864.0 + lo) / 9007199254740992.0;
        }
    }
}
