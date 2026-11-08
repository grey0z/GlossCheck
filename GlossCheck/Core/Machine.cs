using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GlossCheck.Data;
using GlossCheck.Hardware;
using GlossCheck.Hardware.Fastech;
using GlossCheck.Hardware.Nhg;
using GlossCheck.Hardware.Simulation;
using GlossCheck.Sequences;
using GlossCheck.Settings;

namespace GlossCheck.Core
{
    public enum MachineState
    {
        NotHomed,
        Homing,
        Ready,
        Running,
        Paused,
        Alarm,
        Emergency,
    }

    /// <summary>
    /// Bộ điều khiển máy, kiểu PLC:
    ///  - 1 thread quét duy nhất: đọc IO -> nút cứng + lệnh UI -> an toàn -> chạy luồng -> xuất đèn.
    ///  - Lệnh từ UI được đưa vào hàng đợi và xử lý trong thread quét => logic không cần lock.
    ///  - Máy đo chạy trên thread riêng (GlossMeterService) nên không bao giờ chặn vòng quét.
    ///
    ///  NotHomed --ORIGIN--> Homing --xong--> Ready --START--> Running
    ///  Running --STOP--> Paused --START--> Running (chạy tiếp đúng bước đang dở)
    ///  Paused --RESET--> Ready (bỏ lần đo dở)
    ///  Lỗi --> Alarm --RESET--> Paused (lỗi trục / origin: NotHomed)
    ///  Mất kết nối IO --> Emergency --có kết nối lại + RESET--> NotHomed (bắt buộc ORIGIN)
    ///  (Máy không có nút EMG; nút STOP cứng kiêm RESET.)
    ///
    ///  START khi Ready: đo 1 lần jig đang ở ngoài rồi về Ready.
    /// </summary>
    public sealed class Machine : IDisposable
    {
        private readonly ConcurrentQueue<Action> _commands = new ConcurrentQueue<Action>();
        private readonly TextLog _textLog;
        private readonly bool[] _previousButtons = new bool[3];
        private Thread _scanThread;
        private volatile bool _isScanning;
        private MachineAlarm _activeAlarm;
        private JigStation _lastMeasuredJig;
        private Sequence _manualSequence;

        public Machine(MachineConfig config, Recipe recipe)
        {
            Config = config;
            Recipe = recipe;
            _textLog = new TextLog();

            IGlossMeter meter;
            if (config.Mode == RunMode.Simulation)
            {
                Sim = new SimWorld();
                X = Sim.X; Y = Sim.Y; Io = Sim.Io;
                Interpolator = Sim.Interpolator;
                meter = Sim.Meter;
                Log("Chế độ MÔ PHỎNG");
            }
            else
            {
                var x = new EziServoAxis("X", config.AxisX.IpAddress, config.AxisX.PulsePerMm, config.AxisX.MinPosition, config.AxisX.MaxPosition);
                var y = new EziServoAxis("Y", config.AxisY.IpAddress, config.AxisY.PulsePerMm, config.AxisY.MinPosition, config.AxisY.MaxPosition);
                var io = new EziIoModule(config.Io.InputModuleIp, config.Io.OutputModuleIp, config.Io.InputInvertMask);
                Log(x.Connect() ? "Kết nối trục X OK" : "Kết nối trục X LỖI (" + config.AxisX.IpAddress + ")");
                Log(y.Connect() ? "Kết nối trục Y OK" : "Kết nối trục Y LỖI (" + config.AxisY.IpAddress + ")");
                Log(io.Connect() ? "Kết nối module IO OK" : "Kết nối module IO LỖI");
                X = x; Y = y; Io = io;
                Interpolator = new EziLinearInterpolator(x, y, config.Motion.AccelTimeMs);
                meter = new NhgGlossMeter();
            }
            Axes = new[] { X, Y };

            var lifter1 = new Cylinder("Xi lanh Jig 1", Io, OutputSignal.Jig1LifterUpValve, OutputSignal.Jig1LifterDownValve,
                InputSignal.Jig1LifterUp, InputSignal.Jig1LifterDown, AlarmCode.Jig1LifterUpTimeout, AlarmCode.Jig1LifterDownTimeout);
            var lifter2 = new Cylinder("Xi lanh Jig 2", Io, OutputSignal.Jig2LifterUpValve, OutputSignal.Jig2LifterDownValve,
                InputSignal.Jig2LifterUp, InputSignal.Jig2LifterDown, AlarmCode.Jig2LifterUpTimeout, AlarmCode.Jig2LifterDownTimeout);
            var vacuum1 = new Vacuum("Hút chân không Jig 1", Io, OutputSignal.Jig1VacuumValve, InputSignal.Jig1VacuumOk, AlarmCode.Jig1VacuumLost);
            var vacuum2 = new Vacuum("Hút chân không Jig 2", Io, OutputSignal.Jig2VacuumValve, InputSignal.Jig2VacuumOk, AlarmCode.Jig2VacuumLost);
            Jig1 = new JigStation(JigId.Jig1, "Jig 1", lifter1, vacuum1);
            Jig2 = new JigStation(JigId.Jig2, "Jig 2", lifter2, vacuum2);
            Jigs = new[] { Jig1, Jig2 };
            var counters = CounterStore.Load();
            Jig1.RestoreCounters(counters.Jig1Ok, counters.Jig1Ng);
            Jig2.RestoreCounters(counters.Jig2Ok, counters.Jig2Ng);
            CountersResetAt = counters.ResetAt;
            Gantry =new Gantry(X, Y, Interpolator, new[] { lifter1, lifter2 }, () => Config);

            MeterService = new GlossMeterService(meter, Log);
            ResultLogger = new ResultLogger(config.ResolveResultFolder());

            Measure = new MeasureSequence(this);
            Homing = new HomingSequence(this);
            Measure.AlarmHandler = (sequence, alarm) => RaiseAlarm(alarm, sequence.Name);
            Homing.AlarmHandler = (sequence, alarm) => RaiseAlarm(alarm, sequence.Name, true);

            MeterService.BeginOpen(config.Meter.PortName);
        }

