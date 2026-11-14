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

    /// <summary>Thông tin tra cứu của 1 mã alarm: nội dung, nguyên nhân thường gặp, cách xử lý (hiển thị trên popup / tab Alarm).</summary>
    public sealed class AlarmInfo
    {
        public AlarmInfo(string text, string cause, string remedy)
        {
            Text = text;
            Cause = cause;
            Remedy = remedy;
        }

        public string Text { get; private set; }
        public string Cause { get; private set; }
        public string Remedy { get; private set; }
    }

    /// <summary>Một alarm cụ thể: mã + chi tiết. NeedsOrigin = sau Reset phải ORIGIN lại.</summary>
    public sealed class MachineAlarm
    {
        private const string CheckLifter = "- Kiểm tra áp khí cấp vào máy\r\n- Kiểm tra xi lanh có bị kẹt / vướng vật lạ\r\n- Kiểm tra cảm biến hành trình (đèn trên cảm biến, dây, vị trí gá)\r\n- Thử nâng / hạ tay ở tab Manual, sau đó RESET";
        private const string CheckVacuum = "- Kiểm tra hàng đặt đúng vị trí, không cong vênh\r\n- Kiểm tra áp khí, ống hút, giác hút có bị rách / bẩn\r\n- Kiểm tra công tắc chân không (ngưỡng cài đặt)\r\n- Đặt lại hàng, RESET rồi START để đo lại";

        private static readonly Dictionary<AlarmCode, AlarmInfo> Infos = new Dictionary<AlarmCode, AlarmInfo>
        {
            { AlarmCode.IoCommLost, new AlarmInfo("Mất kết nối module IO (không đọc được nút nhấn / cảm biến)",
                "Cáp LAN tới module Ezi-IO bị lỏng / đứt, module mất nguồn 24 V, hoặc sai địa chỉ IP.",
                "- Kiểm tra nguồn và đèn trên module IO\r\n- Kiểm tra cáp LAN / switch\r\n- Bấm \"Kết nối lại\" ở thanh trạng thái dưới cùng\r\n- Khi IO đã xanh: RESET rồi ORIGIN") },
            { AlarmCode.AxisCommLost, new AlarmInfo("Mất kết nối driver trục",
                "Cáp LAN tới driver Ezi-SERVO lỏng / đứt, driver mất nguồn hoặc sai IP.",
                "- Kiểm tra nguồn và đèn trên driver\r\n- Kiểm tra cáp LAN / switch\r\n- Bấm \"Kết nối lại\", sau đó RESET và ORIGIN") },
            { AlarmCode.AxisServoAlarm, new AlarmInfo("Driver trục báo alarm",
                "Quá tải, va chạm cơ khí, chạm công tắc hành trình, lỗi encoder hoặc quá dòng.",
                "- Xem mã lỗi nháy trên driver (tra tài liệu Ezi-SERVO)\r\n- Kiểm tra trục có bị vướng / va chạm\r\n- RESET (xoá alarm driver) rồi ORIGIN lại") },
            { AlarmCode.AxisNotHomed, new AlarmInfo("Trục chưa origin",
                "Chạy tới vị trí khi trục chưa ORIGIN (vừa bật máy / vừa dừng khẩn cấp).",
                "- RESET rồi bấm ORIGIN") },
            { AlarmCode.AxisSoftLimit, new AlarmInfo("Vị trí đích vượt giới hạn mềm",
                "Vị trí teach nằm ngoài khoảng Min/Max của trục trong Config.",
                "- Kiểm tra lại vị trí ở tab Teaching\r\n- Hoặc chỉnh giới hạn Min/Max trong tab Config\r\n- RESET") },
            { AlarmCode.AxisMoveTimeout, new AlarmInfo("Trục chạy quá thời gian",
                "Trục bị vướng, tốc độ cài đặt quá thấp, hoặc timeout chạy trục trong Config quá ngắn.",
                "- Kiểm tra cơ khí trục có bị kẹt\r\n- Kiểm tra tốc độ / timeout trong tab Config\r\n- RESET rồi START để chạy lại") },
            { AlarmCode.AxisStoppedOffTarget, new AlarmInfo("Trục dừng sai vị trí",
                "Trục bị chặn giữa đường hoặc bị dừng trước khi tới đích.",
                "- Kiểm tra cơ khí trục\r\n- RESET rồi START để chạy lại") },
            { AlarmCode.AxisServoOnTimeout, new AlarmInfo("Không bật được servo",
                "Driver đang alarm, mất nguồn động lực hoặc mất kết nối.",
                "- Kiểm tra nguồn động lực và đèn driver\r\n- RESET rồi ORIGIN") },
            { AlarmCode.HomingTimeout, new AlarmInfo("Origin quá thời gian",
                "Cảm biến origin không tác động (hỏng / lệch / đứt dây) hoặc trục bị kẹt khi về gốc.",
                "- Kiểm tra cảm biến origin và dây\r\n- Kiểm tra trục chạy trơn bằng tay (tắt servo)\r\n- RESET rồi ORIGIN") },
            { AlarmCode.HomingFailed, new AlarmInfo("Origin thất bại",
                "Có lỗi xảy ra trong lúc ORIGIN (xem chi tiết đi kèm).",
                "- Xử lý lỗi trong phần chi tiết\r\n- RESET rồi ORIGIN") },
            { AlarmCode.InterlockLifterNotDown, new AlarmInfo("INTERLOCK: xi lanh jig chưa xuống, không cho chạy XY",
                "Một trong 2 xi lanh nâng jig chưa ở vị trí XUỐNG nên không được chạy trục (tránh va chạm đầu đo).",
                "- Hạ xi lanh ở tab Manual\r\n- Kiểm tra cảm biến XUỐNG của xi lanh\r\n- RESET") },
            { AlarmCode.Jig1LifterUpTimeout, new AlarmInfo("Xi lanh Jig 1: không thấy cảm biến LÊN (kẹt / mất khí?)",
                "Xi lanh Jig 1 không lên tới cảm biến trong thời gian cho phép.", CheckLifter) },
            { AlarmCode.Jig1LifterDownTimeout, new AlarmInfo("Xi lanh Jig 1: không thấy cảm biến XUỐNG (kẹt / mất khí?)",
                "Xi lanh Jig 1 không xuống tới cảm biến trong thời gian cho phép.", CheckLifter) },
            { AlarmCode.Jig2LifterUpTimeout, new AlarmInfo("Xi lanh Jig 2: không thấy cảm biến LÊN (kẹt / mất khí?)",
                "Xi lanh Jig 2 không lên tới cảm biến trong thời gian cho phép.", CheckLifter) },
            { AlarmCode.Jig2LifterDownTimeout, new AlarmInfo("Xi lanh Jig 2: không thấy cảm biến XUỐNG (kẹt / mất khí?)",
                "Xi lanh Jig 2 không xuống tới cảm biến trong thời gian cho phép.", CheckLifter) },
            { AlarmCode.Jig1VacuumLost, new AlarmInfo("Jig 1: mất chân không khi đang giữ hàng (hàng lệch / rơi / mất khí?)",
                "Công tắc chân không Jig 1 tắt trong lúc đang đo: hàng bị xê dịch, rơi, hoặc mất khí.", CheckVacuum) },
            { AlarmCode.Jig2VacuumLost, new AlarmInfo("Jig 2: mất chân không khi đang giữ hàng (hàng lệch / rơi / mất khí?)",
                "Công tắc chân không Jig 2 tắt trong lúc đang đo: hàng bị xê dịch, rơi, hoặc mất khí.", CheckVacuum) },
            { AlarmCode.MeterNotConnected, new AlarmInfo("Máy đo chưa kết nối",
                "Máy đo gloss NHG chưa bật, cáp USB/COM lỏng hoặc sai cổng COM trong Config.",
                "- Bật máy đo, kiểm tra cáp\r\n- Kiểm tra cổng COM trong tab Config\r\n- Bấm \"Kết nối lại\" ở phần máy đo trong tab Manual\r\n- RESET rồi START để đo lại") },
            { AlarmCode.MeterTimeout, new AlarmInfo("Máy đo không phản hồi (quá thời gian)",
                "Máy đo bị treo / tự tắt nguồn (hết pin, chế độ ngủ) hoặc mất kết nối giữa chừng.",
                "- Kiểm tra máy đo còn bật, màn hình còn sáng\r\n- Tắt / bật lại máy đo nếu treo, kết nối lại ở tab Manual\r\n- RESET rồi START để đo lại") },
            { AlarmCode.MeterMeasureFailed, new AlarmInfo("Máy đo trả lỗi khi đo",
                "Máy đo báo lỗi (chưa hiệu chuẩn, đầu đo bẩn, mặt hàng không phẳng...).",
                "- Lau đầu đo, kiểm tra hàng đặt phẳng\r\n- Hiệu chuẩn lại máy đo nếu cần (tab Manual)\r\n- RESET rồi START để đo lại") },
            { AlarmCode.MeterBusy, new AlarmInfo("Máy đo đang bận lệnh trước",
                "Lệnh đo / hiệu chuẩn trước đó chưa xong (thường do thao tác tay ở tab Manual).",
                "- Chờ lệnh trước xong\r\n- RESET rồi START") },
            { AlarmCode.SoftwareError, new AlarmInfo("Lỗi phần mềm",
                "Lỗi ngoài dự kiến trong chương trình (chi tiết đã ghi vào file log).",
                "- RESET rồi ORIGIN\r\n- Nếu lặp lại: gửi file trong thư mục Logs cho người phụ trách phần mềm") },
        };

        private static readonly AlarmInfo UnknownInfo = new AlarmInfo(null, "", "- RESET");

        public MachineAlarm(AlarmCode code, string detail = null)
        {
            Code = code;
            Detail = detail;
            Time = DateTime.Now;
        }

        public AlarmCode Code { get; private set; }
        public string Detail { get; private set; }
        /// <summary>Thời điểm báo. Machine đặt lại lúc báo (alarm timeout của bước được tạo sẵn từ lúc bắt đầu bước).</summary>
        public DateTime Time { get; internal set; }
        /// <summary>Nơi phát sinh (luồng / bước đang chạy), Machine gán khi báo alarm.</summary>
        public string Source { get; internal set; }

        public static AlarmInfo InfoOf(AlarmCode code)
        {
            AlarmInfo info;
            return Infos.TryGetValue(code, out info) ? info : UnknownInfo;
        }

        /// <summary>Tất cả mã alarm (tab Alarm hiển thị bảng tra cứu).</summary>
        public static IEnumerable<KeyValuePair<AlarmCode, AlarmInfo>> AllInfos { get { return Infos; } }

        public static string FormatCode(AlarmCode code) { return "E" + ((int)code).ToString("000"); }

        public string Title { get { return InfoOf(Code).Text ?? Code.ToString(); } }
        public string Cause { get { return InfoOf(Code).Cause; } }
        public string Remedy { get { return InfoOf(Code).Remedy; } }

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

        public string CodeText { get { return FormatCode(Code); } }

        public string Message
        {
            get
            {
                string text = Title;
                return string.IsNullOrEmpty(Detail) ? text : text + " - " + Detail;
            }
        }

        public override string ToString() { return CodeText + "  " + Message; }
    }
}
