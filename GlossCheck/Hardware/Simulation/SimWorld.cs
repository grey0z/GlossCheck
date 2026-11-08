using System;
using System.Threading;

namespace GlossCheck.Hardware.Simulation
{
    /// <summary>
    /// Mô phỏng toàn bộ phần cứng: 2 trục XY, 2 xi lanh nâng jig (van 2 cuộn), 2 hút chân không, nút START / STOP, máy đo.
    /// UI chỉ được đổi các cờ public (bool) và gọi PressButton().
    /// </summary>
    public class SimWorld
    {
        private const double LifterTravelSeconds = 0.3;
        private const double ButtonPressSeconds = 0.25;

        private readonly double[] _lifterState = new double[2]; // 0 = xuống hẳn, 1 = lên hẳn
        private readonly double[] _buttonTimers = new double[3];

        public SimWorld()
        {
            X = new SimAxis("X", 0, 400, 150);
            Y = new SimAxis("Y", 0, 300, 90);
            Io = new SimIoModule();
            Interpolator = new SimLinearInterpolator(X, Y);
            Meter = new SimGlossMeter(this);
        }

        public SimAxis X { get; private set; }
        public SimAxis Y { get; private set; }
        public SimIoModule Io { get; private set; }
        public SimLinearInterpolator Interpolator { get; private set; }
        public SimGlossMeter Meter { get; private set; }

        // ===== Cờ điều khiển mô phỏng (UI chỉnh) =====
        /// <summary>Giả lập rút cáp / mất nguồn module IO (máy chuyển sang Dừng khẩn cấp).</summary>
        public bool IsIoDisconnected { get { return Io.IsDisconnected; } set { Io.IsDisconnected = value; } }
        public volatile bool IsJig1LifterStuck;
        public volatile bool IsJig2LifterStuck;
        public volatile bool IsMeterHung;
        /// <summary>Jig 2 trả giá trị lệch spec (để thử kết quả NG).</summary>
        public volatile bool ForceJig2Ng;
        /// <summary>Có hàng đặt trên jig: van hút bật + có hàng => công tắc chân không báo đã hút.</summary>
        public volatile bool Jig1HasPart = true;
        public volatile bool Jig2HasPart = true;

        public double LifterState(int jigIndex) { return _lifterState[jigIndex]; }

        /// <summary>Giả lập bấm nút cứng trên bảng điều khiển.</summary>
        public void PressButton(InputSignal button)
        {
            int index = (int)button;
            if (index >= 0 && index < _buttonTimers.Length) _buttonTimers[index] = ButtonPressSeconds;
        }

        public void Step(double dt)
        {
            X.Step(dt);
            Y.Step(dt);

            StepLifter(0, OutputSignal.Jig1LifterUpValve, OutputSignal.Jig1LifterDownValve, IsJig1LifterStuck, dt);
            StepLifter(1, OutputSignal.Jig2LifterUpValve, OutputSignal.Jig2LifterDownValve, IsJig2LifterStuck, dt);

            for (int i = 0; i < _buttonTimers.Length; i++) _buttonTimers[i] = Math.Max(0, _buttonTimers[i] - dt);

            var inputs = Io.Inputs;
            inputs[(int)InputSignal.StartButton] = _buttonTimers[(int)InputSignal.StartButton] > 0;
            inputs[(int)InputSignal.StopButton] = _buttonTimers[(int)InputSignal.StopButton] > 0;
            inputs[(int)InputSignal.Jig1VacuumOk] = Io.Outputs[(int)OutputSignal.Jig1VacuumValve] && Jig1HasPart;
            inputs[(int)InputSignal.Jig2VacuumOk] = Io.Outputs[(int)OutputSignal.Jig2VacuumValve] && Jig2HasPart;
            inputs[(int)InputSignal.Jig1LifterDown] = _lifterState[0] <= 0;
            inputs[(int)InputSignal.Jig1LifterUp] = _lifterState[0] >= 1;
            inputs[(int)InputSignal.Jig2LifterDown] = _lifterState[1] <= 0;
            inputs[(int)InputSignal.Jig2LifterUp] = _lifterState[1] >= 1;
        }

        private void StepLifter(int index, OutputSignal upValve, OutputSignal downValve, bool isStuck, double dt)
        {
            if (isStuck) return;
            bool up = Io.Outputs[(int)upValve], down = Io.Outputs[(int)downValve];
            double speed = 1 / LifterTravelSeconds;
            if (up && !down) _lifterState[index] = Math.Min(1, _lifterState[index] + speed * dt);
            else if (down && !up) _lifterState[index] = Math.Max(0, _lifterState[index] - speed * dt);
            // cả 2 van cùng tắt: van 2 cuộn giữ nguyên vị trí
        }
    }

    /// <summary>
    /// Máy đo mô phỏng: mất ~0.7 s mỗi lần đo. Có jig đang nâng lên thì trả giá trị quanh mức danh định,
    /// không có thì trả giá trị gần 0 (đo vào không khí => NG).
    /// </summary>
    public class SimGlossMeter : IGlossMeter
    {
        private const int MeasureTimeMs = 700;
        private readonly SimWorld _world;
        private readonly Random _random = new Random();
        private bool _isOpen;

        public SimGlossMeter(SimWorld world) { _world = world; }

        public bool IsOpen { get { return _isOpen; } }
        public bool Open(string portName) { Thread.Sleep(200); _isOpen = true; return true; }
        public void Close() { _isOpen = false; }
        public bool Calibrate() { Thread.Sleep(1500); return _isOpen; }

        public GlossMeterInfo GetInfo()
        {
            if (!_isOpen) return null;
            return new GlossMeterInfo { DeviceName = "SIM-NHG268", SerialNumber = "SIM0001", PortName = "COM-SIM", IsCalibrated = true };
        }

        public GlossReading Measure()
        {
            if (!_isOpen) return null;
            Thread.Sleep(MeasureTimeMs);
            while (_world.IsMeterHung) Thread.Sleep(50); // giả lập DLL bị treo

            int jig = _world.LifterState(0) >= 1 ? 0 : (_world.LifterState(1) >= 1 ? 1 : -1);
            if (jig < 0) return new GlossReading(Noise(0.5), Noise(0.5), Noise(0.5));
            double offset = (jig == 1 && _world.ForceJig2Ng) ? -25 : 0;
            return new GlossReading(45 + offset + Noise(1.5), 85 + offset + Noise(1.5), 95 + offset + Noise(1.5));
        }

        private double Noise(double amplitude)
        {
            lock (_random) return Math.Round((_random.NextDouble() * 2 - 1) * amplitude + (amplitude < 1 ? amplitude : 0), 1);
        }
    }
}
