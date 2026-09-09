using System.Collections.Generic;
using System.Linq;

namespace HC2.Core.Modules;

/// <summary>
/// One selectable input range, identified by the type code the DCON <c>TT</c> byte and the Modbus Type Code
/// register both use.
/// </summary>
public sealed record InputRange
{
    public byte   Code  { get; init; }
    public string Label { get; init; } = string.Empty;
    public double Min   { get; init; }
    public double Max   { get; init; }
    public string Unit  { get; init; } = string.Empty;

    /// <summary>
    /// Converts a raw 16-bit register value to engineering units.
    /// </summary>
    /// <remarks>
    /// Straight linear across the full unsigned span — raw 0..65535 maps to <see cref="Min"/>..<see cref="Max"/>
    /// — not offset binary. Confirmed against real hardware and cross-checked against Advantech's own IO
    /// Utility on two ranges: +/-10 V read 0.483 at a 0.480 V reference (raw 34341), and 4~20 mA read 4.0005 at
    /// 4.000 mA (raw ~2), 11.998 at 12.000 mA (raw ~32750) and exactly 20.0 for an open channel (raw 65535). An
    /// offset-binary reading fit the symmetric +/-10 V case by coincidence and broke completely on the
    /// asymmetric 4~20 mA range.
    /// </remarks>
    public double ToEngineeringUnits(int raw) => raw / 65535.0 * (Max - Min) + Min;

    public override string ToString() => Label;
}

/// <summary>The input ranges each analog module family accepts.</summary>
public static class InputRanges
{
    /// <summary>
    /// ADAM-4017P, Table 5.1.
    /// </summary>
    /// <remarks>
    /// Code 7 is 4~20 mA, not 0~20 mA. HardwareController's enum was named <c>mA_0To20</c> from an early
    /// DCON-era observation, described itself as "4 ~ 20 mA", and scaled as (4, 20) — the description and the
    /// scaling were the correct pair, confirmed against Advantech's IO Utility. The misleading name is not
    /// carried across.
    /// </remarks>
    public static readonly IReadOnlyList<InputRange> Adam4017P = new[]
    {
        new InputRange { Code = 0x07, Label = "4 ~ 20 mA",   Min =    4, Max =  20, Unit = "mA" },
        new InputRange { Code = 0x08, Label = "+/- 10 V",    Min =  -10, Max =  10, Unit = "V"  },
        new InputRange { Code = 0x09, Label = "+/- 5 V",     Min =   -5, Max =   5, Unit = "V"  },
        new InputRange { Code = 0x0A, Label = "+/- 1 V",     Min =   -1, Max =   1, Unit = "V"  },
        new InputRange { Code = 0x0B, Label = "+/- 500 mV",  Min = -500, Max = 500, Unit = "mV" },
        new InputRange { Code = 0x0C, Label = "+/- 150 mV",  Min = -150, Max = 150, Unit = "mV" },
        new InputRange { Code = 0x0D, Label = "+/- 20 mA",   Min =  -20, Max =  20, Unit = "mA" }
    };

    /// <summary>
    /// ADAM-4117, Table 4.3 — the 4017P's bipolar set plus +/-15 V and a unipolar block the 4017P does not have.
    /// </summary>
    public static readonly IReadOnlyList<InputRange> Adam4117 = new[]
    {
        new InputRange { Code = 0x07, Label = "4 ~ 20 mA",   Min =    4, Max =  20, Unit = "mA" },
        new InputRange { Code = 0x08, Label = "+/- 10 V",    Min =  -10, Max =  10, Unit = "V"  },
        new InputRange { Code = 0x09, Label = "+/- 5 V",     Min =   -5, Max =   5, Unit = "V"  },
        new InputRange { Code = 0x0A, Label = "+/- 1 V",     Min =   -1, Max =   1, Unit = "V"  },
        new InputRange { Code = 0x0B, Label = "+/- 500 mV",  Min = -500, Max = 500, Unit = "mV" },
        new InputRange { Code = 0x0C, Label = "+/- 150 mV",  Min = -150, Max = 150, Unit = "mV" },
        new InputRange { Code = 0x0D, Label = "+/- 20 mA",   Min =  -20, Max =  20, Unit = "mA" },
        new InputRange { Code = 0x15, Label = "+/- 15 V",    Min =  -15, Max =  15, Unit = "V"  },
        new InputRange { Code = 0x48, Label = "0 ~ 10 V",    Min =    0, Max =  10, Unit = "V"  },
        new InputRange { Code = 0x49, Label = "0 ~ 5 V",     Min =    0, Max =   5, Unit = "V"  },
        new InputRange { Code = 0x4A, Label = "0 ~ 1 V",     Min =    0, Max =   1, Unit = "V"  },
        new InputRange { Code = 0x4B, Label = "0 ~ 500 mV",  Min =    0, Max = 500, Unit = "mV" },
        new InputRange { Code = 0x4C, Label = "0 ~ 150 mV",  Min =    0, Max = 150, Unit = "mV" },
        new InputRange { Code = 0x4D, Label = "0 ~ 20 mA",   Min =    0, Max =  20, Unit = "mA" },
        new InputRange { Code = 0x55, Label = "0 ~ 15 V",    Min =    0, Max =  15, Unit = "V"  }
    };

    public static InputRange? Find(IReadOnlyList<InputRange> ranges, byte code) =>
        ranges.FirstOrDefault(range => range.Code == code);
}
