using System;
using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;

namespace GlossCheck.UI
{
    /// <summary>
    /// Tab Auto: 2 khung jig, bước đang chạy, gợi ý thao tác và bảng bộ đếm OK/NG (Jig 1, Jig 2, Tổng).
    /// (Mô phỏng nằm ở SimulationForm, thống kê theo giờ / ngày ở tab Sản lượng.)
    /// </summary>
    public class AutoPage : UserControl, IRefreshablePage
    {
        private const int ColumnOk = 1, ColumnNg = 2, ColumnTotal = 3, ColumnYield = 4;
        private static readonly Font CounterFont = new Font("Segoe UI", 15F, FontStyle.Bold);

        private readonly Machine _machine;
        private readonly JigPanel _jig1Panel;
        private readonly JigPanel _jig2Panel;
        private readonly Label _lblStep;
        private readonly Label _lblNext;
        private readonly Button _btnSwap;
        private Label _lblSince; // tạo trong BuildCounterTable
        /// <summary>[hàng, cột]: hàng 0 = Jig 1, 1 = Jig 2, 2 = Tổng.</summary>
        private readonly Label[,] _counterCells = new Label[3, 5];

        public AutoPage(Machine machine)
        {
            _machine = machine;
            Padding = new Padding(8);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 215));

            _jig1Panel = new JigPanel(machine, machine.Jig1) { Dock = DockStyle.Fill, Margin = new Padding(6) };
            _jig2Panel = new JigPanel(machine, machine.Jig2) { Dock = DockStyle.Fill, Margin = new Padding(6) };
            layout.Controls.Add(_jig1Panel, 0, 0);
            layout.Controls.Add(_jig2Panel, 1, 0);

