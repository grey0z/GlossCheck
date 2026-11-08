using System;
using System.ComponentModel;
using System.IO;
using System.Xml.Serialization;

namespace GlossCheck.Settings
{
    public enum RunMode
    {
        Simulation,
        Real,
    }

    /// <summary>
    /// Cấu hình máy (lưu MachineConfig.xml cạnh file exe). Hiển thị trực tiếp trên PropertyGrid ở tab Cấu hình,
    /// nên mỗi property đều có Category / DisplayName / Description tiếng Việt.
    /// Thông số theo sản phẩm (vị trí teach, spec) nằm trong Recipe, không nằm ở đây.
    /// </summary>
    public class MachineConfig
    {
        public const string FileName = "MachineConfig.xml";

        [Category("1. Chung"), DisplayName("Chế độ chạy"), Description("Simulation: mô phỏng, không cần phần cứng. Real: máy thật. Khởi động lại để áp dụng.")]
        public RunMode Mode { get; set; } = RunMode.Simulation;

        [Category("1. Chung"), DisplayName("Chu kỳ quét (ms)")]
        public int ScanPeriodMs { get; set; } = 10;

        [Category("1. Chung"), DisplayName("Thư mục lưu kết quả"), Description("Rỗng = thư mục Results cạnh file exe. Mỗi ngày 1 file CSV.")]
        public string ResultFolder { get; set; } = "";

        [Browsable(false)] // đổi ở tab Model / Spec
        public string ActiveRecipe { get; set; } = "Default";

        [Category("2. Trục"), DisplayName("Trục X")]
        public AxisSettings AxisX { get; set; } = new AxisSettings { IpAddress = "192.168.0.20", MaxPosition = 400 };

        [Category("2. Trục"), DisplayName("Trục Y")]
        public AxisSettings AxisY { get; set; } = new AxisSettings { IpAddress = "192.168.0.21", MaxPosition = 300 };

        [Category("2. Trục"), DisplayName("Chuyển động XY")]
        public MotionSettings Motion { get; set; } = new MotionSettings();

        [Category("3. IO"), DisplayName("Module IO")]
        public IoSettings Io { get; set; } = new IoSettings();

        [Category("3. IO"), DisplayName("Hút chân không")]
        public VacuumSettings Vacuum { get; set; } = new VacuumSettings();

        [Category("3. IO"), DisplayName("Còi (buzzer)")]
        public BuzzerSettings Buzzer { get; set; } = new BuzzerSettings();

        [Category("4. Máy đo"), DisplayName("Máy đo độ bóng")]
        public MeterSettings Meter { get; set; } = new MeterSettings();

        [Category("5. Timeout"), DisplayName("Timeout")]
        public TimeoutSettings Timeouts { get; set; } = new TimeoutSettings();

