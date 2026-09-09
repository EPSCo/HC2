using System.Collections.Generic;
using HC2.Core.Dcon;

namespace HC2.Core.Modules;

/// <summary>ADAM-4117: eight analog inputs, DCON ASCII, with a wider range set than the ADAM-4017P.</summary>
/// <remarks>
/// The flag byte looks the same but is not: bit 6 here is high-speed mode (manual figure 4.1), where on the
/// ADAM-4017P it is integration time. Reading one module's flags and writing them to the other silently changes
/// the wrong setting, which is why <see cref="AnalogModuleConfig"/> keeps the byte raw.
/// </remarks>
public sealed class Adam4117 : AnalogInputModule
{
    public Adam4117(DconClient client, int address) : base(client, address)
    {
    }

    public override ModuleModel Model => ModuleModel.Adam4117;

    public override int ChannelCount => 8;

    public override IReadOnlyList<InputRange> SupportedRanges => InputRanges.Adam4117;

    /// <summary>Flag byte bit 6 on this family.</summary>
    public static bool HighSpeedModeOf(AnalogModuleConfig config) => config.Bit6;

    public static AnalogModuleConfig WithHighSpeedMode(AnalogModuleConfig config, bool enabled) =>
        config.WithBit6(enabled);
}
