using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using GlossCheck.Hardware;

namespace GlossCheck.Settings
{
    [DataContract]
    public class XyPoint
    {
        [DataMember(Order = 1)] public double X { get; set; }
        [DataMember(Order = 2)] public double Y { get; set; }

        public XyPoint() { }
        public XyPoint(double x, double y) { X = x; Y = y; }
        public XyPoint Clone() { return new XyPoint(X, Y); }
        public override string ToString() { return string.Format("X={0:0.00}  Y={1:0.00}", X, Y); }
    }

    /// <summary>
    /// Các vị trí teach (mm) của cơ cấu con thoi: trục Y mang 2 jig, jig này vào dưới đầu đo thì jig kia ra ngoài.
    ///   Jig1 = Pos1: Jig 1 ở trong (đo Jig 1), Jig 2 ở ngoài (nạp hàng).
    ///   Jig2 = Pos2: Jig 2 ở trong, Jig 1 ở ngoài.
    /// Vị trí Ready (sau ORIGIN, khi kết thúc) = Pos1, không teach riêng.
    /// </summary>
    [DataContract]
    public class TeachPoints
    {
        [DataMember(Order = 1)] public XyPoint Jig1 { get; set; } = new XyPoint(150, 50);
        [DataMember(Order = 2)] public XyPoint Jig2 { get; set; } = new XyPoint(150, 250);

        /// <summary>Vị trí Ready = Pos1 (Jig 1 ở trong, Jig 2 ở ngoài sẵn sàng nạp hàng).</summary>
        public XyPoint Ready { get { return Jig1; } }

        public XyPoint ForJig(JigId jig) { return jig == JigId.Jig1 ? Jig1 : Jig2; }

        public TeachPoints Clone() { return new TeachPoints { Jig1 = Jig1.Clone(), Jig2 = Jig2.Clone() }; }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            // file cũ có thêm "Ready" thì bỏ qua; thiếu điểm nào thì dùng giá trị mặc định
            if (Jig1 == null) Jig1 = new XyPoint(150, 50);
            if (Jig2 == null) Jig2 = new XyPoint(150, 250);
        }
    }

    /// <summary>Spec của 1 góc đo. Giá trị nằm trong [Min, Max] (tính cả biên) là OK.</summary>
    [DataContract]
    public class AngleSpec
    {
        [DataMember(Order = 1)] public GlossAngle Angle { get; set; }
        [DataMember(Order = 2)] public bool IsEnabled { get; set; }
        [DataMember(Order = 3)] public double Min { get; set; }
        [DataMember(Order = 4)] public double Max { get; set; }

        public bool IsWithin(double value) { return value >= Min && value <= Max; }
        public AngleSpec Clone() { return new AngleSpec { Angle = Angle, IsEnabled = IsEnabled, Min = Min, Max = Max }; }
    }

    public enum JigId
    {
        Jig1 = 0,
        Jig2 = 1,
    }

    /// <summary>Recipe (UI gọi là "Model"): vị trí teach + spec theo sản phẩm. Lưu JSON trong thư mục Recipes.</summary>
    [DataContract]
    public class Recipe
    {
        public const int MaxMeasureCount = 10;

        [DataMember(Order = 1)] public string Name { get; set; } = "Default";
        [DataMember(Order = 2)] public TeachPoints Points { get; set; } = new TeachPoints();
        [DataMember(Order = 3)] public List<AngleSpec> Specs { get; set; } = CreateDefaultSpecs();
        /// <summary>Số lần đo liên tiếp ở mỗi jig, kết quả = trung bình.</summary>
        [DataMember(Order = 4)] public int MeasureCount { get; set; } = 1;

        public AngleSpec Spec(GlossAngle angle) { return Specs.First(s => s.Angle == angle); }

        /// <summary>Trả về danh sách lỗi (rỗng = hợp lệ).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Name)) errors.Add("Tên recipe không được rỗng.");
            if (MeasureCount < 1 || MeasureCount > MaxMeasureCount) errors.Add("Số lần đo phải từ 1 đến " + MaxMeasureCount + ".");
            foreach (var spec in Specs.Where(s => s.IsEnabled && s.Min >= s.Max))
                errors.Add("Góc " + GlossAngles.Label(spec.Angle) + ": Min phải nhỏ hơn Max.");
            if (!Specs.Any(s => s.IsEnabled)) errors.Add("Phải bật ít nhất 1 góc đo.");
            return errors;
        }

        public Recipe Clone()
        {
            return new Recipe
            {
                Name = Name,
                Points = Points.Clone(),
                Specs = Specs.Select(s => s.Clone()).ToList(),
                MeasureCount = MeasureCount,
            };
        }

        private static List<AngleSpec> CreateDefaultSpecs()
        {
            return new List<AngleSpec>
            {
                new AngleSpec { Angle = GlossAngle.Deg20, IsEnabled = true, Min = 40, Max = 50 },
                new AngleSpec { Angle = GlossAngle.Deg60, IsEnabled = true, Min = 80, Max = 90 },
                new AngleSpec { Angle = GlossAngle.Deg85, IsEnabled = false, Min = 90, Max = 100 },
            };
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            // file thiếu trường -> bổ sung giá trị mặc định
            if (Points == null) Points = new TeachPoints();
            if (Specs == null) Specs = CreateDefaultSpecs();
            foreach (var angle in GlossAngles.All)
                if (!Specs.Any(s => s.Angle == angle)) Specs.Add(new AngleSpec { Angle = angle });
            if (MeasureCount < 1) MeasureCount = 1;
        }
    }

    /// <summary>Đọc / ghi recipe dạng JSON (Recipes\&lt;tên&gt;.json).</summary>
    public static class RecipeStore
    {
        public static string Folder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recipes"); } }

        public static IList<string> ListNames()
        {
            if (!Directory.Exists(Folder)) return new List<string>();
            return Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        }

        public static Recipe Load(string name)
        {
            string path = PathOf(name);
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
                return (Recipe)new DataContractJsonSerializer(typeof(Recipe)).ReadObject(stream);
        }

        /// <summary>Đọc recipe, không có thì tạo recipe mặc định với tên đó.</summary>
        public static Recipe LoadOrCreate(string name)
        {
            var recipe = Load(name);
            if (recipe != null) return recipe;
            recipe = new Recipe { Name = name };
            Save(recipe);
            return recipe;
        }

        public static void Save(Recipe recipe)
        {
            Directory.CreateDirectory(Folder);
            using (var stream = File.Create(PathOf(recipe.Name)))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
            {
                new DataContractJsonSerializer(typeof(Recipe)).WriteObject(writer, recipe);
                writer.Flush();
            }
        }

        public static void Delete(string name)
        {
            string path = PathOf(name);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string PathOf(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(Folder, name + ".json");
        }
    }
}
