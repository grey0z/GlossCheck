using System;
using System.Net;

namespace GlossCheck.Hardware.Fastech
{
    /// <summary>
    /// IO qua module Fastech Ezi-IO Plus-E. Cho phép input và output nằm ở 2 module khác nhau
    /// (2 IP) hoặc cùng 1 module (cùng IP). BoardId = octet cuối của IP.
    /// Mất kết nối liên tục 5 chu kỳ => IsCommError, mọi input coi như OFF (Machine chuyển sang Dừng khẩn cấp).
    /// </summary>
    public class EziIoModule : IIoModule
    {
        private const int CommErrorScanCount = 5;

        private readonly IPAddress _inputIp;
        private readonly IPAddress _outputIp;
        private readonly int _inputBoardId;
        private readonly int _outputBoardId;
        private readonly uint _inputInvertMask;
        private uint _inputs;
        private uint _outputs;
        private int _failCount;
        private bool _isCommError;

        public EziIoModule(string inputIp, string outputIp, uint inputInvertMask)
        {
            if (!IPAddress.TryParse(inputIp, out _inputIp)) throw new ArgumentException("IP module input không hợp lệ: " + inputIp);
            if (!IPAddress.TryParse(outputIp, out _outputIp)) throw new ArgumentException("IP module output không hợp lệ: " + outputIp);
            _inputBoardId = _inputIp.GetAddressBytes()[3];
            _outputBoardId = _outputIp.GetAddressBytes()[3];
            _inputInvertMask = inputInvertMask;
        }

        public bool IsSingleModule { get { return _inputBoardId == _outputBoardId; } }

        public bool Connect()
        {
            bool ok = EziNativeMethods.FAS_Connect(_inputIp, _inputBoardId);
            if (!IsSingleModule) ok &= EziNativeMethods.FAS_Connect(_outputIp, _outputBoardId);
            return ok;
        }

        public void Close()
        {
            EziNativeMethods.FAS_Close(_inputBoardId);
            if (!IsSingleModule) EziNativeMethods.FAS_Close(_outputBoardId);
        }

        public void Refresh()
        {
            uint input = 0, latch = 0, output = 0, outputStatus = 0;
            bool ok = EziNativeMethods.FAS_GetInput(_inputBoardId, ref input, ref latch) == EziNativeMethods.FMM_OK &&
                      EziNativeMethods.FAS_GetOutput(_outputBoardId, ref output, ref outputStatus) == EziNativeMethods.FMM_OK;
            if (ok)
            {
                _inputs = input ^ _inputInvertMask;
                _outputs = output;
                _failCount = 0;
                _isCommError = false;
            }
            else if (++_failCount >= CommErrorScanCount)
            {
                _isCommError = true;
                _inputs = 0; // không còn tin được input nào
            }
        }

        public bool GetInput(InputSignal signal) { return (_inputs & Bit((int)signal)) != 0; }
        public bool GetOutput(OutputSignal signal) { return (_outputs & Bit((int)signal)) != 0; }

        public void SetOutput(OutputSignal signal, bool on)
        {
            uint mask = Bit((int)signal);
            if (((_outputs & mask) != 0) == on) return; // không đổi -> không gửi lệnh qua mạng
            if (on)
            {
                EziNativeMethods.FAS_SetOutput(_outputBoardId, mask, 0);
                _outputs |= mask;
            }
            else
            {
                EziNativeMethods.FAS_SetOutput(_outputBoardId, 0, mask);
                _outputs &= ~mask;
            }
        }

        public bool IsCommError { get { return _isCommError; } }

        private static uint Bit(int index) { return 1u << index; }
    }
}
