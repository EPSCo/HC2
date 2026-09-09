using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using HC2.Core.Dcon;

namespace HC2.Core.Modules;

/// <summary>
/// Shared behaviour of the DCON analog input modules. Holds an address and a client; the transport and its lock
/// live below, and nothing points back up, so a module is cheap to create and safe to discard.
/// </summary>
/// <remarks>
/// Deliberately not observable and not serializable. HardwareController's module classes implemented
/// <c>INotifyPropertyChanged</c>, carried <c>[XmlIgnore, JsonIgnore]</c> attributes, and performed blocking
/// serial writes inside property setters bound directly to the UI. Configuration is persisted through the
/// config DTOs instead, and every operation here is an explicit call.
/// </remarks>
public abstract class AnalogInputModule
{
    /// <summary>
    /// How long a module is unreachable after accepting a configuration write, per the manual: "An analog input
    /// module requires a maximum of 7 seconds to perform auto calibration and ranging after it is reconfigured.
    /// During this time span, the module cannot be addressed to perform any other actions." Earlier versions of
    /// the old app waited well under a second, and a retry loop running inside this window actively prevented
    /// the reconfiguration from completing.
    /// </summary>
    public const int AutoCalibrationMs = 7000;

    /// <summary>
    /// What a channel reads when it has nothing to measure — disabled, open, or below range. Observed on the
    /// bench: an ADAM-4017P with two unconnected inputs returned exactly this on those channels while the rest
    /// read normally. It is a marker, not a measurement, and must never reach a calculation.
    /// </summary>
    public const double UnderRange = -999999;

    /// <summary>True for a value that is <see cref="UnderRange"/> or not a reading at all.</summary>
    public static bool IsMeasurement(double value) => !double.IsNaN(value) && value > UnderRange + 1;

    protected AnalogInputModule(DconClient client, int address)
    {
        Client  = client ?? throw new ArgumentNullException(nameof(client));
        Address = address;
    }

    public DconClient Client { get; }

    /// <summary>Updated only after a live probe confirms the module answers at a new address.</summary>
    public int Address { get; private set; }

    public abstract ModuleModel Model { get; }

    public abstract int ChannelCount { get; }

    public abstract IReadOnlyList<InputRange> SupportedRanges { get; }

    public string? LastError { get; protected set; }

    /// <summary>Reads every channel in engineering units.</summary>
    public bool TryReadChannels(out double[] values)
    {
        if (Client.TryReadChannels(Address, out values))
        {
            LastError = null;
            return true;
        }

        LastError = Client.LastError;
        return false;
    }

    public bool TryReadFirmwareVersion(out string version)
    {
        version = string.Empty;

        if (!Execute(DconCommands.FirmwareVersion(Address), out var response))
            return false;

        if (!DconResponse.IsAcknowledged(response))
            return Fail($"Could not read the firmware version (answered '{response}').", out version);

        version = DconResponse.Payload(response);
        return true;
    }

    /// <summary>Reads the module-wide configuration at this module's own address.</summary>
    public bool TryReadConfig(out AnalogModuleConfig config) => TryReadConfigAt(Address, out config);

    /// <summary>
    /// Reads the configuration at an explicit address — used for INIT-mode work, where a module always answers
    /// at address 0 no matter what it is configured for.
    /// </summary>
    public bool TryReadConfigAt(int address, out AnalogModuleConfig config)
    {
        config = new AnalogModuleConfig();

        if (!Execute(DconCommands.ReadConfig(address), out var response))
            return false;

        // Validate the shape only. The AA field is the module's own stored address and need not echo the
        // address just queried.
        if (!DconResponse.IsAcknowledged(response) || response.Length < 9)
            return Fail($"Address {address:X2} answered '{response}' to a configuration read.");

        if (!TryHex(response.Substring(1, 2), out var reported)
            || !TryHex(response.Substring(3, 2), out var range)
            || !TryHex(response.Substring(5, 2), out var baud)
            || !TryHex(response.Substring(7, 2), out var flags))
            return Fail($"Could not parse the configuration in '{response}'.");

        config = new AnalogModuleConfig
        {
            ReportedAddress = reported,
            InputRangeCode  = (byte) range,
            BaudRateCode    = (byte) baud,
            Flags           = (byte) flags
        };

        return true;
    }

