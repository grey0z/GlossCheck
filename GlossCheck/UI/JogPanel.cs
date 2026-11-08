using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Hardware;

namespace GlossCheck.UI
{
    /// <summary>
    /// Jog X/Y dùng chung cho tab Thao tác tay và Teaching.
    /// Giữ nút thì chạy, nhả nút thì dừng. Tốc độ chọn theo % của "Tốc độ jog 100%" trong cấu hình.
    /// Machine chặn jog khi xi lanh jig chưa xuống.
    /// </summary>
    public class JogPanel : UserControl
    {
        private static readonly int[] SpeedPercents = { 1, 5, 10, 20, 50, 100 };
        private const int DefaultPercent = 10;

        private readonly Machine _machine;
        private readonly Label _lblX;
        private readonly Label _lblY;
        private readonly ComboBox _cboSpeed;
        private readonly Label _lblSpeed;
        private readonly Label _lblInterlock;

        public JogPanel(Machine machine)
        {
            _machine = machine;
            Width = 330;
            Height = 340;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7 };
            for (int c = 0; c < 3; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // X =
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // Y =
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // tốc độ
            for (int r = 0; r < 3; r++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); // 3 hàng nút
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // cảnh báo interlock

            _lblX = new Label { Dock = DockStyle.Fill, Font = Ui.ValueFont, TextAlign = ContentAlignment.MiddleCenter };
            _lblY = new Label { Dock = DockStyle.Fill, Font = Ui.ValueFont, TextAlign = ContentAlignment.MiddleCenter };
            grid.Controls.Add(_lblX, 0, 0); grid.SetColumnSpan(_lblX, 3);
            grid.Controls.Add(_lblY, 0, 1); grid.SetColumnSpan(_lblY, 3);

            // ---------- Tốc độ: chọn % ----------
            var speedRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            _cboSpeed = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, Font = Ui.BoldFont, Margin = new Padding(3, 5, 3, 3) };
            foreach (int percent in SpeedPercents) _cboSpeed.Items.Add(percent + " %");
            _cboSpeed.SelectedIndex = System.Array.IndexOf(SpeedPercents, DefaultPercent);
            _lblSpeed = new Label { AutoSize = false, Width = 130, Height = 30, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
            speedRow.Controls.Add(new Label { Text = "Tốc độ:", AutoSize = false, Width = 64, Height = 30, Font = Ui.BoldFont, TextAlign = ContentAlignment.MiddleLeft });
            speedRow.Controls.Add(_cboSpeed);
            speedRow.Controls.Add(_lblSpeed);
            grid.Controls.Add(speedRow, 0, 2); grid.SetColumnSpan(speedRow, 3);

            // ---------- Nút jog (hình chữ thập) ----------
            grid.Controls.Add(JogButton("Y −", machine.Y, -1), 1, 3);
            grid.Controls.Add(JogButton("X −", machine.X, -1), 0, 4);
            grid.Controls.Add(StopButton(), 1, 4);
            grid.Controls.Add(JogButton("X +", machine.X, +1), 2, 4);
            grid.Controls.Add(JogButton("Y +", machine.Y, +1), 1, 5);

            _lblInterlock = new Label { Dock = DockStyle.Fill, ForeColor = Ui.NgColor, Font = Ui.BoldFont, TextAlign = ContentAlignment.TopLeft };
            grid.Controls.Add(_lblInterlock, 0, 6); grid.SetColumnSpan(_lblInterlock, 3);
            Controls.Add(grid);
        }

        /// <summary>% tốc độ đang chọn.</summary>
        public int SpeedPercent { get { return SpeedPercents[System.Math.Max(0, _cboSpeed.SelectedIndex)]; } }

        private Button JogButton(string text, IAxis axis, int direction)
        {
            var button = new Button { Text = text, Dock = DockStyle.Fill, Font = Ui.TitleFont, BackColor = Color.FromArgb(220, 230, 245), Margin = new Padding(3) };
            button.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _machine.CmdJog(axis, direction, SpeedPercent); };
            button.MouseUp += (s, e) => _machine.CmdJogStop(axis);
            button.MouseLeave += (s, e) => { if (axis.IsMoving) _machine.CmdJogStop(axis); };
            return button;
        }

        /// <summary>Nút DỪNG cùng kích thước với các nút jog (lấp đầy ô ở tâm chữ thập).</summary>
        private Button StopButton()
        {
            var button = new Button { Text = "DỪNG", Dock = DockStyle.Fill, Font = Ui.BoldFont, BackColor = Color.FromArgb(245, 210, 210), Margin = new Padding(3) };
            button.Click += (s, e) => { _machine.CmdJogStop(_machine.X); _machine.CmdJogStop(_machine.Y); };
            return button;
        }

        public void RefreshView()
        {
            Ui.Set(_lblX, string.Format("X = {0,8:0.00} mm", _machine.X.Position));
            Ui.Set(_lblY, string.Format("Y = {0,8:0.00} mm", _machine.Y.Position));
            var settings = _machine.AxisSettingsOf(_machine.X);
            Ui.Set(_lblSpeed, "= " + settings.JogSpeedAt(SpeedPercent).ToString("0.##") + " mm/s");
            string warning = !_machine.Gantry.AreLiftersDown ? "Xi lanh jig chưa xuống: không jog được."
                : (!_machine.CanOperateManually ? "Máy đang chạy: không jog được." : "");
            Ui.Set(_lblInterlock, warning);
        }
    }
}
