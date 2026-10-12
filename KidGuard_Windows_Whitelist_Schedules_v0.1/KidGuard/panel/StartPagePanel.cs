using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    // The child's start page: an HTML page of links on the child's desktop, with its sites allowed.
    class StartPagePage : Panel {
        readonly Ctx ctx; readonly string child;
        readonly Label state = UI.Lbl("", UI.Title), info = UI.Lbl("", UI.Base, UI.Muted);
        readonly ListView links = Sites.List(L.T("Site the page links to"), L.T("Allowed?"));
        readonly Button toggle, allow, check;

        public StartPagePage(Ctx c) {
            ctx = c; child = c.Child; BackColor = UI.Back; Padding = new Padding(16, 12, 16, 8);
            links.Columns[1].Width = 360;
            toggle = UI.Btn(L.T("Turn on"), (s, e) => Toggle(), true);
            allow = UI.Btn(L.T("Allow all its sites"), (s, e) => ctx.Run(() => StartPage.AllowLinks(ctx.Admin)));
            check = UI.Btn(L.T("Check all its sites..."), (s, e) => CheckAll());
            var buttons = UI.Flow(toggle, allow, check,
                UI.Btn(L.T("Use another page..."), (s, e) => Import()),
                UI.Btn(L.T("Preview"), (s, e) => Preview()));
            buttons.Margin = new Padding(0, 4, 0, 8);
            var note = UI.Lbl(L.T("The page is copied to a folder only {0} and administrators can read. While it is on, a shortcut with the KidGuard icon is kept on {0}'s desktop\n(it appears within a few seconds after {0} signs in, and comes back if deleted). Allowing a site also allows the extra addresses listed for it in the site list.", child),
                UI.Small, UI.Muted);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (Control ctl in new Control[] { state, info, buttons }) { layout.Controls.Add(ctl); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            layout.Controls.Add(links); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(note); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            note.Margin = new Padding(0, 6, 0, 0);
            Controls.Add(layout);
        }

        public void Reload() {
            string source = StartPage.SourcePath(ctx.Admin);
            bool exists = File.Exists(source), enabled = false;
            try { enabled = StartPage.ReadSettings(ctx.Admin).enabled; } catch { }
            links.BeginUpdate(); links.Items.Clear();
            if (!exists) {
                state.Text = L.T("No start page yet"); state.ForeColor = UI.Muted;
                info.Text = L.T("Choose an HTML page with links for {0} (\"Use another page...\").", child);
                toggle.Enabled = allow.Enabled = check.Enabled = false;
                links.EndUpdate();
                return;
            }
            string html = File.ReadAllText(source);
            var hosts = StartPage.LinkHosts(html);
            List<string> entries;
            try { entries = Sites.Entries(ctx.Admin); } catch { entries = new List<string>(); }
            int blocked = 0;
            foreach (string host in hosts) {
                string rule = Sites.MatchingRule(entries, host);
                if (rule == null) blocked++;
                links.Items.Add(new ListViewItem(new[] { host, rule != null ? L.T("Allowed by {0}", rule) : L.T("Not allowed yet") }) { ForeColor = rule != null ? UI.Green : UI.Red });
            }
            links.EndUpdate();
            var deployed = StartPage.DeployedShortcuts(ctx.Admin);
            state.Text = (enabled ? L.T("ON") : L.T("OFF")) + "  -  " + StartPage.Title(html);
            state.ForeColor = enabled ? UI.Green : UI.Muted;
            info.Text = (blocked == 0 ? L.T("{0} linked sites, all allowed.", hosts.Count) : L.T("{0} linked sites, {1} not allowed yet (press \"Allow all its sites\").", hosts.Count, blocked)) + "\n" +
                (enabled ? (deployed.Count > 0 ? L.T("Shortcut on the desktop: {0}", String.Join("; ", deployed)) : L.T("The shortcut appears on {0}'s desktop after {0} signs in.", child))
                         : L.T("Not on {0}'s desktop. Press \"Turn on\" to put it there and allow its sites.", child));
            toggle.Enabled = check.Enabled = true;
            toggle.Text = enabled ? L.T("Turn off (remove from desktop)") : L.T("Turn on: put on {0}'s desktop", child);
            toggle.BackColor = enabled ? UI.Surface : UI.Blue; toggle.ForeColor = enabled ? UI.Ink : Color.White;
            allow.Enabled = blocked > 0;
        }
        // Opens every link of the page in a hidden browser and allows what the sites need to load fully.
        void CheckAll() {
            var links = StartPage.Links(File.ReadAllText(StartPage.SourcePath(ctx.Admin)));
            if (MessageBox.Show(L.T("KidGuard will open each of the {0} links in a hidden browser to find every address the sites need. This takes about {1} minutes. Continue?", links.Count, Math.Max(1, links.Count * 20 / 60)),
                "KidGuard", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            using (var d = new SiteCheckDialog(ctx, links, 0, L.T("Check all sites of the start page"))) {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ctx.Run(() => KidGuard.Parent.AllowRules(ctx.Admin, d.ChosenRules, StartPage.Title(File.ReadAllText(StartPage.SourcePath(ctx.Admin)))));
            }
        }
        void Toggle() {
            bool enabled = false;
            try { enabled = StartPage.ReadSettings(ctx.Admin).enabled; } catch { }
            ctx.Run(() => enabled ? StartPage.Disable(ctx.Admin) : StartPage.Enable(ctx.Admin));
        }
        void Import() {
            using (var dialog = new OpenFileDialog { Title = L.T("Choose the start page for {0}", child), Filter = L.T("Web pages") + " (*.html;*.htm)|*.html;*.htm" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ctx.Run(() => {
                    string result = StartPage.Import(ctx.Admin, dialog.FileName);
                    if (MessageBox.Show(L.T("Allow all the sites this page links to?"), "KidGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        result += " " + StartPage.AllowLinks(ctx.Admin);
                    return result;
                });
            }
        }
        // Opens a copy in the parent's normal (not elevated) browser: explorer.exe hands the file to the desktop shell.
        void Preview() {
            try {
                string copy = Path.Combine(Path.GetTempPath(), "KidGuard-start-page-preview.html");
                File.Copy(StartPage.SourcePath(ctx.Admin), copy, true);
                Process.Start("explorer.exe", "\"" + copy + "\"");
            } catch (Exception ex) { MessageBox.Show(ex.Message, "KidGuard"); }
        }
    }
}
