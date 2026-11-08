using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Hardware;
using GlossCheck.Settings;

namespace GlossCheck.UI
{
    /// <summary>Khung hiển thị 1 bên jig: trạng thái, giá trị 3 góc so với spec, kết quả OK/NG, bộ đếm.</summary>
    public class JigPanel : UserControl
    {
        private readonly Machine _machine;
        private readonly JigStation _jig;
        private readonly Label _lblState;
        private readonly Label _lblResult;
        private readonly Label _lblTime;
        private readonly Label _lblLocation;
        private readonly Label[] _lblValues = new Label[3];
        private readonly Label[] _lblSpecs = new Label[3];
        private readonly Label[] _lblJudges = new Label[3];

        public JigPanel(Machine machine, JigStation jig)
        {
            _machine = machine;
            _jig = jig;
            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(10);
            Ui.DoubleBuffer(this);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // tên jig + Ở TRONG / Ở NGOÀI
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));   // trạng thái (CHƯA ĐO / ĐANG ĐO / OK / NG)
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // bảng giá trị
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // kết quả lần trước (khi đang đo lại)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // thời gian đo (bộ đếm nằm ở bảng chung bên dưới tab Auto)

            // Tên jig (trái) + vị trí hiện tại của jig trên con thoi (phải): Ở TRONG / Ở NGOÀI
            var title = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var name = new Label { Text = jig.Name.ToUpper() + (jig.Id == JigId.Jig1 ? "  (trái)" : "  (phải)"), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            _lblLocation = new Label { Dock = DockStyle.Right, Width = 300, Font = Ui.BoldFont, TextAlign = ContentAlignment.MiddleRight };
            title.Controls.Add(name);
            title.Controls.Add(_lblLocation);
            _lblState = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 26F, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter };

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            for (int r = 0; r < 4; r++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            string[] headers = { "Góc", "Giá trị (GU)", "Spec [Min - Max]", "KQ" };
            for (int c = 0; c < 4; c++) table.Controls.Add(Cell(headers[c], Ui.BoldFont, Color.FromArgb(234, 238, 242)), c, 0);
            foreach (var angle in GlossAngles.All)
            {
                int row = (int)angle + 1;
                table.Controls.Add(Cell(GlossAngles.Label(angle), Ui.BoldFont, Color.White), 0, row);
                _lblValues[(int)angle] = Cell("-", Ui.ValueFont, Color.White);
                _lblSpecs[(int)angle] = Cell("", Ui.NormalFont, Color.White);
                _lblJudges[(int)angle] = Cell("", Ui.BoldFont, Color.White);
                table.Controls.Add(_lblValues[(int)angle], 1, row);
                table.Controls.Add(_lblSpecs[(int)angle], 2, row);
                table.Controls.Add(_lblJudges[(int)angle], 3, row);
            }

            _lblResult = new Label { Dock = DockStyle.Fill, Font = Ui.BoldFont, TextAlign = ContentAlignment.MiddleLeft };
            _lblTime = new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };

            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(_lblState, 0, 1);
            layout.Controls.Add(table, 0, 2);
            layout.Controls.Add(_lblResult, 0, 3);
            layout.Controls.Add(_lblTime, 0, 4);
            Controls.Add(layout);
        }

        private static Label Cell(string text, Font font, Color back)
        {
            return new Label { Text = text, Font = font, BackColor = back, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0) };
        }

        public void RefreshView()
        {
            var state = _jig.State;
            Ui.Set(_lblState, JigStation.StateText(state), Ui.JigColor(state));

            Recipe recipe = _machine.Recipe;
            var reading = _jig.LastReading;
            var judge = _jig.LastJudge;
            foreach (var angle in GlossAngles.All)
            {
                int i = (int)angle;
                var spec = recipe.Spec(angle);
                Ui.Set(_lblSpecs[i], spec.IsEnabled ? string.Format("{0:0.0} - {1:0.0}", spec.Min, spec.Max) : "(không xét)");
                Ui.Set(_lblValues[i], reading != null ? reading[angle].ToString("0.0") : "-");
                bool? ok = judge != null ? judge.AngleOk(angle) : null;
                if (ok == null) Ui.Set(_lblJudges[i], "", Color.White, Color.Black);
                else Ui.Set(_lblJudges[i], ok.Value ? "OK" : "NG", ok.Value ? Ui.OkColor : Ui.NgColor, Color.White);
            }

            // Đang đo lại: bảng vẫn giữ giá trị lần trước, ghi rõ để không nhầm với kết quả mới
            if (judge != null && state == JigState.Measuring)
                Ui.Set(_lblResult, "Giá trị trên là của lần đo trước (" + (judge.IsOk ? "OK" : "NG") + ")", null, Color.DimGray);
            else
                Ui.Set(_lblResult, "", null, Color.DimGray);


            var vacuum = _jig.Vacuum;
            string vacuumText = !vacuum.IsOn ? "Hút: tắt" : (vacuum.IsHolding ? "Hút: ĐANG GIỮ HÀNG" : "Hút: bật, chưa hút được");
            Ui.Set(_lblTime, (_jig.LastMeasuredAt.HasValue ? "Đo lúc " + _jig.LastMeasuredAt.Value.ToString("HH:mm:ss dd/MM") : "Chưa đo")
                             + "      " + vacuumText);

            var machineState = _machine.State;
            var inside = _machine.JigInside;
            if (inside == null)
            {
                bool moving = _machine.X.IsMoving || _machine.Y.IsMoving;
                string text = machineState == MachineState.NotHomed ? "" : (moving ? "con thoi đang chạy…" : "con thoi chưa ở Pos1 / Pos2");
                Ui.Set(_lblLocation, text, null, Color.DimGray);
            }
            else if (inside == _jig) Ui.Set(_lblLocation, "▼ Ở TRONG (dưới máy đo)", null, Ui.BusyColor);
            else Ui.Set(_lblLocation, "▲ Ở NGOÀI (nạp hàng)", null, Ui.OkColor);
        }
    }
}