        // ================= Thành phần =================
        public MachineConfig Config { get; private set; }
        /// <summary>Recipe đang dùng. Chỉ thay qua CmdApplyRecipe (trong thread quét).</summary>
        public Recipe Recipe { get; private set; }
        public SimWorld Sim { get; private set; }
        public IIoModule Io { get; private set; }
        public IAxis X { get; private set; }
        public IAxis Y { get; private set; }
        public IAxis[] Axes { get; private set; }
        public ILinearInterpolator Interpolator { get; private set; }
        public Gantry Gantry { get; private set; }
        public JigStation Jig1 { get; private set; }
        public JigStation Jig2 { get; private set; }
        public JigStation[] Jigs { get; private set; }
        public GlossMeterService MeterService { get; private set; }
        public ResultLogger ResultLogger { get; private set; }
        public MeasureSequence Measure { get; private set; }
        public HomingSequence Homing { get; private set; }

        // ================= Trạng thái (UI đọc) =================
        public MachineState State { get; private set; } = MachineState.NotHomed;
        public MachineAlarm ActiveAlarm { get { return _activeAlarm; } }
        public double ScanTimeMs { get; private set; }
        public bool AllHomed { get { return Axes.All(a => a.IsHomed); } }
        public bool IsManualMoveActive { get { return _manualSequence != null; } }
        public string ManualMoveDescription { get { var s = _manualSequence; return s == null ? "" : s.CurrentStepDescription; } }
        public MeasurementRecord LastRecord { get; private set; }
        public readonly ConcurrentQueue<string> Logs = new ConcurrentQueue<string>();

        /// <summary>
        /// Jig đang nằm dưới đầu đo, xác định theo vị trí con thoi (đang đứng ở Pos1 = Jig 1, Pos2 = Jig 2).
        /// null khi con thoi đang chạy / chưa origin / không ở vị trí teach nào.
        /// </summary>
        public JigStation JigInside
        {
            get
            {
                if (!AllHomed) return null;
                var points = Recipe.Points;
                if (Gantry.IsAt(points.Jig1)) return Jig1;
                if (Gantry.IsAt(points.Jig2)) return Jig2;
                return null;
            }
        }

        /// <summary>
        /// Jig sẽ được đo khi bấm START = jig đang ở NGOÀI (người vận hành vừa nạp hàng xong).
        /// Ví dụ sau ORIGIN con thoi ở Pos1 (Jig 1 ở trong) => START đo Jig 2.
        /// Con thoi không đứng ở vị trí teach nào (vd. vừa jog tay) thì lấy jig khác với jig vừa đo.
        /// </summary>
        public JigStation NextJigForStart
        {
            get
            {
                var inside = JigInside ?? _lastMeasuredJig ?? Jig1;
                return inside == Jig1 ? Jig2 : Jig1;
            }
        }

