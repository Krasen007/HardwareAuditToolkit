# Taste Audit — Hardware Audit Toolkit

Audited against [`tasteful-software-guide.md`](tasteful-software-guide.md).
Scope: `Src/` (Core, Infrastructure, App, Tests), `Src/README.md`,
`Src/docs/hardware-audit-toolkit-architecture.md`, `todo.md`.
Method: full read of the App and Core layers, targeted read of Infrastructure,
verified claims by grep over `Src/`. All line references are to the state of the
tree at audit time.

---

## 1. Core problem, as the codebase states it

> A technician sits at an unfamiliar machine, verifies its hardware works, and
> produces a report someone else will trust.

Architecture principle 5 phrases it as *"Auditable output. Every session produces
a structured, timestamped, machine-identified JSON record plus a human-readable
report."*

## 2. Verdict

**The engineering has taste; the product does not yet.**

Two principles are held consistently across every layer and are genuinely
compounding: *never trap the operator* (§6) and *degrade honestly, never crash*
(§9.7). Both are real points of view, applied without exception.

But the artifact the tool exists to produce — the report — is the least-designed
thing in the repo. The tool is well-built at the layer users never see and
under-decided at the layer that is the entire point.

This is **not** the guide's "Aesthetic-Only Product." It is the inverse: real
judgment under the hood, unfinished judgment at the output. The closest
anti-pattern matches are **The Feature Factory** (the two novelty sub-screens)
and, for the reporting layer specifically, **The CRUD App** — the JSON is a raw
`JsonSerializer.Serialize(session)` dump, so the output's shape is the object
graph's shape rather than a decision about what a reader needs.

## 3. Scorecard

| Guide principle | Verdict | Anchor |
|---|---|---|
| Coherent theory of the domain | Partial | "Never trap the operator" held everywhere; "what makes an audit trustworthy" never decided |
| Get defaults exactly right | Fail | Auto-start policy differs per module, decided per phase |
| Optimize the empty state | Fail | Empty report is a letterhead on a blank form; dashboard shows no state |
| First ten seconds effortless | Partial | Screens legible; dashboard tells the operator nothing about the job |
| Design by subtraction | Fail | Two novelty sub-screens, an unread persistence layer, three dead template sections |
| Taste at every layer, not just surface | Fail | `HtmlReportTemplate` has one test with four `Contains` assertions |
| Every default/omission deliberate | Fail | 8 statuses specified, 2 unreachable, 1 overloaded five ways |
| Don't leak engineering into UX | Fail | `NotRun`, `"duck"`, `BelowNormal`, `"sub-screen"`, `"Module.Start threw"` all reach the report |
| Honest unavailable states | Strong | DDC/CI and sensors degrade cleanly and say so |
| Never trap the user | Strong | Mouse-only and keyboard-only exits from every screen, including fullscreen |

---

## 4. Critical findings

### F1 — The product's output ships broken, and nothing tests it

`SessionExporter.cs:45-46` serializes JSON and renders HTML.
`ReportExportService.cs:55-57` stamps `CompletedAt` *after* that call returns.

Consequence: **every first export of a session says `Completed: in progress`** and
carries `"completedAt": null` and `"jsonPath": null`. Re-exporting fixes the stamp
but silently overwrites the earlier pair, because the filename derives from
`StartedAt`, not export time (`SessionExporter.cs:171-172`).

The deliverable the technician hands over declares itself unfinished. The
`.hat.json` checkpoint — which nothing ever reads (see F7) — is the *more correct*
artifact, because `TestOrchestrator.Dispose()` writes it after `CompletedAt` is set.

Coverage: `ReportExportTests.cs:185-195` is the only test touching the template
and asserts four substrings. Unasserted: the empty-modules branch
(`HtmlReportTemplate.cs:68-69`), the completion branch (`:48`), the measurements
table (`:94-111`), operator actions (`:86-92`), `StatusClass` (`:141-149`), and
HTML escaping.

> Guide: *taste is not a coat of paint — apply it to API responses, error states, data flow.*

### F2 — `Overall status: Passed` on a machine where nothing was tested

`SystemInfoModuleViewModel.cs:48` auto-starts the module from the view-model
constructor. It passes. `TestOrchestrator.cs:367` then sets the session to
`Passed`. `HtmlReportTemplate.cs:59,74` only iterates modules that were actually
*started*, and `TestOrchestrator.cs:115-122` appends a `ModuleResult` only on
start.

