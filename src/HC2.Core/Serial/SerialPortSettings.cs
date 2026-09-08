using System.IO.Ports;

namespace HC2.Core.Serial;

/// <summary>
/// The serial format and timeouts one port runs at. Every module on an RS-485 bus shares one of these — the
/// format belongs to the port, not to the individual module.
/// </summary>
public sealed record SerialPortSettings
{
    /// <summary>Port name as <see cref="SerialPort"/> expects it, e.g. <c>COM3</c>.</summary>
    public string    PortName  { get; init; } = string.Empty;

    public int       BaudRate  { get; init; } = 9600;
    public Parity    Parity    { get; init; } = Parity.None;
    public int       DataBits  { get; init; } = 8;
    public StopBits  StopBits  { get; init; } = StopBits.One;
    public Handshake Handshake { get; init; } = Handshake.None;

    /// <summary>
    /// Total time to wait for a response frame. HardwareController carried the five Win32 <c>COMMTIMEOUTS</c>
    /// fields (read interval / read total constant / read total multiplier / write total constant / write
    /// total multiplier) because the Advantech SDK exposed them; <see cref="SerialPort"/> only offers a total
    /// read and a total write timeout, so the constants map across and the multipliers are dropped. The old
    /// app always set both multipliers to 0, so nothing is actually lost.
    /// </summary>
    public int ReadTimeoutMs  { get; init; } = 300;

    public int WriteTimeoutMs { get; init; } = 300;

    /// <summary>
    /// ADAM/ICP-DAS modules leave the factory at 9600 bps, no parity, 8 data bits, 1 stop bit, no flow
    /// control — the starting point for a bus with no known configuration.
    /// </summary>
    public static SerialPortSettings Factory(string portName) => new() { PortName = portName };

    /// <summary>
    /// The fixed format a module communicates at while its INIT* terminal is grounded, regardless of how it
    /// is configured. See <c>docs/porting-from-hardwarecontroller.md</c>.
    /// </summary>
    public static SerialPortSettings InitMode(string portName) => new() { PortName = portName, BaudRate = 9600 };

    public override string ToString() => $"{PortName} {BaudRate}/{DataBits}/{Parity}/{StopBits}";
}
