using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using HC2.App.Mvvm;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ISerialPortScanner _scanner;

    private SerialPortInfo? _selectedPort;
    private bool            _isScanning;
    private bool            _probeAvailability;
    private bool            _autoRefresh = true;
    private string          _status      = "Ready.";

    /// <summary>Guards the two-way link between the port list selection and the port settings panel.</summary>
    private bool _syncingSelection;

    public MainWindowViewModel(ISerialPortScanner scanner)
    {
        _scanner       = scanner;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        Scan           = new ModuleScanViewModel(PortSettings);
        Live           = new LiveDataViewModel(Scan, PortSettings);

        PortSettings.PropertyChanged += OnPortSettingsChanged;
    }

    public ObservableCollection<SerialPortInfo> Ports { get; } = new();

    public ICommand RefreshCommand { get; }

    /// <summary>Port, baud rate, checksum and protocol, shared by everything that talks to the bus.</summary>
    public PortSettingsViewModel PortSettings { get; } = new();

    /// <summary>Module discovery on the configured port.</summary>
    public ModuleScanViewModel Scan { get; }

    /// <summary>Continuous channel reads of whatever the last scan found.</summary>
    public LiveDataViewModel Live { get; }

    public SerialPortInfo? SelectedPort
    {
        get => _selectedPort;
        set
        {
            if (!SetProperty(ref _selectedPort, value) || _syncingSelection)
                return;

            _syncingSelection     = true;
            PortSettings.PortName = value?.PortName;
            _syncingSelection     = false;
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    /// <summary>Open each port during the scan to report whether it is free. Off by default — see the scanner.</summary>
    public bool ProbeAvailability
    {
        get => _probeAvailability;
        set => SetProperty(ref _probeAvailability, value);
    }

    /// <summary>Re-scan when Windows reports a device arrival or removal.</summary>
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set => SetProperty(ref _autoRefresh, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public async Task RefreshAsync()
    {
        IsScanning = true;
        Status     = "Scanning...";

        try
        {
            var found    = await _scanner.ScanAsync(ProbeAvailability);
            var selected = SelectedPort?.PortName;

            Ports.Clear();

            foreach (var port in found)
                Ports.Add(port);

            // The settings panel offers the same ports as the list, and keeps its selection across a refresh
            // when that port is still there.
            PortSettings.SetAvailablePorts(found.Select(port => port.PortName));

            SelectedPort = Ports.FirstOrDefault(p => p.PortName.Equals(selected, StringComparison.OrdinalIgnoreCase))
                        ?? Ports.FirstOrDefault(p => p.PortName == PortSettings.PortName)
                        ?? Ports.FirstOrDefault();

            Status = found.Count switch
            {
                0 => "No COM ports found.",
                1 => "1 COM port found.",
                _ => $"{found.Count} COM ports found."
            };
        }
        catch (Exception e)
        {
            Status = $"Scan failed: {e.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Called by the window when Windows signals a device-tree change.</summary>
    public void OnDeviceChanged()
    {
        if (AutoRefresh && !IsScanning)
            _ = RefreshAsync();
    }

    /// <summary>Keeps the port list selection in step when the port is changed from the settings panel.</summary>
    private void OnPortSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PortSettingsViewModel.PortName) || _syncingSelection)
            return;

        _syncingSelection = true;
        SelectedPort      = Ports.FirstOrDefault(port => port.PortName == PortSettings.PortName);
        _syncingSelection = false;
    }
}
