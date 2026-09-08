# Porting from HardwareController

A close read of `C:\. Projects\HardwareController`'s `Devices/` and `Security/` (23 files, 4,432 lines),
triaging what HC2 should carry over, adapt, or leave behind.

Most of the value in those lines is not code — it is field-verified knowledge about how ADAM and ICP-DAS
modules actually behave on the wire, written down in comments next to the workarounds it forced.

Legend used throughout:

- **Carry** — knowledge or tables that cost nothing to move and are expensive to rediscover. Copy faithfully.
- **Adapt** — the old design solved a real problem, but the shape should change now that the SDK is gone.
- **Leave** — coupling, dead weight, or defects. Re-typing these would import the bugs along with them.

## One decision gates the whole port

Every byte that reaches a module passes through two methods on `Advantech.Adam.AdamCom`, the base class
`ComPort` inherits: `AdamTransaction(cmd, out result)` for DCON ASCII, and the `Modbus(address)` factory for
RTU. Nothing else in `Devices/` touches the serial line. Those two entry points are the entirety of what the
two Advantech DLLs buy the old app — and the entirety of what HC2 has to replace to stay package-free.

To drop `Advantech.Adam`, `HC2.Core` must implement:

1. **DCON transaction.** Write the command followed by `CR` (`0x0D`), read until the next `CR`, return the
   frame without it. The old code appends `'\r'` at every call site, so the framing is already visible in the
   source.
2. **DCON checksum.** When `Checksum` is on, two hex characters go between the command body and the `CR`.
   *The algorithm is not in the old source* — it lives inside the SDK, and the app only ever flips
   `port.Checksum` as a boolean. This is the one genuine information gap in the port; the ADAM-4000 manual
   specifies it as the low byte of the sum of the frame's ASCII values, but that needs confirming against a
   live module before it is trusted.
3. **Modbus RTU + CRC16.** Needed only for the ADAM-4017P Modbus path — function codes 03, 04, 06 and 16.
   `ComPort.cs` says so explicitly: *"CRC16/RTU framing is handled inside Advantech.Adam.dll, not here; this
   app has no hand-rolled Modbus implementation."*

Items 1 and 3 are well-specified and mechanical. Item 2 is the one that needs hardware in front of you.

## Hardware facts worth more than the code around them

These are the comments that were paid for with a module on a bench. Each records something the manuals either
get wrong or do not say, and every one is protocol-level — none depends on the Advantech SDK, WPF, or .NET 6.

**Carry — `Adam4017.cs` · `ProbeInitMode`, `WriteConfig`**
INIT\* mode overrides address and baud rate. While the module's INIT\* terminal is grounded it answers at
address `0` at a fixed 9600 bps, whatever it is configured for. Baud rate, checksum and protocol can *only* be
changed in this state; attempting it otherwise returns `?AA` and is a hardware limitation, not a bug to chase.

**Carry — `Adam4017.cs` · `SetAddress`**
Seven seconds of silence after any config write. The module auto-calibrates and cannot be addressed at all
during that window. Earlier versions of this code waited well under a second, and a retry loop hammering the
module during the window actively prevented the reconfiguration from completing. The correct sequence is:
write → sleep 7 s → probe `$xxM` at the new address → only then adopt it locally.

**Carry — `Adam4017.cs` / `Adam4117.cs` · `GetModuleConfigAt`**
The `!AA…` reply echoes the module's stored address, not the queried one. Observed live: querying address 0 in
INIT mode gets back `!04…`. Validate the response *shape*, never that `AA` matches what you asked for.

**Carry — `Adam4017.cs` · `ProbeInitMode`**
The first transaction after a baud-rate switch can silently get no reply even when the module is reachable.
Three read-only retries at 300 ms clear it. Only safe for reads — a write-and-retry loop was tried for
`SetAddress` and reverted.

**Carry — `Adam4017ModbusScaling`**
Modbus scaling is straight linear across the full unsigned 16-bit span — `raw / 65535 * (max - min) + min` —
not offset binary. The offset-binary theory fit ±10 V by coincidence and broke on 4~20 mA. Verified against
Advantech's own IO Utility: raw 34341 → 0.483 V at a 0.480 V reference; raw ≈2 → 4.0005 mA at 4.000 mA;
raw 65535 → exactly 20.0 mA on an open channel.

