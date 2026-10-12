using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

// KidGuard setup wizard. All checks and system changes are done by Install.ps1; this is only the front end.
// Texts from Install.ps1 (account problems and notes) are English and translated here by exact match (L.T).
namespace KidGuard.Setup {
#pragma warning disable 0649 // filled by the JSON deserializer
    [DataContract] class AccountInfo {
        [DataMember] public string name;
        [DataMember] public string fullName;
        [DataMember] public string sid;
        [DataMember] public bool enabled;
        [DataMember] public string issue;
        [DataMember] public string kind;
        [DataMember] public string note;
    }
    [DataContract] class MachineInfo {
        [DataMember] public string state;
        [DataMember] public string root;
        [DataMember] public string[] machineIssues;
        [DataMember] public string[] machineWarnings;
        [DataMember] public AccountInfo[] accounts;
    }
#pragma warning restore 0649

    static class SetupProgram {
        [STAThread]
        static int Main(string[] args) {
            string package = null, preview = null, infoFile = null;
            L.Language = L.Default;
            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == "--package") package = args[++i];
                else if (args[i] == "--preview") preview = args[++i];
                else if (args[i] == "--info") infoFile = args[++i];
                else if (args[i] == "--language") L.Language = args[++i];
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Development aid: render the first pages from a saved -ListCandidates JSON, without running anything.
            if (preview != null && infoFile != null) { new Wizard(package ?? ".") { InfoFile = infoFile }.Preview(preview); return 0; }
            if (package == null || !File.Exists(Path.Combine(package, "Install.ps1"))) {
                MessageBox.Show(L.T("Start setup by double-clicking Setup.cmd in the KidGuard folder."), L.T("KidGuard setup"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            Application.Run(new Wizard(package));
            return 0;
        }
    }

    class Wizard : Form {
        static readonly Color Ink = Color.FromArgb(15, 23, 42), Muted = Color.FromArgb(100, 116, 139), Back = Color.FromArgb(248, 250, 252),
            Blue = Color.FromArgb(37, 99, 235), Green = Color.FromArgb(22, 163, 74), Red = Color.FromArgb(220, 38, 38), Amber = Color.FromArgb(180, 83, 9);
        static readonly Font Base = new Font("Segoe UI", 9.75f), Bold = new Font("Segoe UI Semibold", 9.75f),
            Title = new Font("Segoe UI Semibold", 15f), Mono = new Font("Consolas", 9f);
        static readonly string[] Steps = { "Welcome", "Choose account", "Confirm", "Installing", "Finish" };

        readonly string package;
        readonly Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 8), BackColor = Back };
        readonly Label[] stepLabels = new Label[Steps.Length];
        readonly Button back, next, cancel;
        int step;
        MachineInfo info; AccountInfo chosen;
        bool reuse = true, startPage = true, installing, succeeded;
        // Account page controls (kept so Refresh can update them).
        ListView accountList; Label accountDetail, accountProblems; CheckBox reuseBox;
        TextBox output; Label progressText; ProgressBar progress;

        public Wizard(string packageDir) {
            package = packageDir;
            Font = Base; BackColor = Back; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font; Size = new Size(860, 600); MinimumSize = new Size(760, 520);
            MaximizeBox = false;
            var iconStream = typeof(Wizard).Assembly.GetManifestResourceStream("KidGuard.Icon.ico");
            if (iconStream != null) Icon = new Icon(iconStream);

            var side = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = Ink, Padding = new Padding(18, 22, 10, 10) };
            var sideFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var shield = Resource("KidGuard.Shield.png");
            if (shield != null) sideFlow.Controls.Add(new PictureBox { Image = shield, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(96, 96), Margin = new Padding(26, 0, 0, 6) });
            sideFlow.Controls.Add(new Label { Text = "KidGuard", ForeColor = Color.White, Font = Title, AutoSize = true, Margin = new Padding(0, 0, 0, 22) });
            for (int i = 0; i < Steps.Length; i++) {
                stepLabels[i] = new Label { AutoSize = true, Font = Base, Margin = new Padding(0, 0, 0, 12) };
                sideFlow.Controls.Add(stepLabels[i]);
            }
            side.Controls.Add(sideFlow);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                Padding = new Padding(12, 10, 20, 12), BackColor = Color.White };
            cancel = Btn("", (s, e) => Close(), false);
            next = Btn("", (s, e) => Next(), true);
            back = Btn("", (s, e) => GoTo(step - 1), false);
            bottom.Controls.Add(cancel); bottom.Controls.Add(next); bottom.Controls.Add(back);

