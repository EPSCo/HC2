using System.IO.Ports;

namespace HC2.Core.Serial;

/// <summary>Outcome of <see cref="SerialTransportOpener.Open"/>.</summary>
public sealed record SerialOpenResult
{
    /// <summary>The opened transport, or null when neither route could open the port.</summary>
    public ISerialTransport? Transport { get; init; }

    /// <summary>Why the port could not be opened. Null on success.</summary>
    public string? Error { get; init; }

    /// <summary>Something the caller should surface even though the port did open — null when there is nothing
    /// unusual to report.</summary>
    public string? Note { get; init; }

    public bool Opened => Transport != null;
}

/// <summary>
/// Opens a port, falling back to <see cref="Win32SerialTransport"/> when <see cref="SerialPort"/> cannot.
/// </summary>
/// <remarks>
/// <see cref="SerialPort"/> is tried first because it is the better-tested route and handles the normal case.
/// It fails outright on adapters whose driver refuses <c>SetCommState</c> — configuring the line is part of
/// how it opens one — so a second attempt goes through the Win32 API directly, which can keep such a port open
/// and use the configuration it already holds. See <see cref="Win32SerialTransport"/> for the case that
/// prompted this.
/// </remarks>
public static class SerialTransportOpener
{
    public static SerialOpenResult Open(SerialPortSettings settings)
    {
        var managed = new SerialTransport(settings);

        if (managed.Open())
            return new SerialOpenResult { Transport = managed };

        var managedError = managed.LastError;
        managed.Dispose();

        var native = new Win32SerialTransport(settings);

        if (!native.Open())
        {
            var nativeError = native.LastError;
            native.Dispose();

            return new SerialOpenResult { Error = managedError ?? nativeError };
        }

        var note = native.ConfigurationApplied
            ? $"{settings.PortName} would not open through the standard serial driver, but did open directly."
            : native.LastError;

        return new SerialOpenResult { Transport = native, Note = note };
    }
}
