# Operator feedback — resolved

1. ~~on the cpu stress test the temperature is not shown, if permissions are needed show a notice, if not enable the CPU temperature on the graph, also prevent the screen from going to sleep as the test takes 5 min the monitor may go to sleep causing panic as if the computer crashed. also the live graph must fit or scale with the window of the app~~

All three CPU stress complaints have been addressed:
- Temperature N/A now includes a notice when sensor access is unavailable ("run as administrator for core temperatures") via `ISensorProvider.UnavailableReason`.
- `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` is called on burn-in start and cleared on stop/cancel, so the display stays on during the run.
- The live graph `Viewbox` now uses `Stretch="Fill"` and stretches horizontally to fill the window.

2. ~~in the monitor test, when the pattern is on full screen do not show the exit test button, only the back to control, this will help the operator to actually confirm if working on the other screen and not show the report the test was cancelled~~

Fixed: `MonitorPatternWindow` now hides the `ExitOverlay` (`Visibility="Collapsed"`). The operator sees only "Back to controls", which returns to the monitor screen without cancelling. Global `Ctrl+E` still works.

3. ~~on the keyboard test, do not say its a warning that not all buttons are tested if the operator did not flag a defective key.~~

Fixed: `KeyboardTestModule.Confirm()` now resolves `Passed` regardless of coverage when the operator confirms. Missing keys are recorded as a finding, not a verdict. The operator's judgment is authoritative.
