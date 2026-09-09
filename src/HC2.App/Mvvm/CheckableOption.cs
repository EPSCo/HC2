using System;

namespace HC2.App.Mvvm;

/// <summary>One tickable choice in a search ribbon: a value, how to show it, and whether it is selected.</summary>
/// <remarks>
/// These are multi-select by design. Each ticked option widens the search space rather than replacing it, so a
/// bus whose baud rate or framing is unknown can be found by trying several.
/// </remarks>
public sealed class CheckableOption<T> : ViewModelBase
{
    private bool _isSelected;

    public CheckableOption(T value, string label, bool isSelected = false, string? description = null)
    {
        Value       = value;
        Label       = label;
        Description = description;
        _isSelected = isSelected;
    }

    public T Value { get; }

    public string Label { get; }

    /// <summary>Longer text for a tooltip — a port's device name, say, which will not fit on the tick.</summary>
    public string? Description { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    public override string ToString() => Label;
}
