using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    static class Sites {
        // "www.ucha.se" -> ["ucha.se", "*.ucha.se"]: the site and all its subdomains. Only "www." is stripped;
        // anything broader (e.g. a parent domain) must be typed deliberately.
        public static string[] WholeSite(string host) { return KidGuard.Parent.WholeSite(host); }
        public static List<string> Entries(string admin) {
            return File.ReadAllText(Path.Combine(admin, "whitelist.txt")).Split('\n').Select(x => x.Trim())
                .Where(x => x.Length > 0 && !x.StartsWith("#")).Select(x => { try { return Domains.Entry(x); } catch { return x; } }).ToList();
        }
        public static string Meaning(string entry) {
            return entry.StartsWith("*.") ? L.T("Every address ending in .{0} (not {0} itself)", entry.Substring(2)) : L.T("Only {0}", entry);
        }
        public static string MatchingRule(List<string> entries, string host) {
            foreach (var e in entries) { try { if (new Domains(e).Allows(host)) return e; } catch { } }
            return null;
        }
        public static ListView List(params string[] columns) {
            var lv = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, Font = UI.Base, BorderStyle = BorderStyle.FixedSingle };
            foreach (var c in columns) lv.Columns.Add(c);
            lv.Resize += (s, e) => {
                if (lv.Columns.Count == 0) return;
                int rest = lv.ClientSize.Width;
                for (int i = 1; i < lv.Columns.Count; i++) rest -= lv.Columns[i].Width;
                lv.Columns[0].Width = Math.Max(160, rest);
            };
            return lv;
        }
    }

    class SitesPage : Panel {
        readonly Ctx ctx;
        readonly TextBox input = new TextBox { Width = 420, Font = UI.Base, Margin = new Padding(0, 2, 8, 6) };
        readonly CheckBox whole = new CheckBox { Text = L.T("Whole site (also all its subdomains)"), AutoSize = true, Checked = true, Margin = new Padding(0, 4, 8, 6) };
        readonly CheckBox check = new CheckBox { Text = L.T("Check what the site needs first (opens it in a hidden browser, about half a minute)"), AutoSize = true, Checked = true, Margin = new Padding(0, 0, 8, 6) };
        readonly Label preview = UI.Lbl("", UI.Base, UI.Muted);
        readonly TextBox test = new TextBox { Width = 420, Font = UI.Base, Margin = new Padding(0, 2, 8, 6) };
        readonly Label testResult = UI.Lbl("", UI.Bold);
        readonly ListView list = Sites.List(L.T("Rule"), L.T("What it allows"));
        List<string> entries = new List<string>();

        public SitesPage(Ctx c) {
            ctx = c; BackColor = UI.Back; Padding = new Padding(16);
            list.Columns[1].Width = 420;
            var add = UI.Btn(L.T("Add"), (s, e) => Add(), true);
            input.TextChanged += (s, e) => UpdatePreview(); whole.CheckedChanged += (s, e) => UpdatePreview();
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); } };
            test.TextChanged += (s, e) => UpdateTest();

            var addBox = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
            addBox.Controls.Add(UI.Lbl(L.T("Paste a web address or type a site name, e.g. https://www.ucha.se/ or scratch.mit.edu"), UI.Base, UI.Muted));
            addBox.Controls.Add(UI.Flow(input, whole, add));
            addBox.Controls.Add(check);
            addBox.Controls.Add(preview);
            var testBox = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
            testBox.Controls.Add(UI.Lbl(L.T("Check whether an address is allowed:"), UI.Base, UI.Muted));
            testBox.Controls.Add(UI.Flow(test, testResult));
            foreach (Control ctl in testBox.Controls) ctl.Margin = new Padding(0);
            testResult.Margin = new Padding(0, 4, 0, 0);

            var buttons = UI.Flow(
                UI.Btn(L.T("Remove selected"), (s, e) => Remove()),
                UI.Btn(L.T("Add from site list..."), (s, e) => { using (var d = new BundlesDialog(ctx, entries)) d.ShowDialog(this); Reload(); }),
                UI.Btn(L.T("Edit as text..."), (s, e) => { using (var d = new WhitelistTextDialog(ctx)) d.ShowDialog(this); Reload(); }));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(UI.Group(L.T("Allow a site"), addBox));
            layout.Controls.Add(UI.Group(L.T("Test an address"), testBox));
            layout.Controls.Add(UI.Lbl(L.T("Allowed rules (changes apply within about 5 seconds; open pages may need a refresh)"), UI.Base, UI.Muted));
            layout.Controls.Add(list);
            layout.Controls.Add(buttons);
            buttons.Margin = new Padding(0, 8, 0, 0);
            Controls.Add(layout);
            UpdatePreview();
        }
        string[] Pending(out string error) {
            error = null;
            if (input.Text.Trim().Length == 0) return new string[0];
            try { string host = Domains.HostFromInput(input.Text); return whole.Checked ? Sites.WholeSite(host) : new[] { host }; }
            catch (Exception ex) { error = L.T(ex.Message); return new string[0]; }
        }
        void UpdatePreview() {
            string error; var p = Pending(out error);
            preview.ForeColor = error != null ? UI.Red : UI.Muted;
            preview.Text = error != null ? error : p.Length == 0 ? " " : L.T("Will add: {0}", String.Join(",  ", p));
        }
        void Add() {
            string error; var p = Pending(out error);
            if (error != null || p.Length == 0) { UpdatePreview(); return; }
            if (check.Checked) {
                string address = input.Text.Trim();
                using (var d = new SiteCheckDialog(ctx, new[] { address }, 2, L.T("Check what {0} needs", p[0]))) {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    ctx.Run(() => KidGuard.Parent.AllowRules(ctx.Admin, d.ChosenRules, p[0]));
                }
                input.Clear();
                return;
            }
            ctx.Run(() => {
                var results = p.Select(e => KidGuard.Parent.Allow(ctx.Admin, e)).ToList();
                foreach (var e in p) try { Blocked.Dismiss(ctx.Admin, e.StartsWith("*.") ? e.Substring(2) : e); } catch { }
                return String.Join(" ", results);
            });
            input.Clear();
        }
        void Remove() {
            var chosen = list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList();
            if (chosen.Count == 0) return;
            if (MessageBox.Show(L.T("Remove {0}?", String.Join(", ", chosen)), "KidGuard", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            ctx.Run(() => String.Join(" ", chosen.Select(e => KidGuard.Parent.Disallow(ctx.Admin, e))));
        }
        void UpdateTest() {
            if (test.Text.Trim().Length == 0) { testResult.Text = ""; return; }
            try {
                string host = Domains.HostFromInput(test.Text), rule = Sites.MatchingRule(entries, host);
                testResult.ForeColor = rule != null ? UI.Green : UI.Red;
                testResult.Text = rule != null ? L.T("{0} is allowed by {1}", host, rule) : L.T("{0} is blocked (no matching rule)", host);
            } catch (Exception ex) { testResult.ForeColor = UI.Red; testResult.Text = L.T(ex.Message); }
        }
        public void Reload() {
            try { entries = Sites.Entries(ctx.Admin); } catch (Exception ex) { entries = new List<string>(); preview.Text = L.T("Cannot read the allowed list: {0}", ex.Message); }
            var selected = new HashSet<string>(list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text));
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var e in entries.OrderBy(x => x.StartsWith("*.") ? x.Substring(2) : x, StringComparer.Ordinal).ThenBy(x => x, StringComparer.Ordinal)) {
                var item = new ListViewItem(new[] { e, Sites.Meaning(e) });
                item.Selected = selected.Contains(e);
                list.Items.Add(item);
            }
            list.EndUpdate();
            UpdateTest();
        }
    }

    class BundlesDialog : Form {
        public BundlesDialog(Ctx ctx, List<string> current) {
            Text = L.T("Add from site list"); Font = UI.Base; StartPosition = FormStartPosition.CenterParent; Size = new Size(760, 600);
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; BackColor = UI.Back;
            List<Bundle> bundles;
            string path = Path.Combine(ctx.Admin, "bundles.txt");
            if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bundles.txt");
            try { bundles = Bundle.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { bundles = new List<Bundle>(); MessageBox.Show(L.T("Cannot read the site list ({0}):\n{1}", path, ex.Message), "KidGuard"); }

            var have = new HashSet<string>(current);
            var box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Font = UI.Base, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
            foreach (var b in bundles) {
                bool all = b.Entries.All(have.Contains);
                box.Items.Add(new BundleItem(b, all));
            }
            var note = UI.Lbl(L.T("These are starting points. Sites often load pictures, videos or logins from other addresses:\nafter your child first uses a site, check the Blocked tab and allow what it needs."), UI.Base, UI.Muted);
            var add = UI.Btn(L.T("Add selected"), (s, e) => {
                var chosen = box.CheckedItems.Cast<BundleItem>().Select(i => i.Bundle).ToList();
                if (chosen.Count == 0) { Close(); return; }
                ctx.Run(() => {
                    int added = 0;
                    foreach (var b in chosen) foreach (var entry in b.Entries) if (ConfigStore.AllowDomain(ctx.Admin, entry)) added++;
                    Log.Write("Parent: Added from site list: " + String.Join(", ", chosen.Select(b => b.Name)) + ".");
                    return L.T("Added {0} site(s), {1} new rule(s).", chosen.Count, added);
                });
                Close();
            }, true);
            var cancel = UI.Btn("Cancel", (s, e) => Close());
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(note); layout.Controls.Add(box);
            var buttons = UI.Flow(add, cancel); buttons.Margin = new Padding(0, 10, 0, 0);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            AcceptButton = add; CancelButton = cancel;
        }
        class BundleItem {
            public readonly Bundle Bundle; readonly bool already;
            public BundleItem(Bundle b, bool all) { Bundle = b; already = all; }
            public override string ToString() {
                return Bundle.Category + "  ·  " + Bundle.Name + "   (" + String.Join(", ", Bundle.Entries) + ")" + (already ? "   - " + L.T("already allowed") : "");
            }
        }
    }

    class WhitelistTextDialog : Form {
        public WhitelistTextDialog(Ctx ctx) {
            Text = L.T("Edit allowed sites as text"); Font = UI.Base; StartPosition = FormStartPosition.CenterParent; Size = new Size(720, 640);
            MinimizeBox = false; ShowInTaskbar = false; BackColor = UI.Back;
            var text = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = UI.Mono, AcceptsReturn = true };
            text.Text = File.ReadAllText(Path.Combine(ctx.Admin, "whitelist.txt")).Replace("\r\n", "\n").Replace("\n", "\r\n");
            var help = UI.Lbl(L.T("One site per line. \"example.org\" allows only that address; \"*.example.org\" allows every address ending in .example.org.\nLines starting with # are comments. The text is checked before it is saved."), UI.Base, UI.Muted);
            var save = UI.Btn(L.T("Check and save"), (s, e) => {
                try { ctx.Notify(KidGuard.Parent.SaveWhitelist(ctx.Admin, text.Text.Replace("\r\n", "\n"))); ctx.RefreshAll(); Close(); }
                catch (Exception ex) { MessageBox.Show(L.T("Not saved.") + " " + L.T(ex.Message), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }, true);
            var cancel = UI.Btn("Cancel", (s, e) => Close());
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(help); layout.Controls.Add(text);
            var buttons = UI.Flow(save, cancel); buttons.Margin = new Padding(0, 10, 0, 0);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            CancelButton = cancel;
        }
    }

    class BlockedPage : Panel {
        readonly Ctx ctx;
        readonly ListView list = Sites.List(L.T("Address your child tried to open"), L.T("Attempts"), L.T("Last seen"), L.T("First seen"));
        readonly Label empty = UI.Lbl("", UI.Base, UI.Muted);

        public BlockedPage(Ctx c) {
            ctx = c; BackColor = UI.Back; Padding = new Padding(16);
            list.Columns[1].Width = 90; list.Columns[2].Width = 160; list.Columns[3].Width = 160;
            list.Columns[1].TextAlign = HorizontalAlignment.Right;
            var help = UI.Lbl(L.T("Addresses blocked because they are not on the allowed list. When an allowed site looks broken (missing pictures,\nvideos that will not play, login failing), open it on the child's account and check here for the addresses it needs.\nOnly allow addresses you recognise as part of that site."), UI.Base, UI.Muted);
            var buttons = UI.Flow(
                UI.Btn(L.T("Allow this address"), (s, e) => Allow(false), true),
                UI.Btn(L.T("Allow whole site"), (s, e) => Allow(true)),
                UI.Btn(L.T("Dismiss"), (s, e) => Dismiss()),
                UI.Btn(L.T("Refresh"), (s, e) => Reload()));
            buttons.Margin = new Padding(0, 8, 0, 0);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(help); layout.Controls.Add(list); layout.Controls.Add(empty); layout.Controls.Add(buttons);
            Controls.Add(layout);
        }
        List<string> Selected() { return list.SelectedItems.Cast<ListViewItem>().Select(i => i.Text).ToList(); }
        void Allow(bool wholeSite) {
            var hosts = Selected();
            if (hosts.Count == 0) { MessageBox.Show(L.T("Select one or more addresses first."), "KidGuard"); return; }
            var rules = hosts.SelectMany(h => wholeSite ? Sites.WholeSite(h) : new[] { h }).Distinct().ToList();
            if (MessageBox.Show(L.T("Allow these rules?") + "\n\n" + String.Join("\n", rules), "KidGuard", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            ctx.Run(() => {
                var results = rules.Select(r => KidGuard.Parent.Allow(ctx.Admin, r)).ToList();
                foreach (var h in hosts) try { Blocked.Dismiss(ctx.Admin, h); } catch { }
                return String.Join(" ", results);
            });
        }
        void Dismiss() {
            var hosts = Selected();
            ctx.Run(() => { foreach (var h in hosts) Blocked.Dismiss(ctx.Admin, h); return L.T("Dismissed {0} address(es).", hosts.Count); });
        }
        public void Reload() {
            BlockedEntry[] entries;
            try { entries = Blocked.Read(ctx.Admin); } catch (Exception ex) { empty.Text = L.T("Cannot read the blocked list: {0}", ex.Message); return; }
            TimeZoneInfo zone = TimeZoneInfo.Local;
            try { string ignored; zone = ctx.Policy(out ignored).Schedule.Zone; } catch { }
            var selected = new HashSet<string>(Selected());
            list.BeginUpdate(); list.Items.Clear();
            foreach (var e in entries.OrderByDescending(x => x.lastSeen, StringComparer.Ordinal)) {
                var item = new ListViewItem(new[] { e.domain, e.count.ToString(CultureInfo.InvariantCulture), UI.When(e.lastSeen, zone), UI.When(e.firstSeen, zone) });
                item.Selected = selected.Contains(e.domain);
                list.Items.Add(item);
            }
            list.EndUpdate();
            empty.Text = entries.Length == 0 ? L.T("Nothing blocked yet.") : L.T("{0} address(es).", entries.Length);
        }
    }

    class LogPage : Panel {
        readonly Ctx ctx;
        readonly TextBox text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = UI.Mono, BackColor = UI.Surface };
        readonly CheckBox important = new CheckBox { Text = L.T("Only parent actions, requests, warnings and errors"), AutoSize = true, Margin = new Padding(0, 4, 12, 8) };
        public LogPage(Ctx c) {
            ctx = c; BackColor = UI.Back; Padding = new Padding(16);
            important.CheckedChanged += (s, e) => Reload();
            var top = UI.Flow(important, UI.Btn(L.T("Refresh"), (s, e) => Reload()), UI.Lbl(L.T("Times are in the schedule's time zone. Newest at the bottom. Log lines are in English."), UI.Base, UI.Muted));
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(top); layout.Controls.Add(text);
            Controls.Add(layout);
        }
        static readonly string[] ImportantWords = { "Parent:", "Child asked", "Warned", "Fail closed", "error", "failed", "Account ", "sign-out" };
        // "2026-10-11T08:56:04.39Z message" -> "Sun 11 Oct 11:56:04  message" in the schedule's time zone.
        static string LocalTime(string line, TimeZoneInfo zone) {
            int space = line.IndexOf(' ');
            DateTime t;
            if (space < 0 || !DateTime.TryParse(line.Substring(0, space), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return line;
            return TimeZoneInfo.ConvertTimeFromUtc(t, zone).ToString("ddd dd MMM HH:mm:ss", L.Culture) + "  " + line.Substring(space + 1);
        }
        public void Reload() {
            string path = Path.Combine(ctx.Admin, "kidguard.log");
            try {
                string all;
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(f)) all = r.ReadToEnd();
                var lines = all.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);
                if (important.Checked) lines = lines.Where(l => ImportantWords.Any(k => l.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0));
                TimeZoneInfo zone = TimeZoneInfo.Local;
                try { string ignored; zone = ctx.Policy(out ignored).Schedule.Zone; } catch { }
                string shown = String.Join("\r\n", lines.Reverse().Take(400).Reverse().Select(l => LocalTime(l, zone)));
                if (shown != text.Text) { text.Text = shown; text.SelectionStart = text.TextLength; text.ScrollToCaret(); }
            } catch (FileNotFoundException) { text.Text = L.T("No log yet."); }
            catch (Exception ex) { text.Text = L.T("Cannot read the log: {0}", ex.Message); }
        }
    }
}
