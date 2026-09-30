using System;
using System.Collections.Generic;
using SmallTown.Generation;
using SmallTown.Simulation.Pathfinding;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Simulation.Agents
{
    /// <summary>Daily schedules, decisions, pedestrian movement and reactions to world events.</summary>
    public sealed class ResidentSystem
    {
        public const float FireRadius = 38f;
        public const float RobberyRadius = 42f;
        public const float SafeDistance = 48f;

        private readonly TownSimulation _sim;
        private readonly CityData _c;
        private readonly NavGraph _g;
        public readonly int[] CrosswalkOcc;
        private readonly List<int> _shops = new List<int>();
        private readonly List<int> _homes = new List<int>();

        public ResidentSystem(TownSimulation sim)
        {
            _sim = sim;
            _c = sim.City;
            _g = sim.City.Walk;
            CrosswalkOcc = new int[_c.Roads.Crosswalks.Count];
            foreach (var b in _c.Buildings)
            {
                if (b.Type == BuildingType.Shop) _shops.Add(b.Id);
                if (b.IsHome) _homes.Add(b.Id);
            }
        }

        // ------------------------------------------------------------------ population

        public void Populate(DetRandom rng, List<Resident> list, int count)
        {
            var homeSlots = new List<int>();
            foreach (var b in _c.Buildings)
                if (b.IsHome)
                    for (int k = 0; k < b.Capacity; k++) homeSlots.Add(b.Id);
            var jobSlots = new List<int>();
            foreach (var b in _c.Buildings)
                if (b.IsWorkplace)
                    for (int k = 0; k < b.Capacity; k++) jobSlots.Add(b.Id);
            Shuffle(rng, homeSlots);
            Shuffle(rng, jobSlots);
            int job = 0;
            for (int i = 0; i < count; i++)
            {
                var r = new Resident { Id = i };
                r.Female = rng.Chance(0.5f);
                float roll = rng.NextFloat();
                if (roll < 0.18f) { r.Role = Role.Student; r.Age = rng.Range(7, 18); }
                else if (roll < 0.36f) { r.Role = Role.Retired; r.Age = rng.Range(65, 88); }
                else { r.Role = Role.Worker; r.Age = rng.Range(20, 65); }
                string first = r.Female ? rng.Pick(NameTables.FemaleFirst) : rng.Pick(NameTables.MaleFirst);
                r.Name = first + " " + NameTables.LastName(rng.Next(NameTables.LastNames.Length), r.Female);
                r.Home = homeSlots.Count > 0 ? homeSlots[i % homeSlots.Count] : 0;
                if (r.Role == Role.Worker)
                {
                    if (job < jobSlots.Count) r.Work = jobSlots[job++];
                    else r.Role = Role.Retired;
                }
                if (r.Role == Role.Student) r.Work = _c.SchoolBuilding;
                r.Friend = _homes[rng.Next(_homes.Count)];
                switch (r.Role)
                {
                    case Role.Worker:
                        r.WakeHour = rng.Range(6.0f, 7.4f);
                        r.LeaveHour = r.WakeHour + rng.Range(0.5f, 1.6f);
                        r.EndHour = rng.Range(16.0f, 18.7f);
                        r.BedHour = rng.Range(21.8f, 23.8f);
                        r.SpeedFactor = rng.Range(0.9f, 1.15f);
                        break;
                    case Role.Student:
                        r.WakeHour = rng.Range(6.4f, 7.1f);
                        r.LeaveHour = rng.Range(7.3f, 7.8f);
                        r.EndHour = rng.Range(14.3f, 15.1f);
                        r.BedHour = rng.Range(20.6f, 22.0f);
                        r.SpeedFactor = rng.Range(1.0f, 1.2f);
                        break;
                    default:
                        r.WakeHour = rng.Range(6.8f, 8.8f);
                        r.LeaveHour = r.WakeHour;
                        r.EndHour = r.WakeHour;
                        r.BedHour = rng.Range(21.0f, 23.0f);
                        r.SpeedFactor = rng.Range(0.72f, 0.9f);
                        break;
                }
                r.LunchOut = rng.Chance(0.4f);
                r.Umbrella = rng.Chance(0.72f);
                r.FestivalLove = rng.NextFloat();
                r.Outdoorsy = rng.Range(0.5f, 1.5f);
                r.Lateral = rng.Range(0.2f, 0.75f);
                r.ColorSeed = (uint)(rng.NextULong() >> 20);
                // Everybody starts the day at home.
                r.State = ResidentState.Inside;
                r.InsideBuilding = r.Home;
                r.Act = Activity.Home;
                r.TargetBuilding = r.Home;
                r.Node = _c.Buildings[r.Home].EntranceNode;
                r.NextThink = rng.Range(0, 40);
                SetPosFromNode(r, r.Node);
                r.PX = r.X; r.PY = r.Y; r.PZ = r.Z;
                list.Add(r);
            }
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

        // ------------------------------------------------------------------ update

        public void Update()
        {
            long tick = _sim.Tick;
            var list = _sim.Residents;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (tick >= r.NextThink)
                {
                    Think(r, false);
                    r.NextThink = tick + 20 + (r.Id % 7);
                }
                Move(r);
            }
        }

        public bool BuildingClosed(int building)
        {
            var w = _sim.World;
            if (w.FireActive && w.FireBuilding == building) return true;
            if (w.RobberyActive && w.RobberyBuilding == building) return true;
            return false;
        }

        /// <summary>Re-evaluates the resident's plan. Returns true if the target changed.</summary>
        public bool Think(Resident r, bool force)
        {
            Decide(r, out var act, out int building, out int node, out var why);
            bool same = act == r.Act && building == r.TargetBuilding && node == r.TargetNode;
            if (same)
            {
                if (r.NoRoute)
                {
                    if (force || _sim.Tick - r.NoRouteSince >= 50) Plan(r);
                }
                else if (force && r.State == ResidentState.Walking && PathBlockedAhead(r))
                {
                    Plan(r);
                }
                return false;
            }
            r.Act = act;
            r.TargetBuilding = building;
            r.TargetNode = node;
            r.ActSince = _sim.Tick;
            r.Reason = why;
            Plan(r);
            return true;
        }

        public int GoalNode(Resident r) => r.TargetBuilding >= 0 ? _c.Buildings[r.TargetBuilding].EntranceNode : r.TargetNode;

        private bool PathBlockedAhead(Resident r)
        {
            if (r.Path == null) return false;
            for (int i = r.PathIdx + 1; i + 1 < r.Path.Length; i++)
            {
                int e = _g.FindEdge(r.Path[i], r.Path[i + 1]);
                if (e < 0 || _g.EdgeBlocked(e)) return true;
            }
            return false;
        }

        private void Decide(Resident r, out Activity act, out int building, out int node, out PlanReason why)
        {
            var w = _sim.World;
            float hour = _sim.Hour;
            building = -1;
            node = -1;

            if (r.Forced == ForcedReason.Fire && !w.FireActive) r.Forced = ForcedReason.None;
            if (r.Forced == ForcedReason.Robbery && !w.RobberyActive) r.Forced = ForcedReason.None;
            if (r.Forced != ForcedReason.None)
            {
                act = r.Act;
                node = r.TargetNode;
                why = PlanReason.Emergency;
                return;
            }
            if (w.FireActive && CheckDanger(r, w.FireBuilding, FireRadius, ForcedReason.Fire, out act, out node))
            {
                why = PlanReason.Emergency;
                return;
            }
            if (w.RobberyActive && CheckDanger(r, w.RobberyBuilding, RobberyRadius, ForcedReason.Robbery, out act, out node))
            {
                why = PlanReason.Emergency;
                return;
            }

            bool night = hour >= r.BedHour || hour < r.WakeHour;
            if (night)
            {
                act = Activity.Home;
                building = r.Home;
                why = PlanReason.Night;
                return;
            }
            bool storm = w.Weather == Weather.Storm;
            bool bad = w.Precipitation;

            if (w.Festival && !storm && hour >= 9f)
            {
                float need = bad ? 0.62f : 0.35f;
                bool busy = (r.Role == Role.Worker && hour >= r.LeaveHour && hour < r.EndHour && r.FestivalLove < 0.85f)
                            || (r.Role == Role.Student && hour >= 8f && hour < 14.5f);
                if (r.FestivalLove > need && !busy)
                {
                    act = Activity.Festival;
                    node = _c.FestivalNode;
                    why = PlanReason.Festival;
                    return;
                }
            }

            why = PlanReason.Schedule;
            if (r.Role == Role.Worker && hour >= r.LeaveHour && hour < r.EndHour)
            {
                if (r.LunchOut && hour >= 12.5f && hour < 13.3f && _shops.Count > 0)
                {
                    act = Activity.Lunch;
                    if (!bad && DetRandom.Hash01(r.Id, _sim.Day, 5) < 0.4f)
                        node = _c.ParkSpotNodes[(int)(DetRandom.Hash(r.Id, _sim.Day, 6) % (uint)_c.ParkSpotNodes.Count)];
                    else
                    {
                        building = _shops[(int)(DetRandom.Hash(r.Id, _sim.Day, 7) % (uint)_shops.Count)];
                        if (BuildingClosed(building)) { act = Activity.Home; building = r.Home; }
                    }
                    if (bad) why = PlanReason.Weather;
                    return;
                }
                if (BuildingClosed(r.Work))
                {
                    act = Activity.Home;
                    building = r.Home;
                    why = PlanReason.Emergency;
                    return;
                }
                act = Activity.Work;
                building = r.Work;
                return;
            }
            if (r.Role == Role.Student && hour >= 8f && hour < 14.5f)
            {
                if (BuildingClosed(r.Work))
                {
                    act = Activity.Home;
                    building = r.Home;
                    why = PlanReason.Emergency;
                    return;
                }
                act = Activity.School;
                building = r.Work;
                return;
            }
            if (r.Role != Role.Retired && hour < r.LeaveHour)
            {
                act = Activity.Home;
                building = r.Home;
                return;
            }
            Leisure(r, hour, bad, out act, out building, out node, out why);
        }

        private bool CheckDanger(Resident r, int dangerBuilding, float radius, ForcedReason reason, out Activity act, out int node)
        {
            act = Activity.Home;
            node = -1;
            var b = _c.Buildings[dangerBuilding];
            bool inside = r.State == ResidentState.Inside && r.InsideBuilding == dangerBuilding;
            bool near = false;
            if (r.IsOutside)
            {
                float dx = r.X - b.X, dz = r.Z - b.Z;
                near = dx * dx + dz * dz < radius * radius;
            }
            if (!inside && !near) return false;
            r.Forced = reason;
            act = inside ? Activity.Evacuate : Activity.Flee;
            node = SafeNodeAwayFrom(b.X, b.Z, r.X, r.Z);
            return true;
        }

        public int SafeNodeAwayFrom(float dx, float dz, float px, float pz)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _c.SafeNodes.Count; i++)
            {
                int n = _c.SafeNodes[i];
                if (_g.NodeFlooded[n]) continue;
                float ex = _g.X[n] - dx, ez = _g.Z[n] - dz;
                if (ex * ex + ez * ez < SafeDistance * SafeDistance) continue;
                float fx = _g.X[n] - px, fz = _g.Z[n] - pz;
                float d = fx * fx + fz * fz;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best >= 0 ? best : _c.SafeNodes[0];
        }

        private void Leisure(Resident r, float hour, bool bad, out Activity act, out int building, out int node, out PlanReason why)
        {
            var w = _sim.World;
            building = -1;
            node = -1;
            why = bad ? PlanReason.Weather : PlanReason.Schedule;
            int slot = (int)(hour / 1.5f) + _sim.Day * 16;
            uint h = DetRandom.Hash(r.Id, slot, 99);
            float u = (h >> 8) * (1f / 16777216f);
            float outdoor = (bad ? 0f : 1f) * (w.Season == Season.Winter ? 0.45f : 1f) * r.Outdoorsy;
            float wHome = 0.30f + (r.Role == Role.Retired ? 0.1f : 0f) + (hour >= 20.5f ? 0.45f : 0f) + (bad ? 0.25f : 0f);
            float wShop = hour >= 8f && hour < 20f && _shops.Count > 0 ? 0.18f : 0f;
            float wPark = 0.14f * outdoor;
            float wMarket = hour >= 8f && hour < 19f ? 0.10f * outdoor : 0f;
            float wProm = 0.09f * outdoor;
            float wVisit = 0.08f;
            float wChurch = hour >= 9f && hour < 19f ? 0.03f : 0f;
            float wBank = hour >= 9f && hour < 17f && !BuildingClosed(_c.BankBuilding) ? 0.03f : 0f;
            float wRiver = w.LotIsPark ? 0.10f * outdoor : 0f;
            float total = wHome + wShop + wPark + wMarket + wProm + wVisit + wChurch + wBank + wRiver;
            float pick = u * total;
            uint h2 = DetRandom.Hash(r.Id, slot, 17);

            if ((pick -= wHome) < 0f) { act = Activity.Home; building = r.Home; return; }
            if ((pick -= wShop) < 0f)
            {
                act = Activity.Shop;
                building = _shops[(int)(h2 % (uint)_shops.Count)];
                if (BuildingClosed(building)) { act = Activity.Home; building = r.Home; }
                return;
            }
            if ((pick -= wPark) < 0f) { act = Activity.Park; node = _c.ParkSpotNodes[(int)(h2 % (uint)_c.ParkSpotNodes.Count)]; return; }
            if ((pick -= wMarket) < 0f) { act = Activity.Market; node = _c.MarketSpotNodes[(int)(h2 % (uint)_c.MarketSpotNodes.Count)]; return; }
            if ((pick -= wProm) < 0f) { act = Activity.Promenade; node = _c.PromenadeNodes[(int)(h2 % (uint)_c.PromenadeNodes.Count)]; return; }
            if ((pick -= wVisit) < 0f) { act = Activity.Visit; building = r.Friend; return; }
            if ((pick -= wChurch) < 0f) { act = Activity.Church; building = _c.ChurchBuilding; return; }
            if ((pick -= wBank) < 0f) { act = Activity.Bank; building = _c.BankBuilding; return; }
            if (wRiver > 0f && _c.RiversideNodes.Count > 0)
            {
                act = Activity.RiversidePark;
                node = _c.RiversideNodes[(int)(h2 % (uint)_c.RiversideNodes.Count)];
                return;
            }
            act = Activity.Home;
            building = r.Home;
        }

        // ------------------------------------------------------------------ planning & movement

        public void Plan(Resident r)
        {
            int goal = GoalNode(r);
            if (goal < 0) { r.NoRoute = false; return; }
            if (r.State == ResidentState.Inside && r.InsideBuilding == r.TargetBuilding && r.TargetBuilding >= 0) { r.NoRoute = false; return; }
            if (r.State == ResidentState.Standing && r.TargetBuilding < 0 && r.Node == goal)
            {
                r.NoRoute = false;
                ComputeStandOffset(r);
                SetStandingPos(r);
                return;
            }
            if (r.State == ResidentState.Walking && r.Path != null && r.PathIdx + 1 < r.Path.Length)
            {
                PlanFromEdge(r, goal);
                return;
            }
            int start = r.Node;
            if (r.State == ResidentState.Inside && r.InsideBuilding >= 0) start = _c.Buildings[r.InsideBuilding].EntranceNode;
            if (start == goal)
            {
                r.NoRoute = false;
                r.Path = new[] { start };
                r.PathIdx = 0;
                r.EdgeT = 0f;
                r.State = ResidentState.Walking;
                r.InsideBuilding = -1;
                Arrive(r);
                return;
            }
            var path = _sim.WalkPaths.Find(start, goal);
            if (path == null)
            {
                SetNoRoute(r);
                return;
            }
            r.NoRoute = false;
            r.State = ResidentState.Walking;
            r.InsideBuilding = -1;
            r.Node = start;
            r.Path = path;
            r.PathIdx = 0;
            r.EdgeT = 0f;
        }

        private void SetNoRoute(Resident r)
        {
            if (!r.NoRoute) r.NoRouteSince = _sim.Tick;
            else r.NoRouteSince = _sim.Tick;
            r.NoRoute = true;
            if (r.State == ResidentState.Walking && (r.Path == null || r.PathIdx + 1 >= r.Path.Length))
            {
                r.State = ResidentState.Standing;
                ComputeStandOffset(r);
                SetStandingPos(r);
            }
        }

        private void PlanFromEdge(Resident r, int goal)
        {
            int a = r.Path[r.PathIdx], b = r.Path[r.PathIdx + 1];
            int e = _g.FindEdge(a, b);
            float len = e >= 0 ? _g.ELen[e] : 0.01f;
            var pa = _sim.WalkPaths.Find(a, goal);
            var pb = _sim.WalkPaths.Find(b, goal);
            float costA = pa != null ? r.EdgeT + _sim.WalkPaths.PathLength(pa) : float.MaxValue;
            float costB = pb != null ? (len - r.EdgeT) + _sim.WalkPaths.PathLength(pb) : float.MaxValue;
            if (pa == null && pb == null)
            {
                if (!r.NoRoute) r.NoRouteSince = _sim.Tick;
                r.NoRoute = true;
                r.Path = new[] { a, b };
                r.PathIdx = 0;
                return;
            }
            r.NoRoute = false;
            if (costB <= costA)
            {
                var np = new int[pb.Length + 1];
                np[0] = a;
                Array.Copy(pb, 0, np, 1, pb.Length);
                r.Path = np;
                r.PathIdx = 0;
            }
            else
            {
                var np = new int[pa.Length + 1];
                np[0] = b;
                Array.Copy(pa, 0, np, 1, pa.Length);
                r.Path = np;
                r.PathIdx = 0;
                r.EdgeT = len - r.EdgeT;
            }
        }

        private float SpeedOf(Resident r)
        {
            float s = _sim.Config.WalkSpeed * r.SpeedFactor;
            switch (_sim.World.Weather)
            {
                case Weather.Rain: s *= 1.1f; break;
                case Weather.Storm: s *= 1.15f; break;
                case Weather.Snow: s *= 0.8f; break;
            }
            if (r.Act == Activity.Flee || r.Act == Activity.Evacuate) s *= 1.7f;
            return s;
        }

        private void Move(Resident r)
        {
            r.PX = r.X; r.PY = r.Y; r.PZ = r.Z; r.PHeading = r.Heading;
            r.Moving = false;
            if (r.State != ResidentState.Walking || r.Path == null) return;
            float remaining = SpeedOf(r) * _sim.Dt;
            int guard = 0;
            while (remaining > 1e-5f && guard++ < 12)
            {
                if (r.PathIdx >= r.Path.Length - 1)
                {
                    Arrive(r);
                    return;
                }
                int a = r.Path[r.PathIdx], b = r.Path[r.PathIdx + 1];
                int e = _g.FindEdge(a, b);
                if (e < 0)
                {
                    r.Node = a;
                    r.State = ResidentState.Standing;
                    Plan(r);
                    return;
                }
                if (r.EdgeT <= 0f)
                {
                    bool escaping = _g.EdgeFlooded[e] && !_g.EdgeClosed[e] && _g.NodeFlooded[a];
                    if (_g.EdgeBlocked(e) && !escaping)
                    {
                        r.Node = a;
                        r.State = ResidentState.Standing;
                        r.Path = null;
                        r.Reason = PlanReason.Blocked;
                        Plan(r);
                        if (r.State != ResidentState.Walking) { SetStandingPos(r); return; }
                        continue;
                    }
                    int cw = _g.ECrosswalk[e];
                    if (cw >= 0 && r.OnCrosswalk != cw)
                    {
                        CrosswalkOcc[cw]++;
                        r.OnCrosswalk = cw;
                    }
                }
                float len = _g.ELen[e];
                float step = Math.Min(remaining, len - r.EdgeT);
                r.EdgeT += step;
                remaining -= step;
                if (r.EdgeT >= len - 1e-4f)
                {
                    LeaveCrosswalk(r);
                    r.PathIdx++;
                    r.EdgeT = 0f;
                    r.Node = b;
                }
            }
            if (r.State == ResidentState.Walking) SetWalkingPos(r);
            r.Moving = true;
        }

        private void LeaveCrosswalk(Resident r)
        {
            if (r.OnCrosswalk >= 0)
            {
                CrosswalkOcc[r.OnCrosswalk] = Math.Max(0, CrosswalkOcc[r.OnCrosswalk] - 1);
                r.OnCrosswalk = -1;
            }
        }

        private void Arrive(Resident r)
        {
            LeaveCrosswalk(r);
            if (r.Path != null && r.Path.Length > 0) r.Node = r.Path[r.Path.Length - 1];
            r.Path = null;
            r.PathIdx = 0;
            r.EdgeT = 0f;
            int goal = GoalNode(r);
            if (r.Node != goal)
            {
                r.State = ResidentState.Standing;
                ComputeStandOffset(r);
                SetStandingPos(r);
                return;
            }
            if (r.TargetBuilding >= 0)
            {
                r.State = ResidentState.Inside;
                r.InsideBuilding = r.TargetBuilding;
                SetPosFromNode(r, r.Node);
                return;
            }
            r.State = ResidentState.Standing;
            ComputeStandOffset(r);
            SetStandingPos(r);
        }

        private void ComputeStandOffset(Resident r)
        {
            float h1 = DetRandom.Hash01(r.Id, (int)(r.ActSince & 0x7fffffff), 1);
            float h2 = DetRandom.Hash01(r.Id, (int)(r.ActSince & 0x7fffffff), 2);
            float ang = h1 * MathUtil.Pi * 2f;
            float rad;
            switch (r.Act)
            {
                case Activity.Festival:
                    r.StandX = (h1 - 0.5f) * 13f;
                    r.StandZ = -4.2f + h2 * 7.6f;
                    return;
                case Activity.Park:
                case Activity.Market:
                case Activity.Lunch:
                    rad = 1.0f + h2 * 2.8f;
                    break;
                case Activity.Evacuate:
                case Activity.Flee:
                    rad = 0.8f + h2 * 3.5f;
                    break;
                default:
                    rad = 0.3f + h2 * 1.0f;
                    break;
            }
            r.StandX = MathF.Cos(ang) * rad;
            r.StandZ = MathF.Sin(ang) * rad;
        }

        private void SetStandingPos(Resident r)
        {
            if (r.Node < 0) return;
            r.X = _g.X[r.Node] + r.StandX;
            r.Y = _g.Y[r.Node];
            r.Z = _g.Z[r.Node] + r.StandZ;
            if (r.Act == Activity.Festival)
            {
                r.Heading = MathUtil.HeadingDeg(_c.StageX - r.X, _c.StageZ - r.Z);
            }
        }

        private void SetPosFromNode(Resident r, int node)
        {
            r.X = _g.X[node];
            r.Y = _g.Y[node];
            r.Z = _g.Z[node];
        }

        private void SetWalkingPos(Resident r)
        {
            if (r.Path == null || r.PathIdx + 1 >= r.Path.Length)
            {
                SetPosFromNode(r, r.Node);
                return;
            }
            int a = r.Path[r.PathIdx], b = r.Path[r.PathIdx + 1];
            float dx = _g.X[b] - _g.X[a], dz = _g.Z[b] - _g.Z[a];
            float len = MathF.Sqrt(dx * dx + dz * dz);
            int e = _g.FindEdge(a, b);
            float elen = e >= 0 ? _g.ELen[e] : Math.Max(len, 0.01f);
            float t = MathUtil.Clamp01(r.EdgeT / elen);
            float nx = len > 1e-4f ? dz / len : 0f, nz = len > 1e-4f ? -dx / len : 0f;
            r.X = _g.X[a] + dx * t + nx * r.Lateral;
            r.Z = _g.Z[a] + dz * t + nz * r.Lateral;
            r.Y = _g.Y[a] + (_g.Y[b] - _g.Y[a]) * t;
            if (len > 1e-4f) r.Heading = MathUtil.HeadingDeg(dx, dz);
        }

        /// <summary>Rebuilds derived crosswalk occupancy after loading a snapshot.</summary>
        public void RebuildDerived()
        {
            Array.Clear(CrosswalkOcc, 0, CrosswalkOcc.Length);
            foreach (var r in _sim.Residents)
                if (r.OnCrosswalk >= 0 && r.OnCrosswalk < CrosswalkOcc.Length) CrosswalkOcc[r.OnCrosswalk]++;
        }

        public static bool IsOutdoorLeisure(Activity a) =>
            a == Activity.Park || a == Activity.Market || a == Activity.Promenade || a == Activity.RiversidePark || a == Activity.Festival;
    }
}
