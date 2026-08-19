# Hardware Audit Toolkit

Portable sysadmin hardware audit tool — v1 scope: keyboard, mouse, monitor,
system info, and CPU stress testing. Portable, offline-first, no admin
required, no database. See `hardware-audit-toolkit-architecture.md` for the
full architecture and phase plan.

## Solution layout

```
Src/
  HardwareAuditToolkit.sln
  Core/            # contracts (ITestModule, TestStatus), session models, TestOrchestrator — no UI/Win32 refs
  Infrastructure/  # Win32 wrappers, WMI/CIM, LibreHardwareMonitor sensor adapter
  App/             # WPF host: DI shell, single-instance enforcer, app manifest, publish profiles
  Tests/           # xunit
docs/
  DeploymentNote.md  # §9.1 one-pager for security teams (hash, extraction path, signing)
```

## Build & test

```powershell
dotnet build Src\HardwareAuditToolkit.sln
dotnet test  Src\HardwareAuditToolkit.sln
```

## Publish

```powershell
# Primary: portable, self-contained, single-file .exe (§9.1)
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableSingleFile
# Fallback: self-contained folder build
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableFolder
```

Outputs land in `Src\App\bin\publish\`.

## Phase status

- **Phase 0 (scaffolding) — done:** solution, contracts in Core, DI shell,
  Per-Monitor V2 manifest, `Global\` single-instance enforcement, publish
  profiles, orchestrator unit tests.
- **Phase 1 (shell, navigation & exit) — done:** main window with persistent
  header + dashboard, DI shell, `Global\` single-instance enforcement, Per-Monitor
  V2 manifest (§9.4), global Ctrl+E low-level hook on its **own dedicated thread**
  (§9.2) routed to the orchestrator's exit flow, reusable **Exit Test** overlay (§6),
  the `AuditSession` model, a hidden message-only window wired for
  `WM_INPUT_DEVICE_CHANGE`/`WM_DISPLAYCHANGE` (§9.5), a navigation service, and
  orchestrator unit tests (12 passing).
- **Phase 2 (system info & live sensors) — done:** `SystemInfoProvider` (WMI) +
  `LibreHardwareMonitorSensorProvider` (on the event bus via `SensorReadingsMessage`),
  `SystemInfoModule` (non-exclusive) and the §8 CPU stress module (exclusive, one
  `BelowNormal` worker per core, 5-min cap, manual Stop, live telemetry), plus
   `SystemInfoView`/`CpuStressView` wired through `NavigationService` + `MainWindow`
   data templates. 16 tests pass (orchestrator + new `Phase2ModuleTests`).
- **Phase 3 (keyboard test) — done:** `RawKeyboardInput` (Infrastructure, native
   message-only window, scan-code based) + ANSI `KeyboardLayout`; exclusive
   `KeyboardTestModule` (per-key untested→pressed→confirmed, operator confirmation
   is the recorded status); `KeyboardTestView`/`KeyboardTestModuleViewModel` driven
   through the orchestrator and the event bus (`KeyEventMessage`/`KeyboardTestStatusMessage`);
   WPM/accuracy sub-screen launched from within the module (not a separate
   exclusive module); `Esc`-is-just-data (no exit handler, `Ctrl+E` still exits via
   the independent hook); raw-input registration torn down on `Cancel()` **and** on
   view-model disposal so navigation doesn't leak capture. 24 tests pass
   (`TestOrchestratorTests` + `Phase2ModuleTests` + `KeyboardModuleTests`).
- **Phase 4 (mouse test) — done:** `RawMouseInput` (Infrastructure, native
   message-only window, scan-agnostic button/wheel/delta stream) + `MouseTestModule`
   (exclusive, one raw-input registration torn down on `Cancel()` **and** view-model
   disposal so navigation doesn't leak capture); `MouseTestModuleViewModel`
   + `MouseTestView` via `NavigationService` + `MainWindow` data templates. Click /
   scroll / drag-hold are logged with per-button counters; a drag is distinctly
   flagged from a click ("released mid-drag — drop detected"), and a mouse unplug
   mid-hold is recorded as an incomplete drag/drop finding (§9.5, no freeze). A
   duck-outline tracing sub-screen (held-button trace scored by path coverage)
   launches from within the module like the keyboard WPM sub-screen. 30 tests pass
   (orchestrator + `KeyboardModuleTests` + `Phase2ModuleTests` + `MouseModuleTests`).
- **Phase 5 (monitor test) — done:** `DdcCiControl` (Infrastructure, `dxva2.dll`
   wrapper) with graceful "unsupported" handling; exclusive `MonitorTestModule`
   (DDC/CI brightness is best-effort and degrades to a clean "unsupported"
   reading, so the module can still Pass on operator confirmation alone);
   `MonitorTestModuleViewModel` + `MonitorTestView` via `NavigationService` +
   `MainWindow` data templates; a live multi-monitor picker that reacts to
   `WM_DISPLAYCHANGE` (`DeviceTopologyChangedMessage`); and a fullscreen
   `MonitorPatternWindow` using the auto-hiding Exit overlay + Ctrl+E (§6) placed
   on the selected display via `SetWindowPos` in raw device pixels so it lands
   correctly across mixed-DPI setups (§9.4). 36 tests pass (orchestrator +
   `KeyboardModuleTests` + `Phase2ModuleTests` + `MouseModuleTests` +
   `MonitorModuleTests`).
- **Phase 6 (reporting) — done:** `SessionExporter` (Core, no UI/Win32 refs)
  serializes `AuditSession` to a structured JSON file (`TestStatus` written as a
  string enum) plus a self-contained, printable HTML report (`HtmlReportTemplate`);
  the App `ReportExportService` runs the full write-path fallback cascade (§9.6:
  portable app dir → Desktop → %TEMP% → manual folder picker → clipboard modal),
  each candidate probed with a quick write-test so a vanishing volume (e.g. the USB
  stick pulled mid-write) is caught without losing the in-memory session; an
  always-available **Export Report** button lives in the persistent header (and on
  the dashboard). 43 tests pass (`TestOrchestratorTests` + `Phase2ModuleTests` +
  `KeyboardModuleTests` + `MouseModuleTests` + `MonitorModuleTests` +
  `ReportExportTests`).

---

### Conventions to follow

- MVVM via `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`);
  DI via `Microsoft.Extensions.DependencyInjection`.
- Add xunit tests under `Src/Tests/`. New `Core` logic mirrors
  `TestOrchestratorTests.cs`; new module/orchestrator integration mirrors
  `Phase2ModuleTests.cs` (compose via DI, assert terminal status). A unit test for
  the scan-code→key-id layout mapping is high-value and OS-independent.
- Verify with `dotnet build Src\HardwareAuditToolkit.sln` and
  `dotnet test Src\HardwareAuditToolkit.sln`; **F5** in VS Code launches the WPF app
  (see `.vscode/launch.json`). On-hardware verification required for the "every
  physical key registers" DoD.

