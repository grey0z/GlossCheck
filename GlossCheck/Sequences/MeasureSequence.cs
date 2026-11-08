using System.Collections.Generic;
using GlossCheck.Core;

namespace GlossCheck.Sequences
{
    /// <summary>
    /// 1 lần đo (mỗi lần bấm START chạy 1 lần). Target = jig đang ở NGOÀI (người vận hành vừa đặt hàng):
    ///   1. Bật hút chân không Target, chờ công tắc báo đã hút.
    ///      Không hút được (chưa đặt hàng / lệch / mất khí) => nhả hút, bíp cảnh báo, huỷ lần đo (KHÔNG alarm).
    ///   2. Hạ cả 2 xi lanh -> XY tới vị trí của Target (con thoi đưa Target vào, jig kia ra ngoài).
    ///   3. Nhả hút jig vừa ra ngoài để người vận hành lấy hàng đã đo.
    ///   4. Kiểm tra Target vẫn giữ hàng -> nâng xi lanh -> đo -> kiểm tra lại -> hạ xi lanh.
    ///   5. Phán định + ghi CSV (+ bíp khi NG). Target ở lại bên trong, vẫn hút giữ hàng tới lần START sau.
    /// Hút chân không giữ nguyên khi STOP / alarm / dừng khẩn cấp để hàng không bị xê dịch.
    /// </summary>
    public class MeasureSequence : Sequence
    {
        private const int HoldCheckMs = 500;
        private readonly Machine _machine;

        public MeasureSequence(Machine machine) : base("Đo", false) { _machine = machine; }

        /// <summary>Jig cần đo trong lần chạy này.</summary>
        public JigStation Target { get; internal set; }

        protected override IEnumerable<IStep> Run()
        {
            var m = _machine;
            var timeouts = m.Config.Timeouts;
            var vacuumSettings = m.Config.Vacuum;
            var jig = Target;
            var other = jig == m.Jig1 ? m.Jig2 : m.Jig1;
            var recipe = m.Recipe; // dùng 1 recipe cố định cho cả lần đo

            jig.BeginMeasure();

            // 1. Hút giữ hàng trên jig đang ở ngoài
            yield return Do("Bật hút chân không " + jig.Name, () => jig.Vacuum.Command(true));
            if (vacuumSettings.UseSensor)
            {
                var hold = new TryWaitStep("Chờ hút được hàng " + jig.Name, () => jig.Vacuum.IsHolding, vacuumSettings.HoldTimeoutMs);
                yield return hold;
                if (hold.TimedOut)
                {
                    jig.Vacuum.Command(false);
                    jig.CancelMeasure();
                    m.ReportNoPart(jig);
                    yield break; // huỷ lần đo, máy về Sẵn sàng
                }
            }
            else
            {
                yield return Delay(vacuumSettings.HoldTimeoutMs);
            }

            // 2. Đưa jig vào vị trí đo
            yield return Cylinder.MoveAll("Hạ cả 2 xi lanh jig", false, timeouts.CylinderMs, m.Jig1.Lifter, m.Jig2.Lifter);
            yield return m.Gantry.MoveTo("Đưa " + jig.Name + " vào vị trí đo", recipe.Points.ForJig(jig.Id));

            // 3. Jig kia đã ra ngoài: nhả hút để lấy hàng
            yield return Do("Nhả chân không " + other.Name + " (đã ra ngoài)", () => other.Vacuum.Command(false));

            // 4. Đo
            if (vacuumSettings.UseSensor) yield return HoldCheck(jig, "trước khi đo");
            yield return jig.Lifter.MoveUp(timeouts.CylinderMs);

            var measure = new MeasureStep(m.MeterService, recipe.MeasureCount, timeouts.MeasureMs);
            yield return measure;

            if (vacuumSettings.UseSensor) yield return HoldCheck(jig, "sau khi đo");
            yield return jig.Lifter.MoveDown(timeouts.CylinderMs);

            // 5. Kết quả
            yield return Do("Phán định và ghi kết quả " + jig.Name, () => m.RecordResult(jig, recipe, measure.Reading));
        }

        /// <summary>Hàng phải còn được hút giữ; mất chân không => alarm (hàng lệch, kết quả không tin cậy).</summary>
        private static IStep HoldCheck(JigStation jig, string when)
        {
            return WaitUntil("Kiểm tra " + jig.Name + " còn giữ hàng " + when, () => jig.Vacuum.IsHolding, HoldCheckMs,
                new MachineAlarm(jig.Vacuum.LostCode));
        }

        /// <summary>Huỷ giữa chừng (dừng khẩn cấp / RESET khi tạm dừng): bỏ lần đo dở, không ghi kết quả. Hút giữ nguyên.</summary>
        protected override void OnAborted()
        {
            if (Target != null) Target.CancelMeasure();
        }
    }
}
