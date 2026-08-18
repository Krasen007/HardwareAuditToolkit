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
- **Phase 3 (keyboard test) — next:** see the **Phase 3 handoff** section below for
  what to build and exactly where to hook it in.

---

## Phase 3 handoff (for the next implementer / LLM agent)

Read `hardware-audit-toolkit-architecture.md` (§3–§6, §9.2, §9.5) together with this
section. Everything below reflects the **current, verified** Phase 2 state.

### Where the key pieces live

| Concern | Location |
|---|---|
| Module contract & status enums | `Src/Core/ITestModule.cs`, `IModuleMetadata.cs`, `ModulePhase.cs` |
| Orchestrator (exclusivity, timeout, results) | `Src/Core/TestOrchestrator.cs` (unit-tested) |
| Session / result / measurement models | `Src/Core/AuditSession.cs` |
| DI shell, exit flow, singleton wiring | `Src/App/App.xaml.cs` (`ConfigureServices`) |
| Screen switching | `Src/App/Services/NavigationService.cs` + `MainWindow.xaml` data templates |
| Dashboard (module list) | `Src/App/ViewModels/DashboardViewModel.cs` + `Views/DashboardView.xaml` |
| Module stub screen (keyboard/mouse/monitor) | `Src/App/Views/ModulePlaceholderView.xaml` (+ VM) |
| Exit overlay & hotkey | `Src/App/Views/ExitOverlay.xaml`, `Services/ExitHotkeyService.cs` |
| Live device-change listener (raw input already registered) | `Src/App/Services/DeviceChangeService.cs` (registers `RIDEV_INPUTSINK`/`RIDEV_DEVNOTIFY` for keyboard usage 0x06) |
| Event bus messages | `Src/App/Messages/` + `Src/Infrastructure/SensorReadingsMessage.cs` (`WeakReferenceMessenger`) |
| Phase 2 implementations to mirror | `Src/App/Modules/SystemInfoModule.cs`, `CpuStressModule.cs`; `Src/App/ViewModels/*ModuleViewModel.cs`; `Src/App/Views/*View.xaml` |
| Sensor abstractions | `Src/Infrastructure/ISensorProvider.cs`, `SensorReading.cs` |

### What Phase 3 must deliver (per the architecture doc)

- **`RawKeyboardInput` wrapper (Infrastructure, scan-code based).** P/Invoke raw
  input for keyboard (`RIDEV_INPUTSINK`) and surface each key as a
  `(scanCode, virtualKey, isExtended, isKeyDown)` sample. Prefer scan codes over
  virtual keys so physical-layout detection is robust; translate to a key id via a
  vector layout.
- **Vector keyboard layout (ANSI, v1).** A data-driven map from scan code →
  on-screen key id, used both to render the test grid and to mark keys
  untested → pressed → confirmed. Non-US layouts are explicitly deferred to v2.
- **`KeyboardTestModule` implementing `ITestModule`** (exclusive, `IsExclusive`
  already set for `keyboard` in the dashboard): per-key untested/pressed/confirmed
  state, pass criteria = every expected key registered at least once, operator
  confirmation ("all keys work?") becomes the recorded status. Route it through the
  orchestrator like the Phase 2 modules.
- **WPM / accuracy sub-screen.** A timed typing sample that records gross WPM and
  accuracy; store as `Measurements`/`Findings`. Should be a secondary view launched
  from the keyboard module, not a separate exclusive module.
- **Esc-is-just-data, implemented explicitly.** In the keyboard module, `Esc` is
  ordinary test data — it must **never** carry exit meaning here (architecture §6).
  The global `ExitHotkeyService` already only fires on **Ctrl+E**, so it does not
  conflict; document/enforce that no `Esc` handler in the keyboard module triggers
  `ExitRequestedMessage`. Ctrl+E must **still** exit during raw capture, including
  under simultaneous CPU stress.

### Concrete integration points & gotchas (current state)

- **Raw input window.** `DeviceChangeService` already registered keyboard raw input
  (usage 0x06) for *device-change* notifications. For data capture,
  `RawKeyboardInput` must register its **own** raw-input registration (keyboard,
  `RIDEV_INPUTSINK`, no `RIDEV_DEVNOTIFY` needed for key data) against a message
  window — either a dedicated hidden `HwndSource` or reuse the pattern in
  `DeviceChangeService`. Do **not** block the WPF `Dispatcher` thread in the
  `WM_INPUT` handler; parse via `GetRawInputData` and post to the module/event bus.
- **Keep the Ctrl+E thread independent.** `ExitHotkeyService` runs the low-level
  hook on its **own dedicated thread** (§9.2). Raw keyboard capture must not pump
  that thread or starve it; the point of Phase 3's DoD is that Ctrl+E still exits
  reliably *while* raw capture is active and *while* a stress module runs. Add a
  test/check that toggles the stress module and confirms Ctrl+E still exits.
- **Register the module + view with DI/navigation.** Add
  `KeyboardTestModule` as `ITestModule` (concrete singleton, like Phase 2), and
  route `NavigateToModule("keyboard")` in `NavigationService` to a
  `KeyboardTestModuleViewModel` (+ `KeyboardTestView` data template in
  `MainWindow.xaml`). The keyboard card is already flagged `IsExclusive = true`.
- **Drive the dashboard from real metadata (optional but preferred).** `DashboardViewModel`
  still hardcodes five `DashboardItemViewModel`s; `ModulePlaceholderView` is the
  current landing screen for `keyboard`. Swap in the real view/VM rather than
  duplicating the placeholder.
- **Exit flow already works — don't reinvent it.** Every exit path sends
  `ExitRequestedMessage`; `App.HandleExitRequested` calls `orchestrator.CancelAll()`
  → sets `AuditSession.CompletedAt` → `Shutdown()`. `KeyboardTestModule.Cancel()`
  only needs to unregister raw input and call its `onComplete`.
- **Hook/resource cleanup is a Phase 7 concern but start clean.** Registration of
  raw input and any `HwndSource` must be torn down in `Cancel()` **and** when the
  screen is navigated away (the `NavigationService.SetScreen` already disposes the
  outgoing view model — put raw-input unregistration in `IDisposable.Dispose()` of
  the view model or in `Cancel()`). A leaked `RIDEV_INPUTSINK` registration across
  navigation is exactly the bug Phase 7 will hunt.
- **Sensor reads go on the Event Bus** (established in Phase 2): reuse
  `WeakReferenceMessenger`; add a `KeyboardTelemetryMessage`/`KeyEventMessage` if the
  view needs a live stream rather than direct callbacks.
- **Keep `Core` dependency-free.** Raw input P/Invoke lives in `Infrastructure`;
  `Core` stays UI/Win32-free so the orchestrator stays unit-testable. The module
  itself lives in `App/Modules` (it needs the event bus + DI).
- **Linux/macOS note:** the solution `<TargetFramework>net8.0-windows</TargetFramework>`
  + WPF is Windows-only; build & run on Windows.

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

