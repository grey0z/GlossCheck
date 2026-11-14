using System;
using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;

namespace GlossCheck.UI
{
    /// <summary>
    /// Popup cảnh báo khi máy báo alarm / dừng khẩn cấp: mã lỗi, nội dung, nơi xảy ra, nguyên nhân, cách xử lý.
    /// MainForm tự mở khi có alarm mới và tự đóng khi alarm đã được RESET (nút trên popup, nút trên màn hình hoặc nút STOP cứng).
    /// Bấm ĐÓNG chỉ ẩn popup, alarm vẫn còn; bấm vào dòng alarm trên đầu màn hình để mở lại.
    /// </summary>
    public class AlarmForm : Form
    {
        private readonly Machine _machine;
        private readonly Label _lblBand;
        private readonly Label _lblTime;
        private readonly Label _lblTitle;
        private readonly Label _lblSource;
        private readonly Label _lblCause;
        private readonly Label _lblRemedy;
        private readonly Label _lblHint;
        private readonly Button _btnReset;
        private readonly Button _btnMute;
        private MachineAlarm _alarm;

        public AlarmForm(Machine machine)
        {
            _machine = machine;
            Text = "Cảnh báo";
            Font = Ui.NormalFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(720, 560);
            BackColor = Color.White;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(0) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));  // dải màu: ALARM E301
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));  // nội dung
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // nơi xảy ra
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));   // nguyên nhân
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));   // cách xử lý
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // gợi ý sau RESET
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));  // nút

            var band = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            band.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            band.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            band.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _lblBand = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0), Margin = new Padding(0) };
            _lblTime = new Label { Dock = DockStyle.Fill, Font = Ui.BoldFont, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 14, 0), Margin = new Padding(0) };
            band.Controls.Add(_lblBand, 0, 0);
            band.Controls.Add(_lblTime, 1, 0);
            _bandPanel = band;

            _lblTitle = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 15F, FontStyle.Bold), ForeColor = Ui.NgColor, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0), AutoEllipsis = true };
            _lblSource = new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0), AutoEllipsis = true };
            _lblCause = new Label { Dock = DockStyle.Fill, Padding = new Padding(4), Font = new Font("Segoe UI", 11F) };
            _lblRemedy = new Label { Dock = DockStyle.Fill, Padding = new Padding(4), Font = new Font("Segoe UI", 11F) };
            _lblHint = new Label { Dock = DockStyle.Fill, Font = Ui.BoldFont, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0), AutoEllipsis = true };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6), WrapContents = false };
            var btnClose = new Button { Text = "ĐÓNG", Width = 120, Height = 54, Font = Ui.TitleFont, Margin = new Padding(6, 0, 0, 0) };
            btnClose.Click += (s, e) => Hide();
            _btnMute = new Button { Text = "TẮT CÒI", Width = 140, Height = 54, Font = Ui.TitleFont, Margin = new Padding(6, 0, 0, 0) };
            _btnMute.Click += (s, e) => _machine.CmdMuteBuzzer();
            _btnReset = new Button { Text = "RESET", Width = 160, Height = 54, Font = Ui.TitleFont, BackColor = Color.LightCoral, Margin = new Padding(6, 0, 0, 0) };
            _btnReset.Click += (s, e) => _machine.CmdReset();
            _btnReset.EnabledChanged += (s, e) => _btnReset.BackColor = _btnReset.Enabled ? Color.LightCoral : Color.Gainsboro;
            buttons.Controls.AddRange(new Control[] { btnClose, _btnMute, _btnReset });

            layout.Controls.Add(band, 0, 0);
            layout.Controls.Add(_lblTitle, 0, 1);
            layout.Controls.Add(_lblSource, 0, 2);
            layout.Controls.Add(Section("Nguyên nhân", _lblCause), 0, 3);
            layout.Controls.Add(Section("Cách xử lý", _lblRemedy), 0, 4);
            layout.Controls.Add(_lblHint, 0, 5);
            layout.Controls.Add(buttons, 0, 6);
            Controls.Add(layout);

            CancelButton = btnClose;
        }

        private readonly Control _bandPanel;

        private static GroupBox Section(string title, Control content)
        {
            var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Font = Ui.BoldFont, Margin = new Padding(12, 2, 12, 2), Padding = new Padding(8, 4, 8, 4) };
            group.Controls.Add(content); // nội dung đã đặt font riêng (không in đậm như tiêu đề nhóm)
            return group;
        }

        /// <summary>Hiện popup cho alarm này, căn giữa màn hình chính.</summary>
        public void ShowAlarm(MachineAlarm alarm, Form owner)
        {
            _alarm = alarm;
            _lblTime.Text = alarm.Time.ToString("dd'/'MM'/'yyyy  HH:mm:ss");
            _lblTitle.Text = string.IsNullOrEmpty(alarm.Detail) ? alarm.Title : alarm.Title + "\r\n" + alarm.Detail;
            _lblSource.Text = "Xảy ra tại: " + (string.IsNullOrEmpty(alarm.Source) ? "-" : alarm.Source);
            _lblCause.Text = alarm.Cause;
            _lblRemedy.Text = alarm.Remedy;
            RefreshView();

            if (owner != null)
                Location = new Point(owner.Left + (owner.Width - Width) / 2, owner.Top + (owner.Height - Height) / 2);
            if (!Visible) Show(owner);
            Activate();
        }

        /// <summary>MainForm gọi theo timer khi popup đang hiện: nhấp nháy dải màu, cập nhật nút.</summary>
        public void RefreshView()
        {
            if (_alarm == null) return;
            var state = _machine.State;
            // Trạng thái có thể đổi sau alarm 1 chút (vòng quét đặt alarm trước rồi mới đổi state) nên cập nhật mỗi lần
            bool emergency = state == MachineState.Emergency || _alarm.Code == AlarmCode.IoCommLost;
            string kind = emergency ? "DỪNG KHẨN CẤP" : "ALARM";
            if (Text != kind) Text = kind;
            Ui.Set(_lblBand, kind + "   " + _alarm.CodeText);
            bool blink = DateTime.Now.Millisecond < 500;
            var baseColor = emergency ? Ui.NgColor : Color.DarkOrange;
            var bandColor = _machine.IsBuzzerSounding && !blink ? ControlPaint.Light(baseColor) : baseColor;
            if (_bandPanel.BackColor != bandColor) _bandPanel.BackColor = bandColor;

            bool ioStillLost = emergency && !_machine.IsIoConnected;
            Ui.SetEnabled(_btnReset, (state == MachineState.Alarm || emergency) && !ioStillLost);
            Ui.SetEnabled(_btnMute, _machine.IsBuzzerSounding);

            string hint;
            Color hintColor = Color.DimGray;
            if (ioStillLost) { hint = "Chưa RESET được: module IO vẫn mất kết nối."; hintColor = Ui.NgColor; }
            else if (emergency || _alarm.NeedsOrigin) hint = "Sau khi RESET: bấm ORIGIN rồi mới chạy lại.";
            else if (_machine.Measure.IsActive) hint = "Sau khi RESET: bấm START để chạy tiếp bước bị lỗi (RESET lần nữa để huỷ lần đo).";
            else hint = "Xử lý xong thì bấm RESET (hoặc nút STOP trên máy).";
            Ui.Set(_lblHint, hint, null, hintColor);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Nút X chỉ ẩn popup (dùng lại cho lần sau); đóng hẳn khi thoát chương trình
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
