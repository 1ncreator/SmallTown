using System;
using System.Collections.Generic;
using SmallTown.Simulation.Pathfinding;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Simulation.Traffic
{
    /// <summary>
    /// Lane-following traffic. Intersections are handled by a first-come-first-served reservation
    /// (one crossing movement at a time, emergency vehicles first, "don't block the box"),
    /// which works for T-junctions and bridge heads without timing plans. Cars always yield
    /// to pedestrians on zebra crossings. Unreachable destinations make cars pull over and wait.
    /// </summary>
    public sealed class TrafficSystem
    {
        public const float Accel = 3.2f;
        public const float Decel = 7f;
        public const float MinGap = 1.8f;
        public const float ParkOffset = 1.25f;
        public const float LotSpeed = 3.5f;

        private readonly TownSimulation _sim;
        private readonly CityData _c;
        private readonly RoadNetwork _n;
        public readonly List<int>[] LaneCars;
        public readonly List<int>[] InterUsers;
        public readonly bool[] LotTaken;
        private readonly int[] _best;
        private readonly int[] _bestStamp;
        private int _stamp;
        private readonly List<int> _tmp = new List<int>(32);
        private readonly List<int> _alt = new List<int>(32);
        private readonly List<int> _cand = new List<int>(32);
        private readonly List<int> _reroute = new List<int>(8);
        private readonly List<int> _workDest = new List<int>();
        private readonly List<int> _homeDest = new List<int>();
        private readonly List<int> _anyDest = new List<int>();
        private readonly Vec2[] _lotPts = new Vec2[5];
        private readonly float[] _lotY = new float[5];

        public float StopOffset => _c.Config.SidewalkWidth + 0.6f;

        public TrafficSystem(TownSimulation sim)
        {
            _sim = sim;
            _c = sim.City;
            _n = sim.City.Roads;
            LaneCars = new List<int>[_n.LaneCount];
            for (int i = 0; i < LaneCars.Length; i++) LaneCars[i] = new List<int>(8);
            InterUsers = new List<int>[_n.IntersectionCount];
            for (int i = 0; i < InterUsers.Length; i++) InterUsers[i] = new List<int>(4);
            LotTaken = new bool[_c.LotSlots.Count];
            _best = new int[_n.IntersectionCount];
            _bestStamp = new int[_n.IntersectionCount];
            foreach (var b in _c.Buildings)
            {
                if (b.CurbLane < 0) continue;
                if (b.Type == BuildingType.FireStation || b.Type == BuildingType.Police) continue;
                _anyDest.Add(b.Id);
                if (b.IsHome) _homeDest.Add(b.Id);
                if (b.IsWorkplace) _workDest.Add(b.Id);
            }
        }

        // ------------------------------------------------------------------ population

        public void Populate(DetRandom rng, List<Vehicle> list)
        {
            var cfg = _sim.Config;
            int id = 0;
            int lotCars = Math.Min(6, _c.LotSlots.Count);
            for (int i = 0; i < cfg.Cars; i++)
            {
                var v = new Vehicle { Id = id++, Kind = VehicleKind.Car, ColorIndex = rng.Next(12), HalfLen = 2.2f };
                if (i < lotCars)
                {
                    v.State = VehicleState.LotParked;
                    v.LotSlot = i * 2 % _c.LotSlots.Count;
                    if (LotTaken[v.LotSlot]) v.LotSlot = FreeSlot();
                    LotTaken[v.LotSlot] = true;
                    v.DwellUntil = rng.Range(300, 3000);
                    UpdateLotPose(v, float.MaxValue);
                }
                else
                {
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        var b = _c.Buildings[_anyDest[rng.Next(_anyDest.Count)]];
                        float s = MathUtil.Clamp(b.CurbS + rng.Range(-4f, 4f), 4f, _n.LLen[b.CurbLane] - 6f);
                        if (CurbOccupied(list, b.CurbLane, s, -1)) continue;
                        v.Lane = b.CurbLane;
                        v.S = s;
                        v.DestBuilding = b.Id;
                        break;
                    }
                    if (v.Lane < 0)
                    {
                        var b = _c.Buildings[_anyDest[i % _anyDest.Count]];
                        v.Lane = b.CurbLane;
                        v.S = b.CurbS;
                    }
                    v.State = VehicleState.Parked;
                    v.DwellUntil = rng.Range(0, 400);
                    SetParkedPose(v);
                }
                list.Add(v);
            }
            for (int i = 0; i < cfg.PoliceCars; i++)
                list.Add(MakeService(id++, VehicleKind.Police, _c.PoliceBuilding, -i * 6.5f, list));
            for (int i = 0; i < cfg.FireTrucks; i++)
                list.Add(MakeService(id++, VehicleKind.FireTruck, _c.FireStationBuilding, -i * 8f, list));
            foreach (var v in list)
            {
                v.PX = v.X; v.PY = v.Y; v.PZ = v.Z; v.PHeading = v.Heading;
            }
        }

        private Vehicle MakeService(int id, VehicleKind kind, int home, float offset, List<Vehicle> list)
        {
            var b = _c.Buildings[home];
            var v = new Vehicle
            {
                Id = id, Kind = kind, HomeBuilding = home, HomeOffset = offset,
                HalfLen = kind == VehicleKind.FireTruck ? 3.3f : 2.3f,
                State = VehicleState.Idle, Lane = b.CurbLane,
                S = MathUtil.Clamp(b.CurbS + offset, 4f, _n.LLen[b.CurbLane] - 5f),
                DestBuilding = home
            };
            SetParkedPose(v);
            return v;
        }

        private bool CurbOccupied(List<Vehicle> list, int lane, float s, int self)
        {
            foreach (var o in list)
            {
                if (o.Id == self || o.Lane != lane) continue;
                if (o.State != VehicleState.Parked && o.State != VehicleState.Idle && o.State != VehicleState.OnScene) continue;
                if (Math.Abs(o.S - s) < 5.4f) return true;
            }
            return false;
        }

        private int FreeSlot()
        {
            for (int i = 0; i < LotTaken.Length; i++)
                if (!LotTaken[i]) return i;
            return -1;
        }

        public bool LotFlooded => _sim.World.FloodLevel >= CityData.LotY - 0.02f;

        public bool LotUsable => !_sim.World.LotIsPark && !LotFlooded;

        // ------------------------------------------------------------------ update

        public void Update()
        {
            long tick = _sim.Tick;
            var vs = _sim.Vehicles;
            float dt = _sim.Dt;
            for (int i = 0; i < vs.Count; i++)
            {
                var v = vs[i];
                v.PX = v.X; v.PY = v.Y; v.PZ = v.Z; v.PHeading = v.Heading;
                switch (v.State)
                {
                    case VehicleState.Parked:
                        if (tick >= v.DwellUntil && tick >= v.NextRetry)
                        {
                            if (v.Kind == VehicleKind.Car || v.Dest != DestKind.None) TryDepart(v);
                        }
                        break;
                    case VehicleState.Idle:
                    case VehicleState.OnScene:
                        if (v.Dest != DestKind.None && tick >= v.NextRetry) TryDepart(v);
                        break;
                    case VehicleState.LotParked:
                        if (tick >= v.DwellUntil && !LotFlooded)
                        {
                            v.State = VehicleState.LotExit;
                            v.LotProgress = 0f;
                        }
                        break;
                    case VehicleState.LotEnter:
                        v.LotProgress += LotSpeed * dt;
                        if (UpdateLotPose(v, v.LotProgress) >= 1f)
                        {
                            v.State = VehicleState.LotParked;
                            v.DwellUntil = tick + DwellTicks(v);
                        }
                        break;
                    case VehicleState.LotExit:
                        v.LotProgress += LotSpeed * dt;
                        float total = LotPathLength(v);
                        UpdateLotPose(v, total - v.LotProgress);
                        if (v.LotProgress > 3f) v.Heading += 180f;
                        if (v.LotProgress >= total)
                        {
                            if (v.LotSlot >= 0) LotTaken[v.LotSlot] = false;
                            v.LotSlot = -1;
                            v.State = VehicleState.Parked;
                            v.Lane = _c.LotCurbLane;
                            v.S = _c.LotCurbS;
                            v.Dest = DestKind.None;
                            v.DwellUntil = tick;
                            SetParkedPose(v);
                        }
                        break;
                }
            }

            Arbitrate(tick);

            for (int i = 0; i < vs.Count; i++)
            {
                var v = vs[i];
                if (v.State == VehicleState.Driving) MoveDriving(v, dt);
                else if (v.State == VehicleState.Transit) MoveTransit(v, dt);
            }

            for (int l = 0; l < LaneCars.Length; l++) SortLaneList(LaneCars[l]);
        }

        private int DwellTicks(Vehicle v)
        {
            float hour = _sim.Hour;
            int tps = _sim.Config.TicksPerSecond;
            if ((hour >= 22.5f || hour < 5.5f) && DetRandom.Hash01(v.Id, _sim.Day, 3) < 0.8f)
            {
                float wake = 6f + DetRandom.Hash01(v.Id, _sim.Day, 4) * 1.5f;
                float hoursLeft = hour >= 22.5f ? (24f - hour + wake) : (wake - hour);
                return Math.Max(tps * 10, (int)(hoursLeft / 24f * _sim.TicksPerDay));
            }
            return (int)(_sim.Rng.Range(15f, 45f) * tps);
        }

        private float SpeedLimit(Vehicle v, int lane)
        {
            float s = _sim.Config.CarSpeed;
            switch (_sim.World.Weather)
            {
                case Weather.Rain: s *= 0.8f; break;
                case Weather.Storm: s *= 0.7f; break;
                case Weather.Snow: s *= 0.6f; break;
            }
            if (_sim.World.Season == Season.Winter && _sim.World.Weather != Weather.Snow) s *= 0.85f;
            if (lane >= 0 && _n.LBridge[lane] >= 0) s *= 0.9f;
            if (v.Siren) s *= 1.35f;
            if (v.Kind == VehicleKind.FireTruck) s *= 0.92f;
            return s;
        }

        /// <summary>Global speed factor versus clear summer weather (for consequence reports).</summary>
        public float WeatherSpeedFactor()
        {
            var probe = new Vehicle();
            return SpeedLimit(probe, -1) / _sim.Config.CarSpeed;
        }

        private int IndexInLane(Vehicle v)
        {
            var list = LaneCars[v.Lane];
            for (int i = 0; i < list.Count; i++)
                if (list[i] == v.Id) return i;
            return -1;
        }

        private Vehicle Leader(Vehicle v)
        {
            var list = LaneCars[v.Lane];
            int idx = IndexInLane(v);
            Vehicle best = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == idx) continue;
                var o = _sim.Vehicles[list[i]];
                if (o.S > v.S || (o.S == v.S && o.Id > v.Id))
                {
                    if (best == null || o.S < best.S) best = o;
                }
            }
            return best;
        }

        private static float Brake(float v, float limit, float vmax, float dt)
        {
            float nv = Math.Min(vmax, v + Accel * dt);
            if (limit <= 0.05f) return 0f;
            float allowed = MathF.Sqrt(2f * Decel * Math.Max(0f, limit - 0.05f));
            return Math.Min(nv, allowed);
        }

        private bool IsLast(Vehicle v) => v.RouteIdx >= v.Route.Count - 1;

        private void MoveDriving(Vehicle v, float dt)
        {
            if (v.PullOver)
            {
                PullOverNow(v);
                return;
            }
            float L = _n.LLen[v.Lane];
            float limit = float.MaxValue;
            var lead = Leader(v);
            if (lead != null) limit = lead.S - lead.HalfLen - (v.S + v.HalfLen) - MinGap;
            bool last = IsLast(v);
            if (last) limit = Math.Min(limit, v.DestS - v.S);
            else if (!v.Granted) limit = Math.Min(limit, L - StopOffset - v.HalfLen - v.S);
            v.V = Brake(v.V, limit, SpeedLimit(v, v.Lane), dt);
            v.S += v.V * dt;
            v.StoppedTicks = v.V < 0.2f ? v.StoppedTicks + 1 : 0;
            if (last && v.DestS - v.S <= 0.4f && v.V <= 1.5f)
            {
                Arrive(v);
                return;
            }
            if (!last && v.Granted && v.S >= L)
            {
                StartTransit(v);
                return;
            }
            SetLanePose(v);
        }

        private void MoveTransit(Vehicle v, float dt)
        {
            int t = v.Turn;
            float TL = _n.TLen[t];
            float limit = float.MaxValue;
            var lead = Leader(v);
            if (lead != null) limit = lead.S - lead.HalfLen - (v.S + v.HalfLen) - MinGap;
            int cw = _n.LCwStart[v.Lane];
            if (cw >= 0 && _sim.People.CrosswalkOcc[cw] > 0 && TL - v.T > 1.0f) limit = Math.Min(limit, TL - v.T - 1.0f);
            float vmax = SpeedLimit(v, v.Lane) * (_n.TKind[t] == TurnKind.Straight ? 0.85f : 0.5f);
            v.V = Brake(v.V, limit, vmax, dt);
            v.T += v.V * dt;
            v.S = v.T - TL;
            v.StoppedTicks = v.V < 0.2f ? v.StoppedTicks + 1 : 0;
            if (v.T >= TL)
            {
                ReleaseInter(v, v.TransitInter);
                v.TransitInter = -1;
                v.Turn = -1;
                v.State = VehicleState.Driving;
                SetLanePose(v);
                return;
            }
            float u = v.T / TL;
            Vec2 p = MathUtil.Bezier(_n.TP0[t], _n.TP1[t], _n.TP2[t], u);
            Vec2 d = MathUtil.BezierTangent(_n.TP0[t], _n.TP1[t], _n.TP2[t], u);
            v.X = p.X;
            v.Z = p.Z;
            v.Y = CityData.StreetY;
            v.Heading = MathUtil.HeadingDeg(d.X, d.Z);
        }

        private void StartTransit(Vehicle v)
        {
            int next = v.Route[v.RouteIdx + 1];
            int turn = _n.FindTurn(v.Lane, next);
            if (turn < 0)
            {
                v.S = Math.Min(v.S, _n.LLen[v.Lane] - 0.5f);
                v.V = 0f;
                Reroute(v);
                return;
            }
            float overflow = v.S - _n.LLen[v.Lane];
            LaneCars[v.Lane].Remove(v.Id);
            v.Lane = next;
            v.RouteIdx++;
            v.Turn = turn;
            v.T = overflow;
            v.S = v.T - _n.TLen[turn];
            LaneCars[next].Insert(0, v.Id);
            v.State = VehicleState.Transit;
            v.Granted = false;
            v.TransitInter = v.GrantInter;
            v.GrantInter = -1;
        }

        private void ReleaseInter(Vehicle v, int inter)
        {
            if (inter >= 0) InterUsers[inter].Remove(v.Id);
        }

        private void Arbitrate(long tick)
        {
            _stamp++;
            _cand.Clear();
            _reroute.Clear();
            var vs = _sim.Vehicles;
            for (int i = 0; i < vs.Count; i++)
            {
                var v = vs[i];
                if (v.State != VehicleState.Driving || v.Granted || v.PullOver || IsLast(v)) continue;
                float L = _n.LLen[v.Lane];
                float dist = L - StopOffset - v.HalfLen - v.S;
                float reqDist = Math.Max(2.0f, v.V * v.V / (2f * Decel) + 2.5f);
                if (dist > reqDist) { v.RequestTick = -1; continue; }
                if (Leader(v) != null) continue;
                if (v.RequestTick < 0) v.RequestTick = tick;
                int next = v.Route[v.RouteIdx + 1];
                if (_n.LaneBlocked[next]) { _reroute.Add(v.Id); continue; }
                int inter = _n.LTo[v.Lane];
                if (InterUsers[inter].Count > 0) continue;
                if (!CwFree(_n.LCwEnd[v.Lane]) || !CwFree(_n.LCwStart[next])) continue;
                if (!LaneStartSpace(next, v.HalfLen)) continue;
                if (_bestStamp[inter] != _stamp)
                {
                    _bestStamp[inter] = _stamp;
                    _best[inter] = v.Id;
                    _cand.Add(inter);
                }
                else if (Better(v, vs[_best[inter]])) _best[inter] = v.Id;
            }
            for (int k = 0; k < _cand.Count; k++)
            {
                int inter = _cand[k];
                var v = vs[_best[inter]];
                v.Granted = true;
                v.GrantInter = inter;
                v.RequestTick = -1;
                InterUsers[inter].Add(v.Id);
            }
            for (int k = 0; k < _reroute.Count; k++) Reroute(vs[_reroute[k]]);
        }

        private static bool Better(Vehicle a, Vehicle b)
        {
            if (a.Siren != b.Siren) return a.Siren;
            if (a.RequestTick != b.RequestTick) return a.RequestTick < b.RequestTick;
            return a.Id < b.Id;
        }

        private bool CwFree(int cw) => cw < 0 || _sim.People.CrosswalkOcc[cw] == 0;

        private bool LaneStartSpace(int lane, float halfLen)
        {
            var list = LaneCars[lane];
            float minS = float.MaxValue;
            float minHalf = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                var o = _sim.Vehicles[list[i]];
                if (o.S < minS) { minS = o.S; minHalf = o.HalfLen; }
            }
            if (list.Count == 0) return true;
            return minS - minHalf >= halfLen * 2f + MinGap + 0.5f;
        }

        private bool LaneHasSpaceAt(int lane, float s, float halfLen, int self)
        {
            var list = LaneCars[lane];
            for (int i = 0; i < list.Count; i++)
            {
                var o = _sim.Vehicles[list[i]];
                if (o.Id == self) continue;
                if (Math.Abs(o.S - s) < o.HalfLen + halfLen + 3.5f) return false;
                // a car approaching from behind must have room to brake
                if (o.S < s && s - o.S < o.HalfLen + halfLen + 3.5f + o.V * o.V / (2f * Decel)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ departures, arrivals, routing

        private void PickDestination(Vehicle v)
        {
            var rng = _sim.Rng;
            if (v.Kind != VehicleKind.Car) return;
            if (LotUsable && rng.Chance(0.12f))
            {
                int slot = FreeSlot();
                if (slot >= 0 && v.Lane != _c.LotCurbLane)
                {
                    v.Dest = DestKind.Lot;
                    v.DestLane = _c.LotCurbLane;
                    v.DestS = _c.LotCurbS;
                    v.DestBuilding = -1;
                    v.LotSlot = slot;
                    LotTaken[slot] = true;
                    return;
                }
            }
            float hour = _sim.Hour;
            List<int> pool = _anyDest;
            float roll = rng.NextFloat();
            if (hour >= 6f && hour < 10f && roll < 0.7f) pool = _workDest;
            else if (hour >= 16f && hour < 21f && roll < 0.6f) pool = _homeDest;
            else if ((hour >= 21f || hour < 6f) && roll < 0.75f) pool = _homeDest;
            int pick = pool[rng.Next(pool.Count)];
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (_c.Buildings[pick].CurbLane != v.Lane) break;
                pick = pool[rng.Next(pool.Count)];
            }
            var b = _c.Buildings[pick];
            v.Dest = DestKind.Building;
            v.DestBuilding = pick;
            v.DestLane = b.CurbLane;
            v.DestS = MathUtil.Clamp(b.CurbS + rng.Range(-4f, 3f), 4f, _n.LLen[b.CurbLane] - 6f);
        }

        private bool FindRoute(int fromLane, float fromS, int destLane, float destS, List<int> route)
        {
            route.Clear();
            if (fromLane < 0 || destLane < 0) return false;
            if (fromLane == destLane && destS >= fromS + 2f)
            {
                route.Add(fromLane);
                return true;
            }
            float best = float.MaxValue;
            for (int k = _n.LOutStart[fromLane]; k < _n.LOutStart[fromLane + 1]; k++)
            {
                int t = _n.LOutTurns[k];
                int next = _n.TTo[t];
                if (_n.LaneBlocked[next]) continue;
                if (!_sim.RoadPaths.Find(next, destLane, _tmp)) continue;
                float cost = _n.TLen[t];
                for (int i = 0; i < _tmp.Count; i++) cost += _n.LLen[_tmp[i]];
                if (_n.TKind[t] == TurnKind.Left) cost += 6f;
                if (cost < best)
                {
                    best = cost;
                    route.Clear();
                    route.Add(fromLane);
                    route.AddRange(_tmp);
                }
            }
            return best < float.MaxValue;
        }

        private void TryDepart(Vehicle v)
        {
            long tick = _sim.Tick;
            if (v.Dest == DestKind.None) PickDestination(v);
            if (v.Dest == DestKind.None) return;
            if (v.Dest == DestKind.Lot && !LotUsable)
            {
                ReleaseSlot(v);
                v.Dest = DestKind.None;
                PickDestination(v);
            }
            if (!FindRoute(v.Lane, v.S, v.DestLane, v.DestS, v.Route))
            {
                v.NoRoute = true;
                v.NextRetry = tick + 30;
                return;
            }
            v.NoRoute = false;
            if (!LaneHasSpaceAt(v.Lane, v.S, v.HalfLen, v.Id))
            {
                v.NextRetry = tick + 5;
                return;
            }
            v.State = VehicleState.Driving;
            v.V = 0f;
            v.RouteIdx = 0;
            v.PullOver = false;
            v.StoppedTicks = 0;
            LaneCars[v.Lane].Add(v.Id);
            SortLane(v.Lane);
        }

        private void ReleaseSlot(Vehicle v)
        {
            if (v.LotSlot >= 0) LotTaken[v.LotSlot] = false;
            v.LotSlot = -1;
        }

        private void Arrive(Vehicle v)
        {
            LaneCars[v.Lane].Remove(v.Id);
            v.V = 0f;
            v.StoppedTicks = 0;
            long tick = _sim.Tick;
            var dest = v.Dest;
            v.Dest = DestKind.None;
            switch (dest)
            {
                case DestKind.Lot:
                    if (!LotUsable || v.LotSlot < 0)
                    {
                        ReleaseSlot(v);
                        v.State = VehicleState.Parked;
                        v.DwellUntil = tick;
                        SetParkedPose(v);
                        return;
                    }
                    v.State = VehicleState.LotEnter;
                    v.LotProgress = 0f;
                    UpdateLotPose(v, 0f);
                    return;
                case DestKind.Scene:
                    v.State = VehicleState.OnScene;
                    SetParkedPose(v);
                    _sim.OnVehicleArrived(v);
                    return;
                case DestKind.Base:
                    v.State = VehicleState.Idle;
                    v.Siren = false;
                    v.DestBuilding = v.HomeBuilding;
                    SetParkedPose(v);
                    return;
                default:
                    v.State = VehicleState.Parked;
                    v.DwellUntil = tick + DwellTicks(v);
                    SetParkedPose(v);
                    return;
            }
        }

        private void PullOverNow(Vehicle v)
        {
            LaneCars[v.Lane].Remove(v.Id);
            ReleaseGrant(v);
            v.State = v.Kind == VehicleKind.Car ? VehicleState.Parked : (v.Dest == DestKind.None ? VehicleState.Idle : VehicleState.Parked);
            v.V = 0f;
            v.PullOver = false;
            v.NoRoute = true;
            v.DwellUntil = _sim.Tick;
            v.NextRetry = _sim.Tick + 30;
            v.S = MathUtil.Clamp(v.S, 2f, _n.LLen[v.Lane] - 2f);
            SetParkedPose(v);
        }

        private void ReleaseGrant(Vehicle v)
        {
            if (v.Granted && v.GrantInter >= 0) InterUsers[v.GrantInter].Remove(v.Id);
            v.Granted = false;
            v.GrantInter = -1;
        }

        /// <summary>Recomputes the route from the current position. Returns true if a route exists.</summary>
        public bool Reroute(Vehicle v)
        {
            if (v.State != VehicleState.Driving && v.State != VehicleState.Transit) return !v.NoRoute;
            ReleaseGrant(v);
            float s = Math.Max(0f, v.S);
            if (FindRoute(v.Lane, s, v.DestLane, v.DestS, _alt))
            {
                v.Route.Clear();
                v.Route.AddRange(_alt);
                v.RouteIdx = 0;
                v.NoRoute = false;
                v.PullOver = false;
                return true;
            }
            v.NoRoute = true;
            v.PullOver = v.State == VehicleState.Driving;
            if (v.State == VehicleState.Transit)
            {
                // finish the turn, then stop on the next lane
                v.Route.Clear();
                v.Route.Add(v.Lane);
                v.RouteIdx = 0;
                v.DestLane = v.Lane;
                v.DestS = Math.Min(_n.LLen[v.Lane] - 3f, 6f);
            }
            return false;
        }

        /// <summary>Reroutes every moving vehicle whose remaining route became blocked. Parked waiting cars retry soon.</summary>
        public void RerouteAll()
        {
            foreach (var v in _sim.Vehicles)
            {
                if (v.State == VehicleState.Driving || v.State == VehicleState.Transit)
                {
                    bool blocked = v.NoRoute;
                    for (int i = v.RouteIdx + 1; i < v.Route.Count && !blocked; i++)
                        if (_n.LaneBlocked[v.Route[i]]) blocked = true;
                    if (blocked || v.Kind != VehicleKind.Car) Reroute(v);
                    else ImproveRoute(v);
                }
                else if (v.NoRoute || v.State == VehicleState.Parked)
                {
                    if (v.NoRoute)
                    {
                        // Waiting cars re-evaluate immediately.
                        if (v.Dest != DestKind.None && FindRoute(v.Lane, v.S, v.DestLane, v.DestS, _alt))
                        {
                            v.NoRoute = false;
                            v.NextRetry = _sim.Tick;
                            v.DwellUntil = _sim.Tick;
                        }
                    }
                }
            }
        }

        /// <summary>After a bridge reopens, drivers take the shorter route if one appears.</summary>
        private void ImproveRoute(Vehicle v)
        {
            float s = Math.Max(0f, v.S);
            if (!FindRoute(v.Lane, s, v.DestLane, v.DestS, _alt)) return;
            float cur = 0f, alt = 0f;
            for (int i = v.RouteIdx; i < v.Route.Count; i++) cur += _n.LLen[v.Route[i]];
            for (int i = 0; i < _alt.Count; i++) alt += _n.LLen[_alt[i]];
            if (alt + 10f < cur)
            {
                ReleaseGrant(v);
                v.Route.Clear();
                v.Route.AddRange(_alt);
                v.RouteIdx = 0;
            }
        }

        // ------------------------------------------------------------------ emergency services

        public int Dispatch(VehicleKind kind, int building, out float routeLength, out int unreachable)
        {
            routeLength = 0f;
            unreachable = 0;
            var b = _c.Buildings[building];
            int n = 0;
            foreach (var v in _sim.Vehicles)
            {
                if (v.Kind != kind) continue;
                v.Dest = DestKind.Scene;
                v.DestBuilding = building;
                v.DestLane = b.CurbLane;
                v.DestS = MathUtil.Clamp(b.CurbS - n * 7f, 3f, _n.LLen[b.CurbLane] - 4f);
                v.Siren = true;
                v.NoRoute = false;
                v.NextRetry = _sim.Tick;
                v.DwellUntil = _sim.Tick;
                if (v.State == VehicleState.Driving || v.State == VehicleState.Transit) Reroute(v);
                else if (v.State == VehicleState.Idle || v.State == VehicleState.Parked || v.State == VehicleState.OnScene)
                {
                    if (v.State == VehicleState.OnScene) v.State = VehicleState.Parked;
                    if (!FindRoute(v.Lane, v.S, v.DestLane, v.DestS, _alt)) v.NoRoute = true;
                    else if (v.State == VehicleState.Idle || v.State == VehicleState.Parked) TryDepart(v);
                }
                if (v.NoRoute) unreachable++;
                else routeLength = Math.Max(routeLength, RemainingRouteLength(v));
                n++;
            }
            return n;
        }

        public float RemainingRouteLength(Vehicle v)
        {
            if (v.Route.Count == 0)
            {
                if (FindRoute(v.Lane, v.S, v.DestLane, v.DestS, _alt))
                {
                    float l = 0f;
                    for (int i = 0; i < _alt.Count; i++) l += _n.LLen[_alt[i]];
                    return l;
                }
                return 0f;
            }
            float len = -Math.Max(0f, v.S);
            for (int i = v.RouteIdx; i < v.Route.Count; i++) len += _n.LLen[v.Route[i]];
            len += v.DestS - _n.LLen[v.Route[v.Route.Count - 1]];
            return Math.Max(0f, len);
        }

        public void SendHome(VehicleKind kind)
        {
            foreach (var v in _sim.Vehicles)
            {
                if (v.Kind != kind) continue;
                var home = _c.Buildings[v.HomeBuilding];
                v.Siren = false;
                v.Dest = DestKind.Base;
                v.DestBuilding = v.HomeBuilding;
                v.DestLane = home.CurbLane;
                v.DestS = MathUtil.Clamp(home.CurbS + v.HomeOffset, 4f, _n.LLen[home.CurbLane] - 5f);
                v.NextRetry = _sim.Tick;
                v.DwellUntil = _sim.Tick;
                v.NoRoute = false;
                if (v.State == VehicleState.OnScene) v.State = VehicleState.Parked;
                if (v.State == VehicleState.Idle && v.Lane == v.DestLane && Math.Abs(v.S - v.DestS) < 1f) v.Dest = DestKind.None;
                if (v.State == VehicleState.Driving || v.State == VehicleState.Transit) Reroute(v);
            }
        }

        /// <summary>Cars parked in the riverside lot drive away (lot converted to a park). Returns how many leave.</summary>
        public int EvictLot()
        {
            int n = 0;
            foreach (var v in _sim.Vehicles)
            {
                if (v.State == VehicleState.LotParked)
                {
                    v.DwellUntil = _sim.Tick + n * 12;
                    n++;
                }
                else if (v.Dest == DestKind.Lot)
                {
                    ReleaseSlot(v);
                    v.Dest = DestKind.None;
                    PickDestination(v);
                    if (v.State == VehicleState.Driving || v.State == VehicleState.Transit) Reroute(v);
                }
            }
            return n;
        }

        public int CarsInLot()
        {
            int n = 0;
            foreach (var v in _sim.Vehicles)
                if (v.State == VehicleState.LotParked || v.State == VehicleState.LotEnter || v.State == VehicleState.LotExit) n++;
            return n;
        }

        // ------------------------------------------------------------------ poses

        private void SetLanePose(Vehicle v)
        {
            Vec2 p = _n.LanePoint(v.Lane, v.S);
            v.X = p.X;
            v.Z = p.Z;
            v.Y = CityData.StreetY;
            v.Heading = MathUtil.HeadingDeg(_n.LDir[v.Lane].X, _n.LDir[v.Lane].Z);
        }

        public void SetParkedPose(Vehicle v)
        {
            Vec2 p = _n.LanePoint(v.Lane, v.S) + _n.LDir[v.Lane].Right * ParkOffset;
            v.X = p.X;
            v.Z = p.Z;
            v.Y = CityData.StreetY;
            v.Heading = MathUtil.HeadingDeg(_n.LDir[v.Lane].X, _n.LDir[v.Lane].Z);
        }

        private void BuildLotPath(Vehicle v)
        {
            var slot = _c.LotSlots[Math.Max(0, v.LotSlot)];
            _lotPts[0] = _n.LanePoint(_c.LotCurbLane, _c.LotCurbS) + _n.LDir[_c.LotCurbLane].Right * ParkOffset;
            _lotPts[1] = new Vec2(_c.RampTopX, _c.RampTopZ);
            _lotPts[2] = new Vec2(_c.RampBottomX, _c.RampBottomZ);
            _lotPts[3] = new Vec2(_c.RampBottomX, slot.Z);
            _lotPts[4] = new Vec2(slot.X, slot.Z);
            _lotY[0] = CityData.StreetY;
            _lotY[1] = CityData.StreetY;
            _lotY[2] = CityData.LotY;
            _lotY[3] = CityData.LotY;
            _lotY[4] = CityData.LotY;
        }

        private float LotPathLength(Vehicle v)
        {
            BuildLotPath(v);
            float len = 0f;
            for (int i = 0; i < 4; i++) len += Vec2.Distance(_lotPts[i], _lotPts[i + 1]);
            return len;
        }

        /// <summary>Places the car at distance d along curb→slot path. Returns fraction [0,1].</summary>
        private float UpdateLotPose(Vehicle v, float d)
        {
            BuildLotPath(v);
            float total = 0f;
            for (int i = 0; i < 4; i++) total += Vec2.Distance(_lotPts[i], _lotPts[i + 1]);
            d = MathUtil.Clamp(d, 0f, total);
            float acc = 0f;
            for (int i = 0; i < 4; i++)
            {
                float seg = Vec2.Distance(_lotPts[i], _lotPts[i + 1]);
                if (d <= acc + seg || i == 3)
                {
                    float t = seg > 1e-4f ? MathUtil.Clamp01((d - acc) / seg) : 1f;
                    Vec2 p = Vec2.Lerp(_lotPts[i], _lotPts[i + 1], t);
                    v.X = p.X;
                    v.Z = p.Z;
                    v.Y = MathUtil.Lerp(_lotY[i], _lotY[i + 1], t);
                    Vec2 dir = _lotPts[i + 1] - _lotPts[i];
                    if (dir.SqrLength > 1e-4f) v.Heading = MathUtil.HeadingDeg(dir.X, dir.Z);
                    break;
                }
                acc += seg;
            }
            if (d >= total - 1e-3f && v.LotSlot >= 0) v.Heading = _c.LotSlots[v.LotSlot].Yaw;
            return total > 0f ? d / total : 1f;
        }

        // ------------------------------------------------------------------ derived state

        private void SortLane(int lane) => SortLaneList(LaneCars[lane]);

        private void SortLaneList(List<int> list)
        {
            var vs = _sim.Vehicles;
            for (int i = 1; i < list.Count; i++)
            {
                int id = list[i];
                var v = vs[id];
                int j = i - 1;
                while (j >= 0)
                {
                    var o = vs[list[j]];
                    if (o.S < v.S || (o.S == v.S && o.Id < v.Id)) break;
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = id;
            }
        }

        public void RebuildDerived()
        {
            foreach (var l in LaneCars) l.Clear();
            foreach (var u in InterUsers) u.Clear();
            Array.Clear(LotTaken, 0, LotTaken.Length);
            foreach (var v in _sim.Vehicles)
            {
                if (v.State == VehicleState.Driving || v.State == VehicleState.Transit) LaneCars[v.Lane].Add(v.Id);
                if (v.State == VehicleState.Transit && v.TransitInter >= 0) InterUsers[v.TransitInter].Add(v.Id);
                if (v.Granted && v.GrantInter >= 0) InterUsers[v.GrantInter].Add(v.Id);
                if (v.LotSlot >= 0 && v.LotSlot < LotTaken.Length) LotTaken[v.LotSlot] = true;
            }
            foreach (var l in LaneCars) SortLaneList(l);
            foreach (var u in InterUsers) u.Sort();
        }
    }
}
