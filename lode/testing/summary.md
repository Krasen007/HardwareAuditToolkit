# Testing

`Src/Tests/` — xunit, 11 files, **61 executed cases, all passing**, no hardware
required.

```powershell
dotnet test Src\HardwareAuditToolkit.sln
```

## Coverage map

| File | Cases | Substance |
|---|---|---|
`TestOrchestratorTests.cs` | 13 | **Strongest file.** Exclusivity, single-start, precondition rejection, cancel→`Cancelled`, timeout force-cancel, the double-record regression, `Start` throws→`Failed`, restart appends a second record |
`KeyboardModuleTests.cs` | 8 | Layout uniqueness/labels, all-keys→`Passed`, missing→`Warning`, flag→`Failed`, cancel stops capture, out-of-layout keys ignored, repeat counter |
`MouseModuleTests.cs` | 8 | Click-vs-drag classification incl. same-sample release edge cases, flag/cancel, trace measurement |
`ReportExportTests.cs` | 7 | Cascade: writes pair, JSON round-trip, picker fallback, clipboard fallback, total failure, null guards. **One** HTML test |
`MonitorModuleTests.cs` | 6 | Confirm/flag/cancel, `ApplyBrightness` delegation, DDC-unsupported still passes |
`Phase2ModuleTests.cs` | 5 | DI discovery, system info terminal state, stress start/cancel, sensor smoke test, DI lifetimes |
`ReportExportServiceTests.cs` | 4 | App-layer cascade + `CompletedAt` semantics |
`NavigationServiceTests.cs` | 2 (7 cases) | id→type routing map |
`CpuStressFaultInjectionTests.cs` | 1 | **High value.** Injected worker throw → `Failed` + finding, process survives |

## Conventions

- **Core logic** mirrors `TestOrchestratorTests.cs`.
- **Module/orchestrator integration** mirrors `Phase2ModuleTests.cs` — compose through
  DI, drive a fake, assert the **terminal status** rather than intermediate state.
- **Infrastructure is always faked** — `FakeRawKeyboardInput`, `FakeRawMouseInput`,
  `FakeDdc`. No test touches real hardware, so the suite runs anywhere.
- **Time is injected** via `TimeProvider`, so timeout behaviour is deterministic:

```csharp
var time = new FakeTimeProvider();
var orchestrator = new TestOrchestrator(session, [module], time);
orchestrator.TryStartModule("keyboard", out _);
time.Advance(TimeSpan.FromMinutes(31));    // force the MaxDuration cancel
```

## What is genuinely well covered

Orchestration, the module state machines, mouse input classification, the write-path
cascade, and the "degrade, don't crash" guard — roughly 40 of the 56 test methods
assert real behaviour.

## The gap that matters: the report is untested

**The product's actual output has one test with four assertions:**

```csharp
Assert.Contains("TESTHOST", html);
Assert.Contains("Keyboard Test", html);
Assert.Contains("<html", html);
Assert.Contains("All keys registered.", html);
```

Untested in `HtmlReportTemplate`: the empty-modules branch, the "in progress" vs
completed branch, the measurements table, operator actions, artifacts, notes, the
Machine ID branch, `StatusClass` for any status, `Enc` escaping, and `Fmt`.

**There is no golden/approval file**, so any wording or layout change is invisible to
CI. This is why the `CompletedAt` ordering bug and the `Passed`-with-nothing-tested
defect both shipped. Roadmap C8 adds golden files for four sessions — empty,
one-module, mid-run, full-with-defect — plus an escaping test. **Do that before
changing report wording.**

## Zero coverage

- `ExportResultDialog`, and `DashboardViewModel.ExportReport`'s success guard — i.e.
  exactly the code that makes export failure invisible.
- `ReportExportService.ShowClipboardFallback`'s unconditional `return true`.
- `SystemInfoProvider` and `SystemInfoModule.Populate` — the field set, labels and
  contexts, which is why the UI/report divergence went unnoticed.
- `CpuStressModule`'s natural `Passed` completion, the `Duration` clamp, and
  `PublishTelemetry`.
- Session-level `Warning` aggregation, and the nothing-run `NotRun` case.
- All Infrastructure: `RawKeyboardInput`, `RawMouseInput`, `DdcCiControl`,
  `DeviceChangeService`, `ExitHotkeyService`, `SingleInstanceEnforcer`,
  `BundleExtractionBootstrap`, `FileDiagnosticLog`.
- View-model maths: WPM/accuracy and trace coverage.
- Exit paths: `App.HandleExitRequested` and `OnMainWindowClosing`.

## Tests that assert plumbing rather than behaviour

Worth knowing so they are not mistaken for safety:

- **Four near-identical DI-registration tests** across `Phase2ModuleTests`,
  `KeyboardModuleTests`, `MouseModuleTests`, `MonitorModuleTests` — three repeat the
  same literal `Assert.Equal(5, …ITestModule count)` and invoke
  `App.ConfigureServices` by reflection. They assert container shape, not behaviour.
- `SensorProvider_OpensWithoutThrowing` — self-described as having no assertions on
  readings.
- `NavigationServiceTests` uses `RuntimeHelpers.GetUninitializedObject` to avoid
  constructing view models, so it asserts the map only.
- Three copies of a reflection helper reach into `TestOrchestrator._session`.

## Hygiene note

`ReportExportServiceTests` writes into the **live application directory** — the real
first cascade candidate — and deletes afterwards, so it leaves files behind if it
fails mid-test.

## Rule for new work

Any change to the reporting layer adds a golden-file assertion **first**. Any new
background loop gets a fault-injection test shaped like
`CpuStressFaultInjectionTests`. Any new module gets a terminal-status test shaped like
`Phase2ModuleTests`.
