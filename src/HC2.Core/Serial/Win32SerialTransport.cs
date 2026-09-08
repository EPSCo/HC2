using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HC2.Core.Serial;

/// <summary>
/// <see cref="ISerialTransport"/> built directly on the Win32 serial API, for adapters that
/// <see cref="SerialPort"/> cannot open.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SerialPort.Open"/> always calls <c>SetCommState</c> and treats a failure there as fatal. A CH340
/// adapter on this project's bench opens fine with <c>CreateFile</c> and reports its settings correctly with
/// <c>GetCommState</c>, but refuses <c>SetCommState</c> with <c>ERROR_GEN_FAILURE</c> — even when handed back
/// the values it just reported. .NET therefore cannot open that port at all, while the Advantech SDK can,
/// because the SDK keeps the port open and carries on with whatever configuration the port already holds.
/// </para>
/// <para>
/// This class does the same: it configures the line when the driver allows it, and when the driver refuses it
/// checks whether the port already happens to hold the settings that were wanted. If it does, the refusal does
/// not matter and communication proceeds; if it does not, the caller is told the line cannot be reconfigured
/// rather than being handed a port that would silently run at the wrong rate.
/// </para>
/// </remarks>
public sealed class Win32SerialTransport : ISerialTransport
{
    private readonly object _gate = new();

    private SafeFileHandle? _handle;

    public Win32SerialTransport(SerialPortSettings settings) => Settings = settings;

    public SerialPortSettings Settings { get; private set; }

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    public string? LastError { get; private set; }

    /// <summary>
    /// False when the driver refused to apply the line settings and the port is running on whatever
    /// configuration it already had. Communication still works; the rate simply cannot be changed.
    /// </summary>
    public bool ConfigurationApplied { get; private set; } = true;

