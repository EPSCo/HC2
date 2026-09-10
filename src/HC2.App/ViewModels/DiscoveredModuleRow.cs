using System;
using System.ComponentModel;
using System.Reflection;
using HC2.Core;
using HC2.Core.Dcon;

namespace HC2.App.ViewModels;

/// <summary>Display shape for one scan result — keeps formatting and labelling out of the XAML.</summary>
public sealed class DiscoveredModuleRow
{
    public DiscoveredModuleRow(DiscoveredModule module) => Module = module;

    public DiscoveredModule Module { get; }

    /// <summary>The port it answered on — a scan can cover several.</summary>
    public string Port => Module.PortName;

    /// <summary>The framing it answered under, e.g. <c>N,8,1</c>.</summary>
    public string Format => Module.Format.Label;

    /// <summary>
    /// Decimal, and only decimal. Hex is how the address travels on the wire, but every module in the
    /// field is labelled in decimal, so showing both invited people to read the wrong one.
    /// </summary>
    public int Address => Module.Address;

    public string Model => Module.Model.HasValue
        ? Describe(Module.Model.Value)
        : $"Unrecognized ({Module.Identifier})";

    /// <summary>Bare number: the column is headed Baud, so the unit does not need repeating on every row.</summary>
    public string BaudRate => Module.BaudRate.ToString();

    public string Checksum => Module.Checksum ? "Enabled" : "Disabled";

    public string Channels => Module.ChannelCount > 0 ? Module.ChannelCount.ToString() : "—";

    public string Protocol => Describe(Module.Protocol);

    public bool IsRecognized => Module.IsRecognized;

    /// <summary>Reads a value's <see cref="DescriptionAttribute"/>, falling back to its name.</summary>
    private static string Describe<T>(T value) where T : struct, Enum
    {
        var field = typeof(T).GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static);

        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();
    }
}
