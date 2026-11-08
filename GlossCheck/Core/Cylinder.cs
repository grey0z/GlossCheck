using System.Linq;
using GlossCheck.Hardware;

namespace GlossCheck.Core
{
    /// <summary>
    /// Xi lanh nâng jig: van 2 cuộn (double solenoid) + 2 cảm biến hành trình.
    /// Van 2 cuộn giữ nguyên vị trí khi mất điện, nên Stop / alarm / dừng khẩn cấp không làm xi lanh đổi trạng thái.
    /// </summary>
    public class Cylinder
    {
        private readonly IIoModule _io;
        private readonly OutputSignal _upValve;
        private readonly OutputSignal _downValve;
        private readonly InputSignal _upSensor;
        private readonly InputSignal _downSensor;

        public Cylinder(string name, IIoModule io, OutputSignal upValve, OutputSignal downValve,
                        InputSignal upSensor, InputSignal downSensor, AlarmCode upTimeoutCode, AlarmCode downTimeoutCode)
        {
            Name = name;
            _io = io;
            _upValve = upValve;
            _downValve = downValve;
            _upSensor = upSensor;
            _downSensor = downSensor;
            UpTimeoutCode = upTimeoutCode;
            DownTimeoutCode = downTimeoutCode;
        }

        public string Name { get; private set; }
        public AlarmCode UpTimeoutCode { get; private set; }
        public AlarmCode DownTimeoutCode { get; private set; }

        public bool IsUp { get { return _io.GetInput(_upSensor) && !_io.GetInput(_downSensor); } }
        public bool IsDown { get { return _io.GetInput(_downSensor) && !_io.GetInput(_upSensor); } }
        public bool IsUpCommanded { get { return _io.GetOutput(_upValve); } }
        /// <summary>Giá trị thô từng cảm biến (để hiển thị / chẩn đoán: cả 2 cùng ON là lỗi cảm biến).</summary>
        public bool IsUpSensorOn { get { return _io.GetInput(_upSensor); } }
        public bool IsDownSensorOn { get { return _io.GetInput(_downSensor); } }

        public void Command(bool up)
        {
            _io.SetOutput(_upValve, up);
            _io.SetOutput(_downValve, !up);
        }

        public IStep MoveUp(int timeoutMs) { return new CylinderStep(Name + " LÊN", true, timeoutMs, this); }
        public IStep MoveDown(int timeoutMs) { return new CylinderStep(Name + " XUỐNG", false, timeoutMs, this); }

        /// <summary>Đưa nhiều xi lanh cùng lúc lên / xuống, xong khi tất cả tới cảm biến.</summary>
        public static IStep MoveAll(string description, bool up, int timeoutMs, params Cylinder[] cylinders)
        {
            return new CylinderStep(description, up, timeoutMs, cylinders);
        }

        private sealed class CylinderStep : StepBase
        {
            private readonly Cylinder[] _cylinders;
            private readonly bool _up;

            public CylinderStep(string description, bool up, int timeoutMs, params Cylinder[] cylinders)
            {
                Description = description;
                _cylinders = cylinders;
                _up = up;
                TimeoutMs = timeoutMs;
                TimeoutAlarm = null; // xác định theo xi lanh chưa tới, xem IsDone
            }

            public override void Start()
            {
                base.Start();
                foreach (var cylinder in _cylinders) cylinder.Command(_up);
                UpdateTimeoutAlarm();
            }

            public override bool IsDone()
            {
                UpdateTimeoutAlarm();
                return _cylinders.All(c => _up ? c.IsUp : c.IsDown);
            }

            // Pause: KHÔNG làm gì -> xi lanh giữ nguyên vị trí

            private void UpdateTimeoutAlarm()
            {
                var late = _cylinders.FirstOrDefault(c => !(_up ? c.IsUp : c.IsDown)) ?? _cylinders[0];
                var code = _up ? late.UpTimeoutCode : late.DownTimeoutCode;
                if (TimeoutAlarm == null || TimeoutAlarm.Code != code) TimeoutAlarm = new MachineAlarm(code);
            }
        }
    }
}
