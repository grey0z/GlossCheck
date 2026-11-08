using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Hardware;

namespace GlossCheck.UI
{
    /// <summary>
    /// Thanh trạng thái kết nối ở đáy màn hình: mỗi thiết bị 1 đèn
    ///   xanh = đã kết nối, đỏ = mất kết nối, cam = kết nối nhưng cần chú ý (máy đo chưa calib / đang bận), xám = mô phỏng.
    /// Nút "Kết nối lại" thử kết nối lại các thiết bị đang mất kết nối.
    /// </summary>
    public class ConnectionBar : UserControl
    {
        private static readonly Color Connected = Color.FromArgb(46, 160, 67);
        private static readonly Color Lost = Color.FromArgb(207, 34, 46);
        private static readonly Color Warning = Color.FromArgb(220, 130, 0);
        private static readonly Color Simulated = Color.FromArgb(120, 120, 120);

        private readonly Machine _machine;
        private readonly Label _axisX;
        private readonly Label _axisY;
        private readonly Label _io;
        private readonly Label _meter;
        private readonly Label _info;
        private readonly Button _btnReconnect;
        private readonly ToolTip _toolTip = new ToolTip();

        public ConnectionBar(Machine machine)
        {
            _machine = machine;
            Height = 34;
            BackColor = Color.White;
            Padding = new Padding(4, 2, 4, 2);

            var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            _axisX = Indicator();
            _axisY = Indicator();
            _io = Indicator();
            _meter = Indicator();
            _btnReconnect = new Button { Text = "Kết nối lại", Width = 100, Height = 28, Margin = new Padding(8, 1, 3, 1), Font = Ui.NormalFont };
            _btnReconnect.Click += (s, e) => _machine.CmdReconnect();
            left.Controls.AddRange(new Control[] { new Label { Text = "Kết nối:", AutoSize = true, Font = Ui.BoldFont, Margin = new Padding(3, 7, 6, 0) },
                                                   _axisX, _axisY, _io, _meter, _btnReconnect });

            _info = new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true };

            Controls.Add(_info);
            Controls.Add(left);

            var config = machine.Config;
            _toolTip.SetToolTip(_axisX, "Driver trục X - " + config.AxisX.IpAddress);
            _toolTip.SetToolTip(_axisY, "Driver trục Y - " + config.AxisY.IpAddress);
            _toolTip.SetToolTip(_io, "Module Ezi-IO - input " + config.Io.InputModuleIp + ", output " + config.Io.OutputModuleIp);
            _toolTip.SetToolTip(_btnReconnect, "Kết nối lại các thiết bị đang mất kết nối (không dùng khi máy đang chạy)");
        }

        private static Label Indicator()
        {
            return new Label { AutoSize = true, Font = Ui.BoldFont, Margin = new Padding(6, 7, 6, 0) };
        }

        private static void Show(Label label, string text, Color color)
        {
            Ui.Set(label, "● " + text, null, color);
        }

        public void RefreshView()
        {
            var config = _machine.Config;
            bool sim = _machine.IsSimulation;

            if (sim)
            {
                Show(_axisX, "Trục X (mô phỏng)", Simulated);
                Show(_axisY, "Trục Y (mô phỏng)", Simulated);
                if (_machine.IsIoConnected) Show(_io, "IO (mô phỏng)", Simulated);
                else Show(_io, "IO board MẤT KẾT NỐI (mô phỏng)", Lost);
            }
            else
            {
                bool x = _machine.IsAxisConnected(_machine.X), y = _machine.IsAxisConnected(_machine.Y);
                Show(_axisX, "Trục X " + (x ? "" : "MẤT KẾT NỐI ") + IpTail(config.AxisX.IpAddress), x ? Connected : Lost);
                Show(_axisY, "Trục Y " + (y ? "" : "MẤT KẾT NỐI ") + IpTail(config.AxisY.IpAddress), y ? Connected : Lost);
                bool io = _machine.IsIoConnected;
                Show(_io, "IO board " + (io ? "" : "MẤT KẾT NỐI ") + IpTail(config.Io.InputModuleIp), io ? Connected : Lost);
            }

            // Máy đo: đã mở / bận / chưa calib / chưa kết nối
            var meter = _machine.MeterService;
            var info = meter.Info;
            string meterName = "Máy đo gloss" + (info != null && !string.IsNullOrEmpty(info.PortName) ? " " + info.PortName : "");
            if (meter.IsBusy && !meter.IsOpen) Show(_meter, meterName + " đang kết nối…", Warning);
            else if (!meter.IsOpen) Show(_meter, meterName + " CHƯA KẾT NỐI", Lost);
            else if (info != null && !info.IsCalibrated) Show(_meter, meterName + " CHƯA CALIB", Warning);
            else if (meter.IsBusy) Show(_meter, meterName + " (" + meter.CurrentJob.ToLower() + ")", Connected);
            else Show(_meter, meterName + (sim ? " (mô phỏng)" : ""), sim ? Simulated : Connected);
            string meterTip = info != null ? string.Format("{0}  SN {1}  {2}  {3}", info.DeviceName, info.SerialNumber, info.PortName, info.IsCalibrated ? "đã calib" : "CHƯA CALIB") : "Máy đo độ bóng NHG (NGComm.dll)";
            if (_toolTip.GetToolTip(_meter) != meterTip) _toolTip.SetToolTip(_meter, meterTip);

            var state = _machine.State;
            Ui.SetEnabled(_btnReconnect, !sim && state != MachineState.Running && state != MachineState.Homing);

            Ui.Set(_info, string.Format("Scan {0:0.00} ms    Kết quả: {1}", _machine.ScanTimeMs, config.ResolveResultFolder()));
        }

        private static string IpTail(string ip)
        {
            int dot = ip.LastIndexOf('.');
            return dot >= 0 ? "(." + ip.Substring(dot + 1) + ")" : "";
        }
    }
}
