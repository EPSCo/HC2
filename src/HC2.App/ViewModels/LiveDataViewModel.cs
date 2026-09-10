using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HC2.App.Mvvm;
using HC2.Core.Dcon;
using HC2.Core.Modules;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

/// <summary>
/// Polls the modules the last scan found and shows their channel values as they change.
/// </summary>
/// <remarks>
/// <para>
/// Each module is read under the settings it answered under during the scan, not under one setting for the
/// whole bus. Modules on the same line can run at different rates — this bench has an ADAM-4017P at 4800 and
/// an ICP-7017Z at 9600 on COM5 — so the port is reconfigured before each module's read.
/// </para>
/// <para>
/// Read-only: the loop issues nothing but <c>#AA</c> channel reads. All serial work happens on a background
/// task — a transaction blocks for as long as the timeout allows, which would freeze the window if it ran on
/// the dispatcher, and doing exactly that inside property setters is one of the faults recorded in the port
/// triage.
/// </para>
/// </remarks>
public sealed class LiveDataViewModel : ViewModelBase
{
    private readonly ModuleScanViewModel   _scan;
    private readonly PortSettingsViewModel _settings;
    private readonly RelayCommand          _start;
    private readonly List<string>          _labels = new();

    private CancellationTokenSource? _cancellation;

    private int    _intervalMs = 500;
    private bool   _isRunning;
    private int    _cycles;
    private int    _failures;
    private string _status = "Scan for modules, then start.";

    public LiveDataViewModel(ModuleScanViewModel scan, PortSettingsViewModel settings)
    {
        _scan     = scan;
        _settings = settings;
        _start    = new RelayCommand(StartOrStop, () => IsRunning || CanStart());

        // Start becomes possible the moment a scan produces something to read.
        _scan.Results.CollectionChanged += OnScanResultsChanged;
        _settings.SelectionChanged      += (_, _) => _start.RaiseCanExecuteChanged();
    }

    public ObservableCollection<ChannelReadingRow> Channels { get; } = new();

    /// <summary>Starts the polling loop, or stops the one running. One button drives both.</summary>
    public ICommand StartCommand => _start;

    /// <summary>What that button says right now.</summary>
    public string StartLabel => IsRunning ? "Stop" : "Start";

    /// <summary>Delay between polling rounds. The round itself takes as long as the modules take to answer.</summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set => SetProperty(ref _intervalMs, value < 50 ? 50 : value > 60000 ? 60000 : value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;

