using System;
using System.Collections.Generic;

namespace GlossCheck.Core
{
    /// <summary>
    /// Mã alarm. Số hiển thị trên UI/log dạng "E030" để tra cứu nhanh.
    /// Nhóm: 0xx an toàn, 1xx trục, 2xx interlock, 3xx xi lanh, 4xx máy đo, 9xx phần mềm.
    /// </summary>
    public enum AlarmCode
    {
        IoCommLost = 2, // (E001 trước đây là nút EMG - máy không có nút EMG)

        AxisCommLost = 100,
        AxisServoAlarm = 101,
        AxisNotHomed = 102,
        AxisSoftLimit = 103,
        AxisMoveTimeout = 104,
        AxisStoppedOffTarget = 105,
        AxisServoOnTimeout = 106,
        HomingTimeout = 110,
        HomingFailed = 111,

        InterlockLifterNotDown = 200,

        Jig1LifterUpTimeout = 300,
        Jig1LifterDownTimeout = 301,
        Jig2LifterUpTimeout = 302,
        Jig2LifterDownTimeout = 303,
        Jig1VacuumLost = 310,
        Jig2VacuumLost = 311,

        MeterNotConnected = 400,
        MeterTimeout = 401,
        MeterMeasureFailed = 402,
        MeterBusy = 403,

        SoftwareError = 900,
    }

    /// <summary>Một alarm cụ thể: mã + chi tiết. NeedsOrigin = sau Reset phải ORIGIN lại.</summary>
    public sealed class MachineAlarm
    {
        private static readonly Dictionary<AlarmCode, string> Texts = new Dictionary<AlarmCode, string>
        {
            { AlarmCode.IoCommLost, "Mất kết nối module IO (không đọc được nút nhấn / cảm biến)" },
            { AlarmCode.AxisCommLost, "Mất kết nối driver trục" },
            { AlarmCode.AxisServoAlarm, "Driver trục báo alarm" },
            { AlarmCode.AxisNotHomed, "Trục chưa origin" },
            { AlarmCode.AxisSoftLimit, "Vị trí đích vượt giới hạn mềm" },
            { AlarmCode.AxisMoveTimeout, "Trục chạy quá thời gian" },
            { AlarmCode.AxisStoppedOffTarget, "Trục dừng sai vị trí" },
            { AlarmCode.AxisServoOnTimeout, "Không bật được servo" },
            { AlarmCode.HomingTimeout, "Origin quá thời gian" },
            { AlarmCode.HomingFailed, "Origin thất bại" },
            { AlarmCode.InterlockLifterNotDown, "INTERLOCK: xi lanh jig chưa xuống, không cho chạy XY" },
            { AlarmCode.Jig1LifterUpTimeout, "Xi lanh Jig 1: không thấy cảm biến LÊN (kẹt / mất khí?)" },
            { AlarmCode.Jig1LifterDownTimeout, "Xi lanh Jig 1: không thấy cảm biến XUỐNG (kẹt / mất khí?)" },
            { AlarmCode.Jig2LifterUpTimeout, "Xi lanh Jig 2: không thấy cảm biến LÊN (kẹt / mất khí?)" },
            { AlarmCode.Jig2LifterDownTimeout, "Xi lanh Jig 2: không thấy cảm biến XUỐNG (kẹt / mất khí?)" },
            { AlarmCode.Jig1VacuumLost, "Jig 1: mất chân không khi đang giữ hàng (hàng lệch / rơi / mất khí?)" },
            { AlarmCode.Jig2VacuumLost, "Jig 2: mất chân không khi đang giữ hàng (hàng lệch / rơi / mất khí?)" },
            { AlarmCode.MeterNotConnected, "Máy đo chưa kết nối" },
            { AlarmCode.MeterTimeout, "Máy đo không phản hồi (quá thời gian)" },
            { AlarmCode.MeterMeasureFailed, "Máy đo trả lỗi khi đo" },
            { AlarmCode.MeterBusy, "Máy đo đang bận lệnh trước" },
            { AlarmCode.SoftwareError, "Lỗi phần mềm" },
        };

        public MachineAlarm(AlarmCode code, string detail = null)
        {
            Code = code;
            Detail = detail;
            Time = DateTime.Now;
        }

        public AlarmCode Code { get; private set; }
        public string Detail { get; private set; }
        public DateTime Time { get; private set; }

        /// <summary>Lỗi liên quan tới trục / origin: vị trí không còn tin cậy => bắt buộc origin lại.</summary>
        public bool NeedsOrigin
        {
            get
            {
                switch (Code)
                {
                    case AlarmCode.AxisCommLost:
                    case AlarmCode.AxisServoAlarm:
                    case AlarmCode.AxisNotHomed:
                    case AlarmCode.HomingTimeout:
                    case AlarmCode.HomingFailed:
                    case AlarmCode.AxisServoOnTimeout:
                    case AlarmCode.SoftwareError:
                        return true;
                    default:
                        return false;
                }
            }
        }

        public string CodeText { get { return "E" + ((int)Code).ToString("000"); } }

        public string Message
        {
            get
            {
                string text;
                if (!Texts.TryGetValue(Code, out text)) text = Code.ToString();
                return string.IsNullOrEmpty(Detail) ? text : text + " - " + Detail;
            }
        }

        public override string ToString() { return CodeText + "  " + Message; }
    }
}