So a machine whose keyboard, mouse, monitor and CPU were never touched produces a
green report that never mentions them. There is no roster of untested modules, no
counts, no "4 of 5 passed."

**This is the most damaging failure in the audit: the tool can certify a machine
it never examined.**

### F3 — The status vocabulary was specified, not decided

Copied wholesale from the architecture table (§4) and never calibrated against
real code paths.

- **`Skipped` and `Unsupported` are never assigned.** Verified: zero write sites
  in the solution. Only reads at `TestOrchestrator.cs:359,367`,
  `HtmlReportTemplate.cs:145`, `MonitorTestModuleViewModel.cs:193`. The one
  genuinely-unsupported capability, DDC/CI, deliberately resolves to `Passed`
  instead (`MonitorTestModule.cs:233-235`).
- **`Cancelled` means five unrelated things**: navigated away, pressed Ctrl+E,
  closed the window, hit the unattended timeout — and *successfully stopping a
  burn-in early* (`CpuStressModuleViewModel.cs:284` → `CpuStressModule.cs:159`).
  A deliberate 30-second smoke test is recorded identically to an abandoned run.
- **`Failed` means both** "the hardware is broken" and "our app threw"
  (`TestOrchestrator.cs:154`, `CpuStressModule.cs:225`).
- **`Warning` means both** "operator confirmed with keys untested"
  (`KeyboardTestModule.cs:167`) and "WMI collection threw"
  (`SystemInfoModule.cs:104`).
- `TestOrchestrator.cs:373` is unreachable.

Eight statuses producing five meanings, two of them false, is not calibration —
it is deferral.

### F4 — The keyboard module fights the operator; the mouse module trusts it blindly

`KeyboardTestModule.cs:163-169`: the operator confirms the keyboard works, and the
tool overrides them with `Warning`. That is **`todo.md` item 1** — a user telling
you your theory of them is wrong.

`MouseTestModule.cs:146-168`: `Confirm()` returns `Passed` unconditionally. Zero
clicks, zero scrolls, zero drags — `Passed`. The module never emits `Warning` at
all, making `MouseTestModuleViewModel.cs:199` unreachable.

Two modules, opposite philosophies, in the same report. One distrusts operator
judgment; one requires no evidence. Neither is wrong in isolation. Having both is
incoherence.

**The operator can never say what was wrong.** `FlagDefect(note)` accepts a
description (`KeyboardTestModule.cs:178`), but all three call sites pass a
hardcoded constant (`KeyboardTestModuleViewModel.cs:216`,
`MouseTestModuleViewModel.cs:253`, `MonitorTestModuleViewModel.cs:244`). The
`note` parameter is exercised only by tests. Every failed module reads
identically. For an audit tool, "what was broken" is the highest-value field in
the document, and it is unreachable from the UI.

### F5 — The sub-screens are the Feature Factory, and they corrupt the data

**WPM typing test** (~90 lines across VM/XAML/module) measures the *operator's*
typing speed against a hardcoded pangram (`KeyboardTestModuleViewModel.cs:62,266-287`).
**Duck tracing** (~130 lines) measures the operator's steadiness against 18
hardcoded waypoints (`MouseTestModuleViewModel.cs:131-160,324-340`). Neither
affects any status. Neither tests hardware.

Both leave raw capture running — `ToggleWpm` and `ToggleTrace` only flip a view flag:

- Typing the pangram feeds `KeyboardTestModule.OnKey`, so the typing test
  **silently fills the coverage metric that decides Pass vs Warning**.
- Tracing the duck inflates `LeftClicks`/`DragCount`, which then appear in the
  confirm finding as evidence (`MouseTestModule.cs:156-158`).

`MouseTestModuleViewModel.cs:20` still promises "duck/bicycle." The bicycle was
never built.

> Taste Test #5, Omission: what did these cost? The report's design, the status
> vocabulary, and the defect-description field.

### F6 — Leaving is indistinguishable from failing

Every `ExitOverlay` press routes to `CancelAll()` (`App.xaml.cs:134-137` →
`TestOrchestrator.cs:205`) and records `Cancelled`. On the fullscreen pattern
window, "Back to controls" and "Exit Test (Ctrl+E)" sit adjacent, look
equivalent, and produce opposite report outcomes (`MonitorPatternWindow.xaml:20-30`;
`BackButton_Click` at `.xaml.cs:118-119` does not cancel). That is **`todo.md` item 2**.