        /// <summary>Cho phép thao tác tay khi máy không chạy auto / origin / EMG và không có lệnh tay đang chạy.</summary>
        public bool CanOperateManually
        {
            get
            {
                var state = State;
                return state != MachineState.Running && state != MachineState.Homing && state != MachineState.Emergency
                       && _manualSequence == null;
            }
        }

        /// <summary>Jig đang ở ngoài (người vận hành với tới được). null khi chưa biết vị trí con thoi.</summary>
        public JigStation JigOutside
        {
            get
            {
                var inside = JigInside;
                return inside == null ? null : (inside == Jig1 ? Jig2 : Jig1);
            }
        }

        /// <summary>
        /// ĐỔI JIG: chạy con thoi sang vị trí còn lại mà không đo, để lấy jig đang ở trong ra
        /// (vd. sản phẩm cuối ca). Chỉ khi máy Sẵn sàng (không đang đo).
        /// </summary>
        public bool CanSwapJig
        {
            get { return State == MachineState.Ready && AllHomed && _manualSequence == null && JigInside != null; }
        }

        // =====================================================================
        //  Vòng đời
        // =====================================================================
        public void StartScan()
        {
            _isScanning = true;
            _scanThread = new Thread(ScanLoop) { IsBackground = true, Name = "MachineScan", Priority = ThreadPriority.AboveNormal };
            _scanThread.Start();
        }

        public void Dispose()
        {
            _isScanning = false;
            if (_scanThread != null) _scanThread.Join(1000);
            foreach (var axis in Axes) { axis.Stop(); }
            MeterService.Dispose();
            foreach (var axis in Axes.OfType<EziServoAxis>()) axis.Close();
            var io = Io as EziIoModule;
            if (io != null) io.Close();
            _textLog.Dispose();
        }

        // =====================================================================
        //  Lệnh từ UI (đưa vào hàng đợi, thực thi trong thread quét)
        // =====================================================================
        public void CmdOrigin() { Post(DoOrigin); }
        public void CmdStart() { Post(DoStart); }
        public void CmdStop() { Post(DoStop); }
        public void CmdReset() { Post(DoReset); }

        public void CmdResetCounters()
        {
            Post(() =>
            {
                foreach (var jig in Jigs) jig.ResetCounters();
                CountersResetAt = DateTime.Now;
                SaveCounters();
                Log("Xoá bộ đếm OK/NG");
            });
        }

        /// <summary>Thời điểm xoá bộ đếm gần nhất (bộ đếm tính từ lúc này).</summary>
        public DateTime CountersResetAt { get; private set; }

        private void SaveCounters()
        {
            CounterStore.SaveAsync(new CounterSnapshot
            {
                Jig1Ok = Jig1.OkCount, Jig1Ng = Jig1.NgCount,
                Jig2Ok = Jig2.OkCount, Jig2Ng = Jig2.NgCount,
                ResetAt = CountersResetAt,
            });
        }

        public void CmdApplyRecipe(Recipe recipe)
        {
            Post(() =>
            {
                if (State == MachineState.Running || State == MachineState.Homing) { Log("Không đổi recipe khi máy đang chạy"); return; }
                Recipe = recipe;
                Config.ActiveRecipe = recipe.Name;
                Log("Áp dụng recipe: " + recipe.Name);
            });
        }

        /// <summary>
        /// Áp dụng ngay các thông số không liên quan phần cứng: tốc độ, nội suy, dung sai, timeout, chu kỳ quét,
        /// thư mục kết quả. IP / pulse/mm / giới hạn mềm / chế độ chạy / gia tốc nội suy cần khởi động lại.
        /// </summary>
        public void CmdApplyRuntimeConfig(MachineConfig source)
        {
            Post(() =>
            {
                if (State == MachineState.Running || State == MachineState.Homing) { Log("Không đổi cấu hình khi máy đang chạy"); return; }
                Config.ScanPeriodMs = Math.Max(1, source.ScanPeriodMs);
                Config.Timeouts = source.Timeouts;
                Config.Motion.UseLinearInterpolation = source.Motion.UseLinearInterpolation;
                Config.Motion.LinearSpeed = source.Motion.LinearSpeed;
                Config.Motion.InPositionTolerance = source.Motion.InPositionTolerance;
                foreach (var pair in new[] { Tuple.Create(Config.AxisX, source.AxisX), Tuple.Create(Config.AxisY, source.AxisY) })
                {
                    pair.Item1.MoveSpeed = pair.Item2.MoveSpeed;
                    pair.Item1.JogMaxSpeed = pair.Item2.JogMaxSpeed;
                }
                if (Config.ResultFolder != source.ResultFolder)
                {
                    Config.ResultFolder = source.ResultFolder;
                    ResultLogger = new ResultLogger(Config.ResolveResultFolder());
                }
                Log("Đã áp dụng cấu hình (tốc độ, timeout, thư mục kết quả)");
            });
        }

