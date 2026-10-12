using System;
using System.Collections.Generic;

namespace KidGuard {
    // A named group of whitelist entries offered by the Parent Panel (Admin\bundles.txt).
    // Format: "[Category | Site name]" starts a site; following lines are whitelist entries; "#" lines are comments.
    public class Bundle {
        public string Category, Name;
        public readonly List<string> Entries = new List<string>();
        public static List<Bundle> Parse(string text) {
            var list = new List<Bundle>();
            Bundle current = null;
            int number = 0;
            foreach (string raw in text.Split('\n')) {
                number++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[")) {
                    if (!line.EndsWith("]")) throw new FormatException("Line " + number + ": expected [Category | Site name].");
                    string[] parts = line.Substring(1, line.Length - 2).Split('|');
                    if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
                        throw new FormatException("Line " + number + ": expected [Category | Site name].");
                    current = new Bundle { Category = parts[0].Trim(), Name = parts[1].Trim() };
                    list.Add(current);
                    continue;
                }
                if (current == null) throw new FormatException("Line " + number + ": entry before the first [Category | Site name].");
                try { current.Entries.Add(Domains.Entry(line)); }
                catch (FormatException ex) { throw new FormatException("Line " + number + ": " + ex.Message); }
            }
            foreach (var b in list) if (b.Entries.Count == 0) throw new FormatException("Site '" + b.Name + "' has no entries.");
            return list;
        }
    }
}
