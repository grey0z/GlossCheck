using GlossCheck.Hardware;

namespace GlossCheck.Core
{
    /// <summary>
    /// Hút chân không giữ hàng trên jig: 1 van (output) + 1 công tắc chân không (input, có thể tắt trong cấu hình).
    /// Van giữ nguyên trạng thái khi Stop / alarm / dừng khẩn cấp để hàng không bị xê dịch.
    /// </summary>
    public class Vacuum
    {
        private readonly IIoModule _io;
        private readonly OutputSignal _valve;
        private readonly InputSignal _sensor;

        public Vacuum(string name, IIoModule io, OutputSignal valve, InputSignal sensor, AlarmCode lostCode)
        {
            Name = name;
            _io = io;
            _valve = valve;
            _sensor = sensor;
            LostCode = lostCode;
        }

        public string Name { get; private set; }
        /// <summary>Alarm khi đang giữ hàng mà mất chân không (hàng bị lệch / rơi).</summary>
        public AlarmCode LostCode { get; private set; }

        /// <summary>Van đang được bật.</summary>
        public bool IsOn { get { return _io.GetOutput(_valve); } }
        /// <summary>Công tắc chân không báo đã hút được hàng.</summary>
        public bool IsHolding { get { return _io.GetInput(_sensor); } }

        public void Command(bool on) { _io.SetOutput(_valve, on); }
    }
}
