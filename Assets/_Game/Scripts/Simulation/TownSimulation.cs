using System;
using System.Collections.Generic;
using System.IO;
using SmallTown.Generation;
using SmallTown.Simulation.Agents;
using SmallTown.Simulation.Pathfinding;
using SmallTown.Simulation.Traffic;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Simulation
{
    public sealed class SimStats
    {
        public int PeopleOutside;
        public int PeopleWalking;
        public int PeopleNoRoute;
        public int CarsOnRoad;
        public int CarsWaiting;
        public int CarsNoRoute;
        public int CarsParked;
        public int RoutesUnavailable;
        public int FestivalCrowd;
    }

    /// <summary>
    /// The whole town simulation: pure C#, fixed tick, deterministic (own RNG, no wall clock),
    /// serialisable into a byte snapshot. The view layer only reads it and sends commands.
    /// </summary>
    public sealed partial class TownSimulation
    {
        private const int SnapshotMagic = 0x314E5453; // "STN1"

        public readonly SimConfig Config;
        public readonly CityData City;
        public readonly DetRandom Rng;
        public readonly WorldState World = new WorldState();
        public readonly List<Resident> Residents = new List<Resident>();
        public readonly List<Vehicle> Vehicles = new List<Vehicle>();
        public readonly WalkPathfinder WalkPaths;
        public readonly RoadPathfinder RoadPaths;
        public readonly ResidentSystem People;
        public readonly TrafficSystem Traffic;
        public readonly SimStats Stats = new SimStats();
        /// <summary>Transient messages for the UI (not part of snapshots).</summary>
        public readonly List<string> Notifications = new List<string>();

        public long Tick;
        public long ClockTicks;
        public readonly float Dt;
        public readonly int TicksPerDay;

        public TownSimulation(SimConfig config, CityData city = null)
        {
            Config = config;
            City = city ?? CityGenerator.Generate(config);
            Dt = config.TickDt;
            TicksPerDay = Math.Max(240, config.TicksPerDay);
            Rng = new DetRandom((ulong)(uint)config.Seed * 0x9E3779B97F4A7C15UL + 0x5851F42D4C957F2DUL);
            ClockTicks = (long)(config.StartHour / 24f * TicksPerDay);
            City.Walk.UpdateBlocking(false, false, 0f);
            City.Roads.UpdateBlocking(false, false, false);
            WalkPaths = new WalkPathfinder(City.Walk);
            RoadPaths = new RoadPathfinder(City.Roads);
            People = new ResidentSystem(this);
            Traffic = new TrafficSystem(this);
            People.Populate(Rng, Residents, config.Residents);
            Traffic.Populate(Rng, Vehicles);
            UpdateStats();
        }

        public float Hour => (ClockTicks % TicksPerDay) * 24f / TicksPerDay;
        public int Day => (int)(ClockTicks / TicksPerDay);
        public float SimSeconds => Tick * Dt;

        public void Step()
        {
            Tick++;
            ClockTicks++;
            UpdateWorld();
            People.Update();
            Traffic.Update();
            UpdateStats();
        }

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        public void Notify(string message)
        {
            if (Notifications.Count > 20) Notifications.RemoveAt(0);
            Notifications.Add(message);
        }

        // ------------------------------------------------------------------ world dynamics

        private void UpdateWorld()
        {
            var w = World;
            float prev = w.RiverLevel;
            w.RiverLevel = MathUtil.MoveTowards(w.RiverLevel, w.RiverTarget, 0.3f * Dt);
            CheckFloodThreshold(prev, w.RiverLevel, CityData.LotY, "Вода затопила Парковку у реки", "Парковка у реки снова сухая");
            CheckFloodThreshold(prev, w.RiverLevel, CityData.TerraceY, "Набережные ушли под воду", "Набережные снова открыты");
            CheckFloodThreshold(prev, w.RiverLevel, CityData.BridgeFloodY, "Вода подступила к мостам — они закрыты", "Вода ушла от мостов");

            if (w.FireActive)
            {
                if (w.FireStage == FireStage.Burning) w.FireIntensity = Math.Min(1f, w.FireIntensity + Dt / 8f);
                else if (w.FireStage == FireStage.Fighting)
                {
                    w.FireIntensity -= Dt / 18f;
                    if (w.FireIntensity <= 0f)
                    {
                        string name = City.Buildings[w.FireBuilding].Name;
                        ExtinguishFire();
                        Notify("Пожар потушен: «" + name + "». Пожарные возвращаются в часть");
                        ReplanAll();
                    }
                }
            }

            if (w.RobberyActive)
            {
                int tps = Config.TicksPerSecond;
                if (w.RobberyStage == RobberyStage.InProgress && Tick - w.RobberySince > 120 * tps)
                {
                    EndRobbery();
                    Notify("Грабители скрылись до приезда полиции. В городе снова спокойно");
                    ReplanAll();
                }
                else if (w.RobberyStage == RobberyStage.PoliceOnScene && Tick >= w.RobberyResolveTick)
                {
                    EndRobbery();
                    Notify("Грабители задержаны — в городе снова спокойно");
                    ReplanAll();
                }
            }

            if (w.Festival && (Tick & 15) == 0)
            {
                int crowd = CountFestivalCrowd();
                if (crowd > w.FestivalPeak) w.FestivalPeak = crowd;
                if (!w.FestivalPeakReported && Tick - w.FestivalSince > 45 * Config.TicksPerSecond && crowd > 0)
                {
                    w.FestivalPeakReported = true;
                    Notify("На фестивале уже " + RuText.People(crowd));
                }
            }
        }

        private void CheckFloodThreshold(float prev, float cur, float level, string up, string down)
        {
            if (prev < level && cur >= level) Notify(up);
            else if (prev >= level && cur < level) Notify(down);
        }

        public int CountFestivalCrowd()
        {
            int n = 0;
            foreach (var r in Residents)
                if (r.Act == Activity.Festival && r.State == ResidentState.Standing) n++;
            return n;
        }

        internal void OnVehicleArrived(Vehicle v)
        {
            var w = World;
            if (v.DestBuilding < 0) return;
            var b = City.Buildings[v.DestBuilding];
            float dx = v.X - b.X, dz = v.Z - b.Z;
            if (dx * dx + dz * dz > 40f * 40f) return;
            if (v.Kind == VehicleKind.FireTruck && w.FireActive && v.DestBuilding == w.FireBuilding && w.FireStage == FireStage.Burning)
            {
                w.FireStage = FireStage.Fighting;
                Notify("Пожарные прибыли к «" + b.Name + "» и тушат огонь");
            }
            if (v.Kind == VehicleKind.Police && w.RobberyActive && v.DestBuilding == w.RobberyBuilding && w.RobberyStage == RobberyStage.InProgress)
            {
                w.RobberyStage = RobberyStage.PoliceOnScene;
                w.RobberyResolveTick = Tick + 15 * Config.TicksPerSecond;
                Notify("Полиция прибыла к банку");
            }
        }

        private void ExtinguishFire()
        {
            World.FireStage = FireStage.None;
            World.FireBuilding = -1;
            World.FireIntensity = 0f;
            Traffic.SendHome(VehicleKind.FireTruck);
        }

        private void EndRobbery()
        {
            World.RobberyStage = RobberyStage.None;
            World.RobberyBuilding = -1;
            Traffic.SendHome(VehicleKind.Police);
        }

        public void RefreshBlocking()
        {
            var w = World;
            City.Walk.UpdateBlocking(w.NorthClosed, w.SouthClosed, w.FloodLevel);
            City.Roads.UpdateBlocking(w.NorthClosed, w.SouthClosed, w.BridgesFlooded);
        }

        /// <summary>Every agent re-evaluates its plan and route against the current world.</summary>
        public void ReplanAll()
        {
            for (int i = 0; i < Residents.Count; i++) People.Think(Residents[i], true);
            Traffic.RerouteAll();
        }

        public void UpdateStats()
        {
            var s = Stats;
            s.PeopleOutside = s.PeopleWalking = s.PeopleNoRoute = 0;
            s.CarsOnRoad = s.CarsWaiting = s.CarsNoRoute = s.CarsParked = 0;
            s.FestivalCrowd = 0;
            for (int i = 0; i < Residents.Count; i++)
            {
                var r = Residents[i];
                if (r.IsOutside) s.PeopleOutside++;
                if (r.State == ResidentState.Walking) s.PeopleWalking++;
                if (r.NoRoute) s.PeopleNoRoute++;
                if (r.Act == Activity.Festival && r.State == ResidentState.Standing) s.FestivalCrowd++;
            }
            for (int i = 0; i < Vehicles.Count; i++)
            {
                var v = Vehicles[i];
                if (v.OnRoad) s.CarsOnRoad++;
                else s.CarsParked++;
                if (v.NoRoute) s.CarsNoRoute++;
                if (v.NoRoute || (v.OnRoad && v.StoppedTicks > 25)) s.CarsWaiting++;
            }
            s.RoutesUnavailable = s.PeopleNoRoute + s.CarsNoRoute;
        }

        // ------------------------------------------------------------------ snapshots

        public byte[] SaveSnapshot()
        {
            using (var ms = new MemoryStream(32768))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(SnapshotMagic);
                w.Write(Config.Seed);
                w.Write(Tick);
                w.Write(ClockTicks);
                w.Write(Rng.State0);
                w.Write(Rng.State1);
                World.Write(w);
                w.Write(Residents.Count);
                for (int i = 0; i < Residents.Count; i++) Residents[i].Write(w);
                w.Write(Vehicles.Count);
                for (int i = 0; i < Vehicles.Count; i++) Vehicles[i].Write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public void LoadSnapshot(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadInt32() != SnapshotMagic) throw new InvalidDataException("Not a Small Town snapshot");
                if (r.ReadInt32() != Config.Seed) throw new InvalidDataException("Snapshot belongs to another seed");
                Tick = r.ReadInt64();
                ClockTicks = r.ReadInt64();
                ulong s0 = r.ReadUInt64(), s1 = r.ReadUInt64();
                Rng.SetState(s0, s1);
                World.Read(r);
                int rc = r.ReadInt32();
                if (rc != Residents.Count) throw new InvalidDataException("Resident count mismatch");
                for (int i = 0; i < rc; i++) Residents[i].Read(r);
                int vc = r.ReadInt32();
                if (vc != Vehicles.Count) throw new InvalidDataException("Vehicle count mismatch");
                for (int i = 0; i < vc; i++) Vehicles[i].Read(r);
            }
            RefreshBlocking();
            WalkPaths.ClearCache();
            People.RebuildDerived();
            Traffic.RebuildDerived();
            Notifications.Clear();
            UpdateStats();
        }
    }
}
