# Hardware Audit Toolkit

Portable sysadmin hardware audit tool — v1 scope: keyboard, mouse, monitor,
system info, and CPU stress testing. Portable, offline-first, no admin
required, no database. See `hardware-audit-toolkit-architecture.md` for the
full architecture and phase plan.

## Solution layout

```
Src/
  HardwareAuditToolkit.sln
  Core/            # contracts (ITestModule, TestStatus), session models, TestOrchestrator — no UI/Win32 refs
  Infrastructure/  # Win32 wrappers, WMI/CIM, LibreHardwareMonitor sensor adapter
  App/             # WPF host: DI shell, single-instance enforcer, app manifest, publish profiles
  Tests/           # xunit
docs/
  DeploymentNote.md  # §9.1 one-pager for security teams (hash, extraction path, signing)
```

## Build & test

```powershell
dotnet build Src\HardwareAuditToolkit.sln
dotnet test  Src\HardwareAuditToolkit.sln
```

## Publish

```powershell
# Primary: portable, self-contained, single-file .exe (§9.1)
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableSingleFile
# Fallback: self-contained folder build
dotnet publish Src\App\HardwareAuditToolkit.App.csproj -c Release -p:PublishProfile=PortableFolder
```

Outputs land in `Src\App\bin\publish\`.

## Phase status

- **Phase 0 (scaffolding) — done:** solution, contracts in Core, DI shell,
  Per-Monitor V2 manifest, `Global\` single-instance enforcement, publish
  profiles, orchestrator unit tests.
- Phase 1+ — see the architecture doc.
