using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    // Language, the child's helper, and version information.
    class SettingsPage : Panel {
        readonly Ctx ctx;
        readonly Func<bool> unsavedSchedule;
        readonly RadioButton english = new RadioButton { Text = "English", AutoSize = true, Margin = new Padding(0, 4, 24, 8) };
        readonly RadioButton bulgarian = new RadioButton { Text = "Български", AutoSize = true, Margin = new Padding(0, 4, 24, 8) };
        readonly CheckBox helper = new CheckBox { AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        bool loading;

        public SettingsPage(Ctx c, Func<bool> scheduleDirty) {
            ctx = c; unsavedSchedule = scheduleDirty; BackColor = UI.Back;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            english.Checked = !L.Bulgarian; bulgarian.Checked = L.Bulgarian;
            var languageBox = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
            languageBox.Controls.Add(UI.Flow(english, bulgarian, UI.Btn(L.T("Apply (restarts the Parent Panel)"), (s, e) => ApplyLanguage(), true)));
            languageBox.Controls.Add(UI.Lbl(L.T("Used for the Parent Panel and for everything {0} sees from KidGuard: warnings before sign-out and the helper next to the clock.", ctx.Child), UI.Base, UI.Muted));
            layout.Controls.Add(UI.Group(L.T("Language"), languageBox));

            helper.Text = L.T("Show the KidGuard helper next to the clock on {0}'s account", ctx.Child);
            helper.CheckedChanged += (s, e) => { if (!loading) ctx.Run(() => KidGuard.Parent.SetChildHelper(ctx.Admin, helper.Checked)); };
            var helperBox = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
            helperBox.Controls.Add(helper);
            helperBox.Controls.Add(UI.Lbl(L.T("It shows {0} how much time is left and when the internet turns off, and lets {0} ask you for a blocked site or more time.\nRequests appear in the Requests tab. Turning it off closes it within a few seconds.", ctx.Child), UI.Base, UI.Muted));
            layout.Controls.Add(UI.Group(L.T("Helper for {0}", ctx.Child), helperBox));

            var about = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
            about.Controls.Add(UI.Lbl(L.T("KidGuard version {0}", Program.Version), UI.Base));
            about.Controls.Add(UI.Lbl(L.T("Settings folder: {0}", ctx.Admin), UI.Base, UI.Muted));
            about.Controls.Add(UI.Lbl(L.T("To remove KidGuard: Settings > Apps > Installed apps > KidGuard parental controls > Uninstall."), UI.Base, UI.Muted));
            layout.Controls.Add(UI.Group(L.T("About"), about));

            for (int i = 0; i < layout.Controls.Count; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            VisibleChanged += (s, e) => { if (Visible) Reload(); };
            Reload();
        }
        public void Reload() {
            loading = true;
            helper.Checked = !Settings.Read(ctx.Admin).hideChildHelper;
            loading = false;
        }
        void ApplyLanguage() {
            string language = bulgarian.Checked ? "bg" : "en";
            if (unsavedSchedule() && MessageBox.Show(L.T("The schedule has unsaved changes. Close without saving?"), "KidGuard",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { KidGuard.Parent.SetLanguage(ctx.Admin, language); }
            catch (Exception ex) { MessageBox.Show(L.T(ex.Message), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            // Start a new Parent Panel in the new language (already elevated, so no second prompt), then close this one.
            string args = ctx.Admin == GuardService.Admin ? "" : "--admin \"" + ctx.Admin + "\"";
            try { Process.Start(Application.ExecutablePath, args); } catch (Exception ex) { MessageBox.Show(ex.Message, "KidGuard"); return; }
            // The parent already confirmed discarding unsaved changes; skip the closing question.
            var form = FindForm();
            if (form != null) form.Hide();
            Environment.Exit(0);
        }
    }
}
