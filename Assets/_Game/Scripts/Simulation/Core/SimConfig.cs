namespace SmallTown.Simulation
{
    /// <summary>All tunable parameters of the city and the simulation. Plain data, no engine types.</summary>
    public sealed class SimConfig
    {
        public int Seed = 20260923;

        // City layout
        public int BlocksWest = 3;
        public int BlocksEast = 3;
        public int BlockRows = 5;
        public float BlockSize = 26f;
        public float RoadWidth = 8f;
        public float SidewalkWidth = 2.2f;
        public float RiverWidth = 20f;
        public float EmbankmentWidth = 12f;
        public float Margin = 8f;

        // Population
        public int Residents = 200;
        public int Cars = 32;
        public int PoliceCars = 2;
        public int FireTrucks = 1;

        // Time
        public int TicksPerSecond = 10;
        /// <summary>Real seconds per in-game day at 1x speed.</summary>
        public float DayLengthSeconds = 600f;
        public float StartHour = 8f;

        // Movement (metres per simulated second, toy scale)
        public float WalkSpeed = 3.2f;
        public float CarSpeed = 10f;

        public SimConfig Clone() => (SimConfig)MemberwiseClone();

        public float TickDt => 1f / TicksPerSecond;
        public int TicksPerDay => (int)(DayLengthSeconds * TicksPerSecond);
    }
}
