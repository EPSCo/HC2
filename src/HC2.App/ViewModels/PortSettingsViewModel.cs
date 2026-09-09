using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HC2.App.Mvvm;
using HC2.Core;
using HC2.Core.Dcon;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

/// <summary>
/// The search space a module scan covers: which ports, which baud rates, which framings, and whether frames
/// carry a checksum — each a multi-select ribbon rather than a single value.
/// </summary>
/// <remarks>
/// Modelled on HardwareController's module finder, where these are the dimensions of the sweep. Every extra
/// tick multiplies the scan, so the defaults are the narrow, known-good ones: the two rates this equipment is
/// deployed at, checksum off, and the framing every module here ships with.
/// </remarks>
public sealed class PortSettingsViewModel : ViewModelBase
{
    private BusProtocol _protocol = BusProtocol.DconAscii;

    public PortSettingsViewModel()
    {
        foreach (var (code, bitsPerSecond) in BaudRateCodes.All)
        {
            _ = code;

            // 4800 and 9600 pre-ticked: the rate this equipment runs at, and the rate every module leaves the
            // factory at. HardwareController falls back to exactly this pair when nothing is selected.
            Add(BaudRates, new CheckableOption<int>(bitsPerSecond, $"{bitsPerSecond:N0}",
                                                    bitsPerSecond is 4800 or 9600));
        }

        Add(ChecksumModes, new CheckableOption<bool>(false, "Checksum Disabled", isSelected: true));
        Add(ChecksumModes, new CheckableOption<bool>(true,  "Checksum Enabled"));

        foreach (var format in SerialFormat.Standard)
            Add(Formats, new CheckableOption<SerialFormat>(format, format.Label, format.Equals(SerialFormat.Default)));
    }

    public ObservableCollection<CheckableOption<string>>       ComPorts      { get; } = new();
    public ObservableCollection<CheckableOption<int>>          BaudRates     { get; } = new();
    public ObservableCollection<CheckableOption<bool>>         ChecksumModes { get; } = new();
    public ObservableCollection<CheckableOption<SerialFormat>> Formats       { get; } = new();

    public IReadOnlyList<BusProtocol> ProtocolOptions { get; } =
        new[] { BusProtocol.DconAscii, BusProtocol.ModbusRtu };

    public BusProtocol Protocol
    {
        get => _protocol;
        set
        {
            if (!SetProperty(ref _protocol, value))
                return;

            RaisePropertyChanged(nameof(IsSupportedProtocol));
            RaisePropertyChanged(nameof(ProtocolWarning));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>False for a protocol HC2 cannot actually speak, which gates every operation.</summary>
    public bool IsSupportedProtocol => Protocol == BusProtocol.DconAscii;

    public string ProtocolWarning => IsSupportedProtocol
        ? string.Empty
        : "Modbus RTU is not implemented yet — scanning and reading are disabled while it is selected. " +
          "DCON's identification command has no Modbus equivalent, so a Modbus bus cannot be discovered here.";

    /// <summary>Raised whenever any tick or the protocol changes, so commands can re-evaluate.</summary>
    public event EventHandler? SelectionChanged;

    public IReadOnlyList<string>       SelectedPorts     => Selected(ComPorts);
    public IReadOnlyList<int>          SelectedBaudRates => Fallback(Selected(BaudRates),     new[] { 4800, 9600 });
    public IReadOnlyList<bool>         SelectedChecksums => Fallback(Selected(ChecksumModes), new[] { false });
    public IReadOnlyList<SerialFormat> SelectedFormats   => Fallback(Selected(Formats),       new[] { SerialFormat.Default });

    public bool HasPortSelected => ComPorts.Any(option => option.IsSelected);

    /// <summary>The first selected port, for anything that works on one at a time.</summary>
    public string? PrimaryPort => SelectedPorts.FirstOrDefault();

    /// <summary>
    /// Refreshes the port list, keeping ticks for ports that are still present. When nothing survives, the
    /// first port is ticked so the panel is never in a state where a scan is impossible for no visible reason.
    /// </summary>
    public void SetAvailablePorts(IEnumerable<string> ports)
    {
        var previouslySelected = new HashSet<string>(SelectedPorts, StringComparer.OrdinalIgnoreCase);

        foreach (var option in ComPorts)
            option.SelectionChanged -= OnOptionChanged;

        ComPorts.Clear();

        foreach (var port in ports)
            Add(ComPorts, new CheckableOption<string>(port, port, previouslySelected.Contains(port)));

        if (ComPorts.Count > 0 && !HasPortSelected)
            ComPorts[0].IsSelected = true;

        RaisePropertyChanged(nameof(HasPortSelected));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ticks exactly one port, for the port list selection driving the ribbon.</summary>
    public void SelectOnlyPort(string? portName)
    {
        foreach (var option in ComPorts)
            option.IsSelected = string.Equals(option.Value, portName, StringComparison.OrdinalIgnoreCase);
    }

    private void Add<T>(ObservableCollection<CheckableOption<T>> target, CheckableOption<T> option)
    {
        option.SelectionChanged += OnOptionChanged;
        target.Add(option);
    }

    private void OnOptionChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(HasPortSelected));
        RaisePropertyChanged(nameof(PrimaryPort));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<T> Selected<T>(IEnumerable<CheckableOption<T>> options) =>
        options.Where(option => option.IsSelected).Select(option => option.Value).ToArray();

    /// <summary>
    /// Falls back to a sensible set when nothing is ticked, rather than scanning nothing at all — the same
    /// thing HardwareController does before a search.
    /// </summary>
    private static IReadOnlyList<T> Fallback<T>(IReadOnlyList<T> selected, IReadOnlyList<T> whenEmpty) =>
        selected.Count > 0 ? selected : whenEmpty;
}
