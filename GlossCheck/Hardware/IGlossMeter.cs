using System;
using System.Collections.Generic;
using System.Linq;

namespace GlossCheck.Hardware
{
    /// <summary>Góc đo của máy đo độ bóng NHG (giá trị = chỉ số trong mảng kết quả).</summary>
    public enum GlossAngle
    {
        Deg20 = 0,
        Deg60 = 1,
        Deg85 = 2,
    }

    public static class GlossAngles
    {
        public static readonly GlossAngle[] All = { GlossAngle.Deg20, GlossAngle.Deg60, GlossAngle.Deg85 };
        public static string Label(GlossAngle angle)
        {
            switch (angle)
            {
                case GlossAngle.Deg20: return "20°";
                case GlossAngle.Deg60: return "60°";
                default: return "85°";
            }
        }
    }

    /// <summary>Một kết quả đo: độ bóng (GU) ở 3 góc.</summary>
    public sealed class GlossReading
    {
        private readonly double[] values = new double[3];
        public DateTime Time { get; private set; }

        public GlossReading(double deg20, double deg60, double deg85)
        {
            values[0] = deg20; values[1] = deg60; values[2] = deg85;
            Time = DateTime.Now;
        }

        public double this[GlossAngle angle] { get { return values[(int)angle]; } }

        public static GlossReading Average(IList<GlossReading> readings)
        {
            if (readings == null || readings.Count == 0) return null;
            return new GlossReading(
                readings.Average(r => r[GlossAngle.Deg20]),
                readings.Average(r => r[GlossAngle.Deg60]),
                readings.Average(r => r[GlossAngle.Deg85]));
        }

        public override string ToString()
        {
            return string.Format("20°={0:0.0}  60°={1:0.0}  85°={2:0.0}", values[0], values[1], values[2]);
        }
    }

    public sealed class GlossMeterInfo
    {
        public string DeviceName;
        public string SerialNumber;
        public string PortName;
        public bool IsCalibrated;
    }

    /// <summary>
    /// Máy đo độ bóng. Các hàm đều CHẶN (gọi DLL đồng bộ) nên chỉ được gọi từ
    /// GlossMeterService (thread riêng), không gọi từ thread quét hay thread UI.
    /// </summary>
    public interface IGlossMeter
    {
        bool IsOpen { get; }
        /// <param name="portName">Rỗng = tự dò cổng COM.</param>
        bool Open(string portName);
        void Close();
        /// <returns>null nếu đo lỗi.</returns>
        GlossReading Measure();
        bool Calibrate();
        /// <returns>null nếu đọc lỗi.</returns>
        GlossMeterInfo GetInfo();
    }
}
