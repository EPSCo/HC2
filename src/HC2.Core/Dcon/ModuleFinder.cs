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

    /// <summary>The rate it answered at — not necessarily the first rate the sweep tried.</summary>
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
/// The search space for a scan: every combination of baud rate, framing and checksum mode across an address
/// range.
/// </summary>
/// <remarks>
/// Each list widens the sweep multiplicatively, which is why they default to one entry each. A bus whose
/// settings are known should be scanned with exactly those; the lists exist for a bus whose settings are not.
/// </remarks>
public sealed record ModuleScanOptions
{
    public int FirstAddress { get; init; } = DconCommands.MinAddress;
    public int LastAddress  { get; init; } = DconCommands.MaxAddress;

    /// <summary>Rates to try. Empty means the rate the transport is already configured for.</summary>
    public IReadOnlyList<int> BaudRates { get; init; } = Array.Empty<int>();

    /// <summary>Framings to try. Empty means the framing the transport is already configured for.</summary>
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

    /// <summary>Total probes a scan with these options performs on one port.</summary>
    public int ProbeCount =>
        Math.Max(0, LastAddress - FirstAddress + 1)
        * Math.Max(1, BaudRates.Count)
        * Math.Max(1, Formats.Count)
        * Math.Max(1, ChecksumModes.Count);
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
/// Sweeps a range of module addresses with the <c>$AAM</c> identification command and reports what answers.
/// </summary>
/// <remarks>
/// Read-only: nothing in a scan writes to a module. The transport's original settings are restored when the
/// scan ends, including when it is cancelled or throws. One finder covers one port; scanning several means
/// running one per port.
/// </remarks>
public sealed class ModuleFinder
{
    private readonly ISerialTransport _transport;

    public ModuleFinder(ISerialTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<IReadOnlyList<DiscoveredModule>> ScanAsync(ModuleScanOptions              options,
                                                            IProgress<ModuleScanProgress>? progress          = null,
                                                            CancellationToken              cancellationToken = default,
                                                            int                            completedBefore   = 0,
                                                            int                            totalOverall      = 0)
        => Task.Run(() => Scan(options, progress, cancellationToken, completedBefore, totalOverall), cancellationToken);

    /// <param name="completedBefore">Probes already done on earlier ports, so progress spans a multi-port scan.</param>
    /// <param name="totalOverall">Probes across every port, or 0 to report this port's total alone.</param>
    private IReadOnlyList<DiscoveredModule> Scan(ModuleScanOptions              options,
                                                 IProgress<ModuleScanProgress>? progress,
                                                 CancellationToken              cancellationToken,
                                                 int                            completedBefore,
                                                 int                            totalOverall)
    {
        if (options.FirstAddress > options.LastAddress)
            throw new ArgumentException("The first address is above the last address.", nameof(options));

        var found    = new List<DiscoveredModule>();
        var original = _transport.Settings;

        var rates = options.BaudRates.Count > 0
            ? options.BaudRates.Distinct().OrderBy(rate => rate).ToArray()
            : new[] { original.BaudRate };

        var formats = options.Formats.Count > 0
            ? options.Formats.ToArray()
            : new[] { new SerialFormat { Parity = original.Parity, DataBits = original.DataBits, StopBits = original.StopBits } };

        var checksumModes = options.ChecksumModes.Count > 0 ? options.ChecksumModes.ToArray() : new[] { false };

        var total     = totalOverall > 0 ? totalOverall : options.ProbeCount;
        var completed = completedBefore;

        try
        {
            foreach (var rate in rates)
            foreach (var format in formats)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var wanted = original with
                {
                    BaudRate      = rate,
                    Parity        = format.Parity,
                    DataBits      = format.DataBits,
                    StopBits      = format.StopBits,
                    ReadTimeoutMs = options.ProbeTimeoutMs
                };

                // A combination the port will not accept is skipped rather than scanned at the wrong settings,
                // which would look like an empty bus instead of a configuration the adapter cannot reach.
                if (!_transport.Configure(wanted))
                {
                    completed += (options.LastAddress - options.FirstAddress + 1) * checksumModes.Length;
                    continue;
                }

                if (!_transport.IsOpen && !_transport.Open())
                    break;

                foreach (var checksum in checksumModes)
                {
                    var client = new DconClient(_transport, checksum);

                    // The first transaction after a settings change can go unanswered even when the bus is
                    // healthy, so the first address of each pass gets the read-only retry. Every address after
                    // it is probed once — retrying all 256 would triple a scan that is already dominated by
                    // waiting on silence.
                    var firstProbeOfPass = true;

                    for (var address = options.FirstAddress; address <= options.LastAddress; address++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var module = Probe(client, original.PortName, address, rate, format, checksum, firstProbeOfPass);
                        firstProbeOfPass = false;
                        completed++;

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
            _transport.Configure(original);
        }

        return found;
    }

    private static DiscoveredModule? Probe(DconClient  client,
                                            string      portName,
                                            int         address,
                                            int         rate,
                                            SerialFormat format,
                                            bool        checksum,
                                            bool        withRetry)
    {
        var command  = DconCommands.Identify(address);
        var response = string.Empty;

        var answered = withRetry
            ? client.ExecuteWithRetry(command, out response)
            : client.Execute(command, out response);

        if (!answered || !DconResponse.IsAcknowledged(response))
            return null;

        var identifier = DconResponse.Payload(response);

        // Something answered. Record it even when the identifier is unfamiliar — an unknown module on the bus
        // is worth showing rather than silently skipping, since it still occupies the address.
        var recognized = ModuleModelId.TryParse(identifier, out var model);

        return new DiscoveredModule
        {
            PortName   = portName,
            Address    = address,
            Model      = recognized ? model : null,
            Identifier = identifier,
            BaudRate   = rate,
            Format     = format,
            Checksum   = checksum
        };
    }
}
