using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    static class PanelProgram {
        [STAThread]
        static int Main(string[] args) {
            string admin = GuardService.Admin, snapshot = null, language = null, checkSite = null;
            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == "--admin") admin = args[++i];
                else if (args[i] == "--snapshot") snapshot = args[++i];
                else if (args[i] == "--language") language = args[++i];
                else if (args[i] == "--check-site") checkSite = args[++i];
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            L.Language = language ?? Settings.Read(admin).Language;
            Application.ThreadException += (s, e) => MessageBox.Show(L.T(e.Exception.Message), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            // The manifest requests elevation; this catches a copy started without it.
            bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            if (!elevated && snapshot == null) {
                MessageBox.Show(L.T("The KidGuard Parent Panel must run as administrator."), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            if (!File.Exists(Path.Combine(admin, "schedule.json"))) {
                MessageBox.Show(L.T("KidGuard configuration was not found in:\n{0}\n\nInstall KidGuard first.", admin), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            Log.DirectoryPath = admin;
            // Fix this administrator's shortcuts if they show no icon or point elsewhere (never re-adds a deleted one).
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KidGuard");
            string here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (snapshot == null && string.Equals(here, installed, StringComparison.OrdinalIgnoreCase)) { try { PanelShortcuts.Repair(installed); } catch { } }
            var form = new MainForm(admin);
            if (snapshot != null && checkSite != null) {
                var ctx = new Ctx { Admin = admin, Child = "vikto", Notify = s => { }, RefreshAll = () => { } };
                new SiteCheckDialog(ctx, new[] { checkSite }, 2, L.T("Check what {0} needs", checkSite)).Snapshot(snapshot);
                return 0;
            }
            if (snapshot != null) { form.Snapshot(snapshot); return 0; }
            Application.Run(form);
            return 0;
        }
    }

    static class UI {
        public static readonly Color Ink = Color.FromArgb(15, 23, 42), Muted = Color.FromArgb(100, 116, 139),
            Back = Color.FromArgb(248, 250, 252), Border = Color.FromArgb(226, 232, 240), Surface = Color.White,
            Blue = Color.FromArgb(37, 99, 235), BlueLight = Color.FromArgb(219, 234, 254),
            Green = Color.FromArgb(22, 163, 74), GreenLight = Color.FromArgb(220, 252, 231),
            Red = Color.FromArgb(220, 38, 38), RedLight = Color.FromArgb(254, 226, 226),
            Amber = Color.FromArgb(180, 83, 9), AmberLight = Color.FromArgb(254, 243, 199);
        public static readonly Font Base = new Font("Segoe UI", 9.75f), Small = new Font("Segoe UI", 8.25f),
            Bold = new Font("Segoe UI Semibold", 9.75f), Title = new Font("Segoe UI Semibold", 12f),
            Big = new Font("Segoe UI Semibold", 20f), Mono = new Font("Consolas", 9.75f);
        static Image logo; static Icon icon;
        // Embedded by Build.ps1 (/resource). Missing resources just mean no picture.
        public static Image Logo { get { if (logo == null) { var s = typeof(UI).Assembly.GetManifestResourceStream("KidGuard.Logo.png"); if (s != null) logo = Image.FromStream(s); } return logo; } }
        public static Icon AppIcon { get { if (icon == null) { var s = typeof(UI).Assembly.GetManifestResourceStream("KidGuard.Icon.ico"); if (s != null) icon = new Icon(s); } return icon; } }

        public static Button Btn(string text, EventHandler click, bool primary = false) {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 8), Font = Base, Cursor = Cursors.Hand,
                BackColor = primary ? Blue : Surface, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(203, 213, 225);
            // A flat button keeps its colour when disabled; grey makes clear it cannot be pressed now.
            if (primary) b.EnabledChanged += (s, e) => { b.BackColor = b.Enabled ? Blue : Color.FromArgb(203, 213, 225); b.FlatAppearance.BorderColor = b.BackColor; };
            if (click != null) b.Click += click;
            return b;
        }
        public static Label Lbl(string text, Font font = null, Color? color = null) {
            return new Label { Text = text, AutoSize = true, Font = font ?? Base, ForeColor = color ?? Ink, Margin = new Padding(0, 0, 0, 6) };
        }
        public static FlowLayoutPanel Flow(params Control[] items) {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0), Dock = DockStyle.Fill };
            f.Controls.AddRange(items);
            return f;
        }
        public static GroupBox Group(string title, Control content) {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = Bold, ForeColor = Ink,
                Padding = new Padding(10, 6, 4, 2), Margin = new Padding(0, 0, 12, 12) };
            content.Font = Base; content.Dock = DockStyle.Fill;
            g.Controls.Add(content);
            return g;
        }
        // "today 20:00", "tomorrow 16:00", "Mon 19 Oct 16:00", in the schedule's time zone.
        public static string When(DateTime utc, TimeZoneInfo zone) {
            if (utc.Year >= 9999) return L.T("until unlocked");
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone), today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
            string clock = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (local.Date == today) return L.T("today {0}", clock);
            if (local.Date == today.AddDays(1)) return L.T("tomorrow {0}", clock);
            return local.ToString("ddd d MMM", L.Culture) + " " + clock;
        }
        public static string When(string utcText, TimeZoneInfo zone) {
            DateTime? t = Overrides.Time(utcText);
            return t.HasValue ? When(t.Value, zone) : "-";
        }
    }

    // Shared by the pages: where the configuration lives and how to report results.
    class Ctx {
        public string Admin, Child;
        public Action<string> Notify;
        public Action RefreshAll;
        public Action ShowRequests;
        public void Run(Func<string> action) {
            try { Notify(action()); }
            catch (Exception ex) { MessageBox.Show(L.T(ex.Message), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            RefreshAll();
        }
        public Policy Policy(out string configError) { return ConfigStore.Load(Admin, out configError); }
    }

    class MainForm : Form {
        readonly Ctx ctx;
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill, Font = UI.Base, Padding = new Point(14, 5) };
        readonly ToolStripStatusLabel message = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly Label headerRight = new Label { Dock = DockStyle.Right, AutoSize = true, ForeColor = UI.Muted, Font = UI.Base, Padding = new Padding(0, 22, 16, 0) };
        readonly DashboardPage dashboard; readonly SchedulePage schedule; readonly SitesPage sites; readonly RequestsPage requests;
        readonly StartPagePage startPage; readonly BlockedPage blocked; readonly LogPage log; readonly SettingsPage settings;
        readonly Timer timer = new Timer { Interval = 3000 };
        TabPage requestsTab;
        int ticks;

        public MainForm(string admin) {
            string child = L.T("child");
            try { child = Installation.Read(admin).userName; } catch { }
            ctx = new Ctx { Admin = admin, Child = child, Notify = Notify, RefreshAll = RefreshAll, ShowRequests = () => tabs.SelectedTab = requestsTab };
            Text = L.T("KidGuard Parent Panel - {0}", child);
            Font = UI.Base; BackColor = UI.Back;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 800); MinimumSize = new Size(820, 540);
            Icon = UI.AppIcon;
            // Fit small screens (e.g. 1366x720): the window must never extend below the taskbar.
            Load += (s, e) => {
                var area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            };

            int headerHeight = UI.Title.Height * 3 + 6;
            var header = new Panel { Dock = DockStyle.Top, Height = headerHeight, BackColor = UI.Surface };
            header.Paint += (s, e) => { using (var pen = new Pen(UI.Border)) e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
            var logo = UI.Logo;
            if (logo != null) {
                int h = headerHeight - 16;
                header.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Left, Width = logo.Width * h / logo.Height + 24, Padding = new Padding(16, 8, 8, 8) });
            }
            var title = new Label { Text = L.T("Parent Panel") + "  ·  " + child, Dock = DockStyle.Left, AutoSize = true, ForeColor = UI.Ink, Font = UI.Title, Padding = new Padding(4, 19, 0, 0) };
            header.Controls.Add(title); title.BringToFront();
            header.Controls.Add(headerRight);

            dashboard = new DashboardPage(ctx); schedule = new SchedulePage(ctx); sites = new SitesPage(ctx); requests = new RequestsPage(ctx);
            startPage = new StartPagePage(ctx); blocked = new BlockedPage(ctx); log = new LogPage(ctx); settings = new SettingsPage(ctx, () => schedule.Dirty);
            AddTab("dashboard", L.T("Dashboard"), dashboard); AddTab("schedule", L.T("Schedule"), schedule); AddTab("allowed-sites", L.T("Allowed sites"), sites);
            requestsTab = AddTab("requests", L.T("Requests"), requests);
            AddTab("start-page", L.T("Start page"), startPage); AddTab("blocked", L.T("Blocked"), blocked); AddTab("activity-log", L.T("Activity log"), log);
            AddTab("settings", L.T("Settings"), settings);
            tabs.SelectedIndexChanged += (s, e) => RefreshVisible();

            var strip = new StatusStrip { SizingGrip = false, BackColor = UI.Surface };
            strip.Items.Add(message);
            Controls.Add(tabs); Controls.Add(header); Controls.Add(strip);

            timer.Tick += (s, e) => {
                ticks++; dashboard.RefreshStatus(); UpdateHeader();
                var page = tabs.SelectedTab.Controls[0];
                if (ticks % 3 == 0 && (page is BlockedPage || page is LogPage || page is RequestsPage)) RefreshVisible();
            };
            Load += (s, e) => { RefreshAll(); schedule.Reload(); timer.Start(); };
            FormClosing += (s, e) => {
                if (schedule.Dirty && MessageBox.Show(L.T("The schedule has unsaved changes. Close without saving?"), "KidGuard",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
            };
            Notify(L.T("Changes apply within about 5 seconds. Times are shown in the schedule's time zone."));
        }
        TabPage AddTab(string name, string title, Control page) {
            var tab = new TabPage(title) { Name = name, BackColor = UI.Back, Padding = new Padding(0) };
            page.Dock = DockStyle.Fill; tab.Controls.Add(page); tabs.TabPages.Add(tab);
            return tab;
        }
        void Notify(string text) { message.ForeColor = UI.Ink; message.Text = DateTime.Now.ToString("HH:mm:ss") + "   " + text; }
        void UpdateHeader() {
            int waiting = Requests.WaitingCount(ctx.Admin);
            string title = waiting > 0 ? L.T("Requests ({0})", waiting) : L.T("Requests");
            if (requestsTab.Text != title) requestsTab.Text = title;
            headerRight.Text = dashboard.HeaderText;
        }
        void RefreshVisible() {
            var page = tabs.SelectedTab.Controls[0];
            if (page is SitesPage) sites.Reload();
            else if (page is RequestsPage) requests.Reload();
            else if (page is StartPagePage) startPage.Reload();
            else if (page is BlockedPage) blocked.Reload();
            else if (page is LogPage) log.Reload();
        }
        void RefreshAll() { dashboard.RefreshStatus(); UpdateHeader(); sites.Reload(); requests.Reload(); startPage.Reload(); blocked.Reload(); log.Reload(); }

        // Development aid: render every tab to PNG files without user interaction.
        public void Snapshot(string dir) {
            Directory.CreateDirectory(dir);
            ShowInTaskbar = false;
            Show(); Location = new Point(-20000, -20000); Application.DoEvents();
            for (int i = 0; i < tabs.TabCount; i++) {
                tabs.SelectedIndex = i; Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height)) {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, (i + 1) + "-" + tabs.TabPages[i].Name + ".png"), ImageFormat.Png);
                }
            }
            Close();
        }
    }

    // A white status tile: small title, large coloured value, detail line.
    class Card : TableLayoutPanel {
        readonly Label value = UI.Lbl("-", UI.Big), detail = new Label { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Font = UI.Base, ForeColor = UI.Muted, Height = UI.Base.Height + 4 };
        public Card(string title) {
            ColumnCount = 1; Dock = DockStyle.Fill; BackColor = UI.Surface; Padding = new Padding(16, 12, 16, 8); Margin = new Padding(0, 0, 12, 12);
            AutoSize = true;
            Controls.Add(UI.Lbl(title.ToUpper(L.Culture), UI.Small, UI.Muted));
            Controls.Add(value); Controls.Add(detail);
        }
        public void Set(string text, Color color, string more) { value.Text = text; value.ForeColor = color; detail.Text = more; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using (var pen = new Pen(UI.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    class DashboardPage : Panel {
        readonly Ctx ctx;
        readonly Card account = new Card(L.T("Windows account")), internet = new Card(L.T("Internet")), protection = new Card(L.T("Protection"));
        readonly Label alert = new Label { AutoSize = true, Font = UI.Bold, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 0, 12), Visible = false };
        readonly LinkLabel asked = new LinkLabel { AutoSize = true, Font = UI.Bold, Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 0, 0, 12), Visible = false,
            BackColor = UI.BlueLight, LinkColor = UI.Blue, ActiveLinkColor = UI.Blue };
        readonly Label overrides = UI.Lbl("", UI.Base);
        readonly CheckBox withInternet = new CheckBox { Text = L.T("Include internet"), AutoSize = true, Margin = new Padding(0, 4, 8, 8) };
        public string HeaderText = "";

        public DashboardPage(Ctx c) {
            ctx = c; BackColor = UI.Back;
            // Fixed-width column so the cards share the page width and the button groups wrap.
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            cards.Controls.Add(account, 0, 0); cards.Controls.Add(internet, 1, 0); cards.Controls.Add(protection, 2, 0);
            layout.Controls.Add(cards);
            layout.Controls.Add(alert);
            asked.LinkClicked += (s, e) => ctx.ShowRequests();
            layout.Controls.Add(asked);

            layout.Controls.Add(UI.Lbl(L.T("Temporary changes"), UI.Title));
            layout.Controls.Add(overrides);

            string a = ctx.Admin;
            var lockRow = UI.Flow(
                UI.Btn(L.T("Lock now"), (s, e) => Confirm(L.T("Lock now? Your child will be signed out immediately and may lose unsaved work."), () => KidGuard.Parent.Lock(a, null))),
                UI.Btn(L.T("Lock for 1 hour"), (s, e) => Confirm(L.T("Lock for 1 hour? Your child will be signed out immediately."), () => KidGuard.Parent.Lock(a, 60))),
                UI.Btn(L.T("Unlock"), (s, e) => ctx.Run(() => KidGuard.Parent.Unlock(a)), true));
            var bonusRow = UI.Flow(
                UI.Btn(L.T("+15 min"), (s, e) => ctx.Run(() => KidGuard.Parent.Bonus(a, 15, withInternet.Checked))),
                UI.Btn(L.T("+30 min"), (s, e) => ctx.Run(() => KidGuard.Parent.Bonus(a, 30, withInternet.Checked))),
                UI.Btn(L.T("+1 hour"), (s, e) => ctx.Run(() => KidGuard.Parent.Bonus(a, 60, withInternet.Checked))),
                withInternet);
            var netRow = UI.Flow(
                UI.Btn(L.T("Pause 30 min"), (s, e) => ctx.Run(() => KidGuard.Parent.PauseInternet(a, 30))),
                UI.Btn(L.T("Pause 1 hour"), (s, e) => ctx.Run(() => KidGuard.Parent.PauseInternet(a, 60))),
                UI.Btn(L.T("Resume internet"), (s, e) => ctx.Run(() => KidGuard.Parent.ResumeInternet(a)), true));
            var groups = UI.Flow(UI.Group(L.T("Lock"), lockRow), UI.Group(L.T("Extra time (added after today's end time)"), bonusRow), UI.Group(L.T("Internet"), netRow),
                UI.Group(L.T("Reset"), UI.Flow(UI.Btn(L.T("Clear all temporary changes"), (s, e) => ctx.Run(() => KidGuard.Parent.ClearOverrides(a))))));
            groups.Margin = new Padding(0, 8, 0, 0);
            layout.Controls.Add(groups);
            layout.Controls.Add(UI.Lbl(L.T("The child sees a warning 10 minutes and 1 minute before sign-out, and 5 minutes before the internet turns off."), UI.Small, UI.Muted));
            for (int i = 0; i < layout.Controls.Count; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            Resize += (s, e) => alert.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 48), 0);
        }
        void Confirm(string question, Func<string> action) {
            if (MessageBox.Show(question, "KidGuard", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) ctx.Run(action);
        }
        static ServiceStatus ReadStatus(string admin, out double ageSeconds) {
            ageSeconds = double.MaxValue;
            try {
                string path = Path.Combine(admin, "status.json");
                if (!File.Exists(path)) return null;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path)))) {
                    var s = (ServiceStatus)new DataContractJsonSerializer(typeof(ServiceStatus)).ReadObject(ms);
                    DateTime? t = Overrides.Time(s.updated);
                    if (t.HasValue) ageSeconds = (DateTime.UtcNow - t.Value).TotalSeconds;
                    return s;
                }
            } catch { return null; }
        }
        // English service state (for the logic) and its translation (for display).
        static string ServiceState() {
            try { using (var sc = new ServiceController("KidGuard")) return sc.Status.ToString(); }
            catch (InvalidOperationException) { return "Not installed"; }
        }
        static string StateText(string state) {
            switch (state) {
                case "Running": return L.T("running");
                case "Stopped": return L.T("stopped");
                case "Not installed": return L.T("not installed");
                case "StartPending": return L.T("starting");
                case "StopPending": return L.T("stopping");
                default: return state.ToLowerInvariant();
            }
        }
        public void RefreshStatus() {
            DateTime now = DateTime.UtcNow;
            string configError = null; Policy p = null;
            try { p = ctx.Policy(out configError); } catch (Exception ex) { configError = ex.Message; }
            TimeZoneInfo zone = p != null ? p.Schedule.Zone : TimeZoneInfo.Local;
            double age; var st = ReadStatus(ctx.Admin, out age);
            string service = ServiceState(), serviceText = StateText(service);
            bool reporting = st != null && age >= -5 && age < 20 && service == "Running";
            // Without fresh service data, show what the configuration says (and say so).
            if (!reporting && p != null) { st = ServiceStatus.From(p, now, configError); st.enforcing = false; }

            if (st == null || st.failClosedReason != null) {
                account.Set(L.T("BLOCKED"), UI.Red, L.T("Protection failed closed"));
                internet.Set(L.T("OFF"), UI.Red, "");
            } else {
                account.Set(st.accountAllowed ? L.T("ALLOWED") : L.T("BLOCKED"), st.accountAllowed ? UI.Green : UI.Red,
                    st.nextAccountChange == null ? L.T("No change in the next 8 days") :
                    st.accountAllowed ? L.T("Sign-out {0}", UI.When(st.nextAccountChange, zone)) : L.T("Opens {0}", UI.When(st.nextAccountChange, zone)));
                internet.Set(st.internetAllowed ? L.T("ON") : L.T("OFF"), st.internetAllowed ? UI.Green : UI.Red,
                    st.nextInternetChange == null ? L.T("No change in the next 8 days") :
                    st.internetAllowed ? L.T("Turns off {0}", UI.When(st.nextInternetChange, zone)) : L.T("Turns on {0}", UI.When(st.nextInternetChange, zone)));
            }
            if (reporting) protection.Set(L.T("ACTIVE"), UI.Green, L.T("Service running") + " · " + (st.childSessions > 0 ? L.T("child signed in") : L.T("child not signed in")));
            else protection.Set(service == "Not installed" ? L.T("NOT INSTALLED") : L.T("NOT REPORTING"), UI.Amber, L.T("Service: {0}", serviceText));
            HeaderText = (st != null && st.accountAllowed ? L.T("Account allowed") : L.T("Account blocked")) + "  ·  " +
                (st != null && st.internetAllowed ? L.T("Internet on") : L.T("Internet off")) + "  ·  " + L.T("Service: {0}", serviceText);

            string problem = st != null && st.childIsAdmin ? L.T("Your child's account has administrator rights. With them KidGuard can be removed or bypassed.\nMake it a Standard account (Settings > Accounts > Other users > Change account type) or remove it from the Administrators group.") :
                st != null && st.failClosedReason != null ? L.T("Protection failed closed: {0}\nThe child is signed out and web access is blocked until this is fixed.", st.failClosedReason) :
                configError != null ? L.T("A settings file has an error: {0}\nKidGuard keeps using the last valid settings. Fix it from the Schedule or Allowed sites tab.", configError) :
                !reporting ? (service == "Not installed" ? L.T("The KidGuard service is not installed.") : L.T("The KidGuard service is not reporting ({0}).", serviceText)) +
                    " " + L.T("The status above is calculated from the settings, not confirmed by the service.") : null;
            alert.Visible = problem != null;
            if (problem != null) { alert.Text = problem; bool severe = st != null && (st.failClosedReason != null || st.childIsAdmin); alert.BackColor = severe ? UI.RedLight : UI.AmberLight; alert.ForeColor = severe ? UI.Red : UI.Amber; }

            int waiting = Requests.WaitingCount(ctx.Admin);
            asked.Visible = waiting > 0;
            if (waiting > 0) {
                asked.Text = L.T("{0} is asking you for {1} thing(s).", ctx.Child, waiting) + "  " + L.T("Open Requests");
                asked.LinkArea = new LinkArea(asked.Text.Length - L.T("Open Requests").Length, L.T("Open Requests").Length);
            }

            var lines = new List<string>();
            if (p != null) {
                var o = p.Overrides;
                if (p.Schedule.forceLock) lines.Add(L.T("Locked by schedule file (forceLock). Press Unlock to clear."));
                if (o.Locked(now)) lines.Add(o.lockUntil == Overrides.Forever ? L.T("Locked until you unlock.") : L.T("Locked until {0}.", UI.When(o.lockUntil, zone)));
                if (o.Bonus(now)) lines.Add(o.bonusInternet ? L.T("Extra time until {0} (with internet).", UI.When(o.bonusUntil, zone)) : L.T("Extra time until {0} (offline only).", UI.When(o.bonusUntil, zone)));
                if (o.InternetPaused(now)) lines.Add(L.T("Internet paused until {0}.", UI.When(o.internetPausedUntil, zone)));
            }
            overrides.Text = lines.Count == 0 ? L.T("None. The weekly schedule applies.") : String.Join("\n", lines);
        }
    }
}
