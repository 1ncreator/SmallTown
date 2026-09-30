using System;
using System.Collections.Generic;
using SmallTown.Simulation;
using SmallTown.Simulation.Events;
using SmallTown.Simulation.World;
using SmallTown.Utils;

namespace SmallTown.Commands
{
    public enum SpecKind : byte { World, Undo, ResetWorld, Pause, Resume, Faster, Slower, SetSpeed, Show, Help }

    /// <summary>A parsed, fully resolved instruction.</summary>
    public sealed class CommandSpec
    {
        public SpecKind Kind;
        public WorldCommand World;
        public int Place = -1;
        public float Value;
        public string Canonical = "";

        public override string ToString() => Kind == SpecKind.World ? World.ToString() : Kind.ToString();
    }

    public enum ParseStatus : byte { Ok, Ambiguous, CannotDo, Unknown, Empty }

    public struct ParseOption
    {
        public string Label;
        public string Text;

        public ParseOption(string label, string text)
        {
            Label = label;
            Text = text;
        }
    }

    public sealed class ParseResult
    {
        public ParseStatus Status;
        public readonly List<CommandSpec> Commands = new List<CommandSpec>();
        public string Message = "";
        public readonly List<ParseOption> Options = new List<ParseOption>();
    }

    /// <summary>What the parser may need to know about the world: selection, last entity, bridge states.</summary>
    public sealed class ParseContext
    {
        public CityData City;
        public bool NorthClosed;
        public bool SouthClosed;
        public bool LotIsPark;
        public int FireBuilding = -1;
        public EntityRef Selected = EntityRef.None;
        public EntityRef Last = EntityRef.None;
    }

    /// <summary>
    /// Offline rule-based interpreter for Russian and English commands. Vocabulary comes from
    /// <see cref="CommandGrammar"/>; this class contains only the resolution rules.
    /// </summary>
    public sealed class CommandParser
    {
        private sealed class Ann
        {
            public string Cat;
            public string Id;
            public float Num;
            public string Token;
        }

        private sealed class Clause
        {
            public readonly List<Ann> Anns = new List<Ann>();
            public readonly HashSet<string> I = new HashSet<string>();
            public readonly HashSet<string> E = new HashSet<string>();
            public readonly Dictionary<string, int> Pos = new Dictionary<string, int>();
            public bool This, It;
            public float Amount = float.NaN;
            public float Number = float.NaN;
            public float Hour = -1f;
            public int TargetBuilding = -1;
            public string FirstIntent;
            public bool Any => I.Count > 0 || E.Count > 0 || This || It || !float.IsNaN(Amount) || Hour >= 0f || !float.IsNaN(Number);
        }

        private sealed class ClauseResult
        {
            public ParseStatus Status;
            public CommandSpec Spec;
            public string Message = "";
            public readonly List<ParseOption> Variants = new List<ParseOption>();
        }

        private static readonly string[] TargetEntities =
        {
            "bridge", "north", "south", "both", "river", "level", "park", "parking", "festival", "school", "bank", "church", "market",
            "square", "firestation", "police", "watertower", "lighthouse", "promenade", "central"
        };

        private static readonly string[] DomainEntities =
        {
            "bridge", "north", "south", "both", "river", "level", "park", "parking", "festival", "rain", "storm", "snow", "clear",
            "dawn", "morning", "day", "noon", "evening", "night", "midnight", "spring", "summer", "autumn", "winter", "school", "bank",
            "church", "market", "square", "firestation", "police", "watertower", "lighthouse", "promenade", "weather_word", "time_word",
            "season_word", "speed_word", "central"
        };

        private readonly CommandGrammar _g;

        public CommandParser(CommandGrammar grammar)
        {
            _g = grammar;
        }

        public const string CapabilitiesHint = "Я умею: мосты, уровень реки, погода, время суток, времена года, фестиваль, парковка у реки, пожар и ограбление банка.";

        // ------------------------------------------------------------------ entry point