        public static string DefaultPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); } }

        public static MachineConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                var config = new MachineConfig();
                config.Save(path);
                return config;
            }
            using (var stream = File.OpenRead(path))
                return (MachineConfig)new XmlSerializer(typeof(MachineConfig)).Deserialize(stream);
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path))
                new XmlSerializer(typeof(MachineConfig)).Serialize(stream, this);
        }

        /// <summary>
        /// Chỉ cập nhật tên recipe đang dùng trong file (không ghi đè các thay đổi cấu hình
        /// đã lưu nhưng chưa khởi động lại để áp dụng).
        /// </summary>
        public static void SaveActiveRecipe(string path, string recipeName)
        {
            var config = Load(path);
            if (config.ActiveRecipe == recipeName) return;
            config.ActiveRecipe = recipeName;
            config.Save(path);
        }

        public MachineConfig Clone()
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new XmlSerializer(typeof(MachineConfig));
                serializer.Serialize(stream, this);
                stream.Position = 0;
                return (MachineConfig)serializer.Deserialize(stream);
            }
        }

        public string ResolveResultFolder()
        {
            return string.IsNullOrWhiteSpace(ResultFolder)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results")
                : ResultFolder;
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class AxisSettings
    {
        [DisplayName("IP driver"), Description("BoardId = số cuối của IP.")]
        public string IpAddress { get; set; } = "192.168.0.20";

        [DisplayName("Pulse / mm"), Description("10000 pulse/vòng, vít me 10 mm => 1000.")]
        public double PulsePerMm { get; set; } = 1000;

        [DisplayName("Giới hạn mềm min (mm)")]
        public double MinPosition { get; set; } = 0;

        [DisplayName("Giới hạn mềm max (mm)")]
        public double MaxPosition { get; set; } = 400;

        [DisplayName("Tốc độ chạy (mm/s)")]
        public double MoveSpeed { get; set; } = 150;

        [DisplayName("Tốc độ jog 100% (mm/s)"), Description("Tốc độ jog lớn nhất. Màn hình jog chọn 1 / 5 / 10 / 20 / 50 / 100 % của giá trị này.")]
        public double JogMaxSpeed { get; set; } = 50;

        /// <summary>Tốc độ jog (mm/s) theo phần trăm của JogMaxSpeed.</summary>
        public double JogSpeedAt(int percent) { return JogMaxSpeed * Math.Max(1, Math.Min(100, percent)) / 100.0; }

        public override string ToString() { return IpAddress; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class MotionSettings
    {
        [DisplayName("Dùng nội suy XY"), Description("true: X/Y đi đường thẳng, tới đích cùng lúc (FAS_MoveLinearAbsPos).")]
        public bool UseLinearInterpolation { get; set; } = true;

        [DisplayName("Tốc độ nội suy (mm/s)")]
        public double LinearSpeed { get; set; } = 150;

        [DisplayName("Thời gian tăng tốc (ms)")]
        public int AccelTimeMs { get; set; } = 200;

        [DisplayName("Dung sai tới vị trí (mm)")]
        public double InPositionTolerance { get; set; } = 0.05;

        public override string ToString() { return UseLinearInterpolation ? "Nội suy " + LinearSpeed + " mm/s" : "Từng trục"; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class IoSettings
    {
        [DisplayName("IP module input"), Description("Input và output cùng 1 module thì nhập cùng IP.")]
        public string InputModuleIp { get; set; } = "192.168.0.2";

        [DisplayName("IP module output")]
        public string OutputModuleIp { get; set; } = "192.168.0.3";

        [DisplayName("Mặt nạ đảo input"), Description("Bit = 1 thì đảo logic input đó (cảm biến / nút đi dây NC). Mặc định 0 = không đảo.")]
        public uint InputInvertMask { get; set; } = 0;

        public override string ToString() { return InputModuleIp + " / " + OutputModuleIp; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class VacuumSettings
    {
        [DisplayName("Dùng công tắc chân không"), Description("true: kiểm tra input 'đã hút' (IN8 / IN9). false: chỉ bật van rồi chờ 'Thời gian chờ hút'.")]
        public bool UseSensor { get; set; } = true;

        [DisplayName("Thời gian chờ hút (ms)"), Description("Có công tắc: quá thời gian này mà chưa báo hút được => coi như không có hàng, huỷ lần đo. Không có công tắc: thời gian chờ sau khi bật van.")]
        public int HoldTimeoutMs { get; set; } = 1500;

        public override string ToString() { return UseSensor ? "Có công tắc, " + HoldTimeoutMs + " ms" : "Không công tắc, chờ " + HoldTimeoutMs + " ms"; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class BuzzerSettings
    {
        [DisplayName("Kêu khi kết quả NG")]
        public bool BeepOnNg { get; set; } = true;

        [DisplayName("Kêu khi đo xong OK"), Description("Tiếng bíp ngắn báo đã đo xong, lấy hàng được.")]
        public bool BeepOnOk { get; set; } = false;

        public override string ToString() { return "NG: " + (BeepOnNg ? "kêu" : "tắt") + ", OK: " + (BeepOnOk ? "kêu" : "tắt"); }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class MeterSettings
    {
        [DisplayName("Cổng COM"), Description("Rỗng = tự dò cổng.")]
        public string PortName { get; set; } = "";

        public override string ToString() { return string.IsNullOrEmpty(PortName) ? "Tự dò" : PortName; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class TimeoutSettings
    {
        [DisplayName("Chạy trục (ms)")]
        public int AxisMoveMs { get; set; } = 10000;

        [DisplayName("Origin (ms)")]
        public int HomingMs { get; set; } = 60000;

        [DisplayName("Xi lanh (ms)")]
        public int CylinderMs { get; set; } = 3000;

        [DisplayName("Máy đo (ms)"), Description("Thời gian tối đa cho 1 lần đo.")]
        public int MeasureMs { get; set; } = 5000;

        public override string ToString() { return "Trục " + AxisMoveMs + " / XL " + CylinderMs + " / Đo " + MeasureMs; }
    }
}
