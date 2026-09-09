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
/// Polls the modules found by the last scan and shows their channel values as they change.
/// </summary>
/// <remarks>
/// Read-only: the loop issues nothing but <c>#AA</c> channel reads. All serial work happens on a background
/// task — a transaction blocks for as long as the timeout allows, which would freeze the window if it ran on
/// the dispatcher, and doing exactly that inside property setters is one of the faults recorded in the port
/// triage.
/// </remarks>
public sealed class LiveDataViewModel : ViewModelBase
{
    private readonly ModuleScanViewModel   _scan;
    private readonly PortSettingsViewModel _settings;
    private readonly AsyncRelayCommand     _start;
    private readonly RelayCommand          _stop;

    private CancellationTokenSource? _cancellation;

    private int     _intervalMs = 500;
    private bool    _isRunning;
    private int     _cycles;
    private int     _failures;
    private string  _status = "Scan for modules, then start.";

    public LiveDataViewModel(ModuleScanViewModel scan, PortSettingsViewModel settings)
    {
        _scan     = scan;
        _settings = settings;
        _start    = new AsyncRelayCommand(RunAsync, CanStart);
        _stop     = new RelayCommand(Stop, () => IsRunning);

        // Start becomes possible the moment a scan produces something to read.
        _scan.Results.CollectionChanged += OnScanResultsChanged;
        _settings.ProtocolChanged       += (_, _) => _start.RaiseCanExecuteChanged();
    }

    public ObservableCollection<ChannelReadingRow> Channels { get; } = new();

    public ICommand StartCommand => _start;
    public ICommand StopCommand  => _stop;

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
            if (SetProperty(ref _isRunning, value))
                _stop.RaiseCanExecuteChanged();
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

    private async Task RunAsync()
    {
        var discovered = _scan.Results.Where(row => row.IsRecognized).Select(row => row.Module).ToList();

        if (discovered.Count == 0)
        {
            Status = "No recognized modules to read. Run a scan first.";
            return;
        }

        var portName = _settings.PortName;

        if (string.IsNullOrEmpty(portName))
        {
            Status = "No port selected.";
            return;
        }

        if (!_settings.IsSupportedProtocol)
        {
            Status = _settings.ProtocolWarning;
            return;
        }

        var settings = new SerialPortSettings
        {
            PortName      = portName!,
            BaudRate      = discovered[0].BaudRate,
            ReadTimeoutMs = 300
        };

        var opened = SerialTransportOpener.Open(settings);

        if (!opened.Opened)
        {
            Status = opened.Error ?? $"Could not open {portName}.";
            return;
        }

        Cycles     = 0;
        Failures   = 0;
        IsRunning  = true;
        _cancellation = new CancellationTokenSource();

        // Built on the UI thread so reports marshal back to it.
        var progress = new Progress<PollResult>(Apply);

        using var transport = opened.Transport!;

        try
        {
            // Baud rate and checksum come from what the modules actually answered with during the scan, not
            // from the settings panel: if a multi-rate sweep found them somewhere other than the configured
            // rate, that discovered rate is the one that demonstrably works.
            var client  = new DconClient(transport, discovered[0].Checksum);
            var modules = discovered.Select(d => ModuleFactory.Create(client, d))
                                    .Where(module => module != null)
                                    .Select(module => module!)
                                    .ToList();

            // The ICP-7017Z has ten channels or twenty depending on how it is wired, and it is the module that
            // knows which. Ask before laying out rows, or the grid shows ten channels that do not exist.
            foreach (var module in modules.OfType<Icp7017Z>())
                module.TryReadWiringMode(out _);

            BuildRows(modules);

            Status = opened.Note is null
                ? $"Reading {modules.Count} module(s) on {portName}."
                : $"Reading {modules.Count} module(s) on {portName}. {opened.Note}";

            await Task.Run(() => Poll(modules, progress, _cancellation.Token), _cancellation.Token);

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
            IsRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>Runs on a background thread for as long as the token allows.</summary>
    private void Poll(IReadOnlyList<AnalogInputModule>  modules,
                       IProgress<PollResult>            progress,
                       CancellationToken                cancellationToken)
    {
        var clock = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            clock.Restart();

            for (var index = 0; index < modules.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var module = modules[index];

                progress.Report(module.TryReadChannels(out var values)
                    ? new PollResult { ModuleIndex = index, Values = values }
                    : new PollResult { ModuleIndex = index, Error  = module.LastError ?? "No response." });
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
    private void BuildRows(IReadOnlyList<AnalogInputModule> modules)
    {
        Channels.Clear();
        _labels.Clear();

        foreach (var module in modules)
        {
            var label = $"{module.Model} @ {module.Address:X2}";
            _labels.Add(label);

            for (var channel = 0; channel < module.ChannelCount; channel++)
                Channels.Add(new ChannelReadingRow(label, module.Address, channel));
        }
    }

    private void Stop()
    {
        _cancellation?.Cancel();
        Status = "Stopping…";
    }

    private void OnScanResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        _start.RaiseCanExecuteChanged();

    private readonly List<string> _labels = new();

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
