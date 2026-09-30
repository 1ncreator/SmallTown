using System;
using System.Collections.Generic;
using System.Globalization;
using SmallTown.Utils;

namespace SmallTown.Commands
{
    /// <summary>One vocabulary entry: category + id + the word sequence that triggers it.</summary>
    public sealed class Lexeme
    {
        public string Category;
        public string Id;
        public string[] Words;
        public bool[] Exact;
        public int Weight;
    }

    /// <summary>
    /// Data-driven vocabulary for the command interpreter, loaded from JSON
    /// (Assets/_Game/Configs/CommandGrammar.json) so new synonyms need no code changes.
    /// </summary>
    public sealed class CommandGrammar
    {
        public readonly List<Lexeme> Lexemes = new List<Lexeme>();
        public readonly List<string[]> Connectors = new List<string[]>();

        public static CommandGrammar FromJson(string json)
        {
            var g = new CommandGrammar();
            var root = MiniJson.Parse(json);
            foreach (var c in MiniJson.StrList(root, "connectors"))
                g.Connectors.Add(Split(Normalize(c)));
            g.Connectors.Sort((a, b) => b.Length.CompareTo(a.Length));
            var lex = MiniJson.Obj(root, "lexicon");
            if (lex == null) throw new FormatException("Grammar has no 'lexicon'");
            foreach (var cat in lex)
            {
                if (!(cat.Value is Dictionary<string, object> ids)) continue;
                foreach (var id in ids)
                {
                    if (!(id.Value is List<object> words)) continue;
                    foreach (var w in words)
                        if (w is string s) g.Add(cat.Key, id.Key, s);
                }
            }
            return g;
        }

        public void Add(string category, string id, string phrase)
        {
            var parts = Split(Normalize(phrase.TrimStart('=')));
            if (parts.Length == 0) return;
            bool forceExact = phrase.StartsWith("=", StringComparison.Ordinal);
            var lx = new Lexeme { Category = category, Id = id, Words = parts, Exact = new bool[parts.Length] };
            for (int i = 0; i < parts.Length; i++)
            {
                lx.Exact[i] = forceExact || parts[i].Length <= 2;
                lx.Weight += parts[i].Length;
            }
            Lexemes.Add(lx);
        }

        public static string Normalize(string s)
        {
            if (s == null) return "";
            s = s.ToLowerInvariant().Replace('ё', 'е').Replace('—', ' ').Replace('–', ' ').Replace('«', ' ').Replace('»', ' ').Replace('"', ' ');
            return s.Trim();
        }

        private static string[] Split(string s) => s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        public static bool WordMatches(string token, string word, bool exact)
        {
            if (exact) return token == word;
            return token.StartsWith(word, StringComparison.Ordinal);
        }

        /// <summary>
        /// Splits text into tokens: words, numbers (1.5 / 1,5), clock times (18:30) and commas.
        /// Digits glued to letters are separated ("50см" → "50", "см").
        /// </summary>
        public static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            string s = Normalize(text);
            int i = 0;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsDigit(c))
                {
                    sb.Clear();
                    while (i < s.Length)
                    {
                        char d = s[i];
                        if (char.IsDigit(d)) { sb.Append(d); i++; continue; }
                        if ((d == '.' || d == ',' || d == ':') && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                        {
                            sb.Append(d == ',' ? '.' : d);
                            i++;
                            continue;
                        }
                        break;
                    }
                    result.Add(sb.ToString());
                    continue;
                }
                if (char.IsLetter(c) || c == '\'')
                {
                    sb.Clear();
                    while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '\'' || s[i] == '-' || (s[i] == '.' && i + 1 < s.Length && char.IsLetter(s[i + 1]) && sb.Length == 1)))
                    {
                        sb.Append(s[i]);
                        i++;
                    }
                    string w = sb.ToString().Trim('-', '\'');
                    if (w.Length > 0) result.Add(w);
                    continue;
                }
                if (c == ',' || c == ';') result.Add(",");
                i++;
            }
            return result;
        }

        public static bool TryNumber(string token, out float value)
        {
            value = 0f;
            if (token.Length == 0 || !char.IsDigit(token[0]) || token.Contains(":")) return false;
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryClock(string token, out float hour)
        {
            hour = -1f;
            int c = token.IndexOf(':');
            if (c <= 0) return false;
            if (!int.TryParse(token.Substring(0, c), out int h) || !int.TryParse(token.Substring(c + 1), out int m)) return false;
            if (h < 0 || h > 24 || m < 0 || m > 59) return false;
            hour = (h % 24) + m / 60f;
            return true;
        }
    }
}
