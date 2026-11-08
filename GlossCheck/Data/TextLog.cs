using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace GlossCheck.Data
{
    /// <summary>Ghi log ra file Logs\yyyy-MM-dd.log trên thread nền (không làm chậm vòng quét).</summary>
    public sealed class TextLog : IDisposable
    {
        private readonly BlockingCollection<string> _lines = new BlockingCollection<string>(10000);
        private readonly Thread _writer;
        private readonly string _folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public TextLog()
        {
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "TextLog" };
            _writer.Start();
        }

        public void Write(string line) { _lines.TryAdd(line); }

        public void Dispose()
        {
            _lines.CompleteAdding();
            _writer.Join(1000);
        }

        private void WriteLoop()
        {
            foreach (var line in _lines.GetConsumingEnumerable())
            {
                try
                {
                    Directory.CreateDirectory(_folder);
                    File.AppendAllText(Path.Combine(_folder, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // bỏ qua lỗi ghi log
                }
            }
        }
    }
}
