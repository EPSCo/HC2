using System;

namespace HC2.Core.Serial;

/// <summary>
/// A serial line that carries one request/response frame at a time. Implementations serialize every
/// <see cref="Transact"/> call — an RS-485 bus is half duplex, so two overlapping transactions corrupt each
/// other rather than merely interleaving.
/// </summary>
public interface ISerialTransport : IDisposable
{
    SerialPortSettings Settings { get; }

    bool IsOpen { get; }

    /// <summary>Description of the most recent failure, or null if the last operation succeeded.</summary>
    string? LastError { get; }

    /// <summary>Opens the port and applies <see cref="Settings"/>. Returns false rather than throwing.</summary>
    bool Open();

    void Close();

    /// <summary>
    /// Replaces <see cref="Settings"/> and applies them to the live port. Safe to call while open — changing
    /// baud rate mid-session is normal when talking to a module in INIT mode.
    /// </summary>
    bool Configure(SerialPortSettings settings);

    /// <summary>
    /// Writes <paramref name="frame"/> verbatim and reads back everything up to the next carriage return.
    /// </summary>
    /// <param name="frame">The complete frame to send, terminator included.</param>
    /// <param name="response">The response with its terminator stripped; empty on failure.</param>
    /// <returns>False on timeout or port error — see <see cref="LastError"/>.</returns>
    bool Transact(string frame, out string response);
}
