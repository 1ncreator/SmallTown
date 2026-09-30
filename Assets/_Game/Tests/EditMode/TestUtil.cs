using System.Collections.Generic;
using System.IO;
using SmallTown.Commands;
using SmallTown.Generation;
using SmallTown.Simulation;
using SmallTown.Simulation.Pathfinding;
using SmallTown.Simulation.World;
using UnityEngine;

namespace SmallTown.Tests
{
    internal static class TestUtil
    {
        private static CommandGrammar _grammar;
        private static CityData _city;

        public static SimConfig Config(int seed = 20260923)
        {
            return new SimConfig { Seed = seed };
        }

        public static CommandGrammar Grammar()
        {
            if (_grammar == null)
                _grammar = CommandGrammar.FromJson(File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Configs/CommandGrammar.json")));
            return _grammar;
        }

        /// <summary>Shared read-only city for parser tests (blocking flags are never touched there).</summary>
        public static CityData City()
        {
            if (_city == null) _city = CityGenerator.Generate(Config());
            return _city;
        }

        /// <summary>Nodes reachable from start over the pedestrian graph honouring current blocking.</summary>
        public static HashSet<int> Reachable(NavGraph g, int start)
        {
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                for (int k = g.AdjStart[n]; k < g.AdjStart[n + 1]; k++)
                {
                    int e = g.AdjEdges[k];
                    if (g.EdgeBlocked(e)) continue;
                    int o = g.Other(e, n);
                    if (seen.Add(o)) stack.Push(o);
                }
            }
            return seen;
        }

        public static HashSet<int> ReachableLanes(RoadNetwork n, int start, bool reverse)
        {
            var seen = new HashSet<int> { start };
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                int l = stack.Pop();
                if (!reverse)
                {
                    for (int k = n.LOutStart[l]; k < n.LOutStart[l + 1]; k++)
                    {
                        int to = n.TTo[n.LOutTurns[k]];
                        if (n.LaneBlocked[to]) continue;
                        if (seen.Add(to)) stack.Push(to);
                    }
                }
                else
                {
                    for (int t = 0; t < n.TurnCount; t++)
                    {
                        if (n.TTo[t] != l) continue;
                        int from = n.TFrom[t];
                        if (n.LaneBlocked[from]) continue;
                        if (seen.Add(from)) stack.Push(from);
                    }
                }
            }
            return seen;
        }
    }
}
