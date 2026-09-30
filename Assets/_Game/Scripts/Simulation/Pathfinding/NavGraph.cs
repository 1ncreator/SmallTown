using System;
using System.Collections.Generic;

namespace SmallTown.Simulation.Pathfinding
{
    public enum WalkEdgeKind : byte { Sidewalk, Crosswalk, Bridge, Stairs, Path, Terrace }

    /// <summary>
    /// Pedestrian graph: sidewalks, crosswalks, park paths, bridges and the riverside terrace.
    /// Built once by the generator; blocking flags are derived from the world state.
    /// </summary>
    public sealed class NavGraph
    {
        private readonly List<float> _x = new List<float>(), _y = new List<float>(), _z = new List<float>(), _flood = new List<float>();
        private readonly List<int> _cell = new List<int>();
        private readonly List<int> _ea = new List<int>(), _eb = new List<int>(), _ecw = new List<int>();
        private readonly List<WalkEdgeKind> _ek = new List<WalkEdgeKind>();
        private readonly List<sbyte> _ebr = new List<sbyte>();
        private readonly List<float> _eflood = new List<float>();

        public int NodeCount;
        public float[] X, Y, Z, NodeFloodY;
        public int[] NodeCell;
        public int EdgeCount;
        public int[] EA, EB, ECrosswalk;
        public float[] ELen, EFloodY;
        public WalkEdgeKind[] EKind;
        public sbyte[] EBridge;
        public int[] AdjStart, AdjEdges;

        // Derived dynamic state
        public bool[] NodeFlooded;
        public bool[] EdgeClosed;   // closed bridge
        public bool[] EdgeFlooded;
        public int Version;

        public int AddNode(float x, float y, float z, float floodY, int cell)
        {
            _x.Add(x); _y.Add(y); _z.Add(z); _flood.Add(floodY); _cell.Add(cell);
            return _x.Count - 1;
        }

        public void SetNodeY(int n, float y, float floodY)
        {
            _y[n] = y;
            _flood[n] = floodY;
        }

        public float NX(int n) => _x[n];
        public float NZ(int n) => _z[n];
        public int PendingNodeCount => _x.Count;

        public int AddEdge(int a, int b, WalkEdgeKind kind, int bridge = -1, int crosswalk = -1, float floodY = float.MaxValue)
        {
            if (a == b) return -1;
            for (int i = 0; i < _ea.Count; i++)
                if ((_ea[i] == a && _eb[i] == b) || (_ea[i] == b && _eb[i] == a)) return i;
            _ea.Add(a); _eb.Add(b); _ek.Add(kind); _ebr.Add((sbyte)bridge); _ecw.Add(crosswalk); _eflood.Add(floodY);
            return _ea.Count - 1;
        }

        public void Freeze()
        {
            NodeCount = _x.Count;
            X = _x.ToArray(); Y = _y.ToArray(); Z = _z.ToArray(); NodeFloodY = _flood.ToArray(); NodeCell = _cell.ToArray();
            EdgeCount = _ea.Count;
            EA = _ea.ToArray(); EB = _eb.ToArray(); EKind = _ek.ToArray(); EBridge = _ebr.ToArray(); ECrosswalk = _ecw.ToArray(); EFloodY = _eflood.ToArray();
            ELen = new float[EdgeCount];
            var deg = new int[NodeCount];
            for (int e = 0; e < EdgeCount; e++)
            {
                float dx = X[EA[e]] - X[EB[e]], dy = Y[EA[e]] - Y[EB[e]], dz = Z[EA[e]] - Z[EB[e]];
                ELen[e] = MathF.Max(0.01f, MathF.Sqrt(dx * dx + dy * dy + dz * dz));
                deg[EA[e]]++;
                deg[EB[e]]++;
            }
            AdjStart = new int[NodeCount + 1];
            for (int n = 0; n < NodeCount; n++) AdjStart[n + 1] = AdjStart[n] + deg[n];
            AdjEdges = new int[AdjStart[NodeCount]];
            var fill = new int[NodeCount];
            for (int e = 0; e < EdgeCount; e++)
            {
                AdjEdges[AdjStart[EA[e]] + fill[EA[e]]++] = e;
                AdjEdges[AdjStart[EB[e]] + fill[EB[e]]++] = e;
            }
            NodeFlooded = new bool[NodeCount];
            EdgeClosed = new bool[EdgeCount];
            EdgeFlooded = new bool[EdgeCount];
        }

        public int Other(int edge, int node) => EA[edge] == node ? EB[edge] : EA[edge];

        public int FindEdge(int a, int b)
        {
            for (int k = AdjStart[a]; k < AdjStart[a + 1]; k++)
            {
                int e = AdjEdges[k];
                if (Other(e, a) == b) return e;
            }
            return -1;
        }

        /// <summary>Recomputes blocking from bridge closures and the flood level. Returns true if anything changed.</summary>
        public bool UpdateBlocking(bool northClosed, bool southClosed, float floodLevel)
        {
            bool changed = false;
            for (int n = 0; n < NodeCount; n++)
            {
                bool f = floodLevel >= NodeFloodY[n] - 0.02f;
                if (f != NodeFlooded[n]) { NodeFlooded[n] = f; changed = true; }
            }
            for (int e = 0; e < EdgeCount; e++)
            {
                bool closed = (EBridge[e] == 0 && northClosed) || (EBridge[e] == 1 && southClosed);
                bool flooded = NodeFlooded[EA[e]] || NodeFlooded[EB[e]] || floodLevel >= EFloodY[e] - 0.02f;
                if (closed != EdgeClosed[e]) { EdgeClosed[e] = closed; changed = true; }
                if (flooded != EdgeFlooded[e]) { EdgeFlooded[e] = flooded; changed = true; }
            }
            if (changed) Version++;
            return changed;
        }

        public bool EdgeBlocked(int e) => EdgeClosed[e] || EdgeFlooded[e];

        public int NearestNode(float x, float z, bool requireDry)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int n = 0; n < NodeCount; n++)
            {
                if (requireDry && NodeFlooded[n]) continue;
                float dx = X[n] - x, dz = Z[n] - z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }
    }
}