        /// <summary>Thao tác tay 1 lần (bật servo, xi lanh...).</summary>
        public void CmdManual(string description, Action action)
        {
            Post(() =>
            {
                if (!CanOperateManually) { Log("Không cho thao tác tay '" + description + "' khi " + State); return; }
                Log("Tay: " + description);
                action();
            });
        }

        /// <summary>
        /// Jog: giữ nút thì chạy, nhả nút gọi CmdJogStop. speedPercent = % của "Tốc độ jog 100%" trong cấu hình.
        /// Bị chặn khi xi lanh chưa xuống.
        /// </summary>
        public void CmdJog(IAxis axis, int direction, int speedPercent)
        {
            Post(() =>
            {
                if (!CanOperateManually) return;
                var interlock = Gantry.CheckMoveAllowed();
                if (interlock != null) { Log(interlock.Message); return; }
                axis.Jog(direction, AxisSettingsOf(axis).JogSpeedAt(speedPercent));
            });
        }

        public AxisSettings AxisSettingsOf(IAxis axis) { return axis == X ? Config.AxisX : Config.AxisY; }

        public void CmdJogStop(IAxis axis) { Post(() => { if (State != MachineState.Running && State != MachineState.Homing) axis.Stop(); }); }

        /// <summary>Đi tới 1 điểm bằng tay: tự hạ cả 2 xi lanh trước rồi mới chạy XY.</summary>
        public void CmdMoveTo(string description, XyPoint point)
        {
            Post(() =>
            {
                if (!CanOperateManually) { Log("Không cho chạy tay khi " + State); return; }
                if (!AllHomed) { Log("Cần ORIGIN trước khi chạy tới vị trí"); return; }
                Log("Tay: " + description);
                _manualSequence = new ManualMoveSequence(this, description, point.Clone());
            });
        }

        /// <summary>Đổi jig: hạ 2 xi lanh, chạy sang vị trí còn lại (jig đang ở trong đi ra), không đo.</summary>
        public void CmdSwapJig()
        {
            Post(() =>
            {
                if (!CanSwapJig) { Log("Không đổi jig được lúc này (" + State + ")"); return; }
                var goingOut = JigInside;
                var target = goingOut == Jig1 ? Recipe.Points.Jig2 : Recipe.Points.Jig1;
                string description = "Đổi jig: đưa " + goingOut.Name + " ra";
                Log(description);
                _manualSequence = new ManualMoveSequence(this, description, target.Clone(), goingOut);
            });
        }

        // ================= Trạng thái kết nối (UI đọc) =================
        public bool IsSimulation { get { return Sim != null; } }
        /// <summary>Module IO đang liên lạc được (mất liên lạc liên tục 5 chu kỳ => false, máy vào Dừng khẩn cấp).</summary>
        public bool IsIoConnected { get { return !Io.IsCommError; } }
        public bool IsAxisConnected(IAxis axis) { return !axis.IsCommError; }

        /// <summary>
        /// Kết nối lại các thiết bị đang mất kết nối: driver trục, module IO (FAS_Connect) và máy đo.
        /// Không cho khi đang đo / đang origin. Kết nối lại xong vẫn phải RESET / ORIGIN như bình thường.
        /// </summary>
        public void CmdReconnect()
        {
            Post(() =>
            {
                if (State == MachineState.Running || State == MachineState.Homing) { Log("Không kết nối lại khi máy đang chạy"); return; }
                if (IsSimulation) { Log("Chế độ mô phỏng: không cần kết nối"); return; }
                int tried = 0;
                foreach (var axis in Axes.OfType<EziServoAxis>().Where(a => a.IsCommError))
                {
                    tried++;
                    Log((axis.Connect() ? "Kết nối lại trục " : "Kết nối lại LỖI trục ") + axis.Name);
                }
                var io = Io as EziIoModule;
                if (io != null && io.IsCommError)
                {
                    tried++;
                    Log(io.Connect() ? "Kết nối lại module IO OK" : "Kết nối lại module IO LỖI");
                }
                if (!MeterService.IsOpen && !MeterService.IsBusy)
                {
                    tried++;
                    MeterService.BeginOpen(Config.Meter.PortName);
                }
                if (tried == 0) Log("Tất cả thiết bị đang kết nối bình thường");
            });
        }

