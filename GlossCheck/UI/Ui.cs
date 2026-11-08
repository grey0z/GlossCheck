using System;
using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;

namespace GlossCheck.UI
{
    /// <summary>Trang trong TabControl, được MainForm gọi RefreshView() theo timer khi đang hiển thị.</summary>
    public interface IRefreshablePage
    {
        void RefreshView();
    }

    /// <summary>Hàm tạo control dùng chung để các trang có cùng kiểu dáng.</summary>
    internal static class Ui
    {
        public static readonly Font NormalFont = new Font("Segoe UI", 10F);
        public static readonly Font BoldFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("Segoe UI", 13F, FontStyle.Bold);
        public static readonly Font BigFont = new Font("Segoe UI", 26F, FontStyle.Bold);
        public static readonly Font ValueFont = new Font("Consolas", 14F, FontStyle.Bold);

        public static readonly Color OkColor = Color.FromArgb(46, 160, 67);
        public static readonly Color NgColor = Color.FromArgb(207, 34, 46);
        public static readonly Color WaitColor = Color.FromArgb(230, 160, 30);
        public static readonly Color BusyColor = Color.FromArgb(31, 111, 235);
        public static readonly Color IdleColor = Color.FromArgb(140, 149, 159);
        public static readonly Color PanelColor = Color.FromArgb(246, 248, 250);

        public static Button Button(string text, EventHandler click, int width = 120, int height = 34)
        {
            var button = new Button { Text = text, Width = width, Height = height, Font = NormalFont, Margin = new Padding(3) };
            if (click != null) button.Click += click;
            return button;
        }

        /// <param name="width">0 = tự co theo chữ (1 dòng).</param>
        /// <param name="lines">Số dòng hiển thị khi đặt width cố định (chữ tự xuống dòng).</param>
        public static Label Label(string text, int width = 0, Font font = null, int lines = 1)
        {
            var label = new Label { Text = text, Font = font ?? NormalFont, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3) };
            if (width > 0) { label.Width = width; label.Height = 6 + lines * (label.Font.Height + 2); } else label.AutoSize = true;
            return label;
        }

        public static NumericUpDown Number(double min, double max, int decimals, double increment, int width = 100)
        {
            return new NumericUpDown
            {
                Minimum = (decimal)min, Maximum = (decimal)max, DecimalPlaces = decimals,
                Increment = (decimal)increment, Width = width, Font = NormalFont, TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(3),
            };
        }

        public static void SetNumber(NumericUpDown box, double value)
        {
            decimal v = (decimal)value;
            if (v < box.Minimum) v = box.Minimum;
            if (v > box.Maximum) v = box.Maximum;
            box.Value = v;
        }

        public static GroupBox Group(string text, Control content, int width, int height)
        {
            var group = new GroupBox { Text = text, Width = width, Height = height, Font = BoldFont, Margin = new Padding(6), Padding = new Padding(8) };
            content.Dock = DockStyle.Fill;
            content.Font = NormalFont;
            group.Controls.Add(content);
            return group;
        }

        public static FlowLayoutPanel Flow(FlowDirection direction = FlowDirection.LeftToRight, bool wrap = true)
        {
            return new FlowLayoutPanel { FlowDirection = direction, WrapContents = wrap, AutoScroll = true };
        }

        /// <summary>Đặt text / màu chỉ khi đổi, tránh vẽ lại liên tục mỗi lần timer chạy.</summary>
        public static void Set(Label label, string text, Color? back = null, Color? fore = null)
        {
            if (label.Text != text) label.Text = text;
            if (back.HasValue && label.BackColor != back.Value) label.BackColor = back.Value;
            if (fore.HasValue && label.ForeColor != fore.Value) label.ForeColor = fore.Value;
        }

        public static void SetEnabled(Control control, bool enabled) { if (control.Enabled != enabled) control.Enabled = enabled; }

        public static Color StateColor(MachineState state)
        {
            switch (state)
            {
                case MachineState.Running: return OkColor;
                case MachineState.Paused: return WaitColor;
                case MachineState.Ready: return BusyColor;
                case MachineState.Homing: return Color.SteelBlue;
                case MachineState.Alarm: return Color.DarkOrange;
                case MachineState.Emergency: return NgColor;
                default: return IdleColor;
            }
        }

        public static string StateText(MachineState state)
        {
            switch (state)
            {
                case MachineState.NotHomed: return "CHƯA ORIGIN";
                case MachineState.Homing: return "ĐANG ORIGIN";
                case MachineState.Ready: return "SẴN SÀNG";
                case MachineState.Running: return "ĐANG CHẠY";
                case MachineState.Paused: return "TẠM DỪNG";
                case MachineState.Alarm: return "ALARM";
                default: return "DỪNG KHẨN CẤP";
            }
        }

        public static Color JigColor(JigState state)
        {
            switch (state)
            {
                case JigState.Measuring: return BusyColor;
                case JigState.Ok: return OkColor;
                case JigState.Ng: return NgColor;
                default: return IdleColor;
            }
        }

        /// <summary>Bật double buffer cho control (thuộc tính protected) để không nhấp nháy.</summary>
        public static void DoubleBuffer(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(control, true);
        }
    }
}
