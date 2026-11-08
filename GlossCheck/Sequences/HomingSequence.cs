using System.Collections.Generic;
using System.Linq;
using GlossCheck.Core;

namespace GlossCheck.Sequences
{
    /// <summary>Origin: bật servo -> hạ cả 2 xi lanh -> origin X, Y cùng lúc -> về vị trí Ready (= Pos1).</summary>
    public class HomingSequence : Sequence
    {
        private const int ServoOnTimeoutMs = 3000;
        private readonly Machine _machine;

        public HomingSequence(Machine machine) : base("Origin", false) { _machine = machine; }

        protected override IEnumerable<IStep> Run()
        {
            var m = _machine;
            var timeouts = m.Config.Timeouts;

            yield return Do("Bật servo X, Y", () => { foreach (var axis in m.Axes) axis.SetServo(true); });
            yield return WaitUntil("Chờ servo ON", () => m.Axes.All(a => a.IsServoOn), ServoOnTimeoutMs,
                new MachineAlarm(AlarmCode.AxisServoOnTimeout));

            yield return Cylinder.MoveAll("Hạ cả 2 xi lanh jig", false, timeouts.CylinderMs, m.Jig1.Lifter, m.Jig2.Lifter);

            yield return new HomeStep("Origin X, Y", timeouts.HomingMs, m.Gantry.X, m.Gantry.Y);

            // Ready = Pos1: Jig 1 ở trong, Jig 2 ở ngoài sẵn sàng nạp hàng
            yield return m.Gantry.MoveTo("Về vị trí Ready (Pos1: Jig 1 vào, Jig 2 ra)", m.Recipe.Points.Ready);
        }
    }
}
