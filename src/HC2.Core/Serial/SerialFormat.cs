using System.Collections.Generic;
using System.IO.Ports;

namespace HC2.Core.Serial;

/// <summary>
/// A character framing: parity, data bits and stop bits, written the way the ADAM manuals write it — "N,8,1".
/// </summary>
/// <remarks>
/// Separate from <see cref="SerialPortSettings"/> because a scan sweeps framings the way it sweeps baud rates:
/// a bus whose framing is unknown has to be tried several ways before it answers.
/// </remarks>
public sealed record SerialFormat
{
    public Parity   Parity   { get; init; } = Parity.None;
    public int      DataBits { get; init; } = 8;
    public StopBits StopBits { get; init; } = StopBits.One;

    public string Label => $"{Letter},{DataBits},{Stops}";

    private char Letter => Parity switch
    {
        Parity.None  => 'N',
        Parity.Even  => 'E',
        Parity.Odd   => 'O',
        Parity.Mark  => 'M',
        Parity.Space => 'S',
        _            => '?'
    };

    private string Stops => StopBits switch
    {
        StopBits.One          => "1",
        StopBits.Two          => "2",
        StopBits.OnePointFive => "1.5",
        _                     => "?"
    };

    /// <summary>The framing every ADAM and ICP-DAS module here ships with.</summary>
    public static SerialFormat Default => new();

    /// <summary>
    /// The four framings worth sweeping, matching the set HardwareController's module finder offered. Anything
    /// beyond these has never been seen on this equipment.
    /// </summary>
    public static IReadOnlyList<SerialFormat> Standard { get; } = new[]
    {
        new SerialFormat { Parity = Parity.None, DataBits = 8, StopBits = StopBits.One },
        new SerialFormat { Parity = Parity.None, DataBits = 8, StopBits = StopBits.Two },
        new SerialFormat { Parity = Parity.Even, DataBits = 8, StopBits = StopBits.One },
        new SerialFormat { Parity = Parity.Odd,  DataBits = 8, StopBits = StopBits.One }
    };

    public override string ToString() => Label;
}
