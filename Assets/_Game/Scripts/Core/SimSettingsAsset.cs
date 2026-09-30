using SmallTown.Simulation;
using UnityEngine;

namespace SmallTown.Core
{
    /// <summary>Designer-facing knobs for the town. Edit Assets/_Game/Configs/SimSettings.asset.</summary>
    [CreateAssetMenu(menuName = "Small Town/Sim Settings", fileName = "SimSettings")]
    public sealed class SimSettingsAsset : ScriptableObject
    {
        [Header("World")]
        [Tooltip("Same seed = same town and same simulation.")]
        [SerializeField] private int seed = 20260923;
        [SerializeField, Range(3, 5)] private int blocksWest = 3;
        [SerializeField, Range(3, 5)] private int blocksEast = 3;
        [SerializeField, Range(4, 7)] private int blockRows = 5;
        [SerializeField] private float blockSize = 26f;

        [Header("Population")]
        [SerializeField, Range(20, 400)] private int residents = 200;
        [SerializeField, Range(0, 60)] private int cars = 32;
        [SerializeField, Range(0, 4)] private int policeCars = 2;
        [SerializeField, Range(0, 2)] private int fireTrucks = 1;

        [Header("Time")]
        [Tooltip("Real seconds per in-game day at 1x speed.")]
        [SerializeField] private float dayLengthSeconds = 600f;
        [SerializeField, Range(0f, 24f)] private float startHour = 8f;
        [SerializeField] private int ticksPerSecond = 10;

        [Header("Movement (m per simulated second)")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float carSpeed = 10f;

        public SimConfig ToConfig()
        {
            return new SimConfig
            {
                Seed = seed,
                BlocksWest = blocksWest,
                BlocksEast = blocksEast,
                BlockRows = blockRows,
                BlockSize = blockSize,
                Residents = residents,
                Cars = cars,
                PoliceCars = policeCars,
                FireTrucks = fireTrucks,
                DayLengthSeconds = dayLengthSeconds,
                StartHour = startHour,
                TicksPerSecond = Mathf.Max(1, ticksPerSecond),
                WalkSpeed = walkSpeed,
                CarSpeed = carSpeed
            };
        }
    }
}
