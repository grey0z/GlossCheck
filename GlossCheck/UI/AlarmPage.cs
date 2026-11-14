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
    /// Tab Alarm: alarm đang có (bấm để mở popup), lịch sử alarm theo ngày (Logs\Alarms\yyyy-MM-dd.csv)
    /// và bảng tra cứu tất cả mã lỗi kèm nguyên nhân / cách xử lý.
    /// </summary>
    public class AlarmPage : UserControl, IRefreshablePage
    {
        private readonly Machine _machine;
        private readonly Action _showPopup;
        private readonly Label _lblActive;
        private readonly DateTimePicker _datePicker;
        private readonly Label _lblSummary;
        private readonly DataGridView _gridHistory;
        private int _loadedVersion = -1;
        private DateTime _loadedDay;

        public AlarmPage(Machine machine, Action showPopup)
        {
            _machine = machine;
            _showPopup = showPopup;
            Font = Ui.NormalFont;

            // ---------- Alarm đang có ----------
            _lblActive = new Label { Dock = DockStyle.Top, Height = 44, Font = Ui.TitleFont, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0), AutoEllipsis = true, Cursor = Cursors.Hand };
            _lblActive.Click += (s, e) => { if (_machine.ActiveAlarm != null) _showPopup(); };

            // ---------- Lịch sử ----------
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
            _datePicker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 130, Margin = new Padding(3, 5, 3, 3) };
            _datePicker.ValueChanged += (s, e) => LoadHistory();
            var btnToday = Ui.Button("Hôm nay", (s, e) => { _datePicker.Value = DateTime.Today; LoadHistory(); }, 90);
            var btnFolder = Ui.Button("Mở thư mục", (s, e) => OpenFolder(), 110);
            _lblSummary = Ui.Label("", 0, Ui.BoldFont);
            _lblSummary.Margin = new Padding(16, 9, 3, 3);
            toolbar.Controls.AddRange(new Control[] { Ui.Label("Lịch sử ngày:"), _datePicker, btnToday, btnFolder, _lblSummary });
            toolbar.Controls[0].Margin = new Padding(3, 9, 3, 3);

            _gridHistory = Grid();
            _gridHistory.Columns.Add("Time", "Thời gian");
            _gridHistory.Columns.Add("Code", "Mã");
            _gridHistory.Columns.Add("Type", "Loại");
            _gridHistory.Columns.Add("Message", "Nội dung");
            _gridHistory.Columns.Add("Source", "Xảy ra tại");
            _gridHistory.Columns.Add("Reset", "RESET lúc");
            _gridHistory.Columns.Add("Duration", "Kéo dài");
            SetWidths(_gridHistory, 90, 60, 140, 0, 280, 100, 80);
            _gridHistory.Columns["Message"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            var historyBox = new GroupBox { Text = "Lịch sử alarm", Dock = DockStyle.Fill, Font = Ui.BoldFont, Padding = new Padding(6) };
            _gridHistory.Font = Ui.NormalFont;
            toolbar.Font = Ui.NormalFont;
            historyBox.Controls.Add(_gridHistory);
            historyBox.Controls.Add(toolbar);

            // ---------- Tra cứu mã lỗi ----------
            var gridCodes = Grid();
            gridCodes.Columns.Add("Code", "Mã");
            gridCodes.Columns.Add("Text", "Nội dung");
            gridCodes.Columns.Add("Cause", "Nguyên nhân");
            gridCodes.Columns.Add("Remedy", "Cách xử lý");
            SetWidths(gridCodes, 60, 300, 300, 0);
            gridCodes.Columns["Remedy"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            gridCodes.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            gridCodes.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            foreach (var pair in MachineAlarm.AllInfos.OrderBy(p => (int)p.Key))
                gridCodes.Rows.Add(MachineAlarm.FormatCode(pair.Key), pair.Value.Text, pair.Value.Cause, pair.Value.Remedy);
            gridCodes.Font = Ui.NormalFont;
            var codesBox = new GroupBox { Text = "Tra cứu mã lỗi", Dock = DockStyle.Fill, Font = Ui.BoldFont, Padding = new Padding(6) };
            codesBox.Controls.Add(gridCodes);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6 };
            split.Panel1.Controls.Add(historyBox);
            split.Panel2.Controls.Add(codesBox);
            HandleCreated += (s, e) => split.SplitterDistance = Math.Max(150, split.Height / 2);

            Controls.Add(split);
            Controls.Add(_lblActive);
            LoadHistory();
        }

        private static DataGridView Grid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
            };
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 235, 252);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.ColumnHeadersDefaultCellStyle.Font = Ui.BoldFont;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234, 238, 242);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(234, 238, 242); // không tô xanh tiêu đề cột
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.Black;
            grid.EnableHeadersVisualStyles = false;
            grid.VisibleChanged += (s, e) => grid.ClearSelection(); // không tự chọn dòng đầu khi mở tab
            Ui.DoubleBuffer(grid);
            return grid;
        }

        private static void SetWidths(DataGridView grid, params int[] widths)
        {
            for (int i = 0; i < widths.Length; i++)
            {
                grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
                if (widths[i] > 0) grid.Columns[i].Width = widths[i];
            }
        }

        public void RefreshView()
        {
            var alarm = _machine.ActiveAlarm;
            if (alarm == null) Ui.Set(_lblActive, "Không có alarm", Ui.PanelColor, Ui.OkColor);
            else Ui.Set(_lblActive, "ĐANG ALARM: " + alarm + "   (bấm để xem chi tiết)", Color.FromArgb(255, 235, 230), Ui.NgColor);

            // Đang xem hôm nay: tải lại khi có alarm mới / RESET
            if (_datePicker.Value.Date == DateTime.Today && (_machine.AlarmHistory.Version != _loadedVersion || _loadedDay != DateTime.Today))
                LoadHistory();
        }

        private void LoadHistory()
        {
            var day = _datePicker.Value.Date;
            _loadedVersion = _machine.AlarmHistory.Version;
            _loadedDay = day;
            var records = _machine.AlarmHistory.GetDay(day);

            _gridHistory.SuspendLayout();
            _gridHistory.Rows.Clear();
            foreach (var r in records)
            {
                int index = _gridHistory.Rows.Add(
                    r.Time.ToString("HH:mm:ss"), r.CodeText, r.IsEmergency ? "DỪNG KHẨN CẤP" : "ALARM", r.Message, r.Source,
                    r.ResetAt.HasValue ? r.ResetAt.Value.ToString("HH:mm:ss") : "(chưa RESET)",
                    r.Duration.HasValue ? FormatDuration(r.Duration.Value) : "");
                if (!r.ResetAt.HasValue) _gridHistory.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 230);
            }
            _gridHistory.ResumeLayout();
            _gridHistory.ClearSelection();

            var top = records.GroupBy(r => r.CodeText).OrderByDescending(g => g.Count()).FirstOrDefault();
            _lblSummary.Text = records.Count == 0
                ? "Không có alarm"
                : records.Count + " lần alarm" + (top != null && top.Count() > 1 ? "   |   hay gặp nhất: " + top.Key + " (" + top.Count() + " lần)" : "");
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1) return ((int)duration.TotalHours) + "h " + duration.Minutes.ToString("00") + "m";
            if (duration.TotalMinutes >= 1) return ((int)duration.TotalMinutes) + "m " + duration.Seconds.ToString("00") + "s";
            return Math.Max(0, (int)duration.TotalSeconds) + "s";
        }

        private static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(AlarmHistory.Folder);
                Process.Start("explorer.exe", "\"" + AlarmHistory.Folder + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "GlossCheck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
