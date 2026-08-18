# SysAdmin Hardware Audit Toolkit — Architecture & Implementation Plan

*Draft v3 — no code, framework/architecture only*

## 0. Confirmed decisions

| Decision | Choice |
|---|---|
| Deployment | Portable, no installer, runs from a USB stick, as a **single self-contained .exe**. Accepted trade-off: temp-extraction/EDR risk, mitigated per §9.1. |
| Admin/elevation | Not required for anything in v1. Admin-gated enhancements are explicitly deferred to v2. |
| Local storage | No database. Flat JSON files per audit session, no SQL/SQLite/embedded DB engine in v1. |
| v1 scope | Strictly keyboard, mouse, monitor, system info + CPU stress. Everything else is post-v1. |

## 1. Design principles

1. **Portable & offline-first.** No runtime install, no internet dependency.
2. **Never trap the user.** Every module screen offers *multiple independent* ways out — see §6.
3. **No admin required, anywhere, in v1.** Where a capability is inherently inconsistent regardless of privilege (DDC/CI, some sensors), the app shows an honest "unavailable" state.
4. **Modular, not monolithic.** Every test is a self-contained module behind a common contract (§5), coordinated by a single orchestrator (§4).
5. **Auditable output.** Every session produces a structured, timestamped, machine-identified JSON record plus a human-readable report.
6. **Don't become the incident.** The global keyboard hook and raw input capture resemble keylogger behavior to AV/EDR heuristics — plan for code-signing and allow-listing.
7. **Correct by construction, not by patching.** Runtime realities that are expensive to retrofit (DPI awareness, single-instance enforcement, hook responsiveness) are designed in from Phase 0, not treated as later polish — see §9.

## 2. Recommended stack

| Concern | Choice | Why |
|---|---|---|
| Language | C# / .NET 8 (LTS) | As requested |
| UI framework | **WPF** | Mature global hook integration, mature custom-drawing controls, well-documented Win32 interop |
| UI pattern | **MVVM**, via **CommunityToolkit.Mvvm** (MIT-licensed) | Cuts boilerplate; its `WeakReferenceMessenger` doubles as the in-process event bus in §3 |
| Dependency Injection | `Microsoft.Extensions.DependencyInjection` | Standard, free, lets the shell discover `ITestModule`s without hardcoding them |
| Hardware sensors | **LibreHardwareMonitorLib** (NuGet, MPL-2.0) | Free, OSS, commercial-use-friendly. Best-effort without admin |
| System inventory | `System.Management` (WMI/CIM) + `System.IO.DriveInfo` | Works without elevation |
| Low-level input | P/Invoke: `SetWindowsHookEx` (WH_KEYBOARD_LL) + `RegisterRawInputDevice` | Neither requires elevation |
| Monitor control | P/Invoke: `Dxva2.dll` | Best-effort regardless of privilege level |
| DPI | Per-Monitor V2, declared via **app manifest** from Phase 0 | Not the WinForms-specific `HighDpiMode` key — see §9.4 |
| Storage | Flat files: `System.Text.Json` session files + a loose artifact folder | No database dependency (§7) |
| Reporting | Plain HTML + JSON | Zero extra dependency; HTML is printable to PDF |
| Packaging | Single-file, self-contained publish (primary) | Confirmed — mitigations and an alternate profile in §9.1 |

## 3. High-level layered architecture

```
┌───────────────────────────────────────────────────────────────┐
│ Presentation Layer (WPF, MVVM)                                  │
│  Views + ViewModels per module · Shell/Dashboard · Exit overlay │
└───────────────────────────┬─────────────────────────────────────┘
                             │ binds to
┌───────────────────────────▼─────────────────────────────────────┐
│ Application / Service Layer                                      │
│  - ITestModule implementations                                   │
│  - Test Orchestrator (§4)                                        │
│  - Event Bus / Messenger (WeakReferenceMessenger)                 │
└───────────────────────────┬─────────────────────────────────────┘
                             │ depends on (via interfaces)
┌───────────────────────────▼─────────────────────────────────────┐
│ Infrastructure / Hardware Abstraction Layer                       │
│  - Win32 wrappers: RawInput (keyboard/mouse), DDC/CI (Dxva2)      │
│  - WMI/CIM wrapper (CPU/RAM/disk/BIOS)                            │
│  - LibreHardwareMonitor adapter (best-effort)                     │
│  - Device-change listener (§9.5)                                  │
└───────────────────────────┬─────────────────────────────────────┘
                             │ produces
┌───────────────────────────▼─────────────────────────────────────┐
│ Reporting Layer                                                  │
│  AuditSession aggregator → session.json + session.html            │
│  Write-path fallback cascade (§9.6)                                │
└───────────────────────────────────────────────────────────────┘
```

