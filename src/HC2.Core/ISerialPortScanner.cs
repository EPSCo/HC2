using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HC2.Core;

/// <summary>Enumerates the serial ports the machine currently exposes.</summary>
public interface ISerialPortScanner
{
    /// <param name="probeAvailability">
    /// Open each port briefly to find out whether it is free. This asserts DTR/RTS for a moment, so leave it
    /// off when a connected device might react to that.
    /// </param>
    Task<IReadOnlyList<SerialPortInfo>> ScanAsync(bool              probeAvailability = false,
                                                  CancellationToken cancellationToken = default);
}
