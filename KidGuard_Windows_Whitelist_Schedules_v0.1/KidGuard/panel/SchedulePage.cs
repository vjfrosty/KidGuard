using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KidGuard.ParentPanel {
    // Monday-first week of 15-minute cells. Drag a rectangle to paint; starting on an allowed cell erases.
    class WeekGridControl : Control {
        public const int Slot = 15, Slots = 1440 / Slot;
        static readonly string[] Short = { L.T("Mon"), L.T("Tue"), L.T("Wed"), L.T("Thu"), L.T("Fri"), L.T("Sat"), L.T("Sun") };
        public bool[,] Account = new bool[7, Slots], Internet = new bool[7, Slots];
        public bool EditInternet;
        public TimeZoneInfo Zone = TimeZoneInfo.Local;
        public event EventHandler Changed;
        bool dragging, paintValue; int d0, s0, d1, s1, hoverD = -1, hoverS = -1; bool[,] before;

        public WeekGridControl() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UI.Surface; Font = UI.Base; Cursor = Cursors.Cross;
        }
        bool[,] Target { get { return EditInternet ? Internet : Account; } }
        int Left0 { get { return Font.Height * 3; } }
        int Top0 { get { return Font.Height * 2 + 6; } }
        int Bottom0 { get { return Font.Height * 2 + 8; } }
        float CellW { get { return Math.Max(2f, (ClientSize.Width - Left0 - 12) / (float)Slots); } }
        float RowH { get { return Math.Max(Font.Height + 6f, Math.Min(Font.Height * 3.2f, (ClientSize.Height - Top0 - Bottom0) / 7f)); } }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
        bool Hit(Point p, out int d, out int s) {
            d = (int)Math.Floor((p.Y - Top0) / RowH); s = (int)Math.Floor((p.X - Left0) / CellW);
            bool inside = d >= 0 && d < 7 && s >= 0 && s < Slots;
            d = Clamp(d, 0, 6); s = Clamp(s, 0, Slots - 1);
            return inside;
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            int d, s;
            if (e.Button != MouseButtons.Left || !Hit(e.Location, out d, out s)) return;
            dragging = true; d0 = d1 = d; s0 = s1 = s;
            paintValue = !Target[d, s];
            before = (bool[,])Target.Clone();
            Apply();
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            int d, s; bool inside = Hit(e.Location, out d, out s);
            if (dragging) { d1 = d; s1 = s; Apply(); }
            int hd = inside ? d : -1, hs = inside ? s : -1;
            if (hd != hoverD || hs != hoverS) { hoverD = hd; hoverS = hs; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverD = hoverS = -1; Invalidate(); }
        void Apply() {
            var t = Target;
            Array.Copy(before, t, before.Length);
            for (int d = Math.Min(d0, d1); d <= Math.Max(d0, d1); d++)
                for (int s = Math.Min(s0, s1); s <= Math.Max(s0, s1); s++) t[d, s] = paintValue;
            Invalidate();
        }
        static string Clock(int minute) { return (minute / 60 % 24).ToString("00") + ":" + (minute % 60).ToString("00"); }

        Color CellColor(int d, int s) {
            if (!EditInternet) return Account[d, s] ? UI.Blue : Color.FromArgb(241, 245, 249);
            if (Internet[d, s]) return Account[d, s] ? UI.Green : Color.FromArgb(187, 247, 208);
            return Account[d, s] ? UI.BlueLight : Color.FromArgb(241, 245, 249);
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics;
            g.Clear(BackColor);
            float cw = CellW, rh = RowH; int left = Left0, top = Top0;
            float gridW = cw * Slots;
            DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
            int today = ((int)localNow.DayOfWeek + 6) % 7;

            // Hour labels: as dense as the width allows.
            int step = 1;
            float labelW = g.MeasureString("00:00", UI.Small).Width + 4;
            while (cw * 4 * step < labelW && step < 6) step = step == 1 ? 2 : step == 2 ? 3 : 6;
            using (var muted = new SolidBrush(UI.Muted))
                for (int h = 0; h <= 24; h += step) {
                    float x = left + h * 4 * cw;
                    if (h == 24 && cw * 4 * step < labelW * 1.5f) continue;
                    string text = h.ToString("00") + ":00";
                    var size = g.MeasureString(text, UI.Small);
                    g.DrawString(text, UI.Small, muted, Math.Min(x - size.Width / 2, left + gridW - size.Width), top - size.Height - 2);
                }

            for (int d = 0; d < 7; d++) {
                float y = top + d * rh;
                using (var brush = new SolidBrush(d == today ? UI.Blue : UI.Ink))
                    g.DrawString(Short[d], d == today ? UI.Bold : UI.Base, brush, 4, y + (rh - Font.Height) / 2);
                // Fill runs of equal colour in one call.
                for (int s = 0; s < Slots; ) {
                    Color c = CellColor(d, s); int e2 = s + 1;
                    while (e2 < Slots && CellColor(d, e2) == c) e2++;
                    using (var b = new SolidBrush(c)) g.FillRectangle(b, left + s * cw, y + 2, (e2 - s) * cw, rh - 4);
                    s = e2;
                }
            }
            using (var faint = new Pen(Color.FromArgb(40, 15, 23, 42)))
            using (var strong = new Pen(Color.FromArgb(90, 15, 23, 42)))
                for (int h = 0; h <= 24; h++) {
                    float x = left + h * 4 * cw;
                    g.DrawLine(h % 6 == 0 ? strong : faint, x, top, x, top + 7 * rh);
                }
            // Now marker on today's row.
            float nx = left + (localNow.Hour * 60 + localNow.Minute) / (float)Slot * cw, ny = top + today * rh;
            using (var red = new Pen(UI.Red, 2)) g.DrawLine(red, nx, ny, nx, ny + rh);
            if (dragging) {
                float x0 = left + Math.Min(s0, s1) * cw, y0 = top + Math.Min(d0, d1) * rh;
                using (var pen = new Pen(UI.Ink, 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, x0, y0 + 1, (Math.Abs(s1 - s0) + 1) * cw, (Math.Abs(d1 - d0) + 1) * rh - 2);
            }
            float by = top + 7 * rh + 8;
            using (var ink = new SolidBrush(UI.Ink)) {
                if (hoverD >= 0) {
                    bool on = Target[hoverD, hoverS];
                    string info = Short[hoverD] + " " + Clock(hoverS * Slot) + "-" + Clock((hoverS + 1) * Slot) + ": " +
                        (EditInternet ? (Internet[hoverD, hoverS] ? (Account[hoverD, hoverS] ? L.T("internet on") : L.T("internet on, but Windows time is off here (no effect)")) : L.T("internet off"))
                                      : (on ? L.T("Windows allowed") : L.T("Windows blocked")));
                    g.DrawString(info, UI.Bold, ink, left, by);
                } else g.DrawString(L.T("Drag to mark allowed time. Start the drag on a coloured cell to erase."), UI.Base, ink, left, by);
            }
            float lx = left;
            float ly = by + Font.Height + 4;
            var legend = EditInternet
                ? new[] { Tuple.Create(UI.Green, L.T("Internet on")), Tuple.Create(UI.BlueLight, L.T("Windows only (offline)")), Tuple.Create(Color.FromArgb(187, 247, 208), L.T("Internet on but no Windows time (no effect)")) }
                : new[] { Tuple.Create(UI.Blue, L.T("Windows account allowed")), Tuple.Create(Color.FromArgb(241, 245, 249), L.T("Not allowed")) };
            using (var muted = new SolidBrush(UI.Muted))
                foreach (var item in legend) {
                    using (var b = new SolidBrush(item.Item1)) g.FillRectangle(b, lx, ly + 3, 14, 10);
                    g.DrawRectangle(Pens.Silver, lx, ly + 3, 14, 10);
                    g.DrawString(item.Item2, UI.Small, muted, lx + 18, ly);
                    lx += 18 + g.MeasureString(item.Item2, UI.Small).Width + 16;
                }
        }
    }

    class SchedulePage : Panel {
        readonly Ctx ctx;
        readonly RadioButton modeAccount = new RadioButton { Text = L.T("Windows account hours"), AutoSize = true, Checked = true, Margin = new Padding(0, 4, 16, 0) };
        readonly RadioButton modeInternet = new RadioButton { Text = L.T("Internet hours"), AutoSize = true, Margin = new Padding(0, 4, 24, 0) };
        readonly ComboBox zone = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, Margin = new Padding(0, 0, 8, 0) };
        readonly WeekGridControl grid = new WeekGridControl { Dock = DockStyle.Fill };
        readonly Label summary = UI.Lbl("", UI.Base), warning = UI.Lbl("", UI.Bold, UI.Amber);
        readonly Button save, undo, copy;
        Schedule loaded;
        public bool Dirty { get; private set; }

        public SchedulePage(Ctx c) {
            ctx = c; BackColor = UI.Back; Padding = new Padding(16, 10, 16, 8);
            foreach (var z in TimeZoneInfo.GetSystemTimeZones()) zone.Items.Add(new ZoneItem(z));
            save = UI.Btn(L.T("Save schedule"), (s, e) => Save(), true);
            undo = UI.Btn(L.T("Undo changes"), (s, e) => Reload());
            copy = UI.Btn(L.T("Internet = Windows hours"), (s, e) => { Array.Copy(grid.Account, grid.Internet, grid.Account.Length); MarkDirty(); });
            var clear = UI.Btn(L.T("Clear all"), (s, e) => {
                var t = modeInternet.Checked ? grid.Internet : grid.Account; Array.Clear(t, 0, t.Length); MarkDirty();
            });
            modeAccount.CheckedChanged += (s, e) => { grid.EditInternet = modeInternet.Checked; copy.Visible = modeInternet.Checked; UpdateSummary(); grid.Invalidate(); };
            zone.SelectedIndexChanged += (s, e) => { var z = zone.SelectedItem as ZoneItem; if (z != null && loaded != null && z.Zone.Id != loaded.timeZone) MarkDirty(); if (z != null) grid.Zone = z.Zone; grid.Invalidate(); };
            grid.Changed += (s, e) => MarkDirty();

            var top = UI.Flow(modeAccount, modeInternet, UI.Lbl(L.T("Time zone"), UI.Base, UI.Muted), zone);
            top.Margin = new Padding(0, 0, 0, 8);
            foreach (Control ctl in top.Controls) if (ctl is Label) ctl.Margin = new Padding(0, 4, 6, 0);
            var buttons = UI.Flow(save, undo, copy, clear);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            // Buttons above the grid: always visible, also on short screens.
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(top); layout.Controls.Add(buttons); layout.Controls.Add(grid); layout.Controls.Add(warning); layout.Controls.Add(summary);
            Controls.Add(layout);
            copy.Visible = false;
        }
        class ZoneItem {
            public readonly TimeZoneInfo Zone;
            public ZoneItem(TimeZoneInfo z) { Zone = z; }
            public override string ToString() { return Zone.DisplayName; }
        }
        void MarkDirty() { Dirty = true; UpdateSummary(); grid.Invalidate(); }

        public void Reload() {
            string error = null;
            try { loaded = Schedule.Parse(File.ReadAllText(Path.Combine(ctx.Admin, "schedule.json"))); }
            catch (Exception ex) {
                error = ex.Message;
                try { string ignored; loaded = ctx.Policy(out ignored).Schedule; } catch { loaded = null; }
            }
            if (loaded == null) { warning.Text = L.T("The schedule cannot be read: {0}", error); return; }
            grid.Account = WeekGrid.FromWindows(loaded.accountHours, WeekGridControl.Slot);
            grid.Internet = WeekGrid.FromWindows(loaded.internetHours, WeekGridControl.Slot);
            grid.Zone = loaded.Zone;
            foreach (ZoneItem z in zone.Items) if (z.Zone.Id == loaded.timeZone) { zone.SelectedItem = z; break; }
            var notes = new List<string>();
            if (error != null) notes.Add(L.T("schedule.json has an error ({0}); showing the last valid schedule. Saving replaces the broken file.", error));
            if (!WeekGrid.Exact(loaded.accountHours, WeekGridControl.Slot) || !WeekGrid.Exact(loaded.internetHours, WeekGridControl.Slot))
                notes.Add(L.T("Some times are not on 15-minute steps; saving from this grid rounds them."));
            if (loaded.forceLock) notes.Add(L.T("The schedule file has a manual lock (forceLock). Use Unlock on the Dashboard to clear it."));
            warning.Text = String.Join("\n", notes);
            Dirty = error != null;
            UpdateSummary(); grid.Invalidate();
        }
        void UpdateSummary() {
            save.Enabled = undo.Enabled = Dirty;
            save.Text = Dirty ? L.T("Save schedule") + " *" : L.T("Save schedule");
            summary.Text = L.T("Windows: {0}", Describe(WeekGrid.ToWindows(grid.Account, WeekGridControl.Slot))) + "\n" +
                L.T("Internet: {0}", Describe(WeekGrid.ToWindows(grid.Internet, WeekGridControl.Slot))) + "   " + L.T("(internet also needs Windows time)");
        }
        static string Describe(Window[] windows) {
            if (windows.Length == 0) return L.T("never");
            return String.Join(";  ", windows.Select(w => DaysText(w.days) + " " + w.start + "-" + w.end));
        }
        // ["Monday".."Friday"] -> "Mon-Fri"; non-consecutive days are listed.
        static string DaysText(string[] days) {
            var idx = days.Select(d => Array.IndexOf(WeekGrid.Days, d)).OrderBy(i => i).ToList();
            var parts = new List<string>();
            for (int i = 0; i < idx.Count; ) {
                int j = i; while (j + 1 < idx.Count && idx[j + 1] == idx[j] + 1) j++;
                string a = L.T(WeekGrid.Days[idx[i]].Substring(0, 3)), b = L.T(WeekGrid.Days[idx[j]].Substring(0, 3));
                parts.Add(j == i ? a : j == i + 1 ? a + ", " + b : a + "-" + b);
                i = j + 1;
            }
            return String.Join(", ", parts);
        }
        void Save() {
            var z = zone.SelectedItem as ZoneItem;
            if (z == null) { MessageBox.Show(L.T("Choose a time zone."), "KidGuard"); return; }
            var s = new Schedule {
                timeZone = z.Zone.Id, forceLock = loaded != null && loaded.forceLock,
                accountHours = WeekGrid.ToWindows(grid.Account, WeekGridControl.Slot),
                internetHours = WeekGrid.ToWindows(grid.Internet, WeekGridControl.Slot)
            };
            if (s.accountHours.Length == 0 && MessageBox.Show(L.T("No Windows time is allowed at all. Your child will not be able to sign in. Save anyway?"),
                "KidGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            // On failure keep the parent's edits on screen.
            try { ctx.Notify(KidGuard.Parent.SaveSchedule(ctx.Admin, s.ToJson())); }
            catch (Exception ex) { MessageBox.Show(L.T(ex.Message), "KidGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Reload(); ctx.RefreshAll();
        }
    }
}
