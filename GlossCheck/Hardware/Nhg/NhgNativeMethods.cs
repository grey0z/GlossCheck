using System;
using System.Runtime.InteropServices;

namespace GlossCheck.Hardware.Nhg
{
    /// <summary>
    /// P/Invoke tới NGComm.dll (máy đo độ bóng NHG, giao tiếp serial). Lấy từ NHGApiWrapper của ColorMeasure.
    /// DLL x64 phải nằm cạnh GlossCheck.exe.
    /// </summary>
    internal static class NhgNativeMethods
    {
        private const string DllName = "NGComm.dll";

        public enum ErrorCode
        {
            Success = 0,
            DeviceNotFound,
            DeviceAlreadyOpen,
            DeviceOpenFailed,
            InvalidDeviceHandle,
            Timeout,
            DeviceBusy,
            InvalidCommand,
            ExecuteFailed,
            GetResultFailed,
            InvalidParameter,
            Unknown,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct DeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string DeviceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FirmwareVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InnerCalibrationBoard;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string OuterCalibrationBoard;
            public ushort BasicStorable;
            public ushort StatisticsStorable;
            public ushort ContinuationStorable;
            public ushort BasicStored;
            public ushort StatisticsStored;
            public ushort ContinuationStored;
            public byte Calibrated; // 0 = chưa calib, 1 = đã calib
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TripleValue
        {
            public float Value20;
            public float Value60;
            public float Value85;
        }

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        public static extern ErrorCode NG_OpenDevice(ref IntPtr handle, string portName);

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        public static extern ErrorCode NG_CloseDevice(ref IntPtr handle);

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        public static extern ErrorCode NG_GetDeviceInfo(IntPtr handle, ref DeviceInfo deviceInfo);

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        public static extern ErrorCode NG_Calibrate(IntPtr handle);

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        public static extern ErrorCode NG_Measure(IntPtr handle, ref TripleValue result);

        [DllImport(DllName, CharSet = CharSet.Ansi)]
        public static extern int NG_GetDeviceComPort(IntPtr handle);
    }
}
