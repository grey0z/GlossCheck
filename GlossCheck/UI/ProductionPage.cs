using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Data;

namespace GlossCheck.UI
{
    /// <summary>
    /// Tab Sản lượng: bảng theo giờ của 1 ngày (OK/NG từng jig, tổng, tỉ lệ đạt) và bảng tổng 7 ngày gần nhất.
    /// Số liệu đọc từ file CSV kết quả; đang xem hôm nay thì tự cập nhật khi đo xong mỗi sản phẩm.
    /// </summary>
    public class ProductionPage : UserControl, IRefreshablePage
    {
        private const int DaysInSummary = 7;
        private const string AllRecipes = "(Tất cả model)";

        private readonly Machine _machine;
        private readonly DateTimePicker _datePicker;
        private readonly ComboBox _cboRecipe;
        private readonly DataGridView _gridHours;
        private readonly DataGridView _gridDays;
        private readonly CheckBox _chkHideEmpty;
        private static readonly System.Globalization.CultureInfo Vietnamese = new System.Globalization.CultureInfo("vi-VN");
        private readonly Label _lblSummary;
        private MeasurementRecord _lastSeenRecord;
        private DateTime _loadedAt;
        private bool _isReloading;

        public ProductionPage(Machine machine)
        {
            _machine = machine;
            Padding = new Padding(6);

            // ---------- Thanh chọn ----------
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false };
            _datePicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 140, Font = Ui.TitleFont, Margin = new Padding(3, 6, 3, 3) };
            _datePicker.ValueChanged += (s, e) => Reload();
            _cboRecipe = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Font = Ui.NormalFont, Margin = new Padding(12, 9, 3, 3) };
            _cboRecipe.Items.Add(AllRecipes);
            _cboRecipe.SelectedIndex = 0;
            _cboRecipe.SelectedIndexChanged += (s, e) => { if (!_isReloading) Reload(); };
            toolbar.Controls.Add(Ui.Label("Ngày:", 0, Ui.BoldFont));
            toolbar.Controls.Add(_datePicker);
            toolbar.Controls.Add(Ui.Button("Hôm nay", (s, e) => _datePicker.Value = DateTime.Today, 90, 34));
            toolbar.Controls.Add(Ui.Button("◀", (s, e) => _datePicker.Value = _datePicker.Value.AddDays(-1), 40, 34));
            toolbar.Controls.Add(Ui.Button("▶", (s, e) => _datePicker.Value = _datePicker.Value.AddDays(1), 40, 34));
            toolbar.Controls.Add(_cboRecipe);
            _chkHideEmpty = new CheckBox { Text = "Ẩn giờ không có hàng", AutoSize = true, Checked = true, Margin = new Padding(12, 12, 3, 3) };
            _chkHideEmpty.CheckedChanged += (s, e) => Reload();
            toolbar.Controls.Add(_chkHideEmpty);
            toolbar.Controls.Add(Ui.Button("Làm mới", (s, e) => Reload(), 90, 34));
            toolbar.Controls.Add(Ui.Button("Mở thư mục CSV", (s, e) => OpenResultFolder(), 150, 34));

            _lblSummary = new Label { Dock = DockStyle.Top, Height = 40, Font = Ui.TitleFont, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };

            // ---------- Bảng theo giờ ----------
            _gridHours = CreateGrid();
            AddColumn(_gridHours, "Giờ", 110, false);
            AddColumn(_gridHours, "Jig 1 OK", 72, true);
            AddColumn(_gridHours, "Jig 1 NG", 72, true);
            AddColumn(_gridHours, "Jig 2 OK", 72, true);
            AddColumn(_gridHours, "Jig 2 NG", 72, true);
            AddColumn(_gridHours, "Tổng", 72, true);
            AddColumn(_gridHours, "OK", 72, true);
            AddColumn(_gridHours, "NG", 72, true);
            AddColumn(_gridHours, "Tỉ lệ đạt", 86, true);
            for (int h = 0; h < 24; h++) _gridHours.Rows.Add(string.Format("{0:00}:00 - {1:00}:00", h, (h + 1) % 24));
            _gridHours.Rows.Add("TỔNG NGÀY");
            var totalRow = _gridHours.Rows[24];
            totalRow.DefaultCellStyle.Font = Ui.BoldFont;
            totalRow.DefaultCellStyle.BackColor = Color.FromArgb(234, 238, 242);
            totalRow.Frozen = false;

            // ---------- Bảng 7 ngày ----------
            _gridDays = CreateGrid();
            AddColumn(_gridDays, "Ngày", 110, false);
            AddColumn(_gridDays, "Tổng", 75, true);
            AddColumn(_gridDays, "OK", 75, true);
            AddColumn(_gridDays, "NG", 75, true);
            AddColumn(_gridDays, "Tỉ lệ đạt", 85, true);
            _gridDays.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && _gridDays.Rows[e.RowIndex].Tag is DateTime) _datePicker.Value = (DateTime)_gridDays.Rows[e.RowIndex].Tag;
            };

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 760));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(Titled("Sản lượng theo giờ", _gridHours), 0, 0);
            body.Controls.Add(Titled(DaysInSummary + " ngày gần nhất (double-click để xem ngày đó)", _gridDays), 1, 0);

            Controls.Add(body);
            Controls.Add(_lblSummary);
            Controls.Add(toolbar);
            Reload();
        }

        private static DataGridView CreateGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, Font = Ui.NormalFont,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 32,
                EnableHeadersVisualStyles = false,
            };
            grid.ColumnHeadersDefaultCellStyle.Font = Ui.BoldFont;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234, 238, 242);
            grid.RowTemplate.Height = 24;
            // Bảng chỉ để xem: dòng được chọn chỉ tô xanh nhạt, chữ giữ màu đen
            // (WinForms tự chọn dòng đầu khi bảng hiện ra, tô xanh đậm sẽ trông như dữ liệu đặc biệt)
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 236, 250);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.VisibleChanged += (s, e) => { if (grid.Visible) grid.ClearSelection(); };
            Ui.DoubleBuffer(grid);
            return grid;
        }

        private static void AddColumn(DataGridView grid, string header, int width, bool isNumber)
        {
            var column = new DataGridViewTextBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            column.DefaultCellStyle.Alignment = isNumber ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft;
            grid.Columns.Add(column);
        }

        private static Control Titled(string title, Control content)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Font = Ui.BoldFont, Padding = new Padding(6) };
            content.Dock = DockStyle.Fill;
            group.Controls.Add(content);
            return group;
        }

        private string RecipeFilter { get { return _cboRecipe.SelectedIndex <= 0 ? null : (string)_cboRecipe.SelectedItem; } }
        private bool IsViewingToday { get { return _datePicker.Value.Date == DateTime.Today; } }

        private void Reload()
        {
            string folder = _machine.Config.ResolveResultFolder();
            var date = _datePicker.Value.Date;
            var day = ProductionReport.LoadDay(folder, date, RecipeFilter);
            var days = ProductionReport.LoadDays(folder, date.AddDays(-(DaysInSummary - 1)), date, RecipeFilter);

            UpdateRecipeList(day);
            FillHours(day);
            FillDays(days);

            var total = day.Total;
            _lblSummary.Text = string.Format(Vietnamese, "{0:dddd dd/MM/yyyy}{1}:   Tổng {2}    OK {3}    NG {4}    Tỉ lệ đạt {5}",
                date, RecipeFilter != null ? "  [" + RecipeFilter + "]" : "", total.Total, total.Ok, total.Ng, FormatYield(total));
            _lblSummary.ForeColor = total.Total == 0 ? Color.DimGray : Color.Black;

            _lastSeenRecord = _machine.LastRecord;
            _loadedAt = DateTime.Now;
        }

        private void UpdateRecipeList(DailyProduction day)
        {
            string selected = RecipeFilter;
            _isReloading = true;
            _cboRecipe.BeginUpdate();
            _cboRecipe.Items.Clear();
            _cboRecipe.Items.Add(AllRecipes);
            foreach (var recipe in day.Recipes) _cboRecipe.Items.Add(recipe);
            if (selected != null && !day.Recipes.Contains(selected)) _cboRecipe.Items.Add(selected);
            _cboRecipe.SelectedIndex = selected == null ? 0 : _cboRecipe.Items.IndexOf(selected);
            _cboRecipe.EndUpdate();
            _isReloading = false;
        }

        private void FillHours(DailyProduction day)
        {
            int currentHour = IsViewingToday ? DateTime.Now.Hour : -1;
            for (int h = 0; h < 24; h++)
            {
                var row = _gridHours.Rows[h];
                SetCounts(row, day.Hours[h]);
                var back = h == currentHour ? Color.FromArgb(255, 248, 200) : (day.Hours[h].Total == 0 ? Color.FromArgb(250, 250, 250) : Color.White);
                if (row.DefaultCellStyle.BackColor != back) row.DefaultCellStyle.BackColor = back;
                row.DefaultCellStyle.ForeColor = day.Hours[h].Total == 0 ? Color.Silver : Color.Black;
                bool visible = !_chkHideEmpty.Checked || day.Hours[h].Total > 0 || h == currentHour; // giờ hiện tại luôn hiện
                if (row.Visible != visible) row.Visible = visible;
            }
            SetCounts(_gridHours.Rows[24], day.Total);
            _gridHours.ClearSelection();
        }

        private static void SetCounts(DataGridViewRow row, ProductionCounts counts)
        {
            bool empty = counts.Total == 0;
            SetCell(row, 1, empty ? "" : counts.Jig1Ok.ToString());
            SetCell(row, 2, empty ? "" : counts.Jig1Ng.ToString());
            SetCell(row, 3, empty ? "" : counts.Jig2Ok.ToString());
            SetCell(row, 4, empty ? "" : counts.Jig2Ng.ToString());
            SetCell(row, 5, counts.Total.ToString());
            SetCell(row, 6, empty ? "" : counts.Ok.ToString());
            SetCell(row, 7, empty ? "" : counts.Ng.ToString());
            SetCell(row, 8, FormatYield(counts));
            // NG > 0: tô đỏ ô NG để dễ thấy giờ có lỗi
            row.Cells[7].Style.ForeColor = counts.Ng > 0 ? Ui.NgColor : Color.Empty;
            row.Cells[8].Style.ForeColor = counts.YieldPercent.HasValue && counts.YieldPercent < 100 ? Ui.NgColor : Color.Empty;
        }

        private void FillDays(System.Collections.Generic.List<DailyProduction> days)
        {
            _gridDays.Rows.Clear();
            foreach (var day in days)
            {
                var t = day.Total;
                int index = _gridDays.Rows.Add(day.Date.ToString("ddd dd/MM", Vietnamese), t.Total, t.Total > 0 ? t.Ok.ToString() : "", t.Total > 0 ? t.Ng.ToString() : "", FormatYield(t));
                var row = _gridDays.Rows[index];
                row.Tag = day.Date;
                if (day.Date == _datePicker.Value.Date) { row.DefaultCellStyle.Font = Ui.BoldFont; row.DefaultCellStyle.BackColor = Color.FromArgb(220, 235, 255); }
                if (t.Total == 0) row.DefaultCellStyle.ForeColor = Color.Silver;
                else if (t.Ng > 0) row.Cells[3].Style.ForeColor = Ui.NgColor;
            }
            _gridDays.ClearSelection();
        }

        private static void SetCell(DataGridViewRow row, int index, string value)
        {
            if (!Equals(row.Cells[index].Value as string, value)) row.Cells[index].Value = value;
        }

        private static string FormatYield(ProductionCounts counts)
        {
            return counts.YieldPercent.HasValue ? counts.YieldPercent.Value.ToString("0.0") + " %" : "-";
        }

        private void OpenResultFolder()
        {
            string folder = _machine.Config.ResolveResultFolder();
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        public void RefreshView()
        {
            // Đang xem hôm nay: có kết quả đo mới thì đọc lại; qua giờ mới / qua ngày mới cũng làm mới
            if (!IsViewingToday) return;
            bool hasNewRecord = !ReferenceEquals(_machine.LastRecord, _lastSeenRecord);
            bool hourChanged = DateTime.Now.Hour != _loadedAt.Hour || DateTime.Now.Date != _loadedAt.Date;
            if (hasNewRecord || hourChanged) Reload();
        }
    }
}
