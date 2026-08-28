# Terminology

Domain language used across the codebase, the architecture document and this lode.
`§n` references throughout the code point at sections of
[`../Src/docs/hardware-audit-toolkit-architecture.md`](../Src/docs/hardware-audit-toolkit-architecture.md).

## Roles

- **Operator / technician** — the person physically at the machine running the tool.
  The only human user. Not the person who later reads the report.
- **Reader** — whoever consumes the exported report afterwards. Has no access to
  the machine and no context beyond the document.

## Session and results

- **Audit session** — one run of the application against one machine. Exactly one
  `AuditSession` per process launch, created eagerly at DI configuration time.
- **Module result** — one `ModuleResult` appended to the session per module *start*.
  Restarting a module appends a second record rather than replacing the first.
- **Finding** — a free-form human-readable sentence added to the report. A bare
  `List<string>`; carries no severity, no key and no structure.
- **Measurement** — a timestamped label/value/context row. `Context` is an internal
  tag string (`"cpu"`, `"wpm"`, `"pattern"`), currently rendered to the reader.
- **Operator action** — a logged technician acknowledgement. In practice a
  restatement of the terminal status detail.
- **Artifact** — intended as a loose file (screenshot, raw log) beside the session
  JSON. **Never populated by any module.**
- **Overall status** — session-level status, collapsed from the module statuses by
  precedence `Failed > Warning > Cancelled > Passed`.

## Module vocabulary

- **Test module** — a self-contained test behind `ITestModule`. Five exist.
- **Exclusive module** — declares `IsExclusive`, so the orchestrator permits only
  one at a time. Keyboard, mouse, monitor and CPU stress are exclusive.
- **Ambient service** — continuous background work that sits outside the exclusive
  queue: sensor polling and the device-change listener.
- **Module phase** — lifecycle position: `NotStarted → Setup → Running →
  AwaitingOperatorConfirmation → Complete | Cancelled`. Distinct from `TestStatus`.
- **Test status** — the recorded verdict. See
  [`reporting/status-vocabulary.md`](reporting/status-vocabulary.md); two of its
  eight members are never assigned.
- **Operator confirmation** — for perceptual checks (monitor uniformity, tracing)
  the technician's acknowledgement *is* the recorded status, by design.
- **Sub-screen** — an extra view launched from inside a module rather than as its
  own module: the keyboard WPM test and the mouse duck-tracing test. Neither
  affects any status.
- **Capability** — a declared requirement string such as `"DDC/CI"`. Declared but
  never enforced; `CheckPreconditions()` returns `true` everywhere.

## Exit vocabulary

- **Exit overlay** — the reusable "Exit Test (Ctrl+E)" control placed on every view.
- **Exit request** — `ExitRequestedMessage` on the event bus. Cancels the running
  module and returns to the dashboard; it does **not** quit the app.
- **Native close** — the window `X`. The only path that ends the process.
- **Back to controls** — the pattern window's non-cancelling close. Visually
  adjacent to the exit overlay but semantically opposite.

## Reporting vocabulary

- **Export cascade** — the §9.6 fallback chain: app directory → Desktop → `%TEMP%`
  → manual folder picker → clipboard.
- **Write-test probe** — a throwaway file written and deleted before the real
  payload, so a vanished volume is caught before data is at risk.
- **Checkpoint** — a durable JSON snapshot under `%LOCALAPPDATA%`. Written on
  module completion and shutdown; **never read back**.

## Infrastructure vocabulary

- **Raw input** — `RegisterRawInputDevices` keyboard/mouse capture, read on a
  private message-only window. Scan-code based for the keyboard.
- **Message-only window** — a hidden `HWND` that exists solely to receive Win32
  messages, pumped on its own thread.
- **DDC/CI** — `dxva2.dll` monitor control. Used only to read/set brightness, and
  only best-effort.
- **Sensor provider** — the LibreHardwareMonitor adapter. Broadcasts
  `SensorReadingsMessage`; silently yields nothing when it cannot open, which is
  the usual no-admin case.
- **Best-effort** — a documented contract: the call may fail, must not throw, and
  must report an honest reason instead of a fabricated value.
- **Bundle extraction directory** — the single-file host's native-component
  extraction path, redirected to `%LOCALAPPDATA%\HardwareAuditToolkit\extract`
  so security teams can allow-list one location.
- **Single-instance enforcer** — a `Global\`-prefixed named mutex checked before
  any hook or thread starts; a second launch foregrounds the first.

## Process vocabulary

- **Lode** — this directory. AI-owned project memory describing the *current state*
  of the system, never a changelog.
- **Taste audit** — [`../taste-audit.md`](../taste-audit.md), the design review
  that produced the current roadmap.
