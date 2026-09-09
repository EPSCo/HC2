using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HC2.App.Mvvm;
using HC2.Core.Dcon;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

/// <summary>Runs a <see cref="ModuleFinder"/> sweep across every port and setting ticked in the ribbon.</summary>
public sealed class ModuleScanViewModel : ViewModelBase
{
    private readonly PortSettingsViewModel _settings;
    private readonly AsyncRelayCommand     _scan;
    private readonly RelayCommand          _cancel;

    private CancellationTokenSource? _cancellation;

    private int    _firstAddress = 0;
    private int    _lastAddress  = 255;
    private int    _probeTimeoutMs = 200;
    private bool   _isScanning;
    private double _progress;
    private string _status = "Tick a port, then scan for modules.";

    public ModuleScanViewModel(PortSettingsViewModel settings)
    {
        _settings = settings;
        _scan     = new AsyncRelayCommand(ScanAsync, CanScan);
        _cancel   = new RelayCommand(Cancel, () => IsScanning);

        _settings.SelectionChanged += OnSettingsChanged;
    }

    public ObservableCollection<DiscoveredModuleRow> Results { get; } = new();

    public ICommand ScanCommand   => _scan;
    public ICommand CancelCommand => _cancel;

    public string Headline
    {
        get
        {
            var ports = _settings.SelectedPorts;

            return ports.Count switch
            {
                0 => "Module scan",
                1 => $"Module scan — {ports[0]}",
                _ => $"Module scan — {ports.Count} ports"
            };
        }
    }

    public int FirstAddress
    {
        get => _firstAddress;
        set => SetProperty(ref _firstAddress, Clamp(value));
    }

    public int LastAddress
    {
        get => _lastAddress;
        set => SetProperty(ref _lastAddress, Clamp(value));
    }

    public int ProbeTimeoutMs
    {
        get => _probeTimeoutMs;
        set => SetProperty(ref _probeTimeoutMs, value < 20 ? 20 : value > 2000 ? 2000 : value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
                _cancel.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Completed fraction, 0 to 1, across every port in the sweep.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private bool CanScan() => _settings.HasPortSelected && _settings.IsSupportedProtocol;

    private async Task ScanAsync()
    {
        if (!_settings.IsSupportedProtocol)
        {
            Status = _settings.ProtocolWarning;
            return;
        }

        var ports = _settings.SelectedPorts;

        if (ports.Count == 0)
        {
            Status = "No port ticked.";
            return;
        }

        var options = new ModuleScanOptions
        {
            FirstAddress   = Math.Min(FirstAddress, LastAddress),
            LastAddress    = Math.Max(FirstAddress, LastAddress),
            BaudRates      = _settings.SelectedBaudRates,
            Formats        = _settings.SelectedFormats,
            ChecksumModes  = _settings.SelectedChecksums,
            ProbeTimeoutMs = ProbeTimeoutMs
        };

        Results.Clear();
        Progress      = 0;
        IsScanning    = true;
        _cancellation = new CancellationTokenSource();

        // Constructed here, on the UI thread, so reports marshal back to it.
        var progress = new Progress<ModuleScanProgress>(OnProgress);

        var perPort   = options.ProbeCount;
        var total     = perPort * ports.Count;
        var completed = 0;
        var found     = new List<DiscoveredModule>();
        var notes     = new List<string>();

        try
        {
            foreach (var port in ports)
            {
                _cancellation.Token.ThrowIfCancellationRequested();

                var settings = new SerialPortSettings
                {
                    PortName      = port,
                    BaudRate      = options.BaudRates.FirstOrDefault(),
                    ReadTimeoutMs = options.ProbeTimeoutMs
                };

                var opened = SerialTransportOpener.Open(settings);

                if (!opened.Opened)
                {
                    // One unusable port does not abandon the sweep; its probes are counted as done so the bar
                    // still reaches the end.
                    notes.Add(opened.Error ?? $"Could not open {port}.");
                    completed += perPort;

                    continue;
                }

                using (var transport = opened.Transport!)
                {
                    if (opened.Note != null)
                        notes.Add(opened.Note);

                    found.AddRange(await new ModuleFinder(transport)
                        .ScanAsync(options, progress, _cancellation.Token, completed, total));
                }

                completed += perPort;
            }

            // Results appear live as each probe reports, so the list is normally already complete here. The
            // rebuild is a safety net for a report that did not make it back to the UI thread.
            if (Results.Count != found.Count)
            {
                Results.Clear();

                foreach (var module in found)
                    Results.Add(new DiscoveredModuleRow(module));
            }

            Progress = 1;

            var scope   = ports.Count == 1 ? ports[0] : $"{ports.Count} ports";
            var outcome = found.Count switch
            {
                0 => $"No modules answered on {scope} across {total:N0} probes.",
                1 => $"1 module found on {scope}.",
                _ => $"{found.Count} modules found on {scope}."
            };

            Status = notes.Count == 0 ? outcome : $"{outcome} {string.Join(" ", notes.Distinct())}";
        }
        catch (OperationCanceledException)
        {
            Status = $"Scan cancelled. {Results.Count} module(s) found before stopping.";
        }
        catch (Exception e)
        {
            Status = $"Scan failed: {e.Message}";
        }
        finally
        {
            IsScanning = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    private void OnProgress(ModuleScanProgress report)
    {
        Progress = report.Fraction;

        if (report.Found != null)
            Results.Add(new DiscoveredModuleRow(report.Found));

        Status = $"{report.PortName} · {report.BaudRate:N0} bps · {report.Format.Label}" +
                 (report.Checksum ? " · checksum" : string.Empty) +
                 $" · address {report.Address:X2} — {report.Completed:N0} of {report.Total:N0}";
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        Status = "Cancelling…";
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(Headline));
        _scan.RaiseCanExecuteChanged();
    }

    private static int Clamp(int address) =>
        address < DconCommands.MinAddress ? DconCommands.MinAddress :
        address > DconCommands.MaxAddress ? DconCommands.MaxAddress : address;
}