Compounded by `MonitorTestView.xaml.cs:20` auto-starting the run on load, so
merely looking at the monitor screen and leaving stamps `Cancelled`.

The §6 principle *never trap the operator* was decided beautifully. The follow-on
question — *what does leaving mean?* — was never asked.

### F7 — Written four ways, read never

`SessionCheckpointStore` is invoked from four sites (`TestOrchestrator.cs:240,252,289,343`).
`ISessionCheckpointStore` has exactly one method: `Save`. There is **no `Load`, no
enumeration, no recovery prompt, no cleanup.**

Verified: the only code in the repo that reads a `.hat.json` is
`SessionCheckpointTests.cs:36`. `App.OnStartup` never probes for a prior session.
Files accumulate in `%LOCALAPPDATA%\HardwareAuditToolkit` forever, one per launch
(fresh `SessionId` at `App.xaml.cs:223`).

The stated purpose — "a forced termination cannot lose findings" — is
unimplemented. The data is saved and then abandoned.

### F8 — Engineering vocabulary on the operator's screen and in the report

- Dashboard cards show an **`exclusive` badge** (`DashboardHomeView.xaml:51-54`).
  That is an orchestrator invariant, not operator information.
- Cards show a raw **`Category`** string identical to the module id —
  `DashboardViewModel.cs:13-17` passes `"keyboard"` as both id and category.
- Report findings carry `BelowNormal` thread priority (`CpuStressModule.cs:136`),
  `"(graceful)"` (`MouseTestModule.cs:346`), `"sub-screen"` (`:161`), `"duck"`
  (`:215`), .NET exception type names (`CpuStressModule.cs:232`), and
  `"Module.Start threw an exception"` (`TestOrchestrator.cs:156`).
- Statuses render as raw enum identifiers: `NotRun`, not "Not run"
  (`HtmlReportTemplate.cs:52,63,76`).
- The Measurements table has a **`Context`** column printing lowercase internal
  tags (`"cpu"`, `"storage"`, `"wpm"`, `"trace"`, `"pattern"`) and `-` about half
  the time (`HtmlReportTemplate.cs:98,106`).
- Findings are a bare `List<string>` (`AuditSession.cs:110`) written in four
  voices: operator-subject, impersonal passive, telegraphic data dump
  (`"Clicks — L:1 R:1 M:1; wheel ticks:1; drags:1."`), and code-speak.
- `OperatorActions` is a near-verbatim restatement of `Status` for
  keyboard/mouse/monitor, and empty for the other two — so section shape differs
  per module.
- **Three template sections can never render**: **Machine ID**
  (`AuditSession.MachineId` never assigned anywhere — so the architecture's
  "machine-identified record" does not exist), **Notes** (never assigned, no UI),
  **Artifacts** (zero `Artifacts.Add` calls in the solution).
- Timestamps are UTC-only (`HtmlReportTemplate.cs:136-139`), so they never match
  the technician's wall clock.

### F9 — Four sources of truth for five modules

`ITestModule.Metadata` (DI-discovered via `IEnumerable<ITestModule>`), the
hardcoded list in `DashboardViewModel.cs:11-18`, a *second* hardcoded dictionary
of the same five descriptions in `ModulePlaceholderViewModel.cs:31-38`, and a
string `switch` in `NavigationService.cs:16-24`.

`ModulePlaceholderViewModel`/`ModulePlaceholderView` are Phase-1 scaffolding for
modules that all now exist. `DashboardItemViewModel.cs:8-10` still describes
itself as stubs that "will replace these stubs."

### F10 — The dashboard is a launcher where the workflow needs a workspace

`DashboardItemViewModel.cs:20-24` carries no status. The operator cannot see what
has been run, what passed, what is left, or what the report will contain — the
only way to find out is to export and read the HTML.

`README.md:102-103` claims "an always-available Export Report button lives in the
persistent header." **This is false.** `MainWindow.xaml:38-40` is a bare
`ContentControl`; Export exists on the dashboard alone
(`DashboardHomeView.xaml:25-29`). There is no persistent header — six views each
copy-paste their own `ExitOverlay` and their own "Back to dashboard" button.

