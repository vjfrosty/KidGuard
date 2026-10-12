using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace KidGuard {
    // Every configuration write goes through here: validate first, then replace atomically.
    // Readers never see a half-written file, and a bad edit never reaches the live folder.
    public static class ConfigStore {
        public static string LastGoodDir(string admin) { return Path.Combine(admin, "lastgood"); }

        public static T WithLock<T>(Func<T> action) {
            using (var mutex = new Mutex(false, "Global\\KidGuardConfig")) {
                bool held = false;
                try {
                    try { held = mutex.WaitOne(10000); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new TimeoutException("Configuration is busy; retry.");
                    return action();
                } finally { if (held) mutex.ReleaseMutex(); }
            }
        }
        public static void WithLock(System.Action action) { WithLock<bool>(() => { action(); return true; }); }

        public static void WriteAtomic(string path, string text) {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        // Live policy, or the last policy that loaded successfully if the live files are broken.
        // error is null when the live files are valid. Throws only if neither is usable (fail closed).
        public static Policy Load(string admin, out string error) {
            try { error = null; return Policy.Read(admin); }
            catch (Exception live) {
                error = live.Message;
                try { return Policy.Read(LastGoodDir(admin)); }
                catch (Exception old) { throw new Exception("Live configuration invalid (" + live.Message + ") and no last valid copy (" + old.Message + ")."); }
            }
        }
        public static void SaveLastGood(string admin, Policy p) {
            string dir = LastGoodDir(admin);
            WithLock(() => {
                Directory.CreateDirectory(dir);
                WriteAtomic(Path.Combine(dir, "schedule.json"), p.ScheduleText);
                WriteAtomic(Path.Combine(dir, "whitelist.txt"), p.WhitelistText);
                WriteAtomic(Path.Combine(dir, "overrides.json"), p.OverridesText);
            });
        }

        public static void ApplySchedule(string admin, string text) {
            Schedule.Parse(text);
            WithLock(() => WriteAtomic(Path.Combine(admin, "schedule.json"), text));
        }
        public static void ApplyWhitelist(string admin, string text) {
            new Domains(text);
            WithLock(() => WriteAtomic(Path.Combine(admin, "whitelist.txt"), text));
        }
        public static Overrides ReadOverrides(string admin) {
            string path = Path.Combine(admin, "overrides.json");
            return Overrides.Parse(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        public static Overrides UpdateOverrides(string admin, Action<Overrides> change) {
            return WithLock(() => {
                var o = ReadOverrides(admin);
                change(o); o.Prune(DateTime.UtcNow);
                string json = o.ToJson(); Overrides.Parse(json);
                WriteAtomic(Path.Combine(admin, "overrides.json"), json);
                return o;
            });
        }
        // Adds "host" or "*.host"; returns false if the exact entry is already present.
        public static bool AllowDomain(string admin, string entry) {
            entry = Domains.Entry(entry);
            return WithLock(() => {
                string path = Path.Combine(admin, "whitelist.txt");
                string text = File.ReadAllText(path);
                if (Lines(text).Any(x => x == entry)) return false;
                if (text.Length > 0 && !text.EndsWith("\n")) text += Environment.NewLine;
                text += entry + Environment.NewLine;
                new Domains(text);
                WriteAtomic(path, text);
                return true;
            });
        }
        public static bool DisallowDomain(string admin, string entry) {
            entry = Domains.Entry(entry);
            return WithLock(() => {
                string path = Path.Combine(admin, "whitelist.txt");
                string[] raw = File.ReadAllText(path).Split('\n');
                var kept = raw.Where(x => { string t = x.Trim(); return t.Length == 0 || t.StartsWith("#") || CanonicalOrNull(t) != entry; }).ToArray();
                if (kept.Length == raw.Length) return false;
                WriteAtomic(path, String.Join("\n", kept));
                return true;
            });
        }
        static string CanonicalOrNull(string line) { try { return Domains.Entry(line); } catch { return null; } }
        static IEnumerable<string> Lines(string text) {
            return text.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#")).Select(CanonicalOrNull);
        }
    }

    [DataContract] public class BlockedEntry {
        [DataMember] public string domain;
        [DataMember] public int count;
        [DataMember] public string firstSeen;
        [DataMember] public string lastSeen;
    }
    // Denied hostnames, for the parent to review and allow. The proxy records into memory;
    // the service merges into Admin\blocked.json under the config lock, so admin tools can dismiss safely.
    public static class Blocked {
        public const int Capacity = 500;
        static readonly object Sync = new object();
        static Dictionary<string, BlockedEntry> pending = new Dictionary<string, BlockedEntry>(StringComparer.OrdinalIgnoreCase);
        static string FilePath(string admin) { return Path.Combine(admin, "blocked.json"); }

        public static void Record(string host) {
            string now = Overrides.Format(DateTime.UtcNow);
            lock (Sync) {
                BlockedEntry e;
                if (!pending.TryGetValue(host, out e)) pending[host] = e = new BlockedEntry { domain = host, firstSeen = now };
                e.count++; e.lastSeen = now;
            }
        }
        public static BlockedEntry[] Read(string admin) {
            string path = FilePath(admin);
            if (!File.Exists(path)) return new BlockedEntry[0];
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                return (BlockedEntry[])new DataContractJsonSerializer(typeof(BlockedEntry[])).ReadObject(ms) ?? new BlockedEntry[0];
        }
        static void Write(string admin, IEnumerable<BlockedEntry> entries) {
            using (var ms = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(BlockedEntry[])).WriteObject(ms, entries.ToArray());
                ConfigStore.WriteAtomic(FilePath(admin), Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        // Merge pending denials and drop entries the whitelist now allows.
        public static void Flush(string admin, Domains allowed) {
            Dictionary<string, BlockedEntry> batch;
            lock (Sync) { batch = pending; pending = new Dictionary<string, BlockedEntry>(StringComparer.OrdinalIgnoreCase); }
            try {
                ConfigStore.WithLock(() => {
                    BlockedEntry[] existing;
                    try { existing = Read(admin); } catch { existing = new BlockedEntry[0]; }
                    var merged = existing.Where(x => x != null && x.domain != null)
                        .ToDictionary(x => x.domain, StringComparer.OrdinalIgnoreCase);
                    foreach (var e in batch.Values) {
                        BlockedEntry old;
                        if (merged.TryGetValue(e.domain, out old)) { old.count += e.count; old.lastSeen = e.lastSeen; }
                        else merged[e.domain] = e;
                    }
                    var kept = merged.Values.Where(x => !allowed.Allows(x.domain))
                        .OrderByDescending(x => x.lastSeen, StringComparer.Ordinal).Take(Capacity).ToList();
                    if (batch.Count > 0 || kept.Count != existing.Length) Write(admin, kept);
                });
            } catch {
                // Keep the batch for the next attempt.
                lock (Sync) foreach (var e in batch.Values) {
                    BlockedEntry cur;
                    if (pending.TryGetValue(e.domain, out cur)) { cur.count += e.count; cur.firstSeen = e.firstSeen; }
                    else pending[e.domain] = e;
                }
                throw;
            }
        }
        public static bool Dismiss(string admin, string domain) {
            domain = Domains.Normalize(domain);
            return ConfigStore.WithLock(() => {
                var all = Read(admin);
                var kept = all.Where(x => !String.Equals(x.domain, domain, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (kept.Length == all.Length) return false;
                Write(admin, kept); return true;
            });
        }
    }
}
