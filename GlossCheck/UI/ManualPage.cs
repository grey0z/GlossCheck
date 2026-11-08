using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Hardware;
using GlossCheck.Settings;

namespace GlossCheck.UI
{
    /// <summary>Tab Thao tác tay: servo, jog, đi tới vị trí, xi lanh, máy đo, theo dõi IO.</summary>
    public class ManualPage : UserControl, IRefreshablePage
    {
        private readonly Machine _machine;
        private readonly JogPanel _jogPanel;
        private readonly ListView _lvAxes;
        private readonly ListView _lvIo;
        private readonly NumericUpDown _numX;
        private readonly NumericUpDown _numY;
        private readonly Label _lblLifter1;
        private readonly Label _lblLifter2;
        private readonly Button _btnUp1, _btnDown1, _btnUp2, _btnDown2;
        private readonly Button _btnVacOn1, _btnVacOff1, _btnVacOn2, _btnVacOff2;
        private readonly Label _lblVacuum1, _lblVacuum2;
        private readonly Label _lblMeter;
        private readonly Label _lblMeterReading;
        private readonly Control[] _manualControls;

        public ManualPage(Machine machine)
        {
            _machine = machine;
            Padding = new Padding(6);
            var root = Ui.Flow(FlowDirection.LeftToRight, false);
            root.Dock = DockStyle.Fill;

            // ---------- Cột 1: trục ----------  (cột 2: jog, cột 3: IO)
            var axisFlow = Ui.Flow(FlowDirection.TopDown, false);
            _lvAxes = new ListView { View = View.Details, FullRowSelect = true, GridLines = true, Width = 400, Height = 70, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            foreach (var column in new[] { "Trục:50", "Vị trí (mm):100", "Servo:60", "Origin:60", "Chạy:55", "Alarm:65" })
            {
                var parts = column.Split(':');
                _lvAxes.Columns.Add(parts[0], int.Parse(parts[1]));
            }
            foreach (var axis in machine.Axes) _lvAxes.Items.Add(new ListViewItem(new[] { axis.Name, "", "", "", "", "" }) { Tag = axis });
            Ui.DoubleBuffer(_lvAxes);
            axisFlow.Controls.Add(_lvAxes);

            var servoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var btnServoOn = Ui.Button("Servo ON", (s, e) => _machine.CmdManual("Servo ON", () => { foreach (var a in _machine.Axes) a.SetServo(true); }), 125);
            var btnServoOff = Ui.Button("Servo OFF", (s, e) => _machine.CmdManual("Servo OFF", () => { foreach (var a in _machine.Axes) a.SetServo(false); }), 125);
            var btnAlarmReset = Ui.Button("Reset alarm trục", (s, e) => _machine.CmdManual("Reset alarm trục", () => { foreach (var a in _machine.Axes) a.ResetAlarm(); }), 140);
            servoRow.Controls.AddRange(new Control[] { btnServoOn, btnServoOff, btnAlarmReset });
            axisFlow.Controls.Add(servoRow);

            _numX = Ui.Number(-1000, 1000, 2, 1, 90);
            _numY = Ui.Number(-1000, 1000, 2, 1, 90);
            var gotoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var btnGoto = Ui.Button("Đi tới", (s, e) =>
                _machine.CmdMoveTo(string.Format("Đi tới X={0} Y={1}", _numX.Value, _numY.Value), new XyPoint((double)_numX.Value, (double)_numY.Value)), 90);
            gotoRow.Controls.AddRange(new Control[] { Ui.Label("X"), _numX, Ui.Label("Y"), _numY, btnGoto });
            axisFlow.Controls.Add(gotoRow);
            axisFlow.Controls.Add(Ui.Label("\"Đi tới\" tự hạ cả 2 xi lanh trước khi chạy XY.", 400, null, 1));
            var column1 = Ui.Flow(FlowDirection.TopDown, false);
            column1.AutoSize = true;
            column1.Controls.Add(Ui.Group("Trục X / Y", axisFlow, 440, 250));

            _jogPanel = new JogPanel(machine);
            var jogGroup = Ui.Group("Jog", _jogPanel, 345, 370);

            // ---------- Cột 1 (tiếp): xi lanh + máy đo ----------
            var lifterGrid = new TableLayoutPanel { ColumnCount = 3, RowCount = 4 };
            _lblLifter1 = Ui.Label("", 190, Ui.BoldFont);
            _lblLifter2 = Ui.Label("", 190, Ui.BoldFont);
            _btnUp1 = Ui.Button("Jig 1 LÊN", (s, e) => _machine.CmdManual("Xi lanh Jig 1 lên", () => _machine.Jig1.Lifter.Command(true)), 110);
            _btnDown1 = Ui.Button("Jig 1 XUỐNG", (s, e) => _machine.CmdManual("Xi lanh Jig 1 xuống", () => _machine.Jig1.Lifter.Command(false)), 110);
            _btnUp2 = Ui.Button("Jig 2 LÊN", (s, e) => _machine.CmdManual("Xi lanh Jig 2 lên", () => _machine.Jig2.Lifter.Command(true)), 110);
            _btnDown2 = Ui.Button("Jig 2 XUỐNG", (s, e) => _machine.CmdManual("Xi lanh Jig 2 xuống", () => _machine.Jig2.Lifter.Command(false)), 110);
            lifterGrid.Controls.Add(_btnUp1, 0, 0); lifterGrid.Controls.Add(_btnDown1, 1, 0); lifterGrid.Controls.Add(_lblLifter1, 2, 0);
            lifterGrid.Controls.Add(_btnUp2, 0, 1); lifterGrid.Controls.Add(_btnDown2, 1, 1); lifterGrid.Controls.Add(_lblLifter2, 2, 1);

            // Hút chân không: HÚT sáng xanh khi công tắc báo đã giữ hàng, vàng khi van bật mà chưa hút được; NHẢ sáng khi van tắt
            _lblVacuum1 = Ui.Label("", 190, Ui.BoldFont);
            _lblVacuum2 = Ui.Label("", 190, Ui.BoldFont);
            _btnVacOn1 = Ui.Button("Jig 1 HÚT", (s, e) => _machine.CmdManual("Hút chân không Jig 1", () => _machine.Jig1.Vacuum.Command(true)), 110);
            _btnVacOff1 = Ui.Button("Jig 1 NHẢ", (s, e) => _machine.CmdManual("Nhả chân không Jig 1", () => _machine.Jig1.Vacuum.Command(false)), 110);
            _btnVacOn2 = Ui.Button("Jig 2 HÚT", (s, e) => _machine.CmdManual("Hút chân không Jig 2", () => _machine.Jig2.Vacuum.Command(true)), 110);
            _btnVacOff2 = Ui.Button("Jig 2 NHẢ", (s, e) => _machine.CmdManual("Nhả chân không Jig 2", () => _machine.Jig2.Vacuum.Command(false)), 110);
            lifterGrid.Controls.Add(_btnVacOn1, 0, 2); lifterGrid.Controls.Add(_btnVacOff1, 1, 2); lifterGrid.Controls.Add(_lblVacuum1, 2, 2);
            lifterGrid.Controls.Add(_btnVacOn2, 0, 3); lifterGrid.Controls.Add(_btnVacOff2, 1, 3); lifterGrid.Controls.Add(_lblVacuum2, 2, 3);

            var meterFlow = Ui.Flow(FlowDirection.TopDown, false);
            _lblMeter = Ui.Label("", 410, Ui.BoldFont);
            _lblMeterReading = Ui.Label("", 410, Ui.ValueFont);
            var meterButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var btnConnect = Ui.Button("Kết nối lại", (s, e) => _machine.CmdMeterConnect(), 105);
            var btnMeasure = Ui.Button("Đo thử", (s, e) => _machine.CmdMeterMeasureOnce(), 105);
            var btnCalibrate = Ui.Button("Calib", (s, e) =>
            {
                if (MessageBox.Show("Đặt tấm chuẩn của máy đo đúng vị trí rồi bấm OK để calib.", "Calib máy đo", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                    _machine.CmdMeterCalibrate();
            }, 105);
            meterButtons.Controls.AddRange(new Control[] { btnConnect, btnMeasure, btnCalibrate });
            meterFlow.Controls.AddRange(new Control[] { _lblMeter, meterButtons, _lblMeterReading });

            column1.Controls.Add(Ui.Group("Xi lanh nâng jig / Hút chân không", lifterGrid, 440, 200));
            column1.Controls.Add(Ui.Group("Máy đo độ bóng", meterFlow, 440, 150));
            root.Controls.Add(column1);
            root.Controls.Add(jogGroup);

            // ---------- Cột 3: IO ----------
            _lvIo = new ListView { View = View.Details, FullRowSelect = true, GridLines = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            _lvIo.Columns.Add("IO", 60);
            _lvIo.Columns.Add("Tên", 190);
            _lvIo.Columns.Add("Giá trị", 60);
            foreach (InputSignal signal in Enum.GetValues(typeof(InputSignal)))
                _lvIo.Items.Add(new ListViewItem(new[] { "IN" + (int)signal, IoNames.Of(signal), "" }) { Tag = signal });
            foreach (OutputSignal signal in Enum.GetValues(typeof(OutputSignal)))
                _lvIo.Items.Add(new ListViewItem(new[] { "OUT" + (int)signal, IoNames.Of(signal), "" }) { Tag = signal });
            _lvIo.DoubleClick += (s, e) => ToggleSelectedOutput();
            Ui.DoubleBuffer(_lvIo);
            var ioFlow = Ui.Flow(FlowDirection.TopDown, false);
            _lvIo.Size = new Size(330, 430);
            ioFlow.Controls.Add(_lvIo);
            ioFlow.Controls.Add(Ui.Label("Double-click 1 output để bật/tắt (khi không chạy auto). Đèn / còi bị chương trình ghi đè liên tục.", 330, null, 2));
            root.Controls.Add(Ui.Group("IO", ioFlow, 360, 530));

            Controls.Add(root);
            _manualControls = new Control[] { btnServoOn, btnServoOff, btnAlarmReset, btnGoto, _btnUp1, _btnDown1, _btnUp2, _btnDown2, _btnVacOn1, _btnVacOff1, _btnVacOn2, _btnVacOff2, btnMeasure, btnCalibrate, btnConnect };
        }

        private void ToggleSelectedOutput()
        {
            if (_lvIo.SelectedItems.Count == 0 || !(_lvIo.SelectedItems[0].Tag is OutputSignal)) return;
            var signal = (OutputSignal)_lvIo.SelectedItems[0].Tag;
            bool on = !_machine.Io.GetOutput(signal);
            _machine.CmdManual((on ? "Bật " : "Tắt ") + IoNames.Of(signal), () => _machine.Io.SetOutput(signal, on));
        }

        public void RefreshView()
        {
            _jogPanel.RefreshView();
            bool canOperate = _machine.CanOperateManually;
            foreach (var control in _manualControls) Ui.SetEnabled(control, canOperate);

            foreach (ListViewItem item in _lvAxes.Items)
            {
                var axis = (IAxis)item.Tag;
                SetCell(item, 1, axis.Position.ToString("0.00"));
                SetCell(item, 2, axis.IsServoOn ? "ON" : "off");
                SetCell(item, 3, axis.IsHomed ? "OK" : "-");
                SetCell(item, 4, axis.IsMoving ? "●" : "");
                SetCell(item, 5, axis.IsCommError ? "MẤT KN" : (axis.IsAlarm ? "ALARM" : ""));
                SetBack(item, axis.IsAlarm || axis.IsCommError ? Color.LightCoral : (axis.IsHomed ? Color.White : Color.LightYellow));
            }

            ShowLifter(_machine.Jig1.Lifter, _btnUp1, _btnDown1, _lblLifter1);
            ShowLifter(_machine.Jig2.Lifter, _btnUp2, _btnDown2, _lblLifter2);
            ShowVacuum(_machine.Jig1.Vacuum, _btnVacOn1, _btnVacOff1, _lblVacuum1);
            ShowVacuum(_machine.Jig2.Vacuum, _btnVacOn2, _btnVacOff2, _lblVacuum2);

            var meter = _machine.MeterService;
            var info = meter.Info;
            string status = meter.IsBusy ? "Đang " + meter.CurrentJob.ToLower() + "..." : (meter.IsOpen ? "Đã kết nối" : "CHƯA KẾT NỐI");
            if (info != null) status += string.Format("  |  {0}  {1}  {2}", info.DeviceName, info.PortName, info.IsCalibrated ? "đã calib" : "CHƯA CALIB");
            Ui.Set(_lblMeter, status, null, meter.IsOpen ? Color.Black : Ui.NgColor);
            var reading = meter.LastManualReading;
            Ui.Set(_lblMeterReading, reading != null ? reading.ToString() : "");

            foreach (ListViewItem item in _lvIo.Items)
            {
                bool on = item.Tag is InputSignal ? _machine.Io.GetInput((InputSignal)item.Tag) : _machine.Io.GetOutput((OutputSignal)item.Tag);
                SetCell(item, 2, on ? "ON" : "off");
                SetBack(item, on ? Color.LightGreen : Color.White);
            }
        }

        /// <summary>
        /// Nút LÊN / XUỐNG sáng theo cảm biến đang tác động. Đang ở giữa hành trình (không cảm biến nào ON)
        /// thì không nút nào sáng; 2 cảm biến cùng ON là lỗi cảm biến.
        /// </summary>
        private static void ShowLifter(Cylinder lifter, Button upButton, Button downButton, Label status)
        {
            bool up = lifter.IsUpSensorOn, down = lifter.IsDownSensorOn;
            bool fault = up && down;
            SetLamp(upButton, up && !fault);
            SetLamp(downButton, down && !fault);
            if (fault) Ui.Set(status, "LỖI: 2 cảm biến cùng ON", null, Ui.NgColor);
            else if (!up && !down) Ui.Set(status, "đang ở giữa", null, Color.DimGray);
            else Ui.Set(status, "", null, Color.DimGray);
        }

        /// <summary>
        /// Nút HÚT sáng xanh khi công tắc chân không báo đã giữ hàng, sáng vàng khi van bật mà chưa hút được
        /// (chưa có hàng / lệch / mất khí). Nút NHẢ sáng khi van tắt.
        /// </summary>
        private static void ShowVacuum(Vacuum vacuum, Button onButton, Button offButton, Label status)
        {
            bool on = vacuum.IsOn, holding = vacuum.IsHolding;
            SetLamp(onButton, on ? (holding ? LampOnColor : LampWarnColor) : (Color?)null);
            SetLamp(offButton, !on ? LampOnColor : (Color?)null);
            if (on && !holding) Ui.Set(status, "chưa hút được hàng", null, Ui.WaitColor);
            else if (!on && holding) Ui.Set(status, "LỖI: van tắt mà vẫn báo hút", null, Ui.NgColor);
            else Ui.Set(status, on ? "đang giữ hàng" : "", null, Color.DimGray);
        }

        private static readonly Color LampOnColor = Color.FromArgb(120, 215, 120);
        private static readonly Color LampWarnColor = Color.FromArgb(255, 210, 100);

        private static void SetLamp(Button button, bool on) { SetLamp(button, on ? LampOnColor : (Color?)null); }

        /// <summary>color = null: nút trở về màu thường.</summary>
        private static void SetLamp(Button button, Color? color)
        {
            if (color.HasValue)
            {
                if (button.BackColor != color.Value) { button.BackColor = color.Value; button.Font = Ui.BoldFont; }
            }
            else if (!button.UseVisualStyleBackColor)
            {
                button.BackColor = SystemColors.Control;
                button.UseVisualStyleBackColor = true;
                button.Font = Ui.NormalFont;
            }
        }

        private static void SetCell(ListViewItem item, int index, string text) { if (item.SubItems[index].Text != text) item.SubItems[index].Text = text; }
        private static void SetBack(ListViewItem item, Color color) { if (item.BackColor != color) item.BackColor = color; }
    }
}