Failure surfacing is also silent: `DashboardViewModel.cs:34` guards on
`result.Success && result.JsonPath is not null`, which makes the clipboard branch
of `ExportResultDialog.xaml:19-21` unreachable and shows the operator nothing on a
hard failure. `ReportExportService.cs:94-112` returns `true` even when
`Clipboard.SetText` threw, so the operator can be told the data is on the
clipboard when it is not.

### F11 — Omissions that were not deliberate

- **No display-sleep prevention.** Zero matches for `SetThreadExecutionState` in
  the solution. During a 5-minute burn-in the monitor blanks and the operator
  assumes the machine crashed (**`todo.md` item 3**). This is *"the first ten
  seconds effortless"* failing at the moment of maximum anxiety.
- **Temperature is honest but mute.** `"N/A (sensor unavailable)"` is correct
  (`CpuStressModuleViewModel.cs:112,180`), but nothing explains that elevation is
  the reason; `LibreHardwareMonitorSensorProvider.cs:39-51` swallows the open
  failure silently.
- **The CPU graph** is a fixed 640×220 `Canvas` in a `Viewbox Stretch="Uniform"`
  (`CpuStressView.xaml:77-83`) — it scales but gutters on wide windows rather
  than filling.
- **No way to skip.** `Skipped` exists in the enum with no affordance to say
  "this machine has no mouse."
- **Data recorded outside a running module is silently dropped.**
  `KeyboardTestModule.RecordWpm` has no `IsRunning` guard, while
  `KeyboardTestView.xaml:92` tells the operator "Gross WPM and accuracy are
  recorded to the session."

---

## 5. Where the taste is good — protect it

- **§6 exit discipline.** Mouse-only and keyboard-only paths independently
  sufficient from every screen, including fullscreen. Held across six views
  without exception. A real point of view, consistently applied.
- **§9.7 fault containment.** Background run loops guarded at source, teardown on
  both `Cancel()` and view-model disposal, per-call degradation in
  WMI/DDC/sensors, and a diagnostics log so no fault is silent. Invisible when
  working — exactly the guide's "just works quietly."
- **The write-path cascade** (`SessionExporter.cs:93-127`) probes each candidate
  with a real write-test before committing, so a USB stick pulled mid-write costs
  nothing. Taste applied to a failure mode most tools ignore.
- **`CpuStressView.xaml.cs:8-13`** — the one place a default was consciously
  reversed, with the reasoning written down. That comment is the standard the
  other four modules should be held to.
- **`TestOrchestratorTests.cs`** — 13 tests asserting real invariants, including a
  double-record regression and a `Start`-throws path.
- **`CpuStressFaultInjectionTests.cs`** — injects a worker throw and asserts the
  process survives. Exactly the right test for the stated principle.

---

## 6. Next pass — suggested action plan

Ordered so that cheap, reversible subtractions land before the decisions that
need a human call, and the output fixes land before any new surface.

### Pass A — Subtract (all cheap to reverse, no decisions needed)

