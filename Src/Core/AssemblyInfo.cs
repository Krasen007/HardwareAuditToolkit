using System.Runtime.CompilerServices;

// Phase 7 — fault-injection test friend: lets the Tests assembly exercise internal
// diagnostics seams (the CpuStressModule worker body) without widening the module's
// public surface.
[assembly: InternalsVisibleTo("HardwareAuditToolkit.Tests")]
