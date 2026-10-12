using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    // What the child asked for in the helper next to the clock: a blocked site, or more time.
    class RequestsPage : Panel {
        readonly Ctx ctx;
        readonly ListView list;
        readonly Label empty = UI.Lbl("", UI.Base, UI.Muted);
        readonly Button wholeSite, address, time, deny;
        readonly CheckBox withInternet = new CheckBox { Text = L.T("with internet"), AutoSize = true, Checked = true, Margin = new Padding(0, 4, 16, 8) };

        public RequestsPage(Ctx c) {
            ctx = c; BackColor = UI.Back; Padding = new Padding(16);
            list = Sites.List(L.T("Request"), L.T("Why (in {0}'s words)", ctx.Child), L.T("Asked"), L.T("Answer"));
            list.MultiSelect = false;
            list.Columns[1].Width = 320; list.Columns[2].Width = 150; list.Columns[3].Width = 220;
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            var help = UI.Lbl(L.T("{0} can ask for a blocked site or for more time with the KidGuard icon next to the clock. {0} sees your answer within a few seconds.", ctx.Child) + "\n" +
                L.T("Allow only sites you know. \"Check and allow\" opens the site in a hidden browser first and lists every address it needs to load."), UI.Base, UI.Muted);
            wholeSite = UI.Btn(L.T("Check and allow the site..."), (s, e) => CheckAndAllow(), true);
            address = UI.Btn(L.T("Allow only this address"), (s, e) => Answer(r => KidGuard.Parent.AllowRequestedSite(ctx.Admin, r.id, false)));
            time = UI.Btn(L.T("Give the time"), (s, e) => Answer(r => KidGuard.Parent.GiveRequestedTime(ctx.Admin, r.id, withInternet.Checked)), true);
            deny = UI.Btn(L.T("Say no"), (s, e) => Answer(r => KidGuard.Parent.DenyRequest(ctx.Admin, r.id)));
            var buttons = UI.Flow(wholeSite, address, time, withInternet, deny,
                UI.Btn(L.T("Clear answered"), (s, e) => ctx.Run(() => KidGuard.Parent.ClearAnsweredRequests(ctx.Admin))));
            buttons.Margin = new Padding(0, 8, 0, 0);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(help); layout.Controls.Add(list); layout.Controls.Add(empty); layout.Controls.Add(buttons);
            Controls.Add(layout);
            UpdateButtons();
        }
        ChildRequest Selected { get { return list.SelectedItems.Count == 1 ? (ChildRequest)list.SelectedItems[0].Tag : null; } }
        void UpdateButtons() {
            var r = Selected;
            bool waiting = r != null && r.state == "waiting";
            wholeSite.Enabled = address.Enabled = waiting && r.kind == "site";
            time.Enabled = withInternet.Enabled = waiting && r.kind == "time";
            deny.Enabled = waiting;
            if (waiting && r.kind == "site") address.Text = L.T("Allow only {0}", r.site);
            else address.Text = L.T("Allow only this address");
            if (waiting && r.kind == "time") time.Text = L.T("Give {0} more minutes", r.minutes);
            else time.Text = L.T("Give the time");
        }
        // Opens the site in a hidden browser first, so everything it needs to load is allowed with it.
        void CheckAndAllow() {
            var r = Selected;
            if (r == null || r.kind != "site") return;
            using (var d = new SiteCheckDialog(ctx, new[] { "https://" + r.site + "/" }, 2, L.T("Check what {0} needs", r.site))) {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ctx.Run(() => KidGuard.Parent.AllowRequestedSite(ctx.Admin, r.id, d.ChosenRules));
            }
        }
        void Answer(Func<ChildRequest, string> action) {
            var r = Selected;
            if (r == null) return;
            ctx.Run(() => action(r));
        }
        static string What(ChildRequest r) { return r.kind == "site" ? r.site : L.T("{0} more minutes", r.minutes); }
        static string AnswerText(ChildRequest r, TimeZoneInfo zone) {
            switch (r.state) {
                case "waiting": return L.T("Waiting for you");
                case "allowed": return (r.kind == "site" ? L.T("Allowed: {0}", r.answer) : L.T("Time given")) + " (" + UI.When(r.answered, zone) + ")";
                case "denied": return L.T("You said no") + " (" + UI.When(r.answered, zone) + ")";
                default: return L.T("No answer within a day");
            }
        }
        public void Reload() {
            ChildRequest[] all;
            try { all = Requests.Read(ctx.Admin); } catch (Exception ex) { empty.Text = L.T("Cannot read the requests: {0}", ex.Message); return; }
            TimeZoneInfo zone = TimeZoneInfo.Local;
            try { string ignored; zone = ctx.Policy(out ignored).Schedule.Zone; } catch { }
            string selected = Selected != null ? Selected.id : null;
            list.BeginUpdate(); list.Items.Clear();
            foreach (var r in all.OrderBy(x => x.state == "waiting" ? 0 : 1).ThenByDescending(x => x.asked, StringComparer.Ordinal)) {
                var item = new ListViewItem(new[] { What(r), r.note ?? "", UI.When(r.asked, zone), AnswerText(r, zone) }) { Tag = r };
                item.ForeColor = r.state == "waiting" ? UI.Ink : UI.Muted;
                if (r.state == "waiting") item.Font = UI.Bold;
                item.Selected = r.id == selected;
                list.Items.Add(item);
            }
            list.EndUpdate();
            int waiting = all.Count(r => r.state == "waiting");
            empty.Text = all.Length == 0 ? L.T("No requests yet.") : L.T("{0} waiting, {1} answered.", waiting, all.Length - waiting);
            UpdateButtons();
        }
    }
}
