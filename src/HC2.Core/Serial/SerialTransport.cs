using System;
using System.IO;
using System.IO.Ports;
using System.Text;

namespace HC2.Core.Serial;

/// <summary>
/// <see cref="ISerialTransport"/> over <see cref="SerialPort"/>. Owns the port and the single lock that makes
/// the bus safe to share; nothing above this class needs to know about either.
/// </summary>
public sealed class SerialTransport : ISerialTransport
{
    /// <summary>Frames in both directions are terminated by a carriage return.</summary>
    public const string Terminator = "\r";

    private readonly object _gate = new();

    private SerialPort? _port;

    public SerialTransport(SerialPortSettings settings) => Settings = settings;

    public SerialPortSettings Settings { get; private set; }

    public bool IsOpen => _port is { IsOpen: true };

    public string? LastError { get; private set; }

    public bool Open()
    {
        lock (_gate)
        {
            if (IsOpen)
                return true;

            try
            {
                _port = new SerialPort(Settings.PortName)
                {
                    Encoding = Encoding.ASCII,
                    NewLine  = Terminator
                };

                _port.Open();

                // The serial driver does not reliably carry a configuration through Open() — settings applied
                // to a closed port can be silently replaced by the driver's own defaults. HardwareController
                // hid this by reconfiguring on every transaction; anything that skipped that (its module
                // discovery scan) ended up probing at the wrong baud rate and reporting an empty bus. Applying
                // after the open is what actually sticks.
                Apply(Settings);

                LastError = null;
                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or ArgumentException or InvalidOperationException)
            {
                LastError = $"Could not open {Settings.PortName}: {Explain(e)}";
                _port?.Dispose();
                _port = null;

                return false;
            }
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            if (_port == null)
                return;

            try
            {
                if (_port.IsOpen)
                    _port.Close();
            }
            catch (IOException)
            {
                // Closing a port whose device has already been unplugged throws; nothing useful to do.
            }
            finally
            {
                _port.Dispose();
                _port = null;
            }
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

            try
            {
                Apply(settings);
                LastError = null;

                return true;
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
            {
                LastError = $"Could not apply {settings}: {e.Message}";
                return false;
            }
        }
    }

    public bool Transact(string frame, out string response)
    {
        lock (_gate)
        {
            response = string.Empty;

            if (_port is not { IsOpen: true })
            {
                LastError = "The port is not open.";
                return false;
            }

            try
            {
                // A previous transaction that timed out may have left a late reply sitting in the buffer.
                // Reading it as the answer to this command would desynchronize every transaction that follows,
                // so the buffer is cleared before each write rather than after a failure.
                _port.DiscardInBuffer();
                _port.Write(frame);

                response  = _port.ReadTo(Terminator);
                LastError = null;

                return true;
            }
            catch (TimeoutException)
            {
                LastError = $"No response within {Settings.ReadTimeoutMs} ms.";
                return false;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                LastError = $"Transaction failed on {Settings.PortName}: {e.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Turns a port failure into something diagnosable. .NET's own messages for a serial port are famously
    /// uninformative — "A device attached to the system is not functioning" says nothing about which of the
    /// several Win32 calls behind <see cref="SerialPort.Open"/> actually refused — so the underlying error
    /// number is surfaced along with what it usually means here. HardwareController logged the equivalent
    /// Win32 code for the same reason.
    /// </summary>
    private static string Explain(Exception e)
    {
        // A Win32 error surfaces as an HRESULT in the 0x8007xxxx facility; the low word is the error number.
        var win32 = (e.HResult & unchecked((int) 0xFFFF0000)) == unchecked((int) 0x80070000)
            ? e.HResult & 0xFFFF
            : 0;

        return win32 switch
        {
            2 => $"{e.Message} (Win32 2 — the port no longer exists; the adapter was probably unplugged.)",

            5 => $"{e.Message} (Win32 5, access denied — another program still holds this port.)",

            // Seen on a CH340 adapter whose port opens at the Win32 level and reports its settings correctly,
            // but rejects SetCommState even when handed back the values it just reported. Opening a port
            // always configures it, so the port becomes unusable to any .NET program while this persists.
            31 => $"{e.Message} (Win32 31 — the driver refused to configure the line. The adapter is hung or " +
                  "its driver rejects the configuration: replug it, or roll back the driver to an older version.)",

            _ => win32 != 0 ? $"{e.Message} (Win32 {win32}.)" : e.Message
        };
    }

    /// <summary>Pushes <paramref name="settings"/> onto the live port. Caller holds the lock.</summary>
    private void Apply(SerialPortSettings settings)
    {
        if (_port == null)
            return;

        _port.BaudRate     = settings.BaudRate;
        _port.Parity       = settings.Parity;
        _port.DataBits     = settings.DataBits;
        _port.StopBits     = settings.StopBits;
        _port.Handshake    = settings.Handshake;
        _port.ReadTimeout  = settings.ReadTimeoutMs;
        _port.WriteTimeout = settings.WriteTimeoutMs;
    }

    public void Dispose() => Close();
}
