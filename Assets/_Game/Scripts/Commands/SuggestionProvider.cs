using System.Collections.Generic;
using SmallTown.Simulation;
using SmallTown.Simulation.World;

namespace SmallTown.Commands
{
    /// <summary>Three context-aware command suggestions shown under the input line.</summary>
    public static class SuggestionProvider
    {
        private static readonly string[] Defaults =
        {
            "закрой северный мост",
            "начни фестиваль в парке",
            "подними реку на 1 метр",
            "пусть пойдёт дождь",
            "подожги школу",
            "ограбь банк",
            "сделай ночь",
            "преврати парковку в парк",
            "наступает зима",
            "начни грозу",
            "закрой оба моста",
            "сделай закат",
            "подними реку на 50 см",
            "наступает осень"
        };

        public static List<string> Get(TownSimulation sim, int rotation)
        {
            var w = sim.World;
            var result = new List<string>(3);
            float hour = sim.Hour;

            void Add(string s)
            {
                if (result.Count < 3 && !result.Contains(s)) result.Add(s);
            }

            if (w.FireActive) Add("потуши пожар");
            if (hour >= 21f || hour < 5f) Add("верни утро");
            if (w.Weather == Weather.Snow) Add("останови снег");
            else if (w.Weather != Weather.Clear) Add("пусть будет ясно");
            if (w.NorthClosed && w.SouthClosed) Add("открой оба моста");
            else if (w.NorthClosed) Add("открой северный мост");
            else if (w.SouthClosed) Add("открой южный мост");
            if (w.RiverTarget > 0.01f || w.RiverTarget < -0.01f) Add("верни реку в норму");
            if (w.Festival) Add("закончи фестиваль");
            if (w.LotIsPark) Add("верни парковку");
            if (w.Season == Season.Winter) Add("наступает весна");

            int n = Defaults.Length;
            for (int k = 0; k < n && result.Count < 3; k++)
            {
                string s = Defaults[(rotation * 3 + k) % n];
                if (!Relevant(s, sim)) continue;
                Add(s);
            }
            return result;
        }

        private static bool Relevant(string s, TownSimulation sim)
        {
            var w = sim.World;
            float hour = sim.Hour;
            if (s.Contains("северный мост") && w.NorthClosed) return false;
            if (s.Contains("оба моста") && (w.NorthClosed || w.SouthClosed)) return false;
            if (s.Contains("фестиваль") && w.Festival) return false;
            if (s.Contains("дождь") && w.Weather == Weather.Rain) return false;
            if (s.Contains("грозу") && w.Weather == Weather.Storm) return false;
            if (s.Contains("подожги") && w.FireActive) return false;
            if (s.Contains("ограбь") && w.RobberyActive) return false;
            if (s.Contains("ночь") && (hour >= 21f || hour < 5f)) return false;
            if (s.Contains("закат") && hour >= 17f && hour < 21f) return false;
            if (s.Contains("парковку в парк") && w.LotIsPark) return false;
            if (s.Contains("зима") && w.Season == Season.Winter) return false;
            if (s.Contains("осень") && w.Season == Season.Autumn) return false;
            if (s.Contains("подними реку") && w.RiverTarget >= 1.5f) return false;
            return true;
        }
    }
}
