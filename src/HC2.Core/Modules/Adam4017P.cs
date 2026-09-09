using System.Collections.Generic;
using HC2.Core.Dcon;

namespace HC2.Core.Modules;

/// <summary>Integration time — which mains frequency the module rejects noise from.</summary>
public enum IntegrationTime
{
    /// <summary>Bit clear: 50 ms integration, for 60 Hz mains.</summary>
    Ms50For60HzPower,

    /// <summary>Bit set: 60 ms integration, for 50 Hz mains.</summary>
    Ms60For50HzPower
}

/// <summary>ADAM-4017P: eight analog inputs, DCON ASCII.</summary>
/// <remarks>
/// This module can also be switched to Modbus RTU through bit 2 of the flag byte, which is a one-way trip as
/// far as this app is concerned until a Modbus client exists: none of the commands here have a documented
/// Modbus equivalent, and a module in Modbus mode cannot be asked over DCON whether it is in Modbus mode.
/// </remarks>
public sealed class Adam4017P : AnalogInputModule
{
    public Adam4017P(DconClient client, int address) : base(client, address)
    {
    }

    public override ModuleModel Model => ModuleModel.Adam4017P;

    public override int ChannelCount => 8;

    public override IReadOnlyList<InputRange> SupportedRanges => InputRanges.Adam4017P;

    /// <summary>Flag byte bit 6 on this family (manual figure 5-1).</summary>
    public static IntegrationTime IntegrationTimeOf(AnalogModuleConfig config) =>
        config.Bit6 ? IntegrationTime.Ms60For50HzPower : IntegrationTime.Ms50For60HzPower;

    public static AnalogModuleConfig WithIntegrationTime(AnalogModuleConfig config, IntegrationTime integration) =>
        config.WithBit6(integration == IntegrationTime.Ms60For50HzPower);
}
