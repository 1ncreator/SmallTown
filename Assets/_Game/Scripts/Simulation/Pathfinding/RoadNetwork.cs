using System;
using System.Collections.Generic;
using SmallTown.Utils;

namespace SmallTown.Simulation.Pathfinding
{
    public enum TurnKind : byte { Straight, Right, Left }

    public sealed class RoadSegment
    {
        public int Id;
        public int A, B;            // intersections
        public bool Horizontal;
        public int Line;            // z-line index for horizontal, x-line index for vertical
        public int Span;            // column (horizontal) or row (vertical)
        public int Bridge = -1;
        public int CwA = -1, CwB = -1;
        public int LaneAB = -1, LaneBA = -1;
    }

    public sealed class Crosswalk
    {
        public int Id;
        public int Segment;
        public int WalkEdge;
        public float X, Z;
        public bool AlongX;         // pedestrians walk along X (i.e. crossing a vertical road)
    }

    /// <summary>
    /// Directed lane graph for vehicles. Each road segment has one lane per direction
    /// (right-hand traffic); intersections connect lanes with Bezier turns.
    /// </summary>
    public sealed class RoadNetwork
    {
        public int IntersectionCount;
        public float[] IX, IZ;
        public readonly List<RoadSegment> Segments = new List<RoadSegment>();
        public readonly List<Crosswalk> Crosswalks = new List<Crosswalk>();

        public int LaneCount;
        public int[] LSeg, LFrom, LTo, LCwStart, LCwEnd, LReverse, LBridge;
        public Vec2[] LStart, LEnd, LDir;
        public float[] LLen;
        public int[] LOutStart, LOutTurns;

        public int TurnCount;
        public int[] TFrom, TTo, TInter;
        public Vec2[] TP0, TP1, TP2;
        public float[] TLen;
        public TurnKind[] TKind;

        public bool[] LaneBlocked;
        public int Version;
        public float LaneOffset;
        public float RoadWidth;

        public void BuildLanes(float roadWidth)
        {
            RoadWidth = roadWidth;
            LaneOffset = roadWidth * 0.25f;
            LaneCount = Segments.Count * 2;
            LSeg = new int[LaneCount]; LFrom = new int[LaneCount]; LTo = new int[LaneCount];
            LCwStart = new int[LaneCount]; LCwEnd = new int[LaneCount]; LReverse = new int[LaneCount]; LBridge = new int[LaneCount];
            LStart = new Vec2[LaneCount]; LEnd = new Vec2[LaneCount]; LDir = new Vec2[LaneCount]; LLen = new float[LaneCount];
            for (int s = 0; s < Segments.Count; s++)
            {
                var seg = Segments[s];
                int ab = s * 2, ba = s * 2 + 1;
                seg.LaneAB = ab;
                seg.LaneBA = ba;
                SetupLane(ab, seg, seg.A, seg.B, roadWidth);
                SetupLane(ba, seg, seg.B, seg.A, roadWidth);
                LReverse[ab] = ba;
                LReverse[ba] = ab;
                LCwStart[ab] = seg.CwA; LCwEnd[ab] = seg.CwB;
                LCwStart[ba] = seg.CwB; LCwEnd[ba] = seg.CwA;
            }

            // Turns
            var tFrom = new List<int>(); var tTo = new List<int>(); var tInter = new List<int>();
            var p0 = new List<Vec2>(); var p1 = new List<Vec2>(); var p2 = new List<Vec2>(); var tl = new List<float>(); var tk = new List<TurnKind>();
            var outs = new List<int>[LaneCount];
            for (int l = 0; l < LaneCount; l++) outs[l] = new List<int>();
            for (int inL = 0; inL < LaneCount; inL++)
            {
                int inter = LTo[inL];
                int arms = 0;
                for (int o = 0; o < LaneCount; o++) if (LFrom[o] == inter) arms++;
                for (int outL = 0; outL < LaneCount; outL++)
                {
                    if (LFrom[outL] != inter) continue;
                    if (LSeg[outL] == LSeg[inL] && arms > 1) continue; // no U-turns except dead ends
                    Vec2 a = LEnd[inL], c = LStart[outL];
                    Vec2 din = LDir[inL], dout = LDir[outL];
                    float cross = Vec2.Cross(din, dout);
                    float dot = Vec2.Dot(din, dout);
                    Vec2 b;
                    TurnKind kind;
                    if (dot > 0.9f)
                    {
                        b = Vec2.Lerp(a, c, 0.5f);
                        kind = TurnKind.Straight;
                    }
                    else if (MathF.Abs(cross) < 0.1f)
                    {
                        // U-turn: bulge forward
                        b = Vec2.Lerp(a, c, 0.5f) + din * (roadWidth * 0.6f);
                        kind = TurnKind.Left;
                    }
                    else
                    {
                        // Intersection of a + t*din and c - s*dout
                        float denom = Vec2.Cross(din, dout);
                        Vec2 diff = c - a;
                        float t = Vec2.Cross(diff, dout) / denom;
                        b = a + din * t;
                        kind = cross < 0 ? TurnKind.Right : TurnKind.Left;
                    }
                    float len = 0f;
                    Vec2 prev = a;
                    for (int k = 1; k <= 10; k++)
                    {
                        Vec2 p = MathUtil.Bezier(a, b, c, k / 10f);
                        len += Vec2.Distance(prev, p);
                        prev = p;
                    }
                    outs[inL].Add(tFrom.Count);
                    tFrom.Add(inL); tTo.Add(outL); tInter.Add(inter);
                    p0.Add(a); p1.Add(b); p2.Add(c); tl.Add(MathF.Max(0.5f, len)); tk.Add(kind);
                }
            }
            TurnCount = tFrom.Count;
            TFrom = tFrom.ToArray(); TTo = tTo.ToArray(); TInter = tInter.ToArray();
            TP0 = p0.ToArray(); TP1 = p1.ToArray(); TP2 = p2.ToArray(); TLen = tl.ToArray(); TKind = tk.ToArray();
            LOutStart = new int[LaneCount + 1];
            for (int l = 0; l < LaneCount; l++) LOutStart[l + 1] = LOutStart[l] + outs[l].Count;
            LOutTurns = new int[LOutStart[LaneCount]];
            for (int l = 0; l < LaneCount; l++)
                for (int k = 0; k < outs[l].Count; k++) LOutTurns[LOutStart[l] + k] = outs[l][k];
            LaneBlocked = new bool[LaneCount];
        }

