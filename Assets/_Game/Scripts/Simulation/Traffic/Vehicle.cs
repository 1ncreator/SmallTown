using System.Collections.Generic;
using System.IO;

namespace SmallTown.Simulation.Traffic
{
    public enum VehicleKind : byte { Car, Police, FireTruck }

    public enum VehicleState : byte { Parked, Driving, Transit, LotEnter, LotParked, LotExit, OnScene, Idle }

    public enum DestKind : byte { None, Building, Lot, Base, Scene }

    public sealed class Vehicle
    {
        // Profile
        public int Id;
        public VehicleKind Kind;
        public int ColorIndex;
        public float HalfLen = 2.2f;
        public int HomeBuilding = -1;
        public float HomeOffset;

        // Dynamic
        public VehicleState State;
        public int Lane = -1;
        public float S;
        public float V;
        public int Turn = -1;
        public float T;
        public int TransitInter = -1;
        public readonly List<int> Route = new List<int>(24);
        public int RouteIdx;
        public DestKind Dest;
        public int DestBuilding = -1;
        public int DestLane = -1;
        public float DestS;
        public long DwellUntil;
        public bool Granted;
        public int GrantInter = -1;
        public long RequestTick = -1;
        public int StoppedTicks;
        public bool NoRoute;
        public bool PullOver;
        public long NextRetry;
        public bool Siren;
        public int LotSlot = -1;
        public float LotProgress;
        public float X, Y, Z, PX, PY, PZ, Heading, PHeading;

        public bool OnRoad => State == VehicleState.Driving || State == VehicleState.Transit;

        public void Write(BinaryWriter w)
        {
            w.Write((byte)State);
            w.Write(Lane);
            w.Write(S);
            w.Write(V);
            w.Write(Turn);
            w.Write(T);
            w.Write(TransitInter);
            w.Write(Route.Count);
            for (int i = 0; i < Route.Count; i++) w.Write(Route[i]);
            w.Write(RouteIdx);
            w.Write((byte)Dest);
            w.Write(DestBuilding);
            w.Write(DestLane);
            w.Write(DestS);
            w.Write(DwellUntil);
            w.Write(Granted);
            w.Write(GrantInter);
            w.Write(RequestTick);
            w.Write(StoppedTicks);
            w.Write(NoRoute);
            w.Write(PullOver);
            w.Write(NextRetry);
            w.Write(Siren);
            w.Write(LotSlot);
            w.Write(LotProgress);
            w.Write(X); w.Write(Y); w.Write(Z);
            w.Write(PX); w.Write(PY); w.Write(PZ);
            w.Write(Heading); w.Write(PHeading);
        }

        public void Read(BinaryReader r)
        {
            State = (VehicleState)r.ReadByte();
            Lane = r.ReadInt32();
            S = r.ReadSingle();
            V = r.ReadSingle();
            Turn = r.ReadInt32();
            T = r.ReadSingle();
            TransitInter = r.ReadInt32();
            int n = r.ReadInt32();
            Route.Clear();
            for (int i = 0; i < n; i++) Route.Add(r.ReadInt32());
            RouteIdx = r.ReadInt32();
            Dest = (DestKind)r.ReadByte();
            DestBuilding = r.ReadInt32();
            DestLane = r.ReadInt32();
            DestS = r.ReadSingle();
            DwellUntil = r.ReadInt64();
            Granted = r.ReadBoolean();
            GrantInter = r.ReadInt32();
            RequestTick = r.ReadInt64();
            StoppedTicks = r.ReadInt32();
            NoRoute = r.ReadBoolean();
            PullOver = r.ReadBoolean();
            NextRetry = r.ReadInt64();
            Siren = r.ReadBoolean();
            LotSlot = r.ReadInt32();
            LotProgress = r.ReadSingle();
            X = r.ReadSingle(); Y = r.ReadSingle(); Z = r.ReadSingle();
            PX = r.ReadSingle(); PY = r.ReadSingle(); PZ = r.ReadSingle();
            Heading = r.ReadSingle(); PHeading = r.ReadSingle();
        }
    }
}
