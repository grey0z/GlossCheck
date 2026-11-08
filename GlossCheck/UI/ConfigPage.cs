using System;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Settings;

namespace GlossCheck.UI
{
    /// <summary>
    /// Tab Cấu hình: sửa MachineConfig bằng PropertyGrid.
    /// Tốc độ, timeout, thư mục kết quả áp dụng ngay; IP, pulse/mm, giới hạn mềm, chế độ chạy cần khởi động lại.
    /// </summary>
    public class ConfigPage : UserControl, IRefreshablePage
    {
        private readonly Machine _machine;
        private readonly PropertyGrid _grid;
        private MachineConfig _editing;

        public ConfigPage(Machine machine)
        {
            _machine = machine;
            Padding = new Padding(6);

            _grid = new PropertyGrid { Dock = DockStyle.Fill, Font = Ui.NormalFont, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56 };
            bottom.Controls.Add(Ui.Button("Lưu cấu hình", (s, e) => Save(), 150, 42));
            bottom.Controls.Add(Ui.Button("Huỷ thay đổi", (s, e) => Reload(), 130, 42));
            bottom.Controls.Add(Ui.Label("Tốc độ / timeout / thư mục kết quả: áp dụng ngay.   IP, pulse/mm, giới hạn mềm, chế độ chạy: cần khởi động lại phần mềm."));

            Controls.Add(_grid);
            Controls.Add(bottom);
            Reload();
        }

        private void Reload()
        {
            try { _editing = MachineConfig.Load(MachineConfig.DefaultPath); }
            catch { _editing = _machine.Config.Clone(); }
            _grid.SelectedObject = _editing;
            _grid.ExpandAllGridItems();
        }

        private void Save()
        {
            var state = _machine.State;
            if (state == MachineState.Running || state == MachineState.Homing)
            {
                MessageBox.Show("Dừng máy trước khi lưu cấu hình.", "Cấu hình");
                return;
            }
            try
            {
                _editing.ActiveRecipe = _machine.Recipe.Name;
                _editing.Save(MachineConfig.DefaultPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không lưu được: " + ex.Message, "Cấu hình", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _machine.CmdApplyRuntimeConfig(_editing.Clone());

            var current = _machine.Config;
            bool needsRestart = _editing.Mode != current.Mode
                || _editing.AxisX.IpAddress != current.AxisX.IpAddress || _editing.AxisY.IpAddress != current.AxisY.IpAddress
                || _editing.AxisX.PulsePerMm != current.AxisX.PulsePerMm || _editing.AxisY.PulsePerMm != current.AxisY.PulsePerMm
                || _editing.AxisX.MinPosition != current.AxisX.MinPosition || _editing.AxisX.MaxPosition != current.AxisX.MaxPosition
                || _editing.AxisY.MinPosition != current.AxisY.MinPosition || _editing.AxisY.MaxPosition != current.AxisY.MaxPosition
                || _editing.Io.InputModuleIp != current.Io.InputModuleIp || _editing.Io.OutputModuleIp != current.Io.OutputModuleIp
                || _editing.Io.InputInvertMask != current.Io.InputInvertMask || _editing.Meter.PortName != current.Meter.PortName
                || _editing.Motion.AccelTimeMs != current.Motion.AccelTimeMs;
            MessageBox.Show(needsRestart ? "Đã lưu. Có thay đổi phần cứng: hãy khởi động lại phần mềm để áp dụng." : "Đã lưu và áp dụng.", "Cấu hình");
        }

        public void RefreshView() { }
    }
}
