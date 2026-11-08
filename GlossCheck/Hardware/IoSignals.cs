using System.Collections.Generic;

namespace GlossCheck.Hardware
{
    /// <summary>
    /// Input số trên module Ezi-IO Plus-E (giá trị enum = số bit, IN1 trên module = bit 0).
    /// Giá trị đọc được là giá trị LOGIC (đã đảo theo MachineConfig.Io.InputInvertMask, dùng cho input đi dây NC).
    /// Bảng điều khiển chỉ có START và STOP (không có RESET, không có EMG; STOP kiêm RESET khi alarm).
    /// IN2 (RESET cũ) và IN3 (EMG cũ) nay để trống.
    /// </summary>
    public enum InputSignal
    {
        StartButton = 0,
        StopButton = 1,
        Jig1LifterDown = 4,
        Jig1LifterUp = 5,
        Jig2LifterDown = 6,
        Jig2LifterUp = 7,
        /// <summary>Công tắc chân không Jig 1: ON = đã hút được hàng.</summary>
        Jig1VacuumOk = 8,
        Jig2VacuumOk = 9,
    }

    /// <summary>
    /// Output số trên module Ezi-IO Plus-E (giá trị enum = số bit, OUT1 = bit 0).
    /// OUT4 trước đây là đèn nút RESET, nay để trống.
    /// </summary>
    public enum OutputSignal
    {
        TowerGreen = 0,
        TowerYellow = 1,
        TowerRed = 2,
        StartLamp = 3,
        StopLamp = 5,
        Buzzer = 6,
        Jig1LifterUpValve = 7,
        Jig1LifterDownValve = 8,
        Jig2LifterUpValve = 9,
        Jig2LifterDownValve = 10,
        /// <summary>Van hút chân không Jig 1: ON = hút giữ hàng.</summary>
        Jig1VacuumValve = 11,
        Jig2VacuumValve = 12,
    }

    /// <summary>Tên hiển thị tiếng Việt cho màn hình IO monitor.</summary>
    public static class IoNames
    {
        private static readonly Dictionary<InputSignal, string> inputNames = new Dictionary<InputSignal, string>
        {
            { InputSignal.StartButton, "Nút START" },
            { InputSignal.StopButton, "Nút STOP (kiêm RESET)" },
            { InputSignal.Jig1LifterDown, "Xi lanh Jig 1 - xuống" },
            { InputSignal.Jig1LifterUp, "Xi lanh Jig 1 - lên" },
            { InputSignal.Jig2LifterDown, "Xi lanh Jig 2 - xuống" },
            { InputSignal.Jig2LifterUp, "Xi lanh Jig 2 - lên" },
            { InputSignal.Jig1VacuumOk, "Chân không Jig 1 - đã hút" },
            { InputSignal.Jig2VacuumOk, "Chân không Jig 2 - đã hút" },
        };

        private static readonly Dictionary<OutputSignal, string> outputNames = new Dictionary<OutputSignal, string>
        {
            { OutputSignal.TowerGreen, "Đèn tháp xanh" },
            { OutputSignal.TowerYellow, "Đèn tháp vàng" },
            { OutputSignal.TowerRed, "Đèn tháp đỏ" },
            { OutputSignal.StartLamp, "Đèn nút START" },
            { OutputSignal.StopLamp, "Đèn nút STOP" },
            { OutputSignal.Buzzer, "Còi (buzzer)" },
            { OutputSignal.Jig1LifterUpValve, "Van Jig 1 - lên" },
            { OutputSignal.Jig1LifterDownValve, "Van Jig 1 - xuống" },
            { OutputSignal.Jig2LifterUpValve, "Van Jig 2 - lên" },
            { OutputSignal.Jig2LifterDownValve, "Van Jig 2 - xuống" },
            { OutputSignal.Jig1VacuumValve, "Van hút chân không Jig 1" },
            { OutputSignal.Jig2VacuumValve, "Van hút chân không Jig 2" },
        };

        public static string Of(InputSignal signal) { return inputNames[signal]; }
        public static string Of(OutputSignal signal) { return outputNames[signal]; }
    }
}
