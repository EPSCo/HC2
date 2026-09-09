using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HC2.Core.Serial;

namespace HC2.Core.Dcon;

/// <summary>One module located by a scan.</summary>
public sealed record DiscoveredModule
{
    public int          Address    { get; init; }

    /// <summary>Null when something answered but reported an identifier this app does not know.</summary>
    public ModuleModel? Model      { get; init; }

    /// <summary>Raw identification text the module returned, e.g. <c>4017P</c>.</summary>
    public string       Identifier { get; init; } = string.Empty;

    /// <summary>The rate the module answered at — not necessarily the rate the port started on.</summary>
    public int          BaudRate   { get; init; }

    /// <summary>Whether the module answered on a checksum-enabled bus.</summary>
    public bool         Checksum   { get; init; }

    public bool IsRecognized => Model.HasValue;

    public int ChannelCount => Model.HasValue ? ModuleModelId.ChannelCount(Model.Value) : 0;

    public override string ToString() =>
        $"{(Model.HasValue ? Model.Value.ToString() : Identifier)} @ {Address:X2} ({BaudRate} bps)";
}

/// <summary>What a scan should cover.</summary>
public sealed record ModuleScanOptions
{
    public int FirstAddress { get; init; } = DconCommands.MinAddress;
    public int LastAddress  { get; init; } = DconCommands.MaxAddress;

    /// <summary>
    /// Rates to try. Empty means the rate the transport is already configured for. Every extra rate multiplies
    /// the scan's duration, so widen this only when the bus configuration is genuinely unknown.
    /// </summary>
    public IReadOnlyList<int> BaudRates { get; init; } = Array.Empty<int>();

    /// <summary>Whether the bus runs with checksums. Ignored when <see cref="TryChecksumBus"/> sweeps both.</summary>
    public bool Checksum { get; init; }

    /// <summary>
    /// Sweep with checksums both off and on instead of using <see cref="Checksum"/>, for a bus whose setting is
    /// unknown. Doubles the scan, and HC2's checksum implementation is not yet hardware-verified (see
    /// <see cref="DconChecksum"/>).
    /// </summary>
    public bool TryChecksumBus { get; init; }

    /// <summary>
    /// How long to wait for each address to answer. Deliberately shorter than a working read timeout — most
    /// addresses on any real bus are empty, and the whole scan is paced by how long silence takes to confirm.
    /// </summary>
    public int ProbeTimeoutMs { get; init; } = 200;

    /// <summary>Stop as soon as one module answers, for a quick "is anything out there" check.</summary>
    public bool StopAtFirstMatch { get; init; }

    /// <summary>Total probes a scan with these options will perform.</summary>
    public int ProbeCount =>
        Math.Max(0, LastAddress - FirstAddress + 1)
        * Math.Max(1, BaudRates.Count)
        * (TryChecksumBus ? 2 : 1);
}

/// <summary>Progress for one completed probe.</summary>
public sealed record ModuleScanProgress
{
    public int  Completed { get; init; }
    public int  Total     { get; init; }
    public int  Address   { get; init; }
    public int  BaudRate  { get; init; }
    public bool Checksum  { get; init; }

    /// <summary>Set only on the probe that found something.</summary>
    public DiscoveredModule? Found { get; init; }

    public double Fraction => Total <= 0 ? 0 : (double) Completed / Total;
}

/// <summary>
/// Sweeps a range of module addresses with the <c>$AAM</c> identification command and reports what answers.
/// </summary>
/// <remarks>
/// Read-only: nothing in a scan writes to a module. The transport's original settings are restored when the
/// scan ends, including when it is cancelled or throws.
/// </remarks>
public sealed class ModuleFinder
{
    private readonly ISerialTransport _transport;

    public ModuleFinder(ISerialTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<IReadOnlyList<DiscoveredModule>> ScanAsync(ModuleScanOptions             options,
                                                            IProgress<ModuleScanProgress>? progress          = null,
                                                            CancellationToken              cancellationToken = default)
        => Task.Run(() => Scan(options, progress, cancellationToken), cancellationToken);

    private IReadOnlyList<DiscoveredModule> Scan(ModuleScanOptions             options,
                                                 IProgress<ModuleScanProgress>? progress,
                                                 CancellationToken              cancellationToken)
    {
        if (options.FirstAddress > options.LastAddress)
            throw new ArgumentException("The first address is above the last address.", nameof(options));

        var found    = new List<DiscoveredModule>();
        var original = _transport.Settings;

        var rates = options.BaudRates.Count > 0
            ? options.BaudRates.Distinct().OrderBy(rate => rate).ToArray()
            : new[] { original.BaudRate };

        var checksumModes = options.TryChecksumBus ? new[] { false, true } : new[] { options.Checksum };

        var total     = options.ProbeCount;
        var completed = 0;

        try
        {
            foreach (var rate in rates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_transport.Configure(original with { BaudRate = rate, ReadTimeoutMs = options.ProbeTimeoutMs }))
                    continue;

                if (!_transport.IsOpen && !_transport.Open())
                    break;

                foreach (var checksum in checksumModes)
                {
                    var client = new DconClient(_transport, checksum);

                    // The first transaction after a rate change can go unanswered even when the bus is
                    // healthy, so the first address of each pass gets the read-only retry. Every address after
                    // it is probed once — retrying all 256 would triple a scan that is already dominated by
                    // waiting on silence.
                    var firstProbeOfPass = true;

                    for (var address = options.FirstAddress; address <= options.LastAddress; address++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var module = Probe(client, address, rate, checksum, firstProbeOfPass);
                        firstProbeOfPass = false;
                        completed++;

                        if (module != null)
                            found.Add(module);

                        progress?.Report(new ModuleScanProgress
                        {
                            Completed = completed,
                            Total     = total,
                            Address   = address,
                            BaudRate  = rate,
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

    private static DiscoveredModule? Probe(DconClient client, int address, int rate, bool checksum, bool withRetry)
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
            Address    = address,
            Model      = recognized ? model : null,
            Identifier = identifier,
            BaudRate   = rate,
            Checksum   = checksum
        };
    }
}
