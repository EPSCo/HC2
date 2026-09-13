using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using HC2.Core;
using HC2.Core.Dcon;

namespace HC2.App.ViewModels;

/// <summary>Display shape for one scan result — keeps formatting and labelling out of the XAML.</summary>
public sealed class DiscoveredModuleRow
{
    public DiscoveredModuleRow(DiscoveredModule module) => Module = module;

    public DiscoveredModule Module { get; }

    /// <summary>
    /// The port it answered on, as its number alone (<c>COM5</c> → <c>5</c>): the column is headed COM. A name
    /// that is not <c>COM</c>-prefixed is shown whole rather than mangled.
    /// </summary>
    public string Port => Module.PortName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                          && Module.PortName.Length > 3
        ? Module.PortName.Substring(3)
        : Module.PortName;

    /// <summary>
    /// The framing it answered under, written <c>N, 8.1</c> — parity, then data and stop bits. Built from
    /// <see cref="SerialFormat.Label"/> (<c>N,8,1</c>) so the table and the ribbon cannot disagree on the parts.
    /// </summary>
    public string Format
    {
        get
        {
            var parts = Module.Format.Label.Split(',');

            return parts.Length == 3 ? $"{parts[0]}, {parts[1]}.{parts[2]}" : Module.Format.Label;
        }
    }

    /// <summary>
    /// Decimal, and only decimal. Hex is how the address travels on the wire, but every module in the
    /// field is labelled in decimal, so showing both invited people to read the wrong one.
    /// </summary>
    public int Address => Module.Address;

    public string Model => Module.Model.HasValue
        ? Describe(Module.Model.Value)
        : $"Unrecognized ({Module.Identifier})";

    /// <summary>
    /// Bare number with a thousands separator (<c>4,800</c>): the column is headed Baud, so the unit does not
    /// need repeating on every row. Invariant culture, so the separator does not change with the PC's locale.
    /// </summary>
    public string BaudRate => Module.BaudRate.ToString("N0", CultureInfo.InvariantCulture);

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
