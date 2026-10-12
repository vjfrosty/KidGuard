using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// The child's helper next to the clock: time left, internet state, and asking a parent for a site or more time.
// Started by the KidGuard service in the child's session. It only reads ChildFiles.StatusPath and drops request
// files into ChildFiles.InboxDir; the service validates them and the parent answers in the Parent Panel.
namespace KidGuard.Tray {
    static class TrayProgram {
        [STAThread]
        static int Main(string[] args) {
            string dir = ChildFiles.Dir, snapshot = null;
            // Development aids: --dir <folder with status.json and inbox>, --snapshot <folder for PNG files>.
            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == "--dir") dir = args[++i];
                else if (args[i] == "--snapshot") snapshot = args[++i];
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (snapshot != null) { Snapshots.Run(dir, snapshot); return 0; }
            bool first;
            using (new Mutex(true, "Local\\KidGuardTray", out first)) {
                if (!first) return 0;
                Application.Run(new TrayContext(dir));
            }
            return 0;
        }
    }

    static class Look {
        public static readonly Color Ink = Color.FromArgb(15, 23, 42), Muted = Color.FromArgb(100, 116, 139), Back = Color.White,
            Blue = Color.FromArgb(37, 99, 235), Green = Color.FromArgb(22, 163, 74), Red = Color.FromArgb(220, 38, 38), Amber = Color.FromArgb(180, 83, 9),
            Border = Color.FromArgb(203, 213, 225);
        public static readonly Font Base = new Font("Segoe UI", 10f), Small = new Font("Segoe UI", 8.75f), Bold = new Font("Segoe UI Semibold", 10f),
            Big = new Font("Segoe UI Semibold", 22f), Title = new Font("Segoe UI Semibold", 12f);
        static Icon icon;
        public static Icon AppIcon {
            get {
                if (icon == null) { var s = typeof(Look).Assembly.GetManifestResourceStream("KidGuard.Icon.ico"); icon = s != null ? new Icon(s) : SystemIcons.Shield; }
                return icon;
            }
        }
        public static Icon Sized(int size) { return new Icon(AppIcon, size, size); }
        public static Label Text(string text, Font font = null, Color? color = null) {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = font ?? Base, ForeColor = color ?? Ink, Margin = new Padding(0, 0, 0, 6), MaximumSize = new Size(380, 0) };
        }
        public static Button Btn(string text, EventHandler click, bool primary) {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 6),
                Font = Base, Cursor = Cursors.Hand, BackColor = primary ? Blue : Back, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = primary ? Blue : Border;
            b.Click += click;
            return b;
        }
    }

    // What the helper shows, worked out from the service's status file.
    class View {
        public ChildStatus Status;
        public bool Fresh;
        TimeZoneInfo Zone {
            get { try { return TimeZoneInfo.FindSystemTimeZoneById(Status.zone); } catch { return TimeZoneInfo.Local; } }
        }
        static DateTime? Utc(string text) {
            DateTime t;
            if (text == null || !DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t)) return null;
            return t;
        }
        public static View Read(string dir) {
            var v = new View();
            try {
                v.Status = ChildFiles.FromJson<ChildStatus>(File.ReadAllText(Path.Combine(dir, "status.json")));
                DateTime? updated = Utc(v.Status.updated);
                v.Fresh = updated.HasValue && Math.Abs((DateTime.UtcNow - updated.Value).TotalSeconds) < 40;
                L.Language = v.Status.language;
            } catch { v.Status = null; }
            return v;
        }
        // "20:00", "tomorrow 16:00", "Mon 16:00", in the schedule's time zone.
        public string Clock(string utcText) {
            DateTime? t = Utc(utcText);
            if (!t.HasValue) return "";
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(t.Value, Zone), today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone).Date;
            string clock = local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            if (local.Date == today) return clock;
            if (local.Date == today.AddDays(1)) return L.T("tomorrow {0}", clock);
            return local.ToString("ddd ", L.Culture) + clock;
        }
        public TimeSpan? Left {
            get { DateTime? end = Status == null ? null : Utc(Status.signOutAt); return end.HasValue ? end.Value - DateTime.UtcNow : (TimeSpan?)null; }
        }
        public string BigText {
            get {
                if (Status == null || !Fresh) return "--";
                if (!Status.accountAllowed) return L.T("Time is up");
                return Left.HasValue ? L.Duration(Left.Value) : L.T("No limit");
            }
        }
        public string BigDetail {
            get {
                if (Status == null) return L.T("KidGuard is not running yet.");
                if (!Fresh) return L.T("KidGuard is not answering. The times may be out of date.");
                if (!Status.accountAllowed) return L.T("Windows is about to sign you out.");
                return Left.HasValue ? L.T("computer time left (until {0})", Clock(Status.signOutAt)) : L.T("no sign-out in the next days");
            }
        }
        public string InternetText {
            get {
                if (Status == null) return "";
                if (Status.internetAllowed) return Status.internetChangeAt != null ? L.T("Internet: on until {0}", Clock(Status.internetChangeAt)) : L.T("Internet: on");
                return Status.internetChangeAt != null && Status.accountAllowed ? L.T("Internet: off (on again at {0})", Clock(Status.internetChangeAt)) : L.T("Internet: off");
            }
        }
        public Color BigColor {
            get {
                if (Status == null || !Fresh || !Status.accountAllowed) return Look.Red;
                return Left.HasValue && Left.Value.TotalMinutes <= 10 ? Look.Amber : Look.Green;
            }
        }
        public string Tooltip {
            get {
                string t = "KidGuard - " + (Status == null || !Fresh ? L.T("not answering") : Left.HasValue ? L.T("{0} left", L.Duration(Left.Value)) : L.T("no limit"));
                return t.Length > 63 ? t.Substring(0, 63) : t;
            }
        }
        public static string Describe(ChildRequest r) {
            string what = r.kind == "site" ? r.site : L.T("+{0} min", r.minutes);
            switch (r.state) {
                case "waiting": return L.T("{0} - waiting for an answer", what);
                case "allowed": return L.T("{0} - yes", what);
                case "denied": return L.T("{0} - no", what);
                default: return L.T("{0} - no answer", what);
            }
        }
    }

    class TrayContext : ApplicationContext {
        readonly string dir;
        readonly NotifyIcon icon = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly HashSet<string> announced = new HashSet<string>();
        bool firstRead = true;
        View view = new View();
        StatusWindow window;

        public TrayContext(string statusDir) {
            dir = statusDir;
            icon.Icon = Look.Sized(16);
            icon.Text = "KidGuard";
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            icon.BalloonTipClicked += (s, e) => ShowWindow();
            icon.ContextMenuStrip = new ContextMenuStrip();
            icon.ContextMenuStrip.Opening += (s, e) => BuildMenu();
            icon.Visible = true;
            timer.Tick += (s, e) => Tick();
            timer.Start();
            Tick();
        }
        void BuildMenu() {
            var m = icon.ContextMenuStrip;
            m.Items.Clear();
            m.Items.Add(view.Tooltip).Enabled = false;
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("Show"), null, (s, e) => ShowWindow());
            m.Items.Add(L.T("Ask for a site..."), null, (s, e) => AskSite());
            m.Items.Add(L.T("Ask for more time..."), null, (s, e) => AskTime());
        }
        void Tick() {
            view = View.Read(dir);
            icon.Text = view.Tooltip;
            if (view.Status != null && view.Status.requests != null) {
                foreach (var r in view.Status.requests.Where(x => x.state == "allowed" || x.state == "denied")) {
                    if (!announced.Add(r.id) || firstRead) continue;
                    // A request answered while the helper was running: tell the child.
                    string text = r.kind == "site"
                        ? (r.state == "allowed" ? L.T("Yes! You can open {0} now.", r.site) : L.T("Your parent said no to {0}.", r.site))
                        : (r.state == "allowed" ? L.T("Yes! You got {0} more minutes.", r.minutes) : L.T("Your parent said no to more time."));
                    icon.ShowBalloonTip(8000, "KidGuard", text, r.state == "allowed" ? ToolTipIcon.Info : ToolTipIcon.None);
                }
                firstRead = false;
            }
            if (window != null && window.Visible) window.Fill(view);
        }
        void ShowWindow() {
            if (window == null || window.IsDisposed) window = new StatusWindow(AskSite, AskTime);
            window.Fill(view);
            window.Place();
            window.Show(); window.Activate();
        }
        void AskSite() {
            if (window != null && window.Visible) window.Hide();
            var recent = view.Status != null && view.Status.recentBlocked != null ? view.Status.recentBlocked : new string[0];
            using (var d = new AskSiteDialog(recent)) if (d.ShowDialog() == DialogResult.OK) Send(ChildFiles.RequestText("site", d.Address, 0, d.Note));
        }
        void AskTime() {
            if (window != null && window.Visible) window.Hide();
            using (var d = new AskTimeDialog()) if (d.ShowDialog() == DialogResult.OK) Send(ChildFiles.RequestText("time", null, d.Minutes, d.Note));
        }
        void Send(string text) {
            string path = Path.Combine(dir, "inbox", "r-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            try {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.Write(bytes, 0, bytes.Length);
                icon.ShowBalloonTip(6000, "KidGuard", L.T("Sent. Your parent will see it in KidGuard."), ToolTipIcon.Info);
            } catch (Exception) {
                MessageBox.Show(L.T("Could not send the request. Ask your parent directly."), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { icon.Visible = false; icon.Dispose(); timer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // The small window above the clock.
    class StatusWindow : Form {
        readonly Label big = Look.Text("--", Look.Big), detail = Look.Text("", Look.Base, Look.Muted), internet = Look.Text("", Look.Bold),
            requestsTitle = Look.Text("", Look.Small, Look.Muted), requests = Look.Text("", Look.Small, Look.Ink);
        readonly Button askSite, askTime;

        public StatusWindow(Action askSiteAction, Action askTimeAction) {
            FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true; MaximizeBox = MinimizeBox = false;
            Text = "KidGuard"; BackColor = Look.Back; Font = Look.Base; AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; StartPosition = FormStartPosition.Manual;
            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(18, 12, 18, 10), Dock = DockStyle.Fill };
            var head = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            head.Controls.Add(new PictureBox { Image = Look.Sized(32).ToBitmap(), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 8, 0) });
            var title = Look.Text("KidGuard", Look.Title); title.Margin = new Padding(0, 5, 0, 0);
            head.Controls.Add(title);
            askSite = Look.Btn(L.T("Ask for a site..."), (s, e) => askSiteAction(), true);
            askTime = Look.Btn(L.T("Ask for more time..."), (s, e) => askTimeAction(), false);
            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 4) };
            buttons.Controls.Add(askSite); buttons.Controls.Add(askTime);
            big.Margin = new Padding(0); internet.Margin = new Padding(0, 4, 0, 4); requestsTitle.Margin = new Padding(0, 6, 0, 2);
            foreach (Control c in new Control[] { head, big, detail, internet, buttons, requestsTitle, requests }) layout.Controls.Add(c);
            Controls.Add(layout);
            Deactivate += (s, e) => Hide();
        }
        public void Fill(View v) {
            big.Text = v.BigText; big.ForeColor = v.BigColor;
            detail.Text = v.BigDetail;
            internet.Text = v.InternetText;
            internet.ForeColor = v.Status != null && v.Status.internetAllowed ? Look.Green : Look.Muted;
            var list = v.Status != null && v.Status.requests != null ? v.Status.requests.Take(5).ToList() : new List<ChildRequest>();
            requestsTitle.Text = list.Count > 0 ? L.T("What you asked for") : L.T("Is a site blocked that you need for school or a hobby? Ask your parent here.");
            requests.Text = String.Join("\n", list.Select(View.Describe));
            requests.Visible = list.Count > 0;
            askSite.Enabled = askTime.Enabled = v.Status != null;
        }
        public void Place() {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        }
    }

    abstract class AskDialog : Form {
        protected readonly TableLayoutPanel Body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(18, 14, 18, 12), Dock = DockStyle.Fill };
        protected readonly TextBox NoteBox = new TextBox { Width = 360, MaxLength = ChildFiles.MaxNote, Font = Look.Base, Margin = new Padding(0, 0, 0, 10) };
        protected readonly Button SendButton;
        public string Note { get { return NoteBox.Text.Trim(); } }
        protected AskDialog(string title) {
            Text = title; Font = Look.Base; BackColor = Look.Back; Icon = Look.AppIcon; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; AutoScaleMode = AutoScaleMode.Font;
            SendButton = Look.Btn(L.T("Send to my parent"), (s, e) => { if (Valid()) { DialogResult = DialogResult.OK; Close(); } }, true);
            Controls.Add(Body);
        }
        protected void Finish() {
            Body.Controls.Add(Look.Text(L.T("Why do you need it? (you can leave this empty)"), Look.Base, Look.Muted));
            Body.Controls.Add(NoteBox);
            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            var cancel = Look.Btn(L.T("Cancel"), (s, e) => Close(), false);
            buttons.Controls.Add(SendButton); buttons.Controls.Add(cancel);
            Body.Controls.Add(buttons);
            AcceptButton = SendButton; CancelButton = cancel;
        }
        protected abstract bool Valid();
    }

    class AskSiteDialog : AskDialog {
        readonly ListBox recent = new ListBox { Width = 360, Height = 120, Font = Look.Base, IntegralHeight = false, Margin = new Padding(0, 0, 0, 10) };
        readonly TextBox address = new TextBox { Width = 360, Font = Look.Base, Margin = new Padding(0, 0, 0, 4) };
        readonly Label problem = Look.Text("", Look.Small, Look.Red);
        public string Address { get; private set; }

        public AskSiteDialog(string[] blocked) : base(L.T("Ask for a site")) {
            Body.Controls.Add(Look.Text(L.T("Which site do you need?"), Look.Title));
            if (blocked.Length > 0) {
                Body.Controls.Add(Look.Text(L.T("Blocked in the last 30 minutes (click one):"), Look.Base, Look.Muted));
                recent.Items.AddRange(blocked);
                recent.SelectedIndexChanged += (s, e) => { if (recent.SelectedItem != null) address.Text = (string)recent.SelectedItem; };
                Body.Controls.Add(recent);
            }
            Body.Controls.Add(Look.Text(L.T("Address of the site, e.g. www.example.com"), Look.Base, Look.Muted));
            Body.Controls.Add(address);
            Body.Controls.Add(problem);
            Finish();
        }
        // A light check so the child sees mistakes at once; the service checks the address properly.
        protected override bool Valid() {
            string text = address.Text.Trim().ToLowerInvariant();
            int scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) text = text.Substring(scheme + 3);
            int end = text.IndexOfAny(new[] { '/', '?', '#', ':' });
            if (end >= 0) text = text.Substring(0, end);
            text = text.TrimEnd('.');
            if (!Regex.IsMatch(text, @"^([a-z0-9]([a-z0-9-]*[a-z0-9])?\.)+[a-z][a-z0-9-]*[a-z0-9]$") || text.Length > 253) {
                problem.Text = L.T("Type the address of a site, e.g. www.example.com");
                return false;
            }
            Address = text;
            return true;
        }
    }

    class AskTimeDialog : AskDialog {
        readonly List<RadioButton> choices = new List<RadioButton>();
        public int Minutes { get; private set; }

        public AskTimeDialog() : base(L.T("Ask for more time")) {
            Body.Controls.Add(Look.Text(L.T("How much more time do you need?"), Look.Title));
            var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            foreach (int m in ChildFiles.TimeChoices) {
                var r = new RadioButton { Text = L.T("+{0} min", m), Tag = m, AutoSize = true, Font = Look.Bold, Margin = new Padding(0, 0, 18, 0), Checked = m == 30 };
                choices.Add(r); row.Controls.Add(r);
            }
            Body.Controls.Add(row);
            Body.Controls.Add(Look.Text(L.T("Extra time is added after your normal end time, if your parent says yes."), Look.Small, Look.Muted));
            Finish();
        }
        protected override bool Valid() {
            var chosen = choices.FirstOrDefault(c => c.Checked);
            if (chosen == null) return false;
            Minutes = (int)chosen.Tag;
            return true;
        }
    }

    // Development aid: render the window and dialogs to PNG files.
    static class Snapshots {
        static void Save(Form f, string path) {
            f.StartPosition = FormStartPosition.Manual; f.ShowInTaskbar = false; f.Location = new Point(-20000, -20000);
            f.Show(); Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(path, ImageFormat.Png); }
            f.Hide();
        }
        public static void Run(string dir, string output) {
            Directory.CreateDirectory(output);
            var view = View.Read(dir);
            var w = new StatusWindow(() => { }, () => { });
            w.Fill(view);
            Save(w, Path.Combine(output, "tray-window.png"));
            Save(new AskSiteDialog(view.Status != null && view.Status.recentBlocked != null ? view.Status.recentBlocked : new string[0]), Path.Combine(output, "tray-ask-site.png"));
            Save(new AskTimeDialog(), Path.Combine(output, "tray-ask-time.png"));
            File.WriteAllText(Path.Combine(output, "tray-tooltip.txt"), view.Tooltip + "\r\n" + view.BigText + "\r\n" + view.BigDetail + "\r\n" + view.InternetText, Encoding.UTF8);
        }
    }
}
