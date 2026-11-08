using System;

namespace GlossCheck.Hardware.Nhg
{
    /// <summary>Máy đo độ bóng NHG thật (NGComm.dll). Mọi hàm đều chặn.</summary>
    public class NhgGlossMeter : IGlossMeter
    {
        private IntPtr _handle;

        public bool IsOpen { get { return _handle != IntPtr.Zero; } }

        public bool Open(string portName)
        {
            if (IsOpen) Close();
            var error = NhgNativeMethods.NG_OpenDevice(ref _handle, string.IsNullOrWhiteSpace(portName) ? null : portName);
            if (error != NhgNativeMethods.ErrorCode.Success) _handle = IntPtr.Zero;
            return IsOpen;
        }

        public void Close()
        {
            if (!IsOpen) return;
            NhgNativeMethods.NG_CloseDevice(ref _handle);
            _handle = IntPtr.Zero;
        }

        public GlossReading Measure()
        {
            if (!IsOpen) return null;
            var result = new NhgNativeMethods.TripleValue();
            if (NhgNativeMethods.NG_Measure(_handle, ref result) != NhgNativeMethods.ErrorCode.Success) return null;
            return new GlossReading(result.Value20, result.Value60, result.Value85);
        }

        public bool Calibrate()
        {
            return IsOpen && NhgNativeMethods.NG_Calibrate(_handle) == NhgNativeMethods.ErrorCode.Success;
        }

        public GlossMeterInfo GetInfo()
        {
            if (!IsOpen) return null;
            var info = new NhgNativeMethods.DeviceInfo();
            if (NhgNativeMethods.NG_GetDeviceInfo(_handle, ref info) != NhgNativeMethods.ErrorCode.Success) return null;
            int port = NhgNativeMethods.NG_GetDeviceComPort(_handle);
            return new GlossMeterInfo
            {
                DeviceName = info.DeviceName,
                SerialNumber = info.DeviceNumber,
                PortName = port >= 0 ? "COM" + port : "",
                IsCalibrated = info.Calibrated != 0,
            };
        }
    }
}