**Carry — `ComPort.cs` · `WriteModbusSingleRegister`**
FC16 multi-register writes fail with a CRC error on this hardware; FC06 single-register writes succeed. Reads
at the same baud rate are reliable, which points at RS-485 half-duplex turnaround timing on the longer write
frame. Configure registers one at a time.

**Carry — `Adam4017.cs` · `TryAdoptExistingModbusConfig`**
Read the existing Type Code before writing one. Without this, every re-run of a module search silently reset a
module the user had deliberately set to 4~20 mA back to the class default of ±10 V.

**Carry — `NodeModuleMapping.OpenAndConfigure`**
The serial driver does not reliably keep its configuration across open. Setting format before opening is not
enough — it must be re-applied after. Normal reads paper over this because every transaction reconfigures the
port first; a direct probe does not, and will scan a port with stale settings and report nothing on a port that
has modules on it.

### Protocol reference, consolidated

Scattered across five module classes today. Worth one table in `HC2.Core`, since three of the five models share
most of it. `AA` is the address as two hex digits; every frame is terminated with `CR`.

| Command | Meaning | Response | Models |
| --- | --- | --- | --- |
| `#AA` | Read all channels, engineering units | `>+00.000…`, 7 chars/channel | 4017P, 4117, 7017Z |
| `#AAN` | Read one counter / encoder, hex | `!AAxxxxxxxx` | 7080, 7083 |
| `$AAM` | Module identification — the discovery probe | `!AA4017P` \| `4117` \| `7017Z` \| `7080` \| `7083` | all |
| `$AAF` | Firmware version | `!AAA2.02` | 4017P, 4117 |
| `$AA2` | Read config: range, baud, flags | `!AATTCCFF` | 4017P, 4117 |
| `%AANNTTCCFF` | Write config — address, range, baud, flags | `!NN` | 4017P, 4117 |
| `$AA6` / `$AA5h` | Channel enable mask, get / set | `!AAhh` | 4017P, 4117, 7017Z |
| `$AA8Cn` / `$AA7CnRtt` | Per-channel input range, get / set | `!AAtt` | 4017P, 4117, 7017Z |
| `$AAY` / `$AAXnnnn` | Comm. watchdog, seconds — *nnnn is decimal tenths, not hex* | `!AA` | 4017P, 4117 |
| `@AAS` / `@AASn` | Wiring mode: 0 = differential (10 ch), 1 = single-ended (20 ch) | `!AAn` | 7017Z |
| `$AAAn` | Gate mode: 0 low-active, 1 high-active, 2 none | `!AAn` | 7080 |
| `$AABn` | Input signal pair: 0 TTL/TTL, 1 photo/photo, 2 TTL/photo, 3 photo/TTL | `!AAn` | 7080 |
| `@AAGn` / `@AAPn…` | Counter preset value, get / set (8 hex digits) | `!AAxxxxxxxx` | 7080 |
| `$AA0H\|0L\|1H\|1L` | Filter widths (5 decimal digits) and trigger voltages (×10) | `!AAnnnnn` | 7080 |

### The FF configuration byte

Layouts differ per family, and it is easy to get wrong.

| Bit | ADAM-4017P (manual fig. 5-1) | ADAM-4117 (manual fig. 4.1) |
| --- | --- | --- |
| 7 | Checksum enabled | Checksum enabled |
| 6 | **Integration time** — 0 = 50 ms/60 Hz mains, 1 = 60 ms/50 Hz | **High-speed mode** — this family has no integration-time bit |
| 5–3 | unused | unused |
| 2 | Protocol — 0 = DCON ASCII, 1 = Modbus | Protocol — 0 = DCON ASCII, 1 = Modbus |
| 1–0 | Data format — 00 = engineering units, the only one the parsers handle | Data format — same constraint |

### ADAM-4017P Modbus register map

Confirmed on hardware.

