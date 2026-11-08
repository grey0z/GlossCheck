using System;
using GlossCheck.Data;
using GlossCheck.Hardware;
using GlossCheck.Settings;

namespace GlossCheck.Core
{
    public enum JigState
    {
        /// <summary>Chưa đo lần nào.</summary>
        Empty,
        Measuring,
        Ok,
        Ng,
    }

    /// <summary>
    /// Một bên jig: xi lanh nâng + trạng thái + kết quả đo gần nhất + bộ đếm.
    /// Chỉ thread quét được thay đổi (qua Machine / luồng đo); UI chỉ đọc.
    /// </summary>
    public class JigStation
    {
        private JigState _stateBeforeMeasure;

        public JigStation(JigId id, string name, Cylinder lifter, Vacuum vacuum)
        {
            Id = id;
            Name = name;
            Lifter = lifter;
            Vacuum = vacuum;
        }

        public JigId Id { get; private set; }
        public string Name { get; private set; }
        public Cylinder Lifter { get; private set; }
        /// <summary>Hút chân không giữ hàng trên jig.</summary>
        public Vacuum Vacuum { get; private set; }

        public JigState State { get; private set; }
        public GlossReading LastReading { get; private set; }
        public JudgeResult LastJudge { get; private set; }
        public DateTime? LastMeasuredAt { get; private set; }
        public int OkCount { get; private set; }
        public int NgCount { get; private set; }

        public static string StateText(JigState state)
        {
            switch (state)
            {
                case JigState.Empty: return "CHƯA ĐO";
                case JigState.Measuring: return "ĐANG ĐO";
                case JigState.Ok: return "OK";
                default: return "NG";
            }
        }

        internal void BeginMeasure()
        {
            if (State != JigState.Measuring) _stateBeforeMeasure = State;
            State = JigState.Measuring;
        }

        /// <summary>Lần đo bị huỷ (EMG / RESET): trả về trạng thái trước khi đo, không tính kết quả.</summary>
        internal void CancelMeasure()
        {
            if (State == JigState.Measuring) State = _stateBeforeMeasure;
        }

        internal void SetResult(GlossReading reading, JudgeResult judge)
        {
            LastReading = reading;
            LastJudge = judge;
            LastMeasuredAt = DateTime.Now;
            State = judge.IsOk ? JigState.Ok : JigState.Ng;
            if (judge.IsOk) OkCount++; else NgCount++;
        }

        internal void ResetCounters() { OkCount = 0; NgCount = 0; }

        internal void RestoreCounters(int okCount, int ngCount)
        {
            OkCount = Math.Max(0, okCount);
            NgCount = Math.Max(0, ngCount);
        }
    }
}
