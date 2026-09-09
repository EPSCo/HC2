using HC2.Core.Dcon;

namespace HC2.Core.Modules;

/// <summary>Turns a scan result into something that can be talked to.</summary>
public static class ModuleFactory
{
    /// <summary>
    /// Creates the module class for a discovered module, or null when the model has no implementation yet —
    /// the ICP-7017Z, ICP-7080 and ICP-7083 are recognized by discovery but not yet driven.
    /// </summary>
    public static AnalogInputModule? Create(DconClient client, DiscoveredModule discovered) =>
        discovered.Model.HasValue ? Create(client, discovered.Model.Value, discovered.Address) : null;

    public static AnalogInputModule? Create(DconClient client, ModuleModel model, int address) => model switch
    {
        ModuleModel.Adam4017P => new Adam4017P(client, address),
        ModuleModel.Adam4117  => new Adam4117(client, address),
        _                     => null
    };
}
