using System.Collections.Generic;
using NUnit.Framework;
using SmallTown.Commands;
using SmallTown.Generation;
using SmallTown.Simulation;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.World;

namespace SmallTown.Tests
{
    public sealed class DeterminismTests
    {
        private static void RunScript(TownSimulation sim)
        {
            for (int t = 0; t < 2400; t++)
            {
                if (t == 100) sim.Apply(new WorldCommand(CommandKind.CloseBridge, 0));
                if (t == 400) sim.Apply(new WorldCommand(CommandKind.SetWeather, (int)Weather.Rain));
                if (t == 700) sim.Apply(new WorldCommand(CommandKind.RiverDelta, 0, 1f));
                if (t == 1000) sim.Apply(new WorldCommand(CommandKind.StartFire, sim.City.SchoolBuilding));
                if (t == 1300) sim.Apply(new WorldCommand(CommandKind.StartFestival));
                if (t == 1600) sim.Apply(new WorldCommand(CommandKind.Robbery, sim.City.BankBuilding));
                if (t == 1900) sim.Apply(new WorldCommand(CommandKind.SetTime, 0, 23f));
                sim.Step();
            }
        }

        [Test]
        public void SameSeedAndCommands_GiveIdenticalState()
        {
            var a = new TownSimulation(TestUtil.Config());
            var b = new TownSimulation(TestUtil.Config());
            RunScript(a);
            RunScript(b);
            CollectionAssert.AreEqual(a.SaveSnapshot(), b.SaveSnapshot());
            Assert.AreEqual(2400, a.Tick);
        }

        [Test]
        public void DifferentSeeds_GiveDifferentTowns()
        {
            var a = CityGenerator.Generate(TestUtil.Config(1));
            var b = CityGenerator.Generate(TestUtil.Config(2));
            bool differ = a.Buildings.Count != b.Buildings.Count;
            for (int i = 0; !differ && i < a.Buildings.Count; i++)
                if (a.Buildings[i].X != b.Buildings[i].X || a.Buildings[i].Type != b.Buildings[i].Type) differ = true;
            Assert.IsTrue(differ);
        }

        [Test]
        public void Generation_IsDeterministic()
        {
            var a = CityGenerator.Generate(TestUtil.Config(77));
            var b = CityGenerator.Generate(TestUtil.Config(77));
            Assert.AreEqual(a.Buildings.Count, b.Buildings.Count);
            Assert.AreEqual(a.Walk.NodeCount, b.Walk.NodeCount);
            for (int i = 0; i < a.Buildings.Count; i++)
            {
                Assert.AreEqual(a.Buildings[i].X, b.Buildings[i].X);
                Assert.AreEqual(a.Buildings[i].Style, b.Buildings[i].Style);
            }
        }

