using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using GlossCheck.Hardware;

namespace GlossCheck.Core
{
    /// <summary>
    /// Chạy mọi lệnh của máy đo (DLL đồng bộ, có thể chậm hoặc treo) trên 1 thread riêng,
    /// để thread quét không bao giờ bị chặn. DLL chỉ được gọi từ thread này nên không lo tranh chấp.
    /// Mỗi thời điểm chỉ có 1 lệnh; đang bận (kể cả khi DLL treo) thì từ chối lệnh mới.
    /// </summary>
    public sealed class GlossMeterService : IDisposable
    {
        private readonly IGlossMeter _meter;
        private readonly BlockingCollection<Action> _jobs = new BlockingCollection<Action>();
        private readonly Thread _worker;
        private readonly Action<string> _log;
        private int _isBusy;
        private int _lastRequestId;
        private MeasureResult _lastResult;

        public GlossMeterService(IGlossMeter meter, Action<string> log)
        {
            _meter = meter;
            _log = log;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "GlossMeter" };
            _worker.Start();
        }

        public sealed class MeasureResult
        {
            public int RequestId;
            public GlossReading Reading;
            public string Error;
        }

        public bool IsBusy { get { return Volatile.Read(ref _isBusy) != 0; } }
        public bool IsOpen { get { return _meter.IsOpen; } }
        public string CurrentJob { get; private set; } = "";
        public GlossMeterInfo Info { get; private set; }
        public GlossReading LastManualReading { get; private set; }

        public bool BeginOpen(string portName)
        {
            return TryEnqueue("Kết nối máy đo", () =>
            {
                bool ok = _meter.Open(portName);
                Info = ok ? _meter.GetInfo() : null;
                _log(ok ? "Máy đo: kết nối OK " + DescribeInfo() : "Máy đo: KHÔNG kết nối được");
                if (ok && Info != null && !Info.IsCalibrated) _log("Máy đo: CHƯA CALIB, hãy calib trước khi chạy.");
            });
        }

        public bool BeginCalibrate()
        {
            return TryEnqueue("Calib máy đo", () =>
            {
                bool ok = _meter.Calibrate();
                Info = _meter.GetInfo();
                _log(ok ? "Máy đo: calib OK" : "Máy đo: calib LỖI");
            });
        }

        /// <summary>Đo count lần liên tiếp, lấy trung bình. Kết quả lấy bằng TryGetResult(requestId).</summary>
        public bool BeginMeasure(int count, out int requestId)
        {
            int id = Interlocked.Increment(ref _lastRequestId);
            requestId = id;
            return TryEnqueue("Đo", () =>
            {
                var readings = new List<GlossReading>();
                string error = null;
                for (int i = 0; i < count; i++)
                {
                    var reading = _meter.Measure();
                    if (reading == null) { error = "Lần đo " + (i + 1) + "/" + count + " trả lỗi"; break; }
                    readings.Add(reading);
                }
                Volatile.Write(ref _lastResult, new MeasureResult
                {
                    RequestId = id,
                    Reading = error == null ? GlossReading.Average(readings) : null,
                    Error = error,
                });
            });
        }

        /// <summary>Đo 1 lần ở chế độ tay; kết quả hiện ở LastManualReading và log.</summary>
        public bool BeginManualMeasure()
        {
            return TryEnqueue("Đo thử", () =>
            {
                var reading = _meter.Measure();
                LastManualReading = reading;
                _log(reading != null ? "Đo thử: " + reading : "Đo thử: máy đo trả lỗi");
            });
        }

        public bool TryGetResult(int requestId, out MeasureResult result)
        {
            result = Volatile.Read(ref _lastResult);
            return result != null && result.RequestId == requestId;
        }

        public void Dispose()
        {
            _jobs.CompleteAdding();
            if (!_worker.Join(1000)) return; // DLL đang treo: bỏ qua, thread nền tự kết thúc khi thoát app
            try { _meter.Close(); } catch { }
        }

        private string DescribeInfo()
        {
            var info = Info;
            return info == null ? "" : string.Format("({0}, SN {1}, {2})", info.DeviceName, info.SerialNumber, info.PortName);
        }

        private bool TryEnqueue(string name, Action job)
        {
            if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0) return false;
            CurrentJob = name;
            _jobs.Add(job);
            return true;
        }

        private void WorkerLoop()
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                try { job(); }
                catch (Exception ex) { _log("Máy đo: lỗi " + ex.Message); }
                finally { CurrentJob = ""; Volatile.Write(ref _isBusy, 0); }
            }
        }
    }

    /// <summary>
    /// Bước đo: gửi yêu cầu cho GlossMeterService rồi chờ kết quả, không chặn thread quét.
    /// Chạy tiếp sau Stop / Alarm: lần đo cũ còn đang chạy thì chờ tiếp, đã có kết quả thì dùng luôn,
    /// lần đo trước bị lỗi thì đo lại.
    /// </summary>
    public class MeasureStep : StepBase
    {
        private readonly GlossMeterService _service;
        private readonly int _count;
        private int _requestId;
        private bool _hasRequest;

        public MeasureStep(GlossMeterService service, int count, int timeoutPerMeasureMs)
        {
            _service = service;
            _count = Math.Max(1, count);
            Description = _count == 1 ? "Đo độ bóng" : "Đo độ bóng (" + _count + " lần, lấy trung bình)";
            TimeoutMs = timeoutPerMeasureMs * _count;
            TimeoutAlarm = new MachineAlarm(AlarmCode.MeterTimeout);
        }

        public GlossReading Reading { get; private set; }

        public override void Start()
        {
            base.Start();
            if (Reading != null) return;
            GlossMeterService.MeasureResult pending;
            if (_hasRequest && !_service.TryGetResult(_requestId, out pending) && _service.IsBusy) return; // vẫn đang đo
            if (!_service.IsOpen) { Fault = new MachineAlarm(AlarmCode.MeterNotConnected); return; }
            if (!_service.BeginMeasure(_count, out _requestId))
            {
                Fault = new MachineAlarm(AlarmCode.MeterBusy, _service.CurrentJob);
                return;
            }
            _hasRequest = true;
        }

        public override bool IsDone()
        {
            if (Reading != null) return true;
            GlossMeterService.MeasureResult result;
            if (!_hasRequest || !_service.TryGetResult(_requestId, out result)) return false;
            if (result.Reading == null)
            {
                _hasRequest = false; // lần sau Start sẽ đo lại
                Fault = new MachineAlarm(AlarmCode.MeterMeasureFailed, result.Error);
                return false;
            }
            Reading = result.Reading;
            return true;
        }
    }
}
