using System;
using System.IO;
using System.Windows.Forms;
using GlossCheck.Core;
using GlossCheck.Settings;
using GlossCheck.UI;

namespace GlossCheck
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MachineConfig config;
            try
            {
                config = MachineConfig.Load(MachineConfig.DefaultPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không đọc được " + MachineConfig.FileName + ":\r\n" + ex.Message, "GlossCheck", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (config.Mode == RunMode.Real && !CheckNativeDlls()) return;

            var recipe = RecipeStore.LoadOrCreate(config.ActiveRecipe);
            using (var machine = new Machine(config, recipe))
            {
                machine.StartScan();
                // Lỗi trên thread UI: ghi log và báo ngắn gọn, máy (thread quét) vẫn chạy bình thường
                Application.ThreadException += (s, e) =>
                {
                    machine.Log("Lỗi giao diện: " + e.Exception);
                    MessageBox.Show("Lỗi giao diện: " + e.Exception.Message + "\r\nChi tiết đã ghi vào log.", "GlossCheck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                Application.Run(new MainForm(machine));
            }
        }

        private static bool CheckNativeDlls()
        {
            foreach (var dll in new[] { "EziMOTIONPlusE.dll", "NGComm.dll" })
            {
                if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dll))) continue;
                MessageBox.Show("Thiếu " + dll + " (bản x64) cạnh GlossCheck.exe.", "GlossCheck", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
    }
}