    /// <summary>
    /// Writes address, input range, baud rate and flags in one round trip.
    /// </summary>
    /// <remarks>
    /// Every field is written on every call, so read the current configuration first and change only what you
    /// mean to. Baud rate, checksum and protocol only change while the module's INIT* terminal is grounded;
    /// without it the module answers <c>?AA</c>, which surfaces here as an ordinary failure. After a write the
    /// module is unreachable for up to <see cref="AutoCalibrationMs"/>.
    /// </remarks>
    public bool TryWriteConfig(int fromAddress, int newAddress, AnalogModuleConfig config, out int echoedAddress)
    {
        echoedAddress = -1;

        if (newAddress is < DconCommands.MinAddress or > DconCommands.MaxAddress)
            return Fail($"Address must be {DconCommands.MinAddress}-{DconCommands.MaxAddress}.");

        var command = DconCommands.WriteConfig(fromAddress, newAddress,
                                                config.InputRangeCode, config.BaudRateCode, config.Flags);

        if (!Execute(command, out var response))
            return false;

        if (!DconResponse.IsAcknowledged(response))
            return Fail($"The module rejected the configuration (answered '{response}'). Changing the baud " +
                        "rate, checksum or protocol requires the module's INIT* terminal to be grounded.");

        if (!DconResponse.TryGetReportedAddress(response, out echoedAddress))
            return Fail($"Could not read the address echoed in '{response}'.");

        return true;
    }

    /// <summary>Reads which channels are enabled.</summary>
    public bool TryReadChannelMask(out bool[] enabled)
    {
        enabled = Array.Empty<bool>();

        if (!Execute(DconCommands.ReadChannelMask(Address), out var response))
            return false;

        var payload = DconResponse.Payload(response);

        if (!DconResponse.IsAcknowledged(response) || !TryHex(payload, out var mask))
            return Fail($"Address {Address:X2} answered '{response}' to a channel-enable read.");

        // Bit i of the mask is channel i. HardwareController arrived at the same mapping through a BitArray
        // built most-significant-bit first and then indexed in reverse; this is that, without the detour.
        enabled = new bool[ChannelCount];

        for (var channel = 0; channel < ChannelCount; channel++)
            enabled[channel] = ((mask >> channel) & 1) == 1;

        return true;
    }

    /// <summary>Writes which channels are enabled.</summary>
    public bool TryWriteChannelMask(bool[] enabled)
    {
        if (enabled == null || enabled.Length != ChannelCount)
            return Fail($"Expected {ChannelCount} channel flags, got {enabled?.Length ?? 0}.");

        var mask = 0;

        for (var channel = 0; channel < enabled.Length; channel++)
            if (enabled[channel])
                mask |= 1 << channel;

        // Fixed width, so a mask with a zero high nibble is still sent as the full field. The old app formatted
        // with "X1", which is a minimum width rather than a fixed one, so the digit count varied with the value.
        if (!Execute(DconCommands.WriteChannelMask(Address, mask, ChannelMaskDigits), out var response))
            return false;

        return DconResponse.IsAcknowledged(response)
            || Fail($"Address {Address:X2} answered '{response}' to a channel-enable write.");
    }

    /// <summary>
    /// Reads a channel's input range as the raw type code, without requiring the model to have a documented
    /// range table. The ICP-7017Z has no established table in this codebase, so this is the only form available
    /// for it — HardwareController likewise carried its 7017Z ranges as bare integers.
    /// </summary>
    public bool TryReadInputRangeCode(int channel, out byte code)
    {
        code = 0;

        if (!Execute(DconCommands.ReadInputRange(Address, ChannelField(channel)), out var response))
            return false;

        var payload = DconResponse.Payload(response);

        // Parse the payload rather than a fixed number of trailing characters. HardwareController took the last
        // two characters for the 4017P and the last three for the 4117; the three-character form reads one
        // character of the address as part of the code and cannot be right for both.
        if (!DconResponse.IsAcknowledged(response) || !TryHex(payload, out var value) || value is < 0 or > 255)
            return Fail($"Address {Address:X2} answered '{response}' to an input-range read.");

        code = (byte) value;
        return true;
    }

    /// <summary>Reads a channel's input range and resolves it against the model's range table.</summary>
    public bool TryReadInputRange(int channel, out InputRange? range)
    {
        range = null;

        // Checked before transacting: with no table the answer cannot be resolved whatever comes back, and
        // there is no reason to occupy the bus to find that out.
        if (SupportedRanges.Count == 0)
            return Fail($"{Model} has no range table here; read the raw code with {nameof(TryReadInputRangeCode)}.");

        if (!TryReadInputRangeCode(channel, out var code))
            return false;

        range = InputRanges.Find(SupportedRanges, code);

        return range != null
            || Fail($"Channel {channel} reports input range 0x{code:X2}, which {Model} does not list.");
    }