        public ParseResult Parse(string text, ParseContext ctx)
        {
            var res = new ParseResult();
            var tokens = CommandGrammar.Tokenize(text ?? "");
            if (tokens.Count == 0)
            {
                res.Status = ParseStatus.Empty;
                res.Message = "Напишите, что изменить в мире — например, «закрой северный мост».";
                return res;
            }

            var clauses = new List<Clause>();
            var current = new List<string>();
            for (int i = 0; i < tokens.Count;)
            {
                int k = MatchConnector(tokens, i);
                if (k > 0)
                {
                    if (current.Count > 0) clauses.Add(Annotate(current));
                    current = new List<string>();
                    i += k;
                    continue;
                }
                current.Add(tokens[i]);
                i++;
            }
            if (current.Count > 0) clauses.Add(Annotate(current));
            clauses.RemoveAll(c => !c.Any);
            if (clauses.Count == 0)
            {
                res.Status = ParseStatus.Unknown;
                res.Message = "Не понял команду «" + text.Trim() + "». " + CapabilitiesHint;
                return res;
            }

            // A verb-only clause ("закройте, пожалуйста, северный мост") lends its verb to the next clause.
            for (int i = 0; i + 1 < clauses.Count; i++)
            {
                var c = clauses[i];
                var n = clauses[i + 1];
                bool verbOnly = c.I.Count > 0 && c.E.Count == 0 && !c.This && !c.It && float.IsNaN(c.Amount) && c.Hour < 0f;
                if (!verbOnly || n.I.Count > 0) continue;
                foreach (var intent in c.I) n.I.Add(intent);
                n.FirstIntent = c.FirstIntent;
                clauses.RemoveAt(i);
                i--;
            }

            // Clauses without a verb inherit the previous one ("закрой северный и южный мост").
            string prevIntent = null;
            foreach (var c in clauses)
            {
                if (c.I.Count == 0 && prevIntent != null && (c.E.Count > 0 || c.This || c.It)) c.I.Add(prevIntent);
                if (c.FirstIntent != null) prevIntent = c.FirstIntent;
                else if (c.I.Count > 0) foreach (var i in c.I) { prevIntent = i; break; }
            }

            var results = new List<ClauseResult>();
            foreach (var c in clauses) results.Add(Resolve(c, ctx));

            int amb = results.FindIndex(r => r.Status == ParseStatus.Ambiguous);
            if (amb >= 0)
            {
                res.Status = ParseStatus.Ambiguous;
                res.Message = results[amb].Message;
                foreach (var v in results[amb].Variants)
                {
                    var parts = new List<string>();
                    for (int j = 0; j < results.Count; j++)
                    {
                        if (j == amb) parts.Add(v.Text);
                        else if (results[j].Status == ParseStatus.Ok) parts.Add(results[j].Spec.Canonical);
                    }
                    res.Options.Add(new ParseOption(v.Label, string.Join(" и ", parts)));
                }
                return res;
            }

            foreach (var r in results)
                if (r.Status == ParseStatus.Ok) res.Commands.Add(r.Spec);
            if (res.Commands.Count > 0)
            {
                res.Status = ParseStatus.Ok;
                foreach (var r in results)
                    if (r.Status == ParseStatus.CannotDo) { res.Message = r.Message; break; }
                return res;
            }
            foreach (var r in results)
                if (r.Status == ParseStatus.CannotDo)
                {
                    res.Status = ParseStatus.CannotDo;
                    res.Message = r.Message;
                    return res;
                }
            res.Status = ParseStatus.Unknown;
            res.Message = "Не понял команду «" + text.Trim() + "». " + CapabilitiesHint;
            return res;
        }

        private int MatchConnector(List<string> tokens, int i)
        {
            foreach (var con in _g.Connectors)
            {
                if (i + con.Length > tokens.Count) continue;
                bool ok = true;
                for (int k = 0; k < con.Length && ok; k++)
                    if (tokens[i + k] != con[k]) ok = false;
                if (ok) return con.Length;
            }
            return 0;
        }

        // ------------------------------------------------------------------ annotation