| # | Action | Files |
|---|---|---|
| A1 | Delete the WPM sub-screen: `IsWpmMode`/`WpmTarget`/`StartWpm`/`ScoreWpm`, the XAML block, and `KeyboardTestModule.RecordWpm` | `KeyboardTestModuleViewModel.cs`, `KeyboardTestView.xaml:33,87-114`, `KeyboardTestModule.cs:210-230` |
| A2 | Delete the duck-tracing sub-screen: `BuildTraceTarget`/`StartTrace`/`AddTrace`/`EndTrace`, the XAML block, canvas code-behind, and `MouseTestModule.RecordTrace` | `MouseTestModuleViewModel.cs:131-160,277-342`, `MouseTestView.xaml:37,61-88`, `MouseTestView.xaml.cs:25-51`, `MouseTestModule.cs:203-218` |
| A3 | Delete `SessionCheckpointStore`, `ISessionCheckpointStore`, the four call sites, the DI registration, and both checkpoint test files — **or** implement the resume prompt in `App.OnStartup` that justifies them. Do not leave write-only. | `Core/SessionCheckpointStore.cs`, `Core/ISessionCheckpointStore.cs`, `TestOrchestrator.cs:240,252,289,343`, `App.xaml.cs:230`, `Tests/SessionCheckpointTests.cs`, `Tests/OrchestratorCheckpointTests.cs` |
| A4 | Delete `ModulePlaceholderViewModel`, `ModulePlaceholderView`, its `DataTemplate`, and the `NavigationService` default arm | `ViewModels/ModulePlaceholderViewModel.cs`, `Views/ModulePlaceholderView.xaml`, `MainWindow.xaml:18-20`, `NavigationService.cs:23` |
| A5 | Remove the `exclusive` badge and the `Category` line from dashboard cards; drop `IsExclusive`/`Category` from `DashboardItemViewModel` | `DashboardHomeView.xaml:51-59`, `DashboardItemViewModel.cs` |
| A6 | Remove `Skipped` and `Unsupported` from `TestStatus` and their read sites; remove the dead `TestOrchestrator.cs:373` arm | `IModuleMetadata.cs:64-68`, `TestOrchestrator.cs:359,367,371-374`, `HtmlReportTemplate.cs:145`, `MonitorTestModuleViewModel.cs:193` |
| A7 | Remove the Machine ID, Notes and Artifacts template branches and the unused model members — or populate them. Prefer removal; `MachineId` is the only one worth keeping, and only if actually set. | `HtmlReportTemplate.cs:49-50,113-119,122-126`, `AuditSession.cs:50,56` |
| A8 | Remove the unreachable `Warning` arm in the mouse VM and the stale `duck/bicycle` doc comment | `MouseTestModuleViewModel.cs:20,199` |

**Acceptance:** solution builds with zero warnings; `dotnet test` green with the
deleted-area tests removed; no `TestStatus` member without a write site; no
interface with a write-only method.

### Pass B — Decide the two questions that were never asked

These need the owner's call before implementation. Both are recorded in §7 below.

| # | Action | Depends on |
|---|---|---|
| B1 | Split "leave this screen" from "cancel this test." Give the fullscreen pattern window only the former; keep Ctrl+E as a true abort everywhere. Fixes `todo.md` 2. | Decision D1 |
| B2 | Apply one trust model to every module. Fixes `todo.md` 1, and requires either relaxing the keyboard `Warning` or adding a mouse coverage floor — not one each. | Decision D2 |
| B3 | Stop `MonitorTestView` auto-starting on load, or make leaving-without-confirming a non-event. Align all five modules to one auto-start policy and write the reasoning in a comment, as `CpuStressView.xaml.cs:8-13` already does. | Decision D1 |

### Pass C — Fix the output, then lock it down with tests

| # | Action | Files |
|---|---|---|
| C1 | Stamp `CompletedAt` **before** serialization; derive the filename from export time so re-export never overwrites | `ReportExportService.cs:45-61`, `SessionExporter.cs:45-47,171-172` |
| C2 | Render **every** module, including untested ones, and lead the report with counts ("3 passed, 1 failed, 1 not run"). A partial audit must never read `Passed`. | `HtmlReportTemplate.cs:55-71`, `TestOrchestrator.UpdateOverallStatus`, needs the module roster from `TestOrchestrator.Modules` |
| C3 | Introduce a report DTO between `AuditSession` and both writers, with display-name mapping for statuses, so raw enums, internal context tags and exception type names stop reaching the reader | new `Core/Reporting/ReportModel.cs`, `HtmlReportTemplate.cs`, `SessionExporter.cs:45` |
| C4 | Wire `FlagDefect(note)` to a real text field on all three screens. **Highest-value missing feature in the product.** | `KeyboardTestView.xaml`, `MouseTestView.xaml`, `MonitorTestView.xaml` + the three VMs |
| C5 | Normalise finding voice to one convention; move internal/diagnostic strings out of `Findings` into a separate diagnostics channel | all five `Core/Modules/*.cs`, `TestOrchestrator.cs:156,319` |
| C6 | Surface export failure: drop the `JsonPath is not null` guard, show the clipboard state honestly, and stop returning `true` when `Clipboard.SetText` threw | `DashboardViewModel.cs:34`, `ReportExportService.cs:94-112`, `ExportResultDialog.xaml.cs` |
| C7 | Show the HTML path, not the JSON path, as the primary "Saved to" line — the HTML is the human deliverable | `ExportResultDialog.xaml:16-17` |
| C8 | Golden-file the HTML and JSON for four sessions: empty, one-module, partial/mid-run, and full-with-defect. Add an escaping test feeding `<script>` through hostname and a finding. | `Tests/ReportExportTests.cs` + new golden files |
| C9 | Add local time alongside UTC in the report header | `HtmlReportTemplate.cs:136-139` |

