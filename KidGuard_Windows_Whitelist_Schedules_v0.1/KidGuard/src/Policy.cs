using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KidGuard {
    [DataContract] public class Window {
        [DataMember(IsRequired=true)] public string[] days;
        [DataMember(IsRequired=true)] public string start;
        [DataMember(IsRequired=true)] public string end;
        // Parsed once; the next-change scan evaluates windows thousands of times.
        bool parsed; int a, b, dayMask;
        public static int Minute(string value, bool endPoint) {
            if (endPoint && value == "24:00") return 1440;
            DateTime d;
            if (!DateTime.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                throw new FormatException("Use HH:mm (24:00 is allowed only as an end).");
            return d.Hour * 60 + d.Minute;
        }
        public void Validate() {
            if (days == null || days.Length == 0 || days.Any(x => !Enum.GetNames(typeof(DayOfWeek)).Contains(x)))
                throw new FormatException("Use full English day names, such as Monday.");
            int s = Minute(start, false), e = Minute(end, true);
            if (s == e) throw new FormatException("Equal start/end is ambiguous; use 00:00-24:00 for all day.");
            int mask = 0;
            foreach (string x in days) mask |= 1 << (int)Enum.Parse(typeof(DayOfWeek), x);
            a = s; b = e; dayMask = mask; parsed = true;
        }
        bool Has(DayOfWeek d) { return (dayMask & (1 << (int)d)) != 0; }
        public bool Contains(DateTime local) {
            if (!parsed) Validate();
            int m = local.Hour * 60 + local.Minute;
            if (a < b) return Has(local.DayOfWeek) && m >= a && m < b;
            return (Has(local.DayOfWeek) && m >= a) || (Has(local.AddDays(-1).DayOfWeek) && m < b);
        }
    }
    [DataContract] public class Schedule {
        [DataMember(IsRequired=true)] public string timeZone;
        [DataMember(IsRequired=true)] public bool forceLock;
        [DataMember(IsRequired=true)] public Window[] accountHours;
        [DataMember(IsRequired=true)] public Window[] internetHours;
        public TimeZoneInfo Zone;
        public static Schedule Parse(string json) {
            Schedule s;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                s = (Schedule)new DataContractJsonSerializer(typeof(Schedule)).ReadObject(ms);
            if (s == null || s.accountHours == null || s.internetHours == null) throw new FormatException("Missing schedule arrays.");
            s.Zone = TimeZoneInfo.FindSystemTimeZoneById(s.timeZone);
            foreach (var w in s.accountHours.Concat(s.internetHours)) { if (w == null) throw new FormatException("Null window."); w.Validate(); }
            return s;
        }
        public bool AccountWindow(DateTime utc) {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
            return accountHours.Any(w => w.Contains(local));
        }
        public bool InternetWindow(DateTime utc) {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
            return internetHours.Any(w => w.Contains(local));
        }
        public bool AccountAllowed(DateTime utc) { return !forceLock && AccountWindow(utc); }
        public bool InternetAllowed(DateTime utc) { return AccountAllowed(utc) && InternetWindow(utc); }
        // Hand-editable layout matching config\schedule.json: one window per line.
        public string ToJson() {
            var sb = new StringBuilder();
            sb.Append("{\n  \"timeZone\": \"").Append(Escape(timeZone)).Append("\",\n");
            sb.Append("  \"forceLock\": ").Append(forceLock ? "true" : "false").Append(",\n");
            AppendWindows(sb, "accountHours", accountHours); sb.Append(",\n");
            AppendWindows(sb, "internetHours", internetHours); sb.Append("\n}\n");
            return sb.ToString();
        }
        static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }
        static void AppendWindows(StringBuilder sb, string name, Window[] windows) {
            sb.Append("  \"").Append(name).Append("\": [");
            for (int i = 0; i < windows.Length; i++) {
                var w = windows[i];
                sb.Append(i == 0 ? "\n" : ",\n").Append("    { \"days\": [")
                  .Append(String.Join(", ", w.days.Select(d => "\"" + Escape(d) + "\"")))
                  .Append("], \"start\": \"").Append(w.start).Append("\", \"end\": \"").Append(w.end).Append("\" }");
            }
            sb.Append(windows.Length > 0 ? "\n  ]" : "]");
        }
    }
    // Converts between schedule windows and a Monday-first week of fixed-size slots, for the Parent Panel grid.
    public static class WeekGrid {
        public static readonly string[] Days = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        static readonly DateTime ReferenceMonday = new DateTime(2026, 10, 12);
        public static bool[,] FromWindows(Window[] windows, int slotMinutes) {
            int slots = 1440 / slotMinutes;
            var grid = new bool[7, slots];
            for (int d = 0; d < 7; d++)
                for (int s = 0; s < slots; s++) {
                    DateTime t = ReferenceMonday.AddDays(d).AddMinutes(s * slotMinutes);
                    grid[d, s] = windows.Any(w => w.Contains(t));
                }
            return grid;
        }
        // True when the grid represents the windows exactly, minute by minute.
        public static bool Exact(Window[] windows, int slotMinutes) {
            var grid = FromWindows(windows, slotMinutes);
            for (int m = 0; m < 7 * 1440; m++) {
                DateTime t = ReferenceMonday.AddMinutes(m);
                if (grid[m / 1440, (m % 1440) / slotMinutes] != windows.Any(w => w.Contains(t))) return false;
            }
            return true;
        }
        static string Clock(int minute) { return minute == 1440 ? "24:00" : (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00"); }
        // One window per distinct time range, listing every day that has it. Runs that cross midnight stay split.
        public static Window[] ToWindows(bool[,] grid, int slotMinutes) {
            var order = new List<string>();
            var days = new Dictionary<string, List<string>>();
            int slots = grid.GetLength(1);
            for (int d = 0; d < 7; d++)
                for (int s = 0; s < slots; ) {
                    if (!grid[d, s]) { s++; continue; }
                    int e = s; while (e < slots && grid[d, e]) e++;
                    string key = Clock(s * slotMinutes) + "-" + Clock(e * slotMinutes);
                    if (!days.ContainsKey(key)) { days[key] = new List<string>(); order.Add(key); }
                    days[key].Add(Days[d]);
                    s = e;
                }
            return order.OrderBy(k => k, StringComparer.Ordinal).Select(k => {
                var w = new Window { days = days[k].ToArray(), start = k.Substring(0, 5), end = k.Substring(6) };
                w.Validate(); return w;
            }).ToArray();
        }
    }
    // Temporary parent decisions, kept apart from the recurring timetable. All times are UTC.
    [DataContract] public class Overrides {
        public const string Forever = "9999-12-31T23:59:59Z";
        [DataMember(EmitDefaultValue=false)] public string lockUntil;
        [DataMember(EmitDefaultValue=false)] public string bonusUntil;
        [DataMember(EmitDefaultValue=false)] public bool bonusInternet;
        [DataMember(EmitDefaultValue=false)] public string internetPausedUntil;
        public static string Format(DateTime utc) { return utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture); }
        public static DateTime? Time(string value) {
            if (value == null) return null;
            DateTime d;
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d))
                throw new FormatException("Override times use yyyy-MM-ddTHH:mm:ssZ (UTC): " + value);
            return d;
        }
        public static Overrides Parse(string json) {
            if (String.IsNullOrWhiteSpace(json)) return new Overrides();
            Overrides o;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                o = (Overrides)new DataContractJsonSerializer(typeof(Overrides)).ReadObject(ms);
            if (o == null) throw new FormatException("Invalid overrides.");
            Time(o.lockUntil); Time(o.bonusUntil); Time(o.internetPausedUntil);
            return o;
        }
        public string ToJson() {
            using (var ms = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(Overrides)).WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
        // Parsed-time cache: the next-change scan calls these thousands of times per cycle.
        Dictionary<string, DateTime?> parsed;
        bool Before(DateTime utc, string until) {
            if (until == null) return false;
            DateTime? t;
            // Shared with proxy threads; DataContract skips field initializers, so lock the instance.
            lock (this) {
                if (parsed == null) parsed = new Dictionary<string, DateTime?>();
                if (!parsed.TryGetValue(until, out t)) parsed[until] = t = Time(until);
            }
            return t.HasValue && utc < t.Value;
        }
        public bool Locked(DateTime utc) { return Before(utc, lockUntil); }
        public bool Bonus(DateTime utc) { return Before(utc, bonusUntil); }
        public bool InternetPaused(DateTime utc) { return Before(utc, internetPausedUntil); }
        public IEnumerable<DateTime> Expiries() {
            foreach (string s in new[] { lockUntil, bonusUntil, internetPausedUntil }) { DateTime? t = Time(s); if (t.HasValue) yield return t.Value; }
        }
        // Drop expired entries so the file only describes what is still in force.
        public void Prune(DateTime utc) {
            if (!Locked(utc)) lockUntil = null;
            if (!Bonus(utc)) { bonusUntil = null; bonusInternet = false; }
            if (!InternetPaused(utc)) internetPausedUntil = null;
        }
    }
    public class Domains {
        readonly HashSet<string> exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> suffix = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static string Normalize(string value) {
            if (String.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(c => c > 127 || Char.IsControl(c)))
                throw new FormatException("Use ASCII/punycode domains only.");
            string host = value.TrimEnd('.').ToLowerInvariant();
            IPAddress ip;
            if (host.Length > 253 || !host.Contains('.') || IPAddress.TryParse(host, out ip)) throw new FormatException("A domain, not an IP address, is required.");
            foreach (string label in host.Split('.')) {
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length-1] == '-' ||
                    label.Any(c => !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-'))
                    throw new FormatException("Invalid domain: " + value);
            }
            return host;
        }
        // Accepts what a parent might paste: "https://www.site.org/page?x", "site.org:443", "www.site.org/".
        public static string HostFromInput(string input) {
            string s = (input ?? "").Trim();
            Uri uri;
            if (s.Contains("://") && Uri.TryCreate(s, UriKind.Absolute, out uri)) s = uri.Host;
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            int colon = s.LastIndexOf(':');
            if (colon > 0 && s.IndexOf(':') == colon) s = s.Substring(0, colon);
            return Normalize(s);
        }
        // Shared "client-facing" names of Encrypted ClientHello providers. Allowing one would let any site
        // behind that provider through, so they are refused as whitelist entries.
        public static readonly string[] EchPublicNames = { "cloudflare-ech.com" };
        // Canonical whitelist line for an entry: "host" or "*.host".
        public static string Entry(string line) {
            line = line.Trim();
            bool wild = line.StartsWith("*.");
            string host = Normalize(wild ? line.Substring(2) : line);
            foreach (string ech in EchPublicNames)
                if (host == ech || host.EndsWith("." + ech) || (wild && ech.EndsWith("." + host)))
                    throw new FormatException(line + " cannot be allowed: it is a shared encrypted-connection front that would let other sites through.");
            return wild ? "*." + host : host;
        }
        public Domains(string text) {
            foreach (string raw in text.Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string entry = Entry(line);
                if (entry.StartsWith("*.")) suffix.Add(entry.Substring(2)); else exact.Add(entry);
            }
        }
        // Always allowed, whatever the whitelist says. Google Search (www.google.com) is NOT among them.
        // - Google Family Link: a Chrome profile supervised by Family Link asks kidsmanagement-pa about every page before
        //   showing it, with a sign-in token from accounts.google.com / oauth2 / www.googleapis.com. When these are blocked,
        //   Chrome waits and retries, so every site loads very slowly. They answer yes/no and tokens; no pages to browse.
        // - Google static files and fonts (www.gstatic.com, Google Fonts): most sites use them; they hold no pages.
        public static readonly string[] BrowserServices = {
            "kidsmanagement-pa.googleapis.com", "accounts.google.com", "oauth2.googleapis.com", "www.googleapis.com",
            "fonts.googleapis.com", "fonts.gstatic.com", "www.gstatic.com"
        };
        public bool Allows(string host) {
            try { host = Normalize(host); } catch { return false; }
            return exact.Contains(host) || BrowserServices.Contains(host) || suffix.Any(x => host.EndsWith("." + x, StringComparison.OrdinalIgnoreCase));
        }
    }
    public class Policy {
        public readonly Schedule Schedule;
        public readonly Domains Domains;
        public readonly Overrides Overrides;
        // Revision covers schedule+whitelist (a change closes every tunnel); FullRevision also covers overrides.
        public readonly string Revision, FullRevision;
        public readonly string ScheduleText, WhitelistText, OverridesText;
        public Policy(string schedule, string whitelist) : this(schedule, whitelist, null) { }
        public Policy(string schedule, string whitelist, string overrides) {
            ScheduleText = schedule; WhitelistText = whitelist; OverridesText = overrides ?? "";
            Schedule = Schedule.Parse(schedule); Domains = new Domains(whitelist); Overrides = Overrides.Parse(OverridesText);
            Revision = Hash(schedule + "\0" + whitelist);
            FullRevision = Hash(schedule + "\0" + whitelist + "\0" + OverridesText);
        }
        static string Hash(string text) {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        public static Policy Read(string dir) {
            string o = Path.Combine(dir, "overrides.json");
            return new Policy(File.ReadAllText(Path.Combine(dir, "schedule.json")), File.ReadAllText(Path.Combine(dir, "whitelist.txt")),
                File.Exists(o) ? File.ReadAllText(o) : null);
        }
        // A lock (manual or timed) beats everything; bonus time extends beyond the timetable.
        public bool AccountAllowed(DateTime utc) {
            if (Schedule.forceLock || Overrides.Locked(utc)) return false;
            return Schedule.AccountWindow(utc) || Overrides.Bonus(utc);
        }
        public bool InternetAllowed(DateTime utc) {
            if (!AccountAllowed(utc) || Overrides.InternetPaused(utc)) return false;
            return Schedule.InternetWindow(utc) || (Overrides.bonusInternet && Overrides.Bonus(utc));
        }
        // First instant after utc where state() flips, or null if none within the horizon.
        // Schedules change on whole minutes; overrides can expire at any second.
        public DateTime? NextChange(DateTime utc, Func<DateTime, bool> state, int horizonMinutes) {
            bool now = state(utc);
            var expiries = Overrides.Expiries().Where(x => x > utc).OrderBy(x => x).ToList();
            DateTime t = new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc).AddMinutes(1);
            DateTime end = utc.AddMinutes(horizonMinutes);
            int i = 0;
            for (; t <= end; t = t.AddMinutes(1)) {
                for (; i < expiries.Count && expiries[i] <= t; i++) if (state(expiries[i]) != now) return expiries[i];
                if (state(t) != now) return t;
            }
            return null;
        }
        public DateTime? NextChange(DateTime utc, Func<DateTime, bool> state) { return NextChange(utc, state, 8 * 1440); }
    }
}