        private Clause Annotate(List<string> tokens)
        {
            var c = new Clause();
            int i = 0;
            while (i < tokens.Count)
            {
                string t = tokens[i];
                if (CommandGrammar.TryClock(t, out float clock))
                {
                    c.Hour = clock;
                    i++;
                    continue;
                }
                if (CommandGrammar.TryNumber(t, out float num))
                {
                    c.Anns.Add(new Ann { Cat = "number", Num = num, Token = t });
                    i++;
                    continue;
                }
                Lexeme best = null;
                foreach (var lx in _g.Lexemes)
                {
                    int k = lx.Words.Length;
                    if (i + k > tokens.Count) continue;
                    bool ok = true;
                    for (int j = 0; j < k && ok; j++)
                        if (!CommandGrammar.WordMatches(tokens[i + j], lx.Words[j], lx.Exact[j])) ok = false;
                    if (!ok) continue;
                    if (best == null || k > best.Words.Length || (k == best.Words.Length && lx.Weight > best.Weight)) best = lx;
                }
                if (best == null)
                {
                    i++;
                    continue;
                }
                var ann = new Ann { Cat = best.Category, Id = best.Id, Token = t };
                if (best.Category == "number") float.TryParse(best.Id, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ann.Num);
                c.Anns.Add(ann);
                i += best.Words.Length;
            }

            for (int k = 0; k < c.Anns.Count; k++)
            {
                var a = c.Anns[k];
                var next = k + 1 < c.Anns.Count ? c.Anns[k + 1] : null;
                switch (a.Cat)
                {
                    case "intent":
                        c.I.Add(a.Id);
                        if (c.FirstIntent == null) c.FirstIntent = a.Id;
                        break;
                    case "entity":
                        c.E.Add(a.Id);
                        c.Pos[a.Id] = k;
                        break;
                    case "pronoun":
                        if (a.Id == "this") c.This = true;
                        else c.It = true;
                        break;
                    case "number":
                        if (next != null && next.Cat == "unit")
                        {
                            c.Amount = UnitToMetres(a.Num, next.Id);
                            k++;
                        }
                        else if (next != null && next.Cat == "timeq")
                        {
                            c.Hour = ToHour(a.Num, next.Id);
                            k++;
                        }
                        else if (next != null && next.Cat == "number" && next.Num == 0.5f)
                        {
                            c.Number = a.Num + 0.5f; // "один с половиной"
                            k++;
                        }
                        else c.Number = a.Num;
                        break;
                    case "unit":
                        c.Amount = a.Id == "halfm" ? 0.5f : (a.Id == "cm" ? 0.01f : 1f);
                        break;
                    case "timeq":
                        if (a.Id == "am") c.E.Add("morning");
                        else if (a.Id == "pm") c.E.Add(a.Token.StartsWith("дн", StringComparison.Ordinal) ? "day" : "evening");
                        else if (a.Id == "night") c.E.Add("night");
                        break;
                }
            }
            if (float.IsNaN(c.Amount) && !float.IsNaN(c.Number)) c.Amount = c.Number >= 5f ? c.Number / 100f : c.Number;
            return c;
        }

        private static float UnitToMetres(float v, string unit)
        {
            switch (unit)
            {
                case "cm": return v / 100f;
                case "halfm": return 0.5f;
                default: return v;
            }
        }

        private static float ToHour(float n, string q)
        {
            switch (q)
            {
                case "am": return n >= 12f ? n - 12f : n;
                case "pm": return n < 12f ? n + 12f : n;
                case "night": return n <= 5f ? n : (n < 12f ? n + 12f : n);
                default: return n % 24f;
            }
        }

        // ------------------------------------------------------------------ resolution

        private static bool Has(Clause c, string e) => c.E.Contains(e);
        private static bool Int(Clause c, string i) => c.I.Contains(i);

        private static bool HasAny(Clause c, string[] ids)
        {
            foreach (var id in ids)
                if (c.E.Contains(id)) return true;
            return false;
        }

        private static ClauseResult Ok(WorldCommand w, CityData city)
        {
            return new ClauseResult { Status = ParseStatus.Ok, Spec = new CommandSpec { Kind = SpecKind.World, World = w, Canonical = Canonical(w, city) } };
        }

        private static ClauseResult Ui(SpecKind kind, string canonical, int place = -1, float value = 0f)
        {
            return new ClauseResult { Status = ParseStatus.Ok, Spec = new CommandSpec { Kind = kind, Canonical = canonical, Place = place, Value = value } };
        }

        private static ClauseResult Cannot(string message) => new ClauseResult { Status = ParseStatus.CannotDo, Message = message };

