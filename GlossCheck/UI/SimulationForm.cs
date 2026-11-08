using System;
using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Hardware;
using GlossCheck.Hardware.Simulation;

namespace GlossCheck.UI
{
    /// <summary>
    /// Cửa sổ popup điều khiển mô phỏng (chỉ có ở chế độ Simulation): giả lập nút cứng, mất kết nối IO, lỗi xi lanh,
    /// máy đo treo, giá trị NG. Đóng cửa sổ chỉ ẩn đi, các tuỳ chọn đang bật vẫn giữ nguyên.
    /// </summary>
    public class SimulationForm : Form
    {
        private readonly Machine _machine;
        private readonly SimWorld _sim;
        private readonly Label _lblStatus;
        private readonly Timer _timer;

        public SimulationForm(Machine machine)
        {
            _machine = machine;
            _sim = machine.Sim;
            Text = "Mô phỏng phần cứng";
            Font = Ui.NormalFont;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(360, 520);

            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10) };

            flow.Controls.Add(Ui.Label("Nút cứng (STOP kiêm RESET khi alarm)", 330, Ui.BoldFont));
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            buttons.Controls.Add(PanelButton("START", Color.FromArgb(150, 220, 150), InputSignal.StartButton));
            buttons.Controls.Add(PanelButton("STOP", Color.Khaki, InputSignal.StopButton));
            flow.Controls.Add(buttons);

            flow.Controls.Add(Ui.Label("Hàng trên jig (cho công tắc chân không)", 330, Ui.BoldFont));
            flow.Controls.Add(SimCheck("Có hàng trên Jig 1", _sim.Jig1HasPart, v => _sim.Jig1HasPart = v));
            flow.Controls.Add(SimCheck("Có hàng trên Jig 2", _sim.Jig2HasPart, v => _sim.Jig2HasPart = v));

            // Ô tick lấy trạng thái ban đầu từ SimWorld: cửa sổ có tạo lại cũng không lệch với mô phỏng

            flow.Controls.Add(Ui.Label("Giả lập lỗi", 330, Ui.BoldFont));
            var ioLost = SimCheck("Mất kết nối IO board", _sim.IsIoDisconnected, v => _sim.IsIoDisconnected = v);
            ioLost.ForeColor = Ui.NgColor;
            ioLost.Font = Ui.BoldFont;
            flow.Controls.Add(ioLost);
            flow.Controls.Add(SimCheck("Kẹt xi lanh Jig 1", _sim.IsJig1LifterStuck, v => _sim.IsJig1LifterStuck = v));
            flow.Controls.Add(SimCheck("Kẹt xi lanh Jig 2", _sim.IsJig2LifterStuck, v => _sim.IsJig2LifterStuck = v));
            flow.Controls.Add(SimCheck("Máy đo treo (không trả kết quả)", _sim.IsMeterHung, v => _sim.IsMeterHung = v));
            flow.Controls.Add(SimCheck("Jig 2 cho giá trị NG", _sim.ForceJig2Ng, v => _sim.ForceJig2Ng = v));

            flow.Controls.Add(Ui.Label("Trạng thái mô phỏng", 330, Ui.BoldFont));
            _lblStatus = new Label { Width = 330, Height = 110, Font = new Font("Consolas", 10F), Margin = new Padding(3) };
            flow.Controls.Add(_lblStatus);

            Controls.Add(flow);

            _timer = new Timer { Interval = 100 };
            _timer.Tick += (s, e) => RefreshView();
        }

        private Button PanelButton(string text, Color color, InputSignal button)
        {
            var b = Ui.Button(text, (s, e) => _sim.PressButton(button), 100, 44);
            b.BackColor = color;
            b.Font = Ui.BoldFont;
            return b;
        }

        private static CheckBox SimCheck(string text, bool initial, Action<bool> apply)
        {
            var check = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(6, 6, 6, 2), Checked = initial };
            check.CheckedChanged += (s, e) => apply(check.Checked);
            return check;
        }

        /// <summary>Hiện cửa sổ cạnh phải dưới của cửa sổ chính.</summary>
        public void ShowNear(Form owner)
        {
            if (!Visible)
            {
                Owner = owner; // luôn nằm trên cửa sổ chính, thu nhỏ theo cửa sổ chính
                var area = owner.Bounds;
                Location = new Point(Math.Max(0, area.Right - Width - 30), Math.Max(0, area.Bottom - Height - 60));
                Show();
            }
            Activate();
        }

        private void RefreshView()
        {
            Ui.Set(_lblStatus, string.Format(
                "X = {0,7:0.00} mm   Y = {1,7:0.00} mm\r\nXi lanh Jig 1: {2,-8} Hút: {5}\r\nXi lanh Jig 2: {3,-8} Hút: {6}\r\nMáy đo: {4}\r\nCòi: {7}",
                _machine.X.Position, _machine.Y.Position,
                LifterText(_sim.LifterState(0)), LifterText(_sim.LifterState(1)),
                _machine.MeterService.IsBusy ? "đang đo" : "rảnh",
                VacuumText(_machine.Jig1.Vacuum), VacuumText(_machine.Jig2.Vacuum),
                _machine.Io.GetOutput(OutputSignal.Buzzer) ? "KÊU" : "-"));
        }

        private static string VacuumText(Vacuum vacuum)
        {
            return !vacuum.IsOn ? "tắt" : (vacuum.IsHolding ? "ĐANG GIỮ" : "bật, chưa hút");
        }

        private static string LifterText(double state)
        {
            return state >= 1 ? "LÊN" : (state <= 0 ? "XUỐNG" : "đang chạy " + (state * 100).ToString("0") + "%");
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            _timer.Enabled = Visible;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Đóng = ẩn, giữ nguyên các lỗi đang giả lập. Chỉ huỷ thật khi cửa sổ chính / ứng dụng đóng.
            // (WM_CLOSE gửi từ ngoài có CloseReason = None, nên không chỉ xét UserClosing.)
            bool ownerClosing = e.CloseReason == CloseReason.FormOwnerClosing || e.CloseReason == CloseReason.ApplicationExitCall
                                || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing;
            if (!ownerClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _timer.Stop();
            base.OnFormClosing(e);
        }
    }
}