            Controls.Add(content); Controls.Add(bottom); Controls.Add(side);
            FormClosing += (s, e) => {
                if (installing) { e.Cancel = true; MessageBox.Show(L.T("Please wait until setup finishes."), L.T("KidGuard setup")); }
            };
            GoTo(0);
        }

        // Embedded by Setup.ps1 (/resource); absent in preview builds.
        static Image Resource(string name) {
            var s = typeof(Wizard).Assembly.GetManifestResourceStream(name);
            return s == null ? null : Image.FromStream(s);
        }
        static Button Btn(string text, EventHandler click, bool primary) {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(96, 0), FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(8, 0, 0, 0), BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(203, 213, 225);
            if (primary) b.EnabledChanged += (s, e) => { b.BackColor = b.Enabled ? Blue : Color.FromArgb(203, 213, 225); b.FlatAppearance.BorderColor = b.BackColor; };
            b.Click += click;
            return b;
        }
        static Label Text_(string text, Font font = null, Color? color = null) {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Font = font ?? Base, ForeColor = color ?? Ink, Margin = new Padding(0, 0, 0, 10), MaximumSize = new Size(600, 0) };
        }
        TableLayoutPanel Page(string title, string subtitle) {
            content.Controls.Clear();
            // Scrolls when a page is taller than the window (longer texts, many warnings).
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // Vertical scrolling only.
            t.HorizontalScroll.Maximum = 0; t.HorizontalScroll.Visible = false; t.AutoScroll = true;
            t.Controls.Add(Text_(title, Title));
            if (subtitle != null) t.Controls.Add(Text_(subtitle, Base, Muted));
            content.Controls.Add(t);
            return t;
        }
        static void Fill(TableLayoutPanel t) {
            for (int i = 0; i < t.Controls.Count; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        void GoTo(int s) {
            step = s;
            Text = L.T("KidGuard setup");
            for (int i = 0; i < Steps.Length; i++) {
                stepLabels[i].Text = (i + 1) + "   " + L.T(Steps[i]);
                stepLabels[i].ForeColor = i == step ? Color.White : i < step ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
                stepLabels[i].Font = i == step ? Bold : Base;
            }
            back.Text = L.T("Back"); cancel.Text = L.T("Cancel");
            back.Visible = step == 1 || step == 2;
            next.Visible = step <= 2; cancel.Visible = step <= 2;
            next.Text = step == 2 ? L.T("Install") : L.T("Next");
            switch (step) {
                case 0: ShowWelcome(); break;
                case 1: ShowAccounts(); break;
                case 2: ShowConfirm(); break;
                case 3: ShowInstall(); break;
                case 4: ShowFinish(); break;
            }
        }
        void Next() {
            if (step == 1 && (chosen == null || chosen.issue != null)) return;
            GoTo(step + 1);
        }

        void ShowWelcome() {
            var t = Page(L.T("Set up KidGuard"), L.T("Parental controls for one Windows account on this computer."));
            var languages = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            languages.Controls.Add(Text_(L.T("Language:"), Bold));
            foreach (var pair in new[] { Tuple.Create("en", "English"), Tuple.Create("bg", "Български") }) {
                string code = pair.Item1;
                var b = Btn(pair.Item2, (s, e) => { L.Language = code; GoTo(0); }, L.Language == code);
                b.Margin = new Padding(8, 0, 0, 0); b.MinimumSize = new Size(0, 0);
                languages.Controls.Add(b);
            }
            languages.Controls[0].Margin = new Padding(0, 5, 0, 0);
            t.Controls.Add(languages);
            t.Controls.Add(Text_(L.T("KidGuard will:"), Bold));
            t.Controls.Add(Text_(L.T("•  allow your child's Windows account only at the times you choose, and sign it out afterwards\n•  allow internet only at the times you choose, and only to websites on your allowed list\n•  show your child the time left, and let them ask you for a blocked site or more time\n•  add the KidGuard Parent Panel to your Start menu, to change all of this later")));
            t.Controls.Add(Text_(L.T("Before you continue:"), Bold));
            t.Controls.Add(Text_(L.T("•  Your child needs their own Windows account (local, Microsoft, work/school or domain), not an administrator, signed in at least once and now signed out.\n•  Keep your own administrator account and password. KidGuard can only be changed or removed with it.\n•  This is an engineering preview. Try it on a test account first and run the checklist in tests\\WINDOWS-ACCEPTANCE.md.")));
            Fill(t);
            next.Enabled = true;
        }

        public string InfoFile;
        public void Preview(string dir) {
            Directory.CreateDirectory(dir);
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
            base.Show(); Application.DoEvents();
            for (int i = 0; i <= 2; i++) {
                if (i == 2) { if (chosen == null || chosen.issue != null) break; }
                GoTo(i); Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(Path.Combine(dir, "setup-" + (i + 1) + ".png")); }
            }
            Close();
        }
        void LoadInfo() {
            info = null;
            string stdout, stderr;
            int code = 0;
            if (InfoFile != null) { stdout = File.ReadAllText(InfoFile); stderr = ""; }
            else code = RunPowerShell(new[] { "-ListCandidates" }, out stdout, out stderr);
            string json = stdout.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{"));
            if (code != 0 || json == null) throw new Exception(stderr.Trim().Length > 0 ? stderr.Trim() : L.T("Could not list the accounts on this computer."));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                info = (MachineInfo)new DataContractJsonSerializer(typeof(MachineInfo)).ReadObject(ms);
            if (info.accounts == null) info.accounts = new AccountInfo[0];
            if (info.machineIssues == null) info.machineIssues = new string[0];
            if (info.machineWarnings == null) info.machineWarnings = new string[0];
        }
        static string KindText(string kind) {
            switch (kind) {
                case "microsoft": return L.T("Microsoft");
                case "entra": return L.T("Work/school");
                case "domain": return L.T("Domain");
                default: return L.T("Local");
            }
        }
        void ShowAccounts() {
            var t = Page(L.T("Choose your child's account"), L.T("Only the selected account is restricted. Your own account stays unrestricted."));
            accountList = new ListView { View = View.Details, FullRowSelect = true, ShowItemToolTips = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill,
                Height = 190, BorderStyle = BorderStyle.FixedSingle, Font = Base };
            accountList.Columns.Add(L.T("Account"), 130); accountList.Columns.Add(L.T("Type"), 100); accountList.Columns.Add(L.T("Status"), 300);
            accountList.Resize += (s, e) => accountList.Columns[2].Width = Math.Max(200, accountList.ClientSize.Width - 230 - 4);
            accountList.SelectedIndexChanged += (s, e) => UpdateChoice();
            accountDetail = Text_(" ", Bold); accountProblems = Text_("", Bold, Red);
            reuseBox = new CheckBox { Text = L.T("Keep allowed sites and schedule from the previous KidGuard installation"), Checked = reuse, AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 8) };
            reuseBox.CheckedChanged += (s, e) => reuse = reuseBox.Checked;
            var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            var refresh = Btn(L.T("Refresh list"), (s, e) => RefreshAccounts(), false); refresh.Margin = new Padding(0, 0, 8, 0);
            var settings = Btn(L.T("Open Windows account settings"), (s, e) => { try { Process.Start("ms-settings:otherusers"); } catch { } }, false); settings.Margin = new Padding(0);
            tools.Controls.Add(refresh); tools.Controls.Add(settings);
            t.Controls.Add(accountList); t.Controls.Add(accountDetail); t.Controls.Add(accountProblems); t.Controls.Add(reuseBox); t.Controls.Add(tools);
            t.Controls.Add(Text_(L.T("No suitable account? Local, Microsoft, work/school and domain accounts can all be used. To add one: Windows Settings > Accounts > Other users > Add account. Sign in to it once, sign out, then press Refresh list."), Base, Muted));
            Fill(t);
            t.RowStyles[2] = new RowStyle(SizeType.Absolute, 150);
            RefreshAccounts();
        }
        void RefreshAccounts() {
            Cursor = Cursors.WaitCursor;
            string previous = chosen != null ? chosen.sid : null;
            try { LoadInfo(); }
            catch (Exception ex) { accountProblems.Text = ex.Message; next.Enabled = false; Cursor = Cursors.Default; return; }
            finally { Cursor = Cursors.Default; }
            accountList.BeginUpdate(); accountList.Items.Clear();
            foreach (var a in info.accounts.OrderBy(a => a.issue != null).ThenBy(a => a.name)) {
                string status = a.issue == null ? L.T("Ready") + (a.enabled ? "" : " " + L.T("(account is disabled now)")) : L.T(a.issue);
                var item = new ListViewItem(new[] { a.name + (String.IsNullOrEmpty(a.fullName) || a.fullName == a.name ? "" : " (" + a.fullName + ")"), KindText(a.kind), status }) { Tag = a, ToolTipText = status };
                item.ForeColor = a.issue == null ? Green : Muted;
                item.Selected = a.sid == previous || (previous == null && a.issue == null && info.accounts.Count(x => x.issue == null) == 1);
                accountList.Items.Add(item);
            }
            accountList.EndUpdate();
            var problems = info.machineIssues.Select(x => L.T(x)).ToList();
            if (info.state == "installed") problems.Insert(0, L.T("KidGuard is already installed on this computer. To change it, use the Parent Panel. To install it again, first remove it: Settings > Apps > Installed apps > KidGuard parental controls > Uninstall."));
            problems.AddRange(info.machineWarnings.Select(w => L.T("Warning: {0}", L.T(w))));
            accountProblems.Text = String.Join("\n", problems);
            reuseBox.Visible = info.state == "leftover";
            UpdateChoice();
        }
        void UpdateChoice() {
            chosen = accountList.SelectedItems.Count == 1 ? (AccountInfo)accountList.SelectedItems[0].Tag : null;
            bool blocked = info == null || info.state == "installed" || info.machineIssues.Length > 0;
            if (chosen == null) { accountDetail.Text = L.T("Select an account."); accountDetail.ForeColor = Muted; }
            else if (chosen.issue != null) { accountDetail.Text = L.T("{0} cannot be used yet: {1}", chosen.name, L.T(chosen.issue)); accountDetail.ForeColor = Amber; }
            else { accountDetail.Text = L.T("{0} will be protected.", chosen.name) + (chosen.note != null ? "\n" + L.T(chosen.note) : ""); accountDetail.ForeColor = chosen.note != null ? Amber : Green; }
            next.Enabled = !blocked && chosen != null && chosen.issue == null;
        }

        void ShowConfirm() {
            var t = Page(L.T("Ready to install"), null);
            bool leftover = info.state == "leftover";
            t.Controls.Add(Text_(L.T("Protected account:") + "   " + chosen.name + (String.IsNullOrEmpty(chosen.fullName) ? "" : "  (" + chosen.fullName + ")"), Bold));
            t.Controls.Add(Text_(L.T("Setup will:"), Bold));
            t.Controls.Add(Text_(
                L.T("•  build KidGuard on this computer and run its self-tests (about a minute)") + "\n" +
                L.T("•  block sign-in for {0} briefly while setting up; the schedule then decides when it can sign in", chosen.name) + "\n" +
                L.T("•  set Edge, Chrome and Firefox on {0} to go through KidGuard's website filter", chosen.name) + "\n" +
                L.T("•  add a background service, a watchdog task, network filter rules, a Start menu shortcut and an Apps & features entry") + "\n" +
                L.T("•  show a KidGuard helper next to the clock on {0}'s account (it can be turned off in the Parent Panel)", chosen.name) + "\n" +
                (leftover ? (reuse ? L.T("•  move the previous KidGuard folder aside and keep its allowed sites and schedule") : L.T("•  move the previous KidGuard folder aside")) + "\n" : "")));
            t.Controls.Add(Text_((leftover && reuse ? L.T("Starting settings: your previous schedule and allowed sites (if they are valid).") :
                L.T("Starting settings: the files in the package's config folder. Unchanged, that means weekdays 16:00-20:00 and weekends 09:00-20:00, with internet until 19:30 and a few learning sites.")) +
                " " + L.T("You can change everything in the Parent Panel afterwards.")));
            if (File.Exists(Path.Combine(package, @"config\startpage.html"))) {
                string text = L.T("Put the kids' start page on {0}'s desktop and allow the sites it links to", chosen.name);
                // A check box does not wrap its text by itself: give it room for every line.
                int height = TextRenderer.MeasureText(text, Bold, new Size(570, 0), TextFormatFlags.WordBreak).Height;
                var box = new CheckBox { Text = text, Checked = startPage, AutoSize = false, Size = new Size(600, height + 8), Font = Bold, Margin = new Padding(0, 0, 0, 10) };
                box.CheckedChanged += (s, e) => startPage = box.Checked;
                t.Controls.Add(box);
            }
            if (chosen.note != null) t.Controls.Add(Text_(L.T(chosen.note), Base, Amber));
            t.Controls.Add(Text_(L.T("At the end of allowed time {0} is signed out and unsaved work can be lost. KidGuard shows a warning 10 minutes and 1 minute before.", chosen.name), Bold, Amber));
            Fill(t);
            next.Enabled = true;
        }

        void ShowInstall() {
            var t = Page(L.T("Installing"), L.T("Please wait. Do not sign in to {0} until setup has finished.", chosen.name));
            progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Dock = DockStyle.Fill, Height = 14, Margin = new Padding(0, 0, 0, 10) };
            progressText = Text_(L.T("Starting..."), Bold);
            output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Mono, BackColor = Color.White };
            t.Controls.Add(progress); t.Controls.Add(progressText); t.Controls.Add(output);
            Fill(t);
            t.RowStyles[t.RowStyles.Count - 1] = new RowStyle(SizeType.Percent, 100);
            installing = true;
            var args = new System.Collections.Generic.List<string> { "-ChildSid", chosen.sid, "-Language", L.Language };
            if (info.state == "leftover") { args.Add("-ArchivePrevious"); if (reuse) args.Add("-ReuseSettings"); }
            if (startPage && File.Exists(Path.Combine(package, @"config\startpage.html"))) args.Add("-StartPage");
            var p = Start(args.ToArray());
            DataReceivedEventHandler append = (s, e) => {
                if (e.Data == null) return;
                BeginInvoke((Action)(() => {
                    output.AppendText(e.Data + "\r\n");
                    string line = e.Data.Trim();
                    if (line.Length > 0 && !line.StartsWith("At ") && !line.StartsWith("+")) progressText.Text = line.Length > 90 ? line.Substring(0, 90) + "..." : line;
                }));
            };
            p.OutputDataReceived += append; p.ErrorDataReceived += append;
            p.EnableRaisingEvents = true;
            // Exited fires on a pool thread; WaitForExit() drains the output events, whose UI appends are queued before this.
            p.Exited += (s, e) => {
                p.WaitForExit();
                bool ok = p.ExitCode == 0;
                BeginInvoke((Action)(() => { installing = false; succeeded = ok; GoTo(4); }));
            };
            p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }

        void ShowFinish() {
            string log = output != null ? output.Text : "";
            TableLayoutPanel t;
            if (succeeded) {
                t = Page(L.T("KidGuard is installed"), L.T("{0} is now protected.", chosen.name));
                string panel = Path.Combine(info.root, "KidGuardPanel.exe");
                t.Controls.Add(Text_(L.T("Open the Parent Panel to check the schedule and allowed websites. You can find it later in the Start menu as \"KidGuard Parent Panel\"."), Base));
                var open = Btn(L.T("Open Parent Panel"), (s, e) => { try { Process.Start(panel); Close(); } catch (Exception ex) { MessageBox.Show(ex.Message, L.T("KidGuard setup")); } }, true);
                open.Margin = new Padding(0, 0, 0, 14);
                t.Controls.Add(open);
                t.Controls.Add(Text_(L.T("Before relying on it, sign in to {0} during allowed time and check that allowed sites open and other sites are blocked (see tests\\WINDOWS-ACCEPTANCE.md).", chosen.name), Base, Muted));
            } else {
                t = Page(L.T("Setup did not finish"), L.T("Changes were rolled back where possible. The details are below."));
                t.Controls.Add(Text_(L.T("Fix the problem shown in the last lines and run Setup.cmd again. If an account was left disabled, see \"Diagnose and recover\" in README.md."), Bold, Red));
            }
            var details = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Mono, BackColor = Color.White, Text = log };
            t.Controls.Add(details);
            Fill(t);
            t.RowStyles[t.RowStyles.Count - 1] = new RowStyle(SizeType.Percent, 100);
            details.SelectionStart = details.TextLength; details.ScrollToCaret();
            var finish = Btn(succeeded ? L.T("Finish") : L.T("Close"), (s, e) => Close(), !succeeded);
            next.Parent.Controls.Add(finish);
        }

        Process Start(string[] scriptArgs) {
            string install = Path.Combine(package, "Install.ps1");
            var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + install + "\" " + String.Join(" ", scriptArgs)) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = package
            };
            return new Process { StartInfo = psi };
        }
        int RunPowerShell(string[] scriptArgs, out string stdout, out string stderr) {
            using (var p = Start(scriptArgs)) {
                p.Start();
                var err = p.StandardError.ReadToEndAsync();
                stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                stderr = err.Result;
                return p.ExitCode;
            }
        }
    }
}
