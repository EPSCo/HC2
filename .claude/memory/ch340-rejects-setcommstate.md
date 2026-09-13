---
name: ch340-rejects-setcommstate
description: "The bench CH340 adapter (COM5) refuses SetCommState, so System.IO.Ports cannot open it while the Advantech SDK can — a live constraint on HC2's SDK-free design."
metadata: 
  node_type: memory
  type: project
  originSessionId: 6930222d-1614-45c9-a490-5aa0a5cc2a6e
  modified: 2026-09-09T10:05:22.426Z
---

Measured on 2026-09-08 against the CH340 adapter on COM5 (`USB\VID_1A86&PID_7523`, wch.cn driver
4.0.2026.2, which Windows reports as healthy — `Status OK`, `ConfigManagerErrorCode 0`):

- `CreateFile` on `\\.\COM5` — **succeeds**.
- `GetCommState` — **succeeds**, reporting 4800 8/N/1, flags `0x00001091` (the bus's real settings).
- `SetCommState` — **fails, Win32 31 (`ERROR_GEN_FAILURE`)**, even when handed back the DCB just read.
- `System.IO.Ports.SerialPort.Open()` — **fails** for every parameter combination, because opening always
  calls `SetCommState`. Reproducible from plain PowerShell, so it is not an HC2 defect.
- `Advantech.Adam.AdamCom.OpenComPort()` — **succeeds**. Its `SetComPortState` returns false, and the SDK
  keeps the port open anyway, using whatever configuration the port already holds.

Consequence for [[hc2-is-hardwarecontroller-rewrite]]: the assumption that `System.IO.Ports` alone can replace
the Advantech DLLs does not hold on this hardware. **Resolved** — `HC2.Core.Serial.Win32SerialTransport` does
what the SDK does (CreateFile + SetCommTimeouts, tolerating a `SetCommState` refusal when the port already
holds the wanted settings), and `SerialTransportOpener` falls back to it when `SerialPort` cannot open a port.

**Corrected 2026-09-09:** an earlier note here claimed the line rate could not be changed at all on this
adapter. That was wrong, and it was inferred from a standalone `SetCommState` probe rather than measured
through the transport. `Win32SerialTransport` reconfigures the port freely — 4800 → 9600 → 4800 with
`ConfigurationApplied` true each time — and a multi-rate scan found the ADAM-4017P at 4800 and the ICP-7017Z at
9600 on the same sweep. No driver rollback is needed for HC2.

`System.IO.Ports.SerialPort.Open()` still fails at every rate, so the fallback remains load-bearing. Why it
fails is not pinned down: it performs considerably more setup than `SetCommState` alone, and a hypothesis that
the driver only refuses no-op reconfigurations did not survive testing.

Ground truth for the bench bus, read through the SDK at 4800 with checksum disabled: address **01** answers
`!014017P` (ADAM-4017P) and address **02** answers `!024117` (ADAM-4117). Useful for validating HC2's DCON
parsing without hardware access.
