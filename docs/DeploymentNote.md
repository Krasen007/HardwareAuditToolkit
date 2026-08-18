# Hardware Audit Toolkit — Deployment Note

*Ship this page alongside the published artifact. It is intended to be handed
to a security/EDR team verbatim when requesting allow-listing (§9.1).*

## What this is

A portable sysadmin hardware audit tool for burn-in/refurb stations. v1 scope:
keyboard, mouse, monitor, system info, and CPU stress testing. It reads
hardware state, captures input events, and produces a JSON + HTML report per
audit session. It performs no installation, requires no internet connection,
and runs without administrator privileges.

## Artifact

| Field | Value |
|---|---|
| File | `HardwareAuditToolkit.exe` (self-contained, single-file publish) |
| SHA-256 | `F716DADDB0E745240A0C7FC818BC7E63B2F7E8322B8632E8B9F68199317A9E12` |
| Size | 73,778,820 bytes |
| Version | 0.1.0 |
| Signing | *Not yet Authenticode-signed (placeholder — code signing is table stakes before wide rollout, §9.1)* |

> Recompute the hash on every build: `Get-FileHash HardwareAuditToolkit.exe -Algorithm SHA256`
> The hash above is for the current Phase 0 build only.

## How it is packaged

- **Single self-contained .exe.** .NET bundles the runtime and all managed
  assemblies inside the executable. On first run the .NET host **extracts
  native components to a directory on disk** before the app starts.
- **Extraction directory:** `%LOCALAPPDATA%\HardwareAuditToolkit\extract`
  (controlled by the `DOTNET_BUNDLE_EXTRACT_BASE_DIR` environment variable,
  which the app sets at user scope on first launch — idempotent, only if not
  already configured). Actual extraction path is
  `%LOCALAPPDATA%\HardwareAuditToolkit\extract\<app>\<bundle-id>\`.
  **Please allow-list this one path** rather than blanket-permitting user-temp.
- **First launch caveat:** the very first launch happens before the app can set
  the variable, so it still extracts once to `%TEMP%\.net\HardwareAuditToolkit\…`.
  All subsequent launches use the fixed path above.

## Why it may look suspicious

- Single-file extraction behavior is a common **dropper heuristic**.
- The keyboard test uses a low-level keyboard hook and raw input capture, which
  resembles **keylogger behavior** to AV/EDR heuristics.

These are core, non-negotiable features of the tool. The mitigations are:
predictable extraction path (above), code signing (pending), and this note.

## Recommended allow-listing guidance

- Allow-list by publisher/signature once signed; until then, by SHA-256 above.
- Allow the extraction path `%LOCALAPPDATA%\HardwareAuditToolkit\extract`.
- The app never writes outside the session-report paths (§7) unless the
  technician explicitly picks a report destination (§9.6).

## Fallback artifact

If a site still blocks the single .exe, use the **folder build** instead —
`PortableFolder` publish output. Functionally identical, no extraction on
first run. No code changes required (it is a publish-profile difference only).
