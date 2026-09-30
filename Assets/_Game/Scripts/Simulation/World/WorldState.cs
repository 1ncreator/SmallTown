using System.IO;

namespace SmallTown.Simulation.World
{
    public enum Weather : byte { Clear, Rain, Storm, Snow }

    public enum Season : byte { Spring, Summer, Autumn, Winter }

    public enum FireStage : byte { None, Burning, Fighting }

    public enum RobberyStage : byte { None, InProgress, PoliceOnScene }

    /// <summary>Dynamic, player-changeable state of the world. Fully serialised into snapshots.</summary>
    public sealed class WorldState
    {
        public Weather Weather = Weather.Clear;
        public Season Season = Season.Summer;
        public float RiverTarget;
        public float RiverLevel;
        public bool NorthClosed;
        public bool SouthClosed;
        public bool LotIsPark;
        public bool Festival;
        public long FestivalSince;
        public int FireBuilding = -1;
        public FireStage FireStage;
        public float FireIntensity;
        public long FireSince;
        public int RobberyBuilding = -1;
        public RobberyStage RobberyStage;
        public long RobberySince;
        public long RobberyResolveTick;
        public int FestivalPeak;
        public bool FestivalPeakReported;

        public bool BridgeClosed(int bridge) => bridge == CityData.NorthBridge ? NorthClosed : SouthClosed;

        /// <summary>Level used for passability: the water is committed to its target immediately.</summary>
        public float FloodLevel => RiverTarget > RiverLevel ? RiverTarget : RiverLevel;

        public bool BridgesFlooded => FloodLevel >= CityData.BridgeFloodY - 0.02f;

        public bool FireActive => FireBuilding >= 0 && FireStage != FireStage.None;

        public bool RobberyActive => RobberyBuilding >= 0 && RobberyStage != RobberyStage.None;

        public bool Precipitation => Weather != Weather.Clear;

        public void Write(BinaryWriter w)
        {
            w.Write((byte)Weather);
            w.Write((byte)Season);
            w.Write(RiverTarget);
            w.Write(RiverLevel);
            w.Write(NorthClosed);
            w.Write(SouthClosed);
            w.Write(LotIsPark);
            w.Write(Festival);
            w.Write(FestivalSince);
            w.Write(FireBuilding);
            w.Write((byte)FireStage);
            w.Write(FireIntensity);
            w.Write(FireSince);
            w.Write(RobberyBuilding);
            w.Write((byte)RobberyStage);
            w.Write(RobberySince);
            w.Write(RobberyResolveTick);
            w.Write(FestivalPeak);
            w.Write(FestivalPeakReported);
        }

        public void Read(BinaryReader r)
        {
            Weather = (Weather)r.ReadByte();
            Season = (Season)r.ReadByte();
            RiverTarget = r.ReadSingle();
            RiverLevel = r.ReadSingle();
            NorthClosed = r.ReadBoolean();
            SouthClosed = r.ReadBoolean();
            LotIsPark = r.ReadBoolean();
            Festival = r.ReadBoolean();
            FestivalSince = r.ReadInt64();
            FireBuilding = r.ReadInt32();
            FireStage = (FireStage)r.ReadByte();
            FireIntensity = r.ReadSingle();
            FireSince = r.ReadInt64();
            RobberyBuilding = r.ReadInt32();
            RobberyStage = (RobberyStage)r.ReadByte();
            RobberySince = r.ReadInt64();
            RobberyResolveTick = r.ReadInt64();
            FestivalPeak = r.ReadInt32();
            FestivalPeakReported = r.ReadBoolean();
        }
    }
}
