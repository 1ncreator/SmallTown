using System.Collections.Generic;
using System.Globalization;

namespace SmallTown.Simulation.Events
{
    public enum CommandKind : byte
    {
        None,
        CloseBridge,
        OpenBridge,
        SetWeather,
        RiverDelta,
        RiverSet,
        RiverNormal,
        SetTime,
        SetSeason,
        StartFestival,
        EndFestival,
        LotToPark,
        ParkToLot,
        Robbery,
        StartFire,
        Extinguish
    }

    /// <summary>
    /// A change to the world. Every mutation of the simulation from outside goes through one of these,
    /// which makes them replayable (determinism tests) and undoable (snapshots are taken before them).
    /// </summary>
    public struct WorldCommand
    {
        public CommandKind Kind;
        /// <summary>Bridge (0 north, 1 south, 2 both), weather, season or building id.</summary>
        public int IntArg;
        /// <summary>Metres for river commands, hour for SetTime.</summary>
        public float FloatArg;

        public WorldCommand(CommandKind kind, int intArg = 0, float floatArg = 0f)
        {
            Kind = kind;
            IntArg = intArg;
            FloatArg = floatArg;
        }

        public override string ToString() => Kind + "(" + IntArg + ", " + FloatArg.ToString(CultureInfo.InvariantCulture) + ")";
    }

    public enum EntityKind : byte { None, Place, Building, Resident, Vehicle }

    public struct EntityRef
    {
        public EntityKind Kind;
        public int Id;

        public EntityRef(EntityKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static readonly EntityRef None = new EntityRef(EntityKind.None, -1);
        public bool IsNone => Kind == EntityKind.None;
    }

    /// <summary>Result card of a command: headline plus lines built from real simulation numbers.</summary>
    public sealed class CommandOutcome
    {
        public bool Success = true;
        public bool Changed = true;
        public string Title = "";
        public readonly List<string> Lines = new List<string>();
        public EntityRef Entity = EntityRef.None;
        public int PeopleRerouted;
        public int PeopleChangedPlans;
        public int VehiclesRerouted;
        public int VehiclesWaiting;
        public int RoutesUnavailable;
    }
}
