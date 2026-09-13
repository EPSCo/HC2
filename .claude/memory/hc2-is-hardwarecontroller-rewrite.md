---
name: hc2-is-hardwarecontroller-rewrite
description: "HC2 is a clean, low-dependency rewrite of the HardwareController diagnostic tool; Core stays UI-free, App is plain WPF."
metadata: 
  node_type: memory
  type: project
  originSessionId: 6930222d-1614-45c9-a490-5aa0a5cc2a6e
  modified: 2026-09-08T15:52:54.723Z
---

`C:\. Projects\HC2` (started 2026-09-08) is a from-scratch rewrite of `C:\. Projects\HardwareController`,
the standalone COM-port / ADAM-ICP module diagnostic tool. The explicit goal is **lower dependency**: the old
one pulls DevExpress, ReactiveUI, NLog, Newtonsoft.Json, BouncyCastle, System.Configuration.ConfigurationManager
and the two `Advantech.*` DLLs; HC2 currently has **zero NuGet packages** (BCL + a `System.Management`
framework reference only).

Deliberate structure, confirmed by the user:

- `src/HC2.Core` — UI-free library, so the code can be reused from Daftar / HardwareController later.
- `src/HC2.App` — plain WPF with hand-rolled MVVM (`ViewModelBase`, `AsyncRelayCommand`), no DevExpress and no
  ReactiveUI, so it builds without the licensed DevExpress feed.

Target framework is **net48**, chosen by the user over the net6.0 the rest of the codebase uses — so
`init`/`record` need the `IsExternalInit` shim in HC2.Core, and BCL APIs newer than .NET Framework 4.8
(`AsSpan`, `SupportedOSPlatform`, …) are unavailable.

First feature landed: COM port discovery (`SerialPortScanner` merges `SerialPort.GetPortNames()` with a WMI
`Win32_PnPEntity` query) shown in a port list + detail pane. The equivalent old-app surface to compare against
is `HardwareController/UI/ModuleFinderView.xaml` and `ComPortSettingsDialog.xaml`.

**Read `HC2/docs/porting-from-hardwarecontroller.md` before touching device/protocol code.** It is a full
triage of the old `Devices/` and `Security/` — the DCON command table, the field-verified hardware facts
(INIT* mode, the 7-second post-write settle, Modbus scaling), 10 defects not to copy, and the two open
decisions: the DCON checksum algorithm (not in the old source, lives inside the Advantech SDK) and whether
`.dhw` compatibility forces BouncyCastle to stay.
