using System;
using System.Linq;
using GlossCheck.Hardware;
using GlossCheck.Settings;

namespace GlossCheck.Core
{
    /// <summary>
    /// Gantry XY mang đầu đo. INTERLOCK CHỐNG VA CHẠM nằm ở đây (không nằm trong luồng):
    /// X/Y chỉ được chạy khi TẤT CẢ xi lanh nâng jig đang ở dưới, nên luồng có viết sai thứ tự cũng không đâm máy.
    /// </summary>
    public class Gantry
    {
        private const double AtPointTolerance = 0.5;
        private readonly ILinearInterpolator _interpolator;
        private readonly Cylinder[] _lifters;
        private readonly Func<MachineConfig> _config;

        public Gantry(IAxis x, IAxis y, ILinearInterpolator interpolator, Cylinder[] lifters, Func<MachineConfig> config)
        {
            X = x;
            Y = y;
            _interpolator = interpolator;
            _lifters = lifters;
            _config = config;
        }

        public IAxis X { get; private set; }
        public IAxis Y { get; private set; }

        public bool AreLiftersDown { get { return _lifters.All(l => l.IsDown); } }

        /// <summary>null = được phép chạy XY.</summary>
        public MachineAlarm CheckMoveAllowed()
        {
            var up = _lifters.FirstOrDefault(l => !l.IsDown);
            return up == null ? null : new MachineAlarm(AlarmCode.InterlockLifterNotDown, up.Name);
        }

        public bool IsAt(XyPoint point)
        {
            return Math.Abs(X.Position - point.X) <= AtPointTolerance && Math.Abs(Y.Position - point.Y) <= AtPointTolerance;
        }

        /// <summary>Bước chạy XY tới điểm (nội suy hoặc từng trục, theo cấu hình).</summary>
        public IStep MoveTo(string description, XyPoint point)
        {
            var config = _config();
            var motion = config.Motion;
            var targets = new[] { new AxisTarget(X, point.X), new AxisTarget(Y, point.Y) };
            Action issue;
            if (motion.UseLinearInterpolation)
                issue = () => _interpolator.MoveLinearAbsolute(point.X, point.Y, motion.LinearSpeed);
            else
                issue = () =>
                {
                    X.MoveAbsolute(point.X, config.AxisX.MoveSpeed);
                    Y.MoveAbsolute(point.Y, config.AxisY.MoveSpeed);
                };
            return new MoveStep(description, targets, issue, CheckMoveAllowed, motion.InPositionTolerance, config.Timeouts.AxisMoveMs);
        }
    }
}
