namespace GlossCheck.Hardware
{
    /// <summary>
    /// Một trục servo. Đơn vị: mm và mm/s; lớp thực thi tự quy đổi sang pulse.
    /// Refresh() được gọi đúng 1 lần mỗi chu kỳ quét, các property chỉ trả giá trị đã đọc trong chu kỳ đó.
    /// </summary>
    public interface IAxis
    {
        string Name { get; }
        /// <summary>Giới hạn mềm (mm).</summary>
        double MinPosition { get; }
        double MaxPosition { get; }

        void Refresh();
        double Position { get; }
        bool IsMoving { get; }
        bool IsAlarm { get; }
        bool IsServoOn { get; }
        bool IsHoming { get; }
        /// <summary>Đã origin thành công kể từ lần EMG / alarm / khởi động gần nhất.</summary>
        bool IsHomed { get; }
        bool IsCommError { get; }

        void SetServo(bool on);
        void ResetAlarm();
        void MoveAbsolute(double positionMm, double speedMmS);
        /// <summary>Chạy liên tục theo chiều direction (+1 / -1) tới khi Stop().</summary>
        void Jog(int direction, double speedMmS);
        void Home();
        void Stop();
        void EmergencyStop();
        /// <summary>Xoá cờ IsHomed để bắt buộc origin lại (sau EMG / lỗi trục).</summary>
        void ClearHomed();
    }

    /// <summary>Nội suy tuyến tính 2 trục X/Y: hai trục đi đường thẳng và tới đích cùng lúc.</summary>
    public interface ILinearInterpolator
    {
        void MoveLinearAbsolute(double xMm, double yMm, double feedMmS);
    }

    /// <summary>Module IO số.</summary>
    public interface IIoModule
    {
        void Refresh();
        bool GetInput(InputSignal signal);
        bool GetOutput(OutputSignal signal);
        void SetOutput(OutputSignal signal, bool on);
        bool IsCommError { get; }
    }
}
