# Mouse Test Module

`Core/Modules/MouseTestModule.cs` + `App/ViewModels/MouseTestModuleViewModel.cs`
+ `App/Views/MouseTestView.xaml`. Exclusive, 30-minute cap, auto-starts on `Loaded`.

**Purpose:** confirm every button, both scroll directions, and drag-and-drop work —
and specifically catch a button that releases mid-drag.

## Input classification

`RawMouseInput` produces a scan-agnostic stream of button down/up, wheel ticks and
movement deltas. The module classifies a press/release pair by distance and time:

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Down: button down
    Down --> Click: up with little movement
    Down --> Dragging: movement exceeds threshold
    Dragging --> Drop: up - "released mid-drag, drop detected"
    Dragging --> Incomplete: device unplugged while held
    Click --> Idle
    Drop --> Idle
    Incomplete --> Idle
```

A drag is **distinctly flagged** from a click, and the log records distance and
duration: `"{label} button up — drag: {dist} px over {ms} ms"`. This distinction is
the module's real value — a worn microswitch that drops mid-drag passes a naive
click test.

## Hot-plug handling

This is the only module that handles device loss. It subscribes to
`DeviceTopologyChangedMessage` and, if the mouse disappears while a button is held,
records an honest incomplete-drag finding rather than freezing:

```csharp
Findings.Add("Mouse disconnected while a button was held — drag/drop incomplete (graceful).");
```

The subscription is registered in `Start` (guarded by `_deviceSubscribed`) and
unregistered in `StopInternal`. The screen also shows a banner when no mouse is
detected. **The keyboard module has no equivalent** — worth copying if D2 work
touches both.

Note `"(graceful)"` describes the app's fault handling, not the hardware, and
should not be in a reader-facing finding (roadmap C5).

## Pass criteria

```csharp
// Confirm() — unconditional
var summary = $"Operator confirmed. Clicks — L:{_leftClicks} R:{_rightClicks} " +
              $"M:{_middleClicks}; wheel ticks:{_wheelTicks}; drags:{_dragCount}.";
Findings.Add(summary);
if (!_traceRecorded) { Findings.Add("Operator confirmed without running the tracing sub-screen."); }
cb = StopInternal(TestStatus.Passed, "Passed — operator confirmed all mouse functions work.");
```

| Outcome | Trigger |
|---|---|
| `Passed` | operator confirms — **no coverage requirement whatsoever** |
| `Failed` | operator presses **Flag defective** |
| `Cancelled` | `Ctrl+E`, exit overlay, navigate away, or the 30-minute cap |
| `Warning` | **never produced** |

Zero clicks, zero scrolls, zero drags still yields `Passed`. This is the opposite
philosophy to the keyboard module, which overrides the operator on incomplete
coverage — open decision [D2](../plans/open-decisions.md).

Because `Warning` is never emitted, the `TestStatus.Warning` display arm that used to
live in the view model's status switch was removed in roadmap A8 (it was dead code).

## Screen surface

- Live header counters: `L / R / M` clicks, wheel ticks, drags.
- **Pinned, newest-first click/scroll/drag log**, capped at 500 lines. The counters
  duplicate information already in the log.
- Device-warning banner when no mouse is detected.
- Buttons: Start test, Confirm all works, Flag defective, Reset, Back to dashboard,
  and the tracing toggle.

## Teardown

Raw-input registration **and** the device-change subscription are released in both
`Cancel()` and view-model `Dispose()`. The view model also calls `CancelModule` on
disposal.

## Known defects

| Defect | Detail | Fix |
|---|---|---|
| Tracing sub-screen measures the operator | An 18-waypoint duck outline scored by path coverage within an 18px tolerance. Measures hand steadiness, not hardware. Does not affect any status. | A2 |
| **Tracing pollutes the counters** | `ToggleTrace` only flips a view flag; raw capture keeps running, so tracing inflates `LeftClicks`/`DragCount`, which then appear in the confirm finding as evidence. The log is merely hidden, not paused. | A2 |
| No coverage floor at all | `Passed` on zero input, while the keyboard demands 104/104. | B2 |
| Defect note is hardcoded | Every failure reads identically; the operator cannot say which button. | C4 |
| ~~`"duck/bicycle"` never existed~~ | ~~The view-model comment promised two shapes; only the duck was built.~~ | Fixed — comment now reads "duck" only (A8) |
| Internal terms in findings | `"sub-screen"`, `"duck"`, `"(graceful)"` reach the reader. | C5 |
| Counters never become measurements | Click/scroll/drag totals exist only embedded in the confirm sentence, so the report has no structured mouse data. | C5 |
| Telegraphic finding voice | `"Clicks — L:1 R:1 M:1; wheel ticks:1; drags:1."` is a data dump unlike any other module's prose. | C5 |

## Tests

`Src/Tests/MouseModuleTests.cs` — 8 methods over a `FakeRawMouseInput`, including
the genuinely valuable click-vs-drag classification edge cases (release in the same
sample, release after movement), flag/cancel paths, and the trace measurement. One
is a DI-registration check.

Untested: the trace coverage maths in the view model.