## 4. Test Orchestrator

- Starts, pauses, cancels, and completes individual test modules
- Enforces that **exclusive** modules (keyboard, mouse, monitor, CPU stress) run one at a time, sequentially; ambient background services (sensor polling, device-change listening) run continuously, outside this queue
- Applies safety restrictions (e.g., the CPU stress test's fixed duration cap — §8)
- Records timestamps, measurements, warnings, and operator actions
- Owns the exit-request flow (Ctrl+E / Exit button / window close all route through it — §6)
- Produces a consistent result status per module:

| Status | Meaning |
|---|---|
| `NotRun` | Module hasn't been started this session |
| `Running` | In progress |
| `Passed` | Met pass criteria, or operator confirmed OK |
| `Failed` | Did not meet pass criteria, or operator flagged a defect |
| `Warning` | Completed with a flagged concern that isn't a clean pass or fail |
| `Skipped` | Operator or timeout budget chose not to run it |
| `Unsupported` | Required capability isn't present on this hardware |
| `Cancelled` | Operator or orchestrator (e.g., a timeout) stopped it mid-run |

## 5. Test module contract (`ITestModule`)

| Element | Description |
|---|---|
| **Metadata** | Id, display name, description, category |
| **Capabilities required** | Declarative (e.g., "raw keyboard input", "DDC/CI") — always satisfiable without elevation in v1, but the field exists so v2's admin-gated capabilities slot in cleanly |
| **Preconditions** | e.g., no other exclusive module currently running |
| **Execution workflow** | `Setup → Running → AwaitingOperatorConfirmation → Complete` |
| **Live measurements** | Typed data points streamed out as the test runs — feeds the Event Bus and any live UI |
| **Pass/fail criteria** | Objective where possible (key coverage); for perceptual checks (monitor uniformity, tracing accuracy) the operator's confirmation becomes the recorded status |
| **Operator confirmation points** | Explicit checkpoints requiring technician acknowledgment |
| **Generated findings** | Structured, human-readable statements added to the report |
| **Artifacts** | Screenshots, raw event logs, exported canvases — loose files next to the session JSON |

## 6. Exit / escape UX

- **Ctrl+E (global, low-level hook, on its own dedicated thread — §9.2).** Immediate, no confirmation, active regardless of which module has focus.
- **A visible "Exit Test" button.** Pinned in a persistent header/overlay on every screen, including fullscreen ones. Mouse-only, no keyboard required.
- **The native window close button (X).** Kept wherever normal window chrome is present. Exception: the monitor pattern screen needs true edge-to-edge fullscreen for accurate color testing, so it relies on the auto-hiding Exit overlay + Ctrl+E instead of native chrome.
- **Esc, with confirmation — except in the keyboard module.** Elsewhere, Esc triggers "Exit test? Unsaved measurements will be lost." In the keyboard test module, Esc is ordinary test data — just another key to register — and carries no exit meaning there.
- **A timeout/cancel for unattended runs.** Every module declares a max duration; the orchestrator force-cancels and records `Cancelled`/`Skipped` with a logged reason if exceeded.

**Net rule:** a mouse-only path and a keyboard-only path must each *independently* be sufficient to leave any screen.

## 7. Storage approach (no database)

One JSON file per audit session (`{hostname}_{timestamp}.json`) plus a loose artifact folder. No SQL/SQLite/embedded DB engine in v1. A future "compare sessions" feature can glob and parse the JSON files directly without a database; real fleet-scale historical querying is a legitimate v2/v3 conversation, not a v1 concern.

## 8. CPU stress test

No automatic thermal-based cutoff in v1 (temperature access is best-effort without admin and can't be relied on). Instead: a technician-set duration defaulting to a conservative fixed cap (e.g., 5 minutes), a prominent manual "Stop test" control, and Ctrl+E/Exit always available. Worker threads run at the full `Environment.ProcessorCount` count — not reduced — at **`ThreadPriority.BelowNormal`**, so every core still gets loaded (the point of a burn-in test) while the OS still favors the UI/hook threads under contention. If temperature values happen to be available, they're shown as an informational live readout only.

*Deferred to v2:* Administrator-mode opt-in unlocking full sensor detail and a real automatic thermal cutoff.

## 9. Runtime hardening & edge cases

These affect Phase 0–1 scaffolding and the reporting layer directly — not later polish.

**9.1 Packaging: single .exe, with mitigations**
Confirmed: single self-contained .exe. .NET self-contained single-file publishing extracts native components to a temp bundle directory on first run, which enterprise EDR/AppLocker policies frequently flag as dropper-like behavior. Since that risk is accepted rather than designed away, it needs concrete mitigation rather than a hope that signing alone covers it:
- **Redirect the extraction directory** away from the default `%TEMP%` to a documented, predictable path (e.g., a subfolder next to the .exe, or a fixed `%LOCALAPPDATA%\HardwareAuditToolkit\extract` location) so a security team can allow-list one specific path instead of "anything in user temp."
- **Code-sign the binary** (Authenticode, via the org's PKI/signing process) — this is table stakes for avoiding SmartScreen/AV heuristic flags, not just EDR.
- **Ship a one-page deployment note alongside the tool**: SHA-256 hash of the exe, publisher/signing info, the exact extraction path from above, and a short description of what the tool does — something a technician can hand directly to their security team for allow-listing, rather than that conversation happening reactively after the tool gets blocked in the field.
- **Test against at least one common EDR product** (e.g., Microsoft Defender for Endpoint) before wide rollout, as an explicit Phase 7 task, rather than discovering the block on a live audit.
- **Low-cost hedge:** since single-file vs. folder is just a publish-profile setting, not an architectural difference, keep a **second publish profile producing the self-contained-folder build** as a documented fallback artifact. If a particular site blocks the single .exe despite the mitigations above, the team can hand out the folder build there without any code changes — this costs a build config, not a redesign.

**9.2 Ctrl+E responsiveness under load**
`WH_KEYBOARD_LL` callbacks run on the thread that installed the hook, and that thread must keep pumping messages for the hook to fire — a callback that's starved or blocks too long risks being skipped, independent of whether the app visibly freezes. The hook runs on its **own dedicated background thread with a minimal message loop**, separate from the main WPF Dispatcher thread, so exit responsiveness is decoupled from whatever the UI thread is doing generally — not just from burn-in load specifically.

**9.3 Single-instance enforcement**
A named, system-wide `Mutex` (`Global\` namespace prefix, so it holds across concurrent sessions such as RDP or fast user switching) checked at startup before any hooks or stress threads are created. On detecting a second launch, bring the first instance's window to the foreground rather than silently no-op-ing.

**9.4 Per-monitor DPI awareness — Phase 0, not deferred**
Declared via the app manifest from the very first scaffolding. Mixed-DPI setups (e.g., a scaled laptop panel paired with a 100% external monitor) are common in enterprise fleets, and retrofitting DPI-awareness after the mouse-tracing canvas and monitor pattern renderer already exist means redoing coordinate math in every module that touches screen positions.

**9.5 Hardware hot-plug**
A hidden message-only window (`HwndSource.AddHook`) listening for:
- `WM_INPUT_DEVICE_CHANGE` (paired with `RIDEV_DEVNOTIFY` on raw input registration) — the precise notification for keyboard/mouse arrival/removal
- `WM_DISPLAYCHANGE` — monitor reconfiguration, so the display picker and any in-progress monitor test react to a screen appearing/disappearing instead of showing stale state

**9.6 Reporting write-path fallback**
A cascade, each step preceded by a quick write-test so failures are caught immediately:
1. Portable app directory (next to the .exe)
2. `%USERPROFILE%\Desktop`
3. `%TEMP%`
4. Manual "choose a folder" picker
5. Last resort: on-screen modal with a "Copy Audit JSON to Clipboard" button

The completed session stays in memory until a write actually succeeds — a failure partway down the cascade delays the export, never loses the audit data.

## 10. Implementation plan for an LLM coding agent

**Phase 0 — Scaffolding**
- Deliverables: `.sln` with `App` (WPF host), `Core` (contracts/orchestrator/models, no UI/Win32 references), `Infrastructure` (P/Invoke, WMI, LibreHardwareMonitor adapters), `Tests`. NuGet: `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `LibreHardwareMonitorLib`. `ITestModule` + `TestStatus` defined in `Core`. App manifest declares Per-Monitor V2 DPI awareness (§9.4) and a `Global\` single-instance mutex check (§9.3) runs before anything else.
- Two publish profiles configured: primary single-file self-contained .exe with the extraction directory redirected per §9.1, plus a secondary self-contained-folder profile as the fallback artifact.
- DoD: builds and runs to an empty main window from both publish profiles; a second launch attempt foregrounds the first instance instead of opening a duplicate.

**Phase 1 — Shell, Navigation & Exit**
- Deliverables: MainWindow with a dashboard/sidebar; Ctrl+E hook on its own dedicated thread (§9.2) wired to the orchestrator's exit flow; reusable "Exit Test" overlay; `AuditSession` model; hidden message-only window wired for `WM_INPUT_DEVICE_CHANGE`/`WM_DISPLAYCHANGE` (§9.5).
- DoD: Ctrl+E, the Exit button, and native window close each independently close the app from any screen; unplugging/replugging a keyboard or mouse is reflected without restarting the app.

**Phase 2 — System Info & Live Sensors**
- Deliverables: `SystemInfoProvider` (WMI); `SensorProvider` wrapping LibreHardwareMonitorLib on the Event Bus; `SystemInfoModule`; CPU stress module per §8 (full core count, `BelowNormal` priority, fixed duration cap, manual stop).
- DoD: system info shows accurate live data; stress test loads every core, is stoppable via every exit path, and the UI/Ctrl+E stay responsive throughout — verify this specifically under full load, not just at idle.

**Phase 3 — Keyboard Test**
- Deliverables: `RawKeyboardInput` wrapper (scan-code based); vector keyboard layout (ANSI, v1); `KeyboardTestModule` with per-key untested/pressed/confirmed state; WPM/accuracy sub-screen; Esc-is-just-data rule implemented explicitly.
- DoD: every physical key registers correctly on real hardware; Ctrl+E still exits reliably during raw capture, including under simultaneous CPU stress.

**Phase 4 — Mouse Test**
- Deliverables: `RawMouseInput` wrapper; `MouseTestModule` — click/scroll log, drag-hold drop detection for both buttons, duck/bicycle tracing screen.
- DoD: click/scroll/drag register correctly; an early release mid-drag is clearly flagged; unplugging the mouse mid-test is handled gracefully (§9.5) rather than freezing the module.

**Phase 5 — Monitor Test**
- Deliverables: fullscreen pattern window using the auto-hide Exit overlay (§6); `DdcCiControl` wrapper with graceful "unsupported" handling; multi-monitor picker that reacts to `WM_DISPLAYCHANGE`.
- DoD: patterns render correctly per display at correct scale (verify specifically on a mixed-DPI setup — §9.4); DDC/CI works where supported and reports "unsupported" cleanly where not.

**Phase 6 — Reporting**
- Deliverables: JSON serialization of `AuditSession`; HTML report template; Export action implementing the full write-path fallback cascade (§9.6).
- DoD: a full audit run produces a JSON + HTML pair; pulling the USB drive mid-write is handled by the cascade without losing the in-memory session data.

**Phase 7 — Polish & Refactor**
- Deliverables: hook/resource cleanup audit (no leaked hook handles or raw input registrations across navigation); manual pass of every exit path from every screen, including mid-stress-test; error handling so a single failing WMI/DDC-CI/sensor call degrades to "unavailable" instead of crashing.
- DoD: the above is a literal checklist ticked off before v1 ships.

**Explicitly deferred to v2:** admin-mode opt-in (full sensor detail, automatic thermal cutoff), audio/mic/webcam/battery/network/USB modules, non-US keyboard layouts, silent/CLI unattended mode, cross-session history/trend comparison, code signing + AV/EDR allow-listing.
