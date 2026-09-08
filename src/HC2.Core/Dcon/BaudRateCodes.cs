using System.Collections.Generic;
using System.Linq;

namespace HC2.Core.Dcon;

/// <summary>
/// Maps between the small numeric codes the DCON configuration commands use for baud rate (the <c>CC</c> byte
/// of <c>$AA2</c> / <c>%AANNTTCCFF</c>) and actual bits per second.
/// </summary>
/// <remarks>
/// These two numberings are unrelated and must never be conflated: code <c>0x05</c> means 4800 bps, it is not
/// a rate itself. HardwareController carried this table twice — once per module family — even though the
/// ADAM-4000 Table 5.3 and ADAM-4100 Table 4.5 versions are identical; they are one table here. The 4117's
/// table also lists <c>0x0B</c> for 230400 bps, which the old copy dropped because its own baud-rate enum had
/// no member for it. Nothing here forces that omission, so it is included.
/// </remarks>
public static class BaudRateCodes
{
    private static readonly (byte Code, int BitsPerSecond)[] Table =
    {
        (0x03, 1200),
        (0x04, 2400),
        (0x05, 4800),
        (0x06, 9600),
        (0x07, 19200),
        (0x08, 38400),
        (0x09, 57600),
        (0x0A, 115200),
        (0x0B, 230400)
    };

    /// <summary>The bus rate this app normally runs at, and the fallback for an unrecognized code.</summary>
    public const int DefaultBitsPerSecond = 4800;

    public const byte DefaultCode = 0x05;

    /// <summary>Every (code, rate) pair in ascending order — for populating a baud-rate picker.</summary>
    public static IEnumerable<(byte Code, int BitsPerSecond)> All => Table;

    public static int ToBitsPerSecond(byte code)
    {
        foreach (var entry in Table)
            if (entry.Code == code)
                return entry.BitsPerSecond;

        return DefaultBitsPerSecond;
    }

    public static byte FromBitsPerSecond(int bitsPerSecond)
    {
        foreach (var entry in Table)
            if (entry.BitsPerSecond == bitsPerSecond)
                return entry.Code;

        return DefaultCode;
    }

    public static bool IsKnown(byte code) => Table.Any(entry => entry.Code == code);

    public static string Describe(byte code) =>
        IsKnown(code) ? $"{ToBitsPerSecond(code)} bps" : "Unknown";
}
