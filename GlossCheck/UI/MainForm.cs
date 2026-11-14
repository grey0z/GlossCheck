using System;
using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Settings;

namespace GlossCheck.UI
{
    /// <summary>
    /// Màn hình chính: thanh trạng thái + nút ORIGIN/START/STOP/RESET ở trên, 5 tab ở giữa, log ở dưới.
    /// Mọi thao tác chỉ gửi lệnh vào Machine (hàng đợi); UI không tự điều khiển phần cứng.
    /// </summary>
    public class MainForm : Form
    {
        private readonly Machine _machine;
        private readonly Timer _timer;
        private Label _lblState;
        private Label _lblAlarm;
        private Label _lblRecipe;
        private ConnectionBar _connectionBar;
        private Button _btnOrigin, _btnStart, _btnStop, _btnReset, _btnMute;
        private TabControl _tabs;
        private ListBox _lstLog;

        public MainForm(Machine machine)
        {
            _machine = machine;
            Text = "GlossCheck - Máy đo độ bóng 2 jig" + (machine.Sim != null ? "   [MÔ PHỎNG]" : "");
            Font = Ui.NormalFont;
            MinimumSize = new Size(1280, 800);
            Size = new Size(1400, 900);
            StartPosition = FormStartPosition.CenterScreen;

            BuildLayout();

            _timer = new Timer { Interval = 100 };
            _timer.Tick += (s, e) => RefreshView();
            _timer.Start();
        }

        private void BuildLayout()
        {
            // ---------- Header ----------
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 84, ColumnCount = 3, Padding = new Padding(6), BackColor = Color.White };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // không có dòng này hàng tự giãn => chữ bị cắt

            _lblState = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(3) };
            var info = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            info.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            info.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _lblRecipe = new Label { Dock = DockStyle.Fill, Font = Ui.TitleFont, TextAlign = ContentAlignment.MiddleLeft };
            _lblAlarm = new Label { Dock = DockStyle.Fill, Font = Ui.BoldFont, ForeColor = Ui.NgColor, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; // chữ dài: "…" (bản đầy đủ ở log)
            _lblAlarm.Click += (s, e) => { if (_machine.ActiveAlarm != null) ShowAlarmPopup(); }; // mở lại popup đã đóng
            info.Controls.Add(_lblRecipe, 0, 0);
            info.Controls.Add(_lblAlarm, 0, 1);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            _btnOrigin = BigButton("ORIGIN", Color.LightSkyBlue, (s, e) => _machine.CmdOrigin());
            _btnStart = BigButton("START", Color.FromArgb(150, 220, 150), (s, e) => _machine.CmdStart());
            _btnStop = BigButton("STOP", Color.Khaki, (s, e) => _machine.CmdStop());
            _btnReset = BigButton("RESET", Color.LightCoral, (s, e) => _machine.CmdReset());
            // TẮT CÒI: im còi ngay, alarm vẫn còn (vẫn phải RESET). Nhấp nháy khi còi đang kêu.
            _btnMute = new Button { Text = "TẮT\r\nCÒI", Width = 80, Height = 64, Font = Ui.BoldFont, Margin = new Padding(12, 3, 3, 3) };
            _btnMute.Click += (s, e) => _machine.CmdMuteBuzzer();
            buttons.Controls.AddRange(new Control[] { _btnOrigin, _btnStart, _btnStop, _btnReset, _btnMute });
            header.ColumnStyles[2].Width = 620;
            if (_machine.Sim != null)
            {
                // Chế độ mô phỏng: nút mở cửa sổ popup giả lập phần cứng
                header.ColumnStyles[2].Width = 730;
                var btnSim = new Button { Text = "MÔ\r\nPHỎNG", Width = 90, Height = 64, BackColor = Color.Plum, Font = Ui.BoldFont, Margin = new Padding(6, 3, 3, 3) };
                btnSim.Click += (s, e) => ShowSimulation();
                buttons.Controls.Add(btnSim);
            }

            header.Controls.Add(_lblState, 0, 0);
            header.Controls.Add(info, 1, 0);
            header.Controls.Add(buttons, 2, 0);

            // ---------- Tabs ----------
            _tabs = new TabControl { Dock = DockStyle.Fill, Font = Ui.TitleFont, Padding = new Point(18, 6) };
            AddPage("Auto", new AutoPage(_machine));
            AddPage("Manual", new ManualPage(_machine));
            AddPage("Sản lượng", new ProductionPage(_machine));
            AddPage("Teaching", new TeachingPage(_machine));
            AddPage("Model", new RecipePage(_machine));
            AddPage("Config", new ConfigPage(_machine));
            AddPage("Alarm", new AlarmPage(_machine, ShowAlarmPopup));
            _tabs.SelectedIndexChanged += (s, e) => RefreshView();

            // ---------- Log + status ----------
            _lstLog = new ListBox { Dock = DockStyle.Bottom, Height = 130, Font = new Font("Consolas", 9.5F), HorizontalScrollbar = true, IntegralHeight = false };
            _connectionBar = new ConnectionBar(_machine) { Dock = DockStyle.Bottom };

            Controls.Add(_tabs);
            Controls.Add(_lstLog);
            Controls.Add(_connectionBar);
            Controls.Add(header);
        }

