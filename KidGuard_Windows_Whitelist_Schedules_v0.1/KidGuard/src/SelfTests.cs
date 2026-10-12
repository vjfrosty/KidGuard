using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace KidGuard {
    public static class SelfTests {
        static int count;
        const string ChromeHello="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";
        static void Assert(bool condition,string label) { count++; if(!condition) throw new Exception("TEST FAILED: "+label); }
        static void Throws(System.Action f,string label) { bool failed=false; try { f(); } catch { failed=true; } Assert(failed,label); }
        static byte[] Hello(string host,bool ech,bool fragmented) {
            byte[] name=Encoding.ASCII.GetBytes(host);
            using(var body=new MemoryStream()) using(var ext=new MemoryStream()) using(var result=new MemoryStream()) {
                body.WriteByte(3); body.WriteByte(3); body.Write(new byte[32],0,32); body.WriteByte(0);
                body.Write(new byte[]{0,2,0x13,1,1,0},0,6);
                ext.WriteByte(0); ext.WriteByte(0); U16(ext,name.Length+5); U16(ext,name.Length+3); ext.WriteByte(0); U16(ext,name.Length); ext.Write(name,0,name.Length);
                if(ech) ext.Write(new byte[]{0xfe,0x0d,0,0},0,4);
                U16(body,(int)ext.Length); ext.Position=0; ext.CopyTo(body);
                byte[] payload=body.ToArray(); byte[] hs=new byte[payload.Length+4]; hs[0]=1; hs[2]=(byte)(payload.Length>>8); hs[3]=(byte)payload.Length; Buffer.BlockCopy(payload,0,hs,4,payload.Length);
                int split=fragmented ? 17 : hs.Length;
                Record(result,hs,0,split); if(split<hs.Length) Record(result,hs,split,hs.Length-split);
                return result.ToArray();
            }
        }
        static void U16(Stream s,int x) { s.WriteByte((byte)(x>>8)); s.WriteByte((byte)x); }
        static void Record(Stream s,byte[] b,int p,int n) { s.WriteByte(22); s.WriteByte(3); s.WriteByte(1); U16(s,n); s.Write(b,p,n); }
        public static void Run() {
            count=0; Wfp.AssertLayout(); Assert(true,"x64 ABI");
            var d=new Domains("# sample\nwikipedia.org\n*.wikipedia.org\nwww.khanacademy.org\n");
            Assert(d.Allows("WIKIPEDIA.ORG."),"case/trailing dot");
            Assert(d.Allows("bg.wikipedia.org"),"subdomain");
            Assert(!d.Allows("evilwikipedia.org"),"suffix boundary");
            Assert(!d.Allows("wikipedia.org.evil.com"),"suffix attack");
            Assert(!new Domains("*.wikipedia.org").Allows("wikipedia.org"),"wildcard excludes apex");
            Assert(new Domains("").Allows("kidsmanagement-pa.googleapis.com"),"Family Link check always allowed");
            Assert(new Domains("").Allows("accounts.google.com") && new Domains("").Allows("www.googleapis.com"),"Family Link sign-in always allowed");
            Assert(!new Domains("").Allows("www.google.com") && !new Domains("").Allows("storage.googleapis.com") && !new Domains("").Allows("x.kidsmanagement-pa.googleapis.com"),"not Search, not shared hosting, exact names only");
            Assert(!d.Allows("127.0.0.1"),"IP denied");
            foreach(string bad in new[]{"https://example.com","*","foo..com","example.com:443","127.0.0.1","foo.com/","*.com"})
                Throws(()=>new Domains(bad),"bad domain "+bad);
            var w=new Window { days=new[]{"Monday"},start="16:00",end="20:00" }; w.Validate();
            Assert(w.Contains(new DateTime(2026,10,12,16,0,0)),"inclusive start");
            Assert(!w.Contains(new DateTime(2026,10,12,20,0,0)),"exclusive end");
            Assert(!w.Contains(new DateTime(2026,10,13,17,0,0)),"different day");
            var night=new Window{days=new[]{"Sunday"},start="22:00",end="02:00"}; night.Validate();
            Assert(night.Contains(new DateTime(2026,10,12,1,59,0)),"overnight to Monday");
            Assert(!night.Contains(new DateTime(2026,10,12,2,0,0)),"overnight cutoff");
            var day=new Window{days=new[]{"Monday"},start="00:00",end="24:00"}; day.Validate();
            Assert(day.Contains(new DateTime(2026,10,12,23,59,0)),"whole day");
            Throws(()=>new Window{days=new[]{"Monday"},start="20:00",end="20:00"}.Validate(),"equal times");
            Throws(()=>new Window{days=new[]{"Munday"},start="10:00",end="12:00"}.Validate(),"bad day");
            string json="{\"timeZone\":\"FLE Standard Time\",\"forceLock\":false,\"accountHours\":[{\"days\":[\"Monday\"],\"start\":\"16:00\",\"end\":\"20:00\"}],\"internetHours\":[]}";
            var s=Schedule.Parse(json);
            Assert(s.AccountAllowed(new DateTime(2026,10,12,13,0,0,DateTimeKind.Utc)),"Sofia summer UTC+3");
            Assert(s.AccountAllowed(new DateTime(2026,11,2,14,0,0,DateTimeKind.Utc)),"Sofia winter UTC+2");
            Assert(!s.InternetAllowed(new DateTime(2026,10,12,13,0,0,DateTimeKind.Utc)),"empty internet closed");
            s.forceLock=true; Assert(!s.AccountAllowed(new DateTime(2026,10,12,13,0,0,DateTimeKind.Utc)),"manual lock");
            Throws(()=>Schedule.Parse("{}"),"missing fields closed");
            Throws(()=>Schedule.Parse(json.Replace("FLE Standard Time","Not a zone")),"invalid timezone");
            foreach(string ip in new[]{"127.0.0.1","10.0.0.1","192.168.1.1","169.254.169.254","100.64.0.1","::1","fc00::1","::ffff:127.0.0.1","2001:db8::1"})
                Assert(!Proxy.PublicAddress(IPAddress.Parse(ip)),"private denied "+ip);
            Assert(Proxy.PublicAddress(IPAddress.Parse("1.1.1.1")),"public IPv4");
            Assert(Proxy.PublicAddress(IPAddress.Parse("2606:4700:4700::1111")),"public IPv6");
            foreach(bool fragmented in new[]{false,true}) {
                var good=Hello("wikipedia.org",false,fragmented);
                Assert(TlsHello.ReadAndValidate(new MemoryStream(good),"wikipedia.org").SequenceEqual(good),"TLS SNI/fragments");
                Throws(()=>TlsHello.ReadAndValidate(new MemoryStream(good),"evil.com"),"SNI mismatch");
                Throws(()=>TlsHello.ReadAndValidate(new MemoryStream(good.Take(good.Length-1).ToArray()),"wikipedia.org"),"truncated TLS");
            }
            // ECH extension (GREASE) is accepted; the visible SNI must still match the allowed host.
            Assert(TlsHello.ReadAndValidate(new MemoryStream(Hello("wikipedia.org",true,false)),"wikipedia.org").Length>0,"ECH GREASE accepted with matching SNI");
            Throws(()=>TlsHello.ReadAndValidate(new MemoryStream(Hello("wikipedia.org",true,false)),"evil.com"),"ECH with mismatched SNI denied");
            // Real ClientHello captured from Chrome 155 through the proxy (contains a GREASE ECH extension, SNI www.wikipedia.org).
            byte[] chrome=Convert.FromBase64String(ChromeHello);
            Assert(TlsHello.ReadAndValidate(new MemoryStream(chrome),"www.wikipedia.org").SequenceEqual(chrome),"Chrome 155 ClientHello accepted");
            Throws(()=>TlsHello.ReadAndValidate(new MemoryStream(chrome),"evil.com"),"Chrome 155 ClientHello with other host denied");
            Assert(Proxy.CertificateAuthorityHost("yr2.i.lencr.org") && Proxy.CertificateAuthorityHost("c.pki.goog") && Proxy.CertificateAuthorityHost("CACERTS.digicert.com"),"certificate download hosts");
            Assert(!Proxy.CertificateAuthorityHost("evil-lencr.org") && !Proxy.CertificateAuthorityHost("www.digicert.com") && !Proxy.CertificateAuthorityHost("lencr.org.evil.com"),"certificate hosts are exact");
            foreach(string ech in new[]{"cloudflare-ech.com","*.cloudflare-ech.com","x.cloudflare-ech.com"})
                Throws(()=>Domains.Entry(ech),"ECH public name refused "+ech);
            Throws(()=>TlsHello.ReadAndValidate(new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n")),"wikipedia.org"),"non TLS denied");
            OverrideTests();
            StoreTests();
            PanelLogicTests();
            AccountTests();
            TextTests();
            ChildTests();
            SiteCheckTests();
            Console.WriteLine("PASS: "+count+" policy, parser and ABI assertions. Native Windows integration is a separate test.");
        }
        // Monday 2026-10-12, Sofia is UTC+3: account 16:00-20:00 = 13:00-17:00Z, internet 16:00-19:30 = 13:00-16:30Z.
        const string Monday="{\"timeZone\":\"FLE Standard Time\",\"forceLock\":false,"+
            "\"accountHours\":[{\"days\":[\"Monday\"],\"start\":\"16:00\",\"end\":\"20:00\"}],"+
            "\"internetHours\":[{\"days\":[\"Monday\"],\"start\":\"16:00\",\"end\":\"19:30\"}]}";
        static DateTime Z(int h,int m) { return new DateTime(2026,10,12,h,m,0,DateTimeKind.Utc); }
        static Policy With(string overrides) { return new Policy(Monday,"wikipedia.org\n",overrides); }
        static void OverrideTests() {
            var p=With(null);
            Assert(p.AccountAllowed(Z(13,0)) && p.InternetAllowed(Z(13,0)),"schedule open");
            Assert(p.NextChange(Z(13,0),p.AccountAllowed)==Z(17,0),"next account change");
            Assert(p.NextChange(Z(13,0),p.InternetAllowed)==Z(16,30),"next internet change");
            Assert(p.NextChange(Z(16,59),p.AccountAllowed)==Z(17,0),"next change one minute away");
            Assert(p.NextChange(Z(18,0),p.AccountAllowed)==new DateTime(2026,10,19,13,0,0,DateTimeKind.Utc),"next opening a week later");
            Assert(!p.AccountAllowed(Z(18,0)),"closed after hours");

            var bonus=With("{\"bonusUntil\":\"2026-10-12T18:30:30Z\"}");
            Assert(bonus.AccountAllowed(Z(18,0)),"bonus extends account");
            Assert(!bonus.InternetAllowed(Z(18,0)),"bonus is offline by default");
            Assert(bonus.NextChange(Z(18,0),bonus.AccountAllowed)==new DateTime(2026,10,12,18,30,30,DateTimeKind.Utc),"bonus expiry to the second");
            var online=With("{\"bonusUntil\":\"2026-10-12T18:30:00Z\",\"bonusInternet\":true}");
            Assert(online.InternetAllowed(Z(18,0)),"bonus with internet");
            var locked=With("{\"lockUntil\":\"2026-10-12T18:10:00Z\",\"bonusUntil\":\"2026-10-12T18:30:00Z\",\"bonusInternet\":true}");
            Assert(!locked.AccountAllowed(Z(18,0)) && !locked.InternetAllowed(Z(18,0)),"lock beats bonus");
            Assert(locked.AccountAllowed(Z(18,10)),"lock expires");
            Assert(!With("{\"lockUntil\":\""+Overrides.Forever+"\"}").AccountAllowed(Z(13,0)),"indefinite lock");
            Assert(!new Policy(Monday.Replace("\"forceLock\":false","\"forceLock\":true"),"a.org",online.OverridesText).AccountAllowed(Z(18,0)),"schedule forceLock beats bonus");
            var paused=With("{\"internetPausedUntil\":\"2026-10-12T13:30:00Z\"}");
            Assert(paused.AccountAllowed(Z(13,0)) && !paused.InternetAllowed(Z(13,0)),"internet pause");
            Assert(paused.InternetAllowed(Z(13,30)),"pause expires");
            Assert(p.Revision==paused.Revision && p.FullRevision!=paused.FullRevision,"overrides do not reset tunnels");

            var o=Overrides.Parse(locked.OverridesText); o.Prune(Z(18,20));
            Assert(o.lockUntil==null && o.bonusUntil!=null,"prune expired lock only");
            Assert(Overrides.Parse(o.ToJson()).bonusUntil==o.bonusUntil,"overrides round trip");
            Assert(Overrides.Parse("").lockUntil==null && Overrides.Parse("  ").bonusUntil==null,"empty overrides");
            Throws(()=>Overrides.Parse("{\"lockUntil\":\"tomorrow\"}"),"bad override time");
            Throws(()=>Overrides.Parse("{\"lockUntil\":\"2026-10-12 18:00\"}"),"override time needs UTC format");
        }
        // Same schedule decision for every minute of a reference week (Mon 2026-10-12 .. Sun 2026-10-18, local).
        static bool SameWeek(Window[] a, Window[] b) {
            DateTime monday = new DateTime(2026, 10, 12);
            for (int m = 0; m < 7 * 1440; m++) { DateTime t = monday.AddMinutes(m); if (a.Any(w => w.Contains(t)) != b.Any(w => w.Contains(t))) return false; }
            return true;
        }
        static void PanelLogicTests() {
            string sample = "{\"timeZone\":\"FLE Standard Time\",\"forceLock\":false,\"accountHours\":[" +
                "{\"days\":[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\"],\"start\":\"16:00\",\"end\":\"20:00\"}," +
                "{\"days\":[\"Friday\"],\"start\":\"22:00\",\"end\":\"02:00\"}],\"internetHours\":[]}";
            var s = Schedule.Parse(sample);
            Assert(WeekGrid.Exact(s.accountHours, 15), "15-minute grid is exact for whole-hour schedule");
            var grid = WeekGrid.FromWindows(s.accountHours, 15);
            Assert(grid[0, 64] && !grid[0, 80] && grid[4, 88] && grid[5, 7] && !grid[5, 8] && !grid[6, 0], "grid cells incl. overnight into Saturday");
            var back = WeekGrid.ToWindows(grid, 15);
            Assert(SameWeek(s.accountHours, back), "grid round trip keeps every minute");
            Assert(back.Any(w => w.start == "16:00" && w.end == "20:00" && w.days.Length == 5), "weekdays merged into one window");
            Assert(back.Any(w => w.end == "24:00") && back.Any(w => w.start == "00:00" && w.end == "02:00" && w.days.Single() == "Saturday"), "overnight split at midnight");
            Assert(!WeekGrid.Exact(new[] { new Window { days = new[] { "Monday" }, start = "16:10", end = "17:00" } }, 15), "off-grid time detected");
            Assert(WeekGrid.ToWindows(new bool[7, 96], 15).Length == 0, "empty grid");
            var all = new bool[7, 96]; for (int d = 0; d < 7; d++) for (int x = 0; x < 96; x++) all[d, x] = true;
            var full = WeekGrid.ToWindows(all, 15);
            Assert(full.Length == 1 && full[0].start == "00:00" && full[0].end == "24:00" && full[0].days.Length == 7, "full week");

            s.accountHours = back; s.forceLock = true;
            var again = Schedule.Parse(s.ToJson());
            Assert(again.forceLock && SameWeek(again.accountHours, back) && again.internetHours.Length == 0 && again.timeZone == "FLE Standard Time", "schedule JSON round trip");

            Assert(Domains.HostFromInput(" https://www.Ucha.se/lessons?x=1 ") == "www.ucha.se", "host from URL");
            Assert(Domains.HostFromInput("scratch.mit.edu/projects") == "scratch.mit.edu", "host from path");
            Assert(Domains.HostFromInput("example.org:443") == "example.org", "host from host:port");
            Throws(() => Domains.HostFromInput("https://1.2.3.4/"), "IP input rejected");
            Throws(() => Domains.HostFromInput(""), "empty input rejected");

            var bundles = Bundle.Parse("# c\n[Learn | Site A]\nA.example.org\n*.example.org\n\n[Play | Site B]\nb.example.net\n");
            Assert(bundles.Count == 2 && bundles[0].Category == "Learn" && bundles[0].Entries[0] == "a.example.org" && bundles[0].Entries[1] == "*.example.org", "bundles parse");
            Throws(() => Bundle.Parse("a.example.org\n"), "bundle entry needs a header");
            Throws(() => Bundle.Parse("[Learn | A]\n"), "empty bundle rejected");
            Throws(() => Bundle.Parse("[Learn | A]\nhttps://x.org/\n"), "bad bundle entry rejected");
            Throws(() => Bundle.Parse("[No separator]\na.org\n"), "bundle header needs category");

            string page = "<html><head><title>Viktor&#39;s internet \u00B7 Sites</title></head><body>" +
                "<a href=\"https://bg.khanacademy.org/\">Khan</a> <A HREF='https://Scratch.MIT.edu/projects'>Scratch</a>" +
                "<a href=\"https://scratch.mit.edu/download\">again</a> <a href=\"#top\">top</a> <a href=\"mailto:x@y.org\">mail</a>" +
                "<a href=\"javascript:void(0)\">js</a> <a href=\"https://1.2.3.4/\">ip</a> <img src=\"https://cdn.example.net/x.png\">" +
                "<a class=\"c\" href=\"http://spaceplace.nasa.gov/menu/play/\">NASA</a></body></html>";
            var hosts = StartPage.LinkHosts(page);
            Assert(hosts.SequenceEqual(new[] { "bg.khanacademy.org", "scratch.mit.edu", "spaceplace.nasa.gov" }), "start page link hosts (links only, no duplicates, no IPs)");
            Assert(StartPage.Title(page) == "Viktor's internet \u00B7 Sites", "start page title decoded");
            Assert(StartPage.ShortcutName(StartPage.Title(page)) == "Viktor's internet.url", "shortcut name from title");
            Assert(StartPage.ShortcutName("a:b*c?/d") == "abcd.url" && StartPage.ShortcutName("") == "KidGuard start page.url", "shortcut name made safe");
            var rules = StartPage.Rules(hosts, Bundle.Parse("[Learn | Khan]\nbg.khanacademy.org\n*.khanacademy.org\ncdn.kastatic.org\n[Play | Other]\nother.example.org\n"));
            Assert(rules.SequenceEqual(new[] { "bg.khanacademy.org", "*.khanacademy.org", "cdn.kastatic.org", "scratch.mit.edu", "spaceplace.nasa.gov" }), "start page rules add covering bundles only");
        }
        static List<string> Placeholders(string s) {
            return System.Text.RegularExpressions.Regex.Matches(s, "\\{\\d+(:[^}]*)?\\}").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
        }
        static void TextTests() {
            var table = new Dictionary<string, string>();
            L.Load("# c\nen: A {0}\\nB\nbg: X {0}\\nY\nbg: orphan\nen: lonely\n", table);
            Assert(table.Count == 1 && table["A {0}\nB"] == "X {0}\nY", "translation file pairs and line breaks");
            Assert(L.Pairs.Any(), "Bulgarian texts embedded");
            foreach (var pair in L.Pairs)
                Assert(Placeholders(pair.Key).SequenceEqual(Placeholders(pair.Value)), "same placeholders in translation of: " + pair.Key);
            string before = L.Language;
            try {
                L.Language = "bg";
                Assert(L.T("Save schedule") != "Save schedule" && L.T("not translated text") == "not translated text", "Bulgarian lookup with English fallback");
                Assert(L.Has("Windows will sign you out in {0}. Save your work now.") && L.Has("The internet will turn off in {0}."), "child warnings translated");
                Assert(L.T("Windows will sign you out in {0}. Save your work now.", L.Minutes(10)).Contains("10"), "translated warning formats");
                L.Language = "en";
                Assert(L.Minutes(1) == "1 minute" && L.Minutes(10) == "10 minutes", "minutes in English");
                Assert(L.Duration(TimeSpan.FromMinutes(85)) == "1 h 25 min" && L.Duration(TimeSpan.FromMinutes(44.2)) == "45 min" && L.Duration(TimeSpan.FromMinutes(120)) == "2 h", "durations");
                L.Language = "xx";
                Assert(L.Language == "en", "unknown language falls back to English");
            } finally { L.Language = before; }
        }
        static ServiceStatus st0(Policy p) { return ServiceStatus.From(p, DateTime.UtcNow, null); }
        static void SiteCheckTests() {
            Assert(SiteCheck.SiteDomain("static.ucha.se") == "ucha.se" && SiteCheck.SiteDomain("kids.bbc.co.uk") == "bbc.co.uk" && SiteCheck.SiteDomain("ucha.se") == "ucha.se", "site domain");
            Assert(SiteCheck.SiteDomain("x1.cloudfront.net") == "x1.cloudfront.net" && SiteCheck.SiteDomain("s3.eu-west-1.amazonaws.com") == "s3.eu-west-1.amazonaws.com", "shared hosting keeps the full name");
            var seen = new Dictionary<string, int> {
                { "ucha.se", 9 }, { "static.ucha.se", 21 }, { "www.google-analytics.com", 3 }, { "www.youtube.com", 1 }, { "player.vimeo.com", 1 },
                { "f.vimeocdn.com", 2 }, { "vod-adaptive-ak.vimeocdn.com", 1 }, { "update.googleapis.com", 4 }, { "a1.cloudfront.net", 1 }, { "b2.cloudfront.net", 1 },
                { "firehose.eu-west-1.amazonaws.com", 2 }, { "fonts.googleapis.com", 2 }, { "storage.googleapis.com", 1 }, { "r3.o.lencr.org", 1 }
            };
            var list = SiteCheck.Sort("www.ucha.se", seen);
            Func<string, SiteAddress> rule = r => list.FirstOrDefault(a => a.Rule == r);
            Assert(rule("ucha.se") != null && rule("*.ucha.se") != null && rule("*.ucha.se").Suggested && rule("static.ucha.se") == null, "the site itself becomes the whole site");
            Assert(rule("www.google-analytics.com").Kind == "tracking" && !rule("www.google-analytics.com").Suggested, "tracking not suggested");
            Assert(rule("www.youtube.com").Kind == "general" && !rule("www.youtube.com").Suggested, "general sites not suggested");
            Assert(rule("player.vimeo.com").Suggested && rule("*.vimeocdn.com").Suggested && rule("f.vimeocdn.com") == null, "embedded player allowed, CDN servers merged");
            Assert(rule("a1.cloudfront.net").Suggested && rule("*.cloudfront.net") == null && rule("*.googleapis.com") == null, "shared hosting stays exact");
            Assert(rule("firehose.eu-west-1.amazonaws.com").Kind == "tracking", "statistics on a shared cloud are tracking");
            Assert(rule("update.googleapis.com") == null && rule("r3.o.lencr.org") == null && rule("fonts.googleapis.com").Suggested, "browser traffic hidden, fonts suggested");
            Assert(list.TakeWhile(a => a.Suggested).Count() == list.Count(a => a.Suggested), "suggested addresses listed first");
            string page = "<a href=\"https://ucha.se/watch/7983/lesson\">l</a><a href=\"https://ucha.se/watch/1/other\">same section</a>" +
                "<a href='https://static.ucha.se/font.otf'>f</a><a href=\"https://ucha.se/bg/\">lang</a><a href=\"https://ucha.se/login\">in</a>" +
                "<a href=\"https://other.org/x/y\">x</a><a href=\"https://ucha.se/videos/matematika/1-klas/\">v</a>";
            var links = SiteCheck.SameSiteLinks(page, "ucha.se", 5);
            Assert(links.Count == 2 && links.Contains("https://ucha.se/watch/7983/lesson") && links.Contains("https://ucha.se/videos/matematika/1-klas/"), "extra pages: one per section, no files, languages, logins or other sites");
        }
        static void ChildTests() {
            DateTime now = new DateTime(2026, 10, 12, 14, 0, 0, DateTimeKind.Utc);
            var site = Requests.Clean(ChildFiles.ParseRequest(ChildFiles.RequestText("site", "https://WWW.Example.com/page?x=1", 0, "for\nschool\u0007")), now);
            Assert(site.kind == "site" && site.site == "www.example.com" && site.note == "for school" && site.state == "waiting", "site request parsed and cleaned");
            Assert(Requests.Clean(ChildFiles.ParseRequest("kind=time\nminutes=30\n"), now).minutes == 30, "time request");
            Assert(Requests.Clean(ChildFiles.ParseRequest("kind=site\nsite=a.org\nnote=" + new string('x', 500)), now).note.Length == ChildFiles.MaxNote, "note shortened");
            foreach (string bad in new[] { "kind=time\nminutes=45", "kind=time\nminutes=600", "kind=admin\nsite=a.org", "kind=site\nsite=127.0.0.1", "kind=site\nsite=", "" })
                Throws(() => Requests.Clean(ChildFiles.ParseRequest(bad), now), "bad request refused: " + bad.Replace("\n", " "));

            string dir = Path.Combine(Path.GetTempPath(), "KidGuardTest-" + Guid.NewGuid().ToString("N"));
            string inbox = Path.Combine(dir, "inbox");
            Directory.CreateDirectory(inbox);
            string oldLog = Log.DirectoryPath;
            Log.DirectoryPath = dir;
            try {
                File.WriteAllText(Path.Combine(dir, "schedule.json"), Monday);
                File.WriteAllText(Path.Combine(dir, "whitelist.txt"), "khanacademy.org\n");
                File.WriteAllText(Path.Combine(inbox, "a.txt"), ChildFiles.RequestText("site", "wikipedia.org", 0, "homework"));
                File.WriteAllText(Path.Combine(inbox, "b.txt"), ChildFiles.RequestText("site", "WIKIPEDIA.org", 0, null));
                File.WriteAllText(Path.Combine(inbox, "c.txt"), ChildFiles.RequestText("time", null, 15, null));
                File.WriteAllText(Path.Combine(inbox, "d.txt"), "kind=site\nsite=" + new string('a', 3000));
                File.WriteAllText(Path.Combine(inbox, "e.txt"), "garbage");
                Assert(ChildHelper.Collect(dir, inbox, now) == 2 && Directory.GetFiles(inbox).Length == 0, "inbox collected (duplicate merged, bad and oversized dropped) and emptied");
                var list = Requests.Read(dir);
                Assert(list.Length == 2 && list.Count(r => r.site == "wikipedia.org" && r.note == "homework") == 1, "one request per site");
                using (var held = new FileStream(Path.Combine(inbox, "f.txt"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    Assert(ChildHelper.Collect(dir, inbox, now) == 0 && File.Exists(Path.Combine(inbox, "f.txt")), "file still being written is left for the next cycle");
                File.Delete(Path.Combine(inbox, "f.txt"));

                var many = Enumerable.Range(0, 30).Select(i => ChildFiles.ParseRequest("kind=site\nsite=s" + i + ".example.org")).ToList();
                Requests.Add(dir, many, now);
                Assert(Requests.Read(dir).Count(r => r.state == "waiting") == Requests.MaxWaiting, "waiting requests capped");

                string wiki = Requests.Read(dir).First(r => r.site == "wikipedia.org").id;
                KidGuard.Parent.AllowRequestedSite(dir, wiki, true);
                var w = new Domains(File.ReadAllText(Path.Combine(dir, "whitelist.txt")));
                Assert(w.Allows("wikipedia.org") && w.Allows("en.wikipedia.org") && Requests.Read(dir).First(r => r.id == wiki).state == "allowed", "allowing a requested site whitelists the whole site");
                Throws(() => KidGuard.Parent.AllowRequestedSite(dir, wiki, true), "answered request cannot be answered again");
                string time = Requests.Read(dir).First(r => r.kind == "time").id;
                Throws(() => KidGuard.Parent.AllowRequestedSite(dir, time, true), "time request is not a site");
                KidGuard.Parent.DenyRequest(dir, time);
                Assert(Requests.Read(dir).First(r => r.id == time).state == "denied", "request denied");
                Requests.Add(dir, new[] { ChildFiles.ParseRequest("kind=site\nsite=late.example.org") }, now.AddHours(25));
                Assert(Requests.Read(dir).Count(r => r.state == "expired") == Requests.MaxWaiting - 2, "requests expire after a day");
                Assert(Requests.RemoveAnswered(dir) > 0 && Requests.Read(dir).All(r => r.state == "waiting"), "answered requests cleared");

                Blocked.Record("games.example.net"); Blocked.Flush(dir, new Domains("a.org"));
                string error; var p = ConfigStore.Load(dir, out error);
                var st = ChildHelper.StatusFor(dir, ServiceStatus.From(p, now, null), p, now, new Settings { language = "bg" });
                Assert(st.language == "bg" && st.zone == "FLE Standard Time" && st.accountAllowed && st.signOutAt == "2026-10-12T17:00:00Z" && st.internetChangeAt == "2026-10-12T16:30:00Z", "child status times");
                Assert(st.requests.Length > 0, "child status lists requests");
                Assert(ChildHelper.StatusFor(dir, st0(p), p, DateTime.UtcNow, new Settings()).recentBlocked.Contains("games.example.net"), "recently blocked site offered to the child");
                Assert(ChildHelper.StatusFor(dir, st0(p), p, DateTime.UtcNow.AddHours(1), new Settings()).recentBlocked.Length == 0, "blocked sites older than 30 minutes are not offered");
                var round = ChildFiles.FromJson<ChildStatus>(ChildFiles.ToJson(st));
                Assert(round.signOutAt == st.signOutAt && round.requests.Length == st.requests.Length, "child status round trip");

                Assert(Settings.Read(dir).Language == L.Default && !Settings.Read(dir).hideChildHelper, "settings defaults");
                Settings.Update(dir, s => { s.language = "bg"; s.hideChildHelper = true; });
                Assert(Settings.Read(dir).Language == "bg" && Settings.Read(dir).hideChildHelper, "settings saved");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{ broken");
                Assert(Settings.Read(dir).Language == L.Default, "broken settings mean defaults");

                // Removing a link put where a KidGuard folder belongs must not delete what it points to.
                string target = Path.Combine(dir, "target"), link = Path.Combine(dir, "link");
                Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep.txt"), "x");
                var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                mk.WaitForExit();
                if (Directory.Exists(link)) {
                    SafeDir.Remove(link);
                    Assert(!Directory.Exists(link) && File.Exists(Path.Combine(target, "keep.txt")), "junction removed without following it");
                }
            } finally { Log.DirectoryPath = oldLog; try { Directory.Delete(dir, true); } catch { } }
        }
        static Installation Inst(string json) { using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json))) return Installation.Parse(ms); }
        static void AccountTests() {
            var v01 = Inst("{\"userName\":\"Viktor\",\"userSid\":\"S-1-5-21-1-2-3-1001\",\"originallyEnabled\":true}");
            Assert(v01.kind == null && v01.UsesAccountFlag, "installation record without kind is a local account");
            Assert(Inst("{\"userName\":\"vikto\",\"userSid\":\"S-1-5-21-1-2-3-1005\",\"originallyEnabled\":true,\"kind\":\"microsoft\"}").UsesAccountFlag, "Microsoft account uses the account flag");
            var entra = Inst("{\"userName\":\"AzureAD\\\\Viktor\",\"userSid\":\"S-1-12-1-1-2-3-4\",\"originallyEnabled\":true,\"kind\":\"entra\",\"deniedBefore\":[\"SeDenyRemoteInteractiveLogonRight\"]}");
            Assert(!entra.UsesAccountFlag && entra.deniedBefore.Single() == "SeDenyRemoteInteractiveLogonRight", "Entra account uses deny-logon rights");
            Assert(!Inst("{\"userName\":\"SCHOOL\\\\viktor\",\"userSid\":\"S-1-5-21-9-9-9-2001\",\"originallyEnabled\":true,\"kind\":\"domain\"}").UsesAccountFlag, "domain account uses deny-logon rights");
            Throws(() => Inst("{\"userName\":\"Administrator\",\"userSid\":\"S-1-5-21-1-2-3-500\",\"originallyEnabled\":true,\"kind\":\"entra\"}").VerifyIdentity(), "built-in Administrator refused");

            var add = new List<string>(); var remove = new List<string>();
            LogonRights.Plan(false, new string[0], new string[0], add, remove);
            Assert(add.Count == 2 && remove.Count == 0, "block adds both deny rights");
            add.Clear(); remove.Clear();
            LogonRights.Plan(false, LogonRights.Deny, new string[0], add, remove);
            Assert(add.Count == 0 && remove.Count == 0, "already blocked: no change");
            add.Clear(); remove.Clear();
            LogonRights.Plan(true, LogonRights.Deny, new string[0], add, remove);
            Assert(add.Count == 0 && remove.Count == 2, "allow removes KidGuard's rights");
            add.Clear(); remove.Clear();
            LogonRights.Plan(true, LogonRights.Deny, new[] { "SeDenyRemoteInteractiveLogonRight" }, add, remove);
            Assert(remove.Single() == "SeDenyInteractiveLogonRight", "allow keeps rights the account had before");
            add.Clear(); remove.Clear();
            LogonRights.Plan(true, new string[0], null, add, remove);
            Assert(add.Count == 0 && remove.Count == 0, "allow with nothing set: no change");
        }
        static void StoreTests() {
            string dir=Path.Combine(Path.GetTempPath(),"KidGuardTest-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                File.WriteAllText(Path.Combine(dir,"schedule.json"),Monday);
                File.WriteAllText(Path.Combine(dir,"whitelist.txt"),"# keep me\nwikipedia.org\n");
                string error;
                var good=ConfigStore.Load(dir,out error);
                Assert(error==null,"live config loads");
                ConfigStore.SaveLastGood(dir,good);
                File.WriteAllText(Path.Combine(dir,"schedule.json"),"{ broken");
                var kept=ConfigStore.Load(dir,out error);
                Assert(error!=null && kept.FullRevision==good.FullRevision,"broken edit keeps last valid policy");
                Directory.Delete(ConfigStore.LastGoodDir(dir),true);
                Throws(()=>ConfigStore.Load(dir,out error),"no valid policy fails closed");

                Throws(()=>ConfigStore.ApplySchedule(dir,"{\"timeZone\":\"Nowhere\"}"),"invalid schedule rejected");
                Assert(File.ReadAllText(Path.Combine(dir,"schedule.json"))=="{ broken","rejected schedule not written");
                ConfigStore.ApplySchedule(dir,Monday);
                Assert(ConfigStore.Load(dir,out error)!=null && error==null,"valid schedule applied");
                Throws(()=>ConfigStore.ApplyWhitelist(dir,"https://bad.example/"),"invalid whitelist rejected");

                Assert(ConfigStore.AllowDomain(dir,"Example.ORG"),"allow domain");
                Assert(!ConfigStore.AllowDomain(dir,"example.org."),"allow is idempotent");
                Assert(ConfigStore.AllowDomain(dir,"*.example.org"),"allow wildcard");
                Throws(()=>ConfigStore.AllowDomain(dir,"*"),"bare wildcard rejected");
                var d=new Domains(File.ReadAllText(Path.Combine(dir,"whitelist.txt")));
                Assert(d.Allows("example.org") && d.Allows("www.example.org") && d.Allows("wikipedia.org"),"allowed entries active");
                Assert(ConfigStore.DisallowDomain(dir,"EXAMPLE.org"),"disallow domain");
                Assert(!ConfigStore.DisallowDomain(dir,"example.org"),"disallow missing");
                string text=File.ReadAllText(Path.Combine(dir,"whitelist.txt"));
                Assert(text.Contains("# keep me") && text.Contains("*.example.org") && !new Domains(text).Allows("example.org"),"disallow keeps other lines");

                ConfigStore.UpdateOverrides(dir,x=>x.lockUntil=Overrides.Forever);
                Assert(ConfigStore.ReadOverrides(dir).lockUntil==Overrides.Forever,"override written");
                ConfigStore.UpdateOverrides(dir,x=>x.lockUntil=null);
                Assert(ConfigStore.ReadOverrides(dir).lockUntil==null,"override cleared");

                Blocked.Record("bad.example.net"); Blocked.Record("bad.example.net"); Blocked.Record("en.wikipedia.org");
                Blocked.Flush(dir,new Domains("*.wikipedia.org"));
                var list=Blocked.Read(dir);
                Assert(list.Length==1 && list[0].domain=="bad.example.net" && list[0].count==2,"blocked merged, allowed pruned");
                Blocked.Record("bad.example.net"); Blocked.Flush(dir,new Domains("a.org"));
                Assert(Blocked.Read(dir)[0].count==3,"blocked counts accumulate");
                Blocked.Flush(dir,new Domains("bad.example.net"));
                Assert(Blocked.Read(dir).Length==0,"newly allowed domain leaves the list");
                Blocked.Record("other.example.net"); Blocked.Flush(dir,new Domains("a.org"));
                Assert(Blocked.Dismiss(dir,"OTHER.example.net") && Blocked.Read(dir).Length==0,"dismiss");
            } finally { try { Directory.Delete(dir,true); } catch { } }
        }
    }
}
