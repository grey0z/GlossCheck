using System;

namespace GlossCheck.Hardware.Simulation
{
    /// <summary>Trục mô phỏng: tốc độ không đổi, có servo, alarm, origin, jog và limit cứng.</summary>
    public class SimAxis : IAxis
    {
        private const double HomeSpeed = 80;
        private const double HardLimitMargin = 5;

        // trạng thái "vật lý" (SimWorld.Step cập nhật)
        private double _position;
        private double _target;
        private double _speed;
        private int _jogDirection;
        private bool _isMoving, _isHoming, _isServoOn, _isAlarm, _isHomed;

        // giá trị cache cho logic (Refresh)
        private double _cachedPosition;
        private bool _cachedMoving, _cachedAlarm, _cachedServoOn, _cachedHoming, _cachedHomed;

        public SimAxis(string name, double minPosition, double maxPosition, double startPosition)
        {
            Name = name;
            MinPosition = minPosition;
            MaxPosition = maxPosition;
            _position = startPosition;
        }

        public string Name { get; private set; }
        public double MinPosition { get; private set; }
        public double MaxPosition { get; private set; }
        internal double PhysicalPosition { get { return _position; } }

        internal void Step(double dt)
        {
            if (!_isServoOn || _isAlarm) { _isMoving = false; _isHoming = false; _jogDirection = 0; return; }
            if (_isHoming)
            {
                _position -= HomeSpeed * dt;
                if (_position <= 0) { _position = 0; _isHoming = false; _isHomed = true; }
                return;
            }
            if (_jogDirection != 0)
            {
                _position += _jogDirection * _speed * dt;
            }
            else if (_isMoving)
            {
                double distance = _target - _position;
                double stepLength = _speed * dt;
                if (Math.Abs(distance) <= stepLength) { _position = _target; _isMoving = false; }
                else _position += Math.Sign(distance) * stepLength;
            }

            if (_position < MinPosition - HardLimitMargin || _position > MaxPosition + HardLimitMargin)
            {
                _position = Math.Max(MinPosition - HardLimitMargin, Math.Min(MaxPosition + HardLimitMargin, _position));
                _isMoving = false; _jogDirection = 0; _isAlarm = true; // chạm limit cứng
            }
        }

        internal void CutPower() { _isServoOn = false; _isMoving = false; _isHoming = false; _jogDirection = 0; }

        public void Refresh()
        {
            _cachedPosition = _position;
            _cachedMoving = _isMoving || _isHoming || _jogDirection != 0;
            _cachedAlarm = _isAlarm;
            _cachedServoOn = _isServoOn;
            _cachedHoming = _isHoming;
            _cachedHomed = _isHomed;
        }

        public double Position { get { return _cachedPosition; } }
        public bool IsMoving { get { return _cachedMoving; } }
        public bool IsAlarm { get { return _cachedAlarm; } }
        public bool IsServoOn { get { return _cachedServoOn; } }
        public bool IsHoming { get { return _cachedHoming; } }
        public bool IsHomed { get { return _cachedHomed; } }
        public bool IsCommError { get { return false; } }

        public void SetServo(bool on) { _isServoOn = on; if (!on) { _isMoving = false; _isHoming = false; _jogDirection = 0; } }
        public void ResetAlarm() { _isAlarm = false; }

        public void MoveAbsolute(double positionMm, double speedMmS)
        {
            if (!_isServoOn || _isAlarm) return;
            _target = positionMm; _speed = Math.Max(1, speedMmS);
            _isMoving = true; _isHoming = false; _jogDirection = 0;
        }

        public void Jog(int direction, double speedMmS)
        {
            if (!_isServoOn || _isAlarm) return;
            _speed = Math.Max(1, speedMmS); _jogDirection = Math.Sign(direction); _isMoving = false;
        }

        public void Home() { if (_isServoOn && !_isAlarm) { _isHoming = true; _isMoving = false; _jogDirection = 0; _isHomed = false; } }
        public void Stop() { _isMoving = false; _isHoming = false; _jogDirection = 0; }
        public void EmergencyStop() { Stop(); }
        public void ClearHomed() { _isHomed = false; }
    }

    /// <summary>Nội suy mô phỏng: chia tốc độ theo tỉ lệ quãng đường để 2 trục tới cùng lúc.</summary>
    public class SimLinearInterpolator : ILinearInterpolator
    {
        private readonly SimAxis _x;
        private readonly SimAxis _y;

        public SimLinearInterpolator(SimAxis x, SimAxis y) { _x = x; _y = y; }

        public void MoveLinearAbsolute(double xMm, double yMm, double feedMmS)
        {
            double dx = Math.Abs(xMm - _x.PhysicalPosition);
            double dy = Math.Abs(yMm - _y.PhysicalPosition);
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6) { _x.MoveAbsolute(xMm, feedMmS); _y.MoveAbsolute(yMm, feedMmS); return; }
            _x.MoveAbsolute(xMm, Math.Max(1, feedMmS * dx / length));
            _y.MoveAbsolute(yMm, Math.Max(1, feedMmS * dy / length));
        }
    }

    public class SimIoModule : IIoModule
    {
        internal readonly bool[] Inputs = new bool[16];
        internal readonly bool[] Outputs = new bool[16];
        private readonly bool[] _cachedInputs = new bool[16];

        /// <summary>Giả lập mất kết nối module IO: input đọc về OFF hết, output không ghi được.</summary>
        internal volatile bool IsDisconnected;

        public void Refresh()
        {
            if (IsDisconnected) Array.Clear(_cachedInputs, 0, _cachedInputs.Length);
            else Array.Copy(Inputs, _cachedInputs, Inputs.Length);
        }

        public bool GetInput(InputSignal signal) { return _cachedInputs[(int)signal]; }
        public bool GetOutput(OutputSignal signal) { return Outputs[(int)signal]; }
        public void SetOutput(OutputSignal signal, bool on) { if (!IsDisconnected) Outputs[(int)signal] = on; }
        public bool IsCommError { get { return IsDisconnected; } }
    }
}
