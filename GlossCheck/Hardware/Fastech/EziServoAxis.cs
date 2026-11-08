using System;
using System.Net;

namespace GlossCheck.Hardware.Fastech
{
    /// <summary>
    /// Trục Fastech Ezi-SERVO Plus-E (Ethernet). BoardId = octet cuối của IP (giống cách làm trong ColorMeasure).
    /// Kiểu origin (limit / cảm biến ORG / Z-pulse) cài trong tham số driver bằng phần mềm GUI của Fastech.
    /// </summary>
    public class EziServoAxis : IAxis
    {
        private const uint FlagErrorAll = 0x00000001;
        private const uint FlagOriginReturning = 0x00040000;
        private const uint FlagServoOn = 0x00100000;
        private const uint FlagOriginReturnOk = 0x02000000;
        private const uint FlagMotioning = 0x08000000;
        private const int CommErrorScanCount = 5;

        private readonly IPAddress _ipAddress;
        private uint _status;
        private int _actualPulse;
        private bool _isHomed;
        private bool _wasHoming;
        private bool _isCommError;
        private int _failCount;

        public EziServoAxis(string name, string ipAddress, double pulsePerMm, double minPosition, double maxPosition)
        {
            Name = name;
            PulsePerMm = pulsePerMm;
            MinPosition = minPosition;
            MaxPosition = maxPosition;
            if (!IPAddress.TryParse(ipAddress, out _ipAddress)) throw new ArgumentException("IP trục " + name + " không hợp lệ: " + ipAddress);
            BoardId = _ipAddress.GetAddressBytes()[3];
        }

        public string Name { get; private set; }
        public int BoardId { get; private set; }
        public double PulsePerMm { get; private set; }
        public double MinPosition { get; private set; }
        public double MaxPosition { get; private set; }

        public bool Connect() { return EziNativeMethods.FAS_Connect(_ipAddress, BoardId); }
        public void Close() { EziNativeMethods.FAS_Close(BoardId); }

        public void Refresh()
        {
            uint status = 0;
            int pulse = 0;
            if (EziNativeMethods.FAS_GetAxisStatus(BoardId, ref status) == EziNativeMethods.FMM_OK &&
                EziNativeMethods.FAS_GetActualPos(BoardId, ref pulse) == EziNativeMethods.FMM_OK)
            {
                _status = status;
                _actualPulse = pulse;
                _failCount = 0;
                _isCommError = false;
            }
            else if (++_failCount >= CommErrorScanCount)
            {
                _isCommError = true;
            }

            // Chỉ công nhận homed khi THẤY driver vào trạng thái origin rồi kết thúc với ORIGINRETOK
            bool homingNow = (_status & FlagOriginReturning) != 0;
            if (homingNow) _wasHoming = true;
            else if (_wasHoming)
            {
                _wasHoming = false;
                _isHomed = (_status & FlagOriginReturnOk) != 0;
            }
        }

        public double Position { get { return _actualPulse / PulsePerMm; } }
        public bool IsMoving { get { return (_status & (FlagMotioning | FlagOriginReturning)) != 0; } }
        public bool IsAlarm { get { return (_status & FlagErrorAll) != 0; } }
        public bool IsServoOn { get { return (_status & FlagServoOn) != 0; } }
        public bool IsHoming { get { return (_status & FlagOriginReturning) != 0; } }
        public bool IsHomed { get { return _isHomed; } }
        public bool IsCommError { get { return _isCommError; } }

        public int ToPulse(double mm) { return (int)Math.Round(mm * PulsePerMm); }
        public uint ToPulseSpeed(double mmPerSecond) { return (uint)Math.Max(1, Math.Round(mmPerSecond * PulsePerMm)); }

        public void SetServo(bool on) { EziNativeMethods.FAS_ServoEnable(BoardId, on ? 1 : 0); }
        public void ResetAlarm() { EziNativeMethods.FAS_ServoAlarmReset(BoardId); }

        public void MoveAbsolute(double positionMm, double speedMmS)
        {
            EziNativeMethods.FAS_MoveSingleAxisAbsPos(BoardId, ToPulse(positionMm), ToPulseSpeed(speedMmS));
        }

        public void Jog(int direction, double speedMmS)
        {
            EziNativeMethods.FAS_MoveVelocity(BoardId, ToPulseSpeed(speedMmS),
                direction > 0 ? EziNativeMethods.DIR_INC : EziNativeMethods.DIR_DEC);
        }

        public void Home()
        {
            _isHomed = false;
            _wasHoming = false;
            EziNativeMethods.FAS_MoveOriginSingleAxis(BoardId);
        }

        public void Stop() { EziNativeMethods.FAS_MoveStop(BoardId); }
        public void EmergencyStop() { EziNativeMethods.FAS_EmergencyStop(BoardId); }
        public void ClearHomed() { _isHomed = false; }
    }

    /// <summary>
    /// Nội suy tuyến tính X/Y bằng FAS_MoveLinearAbsPos.
    /// Feedrate tính thẳng từ tốc độ dài (mm/s) -> pulse/s, không tính riêng từng trục rồi gộp lại
    /// (code cũ nhân uint * uint bị tràn số khi tốc độ > 65535 pps).
    /// </summary>
    public class EziLinearInterpolator : ILinearInterpolator
    {
        private readonly EziServoAxis _x;
        private readonly EziServoAxis _y;
        private readonly ushort _accelTimeMs;

        public EziLinearInterpolator(EziServoAxis x, EziServoAxis y, int accelTimeMs)
        {
            _x = x;
            _y = y;
            _accelTimeMs = (ushort)Math.Max(1, Math.Min(9999, accelTimeMs));
        }

        public void MoveLinearAbsolute(double xMm, double yMm, double feedMmS)
        {
            int[] boardIds = { _x.BoardId, _y.BoardId };
            int[] targets = { _x.ToPulse(xMm), _y.ToPulse(yMm) };
            double pulsePerMm = (_x.PulsePerMm + _y.PulsePerMm) / 2; // 2 trục thường cùng hệ số
            uint feed = (uint)Math.Max(1, Math.Round(feedMmS * pulsePerMm));
            EziNativeMethods.FAS_MoveLinearAbsPos(2, boardIds, targets, feed, _accelTimeMs);
        }
    }
}