        public void CmdMeterConnect() { Post(() => RequireMeterFree(() => MeterService.BeginOpen(Config.Meter.PortName))); }
        public void CmdMeterCalibrate() { Post(() => RequireMeterFree(() => MeterService.BeginCalibrate())); }
        public void CmdMeterMeasureOnce() { Post(() => RequireMeterFree(() => MeterService.BeginManualMeasure())); }

        private void RequireMeterFree(Func<bool> action)
        {
            if (State == MachineState.Running) { Log("Máy đo đang dùng cho auto"); return; }
            if (!action()) Log("Máy đo đang bận: " + MeterService.CurrentJob);
        }

        private void Post(Action action) { _commands.Enqueue(action); }

        // =====================================================================
        //  Xử lý lệnh
        // =====================================================================
        private void DoOrigin()
        {
            if (State != MachineState.NotHomed && State != MachineState.Ready) { Log("ORIGIN không hợp lệ khi " + State); return; }
            AbortManualMove();
            Measure.Abort();
            Homing.Restart();
            SetState(MachineState.Homing);
        }

        private void DoStart()
        {
            switch (State)
            {
                case MachineState.Ready:
                    // Mỗi lần START = 1 lần đo: đưa jig đang ở ngoài vào, đo, trả kết quả rồi về Sẵn sàng
                    if (!AllHomed) { SetState(MachineState.NotHomed); return; }
                    if (_manualSequence != null) { Log("Con thoi đang chạy tay, chờ xong rồi bấm START"); return; }
                    if (!MeterService.IsOpen) Log("Cảnh báo: máy đo chưa kết nối");
                    var jig = NextJigForStart;
                    Measure.Restart();
                    Measure.Target = jig;
                    Log("START: đo " + jig.Name);
                    SetState(MachineState.Running);
                    break;

                case MachineState.Running:
                    Log("Đang đo " + (Measure.Target != null ? Measure.Target.Name : "") + ", chờ đo xong");
                    break;

                case MachineState.Paused:
                    if (!AllHomed) { Measure.Abort(); SetState(MachineState.NotHomed); return; }
                    AbortManualMove();
                    SetState(MachineState.Running);
                    Measure.Resume();
                    break;

                case MachineState.NotHomed:
                    Log("Máy chưa ORIGIN");
                    break;

                default:
                    Log("START không hợp lệ khi " + State);
                    break;
            }
        }

        private void DoStop()
        {
            switch (State)
            {
                case MachineState.Running:
                    Measure.Pause();
                    SetState(MachineState.Paused);
                    break;
                case MachineState.Homing:
                    Homing.Abort();
                    foreach (var axis in Axes) axis.Stop();
                    SetState(MachineState.NotHomed);
                    break;
                default:
                    if (_manualSequence != null) { AbortManualMove(); Log("Dừng chạy tay"); }
                    foreach (var axis in Axes) axis.Stop();
                    break;
            }
        }

        private void DoReset()
        {
            switch (State)
            {
                case MachineState.Alarm:
                    foreach (var axis in Axes) axis.ResetAlarm();
                    bool needsOrigin = _activeAlarm != null && _activeAlarm.NeedsOrigin;
                    _activeAlarm = null;
                    if (needsOrigin)
                    {
                        Measure.Abort();
                        foreach (var axis in Axes) axis.ClearHomed();
                        SetState(MachineState.NotHomed);
                    }
                    else if (Measure.IsActive) SetState(MachineState.Paused); // bấm START để làm lại bước bị lỗi
                    else SetState(AllHomed ? MachineState.Ready : MachineState.NotHomed); // lỗi khi thao tác tay
                    break;

                case MachineState.Emergency:
                    if (Io.IsCommError) { Log("Module IO vẫn mất kết nối: kiểm tra cáp / nguồn, bấm Kết nối lại rồi mới RESET."); return; }
                    foreach (var axis in Axes) axis.ResetAlarm();
                    _activeAlarm = null;
                    SetState(MachineState.NotHomed);
                    break;

                case MachineState.Paused:
                    Measure.Abort(); // bỏ lần đo đang dở (không ghi kết quả)
                    SetState(MachineState.Ready);
                    break;
            }
        }

