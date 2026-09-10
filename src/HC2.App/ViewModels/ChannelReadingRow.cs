using HC2.App.Mvvm;
using HC2.Core.Modules;

namespace HC2.App.ViewModels;

/// <summary>One channel of one module in the live view. Created once per channel and updated in place, so the
/// grid does not rebuild its rows on every poll.</summary>
public sealed class ChannelReadingRow : ViewModelBase
{
    private readonly int _address;

    private double  _value = double.NaN;
    private string? _error;

    public ChannelReadingRow(string moduleLabel, int address, int channel)
    {
        ModuleLabel = moduleLabel;
        _address    = address;
        Channel     = channel;
    }

    public string ModuleLabel { get; }

    public string Address => _address.ToString();

    public int Channel { get; }

    public double Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
                RaiseDerived();
        }
    }

    /// <summary>Set when the module could not be read at all, cleared on the next good read.</summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value))
                RaiseDerived();
        }
    }

    public string Display => Error != null                            ? "—"
                           : AnalogInputModule.IsMeasurement(_value)  ? _value.ToString("F3")
                                                                      : "—";

    public string State => Error != null                       ? "Error"
                         : double.IsNaN(_value)                ? "No data"
                         : AnalogInputModule.IsMeasurement(_value) ? "OK"
                                                                  : "Under range";

    public string Detail => Error ?? string.Empty;

    private void RaiseDerived()
    {
        RaisePropertyChanged(nameof(Display));
        RaisePropertyChanged(nameof(State));
        RaisePropertyChanged(nameof(Detail));
    }
}
