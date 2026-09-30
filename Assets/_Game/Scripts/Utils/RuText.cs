using System.Globalization;

namespace SmallTown.Utils
{
    /// <summary>Russian text helpers (plural forms, number formatting).</summary>
    public static class RuText
    {
        /// <summary>Plural(5, "машина", "машины", "машин") → "машин".</summary>
        public static string Plural(int n, string one, string few, string many)
        {
            int a = n < 0 ? -n : n;
            int m10 = a % 10, m100 = a % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }

        public static string Count(int n, string one, string few, string many) => n + " " + Plural(n, one, few, many);

        public static string People(int n) => Count(n, "человек", "человека", "человек");
        public static string Cars(int n) => Count(n, "машина", "машины", "машин");
        public static string Routes(int n) => Count(n, "маршрут", "маршрута", "маршрутов");

        /// <summary>0.5 → "50 см", 1 → "1 м", 1.5 → "1,5 м".</summary>
        public static string Metres(float m)
        {
            float a = m < 0 ? -m : m;
            if (a < 0.995f) return ((int)(a * 100f + 0.5f)) + " см";
            float r = (float)System.Math.Round(a, 1);
            if (System.Math.Abs(r - (int)r) < 0.01f) return ((int)r) + " м";
            return r.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " м";
        }

        public static string SignedMetres(float m) => (m >= 0 ? "+" : "−") + Metres(m);
    }
}
