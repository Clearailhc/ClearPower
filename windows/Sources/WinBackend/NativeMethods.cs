// Every P/Invoke signature of the backend in one place.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClearPower.Win
{
    internal static class NativeMethods
    {
        // ---- clocks ----
        [DllImport("kernel32.dll")]
        public static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

        // ---- console (WinExe running console commands) ----
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        public const int ATTACH_PARENT_PROCESS = -1;
        public const int STD_OUTPUT_HANDLE = -11;

        // ---- power status ----
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        // ---- battery class driver IOCTLs ----
        public static readonly Guid GUID_DEVCLASS_BATTERY = new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
        public const uint IOCTL_BATTERY_QUERY_TAG = 0x294040;
        public const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x294044;
        public const uint IOCTL_BATTERY_QUERY_STATUS = 0x29404c;
        public const uint BATTERY_CAPACITY_RELATIVE = 0x40000000;
        public const uint BATTERY_UNKNOWN_CAPACITY = 0xFFFFFFFF;
        public const int BATTERY_UNKNOWN_RATE = unchecked((int)0x80000000);
        public const uint BATTERY_POWER_ON_LINE = 1, BATTERY_DISCHARGING = 2, BATTERY_CHARGING = 4;

        public const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[]? inBuffer, uint inBufferSize, byte[]? outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

        // ---- processes ----
        public const int SystemProcessInformation = 5;

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        // ---- power mode (overlay scheme) ----
        [DllImport("powrprof.dll")]
        public static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

        [DllImport("powrprof.dll")]
        public static extern uint PowerGetActualOverlayScheme(out Guid actualOverlayGuid);

        [DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

        // ---- display state notifications ----
        public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        public const int DEVICE_NOTIFY_CALLBACK = 2;
        public const int PBT_POWERSETTINGCHANGE = 0x8013;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int DeviceNotifyCallbackRoutine(IntPtr context, int type, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            public DeviceNotifyCallbackRoutine Callback;
            public IntPtr Context;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POWERBROADCAST_SETTING
        {
            public Guid PowerSetting;
            public int DataLength;
            public int Data;
        }

        [DllImport("powrprof.dll")]
        public static extern uint PowerSettingRegisterNotification(ref Guid settingGuid, int flags, ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS recipient, out IntPtr registrationHandle);

        // ---- RPC (Lenovo Power Manager client) ----
        [DllImport("rpcrt4.dll", CharSet = CharSet.Unicode)]
        public static extern int RpcStringBindingComposeW(string? objUuid, string protSeq, string? networkAddr, string? endpoint, string? options, out IntPtr stringBinding);

        [DllImport("rpcrt4.dll", CharSet = CharSet.Unicode)]
        public static extern int RpcBindingFromStringBindingW(IntPtr stringBinding, out IntPtr binding);

        [DllImport("rpcrt4.dll", CharSet = CharSet.Unicode)]
        public static extern int RpcStringFreeW(ref IntPtr str);

        [DllImport("rpcrt4.dll")]
        public static extern int RpcBindingFree(ref IntPtr binding);

        [DllImport("rpcrt4.dll")]
        public static extern int RpcMgmtIsServerListening(IntPtr binding);

        [DllImport("rpcrt4.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr NdrClientCall2(IntPtr stubDescriptor, IntPtr format, __arglist);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr MidlUserAllocate(IntPtr size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void MidlUserFree(IntPtr ptr);
    }
}