        private static ClauseResult Ask(string question, params string[] labelTextPairs)
        {
            var r = new ClauseResult { Status = ParseStatus.Ambiguous, Message = question };
            for (int i = 0; i + 1 < labelTextPairs.Length; i += 2) r.Variants.Add(new ParseOption(labelTextPairs[i], labelTextPairs[i + 1]));
            return r;
        }

        private static bool ApplyImplied(Clause c, EntityRef e, ParseContext ctx, out string error)
        {
            error = null;
            if (e.Kind == EntityKind.Resident || e.Kind == EntityKind.Vehicle)
            {
                error = "Жители и машины живут своей жизнью — командовать ими нельзя. Выберите здание, мост или место.";
                return false;
            }
            if (e.Kind == EntityKind.Building)
            {
                c.TargetBuilding = e.Id;
                var b = ctx.City.Buildings[e.Id];
                if (b.Place >= 0) return ApplyImplied(c, new EntityRef(EntityKind.Place, b.Place), ctx, out error);
                c.E.Add("building");
                return true;
            }
            if (e.Kind != EntityKind.Place || e.Id < 0) return false;
            var p = ctx.City.Places[e.Id];
            switch (p.Key)
            {
                case "north_bridge": c.E.Add("bridge"); c.E.Add("north"); break;
                case "south_bridge": c.E.Add("bridge"); c.E.Add("south"); break;
                case "central_park": c.E.Add("park"); c.E.Add("central"); break;
                case "riverside_parking": c.E.Add("parking"); break;
                case "river": c.E.Add("river"); break;
                case "promenade": c.E.Add("promenade"); break;
                case "market_square": c.E.Add("market"); c.E.Add("square"); break;
                case "school": c.E.Add("school"); break;
                case "bank": c.E.Add("bank"); break;
                case "church": c.E.Add("church"); break;
                case "fire_station": c.E.Add("firestation"); break;
                case "police": c.E.Add("police"); break;
                case "water_tower": c.E.Add("watertower"); break;
                case "lighthouse": c.E.Add("lighthouse"); break;
            }
            if (p.Building >= 0 && p.Kind == PlaceKind.Building) c.TargetBuilding = p.Building;
            return true;
        }

        private static int BuildingFromEntities(Clause c, CityData city)
        {
            if (c.TargetBuilding >= 0) return c.TargetBuilding;
            if (Has(c, "school")) return city.SchoolBuilding;
            if (Has(c, "bank")) return city.BankBuilding;
            if (Has(c, "church")) return city.ChurchBuilding;
            if (Has(c, "firestation")) return city.FireStationBuilding;
            if (Has(c, "police")) return city.PoliceBuilding;
            if (Has(c, "market")) return city.MarketHallBuilding;
            if (Has(c, "watertower") && city.PlaceWaterTower >= 0) return city.Places[city.PlaceWaterTower].Building;
            if (Has(c, "lighthouse") && city.PlaceLighthouse >= 0) return city.Places[city.PlaceLighthouse].Building;
            return -1;
        }

        private static int PlaceFromEntities(Clause c, CityData city)
        {
            if (Has(c, "bridge") && Has(c, "north")) return city.PlaceNorthBridge;
            if (Has(c, "bridge") && Has(c, "south")) return city.PlaceSouthBridge;
            if (Has(c, "parking")) return city.PlaceParking;
            if (Has(c, "park")) return city.PlaceCentralPark;
            if (Has(c, "festival")) return city.PlaceCentralPark;
            if (Has(c, "market") || Has(c, "square")) return city.PlaceMarket;
            if (Has(c, "promenade")) return city.PlacePromenade;
            if (Has(c, "school")) return city.PlaceSchool;
            if (Has(c, "bank")) return city.PlaceBank;
            if (Has(c, "church")) return city.PlaceChurch;
            if (Has(c, "firestation")) return city.PlaceFire;
            if (Has(c, "police")) return city.PlacePolice;
            if (Has(c, "watertower")) return city.PlaceWaterTower;
            if (Has(c, "lighthouse")) return city.PlaceLighthouse;
            if (Has(c, "river") || Has(c, "bridge")) return city.PlaceRiver;
            return -1;
        }

