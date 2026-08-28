# Practices

Patterns this codebase actually follows. Deviating from these breaks something
concrete — each entry says what.

## Layering

`App → Core → Infrastructure`, one direction only.

- **Core must not reference UI or author P/Invoke.** It reaches hardware purely
  through Infrastructure interfaces (`IRawKeyboardInput`, `IDdcCiControl`,
  `ISensorProvider`). This is what makes the module state machines unit-testable
  with fakes and no hardware.
- **App owns every Windows interaction.** File dialogs, clipboard, message boxes
  and window placement live in App, injected into Core as delegates. See
  `ReportExportOptions` for the pattern: Core defines the cascade, App supplies
  `RequestManualFolder` and `ShowClipboardFallback`.

## Module view models must be transient

```csharp
// App.xaml.cs — modules and providers are singletons…
services.AddSingleton<KeyboardTestModule>();
// …but their view models are NOT.
services.AddTransient<KeyboardTestModuleViewModel>();
```

**Invariant:** a module view model subscribes to the event bus in its constructor
and unsubscribes in `Dispose()`. `NavigationService.SetScreen` disposes the
outgoing screen. Registering one as a singleton leaves it permanently dead after
the first navigation away — it would never resubscribe. `NavigationServiceTests`
guards the routing; the lifetime is guarded by DI-registration tests.

## Tear down OS resources twice

Anything holding a hook, a raw-input registration or a thread is released in
**both** places:

1. `Module.Cancel()` — the orchestrator-driven path.
2. View-model `Dispose()` — the navigation-driven path.

Neither implies the other. The operator can navigate away without cancelling, and
the orchestrator can cancel without the screen changing. Releasing in only one
place leaks input capture across navigation.

## Best-effort hardware calls

Every Infrastructure call that can fail returns a result carrying an honest reason
rather than throwing or fabricating a value:

```csharp
// DdcCiControl.GetBrightness — a reason, never an exception, never a fake number
return new BrightnessReading { Supported = false,
    Detail = "Monitor does not report brightness (VCP 0x10 unsupported)." };
```

**Rule:** never substitute `0` for "unknown". A missing sensor renders as
`"N/A (sensor unavailable)"`, not `0.0 %`. A fabricated zero in an audit report is
worse than an absent one.

## Guard every background loop at its source

A throw on a non-UI thread kills the process. Every run loop — CPU stress workers,
raw input capture threads, the `Ctrl+E` hook thread, the device-change window —
wraps its body so a fault becomes `Failed` or "unavailable":

```csharp
catch (Exception ex)
{
    Findings.Add($"Burn-in worker failed ({ex.GetType().Name}): {ex.Message}");
    cb = StopInternal(TestStatus.Failed, "...");
}
```

`App.WireGlobalFaultHandlers` is the last resort, not the strategy.

## Diagnostics never throw

`IDiagnosticLog`/`FileDiagnosticLog` is injected into every fault-guard path and
swallows its own failures. It writes to
`%LOCALAPPDATA%\HardwareAuditToolkit\diagnostics.log` so a published build is
diagnosable without a debugger. A logging failure must never become the incident.

## Probe before you write

The export cascade write-tests each candidate directory with a throwaway file
before the payload, so a pulled USB stick is detected while the session is still
safely in memory. Apply the same shape to any new durable write.

## Event bus over direct references

`WeakReferenceMessenger` carries module→UI traffic (`KeyEventMessage`,
`MouseEventMessage`, `StressTelemetryMessage`, `SensorReadingsMessage`,
`DeviceTopologyChangedMessage`, `ExitRequestedMessage`). Core modules therefore
never hold a view reference. Because registrations are weak, **the subscriber must
be kept alive by DI** — this is another reason view models are resolved rather
than constructed ad hoc.

## Interop: keep `DllImport` where the generator cannot follow

`SYSLIB1054` is suppressed per file, with justification, in `DeviceChangeService`,
`DdcCiControl`, `RawKeyboardInput` and `RawMouseInput`: the `LibraryImport`
generator cannot marshal non-blittable structs carrying `string`/`ByValTStr`
members (`Wndclassex`, `MONITORINFOEX`, `PHYSICAL_MONITOR`) or the
`EnumDisplayMonitors` delegate callback. Blittable signatures such as
`SetWindowPos` **do** use `LibraryImport`, which is why App sets
`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. Do not "modernise" the suppressed
files without first making the native types blittable.

## Analyzer-enforced house style

The solution builds with **zero warnings**; keep it that way.

- Collection expressions: `[]` and `[.. items]`, not `new List<T>()`,
  `Array.Empty<T>()` or `new[] { … }`.
- Primary constructors where nothing sits between parameter and field:
  `sealed class NavigationService(ShellViewModel shell, IServiceProvider services)`.
- Explicit precedence parentheses: `(dx * dx) + (dy * dy)`.
- `string.Contains(char)` for single characters.

## Test conventions

- Core logic mirrors `TestOrchestratorTests.cs`; module/orchestrator integration
  mirrors `Phase2ModuleTests.cs` — compose through DI, drive the fake, assert the
  **terminal status** rather than intermediate state.
- Infrastructure is always faked (`FakeRawKeyboardInput`, `FakeRawMouseInput`,
  `FakeDdc`). No test touches real hardware, so the suite runs anywhere.
- Time is injected via `TimeProvider` so timeout behaviour is deterministic.

```csharp
var time = new FakeTimeProvider();
var orchestrator = new TestOrchestrator(session, [module], time);
orchestrator.TryStartModule("x", out _);
time.Advance(TimeSpan.FromMinutes(31));   // force the MaxDuration cancel
```

**Known weak spot:** the report — the product's actual output — has one test with
four `Contains` assertions. Any change to reporting should add golden files first.
See [`testing/summary.md`](testing/summary.md).

## Writing findings

Findings are read by someone who has never seen the machine or the code. Today
they leak `BelowNormal`, `"(graceful)"`, `"sub-screen"`, `"duck"`, .NET exception
type names and `"Module.Start threw an exception"`. When touching any module,
write findings for the reader, and route internal diagnostics to `IDiagnosticLog`
instead. See [`reporting/html-report.md`](reporting/html-report.md).
