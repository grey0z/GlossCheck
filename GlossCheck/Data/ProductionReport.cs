using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GlossCheck.Settings;

namespace GlossCheck.Data
{
    /// <summary>Số lượng OK / NG theo từng jig trong 1 khoảng (1 giờ, 1 ngày...).</summary>
    public sealed class ProductionCounts
    {
        public int Jig1Ok, Jig1Ng, Jig2Ok, Jig2Ng;

        public int Ok { get { return Jig1Ok + Jig2Ok; } }
        public int Ng { get { return Jig1Ng + Jig2Ng; } }
        public int Total { get { return Ok + Ng; } }
        /// <summary>Tỉ lệ đạt (%), null khi chưa có sản phẩm.</summary>
        public double? YieldPercent { get { return Total > 0 ? 100.0 * Ok / Total : (double?)null; } }

        public void Add(JigId jig, bool isOk)
        {
            if (jig == JigId.Jig1) { if (isOk) Jig1Ok++; else Jig1Ng++; }
            else { if (isOk) Jig2Ok++; else Jig2Ng++; }
        }

        public void Add(ProductionCounts other)
        {
            Jig1Ok += other.Jig1Ok; Jig1Ng += other.Jig1Ng;
            Jig2Ok += other.Jig2Ok; Jig2Ng += other.Jig2Ng;
        }
    }

    /// <summary>Sản lượng 1 ngày: theo từng giờ (0..23) + tổng ngày.</summary>
    public sealed class DailyProduction
    {
        public DailyProduction(DateTime date)
        {
            Date = date.Date;
            for (int h = 0; h < 24; h++) Hours[h] = new ProductionCounts();
        }

        public DateTime Date { get; private set; }
        public readonly ProductionCounts[] Hours = new ProductionCounts[24];
        public readonly ProductionCounts Total = new ProductionCounts();
        /// <summary>Các model có trong file CSV của ngày (để lọc).</summary>
        public readonly SortedSet<string> Recipes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Thống kê sản lượng đọc thẳng từ file CSV kết quả (Results\yyyy-MM-dd.csv) – nguồn dữ liệu duy nhất,
    /// nên số liệu luôn khớp với file và không mất khi tắt phần mềm.
    /// </summary>
    public static class ProductionReport
    {
        private const int ColumnTime = 0, ColumnRecipe = 1, ColumnJig = 2, ColumnResult = 9;

        /// <param name="recipeFilter">null / rỗng = tất cả model.</param>
        public static DailyProduction LoadDay(string folder, DateTime date, string recipeFilter)
        {
            var day = new DailyProduction(date);
            string path = Path.Combine(folder, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
            if (!File.Exists(path)) return day;

            foreach (var line in ReadLinesShared(path).Skip(1)) // bỏ dòng tiêu đề
            {
                var columns = SplitCsv(line);
                if (columns.Count <= ColumnResult) continue;

                DateTime time;
                if (!DateTime.TryParseExact(columns[ColumnTime], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;
                JigId jig;
                if (!Enum.TryParse(columns[ColumnJig], out jig)) continue;

                string recipe = columns[ColumnRecipe];
                day.Recipes.Add(recipe);
                if (!string.IsNullOrEmpty(recipeFilter) && !string.Equals(recipe, recipeFilter, StringComparison.OrdinalIgnoreCase)) continue;

                bool isOk = columns[ColumnResult].Trim() == "OK";
                day.Hours[time.Hour].Add(jig, isOk);
                day.Total.Add(jig, isOk);
            }
            return day;
        }

        /// <summary>Tổng từng ngày trong khoảng [from, to], ngày mới nhất trước.</summary>
        public static List<DailyProduction> LoadDays(string folder, DateTime from, DateTime to, string recipeFilter)
        {
            var days = new List<DailyProduction>();
            for (var date = to.Date; date >= from.Date; date = date.AddDays(-1))
                days.Add(LoadDay(folder, date, recipeFilter));
            return days;
        }

        /// <summary>Đọc cả khi ResultLogger đang ghi file (FileShare.ReadWrite).</summary>
        private static IEnumerable<string> ReadLinesShared(string path)
        {
            var lines = new List<string>();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                }
            }
            catch (IOException)
            {
                // file đang bị khoá lâu: trả về những gì đọc được, lần làm mới sau sẽ đọc lại
            }
            return lines;
        }

        /// <summary>Tách 1 dòng CSV (hỗ trợ ô có dấu phẩy nằm trong ngoặc kép, như ResultLogger ghi).</summary>
        private static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') inQuotes = false;
                    else cell.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }
    }
}
