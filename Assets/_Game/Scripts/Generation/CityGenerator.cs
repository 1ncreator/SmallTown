using System;
using System.Collections.Generic;
using SmallTown.Simulation;
using SmallTown.Simulation.Pathfinding;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Generation
{
    /// <summary>
    /// Deterministic procedural town generator. Produces the block grid, the river with
    /// embankments and exactly two bridges, buildings with entrances, named places,
    /// decorative props, the pedestrian graph and the lane graph.
    /// </summary>
    public static class CityGenerator
    {
        private struct RingPoint
        {
            public float T;
            public int Node;
        }

        private sealed class Ctx
        {
            public CityData C;
            public SimConfig Cfg;
            public DetRandom Rng;
            public NavGraph W;
            public List<RingPoint>[] Ring;
            public float R, SW;
            public RoadSegment[,] VSeg;
            public RoadSegment[,] HSeg;
            public int Rows, Cols, NX, RiverCol;
            public int ShopName, OfficeName, AptName, HouseNo = 1;
            public readonly Dictionary<int, BuildingType> Civic = new Dictionary<int, BuildingType>();
            public int WaterTowerCell = -1;
            public readonly List<float> WestStairs = new List<float>();
            public readonly List<float> EastStairs = new List<float>();
        }

        public static CityData Generate(SimConfig cfg)
        {
            if (cfg.BlockRows < 4) cfg.BlockRows = 4;
            if (cfg.BlocksWest < 3) cfg.BlocksWest = 3;
            if (cfg.BlocksEast < 3) cfg.BlocksEast = 3;
            var x = new Ctx
            {
                C = new CityData { Config = cfg },
                Cfg = cfg,
                Rng = new DetRandom((ulong)(uint)cfg.Seed * 2654435761UL + 1013904223UL),
                W = new NavGraph(),
                R = cfg.RoadWidth,
                SW = cfg.SidewalkWidth
            };
            x.C.Walk = x.W;
            Layout(x);
            BuildRoads(x);
            BuildCells(x);
            AssignKinds(x);
            x.Ring = new List<RingPoint>[x.C.Cells.Count * 4];
            for (int i = 0; i < x.Ring.Length; i++) x.Ring[i] = new List<RingPoint>();
            BuildCorners(x);
            BuildCrosswalks(x);
            BuildLot(x);
            BuildBlocks(x);
            BuildRiverside(x);
            ConnectRings(x);
            x.W.Freeze();
            x.C.Roads.BuildLanes(x.R);
            AssignCurbs(x);
            BuildPlaces(x);
            BuildProps(x);
            x.W.UpdateBlocking(false, false, 0f);
            x.C.Roads.UpdateBlocking(false, false, false);
            return x.C;
        }

        // ------------------------------------------------------------------ layout

        private static void Layout(Ctx x)
        {
            var cfg = x.Cfg;
            var c = x.C;
            int nW = cfg.BlocksWest, nE = cfg.BlocksEast, rows = cfg.BlockRows;
            float R = cfg.RoadWidth, B = cfg.BlockSize;
            x.NX = nW + 1 + nE + 1;
            x.Rows = rows;
            x.Cols = x.NX - 1;
            x.RiverCol = nW;
            var X = new float[x.NX];
            X[0] = 0f;
            for (int i = 1; i <= nW; i++) X[i] = X[i - 1] + R + B;
            X[nW + 1] = X[nW] + R + 2f * cfg.EmbankmentWidth + cfg.RiverWidth;
            for (int i = nW + 2; i < x.NX; i++) X[i] = X[i - 1] + R + B;
            var Z = new float[rows + 1];
            for (int j = 0; j <= rows; j++) Z[j] = j * (R + B);
            float cx = (X[0] + X[x.NX - 1]) * 0.5f, cz = (Z[0] + Z[rows]) * 0.5f;
            for (int i = 0; i < X.Length; i++) X[i] -= cx;
            for (int j = 0; j < Z.Length; j++) Z[j] -= cz;
            c.RoadX = X;
            c.RoadZ = Z;
            c.RiverGap = nW;
            c.BridgeRow[CityData.NorthBridge] = rows - 1;
            c.BridgeRow[CityData.SouthBridge] = 1;
            c.RiverCenterX = (X[nW] + X[nW + 1]) * 0.5f;
            c.RiverWestBank = c.RiverCenterX - cfg.RiverWidth * 0.5f;
            c.RiverEastBank = c.RiverCenterX + cfg.RiverWidth * 0.5f;
            c.WestWallX = X[nW] + R * 0.5f + cfg.SidewalkWidth;
            c.EastWallX = X[nW + 1] - R * 0.5f - cfg.SidewalkWidth;
            c.MinX = X[0] - R * 0.5f - cfg.Margin;
            c.MaxX = X[x.NX - 1] + R * 0.5f + cfg.Margin;
            c.MinZ = Z[0] - R * 0.5f - cfg.Margin;
            c.MaxZ = Z[rows] + R * 0.5f + cfg.Margin;
            c.StreetNamesH = new string[rows + 1];
            for (int j = 0; j <= rows; j++) c.StreetNamesH[j] = j < NameTables.StreetsH.Length ? NameTables.StreetsH[j] : ("Улица " + (j + 1));
            c.StreetNamesV = new string[x.NX];
            for (int i = 0; i < x.NX; i++) c.StreetNamesV[i] = i < NameTables.StreetsV.Length ? NameTables.StreetsV[i] : ("Проезд " + (i + 1));
        }

        private static bool IsBridgeRow(Ctx x, int zLine) => zLine == x.C.BridgeRow[0] || zLine == x.C.BridgeRow[1];

        private static int InterId(Ctx x, int i, int j) => i * (x.Rows + 1) + j;

        private static void BuildRoads(Ctx x)
        {
            var c = x.C;
            var rn = new RoadNetwork();
            c.Roads = rn;
            int nI = x.NX * (x.Rows + 1);
            rn.IntersectionCount = nI;
            rn.IX = new float[nI];
            rn.IZ = new float[nI];
            for (int i = 0; i < x.NX; i++)
                for (int j = 0; j <= x.Rows; j++)
                {
                    int id = InterId(x, i, j);
                    rn.IX[id] = c.RoadX[i];
                    rn.IZ[id] = c.RoadZ[j];
                }
            x.VSeg = new RoadSegment[x.NX, x.Rows];
            x.HSeg = new RoadSegment[x.Rows + 1, x.NX - 1];
            for (int i = 0; i < x.NX; i++)
                for (int j = 0; j < x.Rows; j++)
                {
                    var s = new RoadSegment { Id = rn.Segments.Count, A = InterId(x, i, j), B = InterId(x, i, j + 1), Horizontal = false, Line = i, Span = j };
                    rn.Segments.Add(s);
                    x.VSeg[i, j] = s;
                }
            for (int j = 0; j <= x.Rows; j++)
                for (int i = 0; i < x.NX - 1; i++)
                {
                    int bridge = -1;
                    if (i == x.RiverCol)
                    {
                        if (!IsBridgeRow(x, j)) continue;
                        bridge = j == c.BridgeRow[CityData.NorthBridge] ? CityData.NorthBridge : CityData.SouthBridge;
                    }
                    var s = new RoadSegment { Id = rn.Segments.Count, A = InterId(x, i, j), B = InterId(x, i + 1, j), Horizontal = true, Line = j, Span = i, Bridge = bridge };
                    rn.Segments.Add(s);
                    x.HSeg[j, i] = s;
                }
        }

        private static void BuildCells(Ctx x)
        {
            var c = x.C;
            float h = x.R * 0.5f;
            c.CellGrid = new int[x.Cols, x.Rows];
            for (int col = 0; col < x.Cols; col++)
            {
                if (col == x.RiverCol)
                {
                    var bounds = new List<int> { 0 };
                    int s = Math.Min(c.BridgeRow[0], c.BridgeRow[1]), n = Math.Max(c.BridgeRow[0], c.BridgeRow[1]);
                    bounds.Add(s);
                    bounds.Add(n);
                    bounds.Add(x.Rows);
                    for (int k = 0; k + 1 < bounds.Count; k++)
                    {
                        int r0 = bounds[k], r1 = bounds[k + 1] - 1;
                        if (r1 < r0) continue;
                        var cell = new Cell
                        {
                            Id = c.Cells.Count, Col = col, Row = r0, RowEnd = r1, Kind = CellKind.River,
                            MinX = c.RoadX[col] + h, MaxX = c.RoadX[col + 1] - h,
                            MinZ = c.RoadZ[r0] + h, MaxZ = c.RoadZ[r1 + 1] - h,
                            RoadW = true, RoadE = true, RoadS = IsBridgeRow(x, r0), RoadN = IsBridgeRow(x, r1 + 1)
                        };
                        c.Cells.Add(cell);
                        for (int r = r0; r <= r1; r++) c.CellGrid[col, r] = cell.Id;
                    }
                    continue;
                }
                for (int row = 0; row < x.Rows; row++)
                {
                    var cell = new Cell
                    {
                        Id = c.Cells.Count, Col = col, Row = row, RowEnd = row, Kind = CellKind.Residential,
                        MinX = c.RoadX[col] + h, MaxX = c.RoadX[col + 1] - h,
                        MinZ = c.RoadZ[row] + h, MaxZ = c.RoadZ[row + 1] - h,
                        RoadW = true, RoadE = true, RoadS = true, RoadN = true
                    };
                    c.Cells.Add(cell);
                    c.CellGrid[col, row] = cell.Id;
                }
            }
        }

        private static void AssignKinds(Ctx x)
        {
            var c = x.C;
            int mid = x.Rows / 2;
            int rc = x.RiverCol;
            var taken = new HashSet<int>();

            void Set(int col, int row, CellKind kind)
            {
                col = MathUtil.Clamp(col, 0, x.Cols - 1);
                row = MathUtil.Clamp(row, 0, x.Rows - 1);
                if (col == rc) col = rc - 1;
                int id = c.CellGrid[col, row];
                if (taken.Contains(id)) return;
                taken.Add(id);
                c.Cells[id].Kind = kind;
            }

            Set(rc - 2, mid, CellKind.Market);
            Set(rc + 2, mid, CellKind.Park);
            Set(x.Cols - 1, mid + 1, CellKind.School);
            Set(rc - 1, x.Rows - 1, CellKind.Church);
            int bankCell = c.CellGrid[rc - 2, mid + 1];
            int fireCell = c.CellGrid[0, 1];
            int policeCell = c.CellGrid[rc + 1, 1];
            foreach (var pair in new[] { (bankCell, BuildingType.Bank), (fireCell, BuildingType.FireStation), (policeCell, BuildingType.Police) })
            {
                if (taken.Contains(pair.Item1)) continue;
                taken.Add(pair.Item1);
                c.Cells[pair.Item1].Kind = CellKind.Civic;
                x.Civic[pair.Item1] = pair.Item2;
            }
            x.WaterTowerCell = c.CellGrid[x.Cols - 1, 0];
            taken.Add(x.WaterTowerCell);

            // Remaining cells: commercial near the centre, some apartment blocks, the rest houses.
            var free = new List<int>();
            for (int i = 0; i < c.Cells.Count; i++)
                if (c.Cells[i].Kind != CellKind.River && !taken.Contains(i)) free.Add(i);
            var score = new Dictionary<int, float>();
            foreach (int id in free)
            {
                var cell = c.Cells[id];
                float d = Math.Abs(cell.Col - rc) + Math.Abs(cell.Row - mid) * 0.8f;
                score[id] = d + x.Rng.Range(0f, 1.6f);
            }
            free.Sort((a, b) => score[a].CompareTo(score[b]) != 0 ? score[a].CompareTo(score[b]) : a.CompareTo(b));
            int commercial = Math.Max(3, free.Count / 4);
            for (int i = 0; i < free.Count; i++)
                c.Cells[free[i]].Kind = i < commercial ? CellKind.Commercial : CellKind.Residential;
            var rest = free.GetRange(commercial, free.Count - commercial);
            Shuffle(x.Rng, rest);
            int apts = Math.Max(2, free.Count / 7);
            for (int i = 0; i < apts && i < rest.Count; i++) c.Cells[rest[i]].Kind = CellKind.Apartments;
        }

        private static void Shuffle<T>(DetRandom rng, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }

        // ------------------------------------------------------------------ rings & crosswalks

        private static void RingRect(Ctx x, Cell cell, out float rx0, out float rx1, out float rz0, out float rz1)
        {
            float h = x.SW * 0.5f;
            rx0 = cell.MinX + h;
            rx1 = cell.MaxX - h;
            rz0 = cell.MinZ + h;
            rz1 = cell.MaxZ - h;
        }

        private static void BuildCorners(Ctx x)
        {
            foreach (var cell in x.C.Cells)
            {
                RingRect(x, cell, out float rx0, out float rx1, out float rz0, out float rz1);
                cell.CornerSW = x.W.AddNode(rx0, CityData.CurbY, rz0, float.MaxValue, cell.Id);
                cell.CornerSE = x.W.AddNode(rx1, CityData.CurbY, rz0, float.MaxValue, cell.Id);
                cell.CornerNE = x.W.AddNode(rx1, CityData.CurbY, rz1, float.MaxValue, cell.Id);
                cell.CornerNW = x.W.AddNode(rx0, CityData.CurbY, rz1, float.MaxValue, cell.Id);
                int b = cell.Id * 4;
                x.Ring[b + (int)Side.South].Add(new RingPoint { T = rx0, Node = cell.CornerSW });
                x.Ring[b + (int)Side.South].Add(new RingPoint { T = rx1, Node = cell.CornerSE });
                x.Ring[b + (int)Side.East].Add(new RingPoint { T = rz0, Node = cell.CornerSE });
                x.Ring[b + (int)Side.East].Add(new RingPoint { T = rz1, Node = cell.CornerNE });
                x.Ring[b + (int)Side.North].Add(new RingPoint { T = rx0, Node = cell.CornerNW });
                x.Ring[b + (int)Side.North].Add(new RingPoint { T = rx1, Node = cell.CornerNE });
                x.Ring[b + (int)Side.West].Add(new RingPoint { T = rz0, Node = cell.CornerSW });
                x.Ring[b + (int)Side.West].Add(new RingPoint { T = rz1, Node = cell.CornerNW });
            }
        }

        private static int AddRingPoint(Ctx x, Cell cell, Side side, float t)
        {
            RingRect(x, cell, out float rx0, out float rx1, out float rz0, out float rz1);
            bool alongX = side == Side.South || side == Side.North;
            float lo = alongX ? rx0 : rz0, hi = alongX ? rx1 : rz1;
            t = MathUtil.Clamp(t, lo, hi);
            var list = x.Ring[cell.Id * 4 + (int)side];
            for (int i = 0; i < list.Count; i++)
                if (Math.Abs(list[i].T - t) < 0.5f) return list[i].Node;
            float px, pz;
            switch (side)
            {
                case Side.South: px = t; pz = rz0; break;
                case Side.North: px = t; pz = rz1; break;
                case Side.West: px = rx0; pz = t; break;
                default: px = rx1; pz = t; break;
            }
            int node = x.W.AddNode(px, CityData.CurbY, pz, float.MaxValue, cell.Id);
            list.Add(new RingPoint { T = t, Node = node });
            return node;
        }

        private static void AddCrosswalk(Ctx x, RoadSegment seg, bool atA, int a, int b, bool alongX)
        {
            var rn = x.C.Roads;
            int id = rn.Crosswalks.Count;
            int e = x.W.AddEdge(a, b, WalkEdgeKind.Crosswalk, -1, id);
            rn.Crosswalks.Add(new Crosswalk
            {
                Id = id, Segment = seg.Id, WalkEdge = e, AlongX = alongX,
                X = (x.W.NX(a) + x.W.NX(b)) * 0.5f, Z = (x.W.NZ(a) + x.W.NZ(b)) * 0.5f
            });
            if (atA) seg.CwA = id;
            else seg.CwB = id;
        }

        private static void BuildCrosswalks(Ctx x)
        {
            var c = x.C;
            float off = x.R * 0.5f + x.SW * 0.5f;
            for (int i = 0; i < x.NX; i++)
                for (int j = 0; j < x.Rows; j++)
                {
                    int left = c.CellAt(i - 1, j), right = c.CellAt(i, j);
                    if (left < 0 || right < 0 || left == right) continue;
                    var seg = x.VSeg[i, j];
                    float za = c.RoadZ[j] + off, zb = c.RoadZ[j + 1] - off;
                    AddCrosswalk(x, seg, true, AddRingPoint(x, c.Cells[left], Side.East, za), AddRingPoint(x, c.Cells[right], Side.West, za), true);
                    AddCrosswalk(x, seg, false, AddRingPoint(x, c.Cells[left], Side.East, zb), AddRingPoint(x, c.Cells[right], Side.West, zb), true);
                }
            for (int j = 0; j <= x.Rows; j++)
                for (int i = 0; i < x.NX - 1; i++)
                {
                    var seg = x.HSeg[j, i];
                    if (seg == null) continue;
                    int below = c.CellAt(i, j - 1), above = c.CellAt(i, j);
                    if (below < 0 || above < 0 || below == above) continue;
                    float xa = c.RoadX[i] + off, xb = c.RoadX[i + 1] - off;
                    AddCrosswalk(x, seg, true, AddRingPoint(x, c.Cells[below], Side.North, xa), AddRingPoint(x, c.Cells[above], Side.South, xa), false);
                    AddCrosswalk(x, seg, false, AddRingPoint(x, c.Cells[below], Side.North, xb), AddRingPoint(x, c.Cells[above], Side.South, xb), false);
                }
        }

        private static void ConnectRings(Ctx x)
        {
            var c = x.C;
            foreach (var cell in c.Cells)
            {
                for (int s = 0; s < 4; s++)
                {
                    var side = (Side)s;
                    if (!cell.HasRoad(side)) continue;
                    var list = x.Ring[cell.Id * 4 + s];
                    list.Sort((a, b) => a.T.CompareTo(b.T) != 0 ? a.T.CompareTo(b.T) : a.Node.CompareTo(b.Node));
                    for (int k = 0; k + 1 < list.Count; k++)
                    {
                        var a = list[k];
                        var b = list[k + 1];
                        if (a.Node == b.Node) continue;
                        bool isBridge = false;
                        int bridge = -1;
                        if (cell.Kind == CellKind.River && (side == Side.South || side == Side.North))
                        {
                            float midT = (a.T + b.T) * 0.5f;
                            if (midT > c.WestWallX + 0.1f && midT < c.EastWallX - 0.1f)
                            {
                                isBridge = true;
                                int zLine = side == Side.South ? cell.Row : cell.RowEnd + 1;
                                bridge = zLine == c.BridgeRow[CityData.NorthBridge] ? CityData.NorthBridge : CityData.SouthBridge;
                            }
                        }
                        if (isBridge) x.W.AddEdge(a.Node, b.Node, WalkEdgeKind.Bridge, bridge, -1, CityData.BridgeFloodY);
                        else x.W.AddEdge(a.Node, b.Node, WalkEdgeKind.Sidewalk);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ buildings

        private static Building AddBuilding(Ctx x, Cell cell, BuildingType type, float bx, float bz, float sx, float sz, Side facing, int floors, float height, string name)
        {
            var b = new Building
            {
                Id = x.C.Buildings.Count, Type = type, Name = name, Cell = cell.Id, Facing = facing,
                X = bx, Z = bz, SizeX = sx, SizeZ = sz, Floors = floors, Height = height,
                Style = (uint)(x.Rng.NextULong() >> 16)
            };
            float param;
            switch (facing)
            {
                case Side.South: param = bx; b.DoorX = bx; b.DoorZ = bz - sz * 0.5f; break;
                case Side.North: param = bx; b.DoorX = bx; b.DoorZ = bz + sz * 0.5f; break;
                case Side.West: param = bz; b.DoorX = bx - sx * 0.5f; b.DoorZ = bz; break;
                default: param = bz; b.DoorX = bx + sx * 0.5f; b.DoorZ = bz; break;
            }
            b.EntranceNode = AddRingPoint(x, cell, facing, param);
            x.C.Buildings.Add(b);
            return b;
        }

        private static void Inner(Ctx x, Cell cell, out float ix0, out float ix1, out float iz0, out float iz1)
        {
            ix0 = cell.MinX + x.SW;
            ix1 = cell.MaxX - x.SW;
            iz0 = cell.MinZ + x.SW;
            iz1 = cell.MaxZ - x.SW;
        }

        private static void AddProp(Ctx x, PropKind kind, float px, float py, float pz, float yaw, float scale, byte group = 0)
        {
            x.C.Props.Add(new Prop { Kind = kind, X = px, Y = py, Z = pz, Yaw = yaw, Scale = scale, Variant = x.Rng.Next(4), Group = group });
        }

        private static void BuildBlocks(Ctx x)
        {
            var c = x.C;
            foreach (var cell in c.Cells)
            {
                switch (cell.Kind)
                {
                    case CellKind.River: break;
                    case CellKind.Residential: BuildHouses(x, cell, 0); break;
                    case CellKind.Apartments: BuildApartments(x, cell); break;
                    case CellKind.Commercial: BuildCommercial(x, cell); break;
                    case CellKind.Park: BuildPark(x, cell); break;
                    case CellKind.Market: BuildMarket(x, cell); break;
                    case CellKind.School: BuildSchool(x, cell); break;
                    case CellKind.Church: BuildChurch(x, cell); break;
                    case CellKind.Civic: BuildCivic(x, cell); break;
                }
            }
        }

        /// <summary>Fills the 2x2 lots of a block with houses. usedMask bit (lz*2+lx) marks taken lots.</summary>
        private static void BuildHouses(Ctx x, Cell cell, int usedMask, float shopChance = 0f)
        {
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            float lw = (ix1 - ix0) * 0.5f, lh = (iz1 - iz0) * 0.5f;
            for (int lz = 0; lz < 2; lz++)
                for (int lx = 0; lx < 2; lx++)
                {
                    int bit = 1 << (lz * 2 + lx);
                    if ((usedMask & bit) != 0) continue;
                    float lx0 = ix0 + lx * lw, lz0 = iz0 + lz * lh;
                    if (cell.Id == x.WaterTowerCell && lx == 1 && lz == 1)
                    {
                        var wt = AddBuilding(x, cell, BuildingType.WaterTower, lx0 + lw * 0.5f, lz0 + lh * 0.5f, 6f, 6f, Side.North, 1, 16f, "Водонапорная башня");
                        wt.Capacity = 0;
                        x.C.Props.Add(new Prop { Kind = PropKind.Bush, X = lx0 + 1.5f, Y = CityData.CurbY, Z = lz0 + 1.5f, Scale = 1f });
                        continue;
                    }
                    Side sideX = lx == 0 ? Side.West : Side.East;
                    Side sideZ = lz == 0 ? Side.South : Side.North;
                    Side facing = x.Rng.Chance(0.5f) ? sideX : sideZ;
                    bool shop = x.Rng.Chance(shopChance);
                    float w = x.Rng.Range(5.8f, 7.4f), d = x.Rng.Range(5.8f, 7.4f);
                    int floors = x.Rng.Chance(0.4f) ? 2 : 1;
                    float bx = lx0 + lw * 0.5f + x.Rng.Range(-0.6f, 0.6f);
                    float bz = lz0 + lh * 0.5f + x.Rng.Range(-0.6f, 0.6f);
                    const float setback = 1.3f;
                    switch (facing)
                    {
                        case Side.South: bz = lz0 + setback + d * 0.5f; break;
                        case Side.North: bz = lz0 + lh - setback - d * 0.5f; break;
                        case Side.West: bx = lx0 + setback + w * 0.5f; break;
                        default: bx = lx0 + lw - setback - w * 0.5f; break;
                    }
                    Building b;
                    if (shop)
                    {
                        b = AddBuilding(x, cell, BuildingType.Shop, bx, bz, w + 0.6f, d, facing, floors, floors * 3.3f, NameTables.Shops[x.ShopName++ % NameTables.Shops.Length]);
                        b.Capacity = 4;
                        b.IsWorkplace = true;
                    }
                    else
                    {
                        b = AddBuilding(x, cell, BuildingType.House, bx, bz, w, d, facing, floors, floors * 3.1f, "Дом №" + (x.HouseNo++));
                        b.Capacity = floors == 2 ? 4 : 3;
                        b.IsHome = true;
                    }
                    // Back-yard tree
                    if (x.Rng.Chance(0.75f))
                    {
                        float tx = lx0 + lw * 0.5f, tz = lz0 + lh * 0.5f;
                        float back = lw * 0.5f - 1.5f;
                        switch (facing)
                        {
                            case Side.South: tz = lz0 + lh - 1.4f; tx += (lx == 0 ? -back : back) * 0.6f; break;
                            case Side.North: tz = lz0 + 1.4f; tx += (lx == 0 ? -back : back) * 0.6f; break;
                            case Side.West: tx = lx0 + lw - 1.4f; tz += (lz == 0 ? -back : back) * 0.6f; break;
                            default: tx = lx0 + 1.4f; tz += (lz == 0 ? -back : back) * 0.6f; break;
                        }
                        AddProp(x, x.Rng.Chance(0.25f) ? PropKind.Conifer : PropKind.Tree, tx, CityData.CurbY, tz, x.Rng.Range(0f, 360f), x.Rng.Range(0.65f, 0.9f));
                    }
                }
        }

        private static void BuildApartments(Ctx x, Cell cell)
        {
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            bool splitZ = x.Rng.Chance(0.5f);
            for (int half = 0; half < 2; half++)
            {
                int floors = x.Rng.Range(4, 8);
                float h = floors * 3.0f;
                string name = NameTables.Apartments[x.AptName++ % NameTables.Apartments.Length];
                Building b;
                if (splitZ)
                {
                    float hz = (iz1 - iz0) * 0.5f;
                    float z0 = iz0 + half * hz;
                    float sx = (ix1 - ix0) - 3.5f, sz = hz - 2.6f;
                    b = AddBuilding(x, cell, BuildingType.Apartment, (ix0 + ix1) * 0.5f, z0 + hz * 0.5f + (half == 0 ? -0.4f : 0.4f), sx, sz, half == 0 ? Side.South : Side.North, floors, h, name);
                }
                else
                {
                    float hx = (ix1 - ix0) * 0.5f;
                    float x0 = ix0 + half * hx;
                    float sx = hx - 2.6f, sz = (iz1 - iz0) - 3.5f;
                    b = AddBuilding(x, cell, BuildingType.Apartment, x0 + hx * 0.5f + (half == 0 ? -0.4f : 0.4f), (iz0 + iz1) * 0.5f, sx, sz, half == 0 ? Side.West : Side.East, floors, h, name);
                }
                b.Capacity = floors * 4;
                b.IsHome = true;
            }
        }

        private static void BuildCommercial(Ctx x, Cell cell)
        {
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            // Office tower on one half, shops on the other two lots.
            int half = x.Rng.Next(4); // 0 = south half, 1 = north, 2 = west, 3 = east
            int floors = x.Rng.Range(3, 7);
            float h = floors * 3.4f;
            string name = NameTables.Offices[x.OfficeName++ % NameTables.Offices.Length];
            float hw = (ix1 - ix0) * 0.5f, hh = (iz1 - iz0) * 0.5f;
            Building b;
            int mask;
            switch (half)
            {
                case 0:
                    b = AddBuilding(x, cell, BuildingType.Office, (ix0 + ix1) * 0.5f, iz0 + hh * 0.5f, (ix1 - ix0) - 4f, hh - 2.2f, Side.South, floors, h, name);
                    mask = 1 | 2;
                    break;
                case 1:
                    b = AddBuilding(x, cell, BuildingType.Office, (ix0 + ix1) * 0.5f, iz1 - hh * 0.5f, (ix1 - ix0) - 4f, hh - 2.2f, Side.North, floors, h, name);
                    mask = 4 | 8;
                    break;
                case 2:
                    b = AddBuilding(x, cell, BuildingType.Office, ix0 + hw * 0.5f, (iz0 + iz1) * 0.5f, hw - 2.2f, (iz1 - iz0) - 4f, Side.West, floors, h, name);
                    mask = 1 | 4;
                    break;
                default:
                    b = AddBuilding(x, cell, BuildingType.Office, ix1 - hw * 0.5f, (iz0 + iz1) * 0.5f, hw - 2.2f, (iz1 - iz0) - 4f, Side.East, floors, h, name);
                    mask = 2 | 8;
                    break;
            }
            b.Capacity = floors * 6;
            b.IsWorkplace = true;
            BuildHouses(x, cell, mask, 1f);
        }

        private static void BuildCivic(Ctx x, Cell cell)
        {
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            float lw = (ix1 - ix0) * 0.5f, lh = (iz1 - iz0) * 0.5f;
            var type = x.Civic[cell.Id];
            Building b;
            switch (type)
            {
                case BuildingType.Bank:
                    b = AddBuilding(x, cell, BuildingType.Bank, ix0 + lw * 0.5f, iz0 + 1.3f + 4.4f, 9.4f, 8.8f, Side.South, 2, 7.6f, "Банк «Надёжный»");
                    b.Capacity = 8;
                    x.C.BankBuilding = b.Id;
                    BuildHouses(x, cell, 1, 0.5f);
                    break;
                case BuildingType.FireStation:
                    b = AddBuilding(x, cell, BuildingType.FireStation, ix0 + lw * 1.5f, iz0 + 1.3f + 4.6f, 9.6f, 9.2f, Side.South, 2, 7.2f, "Пожарная часть");
                    b.Capacity = 6;
                    x.C.FireStationBuilding = b.Id;
                    BuildHouses(x, cell, 2, 0.25f);
                    break;
                default:
                    b = AddBuilding(x, cell, BuildingType.Police, ix0 + lw * 0.5f, iz0 + 1.3f + 4.3f, 9.2f, 8.6f, Side.South, 2, 7.0f, "Полицейский участок");
                    b.Capacity = 6;
                    x.C.PoliceBuilding = b.Id;
                    BuildHouses(x, cell, 1, 0.25f);
                    break;
            }
            b.IsWorkplace = true;
        }

        private static void BuildChurch(Ctx x, Cell cell)
        {
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            float lw = (ix1 - ix0) * 0.5f;
            var b = AddBuilding(x, cell, BuildingType.Church, ix0 + lw * 0.5f + 0.3f, (iz0 + iz1) * 0.5f - 0.5f, 8.4f, 15.5f, Side.South, 1, 8.5f, "Церковь");
            b.Capacity = 3;
            b.IsWorkplace = true;
            x.C.ChurchBuilding = b.Id;
            AddProp(x, PropKind.Conifer, ix0 + 1.2f, CityData.CurbY, iz1 - 1.4f, 0f, 0.9f);
            AddProp(x, PropKind.Conifer, ix0 + lw - 1.0f, CityData.CurbY, iz1 - 1.2f, 0f, 0.8f);
            BuildHouses(x, cell, 1 | 4);
        }

        private static void BuildSchool(Ctx x, Cell cell)
        {
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            bool faceWest = cell.Col > x.RiverCol;
            float sx = 10.5f, sz = 18.5f;
            float bx = faceWest ? ix0 + 1.4f + sx * 0.5f : ix1 - 1.4f - sx * 0.5f;
            var b = AddBuilding(x, cell, BuildingType.School, bx, (iz0 + iz1) * 0.5f, sx, sz, faceWest ? Side.West : Side.East, 2, 7.4f, "Школа №1");
            b.Capacity = 12;
            b.IsWorkplace = true;
            x.C.SchoolBuilding = b.Id;
            float yardX = faceWest ? ix1 - 5.2f : ix0 + 5.2f;
            AddProp(x, PropKind.Playground, yardX, CityData.CurbY, (iz0 + iz1) * 0.5f - 3f, 0f, 1f);
            AddProp(x, PropKind.Tree, yardX + (faceWest ? 2.8f : -2.8f), CityData.CurbY, iz1 - 2.2f, 30f, 0.85f);
            AddProp(x, PropKind.Tree, yardX, CityData.CurbY, iz1 - 2.0f, 80f, 0.8f);
            AddProp(x, PropKind.Tree, yardX + (faceWest ? 2.5f : -2.5f), CityData.CurbY, iz0 + 2.0f, 10f, 0.75f);
            AddProp(x, PropKind.Bench, yardX, CityData.CurbY, (iz0 + iz1) * 0.5f + 3.5f, 0f, 1f);
        }

        private static void BuildMarket(Ctx x, Cell cell)
        {
            var c = x.C;
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            float cx = cell.CenterX, cz = cell.CenterZ;
            var hall = AddBuilding(x, cell, BuildingType.MarketHall, cx, iz1 - 1.5f - 4f, 14f, 8f, Side.South, 1, 5.5f, "Рыночный зал");
            hall.Capacity = 10;
            hall.IsWorkplace = true;
            c.MarketHallBuilding = hall.Id;
            float y = CityData.CurbY;
            int p0 = x.W.AddNode(cx, y, cz - 2f, float.MaxValue, cell.Id);
            int p1 = x.W.AddNode(cx - 6f, y, cz - 4f, float.MaxValue, cell.Id);
            int p2 = x.W.AddNode(cx + 6f, y, cz - 4f, float.MaxValue, cell.Id);
            int p3 = x.W.AddNode(cx, y, cz - 7.5f, float.MaxValue, cell.Id);
            int p4 = x.W.AddNode(cx, y, hall.Z - hall.SizeZ * 0.5f - 1.2f, float.MaxValue, cell.Id);
            int gS = AddRingPoint(x, cell, Side.South, cx);
            int gW = AddRingPoint(x, cell, Side.West, cz - 3f);
            int gE = AddRingPoint(x, cell, Side.East, cz - 3f);
            x.W.AddEdge(gS, p3, WalkEdgeKind.Path);
            x.W.AddEdge(p3, p0, WalkEdgeKind.Path);
            x.W.AddEdge(gW, p1, WalkEdgeKind.Path);
            x.W.AddEdge(p1, p0, WalkEdgeKind.Path);
            x.W.AddEdge(gE, p2, WalkEdgeKind.Path);
            x.W.AddEdge(p2, p0, WalkEdgeKind.Path);
            x.W.AddEdge(p0, p4, WalkEdgeKind.Path);
            x.W.AddEdge(p1, p3, WalkEdgeKind.Path);
            x.W.AddEdge(p2, p3, WalkEdgeKind.Path);
            hall.EntranceNode = p4;
            c.MarketSpotNodes.AddRange(new[] { p0, p1, p2, p3 });
            // Stalls around the square
            float[] sxs = { ix0 + 2.2f, ix0 + 2.2f, ix1 - 2.2f, ix1 - 2.2f, cx - 5f, cx + 5f };
            float[] szs = { cz - 6f, cz + 0.5f, cz - 6f, cz + 0.5f, iz0 + 2.2f, iz0 + 2.2f };
            float[] yaws = { 90f, 90f, 270f, 270f, 0f, 0f };
            for (int i = 0; i < sxs.Length; i++) AddProp(x, PropKind.Stall, sxs[i], y, szs[i], yaws[i], 1f);
            AddProp(x, PropKind.Tree, ix0 + 1.6f, y, iz1 - 1.6f, 0f, 0.75f);
            AddProp(x, PropKind.Tree, ix1 - 1.6f, y, iz1 - 1.6f, 0f, 0.75f);
            AddProp(x, PropKind.Bench, cx - 3f, y, cz + 1.3f, 180f, 1f);
            AddProp(x, PropKind.Bench, cx + 3f, y, cz + 1.3f, 180f, 1f);
        }

        private static void BuildPark(Ctx x, Cell cell)
        {
            var c = x.C;
            Inner(x, cell, out float ix0, out float ix1, out float iz0, out float iz1);
            float cx = cell.CenterX, cz = cell.CenterZ;
            float y = CityData.CurbY;
            int center = x.W.AddNode(cx, y, cz, float.MaxValue, cell.Id);
            int iS = x.W.AddNode(cx, y, cz - 7f, float.MaxValue, cell.Id);
            int iW = x.W.AddNode(cx - 7f, y, cz, float.MaxValue, cell.Id);
            int iE = x.W.AddNode(cx + 7f, y, cz, float.MaxValue, cell.Id);
            int d1 = x.W.AddNode(cx - 6.5f, y, cz - 6.5f, float.MaxValue, cell.Id);
            int d2 = x.W.AddNode(cx + 6.5f, y, cz - 6.5f, float.MaxValue, cell.Id);
            int d3 = x.W.AddNode(cx - 7f, y, cz + 6f, float.MaxValue, cell.Id);
            int d4 = x.W.AddNode(cx + 7f, y, cz + 6f, float.MaxValue, cell.Id);
            int gS = AddRingPoint(x, cell, Side.South, cx);
            int gN = AddRingPoint(x, cell, Side.North, cx);
            int gW = AddRingPoint(x, cell, Side.West, cz);
            int gE = AddRingPoint(x, cell, Side.East, cz);
            var W = x.W;
            W.AddEdge(gS, iS, WalkEdgeKind.Path);
            W.AddEdge(iS, center, WalkEdgeKind.Path);
            W.AddEdge(gW, iW, WalkEdgeKind.Path);
            W.AddEdge(iW, center, WalkEdgeKind.Path);
            W.AddEdge(gE, iE, WalkEdgeKind.Path);
            W.AddEdge(iE, center, WalkEdgeKind.Path);
            W.AddEdge(d1, iS, WalkEdgeKind.Path);
            W.AddEdge(d1, iW, WalkEdgeKind.Path);
            W.AddEdge(d2, iS, WalkEdgeKind.Path);
            W.AddEdge(d2, iE, WalkEdgeKind.Path);
            W.AddEdge(d3, iW, WalkEdgeKind.Path);
            W.AddEdge(d4, iE, WalkEdgeKind.Path);
            W.AddEdge(gN, d3, WalkEdgeKind.Path);
            W.AddEdge(gN, d4, WalkEdgeKind.Path);
            c.ParkSpotNodes.AddRange(new[] { center, iS, iW, iE, d1, d2, d3, d4 });
            c.FestivalNode = center;
            c.StageX = cx;
            c.StageZ = cz + 7.2f;
            c.StageYaw = 180f;

            // Trees, benches and flower beds, avoiding paths and the stage.
            int placed = 0, guard = 0;
            while (placed < 14 && guard++ < 400)
            {
                float tx = x.Rng.Range(ix0 + 1.2f, ix1 - 1.2f), tz = x.Rng.Range(iz0 + 1.2f, iz1 - 1.2f);
                if (Math.Abs(tx - cx) < 2.6f || Math.Abs(tz - cz) < 2.6f) continue;
                if (Math.Abs(tx - cx) < 7f && tz > cz + 3f) continue; // stage area
                if (Math.Abs(Math.Abs(tx - cx) - Math.Abs(tz - cz)) < 2.2f && Math.Abs(tz - cz) < 8f) continue; // diagonals
                if (Math.Sqrt((tx - cx) * (tx - cx) + (tz - cz) * (tz - cz)) < 5.5f) continue;
                bool clash = false;
                foreach (var p in c.Props)
                    if (p.Group == 3 && (p.X - tx) * (p.X - tx) + (p.Z - tz) * (p.Z - tz) < 7f) { clash = true; break; }
                if (clash) continue;
                AddProp(x, x.Rng.Chance(0.3f) ? PropKind.Conifer : PropKind.Tree, tx, y, tz, x.Rng.Range(0f, 360f), x.Rng.Range(0.8f, 1.15f), 3);
                placed++;
            }
            AddProp(x, PropKind.Bench, cx - 3.2f, y, cz - 4.3f, 90f, 1f, 3);
            AddProp(x, PropKind.Bench, cx + 3.2f, y, cz - 4.3f, 270f, 1f, 3);
            AddProp(x, PropKind.Bench, cx - 4.3f, y, cz + 2.2f, 0f, 1f, 3);
            AddProp(x, PropKind.Bench, cx + 4.3f, y, cz + 2.2f, 0f, 1f, 3);
            AddProp(x, PropKind.Flowerbed, cx - 3.6f, y, cz - 3.6f, 0f, 1f, 3);
            AddProp(x, PropKind.Flowerbed, cx + 3.6f, y, cz - 3.6f, 0f, 1f, 3);
            AddProp(x, PropKind.Flowerbed, cx, y, cz, 0f, 1.2f, 3);
        }

        // ------------------------------------------------------------------ riverside

        private static void BuildLot(Ctx x)
        {
            var c = x.C;
            int mid = x.Rows / 2;
            float R = x.R;
            int midCell = c.CellAt(x.RiverCol, mid);
            var cell = c.Cells[midCell];
            c.LotMinX = c.WestWallX;
            c.LotMaxX = c.RiverWestBank;
            c.LotMaxZ = MathUtil.Clamp(c.RoadZ[mid + 1] - R * 0.5f - 20f, cell.MinZ + 12f, cell.MaxZ - 20f);
            c.LotMinZ = MathUtil.Clamp(c.RoadZ[mid] - 14f, cell.MinZ + 10f, c.LotMaxZ - 12f);
            c.RampTopX = c.WestWallX + 1.8f;
            c.RampBottomX = c.RampTopX;
            c.RampBottomZ = c.LotMaxZ;
            c.RampTopZ = c.LotMaxZ + 12f;
            for (float z = c.LotMinZ + 1.7f; z <= c.LotMaxZ - 1.4f; z += 2.9f)
                c.LotSlots.Add(new LotSlot { X = c.RiverWestBank - 2.9f, Z = z, Yaw = 90f });
            x.WestStairs.Add(cell.MinZ + 3.5f);
            x.WestStairs.Add(c.LotMinZ - 4f);
            x.WestStairs.Add(cell.MaxZ - 3.5f);
            x.EastStairs.Add(cell.MinZ + 3.5f);
            x.EastStairs.Add(cell.CenterZ);
            x.EastStairs.Add(cell.MaxZ - 3.5f);
            foreach (var other in c.Cells)
            {
                if (other.Kind != CellKind.River || other.Id == midCell) continue;
                x.WestStairs.Add(other.MinZ + 3.5f);
                x.WestStairs.Add(other.MaxZ - 3.5f);
                x.EastStairs.Add(other.MinZ + 3.5f);
                x.EastStairs.Add(other.MaxZ - 3.5f);
            }
        }

        private static int RiverCellAtZ(Ctx x, float z)
        {
            var c = x.C;
            int row = 0;
            for (int j = 0; j < x.Rows; j++)
                if (z >= c.RoadZ[j]) row = j;
            return c.CellAt(x.RiverCol, row);
        }

        private static void BuildRiverside(Ctx x)
        {
            var c = x.C;
            float westX = c.WestWallX + 5.0f, lotX = c.WestWallX + 2.4f, eastX = c.EastWallX - 4.9f;
            float z0 = c.MinZ + 8f, z1 = c.MaxZ - 5f;

            List<float> Samples(List<float> stairs, bool west)
            {
                var zs = new List<float>();
                for (float z = z0; z <= z1 + 0.01f; z += 9f) zs.Add(z);
                zs.AddRange(stairs);
                if (west)
                {
                    zs.Add(c.LotMinZ);
                    zs.Add(c.LotMaxZ);
                    zs.Add((c.LotMinZ + c.LotMaxZ) * 0.5f);
                }
                zs.Sort();
                var result = new List<float>();
                foreach (float z in zs)
                {
                    if (z < z0 - 0.01f || z > z1 + 0.01f) continue;
                    if (result.Count > 0 && Math.Abs(result[result.Count - 1] - z) < 1.5f)
                    {
                        // keep stair / lot values exact
                        if (stairs.Contains(z) || (west && (z == c.LotMinZ || z == c.LotMaxZ))) result[result.Count - 1] = z;
                        continue;
                    }
                    result.Add(z);
                }
                return result;
            }

            var westZ = Samples(x.WestStairs, true);
            var eastZ = Samples(x.EastStairs, false);
            var westNodes = new List<int>();
            var eastNodes = new List<int>();
            foreach (float z in westZ)
            {
                bool inLot = z >= c.LotMinZ - 0.01f && z <= c.LotMaxZ + 0.01f;
                float y = inLot ? CityData.LotY : CityData.TerraceY;
                int n = x.W.AddNode(inLot ? lotX : westX, y, z, y, RiverCellAtZ(x, z));
                westNodes.Add(n);
                if (inLot) c.RiversideNodes.Add(n);
                else c.PromenadeNodes.Add(n);
            }
            foreach (float z in eastZ)
            {
                int n = x.W.AddNode(eastX, CityData.TerraceY, z, CityData.TerraceY, RiverCellAtZ(x, z));
                eastNodes.Add(n);
                c.PromenadeNodes.Add(n);
            }
            for (int i = 0; i + 1 < westNodes.Count; i++) x.W.AddEdge(westNodes[i], westNodes[i + 1], WalkEdgeKind.Terrace);
            for (int i = 0; i + 1 < eastNodes.Count; i++) x.W.AddEdge(eastNodes[i], eastNodes[i + 1], WalkEdgeKind.Terrace);

            // Stairs up to the street-level sidewalk of the river cells.
            for (int i = 0; i < westZ.Count; i++)
            {
                if (!ContainsNear(x.WestStairs, westZ[i])) continue;
                var cell = c.Cells[RiverCellAtZ(x, westZ[i])];
                if (westZ[i] < cell.MinZ + 1f || westZ[i] > cell.MaxZ - 1f) continue;
                int top = AddRingPoint(x, cell, Side.West, westZ[i]);
                x.W.AddEdge(top, westNodes[i], WalkEdgeKind.Stairs, -1, -1, CityData.TerraceY);
            }
            for (int i = 0; i < eastZ.Count; i++)
            {
                if (!ContainsNear(x.EastStairs, eastZ[i])) continue;
                var cell = c.Cells[RiverCellAtZ(x, eastZ[i])];
                if (eastZ[i] < cell.MinZ + 1f || eastZ[i] > cell.MaxZ - 1f) continue;
                int top = AddRingPoint(x, cell, Side.East, eastZ[i]);
                x.W.AddEdge(top, eastNodes[i], WalkEdgeKind.Stairs, -1, -1, CityData.TerraceY);
            }

            // Bridge abutments on the river cells' bridge sides.
            foreach (var cell in c.Cells)
            {
                if (cell.Kind != CellKind.River) continue;
                if (cell.RoadS)
                {
                    AddRingPoint(x, cell, Side.South, c.WestWallX);
                    AddRingPoint(x, cell, Side.South, c.EastWallX);
                }
                if (cell.RoadN)
                {
                    AddRingPoint(x, cell, Side.North, c.WestWallX);
                    AddRingPoint(x, cell, Side.North, c.EastWallX);
                }
            }

            // Lighthouse at the south end of the east terrace.
            var southCell = c.Cells[RiverCellAtZ(x, c.MinZ + 1f)];
            var lh = new Building
            {
                Id = c.Buildings.Count, Type = BuildingType.Lighthouse, Name = "Маяк", Cell = southCell.Id, Facing = Side.North,
                X = c.RiverEastBank + 3.0f, Z = c.MinZ + 3.4f, SizeX = 4.4f, SizeZ = 4.4f, Floors = 1, Height = 13f,
                Style = 7, EntranceNode = eastNodes[0]
            };
            lh.DoorX = lh.X;
            lh.DoorZ = lh.Z + 2.2f;
            c.Buildings.Add(lh);
        }

        private static bool ContainsNear(List<float> list, float v)
        {
            foreach (float f in list)
                if (Math.Abs(f - v) < 0.01f) return true;
            return false;
        }

        // ------------------------------------------------------------------ curbs, places, props

        private static RoadSegment SegmentForSide(Ctx x, Cell cell, Side side)
        {
            switch (side)
            {
                case Side.South: return x.HSeg[cell.Row, cell.Col];
                case Side.North: return x.HSeg[cell.RowEnd + 1, cell.Col];
                case Side.West: return x.VSeg[cell.Col, cell.Row];
                default: return x.VSeg[cell.Col + 1, cell.Row];
            }
        }

        private static void AssignCurbs(Ctx x)
        {
            var c = x.C;
            var rn = c.Roads;
            foreach (var b in c.Buildings)
            {
                var cell = c.Cells[b.Cell];
                if (cell.Kind == CellKind.River) continue;
                var seg = SegmentForSide(x, cell, b.Facing);
                if (seg == null) continue;
                var center = new Vec2(cell.CenterX, cell.CenterZ);
                int lane = seg.LaneAB;
                if (Vec2.Dot(rn.LDir[lane].Right, center - rn.LStart[lane]) < 0) lane = seg.LaneBA;
                float s = Vec2.Dot(new Vec2(b.DoorX, b.DoorZ) - rn.LStart[lane], rn.LDir[lane]);
                b.CurbLane = lane;
                b.CurbS = MathUtil.Clamp(s, 5f, rn.LLen[lane] - 8f);
            }
            int mid = x.Rows / 2;
            var lotSeg = x.VSeg[x.RiverCol, mid];
            c.LotCurbLane = lotSeg.LaneAB;
            c.LotCurbS = MathUtil.Clamp(c.RampTopZ - rn.LStart[lotSeg.LaneAB].Z, 4f, rn.LLen[lotSeg.LaneAB] - 8f);
            c.RampTopZ = rn.LStart[lotSeg.LaneAB].Z + c.LotCurbS;
        }

        private static Place AddPlace(Ctx x, string key, string ru, string en, PlaceKind kind, float px, float py, float pz)
        {
            var p = new Place { Id = x.C.Places.Count, Key = key, NameRu = ru, NameEn = en, Kind = kind, X = px, Y = py, Z = pz };
            x.C.Places.Add(p);
            return p;
        }

        private static Place BuildingPlace(Ctx x, int buildingId, string key, string ru, string en)
        {
            var b = x.C.Buildings[buildingId];
            float top = b.Height + (b.Type == BuildingType.Church ? 12f : 3f);
            var p = AddPlace(x, key, ru, en, PlaceKind.Building, b.X, CityData.CurbY + top, b.Z);
            p.Building = buildingId;
            p.MinX = b.X - b.SizeX * 0.5f;
            p.MaxX = b.X + b.SizeX * 0.5f;
            p.MinZ = b.Z - b.SizeZ * 0.5f;
            p.MaxZ = b.Z + b.SizeZ * 0.5f;
            b.Place = p.Id;
            return p;
        }

        private static void BuildPlaces(Ctx x)
        {
            var c = x.C;
            Cell parkCell = null, marketCell = null;
            foreach (var cell in c.Cells)
            {
                if (cell.Kind == CellKind.Park) parkCell = cell;
                if (cell.Kind == CellKind.Market) marketCell = cell;
            }
            var park = AddPlace(x, "central_park", "Центральный парк", "Central Park", PlaceKind.Park, parkCell.CenterX, CityData.CurbY + 9f, parkCell.CenterZ - 3f);
            park.MinX = parkCell.MinX; park.MaxX = parkCell.MaxX; park.MinZ = parkCell.MinZ; park.MaxZ = parkCell.MaxZ;
            park.SpotNodes.AddRange(c.ParkSpotNodes);
            c.PlaceCentralPark = park.Id;

            var market = AddPlace(x, "market_square", "Рыночная площадь", "Market Square", PlaceKind.Square, marketCell.CenterX, CityData.CurbY + 10f, marketCell.CenterZ - 3f);
            market.MinX = marketCell.MinX; market.MaxX = marketCell.MaxX; market.MinZ = marketCell.MinZ; market.MaxZ = marketCell.MaxZ;
            market.SpotNodes.AddRange(c.MarketSpotNodes);
            market.Building = c.MarketHallBuilding;
            c.Buildings[c.MarketHallBuilding].Place = market.Id;
            c.PlaceMarket = market.Id;

            var lot = AddPlace(x, "riverside_parking", "Парковка у реки", "Riverside Parking", PlaceKind.Parking,
                (c.LotMinX + c.LotMaxX) * 0.5f, CityData.LotY + 5f, (c.LotMinZ + c.LotMaxZ) * 0.5f);
            lot.MinX = c.LotMinX; lot.MaxX = c.LotMaxX; lot.MinZ = c.LotMinZ; lot.MaxZ = c.RampTopZ;
            lot.SpotNodes.AddRange(c.RiversideNodes);
            c.PlaceParking = lot.Id;

            float bridgeHalf = x.R * 0.5f + x.SW;
            for (int br = 0; br < 2; br++)
            {
                float bz = c.BridgeZ(br);
                var p = AddPlace(x, br == 0 ? "north_bridge" : "south_bridge", br == 0 ? "Северный мост" : "Южный мост",
                    br == 0 ? "North Bridge" : "South Bridge", PlaceKind.Bridge, c.RiverCenterX, CityData.StreetY + 5f, bz);
                p.Bridge = br;
                p.MinX = c.WestWallX; p.MaxX = c.EastWallX; p.MinZ = bz - bridgeHalf; p.MaxZ = bz + bridgeHalf;
                if (br == 0) c.PlaceNorthBridge = p.Id;
                else c.PlaceSouthBridge = p.Id;
            }

            int mid = x.Rows / 2;
            var river = AddPlace(x, "river", "Река", "River", PlaceKind.River, c.RiverCenterX, 2.5f, (c.RoadZ[mid + 1] + c.RoadZ[mid + 2 > x.Rows ? x.Rows : mid + 2]) * 0.5f);
            river.MinX = c.RiverWestBank; river.MaxX = c.RiverEastBank; river.MinZ = c.MinZ; river.MaxZ = c.MaxZ;
            c.PlaceRiver = river.Id;

            var prom = AddPlace(x, "promenade", "Набережная", "Promenade", PlaceKind.Promenade, c.EastWallX - 5f, CityData.TerraceY + 3f, c.RoadZ[mid]);
            prom.ShowLabel = false;
            prom.SpotNodes.AddRange(c.PromenadeNodes);
            prom.MinX = c.RiverEastBank; prom.MaxX = c.EastWallX; prom.MinZ = c.MinZ; prom.MaxZ = c.MaxZ;
            c.PlacePromenade = prom.Id;

            c.PlaceSchool = BuildingPlace(x, c.SchoolBuilding, "school", "Школа", "School").Id;
            c.PlaceBank = BuildingPlace(x, c.BankBuilding, "bank", "Банк", "Bank").Id;
            c.PlaceFire = BuildingPlace(x, c.FireStationBuilding, "fire_station", "Пожарная часть", "Fire Station").Id;
            c.PlacePolice = BuildingPlace(x, c.PoliceBuilding, "police", "Полиция", "Police Station").Id;
            c.PlaceChurch = BuildingPlace(x, c.ChurchBuilding, "church", "Церковь", "Church").Id;
            foreach (var b in c.Buildings)
            {
                if (b.Type == BuildingType.WaterTower) c.PlaceWaterTower = BuildingPlace(x, b.Id, "water_tower", "Водонапорная башня", "Water Tower").Id;
                if (b.Type == BuildingType.Lighthouse) c.PlaceLighthouse = BuildingPlace(x, b.Id, "lighthouse", "Маяк", "Lighthouse").Id;
            }

            // Safe gathering points for evacuations: parks, the square and block corners.
            c.SafeNodes.AddRange(c.ParkSpotNodes);
            c.SafeNodes.AddRange(c.MarketSpotNodes);
            foreach (var cell in c.Cells)
            {
                if (cell.Kind == CellKind.River) continue;
                c.SafeNodes.Add(cell.CornerSW);
                c.SafeNodes.Add(cell.CornerNE);
            }
        }

        private static void BuildProps(Ctx x)
        {
            var c = x.C;
            float y = CityData.CurbY;
            // Street lamps at block corners and side midpoints.
            foreach (var cell in c.Cells)
            {
                if (cell.Kind == CellKind.River)
                {
                    for (float z = cell.MinZ + 3f; z < cell.MaxZ - 2f; z += 15f)
                    {
                        AddProp(x, PropKind.Lamp, cell.MinX + 0.45f, y, z, 270f, 1f);
                        AddProp(x, PropKind.Lamp, cell.MaxX - 0.45f, y, z, 90f, 1f);
                    }
                    continue;
                }
                float o = 0.45f;
                AddProp(x, PropKind.Lamp, cell.MinX + o, y, cell.MinZ + o, 225f, 1f);
                AddProp(x, PropKind.Lamp, cell.MaxX - o, y, cell.MinZ + o, 135f, 1f);
                AddProp(x, PropKind.Lamp, cell.MaxX - o, y, cell.MaxZ - o, 45f, 1f);
                AddProp(x, PropKind.Lamp, cell.MinX + o, y, cell.MaxZ - o, 315f, 1f);
                AddProp(x, PropKind.Lamp, cell.CenterX + 3f, y, cell.MinZ + o, 180f, 1f);
                AddProp(x, PropKind.Lamp, cell.CenterX - 3f, y, cell.MaxZ - o, 0f, 1f);
            }
            // Bridge lamps
            for (int br = 0; br < 2; br++)
            {
                float bz = c.BridgeZ(br);
                float side = x.R * 0.5f + x.SW - 0.35f;
                for (int k = 0; k < 3; k++)
                {
                    float bx = MathUtil.Lerp(c.WestWallX + 3f, c.EastWallX - 3f, k / 2f);
                    AddProp(x, PropKind.Lamp, bx, y, bz - side, 180f, 1f);
                    AddProp(x, PropKind.Lamp, bx, y, bz + side, 0f, 1f);
                }
            }

            // Trees along the slab margin (outside the river column).
            for (float px = c.MinX + 3f; px < c.MaxX - 2f; px += 7f)
            {
                if (px > c.WestWallX - 3f && px < c.EastWallX + 3f) continue;
                if (x.Rng.Chance(0.75f)) AddProp(x, x.Rng.Chance(0.3f) ? PropKind.Conifer : PropKind.Tree, px + x.Rng.Range(-1f, 1f), CityData.StreetY, c.MinZ + x.Cfg.Margin * 0.45f, x.Rng.Range(0f, 360f), x.Rng.Range(0.7f, 1.05f));
                if (x.Rng.Chance(0.75f)) AddProp(x, x.Rng.Chance(0.3f) ? PropKind.Conifer : PropKind.Tree, px + x.Rng.Range(-1f, 1f), CityData.StreetY, c.MaxZ - x.Cfg.Margin * 0.45f, x.Rng.Range(0f, 360f), x.Rng.Range(0.7f, 1.05f));
            }
            for (float pz = c.MinZ + 6f; pz < c.MaxZ - 5f; pz += 7f)
            {
                if (x.Rng.Chance(0.75f)) AddProp(x, x.Rng.Chance(0.3f) ? PropKind.Conifer : PropKind.Tree, c.MinX + x.Cfg.Margin * 0.45f, CityData.StreetY, pz + x.Rng.Range(-1f, 1f), x.Rng.Range(0f, 360f), x.Rng.Range(0.7f, 1.05f));
                if (x.Rng.Chance(0.75f)) AddProp(x, x.Rng.Chance(0.3f) ? PropKind.Conifer : PropKind.Tree, c.MaxX - x.Cfg.Margin * 0.45f, CityData.StreetY, pz + x.Rng.Range(-1f, 1f), x.Rng.Range(0f, 360f), x.Rng.Range(0.7f, 1.05f));
            }

            // East promenade: trees near the wall, benches facing the river.
            float bridgeClear = x.R * 0.5f + x.SW + 2.5f;
            for (float z = c.MinZ + 12f; z < c.MaxZ - 4f; z += 11f)
            {
                bool nearBridge = Math.Abs(z - c.BridgeZ(0)) < bridgeClear || Math.Abs(z - c.BridgeZ(1)) < bridgeClear;
                bool nearStairs = false;
                foreach (float s in x.EastStairs) if (Math.Abs(s - z) < 3f) nearStairs = true;
                if (nearBridge || nearStairs) continue;
                AddProp(x, PropKind.Tree, c.EastWallX - 1.6f, CityData.TerraceY, z, x.Rng.Range(0f, 360f), x.Rng.Range(0.75f, 0.95f));
                if (((int)(z * 7)) % 2 == 0) AddProp(x, PropKind.Bench, c.RiverEastBank + 1.3f, CityData.TerraceY, z + 3f, 270f, 1f);
            }
            // West promenade outside the lot
            for (float z = c.MinZ + 14f; z < c.MaxZ - 4f; z += 13f)
            {
                if (z > c.LotMinZ - 4f && z < c.RampTopZ + 3f) continue;
                bool nearBridge = Math.Abs(z - c.BridgeZ(0)) < bridgeClear || Math.Abs(z - c.BridgeZ(1)) < bridgeClear;
                if (nearBridge) continue;
                AddProp(x, PropKind.Bench, c.RiverWestBank - 1.3f, CityData.TerraceY, z, 90f, 1f);
            }

            // Riverside park variant of the lot (group 1)
            for (float z = c.LotMinZ + 2.5f; z < c.LotMaxZ - 1f; z += 5.5f)
            {
                AddProp(x, x.Rng.Chance(0.2f) ? PropKind.Conifer : PropKind.Tree, c.RiverWestBank - 3.6f, CityData.LotY, z, x.Rng.Range(0f, 360f), x.Rng.Range(0.75f, 0.95f), 1);
                AddProp(x, PropKind.Bench, c.RiverWestBank - 1.1f, CityData.LotY, z + 2.6f, 90f, 1f, 1);
            }
            AddProp(x, PropKind.Flowerbed, c.WestWallX + 5.5f, CityData.LotY, (c.LotMinZ + c.LotMaxZ) * 0.5f, 0f, 1f, 1);
            AddProp(x, PropKind.Bush, c.WestWallX + 5.5f, CityData.LotY, c.LotMinZ + 2f, 0f, 1f, 1);
            AddProp(x, PropKind.Bush, c.WestWallX + 5.5f, CityData.LotY, c.LotMaxZ - 2f, 0f, 1f, 1);
        }
    }
}
