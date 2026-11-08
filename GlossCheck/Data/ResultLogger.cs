using System;
using System.Globalization;
using System.IO;
using System.Text;
using GlossCheck.Hardware;
using GlossCheck.Settings;

namespace GlossCheck.Data
{
    /// <summary>Kết quả phán định 1 lần đo.</summary>
    public sealed class JudgeResult
    {
        private readonly bool?[] _angleOk = new bool?[3];

        /// <summary>null = góc này đang tắt (không xét).</summary>
        public bool? AngleOk(GlossAngle angle) { return _angleOk[(int)angle]; }
        public bool IsOk { get; private set; }

        /// <summary>OK khi mọi góc đang bật đều nằm trong [Min, Max]. Không có giá trị đo => NG.</summary>
        public static JudgeResult Evaluate(Recipe recipe, GlossReading reading)
        {
            var result = new JudgeResult { IsOk = reading != null };
            foreach (var angle in GlossAngles.All)
            {
                var spec = recipe.Spec(angle);
                if (!spec.IsEnabled) continue;
                bool ok = reading != null && spec.IsWithin(reading[angle]);
                result._angleOk[(int)angle] = ok;
                if (!ok) result.IsOk = false;
            }
            return result;
        }
    }

    public sealed class MeasurementRecord
    {
        public DateTime Time;
        public string RecipeName;
        public JigId Jig;
        public GlossReading Reading;
        public JudgeResult Judge;
    }

    /// <summary>Ghi mỗi lần đo 1 dòng vào file CSV theo ngày (yyyy-MM-dd.csv), mở được bằng Excel.</summary>
    public class ResultLogger
    {
        private const string Header = "Time,Recipe,Jig,Deg20,Deg60,Deg85,Judge20,Judge60,Judge85,Result";
        private readonly string _folder;
        private readonly object _sync = new object();

        public ResultLogger(string folder) { _folder = folder; }

        public string LastError { get; private set; }

        public void Append(MeasurementRecord record)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_folder);
                    string path = Path.Combine(_folder, record.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
                    bool isNew = !File.Exists(path);
                    using (var writer = new StreamWriter(path, true, new UTF8Encoding(true)))
                    {
                        if (isNew) writer.WriteLine(Header);
                        writer.WriteLine(Format(record));
                    }
                }
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message; // không để lỗi ghi file làm dừng máy
            }
        }

        private static string Format(MeasurementRecord r)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(r.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", c)).Append(',');
            sb.Append(Escape(r.RecipeName)).Append(',');
            sb.Append(r.Jig).Append(',');
            foreach (var angle in GlossAngles.All)
                sb.Append(r.Reading != null ? r.Reading[angle].ToString("0.00", c) : "").Append(',');
            foreach (var angle in GlossAngles.All)
            {
                bool? ok = r.Judge.AngleOk(angle);
                sb.Append(ok == null ? "-" : (ok.Value ? "OK" : "NG")).Append(',');
            }
            sb.Append(r.Judge.IsOk ? "OK" : "NG");
            return sb.ToString();
        }

        private static string Escape(string text)
        {
            if (text == null) return "";
            return text.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }
    }
}
