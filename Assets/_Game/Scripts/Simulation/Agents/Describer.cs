using System.Collections.Generic;
using SmallTown.Simulation.Traffic;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Simulation.Agents
{
    /// <summary>Human-readable (Russian) descriptions of agents, buildings and places for the inspector panel.</summary>
    public static class Describer
    {
        public static string BuildingTypeName(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.House: return "Жилой дом";
                case BuildingType.Apartment: return "Многоэтажный дом";
                case BuildingType.Office: return "Офис";
                case BuildingType.Shop: return "Магазин";
                case BuildingType.School: return "Школа";
                case BuildingType.Bank: return "Банк";
                case BuildingType.FireStation: return "Пожарная часть";
                case BuildingType.Police: return "Полиция";
                case BuildingType.Church: return "Церковь";
                case BuildingType.MarketHall: return "Рынок";
                case BuildingType.WaterTower: return "Водонапорная башня";
                default: return "Маяк";
            }
        }

        private static string Quote(string s) => "«" + s + "»";

        private static string PlaceOfNode(TownSimulation sim, int node)
        {
            var c = sim.City;
            if (c.ParkSpotNodes.Contains(node)) return "Центральный парк";
            if (c.MarketSpotNodes.Contains(node)) return "Рыночная площадь";
            if (c.RiversideNodes.Contains(node)) return sim.World.LotIsPark ? "парк у реки" : "парковка у реки";
            if (c.PromenadeNodes.Contains(node)) return "набережная";
            return "улица";
        }

        public static string GoalPhrase(TownSimulation sim, Resident r)
        {
            var c = sim.City;
            string b = r.TargetBuilding >= 0 ? Quote(c.Buildings[r.TargetBuilding].Name) : "";
            switch (r.Act)
            {
                case Activity.Home: return "домой";
                case Activity.Work: return "на работу в " + b;
                case Activity.School: return "в школу";
                case Activity.Shop: return "в магазин " + b;
                case Activity.Lunch: return r.TargetBuilding >= 0 ? "на обед в " + b : "обедать в парк";
                case Activity.Park: return "гулять в Центральный парк";
                case Activity.Promenade: return "гулять по набережной";
                case Activity.Market: return "на рынок";
                case Activity.Festival: return "на фестиваль";
                case Activity.Bank: return "в банк";
                case Activity.Church: return "в церковь";
                case Activity.Visit: return "в гости в " + b;
                case Activity.RiversidePark: return "в парк у реки";
                case Activity.Evacuate: return "прочь из горящего здания";
                case Activity.Flee: return "подальше от опасности";
                default: return "по делам";
            }
        }

        public static string DoingPhrase(TownSimulation sim, Resident r)
        {
            var c = sim.City;
            switch (r.State)
            {
                case ResidentState.Inside:
                    var b = c.Buildings[r.InsideBuilding];
                    if (r.InsideBuilding == r.Home) return "Дома";
                    if (r.Act == Activity.Work) return "Работает в " + Quote(b.Name);
                    if (r.Act == Activity.School) return "Учится в школе";
                    return "Внутри: " + Quote(b.Name);
                case ResidentState.Walking:
                    if (r.NoRoute) return "Ждёт: маршрут недоступен";
                    return (r.Act == Activity.Flee || r.Act == Activity.Evacuate ? "Бежит " : "Идёт ") + GoalPhrase(sim, r);
                default:
                    if (r.NoRoute) return "Ждёт: не может пройти " + GoalPhrase(sim, r);
                    switch (r.Act)
                    {
                        case Activity.Festival: return "Смотрит концерт на фестивале";
                        case Activity.Park: return "Гуляет в Центральном парке";
                        case Activity.Market: return "Выбирает покупки на рынке";
                        case Activity.Promenade: return "Гуляет по набережной";
                        case Activity.RiversidePark: return "Отдыхает в парке у реки";
                        case Activity.Lunch: return "Обедает в парке";
                        case Activity.Evacuate: return "Эвакуирован, ждёт в безопасности";
                        case Activity.Flee: return "Держится подальше от опасности";
                        default: return "Стоит: " + PlaceOfNode(sim, r.Node);
                    }
            }
        }

        public static void DescribeResident(TownSimulation sim, Resident r, out string title, out string subtitle, List<string> lines)
        {
            var c = sim.City;
            title = r.Name;
            string role;
            switch (r.Role)
            {
                case Role.Student: role = "школьник"; break;
                case Role.Retired: role = "пенсионер"; break;
                default: role = r.Work >= 0 ? "работает: " + Quote(c.Buildings[r.Work].Name) : "житель"; break;
            }
            subtitle = RuText.Count(r.Age, "год", "года", "лет") + " · " + role;
            lines.Clear();
            lines.Add(DoingPhrase(sim, r));
            if (r.State == ResidentState.Walking && !r.NoRoute && r.Path != null)
            {
                float left = 0f;
                var g = c.Walk;
                for (int i = r.PathIdx; i + 1 < r.Path.Length; i++)
                {
                    float dx = g.X[r.Path[i + 1]] - g.X[r.Path[i]], dz = g.Z[r.Path[i + 1]] - g.Z[r.Path[i]];
                    left += (float)System.Math.Sqrt(dx * dx + dz * dz);
                }
                left -= r.EdgeT;
                lines.Add("Осталось идти ~" + (int)System.Math.Max(0, left) + " м");
            }
            lines.Add("Дом: " + c.Buildings[r.Home].Name);
            if (r.Role == Role.Worker)
                lines.Add("График: " + MathUtil.FormatHour(r.LeaveHour) + "–" + MathUtil.FormatHour(r.EndHour) + ", сон в " + MathUtil.FormatHour(r.BedHour));
            else if (r.Role == Role.Student)
                lines.Add("Уроки: 08:00–14:30, сон в " + MathUtil.FormatHour(r.BedHour));
            else
                lines.Add("Встаёт в " + MathUtil.FormatHour(r.WakeHour) + ", ложится в " + MathUtil.FormatHour(r.BedHour));
            if (r.Umbrella && sim.World.Precipitation && r.IsOutside && sim.World.Weather != Weather.Snow) lines.Add("Под зонтом");
        }

        public static string StreetOfLane(TownSimulation sim, int lane)
        {
            var c = sim.City;
            if (lane < 0) return "";
            var seg = c.Roads.Segments[c.Roads.LSeg[lane]];
            if (seg.Bridge >= 0) return seg.Bridge == CityData.NorthBridge ? "Северный мост" : "Южный мост";
            return seg.Horizontal ? c.StreetNamesH[seg.Line] : c.StreetNamesV[seg.Line];
        }

        public static void DescribeVehicle(TownSimulation sim, Vehicle v, out string title, out string subtitle, List<string> lines)
        {
            var c = sim.City;
            switch (v.Kind)
            {
                case VehicleKind.Police: title = "Полицейская машина"; subtitle = "Участок: " + c.Buildings[v.HomeBuilding].Name; break;
                case VehicleKind.FireTruck: title = "Пожарная машина"; subtitle = c.Buildings[v.HomeBuilding].Name; break;
                default: title = "Автомобиль №" + (v.Id + 1); subtitle = "Горожанин за рулём"; break;
            }
            lines.Clear();
            string dest = v.DestBuilding >= 0 ? Quote(c.Buildings[v.DestBuilding].Name) : "";
            switch (v.State)
            {
                case VehicleState.Driving:
                case VehicleState.Transit:
                    if (v.NoRoute) lines.Add("Ждёт: нет проезда");
                    else if (v.Dest == DestKind.Lot) lines.Add("Едет на парковку у реки");
                    else if (v.Dest == DestKind.Base) lines.Add("Возвращается на базу");
                    else if (v.Dest == DestKind.Scene) lines.Add("Мчится на вызов: " + dest);
                    else lines.Add("Едет к " + dest);
                    lines.Add("Скорость: " + (int)(v.V * 3.6f) + " км/ч · " + StreetOfLane(sim, v.Lane));
                    if (v.StoppedTicks > 20) lines.Add("Стоит: пропускает транспорт или пешеходов");
                    break;
                case VehicleState.Parked:
                    lines.Add(v.NoRoute ? "Ждёт у обочины: объезда нет" : "Припаркован у " + (dest.Length > 0 ? dest : "обочины"));
                    lines.Add(StreetOfLane(sim, v.Lane));
                    break;
                case VehicleState.LotEnter:
                case VehicleState.LotParked:
                case VehicleState.LotExit:
                    lines.Add(v.State == VehicleState.LotParked ? "Стоит на парковке у реки" : (v.State == VehicleState.LotEnter ? "Заезжает на парковку" : "Выезжает с парковки"));
                    if (sim.Traffic.LotFlooded) lines.Add("Колёса в воде — не выехать");
                    break;
                case VehicleState.OnScene:
                    lines.Add("На месте вызова: " + dest);
                    break;
                default:
                    lines.Add("Дежурит у базы");
                    break;
            }
            if (v.Siren) lines.Add("Мигалки включены");
        }

        public static void DescribeBuilding(TownSimulation sim, Building b, out string title, out string subtitle, List<string> lines)
        {
            var c = sim.City;
            title = b.Name;
            subtitle = BuildingTypeName(b.Type) + (b.Floors > 1 ? " · " + RuText.Count(b.Floors, "этаж", "этажа", "этажей") : "");
            lines.Clear();
            int inside = 0, living = 0, working = 0;
            foreach (var r in sim.Residents)
            {
                if (r.State == ResidentState.Inside && r.InsideBuilding == b.Id) inside++;
                if (r.Home == b.Id) living++;
                if (r.Work == b.Id) working++;
            }
            var w = sim.World;
            if (w.FireActive && w.FireBuilding == b.Id) lines.Add(w.FireStage == FireStage.Fighting ? "Горит! Пожарные тушат" : "Горит! Ждём пожарных");
            if (w.RobberyActive && w.RobberyBuilding == b.Id) lines.Add(w.RobberyStage == RobberyStage.PoliceOnScene ? "Ограбление: полиция на месте" : "Идёт ограбление!");
            lines.Add("Внутри сейчас: " + RuText.People(inside));
            if (living > 0) lines.Add("Живут: " + RuText.People(living));
            if (working > 0) lines.Add((b.Type == BuildingType.School ? "Учатся и работают: " : "Работают: ") + RuText.People(working));
            if (b.CurbLane >= 0) lines.Add("Адрес: " + StreetOfLane(sim, b.CurbLane));
        }

        public static int CountPeopleIn(TownSimulation sim, Place p)
        {
            int n = 0;
            foreach (var r in sim.Residents)
            {
                if (!r.IsOutside) continue;
                if (r.X >= p.MinX && r.X <= p.MaxX && r.Z >= p.MinZ && r.Z <= p.MaxZ) n++;
            }
            return n;
        }

        public static void DescribePlace(TownSimulation sim, Place p, out string title, out string subtitle, List<string> lines)
        {
            var c = sim.City;
            var w = sim.World;
            title = p.NameRu;
            subtitle = p.NameEn;
            lines.Clear();
            switch (p.Kind)
            {
                case PlaceKind.Bridge:
                    bool closed = w.BridgeClosed(p.Bridge);
                    lines.Add(w.BridgesFlooded ? "Закрыт: вода у самого настила" : (closed ? "Закрыт — шлагбаумы опущены" : "Открыт для машин и пешеходов"));
                    int cars = 0;
                    foreach (var v in sim.Vehicles)
                        if (v.OnRoad && v.Lane >= 0 && c.Roads.LBridge[v.Lane] == p.Bridge) cars++;
                    lines.Add("Сейчас на мосту: " + RuText.Cars(cars) + ", " + RuText.People(CountPeopleIn(sim, p)));
                    break;
                case PlaceKind.River:
                    title = "Река";
                    lines.Add("Уровень воды: " + RuText.SignedMetres(w.RiverLevel) + (System.Math.Abs(w.RiverTarget - w.RiverLevel) > 0.01f ? " → " + RuText.SignedMetres(w.RiverTarget) : ""));
                    lines.Add("Затопление: парковка с +45 см, набережные с +90 см, мосты с +1,7 м");
                    break;
                case PlaceKind.Parking:
                    if (w.LotIsPark)
                    {
                        title = "Парк у реки";
                        lines.Add("Бывшая парковка: деревья и скамейки");
                        lines.Add("Гуляют: " + RuText.People(CountPeopleIn(sim, p)));
                    }
                    else
                    {
                        lines.Add("Машин на парковке: " + sim.Traffic.CarsInLot() + " из " + c.LotSlots.Count);
                    }
                    if (sim.Traffic.LotFlooded) lines.Add("Затоплено!");
                    break;
                case PlaceKind.Park:
                    lines.Add("Сейчас в парке: " + RuText.People(CountPeopleIn(sim, p)));
                    if (w.Festival) lines.Add("Идёт фестиваль! У сцены " + RuText.People(sim.CountFestivalCrowd()));
                    break;
                case PlaceKind.Square:
                    lines.Add("На площади: " + RuText.People(CountPeopleIn(sim, p)));
                    break;
                default:
                    if (p.Building >= 0)
                    {
                        DescribeBuilding(sim, c.Buildings[p.Building], out _, out string sub, lines);
                        subtitle = sub;
                    }
                    break;
            }
        }
    }
}
