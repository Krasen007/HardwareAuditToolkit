# Exit and Navigation

Architecture §6. **The strongest design decision in the product: a mouse-only path
and a keyboard-only path are each independently sufficient to leave any screen.**
Preserve this through any refactor.

## The four exit paths

```mermaid
graph TD
    K[Ctrl+E - global hook, own thread] --> M[ExitRequestedMessage]
    B[Exit Test overlay - every view] --> M
    M --> H[App.HandleExitRequested]
    H --> CA[orchestrator.CancelAll]
    H --> ND[navigation.NavigateToDashboard]
    X[Native close X] --> OC[App.OnMainWindowClosing]
    OC --> CA2[CancelAll]
    OC --> SD[Shutdown - the only app quit]
    ESC[Esc] --> NOTE[confirmation elsewhere; plain data in the keyboard module]
```

**Critical distinction:** `Ctrl+E` and the Exit overlay **do not quit the app**.
They cancel the running module and return to the dashboard. Only the native window
close ends the process.

```csharp
// App.HandleExitRequested
if (orchestrator.CurrentExclusiveModule is not null || orchestrator.RunningModules.Count > 0)
{
    orchestrator.CancelAll();
}
navigation.NavigateToDashboard();
```

The subscription lives on `App`, not on any view, so the handler runs regardless of
which screen is active — including from the fullscreen pattern window, which has no
normal chrome.

## Why Ctrl+E has its own thread

`WH_KEYBOARD_LL` callbacks run on the thread that installed the hook, and that
thread must keep pumping messages for the hook to fire at all. A starved or
blocked callback risks being skipped — independently of whether the UI appears
frozen. `ExitHotkeyService` therefore installs the hook on a **dedicated
background thread with a minimal message loop**, so exit responsiveness is
decoupled from whatever the WPF dispatcher is doing.

This is why the CPU stress workers run at `ThreadPriority.BelowNormal`: every core
still gets loaded (the point of a burn-in) while the OS continues to favour the UI
and hook threads under contention.

## Esc is deliberately asymmetric

- **Everywhere else:** `Esc` triggers "Exit test? Unsaved measurements will be lost."
- **In the keyboard module:** `Esc` is ordinary test data — just another key to
  register — and carries no exit meaning. The operator must be able to verify the
  `Esc` key works.

`Ctrl+E` still exits during raw keyboard capture, because the hook is independent
of the module's raw-input registration.

## The pattern window exception

The monitor pattern screen needs true edge-to-edge fullscreen for accurate colour
testing, so it has no native chrome. It relies on:

- the **auto-hiding** overlay panel (collapses after 3s of no mouse movement,
  reappears on `MouseMove`), and
- `Ctrl+E`.

It carries **two** buttons that hide and show together:

| Button | Action | Recorded result |
|---|---|---|
| Back to controls | `Close()` | nothing — test stays `Running` |
| Exit Test (Ctrl+E) | `ExitRequestedMessage` | **`Cancelled`** |

They look equivalent and do opposite things. This is
[`../../todo.md`](../../todo.md) item 2 and open decision
[D1](../plans/open-decisions.md).

## Navigation and disposal

`NavigationService` is a `moduleId → view model` switch plus one critical rule:

```csharp
private void SetScreen(object screen)
{
    if (_shell.CurrentScreen is IDisposable previous)
    {
        previous.Dispose();   // unsubscribe from the event bus, stop raw capture
    }
    _shell.CurrentScreen = screen;
}
```

Navigation is view-model-first: `MainWindow.xaml` is a single `ContentControl` bound
to `ShellViewModel.CurrentScreen`, and a `DataTemplate` per view model type picks
the view.

**Invariant:** every module view model that subscribes in its constructor must
unsubscribe in `Dispose()`, and must cancel its module there too. Keyboard and
mouse view models call `CancelModule` on disposal, which is why walking away from a
started test records `Cancelled`.

**Corollary:** module view models must be registered `AddTransient`. A singleton
would be disposed on first navigation away and never resubscribe. See
[`../practices.md`](../practices.md).

## Auto-start policy is currently inconsistent

| Screen | Starts the module |
|---|---|
| Keyboard | on `Loaded` |
| Mouse | on `Loaded` |
| Monitor | on `Loaded` |
| System Info | in the **view-model constructor** |
| CPU Stress | **only on explicit Start** |

Only the CPU stress screen documents its choice, and it is the one that got it
right:

```csharp
// Deliberate: NO auto-start — the operator starts the burn-in explicitly, so the
// machine isn't loaded the moment the screen opens.
```

The monitor case is actively harmful: auto-start means merely opening the screen
and leaving stamps `Cancelled` in the report. Roadmap B3.

## Single-instance activation

A second launch does not silently no-op. `SingleInstanceEnforcer.SignalFirstInstance()`
causes the first instance to restore, show, activate, focus, and briefly toggle
`Topmost` to beat foreground-activation restrictions.

## Invariants to preserve

1. Never remove an exit affordance without replacing it with an equivalent of the
   same modality (mouse or keyboard).
2. The `ExitRequestedMessage` subscriber stays on `App` — never move it to a view.
3. Only the native close quits the process.
4. Anything holding an OS resource is released in **both** `Cancel()` and
   view-model `Dispose()`.