            RaisePropertyChanged(nameof(StartLabel));
            _start.RaiseCanExecuteChanged();
        }
    }

    public int Cycles
    {
        get => _cycles;
        private set => SetProperty(ref _cycles, value);
    }

    /// <summary>Reads that came back empty. A few on a long run are normal on a noisy bus; a rising count is not.</summary>
    public int Failures
    {
        get => _failures;
        private set => SetProperty(ref _failures, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private bool CanStart() =>
        !IsRunning && _settings.IsSupportedProtocol && _scan.Results.Any(row => row.IsRecognized);

    /// <remarks>
    /// Synchronous for the same reason as the scan button: an <see cref="AsyncRelayCommand"/> blocks
    /// re-entry while the loop runs, and the stop half has to work exactly then.
    /// </remarks>
    private void StartOrStop()
    {
        if (IsRunning)
            Stop();
        else
            _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var discovered = _scan.Results.Where(row => row.IsRecognized).Select(row => row.Module).ToList();

        if (discovered.Count == 0)
        {
            Status = "No recognized modules to read. Run a scan first.";
            return;
        }

        if (!_settings.IsSupportedProtocol)
        {
            Status = _settings.ProtocolWarning;
            return;
        }

        Cycles        = 0;
        Failures      = 0;
        IsRunning     = true;
        _cancellation = new CancellationTokenSource();

        // Built on the UI thread so reports marshal back to it.
        var progress   = new Progress<PollResult>(Apply);
        var transports = new List<ISerialTransport>();
        var entries    = new List<LiveModule>();
        var notes      = new List<string>();

        var byPort   = discovered.GroupBy(module => module.PortName).ToList();
        var showPort = byPort.Count > 1;

        try
        {
            foreach (var group in byPort)
            {
                // One transport per port, opened once and kept: the modules on it are read in turn and the
                // port is reconfigured between them, which is far cheaper than reopening.
                var opened = SerialTransportOpener.Open(SettingsFor(group.First()));

                if (!opened.Opened)
                {
                    notes.Add(opened.Error ?? $"Could not open {group.Key}.");
                    continue;
                }

                transports.Add(opened.Transport!);

                if (opened.Note != null)
                    notes.Add(opened.Note);

                foreach (var found in group)
                {
                    // A client per module, because checksum mode is per module just as the rate is.
                    var module = ModuleFactory.Create(new DconClient(opened.Transport!, found.Checksum), found);

                    if (module != null)
                        entries.Add(new LiveModule(module, opened.Transport!, SettingsFor(found)));
                }
            }

            if (entries.Count == 0)
            {
                Status = notes.Count > 0 ? string.Join(" ", notes) : "No module could be opened.";
                return;
            }

            // The ICP-7017Z has ten channels or twenty depending on how it is wired, and it is the module that
            // knows which. Ask before laying out rows — under its own settings, like every other exchange.
            foreach (var entry in entries.Where(e => e.Module is Icp7017Z))
            {
                entry.Transport.Configure(entry.Settings);
                ((Icp7017Z) entry.Module).TryReadWiringMode(out _);
            }

            BuildRows(entries, showPort);

            var scope = showPort ? $"{byPort.Count} ports" : byPort[0].Key;
            var rates = entries.Select(e => e.Settings.BaudRate).Distinct().OrderBy(rate => rate).ToArray();
            var mixed = rates.Length > 1 ? $" at {string.Join(" and ", rates.Select(r => $"{r:N0}"))} bps" : string.Empty;

            Status = notes.Count == 0
                ? $"Reading {entries.Count} module(s) on {scope}{mixed}."
                : $"Reading {entries.Count} module(s) on {scope}{mixed}. {string.Join(" ", notes.Distinct())}";

            await Task.Run(() => Poll(entries, progress, _cancellation.Token), _cancellation.Token);

            Status = $"Stopped after {Cycles:N0} cycle(s), {Failures:N0} failed read(s).";
        }
        catch (OperationCanceledException)
        {
            Status = $"Stopped after {Cycles:N0} cycle(s), {Failures:N0} failed read(s).";
        }
        catch (Exception e)
        {
            Status = $"Live read failed: {e.Message}";
        }
        finally
        {
            foreach (var transport in transports)
                transport.Dispose();

            IsRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>The connection one module answered under during the scan.</summary>
    private static SerialPortSettings SettingsFor(DiscoveredModule found) => new()
    {
        PortName      = found.PortName,
        BaudRate      = found.BaudRate,
        Parity        = found.Format.Parity,
        DataBits      = found.Format.DataBits,
        StopBits      = found.Format.StopBits,
        ReadTimeoutMs = 300
    };

    /// <summary>Runs on a background thread for as long as the token allows.</summary>
    private void Poll(IReadOnlyList<LiveModule>  entries,
                       IProgress<PollResult>     progress,
                       CancellationToken         cancellationToken)
    {
        var clock = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            clock.Restart();

            for (var index = 0; index < entries.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = entries[index];

                // Reapplied before every read: two modules sharing a port can want different rates, so
                // whatever the previous module left the line at is not necessarily right for this one.
                if (!entry.Transport.Configure(entry.Settings))
                {
                    progress.Report(new PollResult
                    {
                        ModuleIndex = index,
                        Error       = entry.Transport.LastError ?? "Could not apply this module's port settings."
                    });

                    continue;
                }

                progress.Report(entry.Module.TryReadChannels(out var values)
                    ? new PollResult { ModuleIndex = index, Values = values }
                    : new PollResult { ModuleIndex = index, Error  = entry.Module.LastError ?? "No response." });
            }

            progress.Report(new PollResult { CycleComplete = true, ElapsedMs = clock.ElapsedMilliseconds });

            // Interval is a gap between rounds, not a period: pacing to a fixed period would silently overlap
            // rounds whenever the bus is slower than the interval.
            if (cancellationToken.WaitHandle.WaitOne(IntervalMs))
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Apply(PollResult result)
    {
        if (result.CycleComplete)
        {
            Cycles++;
            Status = $"Reading — {Cycles:N0} cycle(s), last round {result.ElapsedMs} ms, {Failures:N0} failed read(s).";

            return;
        }

        var rows = Channels.Where(row => row.ModuleLabel == _labels[result.ModuleIndex]).ToList();

        if (result.Error != null)
        {
            Failures++;

            foreach (var row in rows)
                row.Error = result.Error;

            return;
        }

        for (var channel = 0; channel < rows.Count; channel++)
        {
            rows[channel].Error = null;
            rows[channel].Value = result.Values != null && channel < result.Values.Length
                ? result.Values[channel]
                : double.NaN;
        }
    }

    /// <summary>
    /// Lays out one row per channel, from the modules themselves rather than from the scan result — a module
    /// knows its real channel count once it has been asked, and the ICP-7017Z's depends on its wiring.
    /// </summary>
    private void BuildRows(IReadOnlyList<LiveModule> entries, bool showPort)
    {
        Channels.Clear();
        _labels.Clear();

        foreach (var entry in entries)
        {
            // The port is part of the label only when more than one is in play: two ports can carry the same
            // model at the same address, and the label is what pairs a reading with its row.
            var label = showPort
                ? $"{entry.Settings.PortName} · {entry.Module.Model} @ {entry.Module.Address}"
                : $"{entry.Module.Model} @ {entry.Module.Address}";

            _labels.Add(label);

            for (var channel = 0; channel < entry.Module.ChannelCount; channel++)
                Channels.Add(new ChannelReadingRow(label, entry.Module.Address, channel));
        }
    }

    private void Stop()
    {
        _cancellation?.Cancel();
        Status = "Stopping…";
    }

    private void OnScanResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _start.RaiseCanExecuteChanged();

    /// <summary>A module together with the line it is reached on and the settings it needs.</summary>
    private sealed class LiveModule
    {
        public LiveModule(AnalogInputModule module, ISerialTransport transport, SerialPortSettings settings)
        {
            Module    = module;
            Transport = transport;
            Settings  = settings;
        }

        public AnalogInputModule  Module    { get; }
        public ISerialTransport   Transport { get; }
        public SerialPortSettings Settings  { get; }
    }

    /// <summary>One module's reading, or the end of a round.</summary>
    private sealed class PollResult
    {
        public int       ModuleIndex   { get; set; }
        public double[]? Values        { get; set; }
        public string?   Error         { get; set; }
        public bool      CycleComplete { get; set; }
        public long      ElapsedMs     { get; set; }
    }
}
