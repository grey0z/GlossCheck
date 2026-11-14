using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GlossCheck.Core
{
    /// <summary>
    /// Một luồng chạy tự động. Viết TUẦN TỰ bằng "yield return" trong Run(), nhưng KHÔNG chặn thread:
    /// mỗi chu kỳ quét Machine gọi Scan() 1 lần, luồng chỉ kiểm tra bước hiện tại rồi trả về ngay.
    /// </summary>
    public abstract class Sequence
    {
        private readonly Stopwatch _stepTimer = new Stopwatch();
        private IEnumerator<IStep> _flow;
        private IStep _currentStep;

        protected Sequence(string name, bool isLooping)
        {
            Name = name;
            IsLooping = isLooping;
        }

        public string Name { get; private set; }
        /// <summary>true: chạy hết thì lặp lại từ đầu (auto). false: chạy 1 lần (origin).</summary>
        public bool IsLooping { get; private set; }
        public bool IsFinished { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsActive { get { return _flow != null; } }
        public int CycleCount { get; private set; }
        public long CurrentStepElapsedMs { get { return _stepTimer.ElapsedMilliseconds; } }

        public string CurrentStepDescription
        {
            get
            {
                var step = _currentStep;
                if (step != null) return step.Description;
                return _flow == null ? "-" : "...";
            }
        }

        /// <summary>Machine gán: báo alarm.</summary>
        public Action<Sequence, MachineAlarm> AlarmHandler;

        // ================= Phần viết luồng =================
        protected abstract IEnumerable<IStep> Run();
        /// <summary>Được gọi khi luồng bị huỷ (EMG / kết thúc auto): trả dữ liệu về trạng thái nhất quán.</summary>
        protected virtual void OnAborted() { }
        /// <summary>
        /// Chờ điều kiện thực thi
        /// </summary>
        /// <param name="description">Mô tả</param>
        /// <param name="condition">Điều kiện</param>
        /// <param name="timeoutMs">Thời gian timeout</param>
        /// <param name="timeoutAlarm">Cảnh báo</param>
        /// <returns></returns>
        protected static IStep WaitUntil(string description, Func<bool> condition, int timeoutMs = 0, MachineAlarm timeoutAlarm = null)
        {
            return new WaitStep(description, condition, timeoutMs, timeoutAlarm);
        }
        /// <summary>
        /// Thực hiện 1 hành động ngay lập tức
        /// </summary>
        /// <param name="description">Mô tả</param>
        /// <param name="action">Hành động thực thi</param>
        /// <returns></returns>
        protected static IStep Do(string description, Action action) { return new ActionStep(description, action); }
        /// <summary>
        /// Trễ thời gian. Thời gian đang Pause không được tính.
        /// </summary>
        /// <param name="milliseconds">Thời gian delay</param>
        /// <returns></returns>
        protected static IStep Delay(int milliseconds) { return new DelayStep(milliseconds); }

        // ================= Machine gọi =================
        public void Scan()
        {
            if (IsPaused || IsFinished) return;

            if (_currentStep == null)
            {
                if (_flow == null) _flow = Run().GetEnumerator();
                if (!_flow.MoveNext())
                {
                    _flow.Dispose();
                    _flow = null;
                    CycleCount++;
                    if (!IsLooping) IsFinished = true;
                    return;
                }
                _currentStep = _flow.Current;
                _currentStep.Start();
                _stepTimer.Restart();
                if (_currentStep.Fault != null) { RaiseAlarm(_currentStep.Fault); return; }
            }

            if (_currentStep.IsDone()) { _currentStep = null; _stepTimer.Reset(); return; }
            if (_currentStep.Fault != null) { RaiseAlarm(_currentStep.Fault); return; }
            if (_currentStep.TimeoutMs > 0 && _stepTimer.ElapsedMilliseconds > _currentStep.TimeoutMs) RaiseAlarm(_currentStep.TimeoutAlarm);
        }

        /// <summary>Stop / Alarm: giữ nguyên vị trí trong luồng, dừng phần cứng của bước hiện tại.</summary>
        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            if (_currentStep != null) _currentStep.Pause();
            _stepTimer.Stop();
        }

        /// <summary>Start: chạy tiếp đúng bước đang dở (gửi lại lệnh của bước đó, tính lại timeout).</summary>
        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            if (_currentStep == null) return;
            _currentStep.Start();
            _stepTimer.Restart();
            if (_currentStep.Fault != null) RaiseAlarm(_currentStep.Fault);
        }

        /// <summary>Huỷ toàn bộ luồng, lần sau chạy lại từ đầu.</summary>
        public void Abort()
        {
            bool wasActive = _flow != null;
            if (_flow != null) _flow.Dispose();
            _flow = null;
            _currentStep = null;
            IsPaused = false;
            IsFinished = false;
            _stepTimer.Reset();
            if (wasActive) OnAborted();
        }

        public void Restart() { Abort(); CycleCount = 0; }

        private void RaiseAlarm(MachineAlarm alarm)
        {
            if (AlarmHandler != null) AlarmHandler(this, alarm);
        }
    }
}
