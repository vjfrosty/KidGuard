using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace KidGuard {
    // Parent actions shared by KidGuard.exe commands and the Parent Panel. Each one validates,
    // writes atomically and logs with a "Parent:" prefix. Callers set Log.DirectoryPath.
    // The result is shown in the parent's language (L); the log line is always English.
    public static class Parent {
        static string Done(string en, params object[] args) { Log.Write("Parent: " + L.English(en, args)); return L.T(en, args); }
        public static string Local(string utcText, Schedule s) {
            if (utcText == Overrides.Forever) return "until unlocked";
            DateTime? t = Overrides.Time(utcText);
            if (!t.HasValue) return "-";
            return TimeZoneInfo.ConvertTimeFromUtc(t.Value, s.Zone).ToString("ddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        static void CheckMinutes(int m) { if (m < 1 || m > 7 * 1440) throw new ArgumentException("Minutes must be 1-10080."); }
        // "www.ucha.se" -> ["ucha.se", "*.ucha.se"]: the site and all its subdomains. Only "www." is stripped;
        // anything broader (e.g. a parent domain) must be typed deliberately.
        public static string[] WholeSite(string host) {
            string root = host.StartsWith("www.") && host.Count(ch => ch == '.') >= 2 ? host.Substring(4) : host;
            return new[] { root, "*." + root };
        }

        // minutes == null locks until Unlock.
        public static string Lock(string admin, int? minutes) {
            if (minutes.HasValue) CheckMinutes(minutes.Value);
            string until = minutes.HasValue ? Overrides.Format(DateTime.UtcNow.AddMinutes(minutes.Value)) : Overrides.Forever;
            ConfigStore.UpdateOverrides(admin, o => o.lockUntil = until);
            return minutes.HasValue ? Done("Locked for {0} minutes.", minutes.Value) : Done("Locked until unlocked.");
        }
        public static string Unlock(string admin) {
            ConfigStore.UpdateOverrides(admin, o => o.lockUntil = null);
            // A manual lock can also be set in schedule.json ("forceLock": true); Unlock clears it too.
            var s = Schedule.Parse(File.ReadAllText(Path.Combine(admin, "schedule.json")));
            if (s.forceLock) { s.forceLock = false; ConfigStore.ApplySchedule(admin, s.ToJson()); }
            return Done("Lock removed; the normal schedule applies.");
        }
        // Extra time starts when access would otherwise end, so +30 at 19:50 (end 20:00) runs to 20:30.
        public static string Bonus(string admin, int minutes, bool internet) {
            CheckMinutes(minutes);
            DateTime now = DateTime.UtcNow;
            string ignored; Policy p = ConfigStore.Load(admin, out ignored);
            if (p.Schedule.forceLock || p.Overrides.Locked(now)) throw new InvalidOperationException("The account is locked; unlock it first.");
            DateTime start = now;
            if (p.Overrides.Bonus(now)) start = Overrides.Time(p.Overrides.bonusUntil).Value;
            else if (p.AccountAllowed(now)) { DateTime? end = p.NextChange(now, p.AccountAllowed); if (end.HasValue) start = end.Value; }
            DateTime untilUtc = start.AddMinutes(minutes);
            string until = Overrides.Format(untilUtc);
            ConfigStore.UpdateOverrides(admin, o => { bool keep = o.bonusInternet && o.Bonus(now); o.bonusUntil = until; o.bonusInternet = internet || keep; });
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(untilUtc, p.Schedule.Zone);
            return internet ? Done("Extra time until {0:ddd dd.MM HH:mm} (internet included).", local) : Done("Extra time until {0:ddd dd.MM HH:mm} (offline only).", local);
        }
        public static string PauseInternet(string admin, int minutes) {
            CheckMinutes(minutes);
            ConfigStore.UpdateOverrides(admin, o => o.internetPausedUntil = Overrides.Format(DateTime.UtcNow.AddMinutes(minutes)));
            return Done("Internet paused for {0} minutes.", minutes);
        }
        public static string ResumeInternet(string admin) {
            ConfigStore.UpdateOverrides(admin, o => o.internetPausedUntil = null);
            return Done("Internet pause removed.");
        }
        public static string ClearOverrides(string admin) {
            ConfigStore.UpdateOverrides(admin, o => { o.lockUntil = null; o.bonusUntil = null; o.bonusInternet = false; o.internetPausedUntil = null; });
            return Done("All temporary overrides removed.");
        }
        public static string Allow(string admin, string entry) {
            entry = Domains.Entry(entry);
            return ConfigStore.AllowDomain(admin, entry) ? Done("Allowed {0}.", entry) : L.T("{0} is already allowed.", entry);
        }
        public static string Disallow(string admin, string entry) {
            entry = Domains.Entry(entry);
            return ConfigStore.DisallowDomain(admin, entry) ? Done("Removed {0}.", entry) : L.T("{0} was not in the whitelist.", entry);
        }
        public static string SaveSchedule(string admin, string json) {
            ConfigStore.ApplySchedule(admin, json);
            return Done("Schedule saved.");
        }
        public static string SaveWhitelist(string admin, string text) {
            ConfigStore.ApplyWhitelist(admin, text);
            return Done("Whitelist saved.");
        }

        // Answers to what the child asked for in their helper.
        public static string AllowRequestedSite(string admin, string id, bool wholeSite) {
            var r = Requests.Waiting(admin, id);
            if (r.kind != "site") throw new InvalidOperationException("This request is not for a site.");
            var rules = wholeSite ? WholeSite(r.site) : new[] { r.site };
            foreach (string rule in rules) ConfigStore.AllowDomain(admin, rule);
            try { Blocked.Dismiss(admin, r.site); } catch { }
            Requests.Answer(admin, id, "allowed", String.Join(", ", rules));
            return Done("Allowed {0} (asked by the child).", String.Join(", ", rules));
        }
        // Rules chosen after "Check what the site needs".
        public static string AllowRules(string admin, IEnumerable<string> rules, string site) {
            var list = rules.Select(Domains.Entry).Distinct().ToList();
            int added = list.Count(rule => ConfigStore.AllowDomain(admin, rule));
            foreach (string rule in list) try { Blocked.Dismiss(admin, rule.StartsWith("*.") ? rule.Substring(2) : rule); } catch { }
            return Done("Allowed {0} after checking {1}: {2} new rule(s).", String.Join(", ", list), site, added);
        }
        public static string AllowRequestedSite(string admin, string id, IList<string> rules) {
            var r = Requests.Waiting(admin, id);
            if (r.kind != "site") throw new InvalidOperationException("This request is not for a site.");
            if (rules.Count == 0) throw new InvalidOperationException("Nothing was ticked.");
            string result = AllowRules(admin, rules, r.site);
            try { Blocked.Dismiss(admin, r.site); } catch { }
            Requests.Answer(admin, id, "allowed", String.Join(", ", rules));
            return result;
        }
        public static string GiveRequestedTime(string admin, string id, bool internet) {
            var r = Requests.Waiting(admin, id);
            if (r.kind != "time") throw new InvalidOperationException("This request is not for time.");
            string result = Bonus(admin, r.minutes, internet);
            Requests.Answer(admin, id, "allowed", "+" + r.minutes);
            return result;
        }
        public static string DenyRequest(string admin, string id) {
            var r = Requests.Waiting(admin, id);
            Requests.Answer(admin, id, "denied", null);
            return r.kind == "site" ? Done("Said no to {0}.", r.site) : Done("Said no to {0} more minutes.", r.minutes);
        }
        public static string ClearAnsweredRequests(string admin) {
            return L.T("Removed {0} answered request(s).", Requests.RemoveAnswered(admin));
        }

        public static string SetLanguage(string admin, string language) {
            Settings.Update(admin, s => s.language = language);
            L.Language = language;
            return language == "bg" ? Done("Language set to Bulgarian.") : Done("Language set to English.");
        }
        public static string SetChildHelper(string admin, bool on) {
            Settings.Update(admin, s => s.hideChildHelper = !on);
            return on ? Done("The child's helper is turned on.") : Done("The child's helper is turned off.");
        }
    }
}
