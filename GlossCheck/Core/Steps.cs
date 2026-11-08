using System;
using System.Diagnostics;
using System.Linq;
using GlossCheck.Hardware;

namespace GlossCheck.Core
{
    /// <summary>
    /// Một bước của luồng. KHÔNG được chặn thread:
    ///   Start()  : gửi lệnh. Được gọi lại khi chạy tiếp sau Stop / Alarm, nên phải gọi lại được nhiều lần.
    ///   IsDone() : kiểm tra mỗi chu kỳ quét.
    ///   Pause()  : dừng phần cứng của bước này khi Stop / Alarm.
    ///   Fault    : khác null => Sequence báo alarm ngay.
    /// </summary>
    public interface IStep
    {
        string Description { get; }
        /// <summary>0 = chờ không giới hạn (vd. chờ người vận hành nạp hàng).</summary>
        int TimeoutMs { get; }
        MachineAlarm TimeoutAlarm { get; }
        MachineAlarm Fault { get; }
        void Start();
        bool IsDone();
        void Pause();
    }

    public abstract class StepBase : IStep
    {
        public string Description { get; protected set; }
        public int TimeoutMs { get; protected set; }
        public MachineAlarm TimeoutAlarm { get; protected set; }
        public MachineAlarm Fault { get; protected set; }
        public virtual void Start() { Fault = null; }
        public abstract bool IsDone();
        public virtual void Pause() { }
    }

    /// <summary>Chờ điều kiện.</summary>
    public class WaitStep : StepBase
    {
        private readonly Func<bool> _condition;

        public WaitStep(string description, Func<bool> condition, int timeoutMs = 0, MachineAlarm timeoutAlarm = null)
        {
            Description = description;
            _condition = condition;
            TimeoutMs = timeoutMs;
            TimeoutAlarm = timeoutAlarm ?? new MachineAlarm(AlarmCode.SoftwareError, "Timeout: " + description);
        }

        public override bool IsDone() { return _condition(); }
    }

    /// <summary>
    /// Chờ điều kiện tối đa limitMs nhưng KHÔNG báo alarm khi hết giờ: xong với TimedOut = true để luồng
    /// tự quyết định (vd. không hút được hàng -> huỷ lần đo nhẹ nhàng thay vì alarm). Thời gian Pause không tính.
    /// </summary>
    public class TryWaitStep : StepBase
    {
        private readonly Func<bool> _condition;
        private readonly int _limitMs;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        public TryWaitStep(string description, Func<bool> condition, int limitMs)
        {
            Description = description;
            _condition = condition;
            _limitMs = limitMs;
        }

        public bool TimedOut { get; private set; }

        public override void Start() { base.Start(); _stopwatch.Start(); }

        public override bool IsDone()
        {
            if (_condition()) { TimedOut = false; return true; }
            if (_stopwatch.ElapsedMilliseconds < _limitMs) return false;
            TimedOut = true;
            return true;
        }

        public override void Pause() { _stopwatch.Stop(); }
    }

    /// <summary>Làm 1 hành động rồi xong ngay. Hành động phải gọi lại được (Resume có thể gọi lại).</summary>
    public class ActionStep : StepBase
    {
        private readonly Action _action;
        public ActionStep(string description, Action action) { Description = description; _action = action; }
        public override void Start() { base.Start(); _action(); }
        public override bool IsDone() { return true; }
    }

    /// <summary>Trễ thời gian. Thời gian đang Pause không được tính.</summary>
    public class DelayStep : StepBase
    {
        private readonly int _durationMs;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        public DelayStep(int durationMs) { _durationMs = durationMs; Description = "Trễ " + durationMs + " ms"; }
        public override void Start() { base.Start(); _stopwatch.Start(); }
        public override bool IsDone() { return _stopwatch.ElapsedMilliseconds >= _durationMs; }
        public override void Pause() { _stopwatch.Stop(); }
    }

    public struct AxisTarget
    {
        public readonly IAxis Axis;
        public readonly double Position;
        public AxisTarget(IAxis axis, double position) { Axis = axis; Position = position; }
    }

