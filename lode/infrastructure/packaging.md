# Packaging and Runtime Hardening

Architecture §9.1–§9.4. These are Phase-0 concerns by deliberate choice: DPI
awareness, single-instance enforcement and extraction paths are expensive to retrofit
once coordinate maths and hooks exist.

## Publish profiles

| Profile | Output | Role |
|---|---|---|
| `PortableSingleFile` | one self-contained `.exe` | primary |
| `PortableFolder` | self-contained folder | documented fallback |

```powershell
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableSingleFile
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableFolder
```

**Why keep both:** single-file vs folder is a publish setting, not an architectural
difference. If a site blocks the single `.exe` despite the mitigations below, the team
hands out the folder build with **no code changes**. That hedge costs a build config,
not a redesign.

Three MSBuild targets support this:

- `RemovePdbFromPublishDirectory` — referenced projects emit portable PDBs into the
  publish folder; drop them so "exactly one artifact" holds.
- `RemoveWpfLocalisationFolders` — the WPF framework reference pulls ~15 MB of
  satellite `.resources.dll` folders for 13 cultures. The app ships English only, so
  the folder profile strips them. Gated to non-single-file publishes.
- `VerifySingleFileArtifact` — **opt-in** (`-p:VerifyPublishArtifacts=true`), errors
  unless exactly one `.exe` is produced. Opt-in so it never slows a normal build.

## §9.1 — the single-file extraction risk

.NET single-file publishing extracts native components to a temp bundle directory on
first run. Enterprise EDR and AppLocker frequently flag that as dropper-like. The risk
was **accepted, not designed away**, so it needs concrete mitigation:

```csharp
// BundleExtractionBootstrap — redirect away from %TEMP% to one allow-listable path
public const string EnvironmentVariableName = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";
public const string RelativeExtractionRoot = @"HardwareAuditToolkit\extract";
```

**Two properties worth remembering:**

1. **It takes effect from the *next* launch.** The runtime host reads
   `DOTNET_BUNDLE_EXTRACT_BASE_DIR` at process start, so the very first launch still
   extracts to `%TEMP%`. This is documented in `docs/DeploymentNote.md` rather than
   hidden.
2. **It never overrides an existing value** in Process, User or Machine scope — a
   group-policy setting wins.

Best-effort and idempotent; a failure only affects the allow-list path, not the app.

Remaining mitigations are **manual and pre-ship**:

- Authenticode code-signing via the org PKI — table stakes for SmartScreen and AV
  heuristics, not just EDR.
- An EDR pass (e.g. Microsoft Defender for Endpoint) before wide rollout.
- `docs/DeploymentNote.md`: SHA-256, publisher/signing info, the exact extraction
  path, and a plain description a technician can hand to a security team **before**
  the tool gets blocked on a live audit.

## §9.3 — single-instance enforcement

A named `Mutex` in the `Global\` namespace, so it holds across concurrent sessions
such as RDP or fast user switching. Checked **before any hook, thread or window
exists** — a duplicate launch must not install a global keyboard hook or spawn stress
threads before discovering it is a duplicate.

A second launch does not silently no-op; it foregrounds the first instance:

```csharp
window.Show(); window.Activate(); window.Focus();
window.Topmost = true; window.Topmost = false;   // beat foreground restrictions
```

## §9.4 — Per-Monitor V2 DPI

Declared in `app.manifest` from the first scaffolding commit, **not** via the
WinForms-specific `HighDpiMode` key.

Mixed-DPI setups — a scaled laptop panel beside a 100% external monitor — are common
in enterprise fleets. Retrofitting DPI awareness after the mouse-tracing canvas and
pattern renderer exist would mean redoing coordinate maths in every module that
touches screen positions.

The payoff is concrete: `MonitorPatternWindow` places itself with `SetWindowPos` in
**raw device pixels**, so a fullscreen pattern lands correctly on the selected display
regardless of per-monitor scaling. (`SetWindowPos` is blittable, so it uses
`LibraryImport` — which is why App sets `<AllowUnsafeBlocks>true`.)

## Runtime footprint

Written state, all under `%LOCALAPPDATA%\HardwareAuditToolkit\`:

| Path | Written by | Read back? |
|---|---|---|
| `extract\` | single-file host | by the runtime |
| `diagnostics.log` | `FileDiagnosticLog` | by a human |
| `audit-<guid>.hat.json` | `SessionCheckpointStore` | **never** — accumulates forever |

Reports go elsewhere entirely, through the export cascade — app directory first, so a
USB run leaves the report on the stick. See
[`../reporting/export-cascade.md`](../reporting/export-cascade.md).

The checkpoint accumulation is open decision
[D3](../plans/open-decisions.md): one file per launch, never read, never pruned.

## Target framework

`net8.0-windows`, `win-x64`, `Nullable` and `ImplicitUsings` enabled across all four
projects. App additionally sets `UseWPF`, `AllowUnsafeBlocks`, the app manifest and
the icon. Version `0.1.0`.
