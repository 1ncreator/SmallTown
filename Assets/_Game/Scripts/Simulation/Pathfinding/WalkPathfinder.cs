using System;
using System.Collections.Generic;

namespace SmallTown.Simulation.Pathfinding
{
    /// <summary>
    /// A* over the pedestrian graph with a result cache that is invalidated whenever the
    /// graph's blocking version changes. Paths are immutable node arrays shared by agents.
    /// </summary>
    public sealed class WalkPathfinder
    {
        private static readonly int[] NoPath = new int[0];
        private readonly NavGraph _g;
        private readonly Dictionary<long, int[]> _cache = new Dictionary<long, int[]>(4096);
        private int _cacheVersion = -1;
        private readonly float[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _search;
        private readonly BinaryHeap _open;
        private readonly List<int> _tmp = new List<int>(256);

        public int CacheHits, CacheMisses;

        public WalkPathfinder(NavGraph graph)
        {
            _g = graph;
            _gScore = new float[graph.NodeCount];
            _cameFrom = new int[graph.NodeCount];
            _stamp = new int[graph.NodeCount];
            _closed = new bool[graph.NodeCount];
            _open = new BinaryHeap(graph.NodeCount * 2 + 16);
        }

        public void ClearCache()
        {
            _cache.Clear();
            _cacheVersion = _g.Version;
        }

        /// <summary>Returns node path from start to goal inclusive, or null when unreachable.</summary>
        public int[] Find(int start, int goal)
        {
            if (start < 0 || goal < 0) return null;
            if (_cacheVersion != _g.Version) ClearCache();
            bool escape = _g.NodeFlooded[start];
            long key = ((long)start << 32) | (uint)goal;
            if (escape) key |= 1L << 62;
            if (_cache.TryGetValue(key, out var cached))
            {
                CacheHits++;
                return cached.Length == 0 ? null : cached;
            }
            CacheMisses++;
            var path = Search(start, goal, escape);
            if (_cache.Count > 20000) _cache.Clear();
            _cache[key] = path ?? NoPath;
            return path;
        }

        public float PathLength(int[] path)
        {
            if (path == null) return 0f;
            float len = 0f;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                float dx = _g.X[path[i]] - _g.X[path[i + 1]], dz = _g.Z[path[i]] - _g.Z[path[i + 1]];
                len += MathF.Sqrt(dx * dx + dz * dz);
            }
            return len;
        }

        private int[] Search(int start, int goal, bool escape)
        {
            if (start == goal) return new[] { start };
            if (_g.NodeFlooded[goal]) return null;
            _search++;
            if (_search == int.MaxValue)
            {
                Array.Clear(_stamp, 0, _stamp.Length);
                _search = 1;
            }
            _open.Clear();
            Touch(start);
            _gScore[start] = 0f;
            _open.Push(start, H(start, goal));
            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closed[cur]) continue;
                _closed[cur] = true;
                if (cur == goal) return Reconstruct(start, goal);
                bool curFlooded = _g.NodeFlooded[cur];
                for (int k = _g.AdjStart[cur]; k < _g.AdjStart[cur + 1]; k++)
                {
                    int e = _g.AdjEdges[k];
                    if (_g.EdgeClosed[e]) continue;
                    if (_g.EdgeFlooded[e] && !(escape && curFlooded)) continue;
                    int nb = _g.Other(e, cur);
                    Touch(nb);
                    if (_closed[nb]) continue;
                    float cost = _g.ELen[e];
                    if (_g.EdgeFlooded[e]) cost *= 3f;
                    float ng = _gScore[cur] + cost;
                    if (ng < _gScore[nb])
                    {
                        _gScore[nb] = ng;
                        _cameFrom[nb] = cur;
                        _open.Push(nb, ng + H(nb, goal));
                    }
                }
            }
            return null;
        }

        private void Touch(int n)
        {
            if (_stamp[n] == _search) return;
            _stamp[n] = _search;
            _gScore[n] = float.MaxValue;
            _cameFrom[n] = -1;
            _closed[n] = false;
        }

        private float H(int a, int b)
        {
            float dx = _g.X[a] - _g.X[b], dz = _g.Z[a] - _g.Z[b];
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private int[] Reconstruct(int start, int goal)
        {
            _tmp.Clear();
            int cur = goal;
            while (cur != -1)
            {
                _tmp.Add(cur);
                if (cur == start) break;
                cur = _cameFrom[cur];
            }
            _tmp.Reverse();
            return _tmp.ToArray();
        }
    }

    /// <summary>Min-heap of (item, priority) with deterministic tie-breaking by item id.</summary>
    public sealed class BinaryHeap
    {
        private int[] _items;
        private float[] _prio;
        public int Count;

        public BinaryHeap(int capacity)
        {
            _items = new int[capacity];
            _prio = new float[capacity];
        }

        public void Clear() => Count = 0;

        public void Push(int item, float priority)
        {
            if (Count == _items.Length)
            {
                Array.Resize(ref _items, Count * 2);
                Array.Resize(ref _prio, Count * 2);
            }
            int i = Count++;
            _items[i] = item;
            _prio[i] = priority;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(i, p)) break;
                Swap(i, p);
                i = p;
            }
        }

        public int Pop()
        {
            int top = _items[0];
            Count--;
            if (Count > 0)
            {
                _items[0] = _items[Count];
                _prio[0] = _prio[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < Count && Less(l, m)) m = l;
                    if (r < Count && Less(r, m)) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }
            return top;
        }

        private bool Less(int a, int b) => _prio[a] < _prio[b] || (_prio[a] == _prio[b] && _items[a] < _items[b]);

        private void Swap(int a, int b)
        {
            int ti = _items[a]; _items[a] = _items[b]; _items[b] = ti;
            float tp = _prio[a]; _prio[a] = _prio[b]; _prio[b] = tp;
        }
    }
}
