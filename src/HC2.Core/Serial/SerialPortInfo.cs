namespace HC2.Core.Serial;

/// <summary>Whether the port can currently be opened.</summary>
public enum PortAvailability
{
    /// <summary>Not probed — the scan ran without <c>probeAvailability</c>.</summary>
    Unknown,

    /// <summary>The port opened cleanly, so nothing else is holding it.</summary>
    Free,

    /// <summary>Another process already has the port open.</summary>
    InUse,

    /// <summary>The port is listed but could not be opened for some other reason.</summary>
    Error
}

/// <summary>One COM port as reported by the OS, enriched with its PnP device details.</summary>
public sealed record SerialPortInfo
{
    /// <summary>Port name as <see cref="System.IO.Ports.SerialPort"/> expects it, e.g. <c>COM3</c>.</summary>
    public string           PortName     { get; init; } = string.Empty;

    /// <summary>Friendly name from the device manager, e.g. <c>USB Serial Port (COM3)</c>.</summary>
    public string           FriendlyName { get; init; } = string.Empty;

    public string           Description  { get; init; } = string.Empty;
    public string           Manufacturer { get; init; } = string.Empty;

    /// <summary>PnP device id, e.g. <c>USB\VID_0403&amp;PID_6001\A50285BI</c>.</summary>
    public string           DeviceId     { get; init; } = string.Empty;

    /// <summary>USB vendor id (<c>0403</c>) when the port sits behind a USB device.</summary>
    public string?          VendorId     { get; init; }

    /// <summary>USB product id (<c>6001</c>) when the port sits behind a USB device.</summary>
    public string?          ProductId    { get; init; }

    /// <summary>Driver status string from WMI, e.g. <c>OK</c>.</summary>
    public string           Status       { get; init; } = string.Empty;

    /// <summary>True when the port came from WMI as well as <c>GetPortNames</c>.</summary>
    public bool             IsPnpDevice  { get; init; }

    public PortAvailability Availability { get; init; } = PortAvailability.Unknown;

    /// <summary>Numeric part of <see cref="PortName"/>, used for natural ordering (COM2 before COM10).</summary>
    public int PortNumber =>
        PortName.Length > 3 && int.TryParse(PortName.Substring(3), out var number) ? number : int.MaxValue;

    public override string ToString() => string.IsNullOrEmpty(FriendlyName) ? PortName : FriendlyName;
}