        private SimulationForm _simulationForm;
        private AlarmForm _alarmForm;
        private MachineAlarm _popupAlarm;

        /// <summary>Hiện popup cho alarm đang có (tự gọi khi có alarm mới, hoặc khi bấm vào dòng alarm).</summary>
        private void ShowAlarmPopup()
        {
            var alarm = _machine.ActiveAlarm;
            if (alarm == null) return;
            if (_alarmForm == null || _alarmForm.IsDisposed) _alarmForm = new AlarmForm(_machine);
            _alarmForm.ShowAlarm(alarm, this);
        }

        /// <summary>Alarm mới -> mở popup; alarm đã RESET -> đóng popup.</summary>
        private void UpdateAlarmPopup()
        {
            var alarm = _machine.ActiveAlarm;
            if (alarm != _popupAlarm)
            {
                _popupAlarm = alarm;
                if (alarm != null) ShowAlarmPopup();
                else if (_alarmForm != null && !_alarmForm.IsDisposed && _alarmForm.Visible) _alarmForm.Hide();
            }
            if (_alarmForm != null && !_alarmForm.IsDisposed && _alarmForm.Visible) _alarmForm.RefreshView();
        }

        private void ShowSimulation()
        {
            if (_simulationForm == null || _simulationForm.IsDisposed) _simulationForm = new SimulationForm(_machine);
            _simulationForm.ShowNear(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_machine.Sim != null) ShowSimulation(); // chế độ mô phỏng: mở sẵn popup
        }

        private void AddPage(string title, UserControl page)
        {
            page.Dock = DockStyle.Fill;
            var tab = new TabPage(title) { Font = Ui.NormalFont, BackColor = Ui.PanelColor };
            tab.Controls.Add(page);
            _tabs.TabPages.Add(tab);
        }

        private static Button BigButton(string text, Color color, EventHandler click)
        {
            var button = new Button { Text = text, Width = 120, Height = 64, BackColor = color, Font = new Font("Segoe UI", 13F, FontStyle.Bold), Margin = new Padding(3) };
            button.Click += click;
            button.EnabledChanged += (s, e) => button.BackColor = button.Enabled ? color : Color.Gainsboro;
            return button;
        }

        private void RefreshView()
        {
            DrainLog();

            var state = _machine.State;
            Ui.Set(_lblState, Ui.StateText(state), Ui.StateColor(state));
            var alarm = _machine.ActiveAlarm;
            Ui.Set(_lblAlarm, alarm != null ? alarm.ToString() : (_machine.IsManualMoveActive ? "Đang chạy tay: " + _machine.ManualMoveDescription : ""));
            var alarmCursor = alarm != null ? Cursors.Hand : Cursors.Default;
            if (_lblAlarm.Cursor != alarmCursor) _lblAlarm.Cursor = alarmCursor;
            UpdateAlarmPopup();
            Ui.Set(_lblRecipe, "Model: " + _machine.Recipe.Name);

            Ui.SetEnabled(_btnOrigin, state == MachineState.NotHomed || state == MachineState.Ready);
            Ui.SetEnabled(_btnStart, (state == MachineState.Ready && !_machine.IsManualMoveActive) || state == MachineState.Paused);
            Ui.SetEnabled(_btnStop, state == MachineState.Running || state == MachineState.Homing || _machine.IsManualMoveActive);
            Ui.SetEnabled(_btnReset, state == MachineState.Alarm || state == MachineState.Emergency || state == MachineState.Paused);

            // TẮT CÒI: bật được khi còi đang kêu, nhấp nháy cam để người vận hành để ý
            bool sounding = _machine.IsBuzzerSounding;
            Ui.SetEnabled(_btnMute, sounding);
            var muteColor = !sounding ? Color.Gainsboro : (DateTime.Now.Millisecond < 500 ? Color.Orange : Color.FromArgb(255, 220, 150));
            if (_btnMute.BackColor != muteColor) _btnMute.BackColor = muteColor;

            _connectionBar.RefreshView();

            var page = _tabs.SelectedTab != null && _tabs.SelectedTab.Controls.Count > 0 ? _tabs.SelectedTab.Controls[0] as IRefreshablePage : null;
            if (page != null) page.RefreshView();
        }

        private void DrainLog()
        {
            string line;
            int count = 0;
            _lstLog.BeginUpdate();
            while (count++ < 200 && _machine.Logs.TryDequeue(out line)) _lstLog.Items.Add(line);
            while (_lstLog.Items.Count > 1000) _lstLog.Items.RemoveAt(0);
            _lstLog.EndUpdate();
            if (count > 1) _lstLog.TopIndex = Math.Max(0, _lstLog.Items.Count - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var state = _machine.State;
            if ((state == MachineState.Running || state == MachineState.Homing) &&
                MessageBox.Show("Máy đang chạy. Dừng máy và thoát?", "GlossCheck", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _timer.Stop();
            try { MachineConfig.SaveActiveRecipe(MachineConfig.DefaultPath, _machine.Recipe.Name); } catch { }
            base.OnFormClosing(e);
        }
    }
}
