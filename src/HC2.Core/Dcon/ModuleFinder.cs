using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HC2.Core.Serial;

namespace HC2.Core.Dcon;

/// <summary>One module located by a scan, described by the settings it actually answered under.</summary>
public sealed record DiscoveredModule
{
    /// <summary>The port it answered on. A scan can cover several.</summary>
    public string       PortName   { get; init; } = string.Empty;

    public int          Address    { get; init; }

    /// <summary>Null when something answered but reported an identifier this app does not know.</summary>
    public ModuleModel? Model      { get; init; }

    /// <summary>Raw identification text the module returned, e.g. <c>4017P</c>.</summary>
    public string       Identifier { get; init; } = string.Empty;

    /// <summary>The rate it answered at.</summary>
    public int          BaudRate   { get; init; }

    /// <summary>The framing it answered under.</summary>
    public SerialFormat Format     { get; init; } = SerialFormat.Default;

    /// <summary>Whether it answered with checksums enabled.</summary>
    public bool         Checksum   { get; init; }

    public bool IsRecognized => Model.HasValue;

    public int ChannelCount => Model.HasValue ? ModuleModelId.ChannelCount(Model.Value) : 0;

    public override string ToString() =>
        $"{(Model.HasValue ? Model.Value.ToString() : Identifier)} @ {Address:X2} " +
        $"({PortName}, {BaudRate} bps, {Format.Label})";
}

/// <summary>
/// The search space for a scan: every combination of baud rate, framing and checksum mode, tried at each
/// address in a range.
/// </summary>
public sealed record ModuleScanOptions
{
    public int FirstAddress { get; init; } = DconCommands.MinAddress;
    public int LastAddress  { get; init; } = DconCommands.MaxAddress;

    /// <summary>Rates to try. Empty means whatever each transport is already configured for.</summary>
    public IReadOnlyList<int> BaudRates { get; init; } = Array.Empty<int>();

    /// <summary>Framings to try. Empty means whatever each transport is already configured for.</summary>
    public IReadOnlyList<SerialFormat> Formats { get; init; } = Array.Empty<SerialFormat>();

    /// <summary>
    /// Checksum modes to try. Defaults to off alone; HC2's checksum implementation is not yet hardware-verified
    /// (see <see cref="DconChecksum"/>), so a hit with it on is worth double-checking.
    /// </summary>
    public IReadOnlyList<bool> ChecksumModes { get; init; } = new[] { false };

    /// <summary>
    /// How long to wait for each address to answer. Deliberately shorter than a working read timeout — most
    /// addresses on any real bus are empty, and the whole scan is paced by how long silence takes to confirm.
    /// </summary>
    public int ProbeTimeoutMs { get; init; } = 200;

    /// <summary>Stop as soon as one module answers, for a quick "is anything out there" check.</summary>
    public bool StopAtFirstMatch { get; init; }

    /// <summary>Probes performed per address, across every ticked port.</summary>
    public int CombinationsPerAddress(int portCount) =>
        Math.Max(1, portCount)
        * Math.Max(1, BaudRates.Count)
        * Math.Max(1, ChecksumModes.Count)
        * Math.Max(1, Formats.Count);

    public int AddressCount => Math.Max(0, LastAddress - FirstAddress + 1);

    public int ProbeCount(int portCount) => AddressCount * CombinationsPerAddress(portCount);
}

/// <summary>Progress for one completed probe.</summary>
public sealed record ModuleScanProgress
{
    public int          Completed { get; init; }
    public int          Total     { get; init; }
    public string       PortName  { get; init; } = string.Empty;
    public int          Address   { get; init; }
    public int          BaudRate  { get; init; }
    public SerialFormat Format    { get; init; } = SerialFormat.Default;
    public bool         Checksum  { get; init; }

    /// <summary>Set only on the probe that found something.</summary>
    public DiscoveredModule? Found { get; init; }

    public double Fraction => Total <= 0 ? 0 : (double) Completed / Total;
}

/// <summary>
/// Sweeps module addresses with the <c>$AAM</c> identification command across every port and setting given,
/// and reports what answers.
/// </summary>
/// <remarks>
/// <para>
/// Address is the outer loop: every selected combination of port, baud rate, checksum mode and framing is
/// tried at address 1, then all of them at address 2, and so on. That order finds a module at a low address
/// quickly whatever settings it uses, instead of walking all 256 addresses at one setting before trying the
/// next — which is what matters when the modules are at low addresses and the bus settings are unknown.
/// </para>
/// <para>
/// Read-only: nothing in a scan writes to a module. Transports are supplied already open and are left open;
/// each has its original settings restored when the scan ends, including when it is cancelled or throws.
/// </para>
/// </remarks>
public sealed class ModuleFinder
{
    private readonly IReadOnlyList<ISerialTransport> _transports;

