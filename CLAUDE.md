# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

HC2 is a from-scratch rewrite of `C:\. Projects\HardwareController` — the standalone diagnostic tool for finding
and reading Advantech ADAM / ICP-DAS analog-input modules on serial COM ports. The goal of the rewrite is **low
dependency**: no DevExpress, no ReactiveUI, no Advantech SDK DLLs, and currently **zero NuGet packages**.

The parent `C:\. Projects\CLAUDE.md` describes Daftar (net6.0, MS DI, MediatR, LiteDB, DevExpress). **None of
that applies here.** HC2 shares only the hardware domain and the commit-message style.

## Build and run

Targets **net48** with `LangVersion 10.0`, `Nullable` enabled and `ImplicitUsings` disabled, in both projects.

```bash
dotnet build HC2.sln
```

```bash
dotnet run --project src/HC2.App/HC2.App.csproj
```

There are no tests. Verifying protocol or transport changes means running against the bench bus (see below).

net48 consequences to keep in mind:
- `record` / `init` work only because of the `IsExternalInit` shim in `src/HC2.Core/IsExternalInit.cs`.
- BCL APIs newer than .NET Framework 4.8 (`AsSpan`, `SupportedOSPlatform`, `System.Text.Json`, …) are
  unavailable. Adding a package to get one is a deliberate decision, not a default — ask first.
- `System.IO.Ports` lives in `System.dll`; `System.Management` (WMI) is an explicit framework reference.

## Architecture

```
HC2.App (WPF, WinExe) ──► HC2.Core (UI-free class library)
```

`HC2.Core` must stay free of WPF and any UI types so it can later be reused from Daftar / HardwareController.

### HC2.Core — layered bottom-up

- **`Serial/`** — `ISerialTransport` owns the port and the *single lock* serializing every transaction
  (RS-485 is half-duplex). Two implementations:
  - `SerialTransport` over `System.IO.Ports.SerialPort`; re-applies settings *after* `Open()` because the driver
    does not reliably keep them across open.
  - `Win32SerialTransport` over `CreateFile`/`SetCommTimeouts` (`NativeSerial`), which tolerates a
    `SetCommState` refusal when the port already holds the wanted settings.
  - `SerialTransportOpener.Open` tries `SerialPort` first and falls back to Win32. **The fallback is
    load-bearing**: the bench CH340 adapter on COM5 cannot be opened by `SerialPort` at any rate.
  - `SerialPortScanner` merges `SerialPort.GetPortNames()` with a WMI `Win32_PnPEntity` query.
- **`Dcon/`** — `DconClient` is stateless framing over a transport: command + optional checksum + `CR`.
  `DconCommands` is the one command table; `DconResponse` parses replies; `ModuleFinder` sweeps
  address × baud × format × checksum across ports (address is the outer loop) into `DiscoveredModule` records.
- **`Modules/`** — `AnalogInputModule` subclasses (`Adam4017P`, `Adam4117`, `Icp7017Z`) are command vocabularies
  over a `DconClient` + address, with no property-change plumbing and no back-reference to a port.
  `ModuleFactory` maps a `ModuleModel` to a class; ICP-7080 / 7083 are recognized by discovery but not driven yet.
- **`BusProtocol`** — `ModbusRtu` exists as a setting only; there is no Modbus client yet.

### HC2.App — plain WPF, hand-rolled MVVM

- No DI container: `App.OnStartup` constructs `MainWindowViewModel(new SerialPortScanner())` directly.
- `Mvvm/` holds `ViewModelBase`, `RelayCommand`, `AsyncRelayCommand`, `CheckableOption`. Use these; do not add
  an MVVM framework.
- `MainWindowViewModel` composes `PortSettingsViewModel` (the multi-select ribbon: ports, baud rates, checksum,
  formats, protocol — the scan's search space), `ModuleScanViewModel` and `LiveDataViewModel`.
- `Interop/DeviceChangeNotifier` hooks `WM_DEVICECHANGE`; the port list refreshes on startup and on every
  device-tree change — there is no refresh button.
- `LiveDataViewModel` reads each module under the settings it *answered under during the scan* (modules on one
  line can run at different rates), and all serial work runs on a background task, never the dispatcher.
- Styles live in `src/HC2.App/Theme.xaml`. Match the old tool's look
  (`C:\. Projects\HardwareController\UI\Styles\AppStyles.xaml`) rather than inventing new styling.

## Hardware rules — read before touching protocol or device code

**`docs/porting-from-hardwarecontroller.md` is required reading.** It holds the DCON command table, the FF
config-byte layouts, the Modbus register map, and defects in the old code not to re-type. The facts most likely
to bite:

- Never retry a **write**. `DconClient.ExecuteWithRetry` is for read-only commands only; after a config write a
  module is silent for ~7 s while it auto-calibrates, and hammering it prevents the write from completing.
- The `!AA…` reply echoes the module's *stored* address — validate response shape, not that `AA` matches.
- INIT\* mode (terminal grounded) answers at address 0, 9600 bps; baud / checksum / protocol changes only work
  there.
- `DconChecksum` is **unverified against hardware** (manual's sum-mod-256 algorithm). Scans default to checksum
  off; treat checksum-on hits with suspicion.
- No blocking serial I/O in property setters or on the UI thread.

Bench ground truth (COM5, CH340): address `01` → `!014017P` at 4800, address `02` → `!024117`, and an ICP-7017Z
answering at 9600 on the same line.

## Conventions

- File-scoped namespaces, explicit `using`s at the top, and **column-aligned** field declarations and assignment
  blocks — match the surrounding alignment when editing.
- XML doc comments explain *why* (often a hardware observation), not just what. Keep that density in Core.
- Methods on protocol / transport types report failure as `bool Try…(…, out …)` plus a `LastError` string, not
  exceptions.
- Commit subjects: `[Feat] ...`, `[Fix] ...`, `[Docs] ...`.
- `.claude/memory/` is checked in as the project's shared Claude memory.