        /// <summary>Luồng đo gọi khi đo xong 1 jig.</summary>
        internal void RecordResult(JigStation jig, Recipe recipe, GlossReading reading)
        {
            var judge = JudgeResult.Evaluate(recipe, reading);
            jig.SetResult(reading, judge);
            SaveCounters();
            _lastMeasuredJig = jig;
            var record = new MeasurementRecord { Time = DateTime.Now, RecipeName = recipe.Name, Jig = jig.Id, Reading = reading, Judge = judge };
            LastRecord = record;
            ResultLogger.Append(record);
            if (ResultLogger.LastError != null) Log("Lỗi ghi CSV: " + ResultLogger.LastError);
            Log(string.Format("{0}: {1}  =>  {2}", jig.Name, reading, judge.IsOk ? "OK" : "NG"));
            if (!judge.IsOk && Config.Buzzer.BeepOnNg) Beep(BeepPattern.Ng);
            else if (judge.IsOk && Config.Buzzer.BeepOnOk) Beep(BeepPattern.Short);
        }

        /// <summary>Luồng đo gọi khi bấm START mà không hút được hàng trên jig: huỷ lần đo, không alarm.</summary>
        internal void ReportNoPart(JigStation jig)
        {
            Log(jig.Name + ": KHÔNG HÚT ĐƯỢC HÀNG (chưa đặt hàng / hàng lệch / mất khí) - huỷ lần đo");
            NoticeText = jig.Name + ": không hút được hàng - đặt lại hàng rồi bấm START";
            NoticeTime = DateTime.Now;
            Beep(BeepPattern.Warning);
        }

        /// <summary>Thông báo ngắn trên màn hình (không phải alarm), vd. không hút được hàng.</summary>
        public string NoticeText { get; private set; } = "";
        public DateTime NoticeTime { get; private set; }

        // ================= Còi =================
        public enum BeepPattern { Short, Ng, Warning }
        private double _beepStart = -1;
        private BeepPattern _beepPattern;
        private double _now;

        /// <summary>Kêu còi theo kiểu: Short = 1 tiếng 0.2 s, Ng = 3 tiếng, Warning = 2 tiếng dài. Alarm / EMG kêu liên tục riêng.</summary>
        public void Beep(BeepPattern pattern)
        {
            _beepPattern = pattern;
            _beepStart = _now;
            _isBuzzerMuted = false; // sự kiện mới thì kêu lại
        }

        private volatile bool _isBuzzerMuted;

        /// <summary>Đã bấm TẮT CÒI: còi im tới khi có alarm / tiếng bíp mới.</summary>
        public bool IsBuzzerMuted { get { return _isBuzzerMuted; } }

        /// <summary>Còi đang (hoặc sẽ) kêu: có alarm / EMG hoặc đang phát tiếng bíp, và chưa tắt còi.</summary>
        public bool IsBuzzerSounding
        {
            get
            {
                var state = State;
                return !_isBuzzerMuted && (state == MachineState.Alarm || state == MachineState.Emergency || _beepStart >= 0);
            }
        }

        /// <summary>TẮT CÒI: im còi ngay (alarm vẫn còn, vẫn phải RESET). Alarm / tiếng bíp mới sẽ kêu lại.</summary>
        public void CmdMuteBuzzer()
        {
            Post(() =>
            {
                if (_isBuzzerMuted) return;
                _isBuzzerMuted = true;
                _beepStart = -1;
                Io.SetOutput(OutputSignal.Buzzer, false);
                Log("Tắt còi");
            });
        }

        private bool BeepOutput(double time)
        {
            if (_beepStart < 0) return false;
            double t = time - _beepStart;
            switch (_beepPattern)
            {
                case BeepPattern.Short:
                    if (t > 0.2) { _beepStart = -1; return false; }
                    return true;
                case BeepPattern.Ng: // 3 tiếng 0.15 s, nghỉ 0.15 s
                    if (t > 0.9) { _beepStart = -1; return false; }
                    return ((int)(t / 0.15)) % 2 == 0;
                default:             // 2 tiếng 0.4 s, nghỉ 0.2 s
                    if (t > 1.0) { _beepStart = -1; return false; }
                    return t < 0.4 || (t > 0.6 && t < 1.0);
            }
        }


        private void AbortManualMove()
        {
            if (_manualSequence == null) return;
            _manualSequence.Abort();
            _manualSequence = null;
            foreach (var axis in Axes) axis.Stop();
        }

