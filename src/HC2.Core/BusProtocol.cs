using System.ComponentModel;

namespace HC2.Core;

/// <summary>How a serial bus is spoken to.</summary>
/// <remarks>
/// A module is switched between the two through bit 2 of its configuration flag byte, and the change only takes
/// effect with its INIT* terminal grounded. Every module on one line must agree, and a module in Modbus mode
/// cannot be asked over DCON what mode it is in — which is why this is a setting the operator states rather
/// than something discovery can work out.
/// </remarks>
public enum BusProtocol
{
    /// <summary>Advantech's DCON ASCII protocol — the only one HC2 implements today.</summary>
    [Description("DCON ASCII")]
    DconAscii,

    /// <summary>
    /// Modbus RTU. Recognized here so the setting can be recorded and so the UI can say plainly that it is not
    /// supported yet; HC2 has no Modbus client, so nothing will talk to a bus in this mode.
    /// </summary>
    [Description("Modbus RTU")]
    ModbusRtu
}