            // ---------- Trái: bước đang chạy + gợi ý ----------
            var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(4, 10, 0, 0) };
            _lblStep = Ui.Label("", 620, Ui.BoldFont, 2);
            _lblNext = Ui.Label("", 620, Ui.NormalFont, 2);
            // ĐỔI JIG: chạy con thoi sang vị trí còn lại mà không đo (lấy jig đang ở trong ra)
            _btnSwap = new Button { Text = "ĐỔI JIG", Width = 300, Height = 48, Font = Ui.TitleFont, BackColor = Color.FromArgb(255, 236, 179), Margin = new Padding(3, 8, 3, 3) };
            _btnSwap.Click += (s, e) =>
            {
                var inside = _machine.JigInside;
                if (inside == null) return;
                if (MessageBox.Show("Con thoi sẽ chạy: " + inside.Name + " ra ngoài, jig còn lại vào trong.\r\nBỎ TAY RA KHỎI JIG rồi bấm OK.",
                        "Đổi jig", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                    _machine.CmdSwapJig();
            };
            _btnSwap.EnabledChanged += (s, e) => _btnSwap.BackColor = _btnSwap.Enabled ? Color.FromArgb(255, 236, 179) : Color.Gainsboro;
            info.Controls.Add(_lblStep);
            info.Controls.Add(_lblNext);
            info.Controls.Add(_btnSwap);
            layout.Controls.Add(info, 0, 1);

            // ---------- Phải: bảng bộ đếm ----------
            layout.Controls.Add(BuildCounterTable(), 1, 1);
            Controls.Add(layout);
        }

        private Control BuildCounterTable()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 4, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, BackColor = Color.White };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            for (int c = 1; c < 5; c++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            for (int r = 1; r < 4; r++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3F));

            var headerBack = Color.FromArgb(234, 238, 242);
            string[] headers = { "", "OK", "NG", "Tổng", "Tỉ lệ đạt" };
            for (int c = 0; c < headers.Length; c++) table.Controls.Add(Cell(headers[c], Ui.BoldFont, headerBack), c, 0);

            string[] rowNames = { "Jig 1", "Jig 2", "TỔNG" };
            for (int r = 0; r < 3; r++)
            {
                var back = r == 2 ? headerBack : Color.White;
                table.Controls.Add(Cell(rowNames[r], Ui.TitleFont, back), 0, r + 1);
                for (int c = 1; c < 5; c++)
                {
                    _counterCells[r, c] = Cell("0", CounterFont, back);
                    table.Controls.Add(_counterCells[r, c], c, r + 1);
                }
            }

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false };
            _lblSince = Ui.Label("", 330);
            footer.Controls.Add(_lblSince);
            footer.Controls.Add(Ui.Button("Xoá bộ đếm", (s, e) =>
            {
                if (MessageBox.Show("Xoá bộ đếm OK/NG của cả 2 jig?\r\n(Dữ liệu trong file CSV và tab Sản lượng không bị xoá.)", "GlossCheck",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    _machine.CmdResetCounters();
            }, 120, 30));

            var group = new GroupBox { Text = "Bộ đếm sản lượng", Dock = DockStyle.Fill, Font = Ui.BoldFont, Padding = new Padding(6), Margin = new Padding(6) };
            group.Controls.Add(table);
            group.Controls.Add(footer);
            return group;
        }

        private static Label Cell(string text, Font font, Color back)
        {
            return new Label { Text = text, Font = font, BackColor = back, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0) };
        }

        public void RefreshView()
        {
            _jig1Panel.RefreshView();
            _jig2Panel.RefreshView();

            var state = _machine.State;
            var measure = _machine.Measure;
            string step = state == MachineState.Running || state == MachineState.Paused || state == MachineState.Alarm
                ? measure.CurrentStepDescription + (measure.IsPaused ? "  (tạm dừng)" : "") + "   " + (measure.CurrentStepElapsedMs / 1000.0).ToString("0.0") + " s"
                : "-";
            Ui.Set(_lblStep, "Bước hiện tại: " + step);

            var next = _machine.NextJigForStart;
            var inside = _machine.JigInside;
            string hint;
            if (state == MachineState.NotHomed) hint = "Bấm ORIGIN để về gốc trước khi chạy.";
            else if (_machine.IsManualMoveActive) hint = "Con thoi đang chạy: " + _machine.ManualMoveDescription;
            else if (state == MachineState.Running)
                hint = "Đang đo " + (measure.Target != null ? measure.Target.Name : "") + "…";
            else if (state == MachineState.Ready && (DateTime.Now - _machine.NoticeTime).TotalSeconds < 10 && _machine.NoticeText != "")
                hint = "⚠ " + _machine.NoticeText; // vd. không hút được hàng
            else if (state == MachineState.Ready)
                hint = "Đặt hàng lên " + next.Name + (inside != null ? " (đang ở ngoài)" : "") + " rồi bấm START."
                       + (inside != null ? "  Lấy " + inside.Name + " ra không đo: bấm ĐỔI JIG." : "");
            else if (state == MachineState.Paused) hint = "Tạm dừng: START để đo tiếp, STOP (nút cứng) / RESET để bỏ lần đo này.";
            else if (state == MachineState.Alarm) hint = "Xử lý lỗi rồi bấm STOP (nút cứng) hoặc RESET để xoá alarm.";
            else if (state == MachineState.Emergency)
                hint = _machine.IsIoConnected
                    ? "IO board đã kết nối lại: bấm STOP (nút cứng) hoặc RESET, sau đó ORIGIN lại."
                    : "Mất kết nối IO board: kiểm tra cáp / nguồn, bấm \"Kết nối lại\" ở thanh dưới, rồi RESET trên màn hình.";
            else hint = "";
            Ui.Set(_lblNext, hint, null, hint.StartsWith("⚠") ? Ui.NgColor : Color.Black);

            bool canSwap = _machine.CanSwapJig;
            Ui.SetEnabled(_btnSwap, canSwap);
            string swapText = inside != null ? "ĐỔI JIG  (đưa " + inside.Name + " ra)" : "ĐỔI JIG";
            if (_btnSwap.Text != swapText) _btnSwap.Text = swapText;

            var jig1 = _machine.Jig1;
            var jig2 = _machine.Jig2;
            SetCounterRow(0, jig1.OkCount, jig1.NgCount);
            SetCounterRow(1, jig2.OkCount, jig2.NgCount);
            SetCounterRow(2, jig1.OkCount + jig2.OkCount, jig1.NgCount + jig2.NgCount);
            Ui.Set(_lblSince, "Tính từ " + _machine.CountersResetAt.ToString("HH:mm dd'/'MM'/'yyyy"));
        }

        private void SetCounterRow(int row, int ok, int ng)
        {
            int total = ok + ng;
            Ui.Set(_counterCells[row, ColumnOk], ok.ToString(), null, Ui.OkColor);
            Ui.Set(_counterCells[row, ColumnNg], ng.ToString(), null, ng > 0 ? Ui.NgColor : Color.Black);
            Ui.Set(_counterCells[row, ColumnTotal], total.ToString(), null, Color.Black);
            double? yield = total > 0 ? 100.0 * ok / total : (double?)null;
            Ui.Set(_counterCells[row, ColumnYield], yield.HasValue ? yield.Value.ToString("0.0") + "%" : "-", null,
                yield.HasValue && yield.Value < 100 ? Ui.NgColor : Color.Black);
        }
    }
}
