using System;
using System.Collections.Generic;
using HC2.Core.Dcon;

namespace HC2.Core.Modules;

/// <summary>How the ICP-7017Z's inputs are wired, which decides how many channels it has.</summary>
public enum WiringMode
{
    /// <summary>Ten differential inputs.</summary>
    Differential,

    /// <summary>Twenty single-ended inputs.</summary>
    SingleEnded
}

/// <summary>
/// ICP-7017Z: an ICP-DAS analog input module whose channel count depends on how it is wired — ten differential
/// or twenty single-ended.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Wiring"/> is not known until <see cref="TryReadWiringMode"/> has been called, and everything that
/// depends on channel count is wrong until it is: the per-channel command field is one hex digit in
/// differential wiring and two in single-ended, so reading a range with the wrong assumption addresses the
/// wrong channel. Read the wiring mode first.
/// </para>
/// <para>
/// No input-range table is defined here. HardwareController drove this module's ranges as bare integers and
/// never wrote down what the codes mean, and inventing a table would be worse than admitting the gap — use
/// <see cref="AnalogInputModule.TryReadInputRangeCode"/> and fill the table in once it is confirmed against the
/// ICP-DAS manual.
/// </para>
/// </remarks>
public sealed class Icp7017Z : AnalogInputModule
{
    public Icp7017Z(DconClient client, int address, WiringMode wiring = WiringMode.SingleEnded)
        : base(client, address)
        => Wiring = wiring;

    public override ModuleModel Model => ModuleModel.Icp7017Z;

    /// <summary>
    /// Ten differential, twenty single-ended. Single-ended is assumed until the module is asked, because it is
    /// the larger of the two — assuming the smaller would hide channels that exist.
    /// </summary>
    public override int ChannelCount => Wiring == WiringMode.Differential ? 10 : 20;

    /// <summary>Empty by design — see the class remarks.</summary>
    public override IReadOnlyList<InputRange> SupportedRanges => Array.Empty<InputRange>();

    public WiringMode Wiring { get; private set; }

    /// <summary>Reads how the module is wired and updates <see cref="Wiring"/>.</summary>
    public bool TryReadWiringMode(out WiringMode wiring)
    {
        wiring = Wiring;

        if (!Client.Execute(DconCommands.ReadWiringMode(Address), out var response))
        {
            LastError = Client.LastError;
            return false;
        }

        var payload = DconResponse.Payload(response);

        if (!DconResponse.IsAcknowledged(response) || payload.Length == 0)
        {
            LastError = $"Address {Address:X2} answered '{response}' to a wiring-mode read.";
            return false;
        }

        wiring    = payload[0] == '0' ? WiringMode.Differential : WiringMode.SingleEnded;
        Wiring    = wiring;
        LastError = null;

        return true;
    }

    /// <summary>
    /// Changes how the module reads its inputs. This halves or doubles the channel count, so anything holding
    /// per-channel state for this module has to be rebuilt afterwards.
    /// </summary>
    public bool TryWriteWiringMode(WiringMode wiring)
    {
        if (!Client.Execute(DconCommands.WriteWiringMode(Address, wiring == WiringMode.SingleEnded), out var response))
        {
            LastError = Client.LastError;
            return false;
        }

        if (!DconResponse.IsAcknowledged(response))
        {
            LastError = $"Address {Address:X2} answered '{response}' to a wiring-mode write.";
            return false;
        }

        Wiring    = wiring;
        LastError = null;

        return true;
    }

    /// <summary>
    /// One hex digit when there are ten channels, two when there are twenty — a single digit cannot address
    /// channel 10 and above.
    /// </summary>
    protected override string ChannelField(int channel) =>
        Wiring == WiringMode.Differential ? channel.ToString("X1") : channel.ToString("X2");

    /// <summary>
    /// Four digits differential, six single-ended. Wider than one bit per channel strictly needs — that is what
    /// HardwareController sent, and it is the only field width this module has been seen to accept.
    /// </summary>
    protected override int ChannelMaskDigits => Wiring == WiringMode.Differential ? 4 : 6;
}
