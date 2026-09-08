using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HC2.Core.Serial;

/// <summary>
/// The Win32 serial API, exactly as much of it as <see cref="Win32SerialTransport"/> needs.
/// </summary>
internal static class NativeSerial
{
    public const uint GENERIC_READ  = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint OPEN_EXISTING = 3;

    public const uint PURGE_TXABORT = 0x0001;
    public const uint PURGE_RXABORT = 0x0002;
    public const uint PURGE_TXCLEAR = 0x0004;
    public const uint PURGE_RXCLEAR = 0x0008;

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_ACCESS_DENIED  = 5;
    public const int ERROR_GEN_FAILURE    = 31;

    /// <summary>DCB parity values. Identical numbering to <see cref="System.IO.Ports.Parity"/>.</summary>
    public const byte NOPARITY = 0;

    /// <summary>DCB stop-bit values — NOT the same numbering as <see cref="System.IO.Ports.StopBits"/>.</summary>
    public const byte ONESTOPBIT = 0, ONE5STOPBITS = 1, TWOSTOPBITS = 2;

    /// <summary>DCB <c>Flags</c> bit 0. Windows requires it set for serial communication.</summary>
    public const uint FBINARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    public struct DCB
    {
        public uint   DCBlength;
        public uint   BaudRate;
        public uint   Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte   ByteSize;
        public byte   Parity;
        public byte   StopBits;
        public sbyte  XonChar;
        public sbyte  XoffChar;
        public sbyte  ErrorChar;
        public sbyte  EofChar;
        public sbyte  EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string   fileName,
                                                    uint     desiredAccess,
                                                    uint     shareMode,
                                                    IntPtr   securityAttributes,
                                                    uint     creationDisposition,
                                                    uint     flagsAndAttributes,
                                                    IntPtr   templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetCommState(SafeFileHandle handle, ref DCB dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetCommState(SafeFileHandle handle, ref DCB dcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetCommTimeouts(SafeFileHandle handle, ref COMMTIMEOUTS timeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PurgeComm(SafeFileHandle handle, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(SafeFileHandle handle,
                                         byte[]         buffer,
                                         int            bytesToWrite,
                                         out int        bytesWritten,
                                         IntPtr         overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle handle,
                                        byte[]         buffer,
                                        int            bytesToRead,
                                        out int        bytesRead,
                                        IntPtr         overlapped);

    /// <summary>
    /// Devices past COM9 are only reachable through the <c>\\.\</c> device namespace; the bare name silently
    /// fails. Using the prefix for every port keeps one code path.
    /// </summary>
    public static string DevicePath(string portName) =>
        portName.StartsWith(@"\\.\", StringComparison.Ordinal) ? portName : @"\\.\" + portName;

    public static string Describe(int error) => error switch
    {
        ERROR_FILE_NOT_FOUND => "the port does not exist",
        ERROR_ACCESS_DENIED  => "another program holds the port",
        ERROR_GEN_FAILURE    => "the driver reported a general failure",
        _                    => $"Win32 error {error}"
    };
}