**Acceptance:** an export taken with nothing run reads unmistakably as "nothing
was audited"; an export with one module run names the four that were not; no raw
enum identifier, internal tag, or exception type name appears in either artifact;
golden files fail on any wording drift.

### Pass D — The operator's environment (`todo.md` item 3)

| # | Action | Files |
|---|---|---|
| D1 | `SetThreadExecutionState(ES_CONTINUOUS \| ES_DISPLAY_REQUIRED \| ES_SYSTEM_REQUIRED)` for the duration of a burn-in, cleared on stop/cancel/dispose | `CpuStressModule.cs` or a small `DisplaySleepBlocker` in Infrastructure |
| D2 | Explain *why* temperature is unavailable: surface the sensor-open failure from `LibreHardwareMonitorSensorProvider` and show a one-line "run as administrator for core temperatures" notice instead of a bare `N/A` | `LibreHardwareMonitorSensorProvider.cs:39-51`, `ISensorProvider.cs`, `CpuStressModuleViewModel.cs:112,180`, `CpuStressView.xaml:57-61` |
| D3 | Make the graph fill available width: bind the `Canvas` width or switch the `Viewbox` to `Stretch="Fill"` horizontally with a fixed vertical scale | `CpuStressView.xaml:77-83`, `CpuStressModuleViewModel.BuildSeries` |

### Pass E — Coherence cleanup (only after A–D)

| # | Action |
|---|---|
| E1 | Single source of truth for the module list: build the dashboard from `TestOrchestrator.Modules`/`IModuleMetadata` and delete the hardcoded list and the `NavigationService` string switch (`DashboardViewModel.cs:11-18`, `NavigationService.cs:16-24`) |
| E2 | Real persistent header in `MainWindow.xaml` carrying Export + Exit, and delete the six copy-pasted `ExitOverlay`/"Back to dashboard" pairs — this is what `README.md:102-103` already claims exists |
| E3 | Per-module status on the dashboard so the operator can see the audit's shape before exporting |
| E4 | Rewrite `Src/README.md` as a description of current state rather than a phase changelog, and remove the false persistent-header claim |
| E5 | Add a `schemaVersion` field to the JSON so downstream consumers have a compatibility marker |

---

## 7. Open decisions requiring the owner

**D1 — What does leaving a test mean?**
Currently every exit path records `Cancelled`, which conflates "I looked and moved
on," "I aborted," "the app closed," "the timeout fired," and "I deliberately
stopped the burn-in early." Options: (a) leaving is a non-event and only Ctrl+E
aborts; (b) leaving records `NotRun`; (c) `Cancelled` splits into `Aborted` vs
`StoppedEarly`. Blocks B1, B3, and part of C2.

**D2 — Is operator judgment authoritative, or is coverage?**
Currently the keyboard says coverage (and overrides the operator with `Warning`);
the mouse says judgment (and accepts zero evidence). One answer must apply to
both. Options: (a) operator is authoritative everywhere, coverage is reported as a
measurement not a verdict — this is what `todo.md` item 1 asks for; (b) coverage
is authoritative everywhere, which means the mouse needs a floor and the operator
cannot pass an untested device. Blocks B2.

**D3 — Does the tool need crash recovery at all?**
If yes, implement the resume prompt and the checkpoint earns its four write sites.
If no, delete it. Write-only is the one option with no defence. Blocks A3.

---

## 8. Verification

```powershell
dotnet build Src\HardwareAuditToolkit.sln
dotnet test  Src\HardwareAuditToolkit.sln
```

The solution is expected to build with zero warnings. Additional checks worth
running after each pass:

- No `TestStatus` member without a write site.
- No interface with a write-only method.
- No raw enum identifier, internal context tag, or .NET exception type name in a
  generated `.html` or `.json`.
- An export with zero modules run must not read as a pass.

---

## 9. Process notes

- `Src/README.md` is a phase changelog rather than a description of current
  state, and already contains one false claim (the persistent-header export
  button, F10).
- `AGENTS.md` mandates a `lode/` knowledge repository that does not exist in this
  repo. Per its own instructions, confirm before creating one; this audit and the
  §7 decisions are the natural seed content.