        // =====================================================================
        //  Vòng quét
        // =====================================================================
        private void ScanLoop()
        {
            var clock = Stopwatch.StartNew();
            double lastTime = clock.Elapsed.TotalSeconds;
            while (_isScanning)
            {
                double startTime = clock.Elapsed.TotalSeconds;
                double dt = Math.Min(0.1, startTime - lastTime);
                lastTime = startTime;
                _now = startTime;
                try
                {
                    // 1. Đọc IO / trạng thái trục (cache cho cả chu kỳ)
                    if (Sim != null) Sim.Step(dt);
                    Io.Refresh();
                    foreach (var axis in Axes) axis.Refresh();

                    // 2. Nút cứng + lệnh UI (dùng IO vừa đọc)
                    ReadPanelButtons();
                    Action command;
                    while (_commands.TryDequeue(out command)) command();

                    // 3. An toàn
                    CheckSafety();

                    // 4. Luồng
                    if (State == MachineState.Homing)
                    {
                        Homing.Scan();
                        if (Homing.IsFinished && State == MachineState.Homing) SetState(MachineState.Ready);
                    }
                    else if (State == MachineState.Running)
                    {
                        Measure.Scan();
                        if (Measure.IsFinished && State == MachineState.Running) SetState(MachineState.Ready); // đo xong 1 lần
                    }
                    ScanManualMove();

                    // 5. Đèn, còi
                    UpdateLamps(startTime);
                }
                catch (Exception ex)
                {
                    RaiseAlarm(new MachineAlarm(AlarmCode.SoftwareError, ex.Message), "Vòng quét");
                    Log(ex.ToString());
                }

                ScanTimeMs = (clock.Elapsed.TotalSeconds - startTime) * 1000;
                Thread.Sleep(Math.Max(1, Config.ScanPeriodMs - (int)ScanTimeMs));
            }
        }

        private void ReadPanelButtons()
        {
            if (RisingEdge(0, InputSignal.StartButton)) { Log("Nút cứng START"); DoStart(); }
            if (RisingEdge(1, InputSignal.StopButton)) { Log("Nút cứng STOP"); DoPanelStop(); }
        }

        /// <summary>
        /// Bảng điều khiển không có nút RESET nên nút STOP cứng kiêm RESET:
        ///   Đang chạy / đang origin / đang chạy tay -> dừng (như STOP).
        ///   Alarm / Dừng khẩn cấp (đã có kết nối IO lại) / Tạm dừng -> RESET.
        /// Nút RESET trên màn hình vẫn dùng được như cũ.
        /// </summary>
        private void DoPanelStop()
        {
            switch (State)
            {
                case MachineState.Alarm:
                case MachineState.Emergency:
                case MachineState.Paused:
                    DoReset();
                    break;
                default:
                    DoStop();
                    break;
            }
        }

        private bool RisingEdge(int index, InputSignal signal)
        {
            bool now = !Io.IsCommError && Io.GetInput(signal);
            bool edge = now && !_previousButtons[index];
            _previousButtons[index] = now;
            return edge;
        }

        private void ScanManualMove()
        {
            var sequence = _manualSequence;
            if (sequence == null) return;
            sequence.Scan();
            if (sequence.IsFinished) { _manualSequence = null; Log("Chạy tay xong"); }
        }

        private void CheckSafety()
        {
            // Không có nút EMG: dừng khẩn cấp chỉ khi mất kết nối IO (không còn đọc được nút STOP / cảm biến)
            if (Io.IsCommError)
            {
                if (State != MachineState.Emergency) EnterEmergency(new MachineAlarm(AlarmCode.IoCommLost));
                return;
            }
            if (State == MachineState.Emergency || State == MachineState.Alarm) return;

            foreach (var axis in Axes)
            {
                if (axis.IsCommError) { RaiseAlarm(new MachineAlarm(AlarmCode.AxisCommLost, "Trục " + axis.Name), "An toàn"); return; }
                if (axis.IsAlarm) { RaiseAlarm(new MachineAlarm(AlarmCode.AxisServoAlarm, "Trục " + axis.Name), "An toàn"); return; }
            }
        }

        private void EnterEmergency(MachineAlarm alarm)
        {
            AbortManualMove();
            Measure.Abort();
            Homing.Abort();
            foreach (var axis in Axes) { axis.EmergencyStop(); axis.SetServo(false); axis.ClearHomed(); }
            // Xi lanh van 2 cuộn: KHÔNG đổi trạng thái
            _activeAlarm = alarm;
            _isBuzzerMuted = false; // sự cố mới: còi kêu lại
            Log("EMERGENCY: " + alarm);
            SetState(MachineState.Emergency);
        }

