# Hardware Audit Toolkit

A portable WPF tool a technician runs on an unfamiliar Windows machine to verify
its keyboard, mouse, monitor, CPU and inventory, then export an auditable report.

Runs from a USB stick. No installer, no admin, no database, no network.

- **Architecture & rationale:** [`docs/hardware-audit-toolkit-architecture.md`](docs/hardware-audit-toolkit-architecture.md)
- **Security-team handout:** [`docs/DeploymentNote.md`](docs/DeploymentNote.md)
- **Known design defects:** [`../taste-audit.md`](../taste-audit.md)
- **Open operator complaints:** [`../todo.md`](../todo.md)
- **AI project memory:** [`../lode/lode-map.md`](../lode/lode-map.md)

## Confirmed constraints

| Decision | Choice |
|---|---|
| Deployment | Portable single self-contained `.exe`; folder build kept as fallback |
| Elevation | Never required. Capabilities that need it degrade to an honest "unavailable" |
| Storage | Flat JSON per session. No SQL/SQLite/embedded DB |
| v1 scope | Keyboard, mouse, monitor, system info, CPU stress. Nothing else |

## Solution layout

```
Src/
  HardwareAuditToolkit.sln
  Core/            # contracts, AuditSession, TestOrchestrator, event-bus messages,
                   #   keyboard layout, reporting, and the five ITestModule
                   #   implementations. No UI; reaches Infrastructure via interfaces only
  Infrastructure/  # Win32 wrappers (raw input, DDC/CI), WMI/CIM inventory,
                   #   LibreHardwareMonitor sensor adapter, diagnostics log
  App/             # WPF host: DI composition root, shell, views/view models,
                   #   single-instance enforcer, DPI manifest, publish profiles
  Tests/           # xunit
  docs/
```

Project references flow one way: `App → Core → Infrastructure`. Core carries no
UI and no directly-authored P/Invoke.

## Build & test

```powershell
dotnet build Src\HardwareAuditToolkit.sln
dotnet test  Src\HardwareAuditToolkit.sln
```

Current state: **zero warnings, 61 xunit tests passing.** `F5` in VS Code launches
the WPF app (see `.vscode/launch.json`).

## Publish

```powershell
# Primary: portable, self-contained, single-file .exe
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableSingleFile

# Fallback for sites that block single-file: self-contained folder
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableFolder
```

