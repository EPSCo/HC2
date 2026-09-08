using System;
using System.Threading;
using HC2.Core.Serial;

namespace HC2.Core.Dcon;

/// <summary>
/// Speaks Advantech's DCON ASCII protocol over an <see cref="ISerialTransport"/>: adds the terminator and the
/// optional checksum, sends, and hands back the bare response frame. Stateless apart from the transport and
/// the checksum mode — the module classes above it own addresses and interpretation.
/// </summary>
public sealed class DconClient
{
    private readonly ISerialTransport _transport;

    /// <param name="checksum">Whether the bus runs with checksums. Every module on one line must agree, and
    /// this is normally off. See <see cref="DconChecksum"/> — the algorithm is not yet hardware-verified.</param>
    public DconClient(ISerialTransport transport, bool checksum = false)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Checksum   = checksum;
    }

    public bool Checksum { get; set; }

    public ISerialTransport Transport => _transport;

    /// <summary>Description of the most recent failure, or null after a successful exchange.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Sends one command and returns the response frame with terminator and checksum removed.
    /// </summary>
    /// <returns>False if the module did not answer, or answered with a checksum that does not match.
    /// A <c>?AA</c> rejection is a real answer and returns true — inspect it with <see cref="DconResponse"/>.</returns>
    public bool Execute(string command, out string response)
    {
        response = string.Empty;

        var frame = Checksum
            ? command + DconChecksum.Compute(command) + SerialTransport.Terminator
            : command + SerialTransport.Terminator;

        if (!_transport.Transact(frame, out var raw))
        {
            LastError = _transport.LastError ?? "No response.";
            return false;
        }

        if (Checksum && !DconChecksum.TryStrip(raw, out raw))
        {
            LastError = $"Checksum mismatch on the response to '{command}'.";
            return false;
        }

        response  = raw;
        LastError = null;

        return true;
    }

    /// <summary>
    /// <see cref="Execute"/> with retries, for READ-ONLY commands only.
    /// </summary>
    /// <remarks>
    /// The first transaction after the port's baud rate changes has been observed on real hardware to get no
    /// reply even though the module is perfectly reachable; a few quick retries clear it. This is safe here
    /// only because the command does not change anything. Never wrap a write this way — a retry loop
    /// hammering a module during its post-write settle window actively prevents the write from completing.
    /// </remarks>
    public bool ExecuteWithRetry(string command, out string response, int attempts = 3, int delayMs = 300)
    {
        response = string.Empty;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (Execute(command, out response))
                return true;

            if (attempt < attempts)
                Thread.Sleep(delayMs);
        }

        return false;
    }

    /// <summary>
    /// Asks what is at <paramref name="address"/> using the identification command every discovery scan is
    /// built on.
    /// </summary>
    /// <returns>False when nothing answers, or when something answers with an identifier this app does not
    /// recognize — <paramref name="identifier"/> still carries the raw text in the latter case.</returns>
    public bool TryIdentify(int address, out ModuleModel model, out string identifier)
    {
        model      = default;
        identifier = string.Empty;

        if (!ExecuteWithRetry(DconCommands.Identify(address), out var response))
            return false;

        if (!DconResponse.IsAcknowledged(response))
        {
            LastError = $"Address {address:X2} answered '{response}'.";
            return false;
        }

        identifier = DconResponse.Payload(response);

        if (ModuleModelId.TryParse(identifier, out model))
            return true;

        LastError = $"Address {address:X2} reports an unrecognized module: '{identifier}'.";
        return false;
    }

    /// <summary>Whether anything at all answers at <paramref name="address"/>, regardless of what it is.</summary>
    public bool Responds(int address) =>
        ExecuteWithRetry(DconCommands.Identify(address), out var response) && DconResponse.IsAcknowledged(response);

    /// <summary>Reads every channel of an analog module in engineering units.</summary>
    public bool TryReadChannels(int address, out double[] values)
    {
        values = Array.Empty<double>();

        if (!Execute(DconCommands.ReadAllChannels(address), out var response))
            return false;

        if (DconResponse.TryParseEngineeringUnits(response, out values))
            return true;

        LastError = $"Address {address:X2} answered '{response}' to a channel read.";
        return false;
    }

    /// <summary>Reads one counter or encoder channel as a raw count.</summary>
    public bool TryReadCounter(int address, int channel, out long value)
    {
        value = 0;

        if (!Execute(DconCommands.ReadChannel(address, channel), out var response))
            return false;

        if (DconResponse.TryParseHexValue(response, out value))
            return true;

        LastError = $"Address {address:X2} channel {channel} answered '{response}'.";
        return false;
    }
}
