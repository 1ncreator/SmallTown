namespace SmallTown.Utils
{
    /// <summary>
    /// Deterministic xoroshiro128+ generator. The whole simulation uses only this RNG,
    /// so a seed plus a command sequence always reproduces the same world.
    /// </summary>
    public sealed class DetRandom
    {
        private ulong _s0;
        private ulong _s1;

        public DetRandom(ulong seed)
        {
            SetSeed(seed);
        }

        public ulong State0 => _s0;
        public ulong State1 => _s1;

        public void SetState(ulong s0, ulong s1)
        {
            _s0 = s0;
            _s1 = s1;
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        public void SetSeed(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            if (_s0 == 0 && _s1 == 0) _s1 = 1;
        }

        private static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong s0 = _s0;
            ulong s1 = _s1;
            ulong result = s0 + s1;
            s1 ^= s0;
            _s0 = Rotl(s0, 24) ^ s1 ^ (s1 << 16);
            _s1 = Rotl(s1, 37);
            return result;
        }

        /// <summary>Integer in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            return (int)((NextULong() >> 33) % (ulong)maxExclusive);
        }

        public int Range(int min, int maxExclusive) => min + Next(maxExclusive - min);

        /// <summary>Float in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float p) => NextFloat() < p;

        public T Pick<T>(T[] items) => items[Next(items.Length)];

        /// <summary>Stateless integer hash, handy for per-agent deterministic variation.</summary>
        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        public static uint Hash(int a, int b, int c = 0)
        {
            return Hash((uint)a * 0x9E3779B1U ^ Hash((uint)b + 0x85EBCA6BU) ^ Hash((uint)c * 0xC2B2AE35U + 0x27D4EB2FU));
        }

        public static float Hash01(int a, int b, int c = 0) => (Hash(a, b, c) >> 8) * (1f / 16777216f);
    }
}