    public ModuleFinder(params ISerialTransport[] transports)
        : this((IReadOnlyList<ISerialTransport>) transports)
    {
    }

    public ModuleFinder(IReadOnlyList<ISerialTransport> transports)
    {
        _transports = transports ?? throw new ArgumentNullException(nameof(transports));

        if (_transports.Count == 0)
            throw new ArgumentException("At least one transport is needed.", nameof(transports));
    }

    public Task<IReadOnlyList<DiscoveredModule>> ScanAsync(ModuleScanOptions              options,
                                                            IProgress<ModuleScanProgress>? progress          = null,
                                                            CancellationToken              cancellationToken = default)
        => Task.Run(() => Scan(options, progress, cancellationToken), cancellationToken);

    private IReadOnlyList<DiscoveredModule> Scan(ModuleScanOptions              options,
                                                 IProgress<ModuleScanProgress>? progress,
                                                 CancellationToken              cancellationToken)
    {
        if (options.FirstAddress > options.LastAddress)
            throw new ArgumentException("The first address is above the last address.", nameof(options));

        var found     = new List<DiscoveredModule>();
        var originals = _transports.ToDictionary(transport => transport, transport => transport.Settings);

        var rates = options.BaudRates.Count > 0
            ? options.BaudRates.Distinct().OrderBy(rate => rate).ToArray()
            : Array.Empty<int>();

        var formats       = options.Formats.Count > 0 ? options.Formats.ToArray() : Array.Empty<SerialFormat>();
        var checksumModes = options.ChecksumModes.Count > 0 ? options.ChecksumModes.ToArray() : new[] { false };

        var total     = options.ProbeCount(_transports.Count);
        var completed = 0;

        try
        {
            for (var address = options.FirstAddress; address <= options.LastAddress; address++)
            {
                foreach (var transport in _transports)
                {
                    var original = originals[transport];

                    foreach (var rate in rates.Length > 0 ? rates : new[] { original.BaudRate })
                    foreach (var checksum in checksumModes)
                    foreach (var format in formats.Length > 0
                                 ? formats
                                 : new[] { new SerialFormat { Parity = original.Parity, DataBits = original.DataBits, StopBits = original.StopBits } })
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        completed++;

                        var module = ProbeOnce(transport, original, address, rate, format, checksum);

                        if (module != null)
                            found.Add(module);

                        progress?.Report(new ModuleScanProgress
                        {
                            Completed = completed,
                            Total     = total,
                            PortName  = original.PortName,
                            Address   = address,
                            BaudRate  = rate,
                            Format    = format,
                            Checksum  = checksum,
                            Found     = module
                        });

                        if (module != null && options.StopAtFirstMatch)
                            return found;
                    }
                }
            }
        }
        finally
        {
            foreach (var pair in originals)
                pair.Key.Configure(pair.Value);
        }

        return found;
    }

    private static DiscoveredModule? ProbeOnce(ISerialTransport   transport,
                                                SerialPortSettings original,
                                                int                address,
                                                int                rate,
                                                SerialFormat       format,
                                                bool               checksum)
    {
        var wanted = original with
        {
            BaudRate      = rate,
            Parity        = format.Parity,
            DataBits      = format.DataBits,
            StopBits      = format.StopBits,
            ReadTimeoutMs = transport.Settings.ReadTimeoutMs
        };

        // A combination the port will not accept is reported as a miss rather than probed at the wrong
        // settings, which would look like an empty address instead of a configuration the adapter cannot reach.
        if (!transport.Configure(wanted) || (!transport.IsOpen && !transport.Open()))
            return null;

        var client   = new DconClient(transport, checksum);
        var response = string.Empty;

        if (!client.Execute(DconCommands.Identify(address), out response)
            || !DconResponse.IsAcknowledged(response))
            return null;

        var identifier = DconResponse.Payload(response);

        // Something answered. Record it even when the identifier is unfamiliar — an unknown module on the bus
        // is worth showing rather than silently skipping, since it still occupies the address.
        var recognized = ModuleModelId.TryParse(identifier, out var model);

        return new DiscoveredModule
        {
            PortName   = original.PortName,
            Address    = address,
            Model      = recognized ? model : null,
            Identifier = identifier,
            BaudRate   = rate,
            Format     = format,
            Checksum   = checksum
        };
    }
}