| Register | FC | Purpose | Note |
| --- | --- | --- | --- |
| 40001–40008 | 03 | Current value, AI0–AI7 | SDK indexing is 1-based: 40001 is `startIndex = 1` |
| 40201–40208 | 06 | Per-channel type code (input range) | Same numeric codes as the DCON TT byte |
| 40221 | 06 | Channel enable bitmask | `0xFF` enables all eight |
| 3xxxx | 04 | Input registers | **Wrong guess — do not re-try.** Read non-monotonically against a real applied signal |

Until type code and channel enable are written, current value reads back as `0xFFFF` — the disabled-channel
sentinel, not data. Raw values are *not* sign-extended despite the `int[]` signature.

### Two things that look like one table and are not

`Adam4017BaudRates` and `Adam4117BaudRates` are byte-identical: codes `0x03`–`0x0A` for 1200 through
115200 bps, both falling back to `0x05` (4800). Collapse them into one. The 4117's Table 4.5 also lists `0x0B`
for 230.4 kbps, dropped because the `Baudrate` enum has no member for it — worth adding rather than silently
omitting.

The input-range enums genuinely differ and must stay separate: the 4017P has seven codes (`0x07`–`0x0D`); the
4117 has those plus ±15 V and a whole unipolar block (`0x48`–`0x55`). Note the 4017P's code `7` is named
`mA_0To20` but described as "4 ~ 20 mA" and scaled as (4, 20) — the description and scaling are the correct
pair, confirmed against the IO Utility. Fix the member name on the way across instead of copying the
contradiction.

## Code worth adapting

**Adapt — `ComPort.ExecutionLock`**
One lock serializing every transaction on a port. Essential on half-duplex RS-485 and the reason DCON and
Modbus traffic never collide. Keep it exactly — but move it into a transport object that owns the `SerialPort`,
rather than onto a class that also holds a module list and raises property-change events.

**Adapt — `IModule` · `Baudrate`, `Parity`, `Databits`, `Stopbits`, `FlowControl`**
Serial format is stored per module, then re-applied to the port on every transaction. The config format already
declares one shared format for every module in the file, so this is per-transaction work that buys nothing.
Hoist format onto the port; leave `Address` and `Checksum` — which really are per-module — on the module.

**Adapt — `Devices/Dto/*.cs`**
The DTO tree is an interop contract, not an internal shape. `NodeConfigDto` is hand-duplicated in Daftar, which
reads these files. If HC2 is to produce configs Daftar consumes, the shape and `SchemaVersion` must stay in
lockstep — including the enum *numeric* values, since they serialize as numbers. Watch `Stopbits.One = 0`
(not 1) and `Baudrate` members equal to literal bps.

**Adapt — `NodeModuleMapping` · `DefaultComPortSettings`, `DefaultTimingSettings`**
Factory defaults are worth keeping as constants: 9600 bps, no parity, 8 data bits, 1 stop bit, no flow control.
Timing defaults split by family — 300 ms for the 4017P and 4117, 100 ms for the 7017Z, 7080 and 7083 — though
the shared config writes 300 for all.

**Leave — `IComPort.Modules`, `AddModule`; `IModule.ComPort`**
The port owns a module list and each module holds a port back-reference. That cycle is what forces
`[XmlIgnore, JsonIgnore]` onto the device models and lets a module reconfigure the shared port as a side
effect. Pass the transport into the call instead of storing it.

## Defects — reasons not to port by re-typing

Found while reading. None are urgent in the old app; all would be inherited free of charge by a faithful copy.

