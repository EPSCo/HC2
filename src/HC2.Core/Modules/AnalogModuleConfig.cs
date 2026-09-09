using HC2.Core.Dcon;

namespace HC2.Core.Modules;

/// <summary>
/// The module-wide settings carried by the <c>$AA2</c> / <c>%AANNTTCCFF</c> command pair — a plain mirror of the
/// wire format, not a translation of it.
/// </summary>
/// <remarks>
/// The flag byte is kept raw and decoded through properties, because its bit 6 means different things on
/// different families: integration time on the ADAM-4017P (manual figure 5-1), high-speed mode on the
/// ADAM-4117 (manual figure 4.1). Storing a decoded <c>bool Integration</c> would quietly write the wrong
/// setting when the same struct met the other module. Each module class exposes bit 6 under its own name.
/// </remarks>
public sealed record AnalogModuleConfig
{
    public const byte ChecksumBit = 0x80;
    public const byte Bit6Mask    = 0x40;
    public const byte ModbusBit   = 0x04;
    public const byte FormatMask  = 0x03;

    /// <summary>
    /// The address in the <c>!AA…</c> reply — the module's own stored address, which is NOT necessarily the
    /// address the command was sent to. A module queried at 0 while its INIT* terminal is grounded still
    /// reports its configured address here.
    /// </summary>
    public int  ReportedAddress { get; init; }

    public byte InputRangeCode  { get; init; }

    /// <summary>Baud rate as a DCON code, not bits per second — see <see cref="BaudRateCodes"/>.</summary>
    public byte BaudRateCode    { get; init; }

    /// <summary>The raw <c>FF</c> byte.</summary>
    public byte Flags           { get; init; }

    public bool ChecksumEnabled  => (Flags & ChecksumBit) != 0;
    public bool ProtocolIsModbus => (Flags & ModbusBit)   != 0;

    /// <summary>Bit 6, whose meaning depends on the module family.</summary>
    public bool Bit6 => (Flags & Bit6Mask) != 0;

    /// <summary>
    /// Bits 1-0. Only <c>00</c>, engineering units, is understood by the channel parsing here — never write
    /// anything else.
    /// </summary>
    public byte DataFormatCode => (byte) (Flags & FormatMask);

    public int BaudRate => BaudRateCodes.ToBitsPerSecond(BaudRateCode);

    public AnalogModuleConfig WithChecksum(bool enabled)  => WithFlag(ChecksumBit, enabled);
    public AnalogModuleConfig WithModbus(bool enabled)    => WithFlag(ModbusBit,   enabled);
    public AnalogModuleConfig WithBit6(bool enabled)      => WithFlag(Bit6Mask,    enabled);

    public AnalogModuleConfig WithBaudRate(int bitsPerSecond) =>
        this with { BaudRateCode = BaudRateCodes.FromBitsPerSecond(bitsPerSecond) };

    public AnalogModuleConfig WithInputRange(byte code) => this with { InputRangeCode = code };

    private AnalogModuleConfig WithFlag(byte mask, bool enabled) =>
        this with { Flags = (byte) (enabled ? Flags | mask : Flags & ~mask) };

    public override string ToString() =>
        $"addr {ReportedAddress:X2}, range 0x{InputRangeCode:X2}, {BaudRate} bps, flags 0x{Flags:X2}";
}
