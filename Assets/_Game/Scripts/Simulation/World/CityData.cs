using System.Collections.Generic;
using SmallTown.Simulation.Pathfinding;

namespace SmallTown.Simulation.World
{
    public enum Side : byte { South = 0, East = 1, North = 2, West = 3 }

    public enum CellKind : byte { Residential, Apartments, Commercial, Park, Market, School, Church, Civic, River }

    public enum BuildingType : byte
    {
        House, Apartment, Office, Shop, School, Bank, FireStation, Police, Church, MarketHall, WaterTower, Lighthouse
    }

    public enum PlaceKind : byte { Park, Square, Parking, Bridge, Building, River, Promenade }

    public enum PropKind : byte { Tree, Conifer, Bush, Lamp, Bench, Stall, Fountain, Playground, Flowerbed }

    public sealed class Cell
    {
        public int Id;
        public int Col;
        public int Row;
        public int RowEnd;
        public CellKind Kind;
        /// <summary>Block edge (curb line) rectangle.</summary>
        public float MinX, MaxX, MinZ, MaxZ;
        public bool RoadS, RoadE, RoadN, RoadW;
        public int CornerSW = -1, CornerSE = -1, CornerNE = -1, CornerNW = -1;
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterZ => (MinZ + MaxZ) * 0.5f;

        public bool HasRoad(Side s)
        {
            switch (s)
            {
                case Side.South: return RoadS;
                case Side.East: return RoadE;
                case Side.North: return RoadN;
                default: return RoadW;
            }
        }
    }

    public sealed class Building
    {
        public int Id;
        public BuildingType Type;
        public string Name;
        public int Cell;
        public Side Facing;
        public float X, Z;          // footprint centre
        public float SizeX, SizeZ;  // footprint size (world axes)
        public int Floors;
        public float Height;        // wall height (roof extra on top)
        public int EntranceNode = -1;
        public float DoorX, DoorZ;
        public int CurbLane = -1;
        public float CurbS;
        public int Capacity;        // homes: residents, work: jobs
        public uint Style;          // visual variation seed
        public int Place = -1;
        public bool IsWorkplace;
        public bool IsHome;
    }

    public sealed class Place
    {
        public int Id;
        public string Key;
        public string NameRu;
        public string NameEn;
        public PlaceKind Kind;
        public float X, Y, Z;       // label anchor
        public float MinX, MaxX, MinZ, MaxZ; // selectable area
        public int Building = -1;
        public int Bridge = -1;
        public bool ShowLabel = true;
        public readonly List<int> SpotNodes = new List<int>();
    }

    public struct Prop
    {
        public PropKind Kind;
        public float X, Y, Z, Yaw, Scale;
        public int Variant;
        /// <summary>0 = always, 1 = only when riverside lot is a park, 2 = only when it is a parking lot, 3 = central park.</summary>
        public byte Group;
    }

    public struct LotSlot
    {
        public float X, Z, Yaw;
    }

    /// <summary>Static, generated description of the town (geometry, places, graphs).</summary>
    public sealed class CityData
    {
        // Vertical profile (metres). Normal river surface is y = 0.
        public const float StreetY = 2.0f;
        public const float CurbY = 2.12f;
        public const float TerraceY = 0.9f;
        public const float LotY = 0.45f;
        public const float BridgeFloodY = 1.7f;
        public const float ChannelBottomY = -1.6f;
        public const float SlabBottomY = -4.5f;
        public const float MaxRiverLevel = 1.9f;
        public const float MinRiverLevel = -1.0f;
        public const int NorthBridge = 0;
        public const int SouthBridge = 1;

        public SimConfig Config;
        public float[] RoadX;
        public float[] RoadZ;
        public int RiverGap;              // index i: river lies between RoadX[i] and RoadX[i+1]
        public int[] BridgeRow = new int[2]; // z-line index per bridge id
        public float RiverCenterX, RiverWestBank, RiverEastBank;
        public float WestWallX, EastWallX; // terrace retaining walls (street-level strip ends)
        public float MinX, MaxX, MinZ, MaxZ;

        public readonly List<Cell> Cells = new List<Cell>();
        public readonly List<Building> Buildings = new List<Building>();
        public readonly List<Place> Places = new List<Place>();
        public readonly List<Prop> Props = new List<Prop>();
        public readonly List<LotSlot> LotSlots = new List<LotSlot>();
        public NavGraph Walk;
        public RoadNetwork Roads;
        public int[,] CellGrid;           // [col,row] -> cell id

        public int PlaceCentralPark, PlaceMarket, PlaceParking, PlaceNorthBridge, PlaceSouthBridge, PlaceRiver, PlacePromenade;
        public int PlaceSchool, PlaceBank, PlaceFire, PlacePolice, PlaceChurch, PlaceWaterTower, PlaceLighthouse;
        public int SchoolBuilding, BankBuilding, FireStationBuilding, PoliceBuilding, ChurchBuilding, MarketHallBuilding;

        public float StageX, StageZ, StageYaw;
        public int FestivalNode;
        public float LotMinX, LotMaxX, LotMinZ, LotMaxZ;
        public int LotCurbLane = -1;
        public float LotCurbS;
        public float RampTopX, RampTopZ, RampBottomX, RampBottomZ;

        public readonly List<int> ParkSpotNodes = new List<int>();
        public readonly List<int> MarketSpotNodes = new List<int>();
        public readonly List<int> PromenadeNodes = new List<int>();
        public readonly List<int> RiversideNodes = new List<int>();
        public readonly List<int> SafeNodes = new List<int>();

        public string[] StreetNamesH;
        public string[] StreetNamesV;

        public float BridgeZ(int bridge) => RoadZ[BridgeRow[bridge]];

        public Place PlaceByKey(string key)
        {
            for (int i = 0; i < Places.Count; i++)
                if (Places[i].Key == key) return Places[i];
            return null;
        }

        public int CellAt(int col, int row)
        {
            if (col < 0 || row < 0 || col >= CellGrid.GetLength(0) || row >= CellGrid.GetLength(1)) return -1;
            return CellGrid[col, row];
        }
    }
}