    /// <summary>
    /// Chạy 1 hoặc nhiều trục tới vị trí TUYỆT ĐỐI. Vì là tuyệt đối nên chạy tiếp sau Stop chỉ cần gửi lại lệnh.
    /// issueMotion: cách gửi lệnh (null = từng trục MoveAbsolute; hoặc nội suy XY).
    /// precondition: interlock, trả về alarm nếu không được phép chạy.
    /// </summary>
    public class MoveStep : StepBase
    {
        private const int DriverResponseDelayMs = 300;
        private readonly AxisTarget[] _targets;
        private readonly Action _issueMotion;
        private readonly Func<MachineAlarm> _precondition;
        private readonly double _tolerance;
        private readonly Stopwatch _sinceStart = new Stopwatch();

        public MoveStep(string description, AxisTarget[] targets, Action issueMotion, Func<MachineAlarm> precondition,
                        double tolerance, int timeoutMs)
        {
            Description = description;
            _targets = targets;
            _issueMotion = issueMotion;
            _precondition = precondition;
            _tolerance = tolerance;
            TimeoutMs = timeoutMs;
            TimeoutAlarm = new MachineAlarm(AlarmCode.AxisMoveTimeout, description);
        }

        public override void Start()
        {
            base.Start();
            Fault = _precondition != null ? _precondition() : null;
            if (Fault != null) return;
            foreach (var target in _targets)
            {
                if (!target.Axis.IsHomed) { Fault = new MachineAlarm(AlarmCode.AxisNotHomed, "Trục " + target.Axis.Name); return; }
                if (target.Position < target.Axis.MinPosition || target.Position > target.Axis.MaxPosition)
                {
                    Fault = new MachineAlarm(AlarmCode.AxisSoftLimit, string.Format("Trục {0}: {1:0.00} mm", target.Axis.Name, target.Position));
                    return;
                }
            }
            _issueMotion();
            _sinceStart.Restart();
        }

        public override bool IsDone()
        {
            bool allArrived = true;
            foreach (var target in _targets)
            {
                var axis = target.Axis;
                if (axis.IsAlarm) { Fault = new MachineAlarm(AlarmCode.AxisServoAlarm, "Trục " + axis.Name + " khi đang chạy"); return false; }
                bool arrived = !axis.IsMoving && Math.Abs(axis.Position - target.Position) <= _tolerance;
                if (arrived) continue;
                allArrived = false;
                // đã đứng yên mà chưa tới đích (sau thời gian trễ phản hồi của driver) => lỗi, không chờ tới hết timeout
                if (!axis.IsMoving && _sinceStart.ElapsedMilliseconds > DriverResponseDelayMs)
                {
                    Fault = new MachineAlarm(AlarmCode.AxisStoppedOffTarget,
                        string.Format("Trục {0}: {1:0.00} ≠ {2:0.00} mm", axis.Name, axis.Position, target.Position));
                    return false;
                }
            }
            return allArrived;
        }

        public override void Pause() { foreach (var target in _targets) target.Axis.Stop(); }
    }

    /// <summary>Origin nhiều trục cùng lúc.</summary>
    public class HomeStep : StepBase
    {
        private readonly IAxis[] _axes;

        public HomeStep(string description, int timeoutMs, params IAxis[] axes)
        {
            Description = description;
            _axes = axes;
            TimeoutMs = timeoutMs;
            TimeoutAlarm = new MachineAlarm(AlarmCode.HomingTimeout, description);
        }

        public override void Start() { base.Start(); foreach (var axis in _axes) axis.Home(); }

        public override bool IsDone()
        {
            var bad = _axes.FirstOrDefault(a => a.IsAlarm);
            if (bad != null) { Fault = new MachineAlarm(AlarmCode.HomingFailed, "Trục " + bad.Name + " alarm khi origin"); return false; }
            return _axes.All(a => a.IsHomed && !a.IsHoming);
        }

        public override void Pause() { foreach (var axis in _axes) axis.Stop(); }
    }
}
