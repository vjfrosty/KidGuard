using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KidGuard {
    // One address a site used, and what KidGuard suggests doing with it.
    public class SiteAddress {
        // "site": part of the site itself; "needed": another address the site loads (pictures, video, fonts, scripts);
        // "general": a big general site (allowing it opens all of it); "tracking": statistics or advertising.
        public string Host, Kind, Rule;
        public int Count;
        public bool Suggested { get { return Kind == "site" || Kind == "needed"; } }
    }

    // "Check what a site needs": open it in a hidden browser through a proxy that lets everything through and
    // records every address, then sort the addresses. Runs on the parent's side only (Parent Panel, --check-site).
    public static class SiteCheck {
        // Second-level suffixes under which sites register names ("bbc.co.uk"); enough for the sites families use.
        static readonly HashSet<string> SecondLevel = new HashSet<string>(StringComparer.Ordinal) {
            "co.uk","org.uk","ac.uk","gov.uk","me.uk","ltd.uk","plc.uk","sch.uk","nhs.uk","com.au","net.au","org.au","edu.au","gov.au",
            "co.nz","org.nz","com.br","com.ar","com.mx","co.jp","ne.jp","or.jp","co.kr","co.za","co.in","com.cn","com.tr","com.ua",
            "co.il","com.sg","com.hk","com.tw","com.pl","com.ro","com.gr","com.es","com.pt","gc.ca","gov.bg","edu.bg","org.bg","com.bg"
        };
        // Statistics, advertising, consent banners, chat widgets and social buttons: a page works without them.
        static readonly string[] Tracking = {
            "google-analytics.com","googletagmanager.com","googlesyndication.com","googleadservices.com","doubleclick.net","adservice.google.com",
            "googletagservices.com","facebook.net","facebook.com","fbcdn.net","instagram.com","hotjar.com","hotjar.io","plausible.io","parsely.com",
            "sentry.io","sentry-cdn.com","cloudflareinsights.com","convertexperiments.com","chatbase.co","intercom.io","intercomcdn.com","addthis.com",
            "pinterest.com","pinimg.com","stats.wp.com","pixel.wp.com","statsigapi.net","featureassets.org","prodregistryv2.org","cookielaw.org",
            "onetrust.com","trustarc.com","cookiebot.com","usercentrics.eu","dap.digitalgov.gov","clarity.ms","segment.io","segment.com","mixpanel.com",
            "amplitude.com","newrelic.com","nr-data.net","criteo.com","criteo.net","taboola.com","outbrain.com","adnxs.com","scorecardresearch.com",
            "quantserve.com","quantcount.com","bat.bing.com","ads-twitter.com","analytics.tiktok.com","yandex.ru","mc.yandex.ru","matomo.cloud",
            "optimizely.com","crazyegg.com","fullstory.com","mouseflow.com","heapanalytics.com","branch.io","adsrvr.org","rubiconproject.com",
            "pubmatic.com","openx.net","amazon-adsystem.com","moatads.com","zendesk.com","zdassets.com","tawk.to","livechatinc.com","hubspot.com",
            "hs-scripts.com","hs-analytics.net","analytics.google.com","licdn.com","ads.linkedin.com","smct.io","smct.co","ihi.flowplayer.com","lensflare.vimeo.com","arclight.vimeo.com","gemius.pl","adocean.pl","cxense.com",
            "bat.bing.net","mparticle.com","coralogix.com","rum-ingress-coralogix.com"
        };
        // Big general sites: allowing one gives the child all of it (search, videos, social media). The parent decides.
        static readonly string[] General = {
            "google.com","google.bg","youtube.com","youtube-nocookie.com","googlevideo.com","ytimg.com","twitter.com","x.com","twimg.com","tiktok.com",
            "reddit.com","redd.it","twitch.tv","discord.com","discord.gg","whatsapp.com","telegram.org","t.me","snapchat.com","linkedin.com",
            "vk.com","ok.ru","bing.com","duckduckgo.com","yahoo.com","vimeo.com","wikipedia.org","amazon.com","ebay.com","roblox.com","dailymotion.com"
        };
        // The browser's own background requests (updates, sync, safe browsing, DNS): never shown.
        static readonly string[] BrowserNoise = {
            "accounts.google.com","clients1.google.com","clients2.google.com","clients4.google.com","clients6.google.com","update.googleapis.com",
            "content-autofill.googleapis.com","optimizationguide-pa.googleapis.com","safebrowsing.googleapis.com","translate.googleapis.com",
            "clientservices.googleapis.com","android.clients.google.com","www.googleapis.com","gvt1.com","gvt2.com","edge.microsoft.com",
            "config.edge.skype.com","edgeassetservice.azureedge.net","smartscreen-prod.microsoft.com","nav.smartscreen.microsoft.com",
            "msedge.net","cloudflare-dns.com","dns.google","chrome.cloudflare-dns.com","dns.quad9.net","login.microsoftonline.com","ntp.msn.com",
            "msn.com","edge.activity.windows.com","browser.events.data.microsoft.com","self.events.data.microsoft.com",
            "www.bing.com","edge-consumer-static.azureedge.net","business.bing.com","copilot.microsoft.com","xpaywalletcdn.azureedge.net",
            "delivery.mp.microsoft.com","smartscreen.microsoft.com","safebrowsing.google.com","mtalk.google.com","connectivitycheck.gstatic.com"
        };
        // Shared hosting: anyone can publish there, so only the exact address is ever allowed, never all of it.
        static readonly string[] SharedHosting = {
            "amazonaws.com","googleapis.com","cloudfront.net","azurefd.net","azureedge.net","windows.net","googleusercontent.com","appspot.com","herokuapp.com",
            "github.io","githubusercontent.com","workers.dev","pages.dev","netlify.app","vercel.app","firebaseapp.com","web.app","akamaihd.net",
            "akamaized.net","fastly.net","b-cdn.net","wixsite.com","blogspot.com","wordpress.com","weebly.com","glitch.me"
        };

        // Players a site embeds, which do not open the whole general site.
        static readonly string[] Embeds = { "player.vimeo.com", "player.twitch.tv", "www.youtube-nocookie.com" };
        static bool Under(string host, string domain) { return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal); }
        static bool InList(string host, string[] list) { return list.Any(d => Under(host, d)); }

        // "static.ucha.se" -> "ucha.se"; "kids.bbc.co.uk" -> "bbc.co.uk". Shared hosting keeps the full name.
        public static string SiteDomain(string host) {
            if (InList(host, SharedHosting)) return host;
            string[] labels = host.Split('.');
            if (labels.Length <= 2) return host;
            string lastTwo = labels[labels.Length - 2] + "." + labels[labels.Length - 1];
            int keep = SecondLevel.Contains(lastTwo) ? 3 : 2;
            return String.Join(".", labels.Skip(labels.Length - keep));
        }
        // Statistics collectors on shared clouds ("firehose.eu-west-1.amazonaws.com").
        static readonly string[] TrackingPrefixes = { "firehose.", "kinesis.", "analytics.", "stats.", "metrics.", "telemetry.", "beacon.", "pixel." };
        public static string Kind(string host, string site) {
            if (InList(host, BrowserNoise)) return "browser";
            if (InList(host, Tracking) || (SiteDomain(host) != site && TrackingPrefixes.Any(p => host.StartsWith(p, StringComparison.Ordinal)))) return "tracking";
            if (SiteDomain(host) == site) return "site";
            if (Embeds.Contains(host)) return "needed";
            if (InList(host, General)) return "general";
            return "needed";
        }
        // Sorted suggestions. The site itself becomes "site.org" and "*.site.org"; other addresses stay exact.
        public static List<SiteAddress> Sort(string startHost, IDictionary<string, int> seen) {
            string site = SiteDomain(startHost);
            var result = new List<SiteAddress>();
            if (site == startHost || !InList(startHost, SharedHosting))
                foreach (string rule in Parent.WholeSite(site)) result.Add(new SiteAddress { Host = rule, Kind = "site", Rule = rule });
            var others = new List<SiteAddress>();
            foreach (var pair in seen) {
                string kind = Kind(pair.Key, site);
                if (kind == "browser" || kind == "site" || Proxy.CertificateAuthorityHost(pair.Key)) continue;
                others.Add(new SiteAddress { Host = pair.Key, Kind = kind, Rule = pair.Key, Count = pair.Value });
            }
            // Several servers of one content provider (video, pictures: "vod-adaptive-ak.vimeocdn.com", "f.vimeocdn.com") become
            // one rule, because their names change between visits. Never for shared hosting, where anyone can publish.
            foreach (var group in others.Where(a => a.Kind == "needed" && !Embeds.Contains(a.Host) && !InList(a.Host, SharedHosting))
                    .GroupBy(a => SiteDomain(a.Host)).Where(g => g.Count() >= 2).ToList()) {
                foreach (var a in group) others.Remove(a);
                others.Add(new SiteAddress { Host = "*." + group.Key, Kind = "needed", Rule = "*." + group.Key, Count = group.Sum(a => a.Count) });
            }
            result.AddRange(others.OrderBy(a => a.Suggested ? 0 : a.Kind == "general" ? 1 : 2).ThenByDescending(a => a.Count).ThenBy(a => a.Host, StringComparer.Ordinal));
            return result;
        }
        // Links on the page that stay on the same site, to check a few more pages (e.g. a lesson, not only the start page).
        // Deeper pages first, one per section, and no language versions of the same page ("/sv/", "/en-gb/").
        public static List<string> SameSiteLinks(string html, string site, int max) {
            var candidates = new List<Uri>();
            foreach (Match m in Regex.Matches(html, "href\\s*=\\s*[\"'](https?://[^\"'#]+)[\"']", RegexOptions.IgnoreCase)) {
                Uri uri;
                if (!Uri.TryCreate(WebUtility.HtmlDecode(m.Groups[1].Value), UriKind.Absolute, out uri) || uri.AbsolutePath.Length <= 1) continue;
                // Pages only: a path ending in a file name must be a web page (fonts, pictures, downloads are not).
                string last = uri.Segments.Last();
                if (last.Contains(".") && !Regex.IsMatch(last, "\\.(html?|php|aspx?)$", RegexOptions.IgnoreCase)) continue;
                if (Regex.IsMatch(uri.AbsolutePath, "(login|logout|signin|sign-in|register|signup|cart|checkout|account|privacy|cookie|terms|xmlrpc|wp-json|wp-admin|feed|rss)", RegexOptions.IgnoreCase)) continue;
                if (Regex.IsMatch(uri.Segments[1], "^[a-z]{2}(-[a-z]{2})?/?$", RegexOptions.IgnoreCase) && uri.Segments.Length <= 3) continue;
                string host;
                try { host = Domains.Normalize(uri.Host); } catch { continue; }
                if (SiteDomain(host) != site || candidates.Any(c => c.AbsoluteUri == uri.AbsoluteUri)) continue;
                candidates.Add(uri);
            }
            var links = new List<string>(); var sections = new HashSet<string>();
            foreach (var uri in candidates.OrderByDescending(u => Math.Min(u.Segments.Length, 4))) {
                if (!sections.Add(uri.Host + uri.Segments[1].TrimEnd('/'))) continue;
                links.Add(uri.GetLeftPart(UriPartial.Query));
                if (links.Count >= max) break;
            }
            return links;
        }
        public static string FindBrowser() {
            var candidates = new List<string>();
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }) {
                if (String.IsNullOrEmpty(root)) continue;
                candidates.Add(Path.Combine(root, @"Microsoft\Edge\Application\msedge.exe"));
                candidates.Add(Path.Combine(root, @"Google\Chrome\Application\chrome.exe"));
            }
            return candidates.FirstOrDefault(File.Exists);
        }
        static int FreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start(); int port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            return port;
        }
        // Loads the page in a hidden browser; returns the page's HTML after scripts ran ("" if it failed).
        static string Load(string browser, string profile, int port, string url, CancellationToken cancel) {
            string args = String.Join(" ", new[] {
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
                "--disable-component-update", "--disable-sync", "--disable-extensions", "--disable-default-apps", "--mute-audio",
                "--disable-features=Translate,OptimizationHints,MediaRouter,AutofillServerCommunication,msSmartScreenProtection",
                "--user-data-dir=\"" + profile + "\"", "--proxy-server=127.0.0.1:" + port, "--proxy-bypass-list=<-loopback>",
                "--window-size=1366,2000", "--virtual-time-budget=12000", "--dump-dom", "\"" + url + "\"" });
            var psi = new ProcessStartInfo(browser, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            // A compatibility setting inherited from the parent process makes Edge quit at once.
            psi.EnvironmentVariables.Remove("__COMPAT_LAYER");
            using (var p = Process.Start(psi)) {
                var output = p.StandardOutput.ReadToEndAsync();
                p.ErrorDataReceived += (s, e) => { };
                p.BeginErrorReadLine();
                var deadline = DateTime.UtcNow.AddSeconds(45);
                while (!p.WaitForExit(250)) {
                    if (cancel.IsCancellationRequested || DateTime.UtcNow > deadline) { try { p.Kill(); } catch { } break; }
                }
                try { return output.Wait(5000) ? output.Result : ""; } catch { return ""; }
            }
        }
        static readonly Policy Everything = new Policy(
            "{\"timeZone\":\"UTC\",\"forceLock\":false,\"accountHours\":[{\"days\":[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\",\"Saturday\",\"Sunday\"],\"start\":\"00:00\",\"end\":\"24:00\"}]," +
            "\"internetHours\":[{\"days\":[\"Monday\",\"Tuesday\",\"Wednesday\",\"Thursday\",\"Friday\",\"Saturday\",\"Sunday\"],\"start\":\"00:00\",\"end\":\"24:00\"}]}", "kidguard.invalid");

        // Opens url, then up to extraPages more pages of the same site found on it. progress gets
        // (page number, total pages, url). Returns every address the browser used, with request counts.
        public static Dictionary<string, int> Run(string url, int extraPages, Action<int, int, string> progress, CancellationToken cancel) {
            string browser = FindBrowser();
            if (browser == null) throw new InvalidOperationException("Microsoft Edge or Google Chrome is needed to check a site, and neither was found.");
            Uri start;
            if (!url.Contains("://")) url = "https://" + url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out start) || (start.Scheme != "https" && start.Scheme != "http")) throw new FormatException("Type a web address, e.g. https://ucha.se/");
            string site = SiteDomain(Domains.Normalize(start.Host));
            var seen = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int port = FreePort();
            var proxy = new Proxy(() => Everything, port) { AllowEverything = true, Report = null, Observe = h => seen.AddOrUpdate(h, 1, (k, v) => v + 1) };
            string profile = Path.Combine(Path.GetTempPath(), "KidGuardSiteCheck-" + Guid.NewGuid().ToString("N"));
            proxy.Start();
            try {
                var pages = new List<string> { start.AbsoluteUri };
                for (int i = 0; i < pages.Count && !cancel.IsCancellationRequested; i++) {
                    if (progress != null) progress(i + 1, pages.Count, pages[i]);
                    string html = Load(browser, profile, port, pages[i], cancel);
                    if (i == 0 && extraPages > 0) pages.AddRange(SameSiteLinks(html, site, extraPages).Where(l => l != pages[0]));
                }
            } finally {
                proxy.Dispose();
                for (int i = 0; i < 10; i++) { try { if (Directory.Exists(profile)) Directory.Delete(profile, true); break; } catch { Thread.Sleep(500); } }
            }
            return new Dictionary<string, int>(seen, StringComparer.OrdinalIgnoreCase);
        }
    }
}
