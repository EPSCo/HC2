using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HC2.Core;

/// <summary>
/// Lists COM ports by combining two sources: <see cref="SerialPort.GetPortNames"/> (authoritative for what can
/// actually be opened) and the WMI <c>Win32_PnPEntity</c> table (friendly name, manufacturer, VID/PID). Ports
/// that only one source knows about are still returned.
/// </summary>
public sealed class SerialPortScanner : ISerialPortScanner
{
    private const string PnpQuery =
        "SELECT Caption, Description, Manufacturer, DeviceID, Status FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'";

    private static readonly Regex PortInCaption = new(@"\((COM\d+)\)", RegexOptions.Compiled);
    private static readonly Regex UsbIds        = new(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})",
                                                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<IReadOnlyList<SerialPortInfo>> ScanAsync(bool              probeAvailability = false,
                                                          CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(probeAvailability, cancellationToken), cancellationToken);

    private static IReadOnlyList<SerialPortInfo> Scan(bool probeAvailability, CancellationToken cancellationToken)
    {
        var ports = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in SerialPort.GetPortNames())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalized = name.TrimEnd();

            ports[normalized] = new SerialPortInfo { PortName = normalized };
        }

        foreach (var device in QueryPnpDevices(cancellationToken))
            ports[device.PortName] = device;

        if (probeAvailability)
        {
            foreach (var key in ports.Keys.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                ports[key] = ports[key] with { Availability = Probe(key) };
            }
        }

        return ports.Values.OrderBy(p => p.PortNumber)
                           .ThenBy(p => p.PortName, StringComparer.OrdinalIgnoreCase)
                           .ToList();
    }

    private static IEnumerable<SerialPortInfo> QueryPnpDevices(CancellationToken cancellationToken)
    {
        var devices = new List<SerialPortInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(PnpQuery);
            using var results  = searcher.Get();

            foreach (var item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var device = (ManagementObject)item;

                var caption = Value(device, "Caption");
                var match   = PortInCaption.Match(caption);

                if (!match.Success)
                    continue;

                var deviceId = Value(device, "DeviceID");
                var usb      = UsbIds.Match(deviceId);

                devices.Add(new SerialPortInfo
                {
                    PortName     = match.Groups[1].Value,
                    FriendlyName = caption,
                    Description  = Value(device, "Description"),
                    Manufacturer = Value(device, "Manufacturer"),
                    DeviceId     = deviceId,
                    Status       = Value(device, "Status"),
                    VendorId     = usb.Success ? usb.Groups[1].Value.ToUpperInvariant() : null,
                    ProductId    = usb.Success ? usb.Groups[2].Value.ToUpperInvariant() : null,
                    IsPnpDevice  = true
                });
            }
        }
        catch (ManagementException)
        {
            // WMI unavailable or the query was refused — fall back to the bare GetPortNames list.
        }

        return devices;
    }

    /// <summary>Opens and immediately closes the port to see whether anything else is holding it.</summary>
    private static PortAvailability Probe(string portName)
    {
        try
        {
            using var port = new SerialPort(portName);
            port.Open();

            return PortAvailability.Free;
        }
        catch (UnauthorizedAccessException)
        {
            return PortAvailability.InUse;
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
        {
            return PortAvailability.Error;
        }
    }

    private static string Value(ManagementBaseObject device, string property)
    {
        try
        {
            return device[property]?.ToString() ?? string.Empty;
        }
        catch (ManagementException)
        {
            return string.Empty;
        }
    }
}
