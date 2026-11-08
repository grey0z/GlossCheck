using System;
using System.Linq;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Hardware;
using GlossCheck.Settings;

namespace GlossCheck.UI
{
    /// <summary>
    /// Tab Model / Spec: danh sách model (recipe), tạo / sao chép / xoá / chọn dùng,
    /// sửa spec 3 góc (bật/tắt, Min, Max) và số lần đo. Vị trí teach sửa ở tab Teaching.
    /// </summary>
    public class RecipePage : UserControl, IRefreshablePage
    {
        private readonly Machine _machine;
        private readonly ListBox _lstRecipes;
        private readonly Label _lblEditing;
        private readonly CheckBox[] _chkEnabled = new CheckBox[3];
        private readonly NumericUpDown[] _numMin = new NumericUpDown[3];
        private readonly NumericUpDown[] _numMax = new NumericUpDown[3];
        private readonly NumericUpDown _numCount;
        private readonly Label _lblActive;
        private Recipe _editing;

        public RecipePage(Machine machine)
        {
            _machine = machine;
            Padding = new Padding(6);
            var root = Ui.Flow(FlowDirection.LeftToRight, true);
            root.Dock = DockStyle.Fill;

            // ---------- Danh sách ----------
            var listFlow = Ui.Flow(FlowDirection.TopDown, false);
            _lstRecipes = new ListBox { Width = 260, Height = 270, Font = Ui.TitleFont, IntegralHeight = false };
            _lstRecipes.SelectedIndexChanged += (s, e) => LoadSelected();
            _lblActive = Ui.Label("", 260, Ui.BoldFont);
            var listButtons = new FlowLayoutPanel { Width = 270, Height = 90 };
            listButtons.Controls.Add(Ui.Button("Mới", (s, e) => CreateRecipe(false), 80));
            listButtons.Controls.Add(Ui.Button("Sao chép", (s, e) => CreateRecipe(true), 80));
            listButtons.Controls.Add(Ui.Button("Xoá", (s, e) => DeleteSelected(), 80));
            listButtons.Controls.Add(Ui.Button("DÙNG MODEL NÀY", (s, e) => UseSelected(), 252, 40));
            listFlow.Controls.AddRange(new Control[] { _lblActive, _lstRecipes, listButtons });
            root.Controls.Add(Ui.Group("Model", listFlow, 300, 480));

            // ---------- Spec ----------
            var specTable = new TableLayoutPanel { ColumnCount = 4, RowCount = 4, AutoSize = true };
            string[] headers = { "Góc", "Xét", "Min (GU)", "Max (GU)" };
            for (int c = 0; c < headers.Length; c++) specTable.Controls.Add(Ui.Label(headers[c], 0, Ui.BoldFont), c, 0);
            foreach (var angle in GlossAngles.All)
            {
                int i = (int)angle;
                _chkEnabled[i] = new CheckBox { AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
                _numMin[i] = Ui.Number(0, 2000, 1, 0.5, 110);
                _numMax[i] = Ui.Number(0, 2000, 1, 0.5, 110);
                specTable.Controls.Add(Ui.Label(GlossAngles.Label(angle), 60, Ui.TitleFont), 0, i + 1);
                specTable.Controls.Add(_chkEnabled[i], 1, i + 1);
                specTable.Controls.Add(_numMin[i], 2, i + 1);
                specTable.Controls.Add(_numMax[i], 3, i + 1);
            }

            _numCount = Ui.Number(1, Recipe.MaxMeasureCount, 0, 1, 80);
            var countRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            countRow.Controls.Add(Ui.Label("Số lần đo mỗi jig (lấy trung bình):"));
            countRow.Controls.Add(_numCount);

            var specFlow = Ui.Flow(FlowDirection.TopDown, false);
            _lblEditing = Ui.Label("", 460, Ui.TitleFont);
            specFlow.Controls.Add(_lblEditing);
            specFlow.Controls.Add(specTable);
            specFlow.Controls.Add(countRow);
            specFlow.Controls.Add(Ui.Label("Giá trị nằm trong [Min, Max] (tính cả biên) là OK. Tất cả góc đang bật đều OK thì jig OK.", 460, null, 2));
            specFlow.Controls.Add(Ui.Button("Lưu spec", (s, e) => SaveEditing(), 150, 40));
            root.Controls.Add(Ui.Group("Spec độ bóng", specFlow, 500, 470));

            Controls.Add(root);
            ReloadList(machine.Recipe.Name);
        }

        private void ReloadList(string select)
        {
            _lstRecipes.Items.Clear();
            foreach (var name in RecipeStore.ListNames()) _lstRecipes.Items.Add(name);
            int index = select != null ? _lstRecipes.Items.IndexOf(select) : -1;
            _lstRecipes.SelectedIndex = index >= 0 ? index : (_lstRecipes.Items.Count > 0 ? 0 : -1);
        }

        private string SelectedName { get { return _lstRecipes.SelectedItem as string; } }

        private void LoadSelected()
        {
            if (SelectedName == null) return;
            try { _editing = RecipeStore.Load(SelectedName); }
            catch (Exception ex) { MessageBox.Show("Không đọc được model: " + ex.Message); _editing = null; }
            if (_editing == null) return;
            _lblEditing.Text = "Đang sửa: " + _editing.Name;
            foreach (var angle in GlossAngles.All)
            {
                int i = (int)angle;
                var spec = _editing.Spec(angle);
                _chkEnabled[i].Checked = spec.IsEnabled;
                Ui.SetNumber(_numMin[i], spec.Min);
                Ui.SetNumber(_numMax[i], spec.Max);
            }
            Ui.SetNumber(_numCount, _editing.MeasureCount);
        }

        private void SaveEditing()
        {
            if (_editing == null) return;
            // Lấy lại bản mới nhất trong file để không ghi đè vị trí teach vừa lưu ở tab Teaching
            var recipe = RecipeStore.Load(_editing.Name) ?? _editing.Clone();
            foreach (var angle in GlossAngles.All)
            {
                int i = (int)angle;
                var spec = recipe.Spec(angle);
                spec.IsEnabled = _chkEnabled[i].Checked;
                spec.Min = (double)_numMin[i].Value;
                spec.Max = (double)_numMax[i].Value;
            }
            recipe.MeasureCount = (int)_numCount.Value;

            var errors = recipe.Validate();
            if (errors.Count > 0) { MessageBox.Show(string.Join("\r\n", errors), "Spec không hợp lệ", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            RecipeStore.Save(recipe);
            _editing = recipe;
            _machine.Log("Đã lưu spec model " + recipe.Name);
            if (recipe.Name == _machine.Recipe.Name) _machine.CmdApplyRecipe(recipe);
        }

        private void CreateRecipe(bool copySelected)
        {
            string name = Prompt(copySelected ? "Tên model mới (sao chép từ " + SelectedName + "):" : "Tên model mới:");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (RecipeStore.ListNames().Contains(name)) { MessageBox.Show("Model \"" + name + "\" đã tồn tại."); return; }
            var recipe = copySelected && _editing != null ? _editing.Clone() : new Recipe();
            recipe.Name = name;
            RecipeStore.Save(recipe);
            ReloadList(name);
        }

        private void DeleteSelected()
        {
            if (SelectedName == null) return;
            if (SelectedName == _machine.Recipe.Name) { MessageBox.Show("Không xoá được model đang dùng."); return; }
            if (MessageBox.Show("Xoá model \"" + SelectedName + "\"?", "Xoá model", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            RecipeStore.Delete(SelectedName);
            ReloadList(_machine.Recipe.Name);
        }

        private void UseSelected()
        {
            if (_editing == null) return;
            var state = _machine.State;
            if (state == MachineState.Running || state == MachineState.Homing) { MessageBox.Show("Dừng máy trước khi đổi model."); return; }
            var errors = _editing.Validate();
            if (errors.Count > 0) { MessageBox.Show(string.Join("\r\n", errors), "Model không hợp lệ"); return; }
            _machine.CmdApplyRecipe(_editing.Clone());
            try { MachineConfig.SaveActiveRecipe(MachineConfig.DefaultPath, _editing.Name); } catch { }
        }

        private static string Prompt(string text)
        {
            using (var form = new Form { Text = "GlossCheck", Width = 420, Height = 170, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                var label = new Label { Text = text, Left = 12, Top = 12, Width = 380 };
                var box = new TextBox { Left = 12, Top = 40, Width = 380, Font = Ui.NormalFont };
                var ok = new Button { Text = "OK", Left = 230, Top = 80, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Huỷ", Left = 315, Top = 80, Width = 75, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { label, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog() == DialogResult.OK ? box.Text : null;
            }
        }

        public void RefreshView()
        {
            Ui.Set(_lblActive, "Đang dùng: " + _machine.Recipe.Name);
        }
    }
}
