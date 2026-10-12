using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace KidGuard {
    // Admin\requests.json: what the child asked for, and the parent's answers.
    public static class Requests {
        public const int Capacity = 100, MaxWaiting = 20;
        static string PathOf(string admin) { return Path.Combine(admin, "requests.json"); }

        public static ChildRequest[] Read(string admin) {
            string path = PathOf(admin);
            if (!File.Exists(path)) return new ChildRequest[0];
            return (ChildFiles.FromJson<ChildRequest[]>(File.ReadAllText(path)) ?? new ChildRequest[0]).Where(r => r != null && r.id != null).ToArray();
        }
        static void Write(string admin, List<ChildRequest> list) {
            // Every waiting request is kept; answered ones only while there is room.
            var kept = list.Where(r => r.state == "waiting").Concat(list.Where(r => r.state != "waiting").OrderByDescending(r => r.asked, StringComparer.Ordinal))
                .Take(Capacity).OrderBy(r => r.asked, StringComparer.Ordinal).ToArray();
            ConfigStore.WriteAtomic(PathOf(admin), ChildFiles.ToJson(kept));
        }
        static void Expire(List<ChildRequest> list, DateTime now) {
            string limit = Overrides.Format(now.AddHours(-24));
            foreach (var r in list) if (r.state == "waiting" && String.CompareOrdinal(r.asked, limit) < 0) { r.state = "expired"; r.answered = Overrides.Format(now); }
        }
        // The child wrote the request file, so every field is checked here.
        public static ChildRequest Clean(ChildRequest raw, DateTime now) {
            var c = new ChildRequest { id = Guid.NewGuid().ToString("N").Substring(0, 12), asked = Overrides.Format(now), state = "waiting" };
            if (raw.kind == "site") { c.kind = "site"; c.site = Domains.HostFromInput(raw.site ?? ""); }
            else if (raw.kind == "time") {
                if (Array.IndexOf(ChildFiles.TimeChoices, raw.minutes) < 0) throw new FormatException("Unsupported number of minutes.");
                c.kind = "time"; c.minutes = raw.minutes;
            } else throw new FormatException("Unknown request kind.");
            if (!String.IsNullOrEmpty(raw.note)) {
                string note = new string(raw.note.Where(ch => !Char.IsControl(ch)).ToArray()).Trim();
                if (note.Length > ChildFiles.MaxNote) note = note.Substring(0, ChildFiles.MaxNote);
                if (note.Length > 0) c.note = note;
            }
            return c;
        }
        // Returns the number of new requests. Asking again for the same thing refreshes the waiting request.
        public static int Add(string admin, IEnumerable<ChildRequest> incoming, DateTime now) {
            return ConfigStore.WithLock(() => {
                var list = Read(admin).ToList();
                Expire(list, now);
                int added = 0;
                foreach (var raw in incoming) {
                    ChildRequest c;
                    try { c = Clean(raw, now); } catch (Exception ex) { Log.Write("Child request ignored: " + ex.Message); continue; }
                    var same = list.FirstOrDefault(x => x.state == "waiting" && x.kind == c.kind && x.site == c.site);
                    if (same != null) { same.asked = c.asked; same.minutes = c.minutes; if (c.note != null) same.note = c.note; continue; }
                    if (list.Count(x => x.state == "waiting") >= MaxWaiting) { Log.Write("Child request ignored: " + MaxWaiting + " requests are already waiting."); continue; }
                    list.Add(c); added++;
                    Log.Write(c.kind == "site" ? "Child asked for site " + c.site + "." : "Child asked for " + c.minutes + " more minutes.");
                }
                Write(admin, list);
                return added;
            });
        }
        public static ChildRequest Waiting(string admin, string id) {
            var r = Read(admin).FirstOrDefault(x => x.id == id);
            if (r == null) throw new InvalidOperationException("This request no longer exists.");
            if (r.state != "waiting") throw new InvalidOperationException("This request was already answered.");
            return r;
        }
        public static void Answer(string admin, string id, string state, string answer) {
            ConfigStore.WithLock(() => {
                var list = Read(admin).ToList();
                var r = list.FirstOrDefault(x => x.id == id);
                if (r == null) throw new InvalidOperationException("This request no longer exists.");
                if (r.state != "waiting") throw new InvalidOperationException("This request was already answered.");
                r.state = state; r.answer = answer; r.answered = Overrides.Format(DateTime.UtcNow);
                Write(admin, list);
            });
        }
        public static int RemoveAnswered(string admin) {
            return ConfigStore.WithLock(() => {
                var list = Read(admin).ToList();
                int removed = list.RemoveAll(r => r.state != "waiting");
                if (removed > 0) Write(admin, list);
                return removed;
            });
        }
        public static int WaitingCount(string admin) {
            try { return Read(admin).Count(r => r.state == "waiting"); } catch { return 0; }
        }
    }

    // A folder that SYSTEM writes into and that a standard user could have created first (anyone may create
    // folders in C:\ProgramData). A link, or a folder owned by anyone but Administrators or SYSTEM, is
    // removed or moved aside and the folder is created again with the given permissions.
    public static class SafeDir {
        static bool Trusted(string path) {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return false;
            var owner = Directory.GetAccessControl(path, AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return owner != null && (owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || owner.IsWellKnown(WellKnownSidType.LocalSystemSid));
        }
        static bool Exists(string path) { return Directory.Exists(path) || File.Exists(path); }
        public static void Ensure(string path, DirectorySecurity security) {
            for (int attempt = 0; attempt < 3; attempt++) {
                if (Exists(path)) {
                    if (Trusted(path)) return;
                    Log.Write("Untrusted folder or link at " + path + "; replacing it.");
                    var attributes = File.GetAttributes(path);
                    // Deleting a link removes only the link, never what it points to.
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path); else File.Delete(path); }
                    else if ((attributes & FileAttributes.Directory) == 0) File.Delete(path);
                    else Directory.Move(path, path + ".untrusted-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
                }
                Directory.CreateDirectory(path, security);
            }
            throw new IOException("Could not create a trusted folder at " + path + ".");
        }
        // Removes a folder KidGuard created; a link put in its place is removed without following it.
        public static void Remove(string path) {
            if (!Exists(path)) return;
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) {
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path); else File.Delete(path);
            } else Directory.Delete(path, true);
        }
        static FileSystemAccessRule Rule(WellKnownSidType who, FileSystemRights rights, InheritanceFlags inherit, PropagationFlags propagate) {
            return new FileSystemAccessRule(new SecurityIdentifier(who, null), rights, inherit, propagate, AccessControlType.Allow);
        }
        // Administrators and SYSTEM: full control. The child: read (or, for a drop box, only create files).
        public static DirectorySecurity ForChild(string childSid, bool dropBox) {
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(Rule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl, all, PropagationFlags.None));
            security.AddAccessRule(Rule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl, all, PropagationFlags.None));
            var child = new SecurityIdentifier(childSid);
            if (!dropBox) security.AddAccessRule(new FileSystemAccessRule(child, FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
            else {
                // Create files in this folder only (no listing, reading or subfolders); write to the files it created.
                security.AddAccessRule(new FileSystemAccessRule(child, FileSystemRights.CreateFiles | FileSystemRights.Synchronize, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(Rule(WellKnownSidType.CreatorOwnerSid, FileSystemRights.Write | FileSystemRights.Synchronize, InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly));
            }
            return security;
        }
    }

    // Service side of the child's helper (KidGuardTray.exe): publish status, collect requests, keep it running.
    public static class ChildHelper {
        public const string ProcessName = "KidGuardTray";
        static string sourceHash, deployedHash;
        static readonly Dictionary<int, List<DateTime>> launches = new Dictionary<int, List<DateTime>>();
        static readonly HashSet<int> gaveUp = new HashSet<int>();

        public static void Cycle(string admin, Installation target, IList<int> sessions, ServiceStatus status, Policy p, DateTime now, Settings settings, string programDir) {
            if (settings.hideChildHelper) { Stop(sessions); return; }
            SafeDir.Ensure(ChildFiles.Dir, SafeDir.ForChild(target.userSid, false));
            SafeDir.Ensure(ChildFiles.InboxDir, SafeDir.ForChild(target.userSid, true));
            Collect(admin, ChildFiles.InboxDir, now);
            ConfigStore.WriteAtomic(ChildFiles.StatusPath, ChildFiles.ToJson(StatusFor(admin, status, p, now, settings)));
            if (!Deploy(programDir)) return;
            foreach (int id in launches.Keys.Where(k => !sessions.Contains(k)).ToList()) { launches.Remove(id); gaveUp.Remove(id); }
            foreach (int session in sessions) EnsureRunning(session, now);
        }

        public static ChildStatus StatusFor(string admin, ServiceStatus s, Policy p, DateTime now, Settings settings) {
            var status = new ChildStatus {
                updated = Overrides.Format(now), language = settings.Language, zone = p.Schedule.Zone.Id,
                accountAllowed = s.accountAllowed, signOutAt = s.accountAllowed ? s.nextAccountChange : null,
                internetAllowed = s.internetAllowed, internetChangeAt = s.nextInternetChange
            };
            string since = Overrides.Format(now.AddMinutes(-30)), recent = Overrides.Format(now.AddDays(-3));
            try {
                status.recentBlocked = Blocked.Read(admin).Where(b => String.CompareOrdinal(b.lastSeen, since) >= 0)
                    .OrderByDescending(b => b.lastSeen, StringComparer.Ordinal).Select(b => b.domain).Take(12).ToArray();
            } catch { status.recentBlocked = new string[0]; }
            try {
                status.requests = Requests.Read(admin).Where(r => String.CompareOrdinal(r.asked, recent) >= 0)
                    .OrderByDescending(r => r.asked, StringComparer.Ordinal).Take(10).ToArray();
            } catch { status.requests = new ChildRequest[0]; }
            return status;
        }

        // Reads at most 10 requests per cycle and deletes every file in the drop box.
        public static int Collect(string admin, string inbox, DateTime now) {
            var incoming = new List<ChildRequest>();
            foreach (string file in Directory.GetFiles(inbox).Take(200)) {
                try {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) == 0 && incoming.Count < 10) {
                        string text = null;
                        // Fails while the helper is still writing the file; it is read in the next cycle.
                        using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                            if (s.Length <= ChildFiles.MaxRequestBytes) using (var r = new StreamReader(s, Encoding.UTF8)) text = r.ReadToEnd();
                        if (text != null) incoming.Add(ChildFiles.ParseRequest(text));
                    }
                    File.Delete(file);
                } catch (IOException) {
                } catch (Exception) { try { File.Delete(file); } catch { } }
            }
            return incoming.Count > 0 ? Requests.Add(admin, incoming, now) : 0;
        }

        static string Hash(string path) {
            using (var sha = SHA256.Create()) using (var f = File.OpenRead(path)) return Convert.ToBase64String(sha.ComputeHash(f));
        }
        // Copies the helper next to the status file (the child cannot read the KidGuard program folder).
        static bool Deploy(string programDir) {
            string source = Path.Combine(programDir, ProcessName + ".exe");
            if (!File.Exists(source)) return false;
            if (sourceHash == null) sourceHash = Hash(source);
            if (deployedHash == sourceHash && File.Exists(ChildFiles.HelperPath)) return true;
            if (!File.Exists(ChildFiles.HelperPath) || Hash(ChildFiles.HelperPath) != sourceHash) {
                StopAll();
                File.Copy(source, ChildFiles.HelperPath, true);
                Log.Write("Child helper copied to " + ChildFiles.HelperPath + ".");
            }
            deployedHash = sourceHash;
            return true;
        }
        static void EnsureRunning(int session, DateTime now) {
            if (Running(session) || gaveUp.Contains(session)) return;
            List<DateTime> times;
            if (!launches.TryGetValue(session, out times)) launches[session] = times = new List<DateTime>();
            times.RemoveAll(t => (now - t).TotalMinutes > 10);
            if (times.Count >= 3) { gaveUp.Add(session); Log.Write("Child helper keeps closing in session " + session + "; it starts again at the next sign-in."); return; }
            times.Add(now);
            Account.StartInSession(session, ChildFiles.HelperPath, ChildFiles.Dir);
            Log.Write("Child helper started in session " + session + ".");
        }
        static bool Running(int session) {
            bool found = false;
            foreach (var p in Process.GetProcessesByName(ProcessName)) { if (p.SessionId == session) found = true; p.Dispose(); }
            return found;
        }
        static void Kill(Func<Process, bool> which) {
            foreach (var p in Process.GetProcessesByName(ProcessName)) {
                try { if (which(p)) { p.Kill(); p.WaitForExit(3000); } } catch { } finally { p.Dispose(); }
            }
        }
        public static void Stop(IList<int> sessions) { Kill(p => sessions.Contains(p.SessionId)); }
        public static void StopAll() { Kill(p => true); }
        // Uninstall: close the helper everywhere and remove the child's folder.
        public static void Remove() {
            StopAll();
            SafeDir.Remove(ChildFiles.Dir);
            sourceHash = deployedHash = null;
        }
    }
}
