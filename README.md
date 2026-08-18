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
- **Phase 2 (system info & live sensors) — next:** see the **Phase 2 handoff**
  section below for what to build and exactly where to hook it in.

---

## Phase 2 handoff (for the next implementer / LLM agent)

Read `hardware-audit-toolkit-architecture.md` (§3–§5, §8) together with this
section. Everything below reflects the **current, verified** Phase 1 state.

### Where the key pieces live

| Concern | Location |
|---|---|
| Module contract & status enums | `Src/Core/ITestModule.cs`, `IModuleMetadata.cs`, `ModulePhase.cs` |
| Orchestrator (exclusivity, timeout, results) | `Src/Core/TestOrchestrator.cs` (unit-tested) |
| Session / result / measurement models | `Src/Core/AuditSession.cs` |
| DI shell, exit flow, singleton wiring | `Src/App/App.xaml.cs` (`ConfigureServices`) |
| Screen switching | `Src/App/Services/NavigationService.cs` + `MainWindow.xaml` data templates |
| Dashboard (module list) | `Src/App/ViewModels/DashboardViewModel.cs` + `Views/DashboardView.xaml` |
| Module stub screen | `Src/App/Views/ModulePlaceholderView.xaml` (+ VM) |
| Exit overlay & hotkey | `Src/App/Views/ExitOverlay.xaml`, `Services/ExitHotkeyService.cs` |
| Event bus messages | `Src/App/Messages/` (`WeakReferenceMessenger`) |
| Sensor abstractions (Phase 2 targets) | `Src/Infrastructure/ISensorProvider.cs`, `SensorReading.cs` |

### What Phase 2 must deliver (per the architecture doc)

- `SystemInfoProvider` (WMI) — CPU / RAM / disk / BIOS inventory (no elevation).
- `SensorProvider` wrapping `LibreHardwareMonitorLib` on the Event Bus via
  `ISensorProvider` (best-effort — an empty reading set means "unavailable", never
  an error).
- `SystemInfoModule` implementing `ITestModule`.
- **CPU stress module** per §8: one worker thread per `Environment.ProcessorCount`
  at `ThreadPriority.BelowNormal`, a technician-set duration defaulting to a fixed
  cap (e.g. 5 min), a prominent manual **Stop test** control, and every exit path
  (Ctrl+E / Exit overlay / window X) staying responsive. No automatic thermal
  cutoff in v1 (temperature is best-effort, informational only).
- DoD: system info shows accurate live data; the stress test loads every core, is
  stoppable via every exit path, and the UI/Ctrl+E stay responsive **under full
  load**, not just at idle.

### Concrete integration points & gotchas (current state)

- **Register real modules with DI.** `ITestModule` implementations are **not yet
  registered** anywhere. `TestOrchestrator` is registered by concrete type
  (`AddSingleton<TestOrchestrator>()`) and takes `IEnumerable<ITestModule>` via its
  constructor, so it will discover every `ITestModule` you `AddSingleton`. Register
  `SystemInfoModule`, the stress module, and the sensor provider there.
- **Drive the dashboard from real metadata.** `DashboardViewModel` currently
  hardcodes 5 `DashboardItemViewModel`s. Prefer publishing them from
  `orchestrator.Modules` (`IModuleMetadata`) so the shell doesn't hardcode modules
  (architecture §3 intent), then have each card's `Open` command
  `TryStartModule` via the orchestrator rather than just swapping screens.
- **Exit flow already works — don't reinvent it.** Every exit path sends
  `ExitRequestedMessage`; `App.HandleExitRequested` calls `orchestrator.CancelAll()`
  → sets `AuditSession.CompletedAt` → `Shutdown()`. Your modules only need an
  `ITestModule.Cancel()` that stops async work and calls its `onComplete`.
- **Replace, don't duplicate, the placeholder.** `ModulePlaceholderView` is the
  current landing screen for `system` and `stress`. Wire real views/ViewModels in
  as DataTemplates in `MainWindow.xaml` and point navigation at them.
- **Sensor reads go on the Event Bus.** `SensorProvider` should stream typed
  readings via `WeakReferenceMessenger` (like `DeviceTopologyChangedMessage` does),
  not a bespoke callback — architects §3 intended the messenger as the in-process
  event bus.
- **Keep `Core` dependency-free.** WMI/P-Invoke/LibreHardwareMonitor live in
  `Infrastructure`; `Core` stays UI/Win32-free so the orchestrator stays unit-testable.
- **Linux/macOS note:** the solution `<TargetFramework>net8.0-windows</TargetFramework>`
  + WPF is Windows-only; build & run on Windows.

### Conventions to follow

- MVVM via `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`);
  DI via `Microsoft.Extensions.DependencyInjection`.
- Add xunit tests under `Src/Tests/` for any new `Core` logic (mirror
  `TestOrchestratorTests.cs`).
- Verify with `dotnet build Src\HardwareAuditToolkit.sln` and
  `dotnet test Src\HardwareAuditToolkit.sln`; **F5** in VS Code launches the WPF app
  (see `.vscode/launch.json`).