| Site | Defect |
| --- | --- |
| `ICP7080.GateModesSource` | All three dropdown entries map to `GateModes.LowActive`. Picking "High Active" or "None" selects low-active. |
| `ICP7080` · `GateMode`, `Channel0Enabled`, `FilterEnabled`, … | Property setters perform blocking serial writes, and these properties are bound directly to the UI. A combo-box change writes to hardware on the dispatcher thread. |
| `ICP7083.SendCommand` | Sets `LastError = "Failed to execute the command"` unconditionally, including on success. `Adam4117.SendCommand` does the same. |
| `ICP7080.Channel0Value` / `Channel1Value` | `long.Parse(hexValue, HexNumber)` on a string that is null until the first read — throws rather than reporting no data. |
| `ComPort.Modules` | Returns `new ReadOnlyObservableCollection<…>(_modules)` on every get, so any binding to it stops receiving collection-change notifications. |
| `ComPort.SetComPortState` | `ErrorCode error; if (!success) error = LastError;` — assigned, never read. The failure is swallowed and the method still returns it as if handled. |
| `ComPort.ExecuteCommand` | The result of `OpenComPort()` is assigned to a local and discarded; the transaction proceeds even when the open failed. |
| `Adam4017.Read`, `Adam4117.Read`, `ICP7017Z.Read` | `result.Remove(0, 1)` then `result[1]` — a short or empty response indexes out of range instead of failing cleanly. |
| `ConfigFileCipher.Decrypt` | Strips *all* trailing zero bytes after PKCS7 unpadding. Harmless for JSON, silent corruption for anything else. |
| `ComPort.cs` lines 29–232 | Roughly 200 lines of hand-rolled `SetProperty` / `OnPropertyChanged` overloads, most unused. `HC2.App` already has a four-method `ViewModelBase`. |

## Security/ — one file, one real decision

`ConfigFileCipher` encrypts exported `.dhw` config files. It is 92 lines and it is the sole reason
`BouncyCastle.NetCore` is a dependency.

### It is not AES, and that matters

The file's own summary calls it "AES-256 (Rijndael/CBC/PKCS7)", but it constructs `new RijndaelEngine(256)` —
Rijndael with a **256-bit block**. AES fixes the block size at 128 bits, so this is Rijndael, not AES, and
`System.Security.Cryptography.Aes` cannot read these files at any key size. That single constructor argument is
the whole dependency: swap it for a BCL `Aes` and every existing `.dhw` file becomes unreadable.

The layout is a 32-byte salt, then a 32-byte IV, then ciphertext; the key is PBKDF2 with 1000 iterations of
SHA-1. The passphrase is a hardcoded GUID pair in the source, which the file's own comment is honest about: it
deters casual inspection and nothing more. Treat the encryption as obfuscation, because that is what it is.

### The choice

If HC2 must read `.dhw` files that already exist, BouncyCastle stays — it is the only dependency in the old app
that is genuinely load-bearing, and reimplementing 256-bit-block Rijndael to avoid it would be a bad trade. If
plain `.dconf` JSON is acceptable going forward, the file and the package both disappear.

One consequence of net48 to settle alongside it: the framework has no built-in JSON serializer. Dropping
Newtonsoft means either the `System.Text.Json` package, or `DataContractJsonSerializer` from the BCL — which
will not round-trip the current DTO shape without attributes. "Zero packages" and "JSON config files" are in
tension on this target framework, and that is worth deciding deliberately rather than discovering later.

## Where this lands

The shape that falls out of the above: a transport that owns the port and the lock, two stateless protocol
clients above it, and module types that are just command vocabularies — no property-change plumbing, no
serialization attributes, no back-reference to the port.

```
HC2.Core
├─ Serial/
│    SerialPortScanner.cs      — done
│    SerialTransport.cs        — owns SerialPort + the one lock; Transact(frame) → frame
├─ Dcon/
│    DconClient.cs             — framing, CR, checksum; no state
│    DconCommands.cs           — the command table above, one place
│    BaudRateCodes.cs          — 0x03..0x0A, was duplicated per family
├─ Modbus/
│    ModbusRtuClient.cs        — CRC16 + FC03/04/06/16
├─ Modules/
│    Adam4017P.cs  Adam4117.cs
│    Icp7017Z.cs   Icp7080.cs  Icp7083.cs
│    InputRanges.cs            — per-family range codes + Modbus scaling
└─ Config/
     NodeConfigDto.cs …        — shape frozen by Daftar; SchemaVersion 1
```

Suggested order of work: the transport and DCON client first, because `$AAM` discovery is what makes the
existing COM-port list useful and it exercises framing without risking any write. The 4017P next, since it
carries every hard-won fact worth having. The 7080 and 7083 last — they are the thinnest classes and the ones
whose old code should be re-derived rather than translated.

---

Source: `C:\. Projects\HardwareController` · `Devices/` and `Security/` · read 2026-09-08.
Also published as an artifact: <https://claude.ai/code/artifact/a2cbaf2b-3167-4d99-b293-12ad238e6557>