        [Test]
        public void Agents_MoveAndCarsDrive()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Run(600);
            Assert.Greater(sim.Stats.PeopleOutside, 10, "people should be out in the morning");
            Assert.Greater(sim.Stats.CarsOnRoad, 3, "cars should be driving");
            int moved = 0;
            foreach (var v in sim.Vehicles)
                if (v.State != Simulation.Traffic.VehicleState.Idle) moved++;
            Assert.Greater(moved, 10);
        }
    }

    public sealed class CityGenerationTests
    {
        [Test]
        public void ExactlyTwoBridges()
        {
            var c = TestUtil.City();
            var ids = new HashSet<int>();
            int count = 0;
            foreach (var s in c.Roads.Segments)
                if (s.Bridge >= 0) { count++; ids.Add(s.Bridge); }
            Assert.AreEqual(2, count);
            Assert.AreEqual(2, ids.Count);
            Assert.NotNull(c.PlaceByKey("north_bridge"));
            Assert.NotNull(c.PlaceByKey("south_bridge"));
            Assert.Greater(c.BridgeZ(CityData.NorthBridge), c.BridgeZ(CityData.SouthBridge));
        }

        [Test]
        public void NamedPlacesExist()
        {
            var c = TestUtil.City();
            foreach (var key in new[] { "central_park", "market_square", "riverside_parking", "river", "school", "bank", "fire_station", "police", "church", "lighthouse", "water_tower" })
                Assert.NotNull(c.PlaceByKey(key), key);
            Assert.Greater(c.ParkSpotNodes.Count, 3);
            Assert.Greater(c.RiversideNodes.Count, 1);
            Assert.Greater(c.LotSlots.Count, 4);
        }

        [Test]
        public void AllBuildingTypesPresent()
        {
            var c = TestUtil.City();
            var types = new HashSet<BuildingType>();
            foreach (var b in c.Buildings) types.Add(b.Type);
            foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
                Assert.IsTrue(types.Contains(t), t.ToString());
            Assert.Greater(c.Buildings.Count, 60);
        }

        [Test]
        public void AllBuildingsReachableOnFoot()
        {
            var sim = new TownSimulation(TestUtil.Config());
            var c = sim.City;
            var reach = TestUtil.Reachable(c.Walk, c.Buildings[0].EntranceNode);
            foreach (var b in c.Buildings)
                Assert.IsTrue(reach.Contains(b.EntranceNode), "unreachable: " + b.Name);
        }

        [Test]
        public void RoadGraphIsStronglyConnected()
        {
            var sim = new TownSimulation(TestUtil.Config());
            var n = sim.City.Roads;
            Assert.AreEqual(n.LaneCount, TestUtil.ReachableLanes(n, 0, false).Count);
            Assert.AreEqual(n.LaneCount, TestUtil.ReachableLanes(n, 0, true).Count);
        }

        [Test]
        public void EveryDestinationHasACurb()
        {
            var c = TestUtil.City();
            foreach (var b in c.Buildings)
            {
                if (b.Type == BuildingType.Lighthouse) continue;
                Assert.GreaterOrEqual(b.CurbLane, 0, b.Name);
                Assert.That(b.CurbS, Is.InRange(0f, c.Roads.LLen[b.CurbLane]));
            }
        }
    }

    public sealed class ConnectivityTests
    {
        [Test]
        public void ClosingOneBridge_KeepsCityConnected()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Apply(new WorldCommand(CommandKind.CloseBridge, CityData.NorthBridge));
            var c = sim.City;
            var reach = TestUtil.Reachable(c.Walk, c.Buildings[0].EntranceNode);
            foreach (var b in c.Buildings) Assert.IsTrue(reach.Contains(b.EntranceNode), b.Name);
            var n = c.Roads;
            int first = -1;
            for (int l = 0; l < n.LaneCount; l++) if (!n.LaneBlocked[l]) { first = l; break; }
            int open = 0;
            for (int l = 0; l < n.LaneCount; l++) if (!n.LaneBlocked[l]) open++;
            Assert.AreEqual(open, TestUtil.ReachableLanes(n, first, false).Count);
            Assert.AreEqual(open, TestUtil.ReachableLanes(n, first, true).Count);
        }

        [Test]
        public void ClosingBothBridges_SplitsCity_AndRoutesBecomeUnavailable()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Run(200);
            var outcome = sim.Apply(new WorldCommand(CommandKind.CloseBridge, 2));
            Assert.IsTrue(outcome.Changed);
            var c = sim.City;
            var reach = TestUtil.Reachable(c.Walk, c.Buildings[0].EntranceNode);
            int unreachable = 0;
            foreach (var b in c.Buildings) if (!reach.Contains(b.EntranceNode)) unreachable++;
            Assert.Greater(unreachable, 0, "the river must split the town");
            sim.Run(300);
            Assert.Greater(sim.Stats.RoutesUnavailable, 0);
        }

        [Test]
        public void ClosingBridge_ReportsRealReroutes()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Run(900);
            var o = sim.Apply(new WorldCommand(CommandKind.CloseBridge, CityData.NorthBridge));
            Assert.AreEqual("Северный мост закрыт!", o.Title);
            Assert.GreaterOrEqual(o.Lines.Count, 1);
            Assert.IsTrue(o.Lines[o.Lines.Count - 1].Contains("на улице"));
            // cars never plan through the closed bridge
            foreach (var v in sim.Vehicles)
            {
                if (!v.OnRoad) continue;
                for (int i = v.RouteIdx + 1; i < v.Route.Count; i++)
                    Assert.AreNotEqual(CityData.NorthBridge, sim.City.Roads.LBridge[v.Route[i]]);
            }
        }
    }

    public sealed class FloodTests
    {
        [Test]
        public void RaisingRiverOneMetre_FloodsRiversideParking()
        {
            var sim = new TownSimulation(TestUtil.Config());
            var o = sim.Apply(new WorldCommand(CommandKind.RiverDelta, 0, 1f));
            var g = sim.City.Walk;
            foreach (int n in sim.City.RiversideNodes) Assert.IsTrue(g.NodeFlooded[n]);
            foreach (int n in sim.City.PromenadeNodes) Assert.IsTrue(g.NodeFlooded[n]);
            Assert.IsTrue(sim.Traffic.LotFlooded);
            Assert.IsFalse(sim.World.BridgesFlooded);
            int entrance = sim.City.Buildings[0].EntranceNode;
            Assert.IsNull(sim.WalkPaths.Find(entrance, sim.City.RiversideNodes[0]));
            StringAssert.Contains("Затоплено", string.Join("|", o.Lines));
            sim.Run(100);
            Assert.AreEqual(1f, sim.World.RiverLevel, 0.001f);
        }

        [Test]
        public void HalfMetre_FloodsParkingButNotPromenade()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Apply(new WorldCommand(CommandKind.RiverDelta, 0, 0.5f));
            var g = sim.City.Walk;
            foreach (int n in sim.City.RiversideNodes) Assert.IsTrue(g.NodeFlooded[n]);
            foreach (int n in sim.City.PromenadeNodes) Assert.IsFalse(g.NodeFlooded[n]);
        }

        [Test]
        public void HighWater_ClosesBridges_AndNormalReopens()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Apply(new WorldCommand(CommandKind.RiverSet, 0, 1.8f));
            Assert.IsTrue(sim.World.BridgesFlooded);
            foreach (var s in sim.City.Roads.Segments)
                if (s.Bridge >= 0) Assert.IsTrue(sim.City.Roads.LaneBlocked[s.LaneAB]);
            sim.Apply(new WorldCommand(CommandKind.RiverNormal));
            foreach (var s in sim.City.Roads.Segments)
                if (s.Bridge >= 0) Assert.IsFalse(sim.City.Roads.LaneBlocked[s.LaneAB]);
            foreach (int n in sim.City.RiversideNodes) Assert.IsFalse(sim.City.Walk.NodeFlooded[n]);
        }

        [Test]
        public void RiverLevelIsClamped()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Apply(new WorldCommand(CommandKind.RiverDelta, 0, 10f));
            Assert.AreEqual(CityData.MaxRiverLevel, sim.World.RiverTarget, 0.001f);
        }
    }

    public sealed class UndoTests
    {
        [Test]
        public void Undo_RestoresExactSnapshot_IncludingTimeAndRng()
        {
            var s = new TownSession(TestUtil.Config(), TestUtil.Grammar());
            s.Sim.Run(150);
            var before = s.Sim.SaveSnapshot();
            var r = s.Execute("закрой северный мост");
            Assert.IsTrue(r.WorldChanged);
            s.Sim.Run(250);
            Assert.IsTrue(s.Undo());
            CollectionAssert.AreEqual(before, s.Sim.SaveSnapshot());
            Assert.IsFalse(s.Sim.World.NorthClosed);
        }

        [Test]
        public void Undo_ThenContinue_MatchesRunWithoutTheCommand()
        {
            var a = new TownSession(TestUtil.Config(), TestUtil.Grammar());
            var b = new TownSimulation(TestUtil.Config());
            a.Sim.Run(120);
            b.Run(120);
            a.Execute("подними реку на 1 метр и пусть пойдёт дождь");
            a.Sim.Run(80);
            a.Undo();
            a.Sim.Run(400);
            b.Run(400);
            CollectionAssert.AreEqual(b.SaveSnapshot(), a.Sim.SaveSnapshot());
        }

        [Test]
        public void SeveralUndos_WalkBackInOrder()
        {
            var s = new TownSession(TestUtil.Config(), TestUtil.Grammar());
            var s0 = s.Sim.SaveSnapshot();
            s.Execute("сделай ночь");
            var s1 = s.Sim.SaveSnapshot();
            s.Execute("наступает зима");
            s.Execute("начни фестиваль");
            Assert.AreEqual(3, s.UndoDepth);
            s.Undo();
            s.Undo();
            CollectionAssert.AreEqual(s1, s.Sim.SaveSnapshot());
            s.Undo();
            CollectionAssert.AreEqual(s0, s.Sim.SaveSnapshot());
            Assert.IsFalse(s.Undo());
        }

        [Test]
        public void UnchangedCommand_DoesNotCreateUndoPoint()
        {
            var s = new TownSession(TestUtil.Config(), TestUtil.Grammar());
            s.Execute("открой оба моста");
            Assert.AreEqual(0, s.UndoDepth);
        }

        [Test]
        public void ResetWorld_StartsOver()
        {
            var s = new TownSession(TestUtil.Config(), TestUtil.Grammar());
            var fresh = s.Sim.SaveSnapshot();
            s.Execute("закрой оба моста");
            s.Sim.Run(100);
            s.ResetWorld();
            CollectionAssert.AreEqual(fresh, s.Sim.SaveSnapshot());
            Assert.AreEqual(0, s.UndoDepth);
        }
    }

    public sealed class EventTests
    {
        [Test]
        public void SchoolFire_EvacuatesAndTruckExtinguishes()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Run(900); // students are at school around 09:30
            var o = sim.Apply(new WorldCommand(CommandKind.StartFire, sim.City.SchoolBuilding));
            Assert.AreEqual("Пожар в школе!", o.Title);
            Assert.IsTrue(sim.World.FireActive);
            int inside = 0;
            foreach (var r in sim.Residents)
                if (r.State == Simulation.Agents.ResidentState.Inside && r.InsideBuilding == sim.City.SchoolBuilding) inside++;
            Assert.AreEqual(0, inside, "nobody may stay in the burning school");
            for (int i = 0; i < 6000 && sim.World.FireActive; i++) sim.Step();
            Assert.IsFalse(sim.World.FireActive, "the fire truck should arrive and put the fire out");
        }

        [Test]
        public void Robbery_PoliceArriveAndCalmReturns()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Run(300);
            sim.Apply(new WorldCommand(CommandKind.Robbery, sim.City.BankBuilding));
            Assert.IsTrue(sim.World.RobberyActive);
            for (int i = 0; i < 6000 && sim.World.RobberyActive; i++) sim.Step();
            Assert.IsFalse(sim.World.RobberyActive);
        }

        [Test]
        public void Festival_DrawsACrowd()
        {
            var sim = new TownSimulation(TestUtil.Config());
            sim.Run(200);
            var o = sim.Apply(new WorldCommand(CommandKind.StartFestival));
            Assert.IsTrue(o.Changed);
            sim.Run(1500);
            Assert.Greater(sim.CountFestivalCrowd(), 5);
        }

        [Test]
        public void LotToPark_SendsCarsAway()
        {
            var sim = new TownSimulation(TestUtil.Config());
            int before = sim.Traffic.CarsInLot();
            Assert.Greater(before, 0);
            sim.Apply(new WorldCommand(CommandKind.LotToPark));
            sim.Run(1200);
            Assert.AreEqual(0, sim.Traffic.CarsInLot());
        }

        [Test]
        public void Rain_SlowsCars()
        {
            var sim = new TownSimulation(TestUtil.Config());
            float dry = sim.Traffic.WeatherSpeedFactor();
            sim.Apply(new WorldCommand(CommandKind.SetWeather, (int)Weather.Rain));
            Assert.Less(sim.Traffic.WeatherSpeedFactor(), dry);
        }
    }
}
