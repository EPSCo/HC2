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

/// <summary>Drives a <see cref="ModuleFinder"/> sweep over the port selected in the port list.</summary>
public sealed class ModuleScanViewModel : ViewModelBase
{
    private readonly AsyncRelayCommand _scan;
    private readonly RelayCommand      _cancel;

    private CancellationTokenSource? _cancellation;

    private string? _portName;
    private int     _firstAddress = 0;
    private int     _lastAddress  = 255;
    // 4800, not the modules' 9600 factory default: a bus that has been commissioned runs at whatever it was
    // configured to, and 4800 is the rate this equipment is deployed at — HardwareController hard-coded the
    // same assumption. Scanning at the factory rate would miss every module on a working bus.
    private int     _baudRate     = BaudRateCodes.DefaultBitsPerSecond;
    private bool    _allBaudRates;
    private bool    _tryChecksum;
    private int     _probeTimeoutMs = 200;
    private bool    _isScanning;
    private double  _progress;
    private string  _status = "Select a port, then scan for modules.";

    public ModuleScanViewModel()
    {
        _scan   = new AsyncRelayCommand(ScanAsync, () => !string.IsNullOrEmpty(PortName));
        _cancel = new RelayCommand(Cancel, () => IsScanning);
    }

    public ObservableCollection<DiscoveredModuleRow> Results { get; } = new();

    public IReadOnlyList<int> BaudRateOptions { get; } =
        BaudRateCodes.All.Select(entry => entry.BitsPerSecond).ToArray();

    public ICommand ScanCommand   => _scan;
    public ICommand CancelCommand => _cancel;

    /// <summary>Set by the shell whenever the port selection changes.</summary>
    public string? PortName
    {
        get => _portName;
        set
        {
            if (SetProperty(ref _portName, value))
            {
                _scan.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(Headline));
            }
        }
    }

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

    public int BaudRate
    {
        get => _baudRate;
        set => SetProperty(ref _baudRate, value);
    }

    /// <summary>Sweep every standard rate instead of just <see cref="BaudRate"/>. Multiplies the scan by nine.</summary>
    public bool AllBaudRates
    {
        get => _allBaudRates;
        set => SetProperty(ref _allBaudRates, value);
    }

    /// <summary>
    /// Also sweep with checksums enabled. HC2's checksum is not yet hardware-verified, so a hit here is a
    /// result worth double-checking rather than trusting outright.
    /// </summary>
    public bool TryChecksum
    {
        get => _tryChecksum;
        set => SetProperty(ref _tryChecksum, value);
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

    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(PortName))
            return;

        var options = new ModuleScanOptions
        {
            FirstAddress   = Math.Min(FirstAddress, LastAddress),
            LastAddress    = Math.Max(FirstAddress, LastAddress),
            BaudRates      = AllBaudRates ? BaudRateOptions : new[] { BaudRate },
            TryChecksumBus = TryChecksum,
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
            PortName      = PortName!,
            BaudRate      = BaudRate,
            ReadTimeoutMs = options.ProbeTimeoutMs
        };

        using var transport = new SerialTransport(settings);

        try
        {
            if (!transport.Open())
            {
                Status = transport.LastError ?? $"Could not open {PortName}.";
                return;
            }

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
            Status = found.Count switch
            {
                0 => $"No modules answered on {PortName} across {options.ProbeCount:N0} probes.",
                1 => $"1 module found on {PortName}.",
                _ => $"{found.Count} modules found on {PortName}."
            };
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
                 (report.Checksum ? ", checksum bus" : string.Empty);
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
        Status = "Cancelling…";
    }

    private static int Clamp(int address) =>
        address < DconCommands.MinAddress ? DconCommands.MinAddress :
        address > DconCommands.MaxAddress ? DconCommands.MaxAddress : address;
}