    public bool Open()
    {
        lock (_gate)
        {
            if (IsOpen)
                return true;

            var handle = NativeSerial.CreateFile(NativeSerial.DevicePath(Settings.PortName),
                                                  NativeSerial.GENERIC_READ | NativeSerial.GENERIC_WRITE,
                                                  0,                       // no sharing: a serial line has one owner
                                                  IntPtr.Zero,
                                                  NativeSerial.OPEN_EXISTING,
                                                  0,                       // synchronous I/O
                                                  IntPtr.Zero);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();

                LastError = $"Could not open {Settings.PortName}: {NativeSerial.Describe(error)} (Win32 {error}).";
                return false;
            }

            _handle = handle;

            if (!ApplyTimeouts(Settings))
            {
                var error = Marshal.GetLastWin32Error();
                Close();

                LastError = $"Could not set timeouts on {Settings.PortName}: {NativeSerial.Describe(error)} (Win32 {error}).";
                return false;
            }

            ApplyState(Settings, out var note);
            LastError = note;

            return true;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _handle?.Dispose();
            _handle = null;
        }
    }

    public bool Configure(SerialPortSettings settings)
    {
        lock (_gate)
        {
            if (!string.Equals(settings.PortName, Settings.PortName, StringComparison.OrdinalIgnoreCase) && IsOpen)
            {
                LastError = "Cannot change the port name while the port is open.";
                return false;
            }

            Settings = settings;

            if (!IsOpen)
                return true;

            if (!ApplyTimeouts(settings))
            {
                var error = Marshal.GetLastWin32Error();
                LastError = $"Could not set timeouts: {NativeSerial.Describe(error)} (Win32 {error}).";

                return false;
            }

            var applied = ApplyState(settings, out var note);
            LastError = note;

            return applied;
        }
    }

    public bool Transact(string frame, out string response)
    {
        lock (_gate)
        {
            response = string.Empty;

            if (_handle is not { IsInvalid: false, IsClosed: false })
            {
                LastError = "The port is not open.";
                return false;
            }

            // Clear anything left over from a transaction that timed out; reading a late reply as the answer to
            // this command would desynchronize every exchange that follows.
            NativeSerial.PurgeComm(_handle, NativeSerial.PURGE_RXCLEAR | NativeSerial.PURGE_TXCLEAR);

            var outgoing = Encoding.ASCII.GetBytes(frame);

            if (!NativeSerial.WriteFile(_handle, outgoing, outgoing.Length, out var written, IntPtr.Zero)
                || written != outgoing.Length)
            {
                var error = Marshal.GetLastWin32Error();
                LastError = $"Write failed on {Settings.PortName}: {NativeSerial.Describe(error)} (Win32 {error}).";

                return false;
            }

            var received = new StringBuilder();
            var buffer   = new byte[64];
            var clock    = Stopwatch.StartNew();

            while (clock.ElapsedMilliseconds < Settings.ReadTimeoutMs)
            {
                if (!NativeSerial.ReadFile(_handle, buffer, buffer.Length, out var read, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    LastError = $"Read failed on {Settings.PortName}: {NativeSerial.Describe(error)} (Win32 {error}).";

                    return false;
                }

                if (read == 0)
                    continue; // the read timed out with nothing; the loop's own deadline decides when to stop

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));

                var terminator = received.ToString().IndexOf('\r');

                if (terminator < 0)
                    continue;

                response  = received.ToString(0, terminator);
                LastError = null;

                return true;
            }

            LastError = received.Length == 0
                ? $"No response within {Settings.ReadTimeoutMs} ms."
                : $"Response was not terminated within {Settings.ReadTimeoutMs} ms (got '{received}').";

            return false;
        }
    }

    /// <summary>
    /// Applies baud rate, framing and parity, tolerating a driver that refuses. Caller holds the lock.
    /// </summary>
    /// <param name="note">Explanation when the settings could not be applied, otherwise null.</param>
    /// <returns>
    /// True when the line ends up running at the requested settings — whether because the driver accepted them
    /// or because the port was already configured that way.
    /// </returns>
    private bool ApplyState(SerialPortSettings settings, out string? note)
    {
        note = null;

        if (_handle == null)
            return false;

        var dcb = new NativeSerial.DCB { DCBlength = (uint) Marshal.SizeOf(typeof(NativeSerial.DCB)) };

        if (!NativeSerial.GetCommState(_handle, ref dcb))
        {
            var error = Marshal.GetLastWin32Error();
            note                 = $"Could not read the current line settings: {NativeSerial.Describe(error)} (Win32 {error}).";
            ConfigurationApplied = false;

            return false;
        }

        // Start from what the port already has, so flow-control and DTR/RTS bits the driver chose are kept
        // rather than being invented here.
        var desired = dcb;

        desired.BaudRate =  (uint) settings.BaudRate;
        desired.ByteSize =  (byte) settings.DataBits;
        desired.Parity   =  (byte) settings.Parity;
        desired.StopBits =  ToDcbStopBits(settings.StopBits);
        desired.Flags    |= NativeSerial.FBINARY;

        if (NativeSerial.SetCommState(_handle, ref desired))
        {
            ConfigurationApplied = true;
            return true;
        }

        var refusal = Marshal.GetLastWin32Error();

        // The driver refused. That only matters if the port is not already running the settings that were
        // wanted — re-read rather than assume, since a failed SetCommState changes nothing.
        var actual = new NativeSerial.DCB { DCBlength = (uint) Marshal.SizeOf(typeof(NativeSerial.DCB)) };

        ConfigurationApplied = false;

        if (NativeSerial.GetCommState(_handle, ref actual)
            && actual.BaudRate == desired.BaudRate
            && actual.ByteSize == desired.ByteSize
            && actual.Parity   == desired.Parity
            && actual.StopBits == desired.StopBits)
        {
            note = $"The driver refused to configure {Settings.PortName} (Win32 {refusal}), but it is already " +
                   $"set to {settings.BaudRate} {settings.DataBits}/{settings.Parity}/{settings.StopBits}, so " +
                   "communication can proceed. The line rate cannot be changed while this persists.";

            return true;
        }

        note = $"The driver refused to configure {Settings.PortName} (Win32 {refusal}), and the port is running " +
               $"at {actual.BaudRate} baud rather than the requested {settings.BaudRate}. Replug the adapter or " +
               "roll its driver back to change the line rate.";

        return false;
    }

    /// <summary>Caller holds the lock.</summary>
    private bool ApplyTimeouts(SerialPortSettings settings)
    {
        if (_handle == null)
            return false;

        var timeouts = new NativeSerial.COMMTIMEOUTS
        {
            // Return once the line has been quiet for a moment after data arrives, so a complete short frame
            // does not wait out the whole timeout; the total constant bounds the wait when nothing answers.
            ReadIntervalTimeout         = 20,
            ReadTotalTimeoutMultiplier  = 0,
            ReadTotalTimeoutConstant    = (uint) settings.ReadTimeoutMs,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant   = (uint) settings.WriteTimeoutMs
        };

        return NativeSerial.SetCommTimeouts(_handle, ref timeouts);
    }

    /// <summary>
    /// <see cref="StopBits"/> and the DCB disagree on numbering: the BCL orders them None/One/Two/OnePointFive,
    /// the DCB orders them One/OnePointFive/Two. Casting one to the other silently sets the wrong framing.
    /// </summary>
    private static byte ToDcbStopBits(StopBits stopBits) => stopBits switch
    {
        StopBits.One          => NativeSerial.ONESTOPBIT,
        StopBits.OnePointFive => NativeSerial.ONE5STOPBITS,
        StopBits.Two          => NativeSerial.TWOSTOPBITS,
        _                     => NativeSerial.ONESTOPBIT
    };

    public void Dispose() => Close();
}