        private ClauseResult Resolve(Clause c, ParseContext ctx)
        {
            var city = ctx.City;

            // Pronouns only fill in a missing target ("закрой его", "подожги это"), never override one.
            if ((c.This || c.It) && !HasAny(c, DomainEntities))
            {
                var e = c.This ? ctx.Selected : ctx.Last;
                if (e.IsNone)
                {
                    if (c.I.Count > 0 && !Int(c, "undo") && !Int(c, "reset") && !Int(c, "pause") && !Int(c, "resume") && !Int(c, "help") && !Int(c, "extinguish"))
                        return Cannot(c.This
                            ? "Не понял, что значит «это» — сначала выберите объект кликом на карте."
                            : "Не понял, что значит «его» — сначала измените что-нибудь или выберите объект.");
                }
                else if (!ApplyImplied(c, e, ctx, out string err) && err != null) return Cannot(err);
            }

            // Session-level commands
            if (Int(c, "undo") && c.E.Count == 0) return Ui(SpecKind.Undo, "отмени");
            if (Int(c, "reset") && (c.E.Count == 0 || Has(c, "world"))) return Ui(SpecKind.ResetWorld, "сбрось мир");
            if (Int(c, "restore") && Has(c, "world") && c.E.Count == 1) return Ui(SpecKind.ResetWorld, "сбрось мир");
            if (Int(c, "help")) return Ui(SpecKind.Help, "помощь");
            if (Int(c, "pause") || (Int(c, "stop") && Has(c, "time_word") && c.E.Count == 1)) return Ui(SpecKind.Pause, "пауза");
            if (Int(c, "resume") && !HasAny(c, DomainEntities)) return Ui(SpecKind.Resume, "продолжи");
            if (Has(c, "speed_word") && !float.IsNaN(c.Number)) return Ui(SpecKind.SetSpeed, "скорость " + c.Number, -1, c.Number);
            if (Int(c, "faster") && !HasAny(c, TargetEntities)) return Ui(SpecKind.Faster, "быстрее");
            if (Int(c, "slower") && !HasAny(c, TargetEntities)) return Ui(SpecKind.Slower, "медленнее");
            if (Int(c, "show"))
            {
                int place = PlaceFromEntities(c, city);
                if (place < 0) return Cannot("Что показать? Например: «покажи школу» или «покажи северный мост».");
                return Ui(SpecKind.Show, "покажи " + city.Places[place].NameRu.ToLowerInvariant(), place);
            }
            if (Int(c, "undo")) c.I.Add("stop"); // "отмени фестиваль"

            // Fire
            if (Int(c, "extinguish") || (Has(c, "fire") && (Int(c, "stop") || Int(c, "lower"))))
                return Ok(new WorldCommand(CommandKind.Extinguish, -1), city);
            if (Int(c, "burn") || (Has(c, "fire") && !Int(c, "show")))
            {
                int b = BuildingFromEntities(c, city);
                if (b < 0)
                {
                    if (HasAny(c, new[] { "bridge", "river", "park", "parking", "promenade", "square" }))
                        return Cannot("Поджечь можно только здание — например, школу или рынок.");
                    return Ask("Что поджечь?", "Школу", "подожги школу", "Рынок", "подожги рынок", "Церковь", "подожги церковь");
                }
                return Ok(new WorldCommand(CommandKind.StartFire, b), city);
            }

            // Robbery
            if (Int(c, "rob"))
            {
                int b = BuildingFromEntities(c, city);
                if (b >= 0 && b != city.BankBuilding) return Cannot("Грабить можно только банк — остальные здания под защитой добрых соседей.");
                return Ok(new WorldCommand(CommandKind.Robbery, city.BankBuilding), city);
            }

            // Festival
            if (Has(c, "festival"))
            {
                if (Int(c, "stop") || Int(c, "close")) return Ok(new WorldCommand(CommandKind.EndFestival), city);
                return Ok(new WorldCommand(CommandKind.StartFestival), city);
            }

            // Riverside parking <-> park
            bool parkingRef = Has(c, "parking") || (Has(c, "park") && Has(c, "river") && !Int(c, "raise") && !Int(c, "lower") && !Int(c, "flood"));
            if (parkingRef)
            {
                bool toLot;
                if (Has(c, "parking") && Has(c, "park"))
                {
                    if (Int(c, "restore")) toLot = true;
                    else toLot = c.Pos["parking"] > c.Pos["park"];
                }
                else if (Has(c, "parking"))
                {
                    if (Int(c, "restore")) toLot = true;
                    else if (Int(c, "convert")) toLot = false;
                    else if (Int(c, "set") || Int(c, "open")) toLot = true;
                    else if (Int(c, "close") || Int(c, "burn") || Int(c, "rob"))
                        return Cannot("Парковку нельзя закрыть или поджечь, но её можно превратить в парк.");
                    else if (Int(c, "flood") || Int(c, "raise")) return Ok(new WorldCommand(CommandKind.RiverSet, 0, 0.6f), city);
                    else return Ask("Что сделать с парковкой у реки?", "Превратить в парк", "преврати парковку в парк", "Вернуть парковку", "верни парковку");
                }
                else
                {
                    if (Int(c, "restore") || Int(c, "convert") && Int(c, "restore")) toLot = true;
                    else if (Int(c, "start") || Int(c, "set") || Int(c, "convert")) toLot = false;
                    else return Ask("Что сделать с парком у реки?", "Сделать парк", "преврати парковку в парк", "Вернуть парковку", "верни парковку");
                }
                return Ok(new WorldCommand(toLot ? CommandKind.ParkToLot : CommandKind.LotToPark), city);
            }

            // Bridges
            bool sideWords = Has(c, "north") || Has(c, "south") || Has(c, "both");
            bool bridgeRef = Has(c, "bridge") || (sideWords && (Int(c, "close") || Int(c, "open") || Int(c, "restore")) && !Has(c, "river") && !Has(c, "park"));
            if (bridgeRef)
            {
                int which = -1;
                if (Has(c, "both") || (Has(c, "north") && Has(c, "south"))) which = 2;
                else if (Has(c, "north")) which = 0;
                else if (Has(c, "south")) which = 1;
                bool? close = null;
                if (Int(c, "close") || Int(c, "stop")) close = true;
                else if (Int(c, "open") || Int(c, "restore") || Int(c, "start")) close = false;
                if (Int(c, "burn")) return Cannot("Мосты каменные — поджечь их не получится. Их можно закрыть.");
                if (close == null)
                {
                    string n = which == 0 ? "северный мост" : which == 1 ? "южный мост" : which == 2 ? "оба моста" : "мост";
                    string nn = which == 0 ? "северный мост" : which == 1 ? "южный мост" : "оба моста";
                    return Ask("Что сделать: закрыть или открыть " + n + "?", "Закрыть", "закрой " + nn, "Открыть", "открой " + nn);
                }
                if (which < 0)
                {
                    if (close == false)
                    {
                        if (ctx.NorthClosed && !ctx.SouthClosed) which = 0;
                        else if (ctx.SouthClosed && !ctx.NorthClosed) which = 1;
                        else if (!ctx.NorthClosed && !ctx.SouthClosed) which = 2;
                        else return Ask("Какой мост открыть?", "Северный", "открой северный мост", "Южный", "открой южный мост", "Оба", "открой оба моста");
                    }
                    else return Ask("Какой мост закрыть?", "Северный", "закрой северный мост", "Южный", "закрой южный мост", "Оба", "закрой оба моста");
                }
                return Ok(new WorldCommand(close == true ? CommandKind.CloseBridge : CommandKind.OpenBridge, which), city);
            }

            // River level
            bool otherDomain = HasAny(c, new[] { "rain", "storm", "snow", "clear", "dawn", "morning", "day", "noon", "evening", "night", "midnight", "spring", "summer", "autumn", "winter", "school", "bank", "church", "market", "square", "police", "firestation", "watertower", "lighthouse", "park" });
            bool riverRef = Has(c, "river") || Has(c, "level") || Int(c, "flood") || (Has(c, "promenade") && (Int(c, "raise") || Int(c, "lower")))
                            || ((Int(c, "raise") || Int(c, "lower")) && !otherDomain);
            if (riverRef)
            {
                bool hasAmount = !float.IsNaN(c.Amount);
                float amt = hasAmount ? Math.Abs(c.Amount) : 0f;
                if (Int(c, "flood"))
                {
                    if (Has(c, "promenade")) return Ok(new WorldCommand(CommandKind.RiverSet, 0, 1.0f), city);
                    return Ok(new WorldCommand(CommandKind.RiverDelta, 0, hasAmount ? amt : 1f), city);
                }
                if (Int(c, "raise"))
                {
                    if (!hasAmount) return Ask("На сколько поднять реку?", "На 50 см", "подними реку на 50 см", "На 1 метр", "подними реку на 1 метр", "На 1,5 метра", "подними реку на 1,5 метра");
                    return Ok(new WorldCommand(CommandKind.RiverDelta, 0, amt), city);
                }
                if (Int(c, "lower"))
                {
                    if (!hasAmount) return Ask("На сколько опустить реку?", "На 50 см", "опусти реку на 50 см", "На 1 метр", "опусти реку на 1 метр", "В норму", "верни реку в норму");
                    return Ok(new WorldCommand(CommandKind.RiverDelta, 0, -amt), city);
                }
                if (Int(c, "restore")) return Ok(new WorldCommand(CommandKind.RiverNormal), city);
                if (Int(c, "set") && hasAmount) return Ok(new WorldCommand(CommandKind.RiverSet, 0, c.Amount), city);
                if (Int(c, "close") || Int(c, "burn") || Int(c, "rob")) return Cannot("С рекой так нельзя, но её уровень можно поднять или опустить.");
                return Ask("Что сделать с рекой?", "Поднять на 1 м", "подними реку на 1 метр", "Опустить на 50 см", "опусти реку на 50 см", "Вернуть в норму", "верни реку в норму");
            }

            // Weather
            string weather = LastOf(c, "rain", "storm", "snow", "clear");
            if (weather != null)
            {
                bool stop = Int(c, "stop") || Int(c, "close");
                var w = Weather.Clear;
                if (!stop || weather == "clear")
                {
                    switch (weather)
                    {
                        case "rain": w = Weather.Rain; break;
                        case "storm": w = Weather.Storm; break;
                        case "snow": w = Weather.Snow; break;
                    }
                }
                return Ok(new WorldCommand(CommandKind.SetWeather, (int)w), city);
            }
            if (Has(c, "weather_word"))
                return Ask("Какую погоду сделать?", "Дождь", "включи дождь", "Гроза", "начни грозу", "Снег", "пусть пойдёт снег", "Ясно", "сделай ясную погоду");

            // Time of day
            if (c.Hour >= 0f) return Ok(new WorldCommand(CommandKind.SetTime, 0, c.Hour), city);
            string phase = LastOf(c, "dawn", "morning", "day", "noon", "evening", "night", "midnight");
            if (phase != null) return Ok(new WorldCommand(CommandKind.SetTime, 0, PhaseHour(phase)), city);
            if (Has(c, "time_word"))
                return Ask("Какое время суток сделать?", "Рассвет", "сделай рассвет", "Утро", "сделай утро", "День", "сделай день", "Закат", "сделай закат", "Ночь", "сделай ночь");

            // Seasons
            string season = LastOf(c, "spring", "summer", "autumn", "winter");
            if (season != null)
            {
                int s = season == "spring" ? 0 : season == "summer" ? 1 : season == "autumn" ? 2 : 3;
                return Ok(new WorldCommand(CommandKind.SetSeason, s), city);
            }
            if (Has(c, "season_word"))
                return Ask("Какое время года?", "Весна", "наступает весна", "Лето", "наступает лето", "Осень", "наступает осень", "Зима", "наступает зима");

            // Recognised something but no rule fits: say so honestly.
            if (c.E.Count > 0 || c.I.Count > 0)
            {
                string what = DescribeTarget(c, city);
                if (what != null) return Cannot("Не умею делать такое с объектом «" + what + "». " + CapabilitiesHint);
                return Cannot("Не понял, что именно изменить. " + CapabilitiesHint);
            }
            return new ClauseResult { Status = ParseStatus.Unknown };
        }

