using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using HC2.App.Mvvm;
using HC2.Core;
using HC2.Core.Dcon;

namespace HC2.App.ViewModels;

/// <summary>
/// The settings every conversation on the bus shares: which port, how fast, whether frames carry a checksum,
/// and which protocol is spoken.
/// </summary>
/// <remarks>
/// One object rather than a copy per feature, because these are properties of the wire, not of scanning or of
/// reading: a scan and a live read that disagreed about the baud rate would simply both be wrong.
/// </remarks>
public sealed class PortSettingsViewModel : ViewModelBase
{
    private string?     _portName;
    private int         _baudRate = BaudRateCodes.DefaultBitsPerSecond;
    private bool        _checksum;
    private BusProtocol _protocol = BusProtocol.DconAscii;

    /// <summary>Ports currently present, kept in step with the port list.</summary>
    public ObservableCollection<string> AvailablePorts { get; } = new();

    public IReadOnlyList<int> BaudRateOptions { get; } =
        BaudRateCodes.All.Select(entry => entry.BitsPerSecond).ToArray();

    public IReadOnlyList<BusProtocol> ProtocolOptions { get; } =
        new[] { BusProtocol.DconAscii, BusProtocol.ModbusRtu };

    public string? PortName
    {
        get => _portName;
        set
        {
            if (SetProperty(ref _portName, value))
                RaisePropertyChanged(nameof(IsPortSelected));
        }
    }

    public int BaudRate
    {
        get => _baudRate;
        set => SetProperty(ref _baudRate, value);
    }

    /// <summary>
    /// Whether frames carry the two-character checksum. Every module on a line must agree with this, and HC2's
    /// checksum is not yet confirmed against hardware — see <c>DconChecksum</c>.
    /// </summary>
    public bool Checksum
    {
        get => _checksum;
        set => SetProperty(ref _checksum, value);
    }

    public BusProtocol Protocol
    {
        get => _protocol;
        set
        {
            if (SetProperty(ref _protocol, value))
            {
                RaisePropertyChanged(nameof(IsSupportedProtocol));
                RaisePropertyChanged(nameof(ProtocolWarning));
                ProtocolChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsPortSelected => !string.IsNullOrEmpty(PortName);

    /// <summary>False for a protocol HC2 cannot actually speak, which gates every operation.</summary>
    public bool IsSupportedProtocol => Protocol == BusProtocol.DconAscii;

    public string ProtocolWarning => IsSupportedProtocol
        ? string.Empty
        : "Modbus RTU is not implemented yet — scanning and reading are disabled while it is selected.";

    /// <summary>Raised when the protocol changes, so commands can re-evaluate whether they can run.</summary>
    public event EventHandler? ProtocolChanged;

    /// <summary>Reads a value's <see cref="DescriptionAttribute"/> for display.</summary>
    public static string Describe(BusProtocol protocol)
    {
        var field = typeof(BusProtocol).GetField(protocol.ToString(), BindingFlags.Public | BindingFlags.Static);

        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? protocol.ToString();
    }

    /// <summary>
    /// Refreshes the port list, keeping the current selection when that port is still present and falling back
    /// to the first available one when it is not.
    /// </summary>
    public void SetAvailablePorts(IEnumerable<string> ports)
    {
        var current = PortName;

        AvailablePorts.Clear();

        foreach (var port in ports)
            AvailablePorts.Add(port);

        PortName = current != null && AvailablePorts.Contains(current)
            ? current
            : AvailablePorts.FirstOrDefault();
    }
}
