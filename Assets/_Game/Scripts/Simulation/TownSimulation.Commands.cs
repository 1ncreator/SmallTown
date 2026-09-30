using System;
using System.Collections.Generic;
using SmallTown.Simulation.Agents;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.Traffic;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Simulation
{
    public sealed partial class TownSimulation
    {
        private Activity[] _preAct;
        private int[] _preBuilding, _preNode;
        private int[][] _prePath;
        private bool[] _preNoRoute;
        private int[] _preVehRoute;
        private bool[] _preVehNoRoute;

        private struct Diff
        {
            public int PeopleChanged, PeopleRerouted, PeopleNoRouteNew;
            public int ToHome, ToWork, ToFestival, FromFestival, ToOutdoor, FromOutdoor, Evacuated, Fleeing, ToRiverPark, FromRiverPark;
            public int VehRerouted, VehNoRouteNew, VehRecovered, VehViaNorth, VehViaSouth;
        }

        /// <summary>
        /// Applies a world change and reports its consequences using real numbers
        /// measured on the agents before and after the change.
        /// </summary>
        public CommandOutcome Apply(WorldCommand cmd)
        {
            var o = new CommandOutcome();
            Capture();
            switch (cmd.Kind)
            {
                case CommandKind.CloseBridge:
                case CommandKind.OpenBridge: ApplyBridge(cmd, o); break;
                case CommandKind.SetWeather: ApplyWeather(cmd, o); break;
                case CommandKind.RiverDelta:
                case CommandKind.RiverSet:
                case CommandKind.RiverNormal: ApplyRiver(cmd, o); break;
                case CommandKind.SetTime: ApplyTime(cmd, o); break;
                case CommandKind.SetSeason: ApplySeason(cmd, o); break;
                case CommandKind.StartFestival:
                case CommandKind.EndFestival: ApplyFestival(cmd, o); break;
                case CommandKind.LotToPark:
                case CommandKind.ParkToLot: ApplyLot(cmd, o); break;
                case CommandKind.Robbery: ApplyRobbery(cmd, o); break;
                case CommandKind.StartFire: ApplyFire(cmd, o); break;
                case CommandKind.Extinguish: ApplyExtinguish(o); break;
                default:
                    o.Success = false;
                    o.Changed = false;
                    o.Title = "Неизвестное действие";
                    break;
            }
            UpdateStats();
            o.RoutesUnavailable = Stats.RoutesUnavailable;
            if (o.Success) Finish(o);
            return o;
        }

        public string StatusLine() => RuText.People(Stats.PeopleOutside) + " на улице, " + RuText.Cars(Stats.CarsOnRoad) + " на дорогах";

        private void Finish(CommandOutcome o)
        {
            while (o.Lines.Count > 2) o.Lines.RemoveAt(o.Lines.Count - 1);
            o.Lines.Add(StatusLine());
        }

        private void Capture()
        {
            int n = Residents.Count;
            if (_preAct == null || _preAct.Length != n)
            {
                _preAct = new Activity[n];
                _preBuilding = new int[n];
                _preNode = new int[n];
                _prePath = new int[n][];
                _preNoRoute = new bool[n];
            }
            for (int i = 0; i < n; i++)
            {
                var r = Residents[i];
                _preAct[i] = r.Act;
                _preBuilding[i] = r.TargetBuilding;
                _preNode[i] = r.TargetNode;
                _prePath[i] = r.Path;
                _preNoRoute[i] = r.NoRoute;
            }
            int m = Vehicles.Count;
            if (_preVehRoute == null || _preVehRoute.Length != m)
            {
                _preVehRoute = new int[m];
                _preVehNoRoute = new bool[m];
            }
            for (int i = 0; i < m; i++)
            {
                _preVehRoute[i] = RouteHash(Vehicles[i]);
                _preVehNoRoute[i] = Vehicles[i].NoRoute;
            }
        }

        private static int RouteHash(Vehicle v)
        {
            unchecked
            {
                int h = 17;
                for (int i = v.RouteIdx; i < v.Route.Count; i++) h = h * 31 + v.Route[i];
                return h;
            }
        }

        private bool UsesBridge(Vehicle v, int bridge)
        {
            for (int i = v.RouteIdx; i < v.Route.Count; i++)
                if (City.Roads.LBridge[v.Route[i]] == bridge) return true;
            return false;
        }

        private Diff ComputeDiff()
        {
            var d = new Diff();
            for (int i = 0; i < Residents.Count; i++)
            {
                var r = Residents[i];
                bool changed = r.Act != _preAct[i] || r.TargetBuilding != _preBuilding[i] || r.TargetNode != _preNode[i];
                if (changed)
                {
                    d.PeopleChanged++;
                    if (r.Act == Activity.Home) d.ToHome++;
                    if (r.Act == Activity.Work || r.Act == Activity.School) d.ToWork++;
                    if (r.Act == Activity.Festival) d.ToFestival++;
                    if (_preAct[i] == Activity.Festival) d.FromFestival++;
                    if (r.Act == Activity.Evacuate) d.Evacuated++;
                    if (r.Act == Activity.Flee) d.Fleeing++;
                    if (r.Act == Activity.RiversidePark) d.ToRiverPark++;
                    if (_preAct[i] == Activity.RiversidePark) d.FromRiverPark++;
                    bool wasOut = ResidentSystem.IsOutdoorLeisure(_preAct[i]);
                    bool isOut = ResidentSystem.IsOutdoorLeisure(r.Act);
                    if (wasOut && !isOut) d.FromOutdoor++;
                    if (!wasOut && isOut) d.ToOutdoor++;
                }
                else if (r.Path != null && _prePath[i] != null && !ReferenceEquals(r.Path, _prePath[i]) && r.State == ResidentState.Walking)
                {
                    d.PeopleRerouted++;
                }
                if (r.NoRoute && !_preNoRoute[i]) d.PeopleNoRouteNew++;
            }
            for (int i = 0; i < Vehicles.Count; i++)
            {
                var v = Vehicles[i];
                bool moving = v.OnRoad;
                if (v.NoRoute && !_preVehNoRoute[i]) d.VehNoRouteNew++;
                else if (!v.NoRoute && _preVehNoRoute[i]) d.VehRecovered++;
                else if (moving && !v.NoRoute && RouteHash(v) != _preVehRoute[i])
                {
                    d.VehRerouted++;
                    if (UsesBridge(v, CityData.NorthBridge)) d.VehViaNorth++;
                    if (UsesBridge(v, CityData.SouthBridge)) d.VehViaSouth++;
                }
            }
            return d;
        }

        private void Unchanged(CommandOutcome o, string title)
        {
            o.Changed = false;
            o.Title = title;
        }

        // ------------------------------------------------------------------ bridges

        private void ApplyBridge(WorldCommand cmd, CommandOutcome o)
        {
            bool close = cmd.Kind == CommandKind.CloseBridge;
            int which = MathUtil.Clamp(cmd.IntArg, 0, 2);
            bool n = which == 0 || which == 2, s = which == 1 || which == 2;
            o.Entity = new EntityRef(EntityKind.Place, which == 1 ? City.PlaceSouthBridge : City.PlaceNorthBridge);
            string name = which == 2 ? "Оба моста" : (which == 0 ? "Северный мост" : "Южный мост");
            bool changed = (n && World.NorthClosed != close) || (s && World.SouthClosed != close);
            if (!changed)
            {
                Unchanged(o, close
                    ? (which == 2 ? "Оба моста уже закрыты" : name + " уже закрыт")
                    : (which == 2 ? "Оба моста уже открыты" : name + " уже открыт"));
                return;
            }
            if (n) World.NorthClosed = close;
            if (s) World.SouthClosed = close;
            RefreshBlocking();
            ReplanAll();
            var d = ComputeDiff();
            o.PeopleRerouted = d.PeopleRerouted + d.PeopleChanged;
            o.VehiclesRerouted = d.VehRerouted;
            o.VehiclesWaiting = d.VehNoRouteNew;
            if (close)
            {
                o.Title = which == 2 ? "Оба моста закрыты!" : name + " закрыт!";
                int people = d.PeopleRerouted + d.PeopleNoRouteNew;
                int cars = d.VehRerouted + d.VehNoRouteNew;
                if (people > 0 || cars > 0)
                    o.Lines.Add(RuText.People(people) + " и " + RuText.Cars(cars) + " меняют маршрут");
                if (which != 2 && d.VehRerouted > 0)
                {
                    string other = which == 0 ? "Южный" : "Северный";
                    o.Lines.Add("Машины поехали через " + other + " мост — " + RuText.Count(d.VehRerouted, "машина перенаправлена", "машины перенаправлены", "машин перенаправлено"));
                }
                if (d.VehNoRouteNew > 0 || World.NorthClosed && World.SouthClosed)
                {
                    int waiting = Stats.CarsNoRoute;
                    if (waiting > 0) o.Lines.Add(RuText.Count(waiting, "машина ждёт", "машины ждут", "машин ждут") + " — объезда нет");
                }
                if (d.PeopleNoRouteNew > 0)
                    o.Lines.Add(RuText.Count(d.PeopleNoRouteNew, "житель не может", "жителя не могут", "жителей не могут") + " попасть на другой берег");
            }
            else
            {
                o.Title = which == 2 ? "Оба моста снова открыты" : name + " снова открыт";
                if (d.VehRecovered > 0) o.Lines.Add(RuText.Count(d.VehRecovered, "машина снова в пути", "машины снова в пути", "машин снова в пути"));
                if (d.VehRerouted > 0) o.Lines.Add(RuText.Count(d.VehRerouted, "машина выбрала", "машины выбрали", "машин выбрали") + " путь покороче");
                int people = d.PeopleRerouted + d.PeopleChanged;
                if (people > 0) o.Lines.Add(RuText.Count(people, "человек меняет", "человека меняют", "человек меняют") + " маршрут");
            }
        }

        // ------------------------------------------------------------------ weather

        public static string WeatherName(Weather w)
        {
            switch (w)
            {
                case Weather.Rain: return "дождь";
                case Weather.Storm: return "гроза";
                case Weather.Snow: return "снег";
                default: return "ясно";
            }
        }

        private void ApplyWeather(WorldCommand cmd, CommandOutcome o)
        {
            var wNew = (Weather)MathUtil.Clamp(cmd.IntArg, 0, 3);
            if (World.Weather == wNew)
            {
                Unchanged(o, wNew == Weather.Clear ? "И так ясно" : "Уже идёт " + WeatherName(wNew));
                return;
            }
            World.Weather = wNew;
            ReplanAll();
            var d = ComputeDiff();
            o.PeopleChangedPlans = d.PeopleChanged;
            int pct = (int)Math.Round((1f - Traffic.WeatherSpeedFactor()) * 100f);
            switch (wNew)
            {
                case Weather.Rain: o.Title = "Пошёл дождь"; break;
                case Weather.Storm: o.Title = "Гроза!"; break;
                case Weather.Snow: o.Title = "Пошёл снег"; break;
                default: o.Title = "Небо прояснилось"; break;
            }
            if (wNew == Weather.Clear)
            {
                if (d.ToOutdoor > 0) o.Lines.Add(RuText.Count(d.ToOutdoor, "человек выходит", "человека выходят", "человек выходят") + " на прогулку");
                o.Lines.Add(pct > 0 ? "Машины всё ещё осторожнее на " + pct + "%" : "Машины снова едут с обычной скоростью");
                return;
            }
            int hiding = d.FromOutdoor + d.ToHome;
            if (hiding > 0) o.Lines.Add(RuText.Count(hiding, "человек спешит", "человека спешат", "человек спешат") + " под крышу");
            if (wNew != Weather.Snow)
            {
                int umbrellas = 0;
                foreach (var r in Residents)
                    if (r.IsOutside && r.Umbrella) umbrellas++;
                if (umbrellas > 0 && hiding == 0) o.Lines.Add(RuText.Count(umbrellas, "человек раскрыл", "человека раскрыли", "человек раскрыли") + " зонты");
            }
            o.Lines.Add("Машины сбавили скорость на " + pct + "%");
        }

        // ------------------------------------------------------------------ river

        private void ApplyRiver(WorldCommand cmd, CommandOutcome o)
        {
            o.Entity = new EntityRef(EntityKind.Place, City.PlaceRiver);
            float old = World.RiverTarget;
            float target;
            if (cmd.Kind == CommandKind.RiverNormal) target = 0f;
            else if (cmd.Kind == CommandKind.RiverSet) target = cmd.FloatArg;
            else target = old + cmd.FloatArg;
            float clamped = MathUtil.Clamp(target, CityData.MinRiverLevel, CityData.MaxRiverLevel);
            bool wasClamped = Math.Abs(clamped - target) > 0.001f;
            target = (float)Math.Round(clamped, 2);
            if (Math.Abs(target - old) < 0.005f)
            {
                Unchanged(o, cmd.Kind == CommandKind.RiverNormal ? "Река и так в норме" : "Уровень реки уже " + RuText.SignedMetres(old));
                if (wasClamped) o.Lines.Add("Предел: от " + RuText.SignedMetres(CityData.MinRiverLevel) + " до " + RuText.SignedMetres(CityData.MaxRiverLevel));
                return;
            }
            int unavailableBefore = Stats.RoutesUnavailable;
            World.RiverTarget = target;
            RefreshBlocking();
            ReplanAll();
            UpdateStats();
            var d = ComputeDiff();
            float delta = target - old;
            if (cmd.Kind == CommandKind.RiverNormal) o.Title = "Река возвращается в норму";
            else if (delta > 0) o.Title = "Река поднимается на " + RuText.Metres(delta);
            else o.Title = "Река опускается на " + RuText.Metres(delta);

            var flooded = new List<string>();
            if (target >= CityData.LotY - 0.02f) flooded.Add(World.LotIsPark ? "парк у реки" : "парковка у реки");
            if (target >= CityData.TerraceY - 0.02f) flooded.Add("набережные");
            if (target >= CityData.BridgeFloodY - 0.02f) flooded.Add("оба моста");
            if (flooded.Count > 0)
            {
                string s = string.Join(", ", flooded);
                o.Lines.Add("Затоплено: " + s);
            }
            else if (old >= CityData.LotY - 0.02f)
                o.Lines.Add("Вода отступает — берег снова сухой");
            else
                o.Lines.Add("Уровень " + RuText.SignedMetres(target) + " — пока всё сухо");

            int unavailable = Stats.RoutesUnavailable;
            if (unavailable > 0)
            {
                int grow = unavailable - unavailableBefore;
                o.Lines.Add("Недоступно: " + RuText.Routes(unavailable) + (grow > 0 ? " (+" + grow + ")" : ""));
            }
            else if (unavailableBefore > 0)
                o.Lines.Add("Все маршруты снова доступны");
            if (target >= CityData.LotY - 0.02f)
            {
                int inLot = Traffic.CarsInLot();
                if (inLot > 0) o.Lines.Insert(Math.Min(1, o.Lines.Count), RuText.Count(inLot, "машина осталась", "машины остались", "машин остались") + " на затопленной парковке");
            }
            if (wasClamped) o.Lines.Insert(0, "Максимум: " + RuText.SignedMetres(CityData.MaxRiverLevel));
            o.PeopleChangedPlans = d.PeopleChanged;
            o.VehiclesWaiting = d.VehNoRouteNew;
        }

        // ------------------------------------------------------------------ time & seasons

        public static string PhaseName(float hour)
        {
            if (hour >= 5f && hour < 7f) return "рассвет";
            if (hour >= 7f && hour < 11f) return "утро";
            if (hour >= 11f && hour < 17f) return "день";
            if (hour >= 17f && hour < 21f) return "вечер";
            return "ночь";
        }

        private void ApplyTime(WorldCommand cmd, CommandOutcome o)
        {
            float hour = ((cmd.FloatArg % 24f) + 24f) % 24f;
            long dayStart = (ClockTicks / TicksPerDay) * TicksPerDay;
            long target = dayStart + (long)(hour / 24f * TicksPerDay);
            if (target <= ClockTicks) target += TicksPerDay;
            ClockTicks = target;
            ReplanAll();
            var d = ComputeDiff();
            o.PeopleChangedPlans = d.PeopleChanged;
            string phase = PhaseName(hour);
            switch (phase)
            {
                case "рассвет": o.Title = "Рассвет, " + MathUtil.FormatHour(hour); break;
                case "утро": o.Title = "Наступило утро, " + MathUtil.FormatHour(hour); break;
                case "день": o.Title = "Наступил день, " + MathUtil.FormatHour(hour); break;
                case "вечер": o.Title = "Закат, " + MathUtil.FormatHour(hour); break;
                default: o.Title = "Наступила ночь, " + MathUtil.FormatHour(hour); break;
            }
            if (d.ToHome > 0) o.Lines.Add(RuText.Count(d.ToHome, "человек идёт", "человека идут", "человек идут") + " домой");
            if (d.ToWork > 0) o.Lines.Add(RuText.Count(d.ToWork, "человек спешит", "человека спешат", "человек спешат") + " на работу и в школу");
            if (phase == "ночь" || phase == "вечер") o.Lines.Add("Зажглись окна и фонари");
            if (o.Lines.Count == 0 && d.PeopleChanged > 0) o.Lines.Add(RuText.Count(d.PeopleChanged, "человек меняет", "человека меняют", "человек меняют") + " планы");
        }

        public static string SeasonName(Season s)
        {
            switch (s)
            {
                case Season.Spring: return "весна";
                case Season.Summer: return "лето";
                case Season.Autumn: return "осень";
                default: return "зима";
            }
        }

        private void ApplySeason(WorldCommand cmd, CommandOutcome o)
        {
            var s = (Season)MathUtil.Clamp(cmd.IntArg, 0, 3);
            if (World.Season == s)
            {
                Unchanged(o, "Уже " + SeasonName(s));
                return;
            }
            World.Season = s;
            ReplanAll();
            var d = ComputeDiff();
            switch (s)
            {
                case Season.Spring:
                    o.Title = "Наступила весна";
                    o.Lines.Add("Деревья зацвели, трава снова зелёная");
                    break;
                case Season.Summer:
                    o.Title = "Наступило лето";
                    o.Lines.Add("Густая листва и тёплые вечера");
                    break;
                case Season.Autumn:
                    o.Title = "Наступила осень";
                    o.Lines.Add("Листва стала золотой и оранжевой");
                    break;
                default:
                    o.Title = "Наступила зима";
                    o.Lines.Add("Снег укрыл крыши, деревья сбросили листву");
                    o.Lines.Add("Машины едут осторожнее: −" + (int)Math.Round((1f - Traffic.WeatherSpeedFactor()) * 100f) + "% скорости");
                    break;
            }
            if (d.PeopleChanged > 0 && o.Lines.Count < 2)
                o.Lines.Add(RuText.Count(d.PeopleChanged, "человек меняет", "человека меняют", "человек меняют") + " планы");
        }

        // ------------------------------------------------------------------ festival & lot

        private void ApplyFestival(WorldCommand cmd, CommandOutcome o)
        {
            o.Entity = new EntityRef(EntityKind.Place, City.PlaceCentralPark);
            bool start = cmd.Kind == CommandKind.StartFestival;
            if (World.Festival == start)
            {
                Unchanged(o, start ? "Фестиваль уже идёт" : "Фестиваля сейчас нет");
                return;
            }
            World.Festival = start;
            if (start)
            {
                World.FestivalSince = Tick;
                World.FestivalPeak = 0;
                World.FestivalPeakReported = false;
            }
            ReplanAll();
            var d = ComputeDiff();
            if (start)
            {
                o.Title = "Фестиваль в Центральном парке!";
                if (d.ToFestival > 0) o.Lines.Add(RuText.Count(d.ToFestival, "человек направился", "человека направились", "человек направились") + " к сцене");
                if (World.Weather == Weather.Storm) o.Lines.Add("В грозу почти никто не пришёл");
                else if (Hour >= 22f || Hour < 9f) o.Lines.Add("Сейчас все спят — гости подтянутся утром");
            }
            else
            {
                o.Title = "Фестиваль закончился";
                if (d.FromFestival > 0) o.Lines.Add(RuText.Count(d.FromFestival, "человек расходится", "человека расходятся", "человек расходятся") + " по домам и делам");
            }
        }

        private void ApplyLot(WorldCommand cmd, CommandOutcome o)
        {
            o.Entity = new EntityRef(EntityKind.Place, City.PlaceParking);
            bool toPark = cmd.Kind == CommandKind.LotToPark;
            if (World.LotIsPark == toPark)
            {
                Unchanged(o, toPark ? "У реки уже парк" : "У реки и так парковка");
                return;
            }
            World.LotIsPark = toPark;
            int leaving = toPark ? Traffic.EvictLot() : 0;
            ReplanAll();
            var d = ComputeDiff();
            if (toPark)
            {
                int trees = 0, benches = 0;
                foreach (var p in City.Props)
                {
                    if (p.Group != 1) continue;
                    if (p.Kind == PropKind.Tree || p.Kind == PropKind.Conifer) trees++;
                    if (p.Kind == PropKind.Bench) benches++;
                }
                o.Title = "Парковка у реки стала парком";
                o.Lines.Add("Посажено " + RuText.Count(trees, "дерево", "дерева", "деревьев") + ", поставлено " + RuText.Count(benches, "скамейка", "скамейки", "скамеек"));
                if (leaving > 0)
                    o.Lines.Add(RuText.Count(leaving, "машина уезжает", "машины уезжают", "машин уезжают") + " с парковки" + (Traffic.LotFlooded ? ", но вода их не пускает" : ""));
                else if (d.ToRiverPark > 0)
                    o.Lines.Add(RuText.Count(d.ToRiverPark, "человек идёт", "человека идут", "человек идут") + " гулять к реке");
            }
            else
            {
                o.Title = "У реки снова парковка";
                if (d.FromRiverPark > 0) o.Lines.Add(RuText.Count(d.FromRiverPark, "человек уходит", "человека уходят", "человек уходят") + " из парка у реки");
                o.Lines.Add(RuText.Count(City.LotSlots.Count, "место", "места", "мест") + " для машин снова свободны");
            }
        }

        // ------------------------------------------------------------------ emergencies

        private void ApplyRobbery(WorldCommand cmd, CommandOutcome o)
        {
            int bank = cmd.IntArg >= 0 ? cmd.IntArg : City.BankBuilding;
            o.Entity = new EntityRef(EntityKind.Place, City.PlaceBank);
            if (World.RobberyActive)
            {
                Unchanged(o, "Банк уже грабят!");
                return;
            }
            World.RobberyBuilding = bank;
            World.RobberyStage = RobberyStage.InProgress;
            World.RobberySince = Tick;
            int n = Traffic.Dispatch(VehicleKind.Police, bank, out float len, out int unreachable);
            ReplanAll();
            var d = ComputeDiff();
            o.Title = "Ограбление банка!";
            int scared = d.Fleeing + d.Evacuated;
            if (scared > 0) o.Lines.Add(RuText.Count(scared, "человек разбегается", "человека разбегаются", "человек разбегаются") + " от банка");
            int going = n - unreachable;
            if (going <= 0) o.Lines.Add("Полиции не проехать к банку — нет маршрута!");
            else
            {
                int secs = (int)Math.Ceiling(len / (Config.CarSpeed * 1.2f)) + 3;
                o.Lines.Add("Полиция выехала: " + RuText.Cars(going) + ", в пути ~" + secs + " с");
            }
        }

        private void ApplyFire(WorldCommand cmd, CommandOutcome o)
        {
            int b = cmd.IntArg >= 0 && cmd.IntArg < City.Buildings.Count ? cmd.IntArg : City.SchoolBuilding;
            var bld = City.Buildings[b];
            o.Entity = bld.Place >= 0 ? new EntityRef(EntityKind.Place, bld.Place) : new EntityRef(EntityKind.Building, b);
            if (World.FireActive)
            {
                Unchanged(o, World.FireBuilding == b ? "Здесь уже горит!" : "Уже горит «" + City.Buildings[World.FireBuilding].Name + "» — сначала потушите его");
                return;
            }
            World.FireBuilding = b;
            World.FireStage = FireStage.Burning;
            World.FireIntensity = 0.3f;
            World.FireSince = Tick;
            Traffic.Dispatch(VehicleKind.FireTruck, b, out float len, out int unreachable);
            ReplanAll();
            var d = ComputeDiff();
            o.Title = bld.Type == BuildingType.School ? "Пожар в школе!" : "Пожар: «" + bld.Name + "»!";
            if (d.Evacuated > 0) o.Lines.Add("Эвакуировано " + RuText.People(d.Evacuated) + (bld.Type == BuildingType.School ? " из школы" : ""));
            if (d.Fleeing > 0) o.Lines.Add(RuText.Count(d.Fleeing, "прохожий отходит", "прохожих отходят", "прохожих отходят") + " подальше");
            if (unreachable > 0) o.Lines.Insert(0, "Пожарной машине не проехать — нет маршрута!");
            else
            {
                int secs = (int)Math.Ceiling(len / (Config.CarSpeed * 1.2f)) + 3;
                o.Lines.Add("Пожарная машина выехала, в пути ~" + secs + " с");
            }
        }

        private void ApplyExtinguish(CommandOutcome o)
        {
            if (!World.FireActive)
            {
                Unchanged(o, "Сейчас ничего не горит");
                return;
            }
            var bld = City.Buildings[World.FireBuilding];
            o.Entity = bld.Place >= 0 ? new EntityRef(EntityKind.Place, bld.Place) : new EntityRef(EntityKind.Building, bld.Id);
            ExtinguishFire();
            ReplanAll();
            var d = ComputeDiff();
            o.Title = "Пожар потушен";
            if (d.PeopleChanged > 0) o.Lines.Add(RuText.Count(d.PeopleChanged, "человек возвращается", "человека возвращаются", "человек возвращаются") + " к своим делам");
            o.Lines.Add("Пожарная машина едет обратно в часть");
        }
    }
}