    public bool TryWriteInputRange(int channel, byte code)
    {
        // A model with no range table cannot have its codes validated here; the module itself rejects a bad one.
        if (SupportedRanges.Count > 0 && InputRanges.Find(SupportedRanges, code) == null)
            return Fail($"{Model} does not accept input range 0x{code:X2}.");

        if (!Execute(DconCommands.WriteInputRange(Address, ChannelField(channel), code), out var response))
            return false;

        return DconResponse.IsAcknowledged(response)
            || Fail($"Address {Address:X2} answered '{response}' to an input-range write.");
    }

    /// <summary>Reads the communication watchdog timeout in seconds; 0 means disabled.</summary>
    public bool TryReadCommWatchdog(out float seconds)
    {
        seconds = 0;

        if (!Execute(DconCommands.ReadCommWatchdog(Address), out var response))
            return false;

        var payload = DconResponse.Payload(response);

        // Decimal tenths of a second, not hex — the one numeric field in this command set that is not hex.
        if (!DconResponse.IsAcknowledged(response)
            || !int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tenths))
            return Fail($"Address {Address:X2} answered '{response}' to a watchdog read.");

        seconds = tenths / 10f;
        return true;
    }

    public bool TryWriteCommWatchdog(float seconds)
    {
        if (!Execute(DconCommands.WriteCommWatchdog(Address, seconds), out var response))
            return false;

        return DconResponse.IsAcknowledged(response)
            || Fail($"Address {Address:X2} answered '{response}' to a watchdog write.");
    }

    /// <summary>
    /// Reads the configuration of whatever module currently has its INIT* terminal grounded.
    /// </summary>
    /// <remarks>
    /// In that state a module always answers at address 0 and always at 9600 bps, whatever it is configured
    /// for, so the caller must point the port at 9600 first — this only sends the probe. The read is retried,
    /// because the first transaction after a rate change has been seen to go unanswered on healthy hardware.
    /// </remarks>
    public bool TryProbeInitMode(out AnalogModuleConfig config)
    {
        config = new AnalogModuleConfig();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (TryReadConfigAt(0, out config))
                return true;

            if (attempt < 3)
                Thread.Sleep(300);
        }

        return Fail("Nothing answered at address 0. Ground the module's INIT* terminal and set the port to 9600 bps.");
    }

    /// <summary>
    /// Moves the module to a new address, preserving everything else, then confirms it actually answers there
    /// before adopting the change locally. On any failure the old address stays in effect.
    /// </summary>
    /// <param name="settleMs">Auto-calibration wait. Only lower it if you know the module is faster.</param>
    public bool TrySetAddress(int newAddress, int settleMs = AutoCalibrationMs)
    {
        if (newAddress is < DconCommands.MinAddress or > DconCommands.MaxAddress)
            return Fail($"Address must be {DconCommands.MinAddress}-{DconCommands.MaxAddress}.");

        if (!TryReadConfig(out var config))
            return false;

        if (!TryWriteConfig(Address, newAddress, config, out var echoed))
            return false;

        if (echoed != newAddress)
            return Fail($"The module accepted the command but echoed address {echoed:X2}, not {newAddress:X2}.");

        Thread.Sleep(settleMs);

        if (!Client.Responds(newAddress))
            return Fail($"The module accepted the address change but did not answer at {newAddress:X2} after " +
                        $"{settleMs} ms. Run a fresh module search to locate it.");

        Address = newAddress;
        return true;
    }

    /// <summary>
    /// How a channel number is written into a per-channel command. One hex digit for the eight-channel modules;
    /// the ICP-7017Z needs two in single-ended wiring, which is why this is overridable.
    /// </summary>
    protected virtual string ChannelField(int channel) => channel.ToString("X1");

    /// <summary>
    /// Hex digits in the channel-enable mask field. Enough to carry one bit per channel by default; the
    /// ICP-7017Z uses wider fields than its channel count strictly needs.
    /// </summary>
    protected virtual int ChannelMaskDigits => (ChannelCount + 3) / 4;

    private bool Execute(string command, out string response)
    {
        if (Client.Execute(command, out response))
        {
            // Cleared on every successful exchange so a stale message from an earlier failure cannot be read
            // as describing the call that just succeeded.
            LastError = null;
            return true;
        }

        LastError = Client.LastError;
        return false;
    }

    private bool Fail(string reason)
    {
        LastError = reason;
        return false;
    }

    private bool Fail(string reason, out string cleared)
    {
        cleared = string.Empty;
        return Fail(reason);
    }

    private static bool TryHex(string text, out int value) =>
        int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