        /// <summary>Báo alarm: dừng luồng (giữ vị trí trong luồng để chạy tiếp). forceOrigin: bắt buộc origin lại.</summary>
        public void RaiseAlarm(MachineAlarm alarm, string source, bool forceOrigin = false)
        {
            if (State == MachineState.Alarm || State == MachineState.Emergency)
            {
                // đang Alarm mà thao tác tay lại lỗi: dừng lệnh tay, giữ alarm cũ
                if (_manualSequence != null) { AbortManualMove(); Log("Lỗi khi chạy tay [" + source + "] " + alarm); }
                return;
            }
            bool wasHoming = State == MachineState.Homing;

            Measure.Pause();
            if (wasHoming) Homing.Abort();
            AbortManualMove();
            foreach (var axis in Axes) axis.Stop();

            _activeAlarm = (forceOrigin || wasHoming) && !alarm.NeedsOrigin
                ? new MachineAlarm(AlarmCode.HomingFailed, alarm.Message)
                : alarm;
            _isBuzzerMuted = false; // sự cố mới: còi kêu lại
            Log("ALARM [" + source + "] " + _activeAlarm);
            SetState(MachineState.Alarm);
        }

        private void UpdateLamps(double time)
        {
            bool blink = ((int)(time * 2)) % 2 == 0;
            var state = State;
            bool isFault = state == MachineState.Alarm || state == MachineState.Emergency;

            Io.SetOutput(OutputSignal.TowerRed, isFault && (state == MachineState.Emergency || blink));
            Io.SetOutput(OutputSignal.TowerYellow, state == MachineState.Paused || state == MachineState.Ready ||
                                                   ((state == MachineState.NotHomed || state == MachineState.Homing) && blink));
            Io.SetOutput(OutputSignal.TowerGreen, state == MachineState.Running);
            // Còi: alarm / EMG kêu ngắt quãng liên tục (tới khi RESET), ngoài ra các tiếng bíp báo (NG, không có hàng...)
            bool beep = BeepOutput(time);
            Io.SetOutput(OutputSignal.Buzzer, !_isBuzzerMuted && ((isFault && blink) || beep));

            // Đèn trên nút: sáng = đang ở trạng thái đó, nhấp nháy = bấm được
            Io.SetOutput(OutputSignal.StartLamp, state == MachineState.Running || ((state == MachineState.Ready || state == MachineState.Paused) && blink));
            // STOP kiêm RESET: sáng khi tạm dừng, nhấp nháy khi alarm (bấm STOP để reset)
            Io.SetOutput(OutputSignal.StopLamp, state == MachineState.Paused || (isFault && blink));
        }

        private void SetState(MachineState state)
        {
            if (State == state) return;
            Log("Trạng thái: " + State + " -> " + state);
            State = state;
        }

        public void Log(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message;
            Logs.Enqueue(line);
            _textLog.Write(line);
        }

        /// <summary>
        /// Chạy tay tới 1 điểm: hạ cả 2 xi lanh rồi chạy XY.
        /// releaseAfterMove: jig đi ra ngoài sau khi chạy (ĐỔI JIG) -> nhả chân không để lấy hàng.
        /// </summary>
        private sealed class ManualMoveSequence : Sequence
        {
            private readonly Machine _machine;
            private readonly string _description;
            private readonly XyPoint _point;
            private readonly JigStation _releaseAfterMove;

            public ManualMoveSequence(Machine machine, string description, XyPoint point, JigStation releaseAfterMove = null) : base("Chạy tay", false)
            {
                _machine = machine;
                _description = description;
                _point = point;
                _releaseAfterMove = releaseAfterMove;
                AlarmHandler = (sequence, alarm) => _machine.RaiseAlarm(alarm, Name);
            }

            protected override System.Collections.Generic.IEnumerable<IStep> Run()
            {
                var m = _machine;
                yield return Cylinder.MoveAll("Hạ cả 2 xi lanh jig", false, m.Config.Timeouts.CylinderMs, m.Jig1.Lifter, m.Jig2.Lifter);
                yield return m.Gantry.MoveTo(_description, _point);
                if (_releaseAfterMove != null)
                {
                    var jig = _releaseAfterMove;
                    yield return Do("Nhả chân không " + jig.Name + " (đã ra ngoài)", () => jig.Vacuum.Command(false));
                }
            }
        }
    }
}
