using System;
using System.Linq;
using System.Threading.Tasks;
using HC2.App.Mvvm;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

/// <summary>
/// The shell: enumerates the machine's COM ports into the port ribbon, and owns the scan and live panels.
/// </summary>
/// <remarks>
/// It no longer keeps a port list or a selected port of its own. The ribbon's COM Port tab is the one place
/// ports are chosen, and it is multi-select — a single "selected port" could not represent two ticked ports.
/// The list refreshes itself: once at startup and again on every device-tree change, with no button to press.
/// </remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ISerialPortScanner _scanner;

    private bool   _isRefreshing;
    private string _status = "Ready.";

    public MainWindowViewModel(ISerialPortScanner scanner)
    {
        _scanner = scanner;
        Scan     = new ModuleScanViewModel(PortSettings);
        Live     = new LiveDataViewModel(Scan, PortSettings);
    }

    /// <summary>Ports, baud rates, checksum modes, framings and protocol — the search space for a scan.</summary>
    public PortSettingsViewModel PortSettings { get; } = new();

    /// <summary>Module discovery across everything ticked in the ribbon.</summary>
    public ModuleScanViewModel Scan { get; }

    /// <summary>Continuous channel reads of whatever the last scan found.</summary>
    public LiveDataViewModel Live { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        Status       = "Looking for COM ports...";

        try
        {
            // Availability is not probed: opening every port to see whether it is free asserts DTR/RTS on each,
            // and nothing displays the result now that the port list has gone.
            var found = await _scanner.ScanAsync(probeAvailability: false);

            PortSettings.SetAvailablePorts(found.Select(port =>
                (port.PortName, string.IsNullOrWhiteSpace(port.FriendlyName) ? null : port.FriendlyName)));

            Status = found.Count switch
            {
                0 => "No COM ports found.",
                1 => "1 COM port found.",
                _ => $"{found.Count} COM ports found."
            };
        }
        catch (Exception e)
        {
            Status = $"Could not list COM ports: {e.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Called by the window when Windows signals a device-tree change.</summary>
    public void OnDeviceChanged()
    {
        if (!IsRefreshing)
            _ = RefreshAsync();
    }
}