        private void SetupLane(int lane, RoadSegment seg, int from, int to, float roadWidth)
        {
            var a = new Vec2(IX[from], IZ[from]);
            var b = new Vec2(IX[to], IZ[to]);
            Vec2 d = (b - a).Normalized;
            Vec2 r = d.Right;
            float half = roadWidth * 0.5f;
            LSeg[lane] = seg.Id;
            LFrom[lane] = from;
            LTo[lane] = to;
            LBridge[lane] = seg.Bridge;
            LDir[lane] = d;
            LStart[lane] = a + d * half + r * LaneOffset;
            LEnd[lane] = b - d * half + r * LaneOffset;
            LLen[lane] = Vec2.Distance(LStart[lane], LEnd[lane]);
        }

        public Vec2 LanePoint(int lane, float s) => LStart[lane] + LDir[lane] * s;

        public int FindTurn(int fromLane, int toLane)
        {
            for (int k = LOutStart[fromLane]; k < LOutStart[fromLane + 1]; k++)
                if (TTo[LOutTurns[k]] == toLane) return LOutTurns[k];
            return -1;
        }

        public bool UpdateBlocking(bool northClosed, bool southClosed, bool bridgesFlooded)
        {
            bool changed = false;
            for (int l = 0; l < LaneCount; l++)
            {
                int br = LBridge[l];
                bool blocked = br >= 0 && (bridgesFlooded || (br == 0 && northClosed) || (br == 1 && southClosed));
                if (blocked != LaneBlocked[l]) { LaneBlocked[l] = blocked; changed = true; }
            }
            if (changed) Version++;
            return changed;
        }
    }

    /// <summary>A* over lanes. Start lane is always allowed (a car already on a bridge may finish crossing).</summary>
    public sealed class RoadPathfinder
    {
        private readonly RoadNetwork _n;
        private readonly float[] _g;
        private readonly int[] _from;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _search;
        private readonly BinaryHeap _open;
        private readonly List<int> _tmp = new List<int>(64);

        public RoadPathfinder(RoadNetwork n)
        {
            _n = n;
            _g = new float[n.LaneCount];
            _from = new int[n.LaneCount];
            _stamp = new int[n.LaneCount];
            _closed = new bool[n.LaneCount];
            _open = new BinaryHeap(n.LaneCount * 4 + 16);
        }

        /// <summary>Fills <paramref name="route"/> with lanes from start to goal. Returns false if unreachable.</summary>
        public bool Find(int startLane, int goalLane, List<int> route)
        {
            route.Clear();
            if (startLane < 0 || goalLane < 0) return false;
            if (startLane == goalLane) { route.Add(startLane); return true; }
            if (_n.LaneBlocked[goalLane]) return false;
            _search++;
            _open.Clear();
            Touch(startLane);
            _g[startLane] = 0f;
            _open.Push(startLane, 0f);
            Vec2 goalPos = _n.LStart[goalLane];
            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closed[cur]) continue;
                _closed[cur] = true;
                if (cur == goalLane)
                {
                    _tmp.Clear();
                    int c = cur;
                    while (c != -1) { _tmp.Add(c); if (c == startLane) break; c = _from[c]; }
                    for (int i = _tmp.Count - 1; i >= 0; i--) route.Add(_tmp[i]);
                    return true;
                }
                for (int k = _n.LOutStart[cur]; k < _n.LOutStart[cur + 1]; k++)
                {
                    int t = _n.LOutTurns[k];
                    int nb = _n.TTo[t];
                    if (_n.LaneBlocked[nb]) continue;
                    Touch(nb);
                    if (_closed[nb]) continue;
                    float pen = _n.TKind[t] == TurnKind.Left ? 6f : (_n.TKind[t] == TurnKind.Right ? 2f : 0f);
                    float ng = _g[cur] + _n.TLen[t] + _n.LLen[nb] + pen;
                    if (ng < _g[nb])
                    {
                        _g[nb] = ng;
                        _from[nb] = cur;
                        _open.Push(nb, ng + Vec2.Distance(_n.LEnd[nb], goalPos));
                    }
                }
            }
            return false;
        }

        private void Touch(int l)
        {
            if (_stamp[l] == _search) return;
            _stamp[l] = _search;
            _g[l] = float.MaxValue;
            _from[l] = -1;
            _closed[l] = false;
        }
    }
}
