using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GlossCheck.Core;

namespace GlossCheck.Data
{
    /// <summary>1 lần xảy ra alarm: lúc báo, lúc RESET (null = chưa RESET).</summary>
    public sealed class AlarmRecord
    {
        public DateTime Time { get; set; }
        public string CodeText { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public bool IsEmergency { get; set; }
        public DateTime? ResetAt { get; set; }

        public TimeSpan? Duration { get { return ResetAt.HasValue ? ResetAt.Value - Time : (TimeSpan?)null; } }

        public AlarmRecord Clone() { return (AlarmRecord)MemberwiseClone(); }
    }

    /// <summary>
    /// Lịch sử alarm, lưu theo ngày: Logs\Alarms\yyyy-MM-dd.csv (mở được bằng Excel).
    /// Mỗi lần thay đổi (báo / RESET) ghi lại cả file của ngày trên thread nền (file nhỏ, ghi file tạm rồi thay thế
    /// nên mất điện giữa chừng không hỏng file). Ghi ngay lúc báo nên tắt phần mềm khi đang alarm vẫn còn bản ghi.
    /// </summary>
    public sealed class AlarmHistory
    {
        private const string Header = "Thời gian,Mã,Nội dung,Nguồn,Loại,Thời gian RESET,Kéo dài (s)";
        private static readonly object FileSync = new object();
        private readonly object _sync = new object();
        private readonly List<AlarmRecord> _today = new List<AlarmRecord>();
        private DateTime _day;
        private AlarmRecord _active;
        private int _version;

        public AlarmHistory()
        {
            _day = DateTime.Today;
            _today.AddRange(Load(_day));
        }

        public static string Folder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Alarms"); } }

        /// <summary>Tăng mỗi khi có thay đổi: UI so sánh để biết khi nào cần tải lại bảng.</summary>
        public int Version { get { return _version; } }

        /// <summary>Machine gọi khi báo alarm / dừng khẩn cấp.</summary>
        public void Add(MachineAlarm alarm, bool isEmergency)
        {
            lock (_sync)
            {
                RollDay(alarm.Time);
                CloseActive(alarm.Time); // phòng trường hợp alarm cũ chưa được đóng
                _active = new AlarmRecord
                {
                    Time = alarm.Time,
                    CodeText = alarm.CodeText,
                    Message = alarm.Message,
                    Source = alarm.Source ?? "",
                    IsEmergency = isEmergency,
                };
                _today.Add(_active);
                Changed();
            }
        }

        /// <summary>Machine gọi khi RESET xoá alarm.</summary>
        public void MarkReset()
        {
            lock (_sync)
            {
                if (_active == null) return;
                CloseActive(DateTime.Now);
                Changed();
            }
        }

        /// <summary>Bản sao lịch sử 1 ngày, mới nhất lên đầu.</summary>
        public List<AlarmRecord> GetDay(DateTime day)
        {
            lock (_sync)
            {
                if (day.Date == _day) return _today.Select(r => r.Clone()).Reverse().ToList();
            }
            var records = Load(day.Date);
            records.Reverse();
            return records;
        }

        private void CloseActive(DateTime time)
        {
            if (_active != null && !_active.ResetAt.HasValue) _active.ResetAt = time;
            _active = null;
        }

        private void RollDay(DateTime time)
        {
            if (time.Date == _day) return;
            // Alarm còn mở từ hôm trước: đóng ở cuối ngày đó để file hôm trước đầy đủ
            if (_active != null) { _active.ResetAt = _day.AddDays(1).AddTicks(-1); _active = null; SaveAsync(_day, _today.Select(r => r.Clone()).ToList()); }
            _day = time.Date;
            _today.Clear();
            _today.AddRange(Load(_day));
        }

        private void Changed()
        {
            Interlocked.Increment(ref _version);
            SaveAsync(_day, _today.Select(r => r.Clone()).ToList());
        }

        private static string PathOf(DateTime day) { return Path.Combine(Folder, day.ToString("yyyy-MM-dd") + ".csv"); }

        private static void SaveAsync(DateTime day, List<AlarmRecord> records)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (FileSync)
                {
                    try
                    {
                        Directory.CreateDirectory(Folder);
                        string path = PathOf(day);
                        string temp = path + ".tmp";
                        var text = new StringBuilder();
                        text.AppendLine(Header);
                        foreach (var r in records)
                        {
                            text.AppendLine(string.Join(",", new[]
                            {
                                r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                                r.CodeText,
                                Csv(r.Message),
                                Csv(r.Source),
                                r.IsEmergency ? "DỪNG KHẨN CẤP" : "ALARM",
                                r.ResetAt.HasValue ? r.ResetAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "",
                                r.Duration.HasValue ? ((int)r.Duration.Value.TotalSeconds).ToString() : "",
                            }));
                        }
                        File.WriteAllText(temp, text.ToString(), new UTF8Encoding(true)); // BOM: Excel đọc đúng tiếng Việt
                        if (File.Exists(path)) File.Replace(temp, path, null);
                        else File.Move(temp, path);
                    }
                    catch
                    {
                        // bỏ qua: lần thay đổi sau sẽ ghi lại cả file
                    }
                }
            });
        }

        private static List<AlarmRecord> Load(DateTime day)
        {
            var records = new List<AlarmRecord>();
            try
            {
                string path = PathOf(day);
                if (!File.Exists(path)) return records;
                string[] lines;
                lock (FileSync) lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (var line in lines.Skip(1))
                {
                    var f = SplitCsv(line);
                    if (f.Count < 6) continue;
                    DateTime time, reset;
                    if (!DateTime.TryParse(f[0], out time)) continue;
                    records.Add(new AlarmRecord
                    {
                        Time = time,
                        CodeText = f[1],
                        Message = f[2],
                        Source = f[3],
                        IsEmergency = f[4] != "ALARM",
                        ResetAt = DateTime.TryParse(f[5], out reset) ? reset : (DateTime?)null,
                    });
                }
            }
            catch
            {
                // file hỏng: bỏ qua, không chặn khởi động
            }
            return records;
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else current.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
