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
    private DiscoveredModuleRow?     _selectedResult;

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

    /// <summary>
    /// The result row the user picked. Anything that talks to one module works from this, because a found
    /// module carries the port, rate, framing and checksum it actually answered under — which need not be what
    /// the ribbon currently shows, and need not match the other modules on the bus.
    /// </summary>
    public DiscoveredModuleRow? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

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

        var notes      = new List<string>();
        var transports = new List<ISerialTransport>();
        var found      = new List<DiscoveredModule>();

        try
        {
            // Every ticked port is opened once and stays open for the whole sweep. Address is the outer loop,
            // so ports are revisited at every address — opening and closing each time would dominate the scan
            // and toggle DTR/RTS on each module repeatedly.
            foreach (var port in ports)
            {
                var opened = SerialTransportOpener.Open(new SerialPortSettings
                {
                    PortName      = port,
                    BaudRate      = options.BaudRates.FirstOrDefault(),
                    ReadTimeoutMs = options.ProbeTimeoutMs
                });

                if (!opened.Opened)
                {
                    notes.Add(opened.Error ?? $"Could not open {port}.");
                    continue;
                }

                transports.Add(opened.Transport!);

                if (opened.Note != null)
                    notes.Add(opened.Note);
            }

            if (transports.Count == 0)
            {
                Status = notes.Count > 0 ? string.Join(" ", notes) : "No port could be opened.";
                return;
            }

            found.AddRange(await new ModuleFinder(transports).ScanAsync(options, progress, _cancellation.Token));

            // Results appear live as each probe reports, so the list is normally already complete here. The
            // rebuild is a safety net for a report that did not make it back to the UI thread.
            if (Results.Count != found.Count)
            {
                Results.Clear();

                foreach (var module in found)
                    Results.Add(new DiscoveredModuleRow(module));
            }

            Progress = 1;

            var scope   = transports.Count == 1 ? transports[0].Settings.PortName : $"{transports.Count} ports";
            var outcome = found.Count switch
            {
                0 => $"No modules answered on {scope} across {options.ProbeCount(transports.Count):N0} probes.",
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
            foreach (var transport in transports)
                transport.Dispose();

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
