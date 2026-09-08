using System;

namespace HC2.Core.Dcon;

/// <summary>
/// The optional two-character checksum that sits between a DCON frame's body and its carriage return.
/// </summary>
/// <remarks>
/// UNVERIFIED AGAINST HARDWARE. HardwareController never implemented this — it only set a boolean on the
/// Advantech SDK's port object and let the SDK build the frame, so the algorithm was never written down
/// anywhere in that codebase. What is implemented here is the ADAM-4000 series manual's specification: the
/// sum of the ASCII values of every character in the frame excluding the terminator, taken modulo 256 and
/// written as two uppercase hex digits. Confirm it against a live module before trusting a checksum-enabled
/// bus; every module this app has been used with runs with checksum disabled, where none of this applies.
/// See <c>docs/porting-from-hardwarecontroller.md</c>.
/// </remarks>
public static class DconChecksum
{
    /// <summary>Computes the checksum for a frame body (the command without terminator or checksum).</summary>
    public static string Compute(string body)
    {
        if (body == null)
            throw new ArgumentNullException(nameof(body));

        var sum = 0;
        foreach (var c in body)
            sum += c;

        return (sum & 0xFF).ToString("X2");
    }

    /// <summary>
    /// Splits a received frame into its body and the checksum trailing it, and reports whether that checksum
    /// matches the body. A frame too short to carry one is reported as invalid with the whole frame as body.
    /// </summary>
    public static bool TryStrip(string frame, out string body)
    {
        body = frame ?? string.Empty;

        if (body.Length < 3)
            return false;

        var received = body.Substring(body.Length - 2);
        var payload  = body.Substring(0, body.Length - 2);

        if (!string.Equals(Compute(payload), received, StringComparison.OrdinalIgnoreCase))
            return false;

        body = payload;
        return true;
    }
}
