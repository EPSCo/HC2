using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using HC2.App.Mvvm;
using HC2.Core.Dcon;
using HC2.Core.Modules;
using HC2.Core.Serial;

namespace HC2.App.ViewModels;

/// <summary>
/// Reads one selected module's per-channel input ranges.
/// </summary>
/// <remarks>
/// <para>
/// Everything here comes from the module that was found, not from the port ribbon. A bus can carry modules at
/// different rates — this one does, with an ADAM-4017P at 4800 and an ICP-7017Z at 9600 — so talking to a
/// module means reopening its own port at its own rate, framing and checksum. Using the ribbon's current
/// values would address the wrong module or get silence.
/// </para>
/// <para>
/// The range table is likewise the selected model's: the ADAM-4017P accepts seven codes, the ADAM-4117 those
/// plus a unipolar block, and the ICP-7017Z has no table recorded here at all, so its codes are shown raw.
/// </para>
/// <para>
/// Read-only. Writing a range changes the module's configuration and has never been exercised against this
/// equipment, so it is deliberately not offered here yet.
/// </para>
/// </remarks>
public sealed class ModuleDetailViewModel : ViewModelBase
{
    private readonly ModuleScanViewModel _scan;
    private readonly AsyncRelayCommand   _read;

    private string _status = "Select a module in the scan results.";
    private bool   _isBusy;

    public ModuleDetailViewModel(ModuleScanViewModel scan)
    {
        _scan = scan;
        _read = new AsyncRelayCommand(ReadAsync, () => !IsBusy && Selected is { IsRecognized: true });

        _scan.PropertyChanged += OnScanChanged;
    }

    public ObservableCollection<ChannelRangeRow> Channels { get; } = new();

    public ICommand ReadCommand => _read;

    public DiscoveredModuleRow? Selected => _scan.SelectedResult;

    public string Headline => Selected is null
        ? "Module"
        : $"Module — {Selected.Model} @ {Selected.Address}";

    /// <summary>The connection this module answered under, which is what will be reopened to talk to it.</summary>
    public string Connection => Selected is null
        ? string.Empty
        : $"{Selected.Port} · {Selected.BaudRate} · {Selected.Format} · checksum {Selected.Checksum.ToLowerInvariant()}";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                _read.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private async Task ReadAsync()
    {
        var selected = Selected;

        if (selected is null || !selected.IsRecognized)
        {
            Status = "Select a recognized module first.";
            return;
        }

        var found = selected.Module;

        Channels.Clear();
        IsBusy = true;
        Status = $"Reading {found.Model} at {found.Address:X2} on {found.PortName}…";

        try
        {
            // Reopened at the module's own settings, not the ribbon's.
            var settings = new SerialPortSettings
            {
                PortName      = found.PortName,
                BaudRate      = found.BaudRate,
                Parity        = found.Format.Parity,
                DataBits      = found.Format.DataBits,
                StopBits      = found.Format.StopBits,
                ReadTimeoutMs = 300
            };

            var rows = await Task.Run(() => Read(found, settings));

            foreach (var row in rows.Rows)
                Channels.Add(row);

            Status = rows.Status;
        }
        catch (Exception e)
        {
            Status = $"Read failed: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Runs on a background thread — every call here blocks on the serial line.</summary>
    private static (System.Collections.Generic.List<ChannelRangeRow> Rows, string Status) Read(
        DiscoveredModule found, SerialPortSettings settings)
    {
        var rows   = new System.Collections.Generic.List<ChannelRangeRow>();
        var opened = SerialTransportOpener.Open(settings);

        if (!opened.Opened)
            return (rows, opened.Error ?? $"Could not open {found.PortName}.");

        using var transport = opened.Transport!;

        var client = new DconClient(transport, found.Checksum);
        var module = ModuleFactory.Create(client, found);

        if (module is null)
            return (rows, $"{found.Model} has no driver here yet.");

        // The ICP-7017Z's channel count and its per-channel command width both depend on how it is wired, so
        // that has to be settled before any channel is addressed.
        if (module is Icp7017Z wired && !wired.TryReadWiringMode(out _))
            return (rows, $"Could not read the wiring mode: {wired.LastError}");

        var hasTable = module.SupportedRanges.Count > 0;

        for (var channel = 0; channel < module.ChannelCount; channel++)
        {
            if (!module.TryReadInputRangeCode(channel, out var code))
            {
                rows.Add(ChannelRangeRow.Unread(channel, module.LastError ?? "No response."));
                continue;
            }

            var range = hasTable ? InputRanges.Find(module.SupportedRanges, code) : null;

            rows.Add(range != null
                ? new ChannelRangeRow(channel, code, range.Label, range.Min, range.Max, range.Unit)
                : new ChannelRangeRow(channel, code,
                                       hasTable
                                           ? $"Not in the {module.Model} table"
                                           : $"{module.Model} has no range table here",
                                       null, null, string.Empty));
        }

        var wiring = module is Icp7017Z z ? $", {z.Wiring} wiring" : string.Empty;

        return (rows, $"{module.Model} at {module.Address:X2} on {found.PortName} — " +
                      $"{module.ChannelCount} channel(s){wiring}, read at {found.BaudRate} bps.");
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModuleScanViewModel.SelectedResult))
            return;

        Channels.Clear();

        RaisePropertyChanged(nameof(Selected));
        RaisePropertyChanged(nameof(Headline));
        RaisePropertyChanged(nameof(Connection));
        _read.RaiseCanExecuteChanged();

        Status = Selected is null
            ? "Select a module in the scan results."
            : Selected.IsRecognized
                ? "Read to see this module's input ranges."
                : "This address answered with an identifier HC2 does not recognize.";
    }
}
