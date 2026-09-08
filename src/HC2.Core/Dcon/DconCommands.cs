using System;

namespace HC2.Core.Dcon;

/// <summary>
/// Builders for every DCON ASCII command this app sends. HardwareController spelled these out as interpolated
/// strings inline in five module classes; collecting them here is what makes the command set reviewable
/// against the manual.
/// </summary>
/// <remarks>
/// <c>AA</c> throughout is the module address as two uppercase hex digits. None of these strings carry the
/// terminator or checksum — <see cref="DconClient"/> adds both.
/// </remarks>
public static class DconCommands
{
    public const int MinAddress = 0;
    public const int MaxAddress = 255;

    /// <summary>Read every channel at once, in engineering units. Answered with a <c>&gt;</c> frame.</summary>
    public static string ReadAllChannels(int address) => $"#{Addr(address)}";

    /// <summary>Read one counter or encoder channel as hex — the ICP-7080 and ICP-7083 have no bulk read.</summary>
    public static string ReadChannel(int address, int channel) => $"#{Addr(address)}{channel:X1}";

    /// <summary>Module identification. The probe every discovery scan is built on.</summary>
    public static string Identify(int address) => $"${Addr(address)}M";

    public static string FirmwareVersion(int address) => $"${Addr(address)}F";

    /// <summary>Read input range, baud-rate code and the flag byte in one round trip.</summary>
    public static string ReadConfig(int address) => $"${Addr(address)}2";

    /// <summary>
    /// Write address, input range, baud rate and flags in one round trip.
    /// </summary>
    /// <param name="fromAddress">The address the module currently answers at — 0 while INIT* is grounded,
    /// which is not necessarily the address the module has stored.</param>
    /// <param name="newAddress">The address to move to, or the current one to leave it alone.</param>
    /// <remarks>
    /// Baud rate, checksum and protocol only change while the module's INIT* terminal is grounded; without it
    /// the module answers <c>?AA</c>. After any accepted write the module is unreachable for up to seven
    /// seconds while it auto-calibrates.
    /// </remarks>
    public static string WriteConfig(int fromAddress, int newAddress, byte inputRangeCode, byte baudRateCode, byte flags) =>
        $"%{Addr(fromAddress)}{Addr(newAddress)}{inputRangeCode:X2}{baudRateCode:X2}{flags:X2}";

    /// <summary>Read the channel-enable bitmask.</summary>
    public static string ReadChannelMask(int address) => $"${Addr(address)}6";

    /// <summary>Write the channel-enable bitmask. Width differs per model — 1 hex digit for the 8-channel
    /// ADAM modules, 4 or 6 for the ICP-7017Z depending on its wiring mode.</summary>
    public static string WriteChannelMask(int address, int mask, int hexDigits) =>
        $"${Addr(address)}5{mask.ToString($"X{hexDigits}")}";

    public static string ReadInputRange(int address, string channel) => $"${Addr(address)}8C{channel}";

    public static string WriteInputRange(int address, string channel, byte rangeCode) =>
        $"${Addr(address)}7C{channel}R{rangeCode:X2}";

    /// <summary>Read the communication watchdog timeout.</summary>
    public static string ReadCommWatchdog(int address) => $"${Addr(address)}Y";

    /// <summary>
    /// Write the communication watchdog timeout, 0.0–999.9 s where 0 disables it. The four digits are a
    /// DECIMAL count of tenths of a second — not hex, unlike almost every other numeric field in DCON.
    /// </summary>
    public static string WriteCommWatchdog(int address, float seconds)
    {
        var clamped = seconds < 0f ? 0f : seconds > 999.9f ? 999.9f : seconds;
        var tenths  = (int) Math.Round(clamped * 10);

        return $"${Addr(address)}X{tenths:D4}";
    }

    /// <summary>ICP-7017Z wiring mode: 0 = differential (10 channels), 1 = single-ended (20).</summary>
    public static string ReadWiringMode(int address) => $"@{Addr(address)}S";

    public static string WriteWiringMode(int address, bool singleEnded) => $"@{Addr(address)}S{(singleEnded ? 1 : 0)}";

    /// <summary>ICP-7080 gate mode: 0 low-active, 1 high-active, 2 none.</summary>
    public static string ReadGateMode(int address) => $"${Addr(address)}A";

    public static string WriteGateMode(int address, int mode) => $"${Addr(address)}A{mode}";

    /// <summary>ICP-7080 input signal pair: 0 TTL/TTL, 1 photo/photo, 2 TTL/photo, 3 photo/TTL.</summary>
    public static string ReadInputSignals(int address) => $"${Addr(address)}B";

    public static string WriteInputSignals(int address, int code) => $"${Addr(address)}B{code}";

    public static string ReadCounterPreset(int address, int channel) => $"@{Addr(address)}G{channel}";

    public static string WriteCounterPreset(int address, int channel, long value) =>
        $"@{Addr(address)}P{channel}{value:X8}";

    public static string ResetCounterToPreset(int address, int channel) => $"${Addr(address)}6{channel}";

    /// <summary>Formats an address as the two uppercase hex digits every command embeds.</summary>
    public static string Addr(int address)
    {
        if (address is < MinAddress or > MaxAddress)
            throw new ArgumentOutOfRangeException(nameof(address), address, $"Address must be {MinAddress}-{MaxAddress}.");

        return address.ToString("X2");
    }
}
