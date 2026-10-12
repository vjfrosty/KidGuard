using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KidGuard {
    // User-facing text in English or Bulgarian. The English text is the key: L.T("Save") returns the
    // Bulgarian line from lang\bg.txt (embedded as the resource KidGuard.bg.txt) or the English text itself.
    // Compiled into KidGuard.exe, the child's helper and the setup wizard. Log lines stay in English.
    public static class L {
        static Dictionary<string, string> bg;
        static string language = "en";
        public static string Language {
            get { return language; }
            set { language = value == "bg" ? "bg" : "en"; }
        }
        public static bool Bulgarian { get { return language == "bg"; } }
        // Windows display language decides when nothing is chosen.
        public static string Default { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bg" ? "bg" : "en"; } }
        public static CultureInfo Culture { get { return Bulgarian ? CultureInfo.GetCultureInfo("bg-BG") : CultureInfo.InvariantCulture; } }

        static Dictionary<string, string> Table {
            get {
                if (bg == null) {
                    var table = new Dictionary<string, string>(StringComparer.Ordinal);
                    try {
                        using (var s = typeof(L).Assembly.GetManifestResourceStream("KidGuard.bg.txt"))
                            if (s != null) using (var r = new StreamReader(s, Encoding.UTF8)) Load(r.ReadToEnd(), table);
                    } catch { }
                    bg = table;
                }
                return bg;
            }
        }
        // Pairs of lines: "en: English text" then "bg: Bulgarian text". "\n" stands for a line break; # starts a comment.
        public static void Load(string text, Dictionary<string, string> table) {
            string en = null;
            foreach (string raw in text.Split('\n')) {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("en: ")) en = line.Substring(4).Replace("\\n", "\n");
                else if (line.StartsWith("bg: ") && en != null) { table[en] = line.Substring(4).Replace("\\n", "\n"); en = null; }
            }
        }
        public static bool Has(string en) { return Table.ContainsKey(en); }
        public static IEnumerable<KeyValuePair<string, string>> Pairs { get { return Table; } }
        public static string T(string en) {
            if (!Bulgarian || en == null) return en;
            string text;
            return Table.TryGetValue(en, out text) ? text : en;
        }
        public static string T(string en, params object[] args) { return String.Format(Culture, T(en), args); }
        // The same sentence in English, for the log.
        public static string English(string en, params object[] args) { return String.Format(CultureInfo.InvariantCulture, en, args); }

        public static string Minutes(int n) { return n == 1 ? T("1 minute") : T("{0} minutes", n); }
        // "1 h 25 min", "45 min", "2 days".
        public static string Duration(TimeSpan span) {
            int total = (int)Math.Ceiling(Math.Max(0, span.TotalMinutes));
            if (total >= 2 * 1440) return T("{0} days", total / 1440);
            if (total >= 60) return total % 60 == 0 ? T("{0} h", total / 60) : T("{0} h {1} min", total / 60, total % 60);
            return T("{0} min", total);
        }
    }
}
