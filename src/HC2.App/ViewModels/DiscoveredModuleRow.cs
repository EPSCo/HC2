using System;
using System.ComponentModel;
using System.Reflection;
using HC2.Core.Dcon;

namespace HC2.App.ViewModels;

/// <summary>Display shape for one scan result — keeps hex formatting and labelling out of the XAML.</summary>
public sealed class DiscoveredModuleRow
{
    public DiscoveredModuleRow(DiscoveredModule module) => Module = module;

    public DiscoveredModule Module { get; }

    /// <summary>The address as the two hex digits every DCON command embeds.</summary>
    public string Address => $"{Module.Address:X2}";

    /// <summary>Decimal address, since module labelling in the field is usually decimal.</summary>
    public int AddressDecimal => Module.Address;

    public string Model => Module.Model.HasValue
        ? Describe(Module.Model.Value)
        : $"Unrecognized ({Module.Identifier})";

    public string BaudRate => $"{Module.BaudRate:N0} bps";

    public string Checksum => Module.Checksum ? "Enabled" : "Disabled";

    public string Channels => Module.ChannelCount > 0 ? Module.ChannelCount.ToString() : "—";

    public bool IsRecognized => Module.IsRecognized;

    /// <summary>Reads a value's <see cref="DescriptionAttribute"/>, falling back to its name.</summary>
    private static string Describe(ModuleModel model)
    {
        var field = typeof(ModuleModel).GetField(model.ToString(), BindingFlags.Public | BindingFlags.Static);

        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? model.ToString();
    }
}
