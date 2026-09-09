using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using HC2.App.Mvvm;
using HC2.Core.Dcon;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

/// <summary>Drives a <see cref="ModuleFinder"/> sweep over the configured port.</summary>
public sealed class ModuleScanViewModel : ViewModelBase
{
    private readonly PortSettingsViewModel _settings;
    private readonly AsyncRelayCommand     _scan;
    private readonly RelayCommand          _cancel;

    private CancellationTokenSource? _cancellation;

    private int    _firstAddress = 0;
    private int    _lastAddress  = 255;
    private bool   _allBaudRates;
    private int    _probeTimeoutMs = 200;
    private bool   _isScanning;
    private double _progress;
    private string _status = "Select a port, then scan for modules.";

    public ModuleScanViewModel(PortSettingsViewModel settings)
    {
        _settings = settings;
        _scan     = new AsyncRelayCommand(ScanAsync, CanScan);
        _cancel   = new RelayCommand(Cancel, () => IsScanning);

        _settings.PropertyChanged += OnSettingsChanged;
        _settings.ProtocolChanged += (_, _) => _scan.RaiseCanExecuteChanged();
    }

    public ObservableCollection<DiscoveredModuleRow> Results { get; } = new();

    public ICommand ScanCommand   => _scan;
    public ICommand CancelCommand => _cancel;

    /// <summary>The port to sweep, owned by the port settings panel.</summary>
    public string? PortName => _settings.PortName;

    public string Headline => string.IsNullOrEmpty(PortName) ? "Module scan" : $"Module scan — {PortName}";

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

    /// <summary>
    /// Sweep every standard rate instead of the configured one, for a bus whose rate is unknown. Multiplies the
    /// scan by nine.
    /// </summary>
    public bool AllBaudRates
    {
        get => _allBaudRates;
        set => SetProperty(ref _allBaudRates, value);
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

    /// <summary>Completed fraction, 0 to 1.</summary>
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

    private bool CanScan() => _settings.IsPortSelected && _settings.IsSupportedProtocol;

    private async Task ScanAsync()
    {
        var portName = PortName;

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

        var options = new ModuleScanOptions
        {
            FirstAddress   = Math.Min(FirstAddress, LastAddress),
            LastAddress    = Math.Max(FirstAddress, LastAddress),
            BaudRates      = AllBaudRates ? _settings.BaudRateOptions : new[] { _settings.BaudRate },
            Checksum       = _settings.Checksum,
            ProbeTimeoutMs = ProbeTimeoutMs
        };

        Results.Clear();
        Progress      = 0;
        IsScanning    = true;
        _cancellation = new CancellationTokenSource();

        // Constructed here, on the UI thread, so reports marshal back to it.
        var progress = new Progress<ModuleScanProgress>(OnProgress);

        var settings = new SerialPortSettings
        {
            PortName      = portName!,
            BaudRate      = _settings.BaudRate,
            ReadTimeoutMs = options.ProbeTimeoutMs
        };

        var opened = SerialTransportOpener.Open(settings);

        if (!opened.Opened)
        {
            Status     = opened.Error ?? $"Could not open {portName}.";
            IsScanning = false;
            _cancellation.Dispose();
            _cancellation = null;

            return;
        }

        using var transport = opened.Transport!;

        try
        {
            var found = await new ModuleFinder(transport).ScanAsync(options, progress, _cancellation.Token);

            // Results appear live as each probe reports, so the list is normally already complete here. The
            // rebuild is a safety net for a report that did not make it back to the UI thread — cheap, and it
            // keeps the final list authoritative rather than merely probable.
            if (Results.Count != found.Count)
            {
                Results.Clear();

                foreach (var module in found)
                    Results.Add(new DiscoveredModuleRow(module));
            }

            Progress = 1;

            var outcome = found.Count switch
            {
                0 => $"No modules answered on {portName} across {options.ProbeCount:N0} probes.",
                1 => $"1 module found on {portName}.",
                _ => $"{found.Count} modules found on {portName}."
            };

            // A port that only opened through the fallback route still works, but the reason is worth keeping
            // in front of the user — it explains why changing the baud rate may not take effect.
            Status = opened.Note is null ? outcome : $"{outcome} {opened.Note}";
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

        Status = $"Probing {report.BaudRate:N0} bps, address {report.Address:X2} — " +
                 $"{report.Completed:N0} of {report.Total:N0}" +
                 (report.Checksum ? ", checksum on" : string.Empty);
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        Status = "Cancelling…";
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PortSettingsViewModel.PortName))
            return;

        RaisePropertyChanged(nameof(PortName));
        RaisePropertyChanged(nameof(Headline));
        _scan.RaiseCanExecuteChanged();
    }

    private static int Clamp(int address) =>
        address < DconCommands.MinAddress ? DconCommands.MinAddress :
        address > DconCommands.MaxAddress ? DconCommands.MaxAddress : address;
}
