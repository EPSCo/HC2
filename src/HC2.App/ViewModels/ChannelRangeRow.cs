namespace HC2.App.ViewModels;

/// <summary>One channel's configured input range, as read back from a module.</summary>
public sealed class ChannelRangeRow
{
    public ChannelRangeRow(int channel, byte? code, string range, double? min, double? max, string unit)
    {
        Channel = channel;
        Code    = code;
        Range   = range;
        Min     = min;
        Max     = max;
        Unit    = unit;
    }

    public int Channel { get; }

    /// <summary>The raw type code, null when the channel could not be read.</summary>
    public byte? Code { get; }

    public string CodeText => Code.HasValue ? $"0x{Code.Value:X2}" : "—";

    /// <summary>The range's label, or an explanation when it cannot be resolved.</summary>
    public string Range { get; }

    public double? Min { get; }
    public double? Max { get; }

    public string Span => Min.HasValue && Max.HasValue ? $"{Min} … {Max} {Unit}".Trim() : "—";

    public string Unit { get; }

    /// <summary>A row created for a channel that did not answer.</summary>
    public static ChannelRangeRow Unread(int channel, string reason) =>
        new(channel, null, reason, null, null, string.Empty);
}
