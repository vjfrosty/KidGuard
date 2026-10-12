using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace KidGuard {
    [DataContract] public class StartPageSettings {
        [DataMember] public bool enabled;
    }

    // The child's start page: an HTML page of links (Admin\startpage.html) placed on the child's desktop.
    // Parent side: import a page, allow every site it links to, turn it on/off.
    // Service side (SYSTEM): copy the page to a folder only the child and administrators can read, and keep a
    // shortcut on the child's real desktop (which may be redirected to OneDrive) while the child is signed in.
    public static class StartPage {
        public static string PublicDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KidGuard-StartPage"); } }
        public static string SourcePath(string admin) { return Path.Combine(admin, "startpage.html"); }
        static string SettingsPath(string admin) { return Path.Combine(admin, "startpage.json"); }
        static string RecordPath(string admin) { return Path.Combine(admin, "startpage-deployed.txt"); }

        public static List<string> LinkHosts(string html) {
            var hosts = new List<string>();
            foreach (Match m in Regex.Matches(html, "<a\\b[^>]*?\\bhref\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)) {
                string href = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                try { string host = Domains.HostFromInput(href); if (!hosts.Contains(host)) hosts.Add(host); } catch (FormatException) { }
            }
            return hosts;
        }
        // The page's web links (http/https), each once, in page order.
        public static List<string> Links(string html) {
            var links = new List<string>();
            foreach (Match m in Regex.Matches(html, "<a\\b[^>]*?\\bhref\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)) {
                string href = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                if ((href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) && !links.Contains(href)) links.Add(href);
            }
            return links;
        }
        public static string Title(string html) {
            var m = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            string t = m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";
            return t.Length > 0 ? t : "KidGuard start page";
        }
        // "Viktor's internet (middle dot) Sites for age 9" -> "Viktor's internet"; characters Windows forbids in file names are removed.
        public static string ShortcutName(string title) {
            string name = title.Split(new[] { '\u00B7', '|' }, 2)[0];
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) name = name.Substring(0, dash);
            name = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
            if (name.Length > 60) name = name.Substring(0, 60).Trim();
            return (name.Length > 0 ? name : "KidGuard start page") + ".url";
        }
        // Each linked host, plus every rule of a site-list bundle that covers it (sites usually need more than one address).
        public static List<string> Rules(IEnumerable<string> hosts, IEnumerable<Bundle> bundles) {
            var rules = new List<string>();
            var list = bundles.ToList();
            foreach (string host in hosts) {
                if (!rules.Contains(host)) rules.Add(host);
                foreach (var b in list)
                    if (new Domains(String.Join("\n", b.Entries)).Allows(host))
                        foreach (string e in b.Entries) if (!rules.Contains(e)) rules.Add(e);
            }
            return rules;
        }
        static List<Bundle> Bundles(string admin) {
            string path = Path.Combine(admin, "bundles.txt");
            try { return File.Exists(path) ? Bundle.Parse(File.ReadAllText(path)) : new List<Bundle>(); } catch (FormatException) { return new List<Bundle>(); }
        }

        public static StartPageSettings ReadSettings(string admin) {
            string path = SettingsPath(admin);
            if (!File.Exists(path)) return new StartPageSettings();
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                return (StartPageSettings)new DataContractJsonSerializer(typeof(StartPageSettings)).ReadObject(ms);
        }
        static void WriteSettings(string admin, bool enabled) {
            using (var ms = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(StartPageSettings)).WriteObject(ms, new StartPageSettings { enabled = enabled });
                string json = Encoding.UTF8.GetString(ms.ToArray());
                ConfigStore.WithLock(() => ConfigStore.WriteAtomic(SettingsPath(admin), json));
            }
        }

        // Parent actions (logged with "Parent:").
        public static string Import(string admin, string file) {
            if (new FileInfo(file).Length > 5 * 1024 * 1024) throw new FormatException("The page is larger than 5 MB.");
            string html = File.ReadAllText(file);
            if (LinkHosts(html).Count == 0) throw new FormatException("The page contains no http:// or https:// links.");
            ConfigStore.WithLock(() => ConfigStore.WriteAtomic(SourcePath(admin), html));
            Log.Write("Parent: Start page imported: " + Title(html) + ".");
            return L.T("Start page \"{0}\" imported with {1} linked sites.", Title(html), LinkHosts(html).Count);
        }
        public static string AllowLinks(string admin) {
            string html = File.ReadAllText(SourcePath(admin));
            int added = 0;
            foreach (string rule in Rules(LinkHosts(html), Bundles(admin))) if (ConfigStore.AllowDomain(admin, rule)) added++;
            Log.Write("Parent: Allowed the start page's sites (" + added + " new rules).");
            return L.T("Allowed the start page's sites: {0} new rule(s).", added);
        }
        public static string Enable(string admin) {
            string allowed = AllowLinks(admin);
            WriteSettings(admin, true);
            Log.Write("Parent: Start page turned on.");
            return allowed + " " + L.T("The shortcut appears on the child's desktop when they are signed in.");
        }
        public static string Disable(string admin) {
            WriteSettings(admin, false);
            Log.Write("Parent: Start page turned off.");
            return L.T("Start page turned off; the shortcut is removed within a few seconds.");
        }
        public static List<string> DeployedShortcuts(string admin) {
            string path = RecordPath(admin);
            return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList() : new List<string>();
        }

        // Service side. Runs every cycle as SYSTEM.
        static string copiedHash;
        public static void Maintain(string admin, Installation target, IList<int> sessions, string icon) {
            if (!ReadSettings(admin).enabled || !File.Exists(SourcePath(admin))) { RemoveDeployed(admin); return; }
            SafeDir.Ensure(PublicDir, SafeDir.ForChild(target.userSid, false));
            byte[] page = File.ReadAllBytes(SourcePath(admin));
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(page));
            string index = Path.Combine(PublicDir, "index.html");
            if (hash != copiedHash || !File.Exists(index)) { File.WriteAllBytes(index, page); copiedHash = hash; }
            string publicIcon = Path.Combine(PublicDir, "kidguard.ico");
            if (File.Exists(icon) && !File.Exists(publicIcon)) File.Copy(icon, publicIcon);
            string name = ShortcutName(Title(Encoding.UTF8.GetString(page)));
            string content = "[InternetShortcut]\r\nURL=" + new Uri(index).AbsoluteUri + "\r\nIconFile=" + publicIcon + "\r\nIconIndex=0\r\n";
            var record = DeployedShortcuts(admin);
            bool changed = false;
            foreach (int session in sessions) {
                string desktop = Account.DesktopOf(session);
                if (desktop == null || !Directory.Exists(desktop)) continue;
                string path = Path.Combine(desktop, name);
                if (!File.Exists(path) || File.ReadAllText(path) != content) {
                    File.WriteAllText(path, content, Encoding.ASCII);
                    if (!record.Contains(path)) { record.Add(path); changed = true; Log.Write("Start page shortcut placed: " + path); }
                }
                // A renamed page leaves its old shortcut behind; remove shortcuts with other names from this desktop.
                foreach (string old in record.Where(r => !String.Equals(r, path, StringComparison.OrdinalIgnoreCase) &&
                        String.Equals(Path.GetDirectoryName(r), desktop, StringComparison.OrdinalIgnoreCase)).ToList()) {
                    try { File.Delete(old); } catch { }
                    record.Remove(old); changed = true;
                }
            }
            if (changed) ConfigStore.WriteAtomic(RecordPath(admin), String.Join("\r\n", record));
        }
        // Remove every deployed shortcut and the readable copy (turned off, or uninstall).
        public static void RemoveDeployed(string admin) {
            var record = DeployedShortcuts(admin);
            foreach (string path in record) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
            if (record.Count > 0) { File.Delete(RecordPath(admin)); Log.Write("Start page shortcuts removed."); }
            SafeDir.Remove(PublicDir);
            copiedHash = null;
        }
    }
}
