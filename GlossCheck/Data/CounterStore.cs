using System;
using System.IO;
using System.Threading;
using System.Xml.Serialization;

namespace GlossCheck.Data
{
    /// <summary>Bộ đếm OK/NG của 2 jig, lưu ra Counters.xml cạnh file exe để tắt / mở phần mềm không bị mất.</summary>
    public class CounterSnapshot
    {
        public int Jig1Ok { get; set; }
        public int Jig1Ng { get; set; }
        public int Jig2Ok { get; set; }
        public int Jig2Ng { get; set; }
        public DateTime ResetAt { get; set; } = DateTime.Now;
    }

    public static class CounterStore
    {
        private static readonly object Sync = new object();

        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Counters.xml"); } }

        public static CounterSnapshot Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new CounterSnapshot();
                using (var stream = File.OpenRead(FilePath))
                    return (CounterSnapshot)new XmlSerializer(typeof(CounterSnapshot)).Deserialize(stream);
            }
            catch
            {
                return new CounterSnapshot(); // file hỏng -> bắt đầu lại từ 0, không chặn khởi động máy
            }
        }

        /// <summary>Ghi trên thread nền để không làm chậm vòng quét. Ghi file tạm rồi thay thế để không hỏng file khi mất điện.</summary>
        public static void SaveAsync(CounterSnapshot snapshot)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (Sync)
                {
                    try
                    {
                        string temp = FilePath + ".tmp";
                        using (var stream = File.Create(temp))
                            new XmlSerializer(typeof(CounterSnapshot)).Serialize(stream, snapshot);
                        if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                        else File.Move(temp, FilePath);
                    }
                    catch
                    {
                        // bỏ qua: lần ghi sau sẽ ghi lại
                    }
                }
            });
        }
    }
}
