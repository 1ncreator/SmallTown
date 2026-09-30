using System.IO;

namespace SmallTown.Simulation.Agents
{
    public enum Activity : byte
    {
        Home, Work, School, Shop, Park, Promenade, Market, Festival, Bank, Church, Visit, RiversidePark, Evacuate, Flee, Lunch
    }

    public enum ResidentState : byte { Inside, Walking, Standing }

    public enum Role : byte { Worker, Student, Retired }

    public enum ForcedReason : byte { None, Fire, Robbery }

    public enum PlanReason : byte { Schedule, Weather, Festival, Emergency, Blocked, Flood, Night, Command }

    /// <summary>
    /// A townsperson. The profile (name, home, job, schedule) is generated deterministically at start;
    /// the dynamic part is written to snapshots.
    /// </summary>
    public sealed class Resident
    {
        // Profile
        public int Id;
        public string Name;
        public bool Female;
        public int Age;
        public Role Role;
        public int Home = -1;
        public int Work = -1;
        public int Friend = -1;
        public float WakeHour, LeaveHour, EndHour, BedHour;
        public bool LunchOut;
        public bool Umbrella;
        public float FestivalLove;
        public float Outdoorsy;
        public float SpeedFactor = 1f;
        public float Lateral = 0.4f;
        public uint ColorSeed;

        // Dynamic
        public Activity Act;
        public int TargetBuilding = -1;
        public int TargetNode = -1;
        public ResidentState State;
        public int InsideBuilding = -1;
        public int Node = -1;
        public int[] Path;
        public int PathIdx;
        public float EdgeT;
        public int OnCrosswalk = -1;
        public bool NoRoute;
        public long NoRouteSince;
        public long NextThink;
        public long ActSince;
        public ForcedReason Forced;
        public PlanReason Reason;
        public float X, Y, Z, PX, PY, PZ, Heading, PHeading;
        public float StandX, StandZ;
        public bool Moving;

        public bool IsOutside => State != ResidentState.Inside;

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Act);
            w.Write(TargetBuilding);
            w.Write(TargetNode);
            w.Write((byte)State);
            w.Write(InsideBuilding);
            w.Write(Node);
            if (Path == null) w.Write(-1);
            else
            {
                w.Write(Path.Length);
                for (int i = 0; i < Path.Length; i++) w.Write(Path[i]);
            }
            w.Write(PathIdx);
            w.Write(EdgeT);
            w.Write(OnCrosswalk);
            w.Write(NoRoute);
            w.Write(NoRouteSince);
            w.Write(NextThink);
            w.Write(ActSince);
            w.Write((byte)Forced);
            w.Write((byte)Reason);
            w.Write(X); w.Write(Y); w.Write(Z);
            w.Write(PX); w.Write(PY); w.Write(PZ);
            w.Write(Heading); w.Write(PHeading);
            w.Write(StandX); w.Write(StandZ);
            w.Write(Moving);
        }

        public void Read(BinaryReader r)
        {
            Act = (Activity)r.ReadByte();
            TargetBuilding = r.ReadInt32();
            TargetNode = r.ReadInt32();
            State = (ResidentState)r.ReadByte();
            InsideBuilding = r.ReadInt32();
            Node = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) Path = null;
            else
            {
                Path = new int[n];
                for (int i = 0; i < n; i++) Path[i] = r.ReadInt32();
            }
            PathIdx = r.ReadInt32();
            EdgeT = r.ReadSingle();
            OnCrosswalk = r.ReadInt32();
            NoRoute = r.ReadBoolean();
            NoRouteSince = r.ReadInt64();
            NextThink = r.ReadInt64();
            ActSince = r.ReadInt64();
            Forced = (ForcedReason)r.ReadByte();
            Reason = (PlanReason)r.ReadByte();
            X = r.ReadSingle(); Y = r.ReadSingle(); Z = r.ReadSingle();
            PX = r.ReadSingle(); PY = r.ReadSingle(); PZ = r.ReadSingle();
            Heading = r.ReadSingle(); PHeading = r.ReadSingle();
            StandX = r.ReadSingle(); StandZ = r.ReadSingle();
            Moving = r.ReadBoolean();
        }
    }
}
