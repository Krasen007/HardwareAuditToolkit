# Hardware Audit Toolkit

Portable sysadmin hardware audit tool — v1 scope: keyboard, mouse, monitor,
system info, and CPU stress testing. Portable, offline-first, no admin
required, no database. See `hardware-audit-toolkit-architecture.md` for the
full architecture and phase plan.

## Solution layout

```
Src/
  HardwareAuditToolkit.sln
  Core/            # contracts, session models, orchestrator, event-bus messages,
                   #   keyboard layout/state, and the Application-layer test modules
                   #   (no UI; depends on Infrastructure only via interfaces)
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

The solution builds with **zero warnings** and the full xunit suite passes on a
clean `dotnet test`.

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
   the dashboard). 46 tests pass (`TestOrchestratorTests` + `Phase2ModuleTests` +
   `KeyboardModuleTests` + `MouseModuleTests` + `MonitorModuleTests` +
   `ReportExportTests`).
- **Phase 7 (polish & refactor) — done:** hook/resource cleanup audit (raw-input and
   `WndProc` run loops torn down on `Cancel()` and view-model disposal, so no capture
   leaks across navigation); global fault containment in `App.xaml.cs`
   (`DispatcherUnhandledException` keeps a UI-thread fault from ending the audit and
   logs every other failure, architecture §9.7); module run-loop guarding so a throw on
   a background thread — the CPU-stress workers, the raw keyboard/mouse capture threads,
   the Ctrl+E hook thread, and the device-change message-only window — degrades to
   "unavailable"/`Failed` instead of terminating the process; and per-call best-effort
   degradation in the WMI/DDC-CI/sensor providers. **Diagnostics are now observable on a
   published build**: every fault path logs to `%LOCALAPPDATA%\HardwareAuditToolkit\diagnostics.log`
   via `IDiagnosticLog`/`FileDiagnosticLog` (never throws), injected into `App.xaml.cs`,
   `RawKeyboardInput`, `RawMouseInput`, `ExitHotkeyService`, and `DeviceChangeService` — so the
   §9.7 "a fault is never silent" claim holds without a debugger attached. **Crash persistence**:
   `TestOrchestrator` writes a durable JSON checkpoint (`ISessionCheckpointStore`/`SessionCheckpointStore`
   under `%LOCALAPPDATA%\HardwareAuditToolkit`) after every module completes and on app exit, so a
   forced termination can't lose findings before an explicit export. **Export correctness**:
   `ReportExportService.Export()` only stamps `CompletedAt` once a durable export actually lands.
   60 tests pass, including App-layer coverage (`ReportExportServiceTests`, `NavigationServiceTests`),
   the CPU fault-injection test (`CpuStressFaultInjectionTests`), and checkpoint tests
   (`OrchestratorCheckpointTests`, `SessionCheckpointTests`). An opt-in `VerifySingleFileArtifact`
   publish target asserts the single-file build produces exactly one `.exe` (§9.1). **Manual
   pre-ship items remain (cannot be satisfied in code):** Authenticode code-signing via the org PKI
   (§9.1), an EDR pass (e.g. Microsoft Defender for Endpoint) before wide rollout, and a manual walk
   of every exit path from every screen, including mid-CPU-stress.
- **Usability pass (post-Phase 7) — done:** repeated-key press clarity in the keyboard
   test (a per-key press counter + red repeat badge on tiles pressed more than once, and a
   newest-first **key press log** mirroring the mouse click/scroll/drag log — indicated by
   `KeyEventMessage.PressCount`/`LogLine`, `KeyViewModel.ShowCountBadge`, and
   `KeyboardTestModuleViewModel.LogLines`); the monitor pattern window now **cycles to the
   next colour on any click** on the pattern surface (wrapping, with a `N/M — pattern` readout
   and each advanced pattern recorded via `MonitorPatternWindow`'s advance callback); and the
   CPU stress screen **no longer auto-starts** the burn-in on load and renders a **live dual-line
   graph** of CPU load % (gold) and maximum core temperature (blue) from `StressTelemetryMessage`
   samples (`CpuStressModuleViewModel.LoadPoints`/`TempPoints`, reflexive `CpuStressView` chart).
   61 xunit tests pass (the keyboard repeat-counter case added).

---

## Recent code-quality cleanup & decisions

A pass resolved every open analyzer diagnostic (IDE / CA / Roslynator / SYSLIB) so
the solution builds with **zero warnings, zero errors** (60 xunit tests passing).
The style rules applied are now the project's house style:

- **Collection expressions** — target-typed `[]` / `[...]` in place of
  `new List<T>()`, `Array.Empty<T>()`, `new[] { ... }`, and `new()` (IDE0028/
  IDE0300/IDE0301).
- **Primary constructors & auto-properties** — e.g. `CpuStressModule(ISensorProvider
  sensors)` replaces an explicit constructor + backing field where there is no logic
  in between (IDE0290/RCS1085).
- **Explicit precedence parentheses** — `a + (b * c)` and
  `(a * b) + (c * d)` emphasize a higher-precedence operand of a lower-precedence
  operator (RCS1123).
- **Single-char `string.Contains(char)`** and removal of redundant `!` where the
  target API already accepts null (CA1847/RCS1249).

### Interop decision: `DllImport` stays for non-blittable P/Invokes (SYSLIB1054 suppressed)

`SYSLIB1054` recommends migrating P/Invokes to `LibraryImport`. **We keep
`DllImport`** for the Win32 / `dxva2.dll` wrappers (`DeviceChangeService`,
`DdcCiControl`, `RawKeyboardInput`, `RawMouseInput`) because the `LibraryImport`
source generator cannot marshal these signatures: non-blittable structs carrying
`string` / `ByValTStr` members (`Wndclassex`, `MONITORINFOEX`, `PHYSICAL_MONITOR`)
and a delegate callback (`EnumDisplayMonitors`). The suggestion is suppressed per
file (`#pragma warning disable/restore SYSLIB1054`) behind a justification comment.
A future migration would require making the native types blittable and enabling
`AllowUnsafeBlocks`.

**Blittable P/Invokes** — e.g. `SetWindowPos` in `MonitorPatternWindow` — are
migrated to `LibraryImport` (which requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
in the App project). These use only blittable types (`IntPtr`, `int`, `uint`,
`bool`) that the source generator can marshal without the non-blittable struct
limitations.

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
- Use the analyzer-driven house style in ["Recent code-quality cleanup &
  decisions"](#recent-code-quality-cleanup--decisions) (collection expressions,
  primary constructors, explicit-precedence parentheses).
- Interop P/Invoke: keep `DllImport` and do **not** convert to `LibraryImport` for
  the existing Win32 / `dxva2.dll` signatures that carry non-blittable structs or a
  delegate callback — `SYSLIB1054` is already suppressed with justification (see
  the interop decision above).

