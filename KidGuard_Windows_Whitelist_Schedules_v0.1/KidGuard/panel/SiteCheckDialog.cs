using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    // "Check what the site needs": opens the site(s) in a hidden browser (SiteCheck), then lets the parent
    // allow the addresses with check boxes. The caller applies ChosenRules.
    class SiteCheckDialog : Form {
        public List<string> ChosenRules = new List<string>();
        readonly Ctx ctx;
        readonly List<string> urls;
        readonly int extraPages;
        readonly Label status = UI.Lbl("", UI.Bold);
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Height = 12, Margin = new Padding(0, 0, 0, 8) };
        readonly ListView list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, Dock = DockStyle.Fill, Font = UI.Base, BorderStyle = BorderStyle.FixedSingle, ShowGroups = true };
        readonly Button allow, cancel;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        bool running;

        public SiteCheckDialog(Ctx c, IEnumerable<string> addresses, int extra, string title) {
            ctx = c; urls = addresses.ToList(); extraPages = extra;
            Text = title; Font = UI.Base; BackColor = UI.Back; Icon = UI.AppIcon; StartPosition = FormStartPosition.CenterParent;
            Size = new Size(860, 620); MinimumSize = new Size(640, 420); ShowInTaskbar = false; MinimizeBox = false;
            list.Columns.Add(L.T("Address"), 330); list.Columns.Add(L.T("Rule to add"), 260); list.Columns.Add(L.T("Times used"), 90, HorizontalAlignment.Right);
            list.Resize += (s, e) => list.Columns[1].Width = Math.Max(160, list.ClientSize.Width - list.Columns[0].Width - list.Columns[2].Width - 4);
            var help = UI.Lbl(L.T("KidGuard opens the site in a hidden browser through a test filter that lets everything through and lists every address the page uses.\nTicked: the site itself and what it needs to load. Not ticked: big general sites (allowing one opens all of it) and tracking or advertising."), UI.Base, UI.Muted);
            // Long lines (Bulgarian) wrap inside the window.
            Resize += (s, e) => help.MaximumSize = new Size(Math.Max(300, ClientSize.Width - 40), 0);
            help.MaximumSize = new Size(Size.Width - 56, 0);
            allow = UI.Btn(L.T("Allow ticked addresses"), (s, e) => Allow(), true);
            cancel = UI.Btn(L.T("Cancel"), (s, e) => { stop.Cancel(); if (!running) Close(); });
            allow.Enabled = false;
            var buttons = UI.Flow(allow, cancel); buttons.Margin = new Padding(0, 10, 0, 0);
            var note = UI.Lbl(L.T("Videos, logins or games inside a site can still need more. After your child uses the site, check the Blocked tab."), UI.Small, UI.Muted);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };
            foreach (Control ctl in new Control[] { help, status, progress }) { layout.Controls.Add(ctl); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            layout.Controls.Add(list); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(note); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(buttons); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            CancelButton = cancel;
            FormClosing += (s, e) => stop.Cancel();
            Shown += (s, e) => Start();
        }

        // From the checking thread; the dialog may already be closed.
        void Ui(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch (InvalidOperationException) { } }
        void Start() {
            running = true;
            progress.Maximum = Math.Max(1, urls.Count);
            status.Text = L.T("Opening the site in a hidden browser...");
            var token = stop.Token;
            Task.Run(() => {
                var found = new List<SiteAddress>();
                string error = null;
                for (int i = 0; i < urls.Count && !token.IsCancellationRequested; i++) {
                    int site = i;
                    try {
                        var seen = SiteCheck.Run(urls[i], extraPages, (page, pages, url) => Ui(() => {
                            status.Text = (urls.Count > 1 ? L.T("Site {0} of {1}", site + 1, urls.Count) + "  ·  " : "") + L.T("page {0} of {1}: {2}", page, pages, url);
                        }), token);
                        Uri start = new Uri(urls[i].Contains("://") ? urls[i] : "https://" + urls[i]);
                        if (!seen.ContainsKey(Domains.Normalize(start.Host))) { error = L.T("The browser could not open {0}. Check the address and the internet connection.", urls[i]); continue; }
                        found.AddRange(SiteCheck.Sort(Domains.Normalize(start.Host), seen));
                    } catch (Exception ex) { error = L.T(ex.Message); if (ex is InvalidOperationException) break; }
                    Ui(() => progress.Value = Math.Min(progress.Maximum, site + 1));
                }
                return Tuple.Create(found, error);
            }).ContinueWith(t => {
                if (IsDisposed) return;
                try { BeginInvoke((Action)(() => ShowResults(t.Result.Item1, t.Result.Item2))); } catch (InvalidOperationException) { }
            });
        }

        void ShowResults(List<SiteAddress> found, string error) {
            running = false;
            if (stop.IsCancellationRequested && found.Count == 0) { Close(); return; }
            // The same rule from several sites: keep it once, with the strongest suggestion.
            var merged = found.GroupBy(a => a.Rule).Select(g => g.OrderBy(a => a.Suggested ? 0 : 1).First()).ToList();
            List<string> entries;
            try { entries = Sites.Entries(ctx.Admin); } catch { entries = new List<string>(); }
            var groups = new Dictionary<string, ListViewGroup> {
                { "site", new ListViewGroup(L.T("Part of the site")) },
                { "needed", new ListViewGroup(L.T("Needed by the site (pictures, videos, fonts, scripts)")) },
                { "general", new ListViewGroup(L.T("Big general sites: allowing one opens all of it to your child")) },
                { "tracking", new ListViewGroup(L.T("Tracking and advertising: not needed")) }
            };
            foreach (var g in groups.Values) list.Groups.Add(g);
            list.BeginUpdate();
            foreach (var a in merged) {
                bool already = entries.Contains(a.Rule) || (!a.Rule.StartsWith("*.") && Sites.MatchingRule(entries, a.Rule) != null);
                var item = new ListViewItem(new[] { a.Host, already ? L.T("already allowed") : a.Rule, a.Count > 0 ? a.Count.ToString() : "" }) {
                    Tag = a.Rule, Group = groups[a.Kind], Checked = a.Suggested && !already, ForeColor = already ? UI.Muted : a.Suggested ? UI.Ink : UI.Muted
                };
                list.Items.Add(item);
            }
            list.EndUpdate();
            list.ItemCheck += (s, e) => { if (list.Items[e.Index].SubItems[1].Text == L.T("already allowed")) e.NewValue = CheckState.Unchecked; };
            int suggested = list.Items.Cast<ListViewItem>().Count(i => i.Checked);
            status.Text = error ?? (stop.IsCancellationRequested ? L.T("Stopped. Showing what was found so far.") : L.T("Done. {0} address(es) ticked.", suggested));
            status.ForeColor = error != null ? UI.Red : UI.Green;
            progress.Value = progress.Maximum;
            allow.Enabled = merged.Count > 0;
            cancel.Text = L.T("Cancel");
        }

        // Development aid: run the check off screen and save the finished dialog as a PNG file.
        public void Snapshot(string png) {
            StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
            Show();
            var until = DateTime.UtcNow.AddMinutes(10);
            while ((running || !allow.Enabled && status.ForeColor != UI.Red) && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(100); }
            Application.DoEvents();
            using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png); }
            Close();
        }
        void Allow() {
            ChosenRules = list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (string)i.Tag).Distinct().ToList();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
