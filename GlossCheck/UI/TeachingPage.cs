using System;
using System.Drawing;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Settings;

namespace GlossCheck.UI
{
    /// <summary>
    /// Tab Teaching: jog tới vị trí, bấm "Lấy vị trí hiện tại" cho từng điểm (Ready / Jig 1 / Jig 2),
    /// "Đi tới" để kiểm tra, rồi "Lưu" vào recipe đang dùng.
    /// </summary>
    public class TeachingPage : UserControl, IRefreshablePage
    {
        private readonly Machine _machine;
        private readonly JogPanel _jogPanel;
        private const int PointCount = 2;
        private readonly NumericUpDown[] _numX = new NumericUpDown[PointCount];
        private readonly NumericUpDown[] _numY = new NumericUpDown[PointCount];
        private readonly Label _lblRecipe;
        private readonly Label _lblDirty;
        private string _loadedRecipeName;
        private Recipe _loadedRecipe;
        private bool _isLoading;
        private bool _isDirty;

        // Cơ cấu con thoi: Pos1 = Jig 1 vào dưới đầu đo (Jig 2 ra ngoài), Pos2 = ngược lại. Ready = Pos1.
        private static readonly string[] PointNames = { "Pos1: Jig 1 (trái) vào - Ready", "Pos2: Jig 2 (phải) vào" };

        public TeachingPage(Machine machine)
        {
            _machine = machine;
            Padding = new Padding(6);
            var root = Ui.Flow(FlowDirection.LeftToRight, true);
            root.Dock = DockStyle.Fill;

            _jogPanel = new JogPanel(machine);
            root.Controls.Add(Ui.Group("Jog", _jogPanel, 350, 370));

            var table = new TableLayoutPanel { ColumnCount = 5, RowCount = PointCount + 1, AutoSize = true };
            string[] headers = { "Vị trí", "X (mm)", "Y (mm)", "", "" };
            for (int c = 0; c < headers.Length; c++) table.Controls.Add(Ui.Label(headers[c], 0, Ui.BoldFont), c, 0);
            for (int i = 0; i < PointCount; i++)
            {
                int index = i;
                _numX[i] = Ui.Number(-1000, 1000, 2, 0.1, 100);
                _numY[i] = Ui.Number(-1000, 1000, 2, 0.1, 100);
                _numX[i].ValueChanged += (s, e) => MarkDirty();
                _numY[i].ValueChanged += (s, e) => MarkDirty();
                table.Controls.Add(Ui.Label(PointNames[i], 230, Ui.BoldFont), 0, i + 1);
                table.Controls.Add(_numX[i], 1, i + 1);
                table.Controls.Add(_numY[i], 2, i + 1);
                table.Controls.Add(Ui.Button("Lấy vị trí hiện tại", (s, e) => CaptureCurrent(index), 160), 3, i + 1);
                table.Controls.Add(Ui.Button("Đi tới", (s, e) => GoTo(index), 90), 4, i + 1);
            }

            var pointFlow = Ui.Flow(FlowDirection.TopDown, false);
            _lblRecipe = Ui.Label("", 700, Ui.TitleFont);
            _lblDirty = Ui.Label("", 700, Ui.BoldFont);
            _lblDirty.ForeColor = Ui.WaitColor;
            var saveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            saveRow.Controls.Add(Ui.Button("Lưu vào model", (s, e) => Save(), 150, 40));
            saveRow.Controls.Add(Ui.Button("Huỷ thay đổi", (s, e) => LoadFromMachine(), 130, 40));
            pointFlow.Controls.Add(_lblRecipe);
            pointFlow.Controls.Add(table);
            pointFlow.Controls.Add(saveRow);
            pointFlow.Controls.Add(_lblDirty);
            pointFlow.Controls.Add(Ui.Label(
                "Trục Y (giữa) mang 2 jig: Pos1 = Jig 1 (trái) vào trong, Jig 2 (phải) ra ngoài; Pos2 = ngược lại.\r\n" +
                "Trục X đưa máy đo sang bên jig đang ở trong. Mỗi vị trí teach cả X (máy đo) và Y (con thoi).\r\n" +
                "Cách teach: jog Y cho jig vào, jog X cho máy đo nằm đúng trên jig (xi lanh ở dưới) -> \"Lấy vị trí hiện tại\".\r\n" +
                "Sau ORIGIN máy về Pos1 (Ready). \"Đi tới\" luôn hạ cả 2 xi lanh trước khi chạy. Bấm \"Lưu vào model\" để áp dụng.", 700, null, 4));
            root.Controls.Add(Ui.Group("Vị trí teach", pointFlow, 740, 400));

            Controls.Add(root);
            LoadFromMachine();
        }

        private void LoadFromMachine()
        {
            _isLoading = true;
            _loadedRecipe = _machine.Recipe;
            _loadedRecipeName = _loadedRecipe.Name;
            var points = Points(_loadedRecipe.Points);
            for (int i = 0; i < PointCount; i++)
            {
                Ui.SetNumber(_numX[i], points[i].X);
                Ui.SetNumber(_numY[i], points[i].Y);
            }
            _isLoading = false;
            _isDirty = false;
        }

        private static XyPoint[] Points(TeachPoints points) { return new[] { points.Jig1, points.Jig2 }; }

        private void MarkDirty() { if (!_isLoading) _isDirty = true; }

        private void CaptureCurrent(int index)
        {
            if (!_machine.AllHomed) { MessageBox.Show("Cần ORIGIN trước khi teach.", "Teaching"); return; }
            Ui.SetNumber(_numX[index], Math.Round(_machine.X.Position, 2));
            Ui.SetNumber(_numY[index], Math.Round(_machine.Y.Position, 2));
        }

        private void GoTo(int index)
        {
            var point = new XyPoint((double)_numX[index].Value, (double)_numY[index].Value);
            _machine.CmdMoveTo("Đi tới " + PointNames[index], point);
        }

        private void Save()
        {
            var state = _machine.State;
            if (state == MachineState.Running || state == MachineState.Homing)
            {
                MessageBox.Show("Dừng máy trước khi lưu vị trí teach.", "Teaching");
                return;
            }
            var recipe = _machine.Recipe.Clone();
            recipe.Points.Jig1 = new XyPoint((double)_numX[0].Value, (double)_numY[0].Value);
            recipe.Points.Jig2 = new XyPoint((double)_numX[1].Value, (double)_numY[1].Value);
            try
            {
                RecipeStore.Save(recipe);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không lưu được: " + ex.Message, "Teaching", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _machine.CmdApplyRecipe(recipe);
            _loadedRecipe = recipe;
            _isDirty = false;
            _machine.Log("Đã lưu vị trí teach vào model " + recipe.Name);
        }

        public void RefreshView()
        {
            _jogPanel.RefreshView();
            // Recipe bị đổi ở tab khác -> nạp lại (nếu không có thay đổi chưa lưu)
            if (!_isDirty && !ReferenceEquals(_machine.Recipe, _loadedRecipe)) LoadFromMachine();
            Ui.Set(_lblRecipe, "Model: " + _loadedRecipeName);
            Ui.Set(_lblDirty, _isDirty ? "Có thay đổi chưa lưu." : "");
        }
    }
}
