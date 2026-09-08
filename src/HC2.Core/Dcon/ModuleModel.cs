using System.ComponentModel;

namespace HC2.Core.Dcon;

/// <summary>The module types this app talks to.</summary>
public enum ModuleModel
{
    [Description("ADAM-4017P")]
    Adam4017P,

    [Description("ADAM-4117")]
    Adam4117,

    [Description("ICP-7017Z")]
    Icp7017Z,

    [Description("ICP-7080")]
    Icp7080,

    [Description("ICP-7083")]
    Icp7083
}

/// <summary>What kind of signal a module reads — drives how its channel values are interpreted.</summary>
public enum ModuleKind
{
    Analog,
    Digital,
    Encoder
}

/// <summary>
/// Maps the identification string a module returns from <c>$AAM</c> (e.g. <c>4017P</c>, <c>7017Z</c>) to a
/// <see cref="ModuleModel"/>. One table, so discovery and connection verification cannot drift apart.
/// </summary>
public static class ModuleModelId
{
    public static bool TryParse(string identifier, out ModuleModel model)
    {
        switch (identifier?.Trim().ToUpperInvariant())
        {
            case "4017P": model = ModuleModel.Adam4017P; return true;
            case "4117":  model = ModuleModel.Adam4117;  return true;
            case "7017Z": model = ModuleModel.Icp7017Z;  return true;
            case "7080":  model = ModuleModel.Icp7080;   return true;
            case "7083":  model = ModuleModel.Icp7083;   return true;

            default:
                model = default;
                return false;
        }
    }

    /// <summary>Number of input channels a model exposes. The ICP-7017Z is wiring-dependent — 10 channels
    /// differential, 20 single-ended — so it has no fixed answer here and reports its single-ended maximum.</summary>
    public static int ChannelCount(ModuleModel model) => model switch
    {
        ModuleModel.Adam4017P => 8,
        ModuleModel.Adam4117  => 8,
        ModuleModel.Icp7017Z  => 20,
        ModuleModel.Icp7080   => 2,
        ModuleModel.Icp7083   => 3,
        _                     => 0
    };

    public static ModuleKind Kind(ModuleModel model) => model switch
    {
        ModuleModel.Icp7080 => ModuleKind.Digital,
        ModuleModel.Icp7083 => ModuleKind.Encoder,
        _                   => ModuleKind.Analog
    };
}