        private static string DescribeTarget(Clause c, CityData city)
        {
            int p = PlaceFromEntities(c, city);
            if (p >= 0) return city.Places[p].NameRu;
            if (c.TargetBuilding >= 0) return city.Buildings[c.TargetBuilding].Name;
            return null;
        }

        private static string LastOf(Clause c, params string[] ids)
        {
            string best = null;
            int bestPos = -1;
            foreach (var id in ids)
                if (c.Pos.TryGetValue(id, out int p) && p > bestPos) { bestPos = p; best = id; }
                else if (c.E.Contains(id) && best == null) best = id;
            return best;
        }

        public static float PhaseHour(string phase)
        {
            switch (phase)
            {
                case "dawn": return 6f;
                case "morning": return 8f;
                case "day": return 13f;
                case "noon": return 12f;
                case "evening": return 19.5f;
                case "night": return 23f;
                case "midnight": return 0f;
                default: return 8f;
            }
        }

        // ------------------------------------------------------------------ canonical texts

        public static string Canonical(WorldCommand w, CityData city)
        {
            switch (w.Kind)
            {
                case CommandKind.CloseBridge: return w.IntArg == 0 ? "закрой северный мост" : w.IntArg == 1 ? "закрой южный мост" : "закрой оба моста";
                case CommandKind.OpenBridge: return w.IntArg == 0 ? "открой северный мост" : w.IntArg == 1 ? "открой южный мост" : "открой оба моста";
                case CommandKind.SetWeather:
                    switch ((Weather)w.IntArg)
                    {
                        case Weather.Rain: return "включи дождь";
                        case Weather.Storm: return "начни грозу";
                        case Weather.Snow: return "пусть пойдёт снег";
                        default: return "сделай ясную погоду";
                    }
                case CommandKind.RiverDelta: return (w.FloatArg >= 0 ? "подними реку на " : "опусти реку на ") + RuText.Metres(w.FloatArg);
                case CommandKind.RiverSet: return w.FloatArg >= 0 ? "установи уровень реки на " + RuText.Metres(w.FloatArg) : "опусти реку на " + RuText.Metres(w.FloatArg);
                case CommandKind.RiverNormal: return "верни реку в норму";
                case CommandKind.SetTime:
                    float h = w.FloatArg;
                    if (Math.Abs(h - 6f) < 0.01f) return "сделай рассвет";
                    if (Math.Abs(h - 8f) < 0.01f) return "сделай утро";
                    if (Math.Abs(h - 13f) < 0.01f) return "сделай день";
                    if (Math.Abs(h - 12f) < 0.01f) return "сделай полдень";
                    if (Math.Abs(h - 19.5f) < 0.01f) return "сделай закат";
                    if (Math.Abs(h - 23f) < 0.01f) return "сделай ночь";
                    if (Math.Abs(h) < 0.01f) return "сделай полночь";
                    return "сделай время " + MathUtil.FormatHour(h);
                case CommandKind.SetSeason:
                    return "наступает " + (w.IntArg == 0 ? "весна" : w.IntArg == 1 ? "лето" : w.IntArg == 2 ? "осень" : "зима");
                case CommandKind.StartFestival: return "начни фестиваль";
                case CommandKind.EndFestival: return "закончи фестиваль";
                case CommandKind.LotToPark: return "преврати парковку в парк";
                case CommandKind.ParkToLot: return "верни парковку";
                case CommandKind.Robbery: return "ограбь банк";
                case CommandKind.Extinguish: return "потуши пожар";
                case CommandKind.StartFire:
                    if (city != null && w.IntArg >= 0 && w.IntArg < city.Buildings.Count)
                    {
                        switch (city.Buildings[w.IntArg].Type)
                        {
                            case BuildingType.School: return "подожги школу";
                            case BuildingType.Bank: return "подожги банк";
                            case BuildingType.Church: return "подожги церковь";
                            case BuildingType.MarketHall: return "подожги рынок";
                            case BuildingType.FireStation: return "подожги пожарную часть";
                            case BuildingType.Police: return "подожги полицию";
                            case BuildingType.WaterTower: return "подожги водонапорную башню";
                            case BuildingType.Lighthouse: return "подожги маяк";
                        }
                    }
                    return "подожги это";
                default: return "";
            }
        }
    }
}