Outputs land in `Src\App\bin\publish\`. Pass `-p:VerifyPublishArtifacts=true` to
assert the single-file profile emits exactly one `.exe`.

## What the app does today

**Shell.** A dashboard of five module cards. Selecting one replaces the window
content with that module's screen; the outgoing view model is disposed so
event-bus subscriptions and raw-input registrations never leak across navigation.

**Exit paths.** Every screen offers a mouse-only and a keyboard-only way out,
independently:

- `Ctrl+E` — global low-level hook on its own dedicated thread, so it stays
  responsive under full CPU load.
- **Exit Test** overlay — present on all six views and on the fullscreen pattern
  window (where it auto-hides after 3s of no mouse movement).
- **Native close (X)** — the only path that actually quits the application.
  `Ctrl+E` and the overlay cancel the running module and return to the dashboard.

**Modules.**

| Module | Exclusive | Starts | Passes when |
|---|---|---|---|
| Keyboard | yes | on screen load | operator confirms **and** all 104 ANSI keys registered; otherwise `Warning` |
| Mouse | yes | on screen load | operator confirms (no coverage requirement) |
| Monitor | yes | on screen load | operator confirms patterns render correctly |
| System Info | no | on screen load | WMI inventory collected |
| CPU Stress | yes | **explicit Start** | the full target duration elapses (300s cap) |

Perceptual checks record the operator's confirmation as the status, by design —
there is no objective pass criterion for monitor uniformity. Keyboard coverage is
the one objective criterion in the product.

**Reporting.** `Export Report` lives on the dashboard. It writes a JSON session
file plus a self-contained printable HTML report through the §9.6 write-path
cascade: app directory → Desktop → `%TEMP%` → manual folder picker → clipboard.
Each candidate is probed with a real write-test first, so a USB stick pulled
mid-write costs nothing and the in-memory session survives.

**Fault containment.** A failing WMI/DDC-CI/sensor call degrades to "unavailable"
rather than crashing. Background run loops — CPU stress workers, raw input capture
threads, the `Ctrl+E` hook thread, the device-change window — are guarded at source
so a throw becomes `Failed`, not a dead process. Every fault path writes to
`%LOCALAPPDATA%\HardwareAuditToolkit\diagnostics.log`, so diagnostics are
observable on a published build without a debugger.

## Known gaps

These are real and documented rather than forgotten. See
[`../taste-audit.md`](../taste-audit.md) for full evidence and a prioritised plan.

- **The report understates what was not tested.** Only modules that were *started*
  appear, so a session where just System Info auto-ran exports as
  `Overall status: Passed` without mentioning the four untested devices.
- **A first export stamps itself "in progress"**, because `CompletedAt` is set
  after serialisation.
- **`TestStatus.Skipped` and `TestStatus.Unsupported` are never assigned**, and
  `Cancelled` covers five unrelated situations, including deliberately stopping a
  burn-in early.
- **The operator cannot describe a defect.** `FlagDefect(note)` accepts text, but
  every call site passes a hardcoded constant, so all failures read identically.
- **`SessionCheckpointStore` is write-only** — no code reads a checkpoint back,
  so the stated crash-recovery guarantee is not implemented.
- **No display-sleep prevention** during the five-minute burn-in.
- **No persistent header.** Each view carries its own copy of the exit overlay and
  its own "Back to dashboard" button.

**Manual pre-ship items** (cannot be satisfied in code): Authenticode signing via
the org PKI, an EDR pass before wide rollout, and a manual walk of every exit path
from every screen including mid-CPU-stress.

## Conventions

- **MVVM** via `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`);
  its `WeakReferenceMessenger` doubles as the in-process event bus.
- **DI** via `Microsoft.Extensions.DependencyInjection`. Modules and providers are
  singletons; **module view models must be transient** — they subscribe in their
  constructor and unsubscribe on disposal, so a singleton would stay dead after
  the first navigation away.
- **Teardown twice.** Anything holding an OS resource is released both in
  `Cancel()` and in view-model `Dispose()`.
- **House style** (analyzer-enforced, zero warnings): collection expressions
  (`[]`, `[...]`) over `new List<T>()`/`Array.Empty<T>()`; primary constructors
  where no logic sits between parameter and field; explicit precedence parentheses
  (`a + (b * c)`); `string.Contains(char)` for single characters.
- **Tests** live in `Src/Tests/`. New Core logic mirrors `TestOrchestratorTests.cs`;
  module/orchestrator integration mirrors `Phase2ModuleTests.cs` (compose via DI,
  assert terminal status). Infrastructure is faked (`FakeRawKeyboardInput`,
  `FakeRawMouseInput`, `FakeDdc`) — no test touches real hardware.

### Interop: `DllImport` stays for non-blittable P/Invokes

`SYSLIB1054` recommends `LibraryImport`. **Keep `DllImport`** for the Win32 /
`dxva2.dll` wrappers in `DeviceChangeService`, `DdcCiControl`, `RawKeyboardInput`
and `RawMouseInput`: the source generator cannot marshal non-blittable structs
carrying `string`/`ByValTStr` members (`Wndclassex`, `MONITORINFOEX`,
`PHYSICAL_MONITOR`) or the `EnumDisplayMonitors` delegate callback. The warning is
suppressed per file behind a justification comment. Migrating would require making
the native types blittable.

Blittable P/Invokes — e.g. `SetWindowPos` in `MonitorPatternWindow` — do use
`LibraryImport` (which is why the App project sets `<AllowUnsafeBlocks>true`).

### On-hardware verification

Two definitions of done cannot be met by `dotnet test`: "every physical key
registers" needs a real keyboard, and "patterns render at correct scale" needs a
real mixed-DPI multi-monitor setup.
