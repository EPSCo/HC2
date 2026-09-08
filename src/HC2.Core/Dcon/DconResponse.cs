using System;
using System.Globalization;

namespace HC2.Core.Dcon;

/// <summary>
/// Parsing for DCON response frames. Every method here tolerates a short or malformed frame — a module that
/// answers with noise, or does not answer at all, is an ordinary event on a serial bus and must not throw.
/// </summary>
/// <remarks>
/// HardwareController's parsers indexed into responses without checking their length
/// (<c>result.Remove(0, 1)</c> followed by <c>result[1]</c>), which turns a truncated reply into an exception
/// instead of a failed read. That is the specific mistake these methods exist to avoid.
/// </remarks>
public static class DconResponse
{
    /// <summary>Leading character of an acknowledged configuration or command response.</summary>
    public const char Acknowledged = '!';

    /// <summary>Leading character of a data response to <c>#AA</c>.</summary>
    public const char Data = '>';

    /// <summary>Leading character of a rejection — an invalid command, or a change the module will only
    /// accept with its INIT* terminal grounded.</summary>
    public const char Rejected = '?';

    public static bool IsAcknowledged(string? frame) => StartsWith(frame, Acknowledged);

    public static bool IsData(string? frame) => StartsWith(frame, Data);

    public static bool IsRejected(string? frame) => StartsWith(frame, Rejected);

    /// <summary>True for any well-formed positive response, of either shape.</summary>
    public static bool IsPositive(string? frame) => IsAcknowledged(frame) || IsData(frame);

    /// <summary>
    /// Reads the address a <c>!AA…</c> frame reports.
    /// </summary>
    /// <remarks>
    /// This is the module's own stored address, which is NOT necessarily the address the command was sent to:
    /// a module queried at 0 while INIT* is grounded still reports its configured address here. Use this to
    /// learn what a module thinks it is, never to verify that the right module answered.
    /// </remarks>
    public static bool TryGetReportedAddress(string? frame, out int address)
    {
        address = -1;

        if (frame is not { Length: >= 3 } || !IsPositive(frame))
            return false;

        return int.TryParse(frame.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
    }

    /// <summary>Everything after the leading character and the two address digits.</summary>
    public static string Payload(string? frame) =>
        frame is { Length: > 3 } ? frame.Substring(3) : string.Empty;

    /// <summary>
    /// Parses the identification string from a <c>$AAM</c> response into a known model.
    /// </summary>
    public static bool TryParseIdentity(string? frame, out ModuleModel model)
    {
        model = default;

        return IsAcknowledged(frame) && ModuleModelId.TryParse(Payload(frame), out model);
    }

    /// <summary>
    /// Parses a <c>&gt;</c> data frame into channel values. Each channel occupies exactly seven characters in
    /// engineering-units format (sign, three integer digits, point, three decimals — e.g. <c>+00.000</c>), so
    /// the channel count comes from the frame's own length rather than being assumed.
    /// </summary>
    /// <param name="values">One entry per channel present; a field that will not parse becomes NaN rather
    /// than failing the whole frame, since one bad channel does not invalidate the others.</param>
    public static bool TryParseEngineeringUnits(string? frame, out double[] values)
    {
        const int fieldWidth = 7;

        values = Array.Empty<double>();

        if (!IsData(frame))
            return false;

        var body     = frame!.Substring(1);
        var channels = body.Length / fieldWidth;

        if (channels == 0)
            return false;

        values = new double[channels];

        for (var i = 0; i < channels; i++)
        {
            var field = body.Substring(i * fieldWidth, fieldWidth);

            values[i] = double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN;
        }

        return true;
    }

    /// <summary>
    /// Parses a hex counter or encoder value from a <c>#AAN</c> response — the ICP-7080 and ICP-7083 answer
    /// with a fixed-width hex count rather than engineering units.
    /// </summary>
    public static bool TryParseHexValue(string? frame, out long value)
    {
        value = 0;

        if (frame is not { Length: > 1 } || !IsPositive(frame))
            return false;

        return long.TryParse(frame.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool StartsWith(string? frame, char marker) => frame is { Length: > 0 } && frame[0] == marker;
}
