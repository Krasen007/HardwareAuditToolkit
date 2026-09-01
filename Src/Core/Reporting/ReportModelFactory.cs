namespace HardwareAuditToolkit.Core.Reporting;

/// <summary>
/// Builds a <see cref="ReportModel"/> from the in-memory <see cref="AuditSession"/> plus
/// the full module roster. The roster matters: the session only records modules that were
/// <em>started</em>, so without it an untouched device silently vanishes from the audit.
/// By merging the roster into the model (adding an explicit "Not run" entry for any module
/// that was never started) a partial audit can never read as a clean pass.
/// </summary>
public static class ReportModelFactory
{
    /// <summary>
    /// Builds the report model. <paramref name="modules"/> is the orchestrator's module
    /// roster (<see cref="TestOrchestrator.Modules"/>); when null it falls back to the
    /// modules already present in the session (which omits never-started devices).
    /// <paramref name="tz"/> controls the local-time display strings and defaults to the
    /// technician's machine time zone; callers that need deterministic output (golden
    /// files) pass <see cref="TimeZoneInfo.Utc"/>.
    /// </summary>
    public static ReportModel Build(
        AuditSession session,
        IEnumerable<ITestModule>? modules = null,
        TimeZoneInfo? tz = null)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        tz ??= TimeZoneInfo.Local;

        var roster = (modules ?? Enumerable.Empty<ITestModule>())
            .Where(m => !string.IsNullOrWhiteSpace(m.ModuleId))
            .Select(m => (Id: m.ModuleId, Display: m.Metadata.DisplayName))
            .ToList();

        var results = new List<ReportModuleResult>();
        if (roster.Count > 0)
        {
            foreach (var (id, display) in roster)
            {
                var matches = session.Modules.Where(m => string.Equals(m.ModuleId, id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 0)
                {
                    results.Add(new ReportModuleResult
                    {
                        ModuleId = id,
                        DisplayName = display,
                        Status = StatusDisplay.Name(TestStatus.NotRun),
                    });
                }
                else
                {
                    results.AddRange(matches.Select(ToReport));
                }
            }
        }
        else
        {
            results.AddRange(session.Modules.Select(ToReport));
        }

        var counts = Count(results);
        return new ReportModel
        {
            SchemaVersion = 1,
            Hostname = session.Hostname,
            SessionId = session.SessionId,
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt,
            MachineId = session.MachineId,
            OverallStatus = OverallStatusLabel(counts),
            Counts = counts,
            ReportPath = session.ReportPath,
            JsonPath = session.JsonPath,
            Modules = results,
            StartedAtLocal = FormatLocal(session.StartedAt, tz),
            CompletedAtLocal = session.CompletedAt is { } c ? FormatLocal(c, tz) : null,
        };
    }
private static ReportModuleResult ToReport(ModuleResult m)
    {
        return new ReportModuleResult
        {
            ModuleId = m.ModuleId,
            DisplayName = m.DisplayName,
            Status = StatusDisplay.Name(m.Status),
            StartedAt = m.StartedAt,
            CompletedAt = m.CompletedAt,
            Findings = [.. m.Findings],
            OperatorActions = [.. m.OperatorActions],
            Measurements = m.Measurements
                .Select(mm => new ReportMeasurement
                {
                    Timestamp = mm.Timestamp,
                    Label = mm.Label,
                    Value = mm.Value,
                })
                .ToList(),
        };
    }

    private static ReportStatusCounts Count(IReadOnlyList<ReportModuleResult> results)
    {
        return new ReportStatusCounts
        {
            Passed = results.Count(r => r.Status == StatusDisplay.Name(TestStatus.Passed)),
            Failed = results.Count(r => r.Status == StatusDisplay.Name(TestStatus.Failed)),
            Warning = results.Count(r => r.Status == StatusDisplay.Name(TestStatus.Warning)),
            Cancelled = results.Count(r => r.Status == StatusDisplay.Name(TestStatus.Cancelled)),
            Running = results.Count(r => r.Status == StatusDisplay.Name(TestStatus.Running)),
            NotRun = results.Count(r => r.Status == StatusDisplay.Name(TestStatus.NotRun)),
            Total = results.Count,
        };
    }

    /// <summary>
    /// Decides the overall status from the full roster so an incomplete audit can never
    /// read as a pass. Precedence mirrors the orchestrator (Failed → Warning → Cancelled)
    /// but is computed against <em>all</em> modules including the ones never started.
    /// </summary>
    private static string OverallStatusLabel(ReportStatusCounts c)
    {
        if (c.Total == 0 || c.NotRun == c.Total)
        {
            return StatusDisplay.Name(TestStatus.NotRun);
        }

        if (c.Failed > 0)
        {
            return StatusDisplay.Name(TestStatus.Failed);
        }

        if (c.Warning > 0)
        {
            return StatusDisplay.Name(TestStatus.Warning);
        }

        if (c.Cancelled > 0)
        {
            return StatusDisplay.Name(TestStatus.Cancelled);
        }

        if (c.Running > 0)
        {
            return "In progress";
        }

        if (c.NotRun > 0)
        {
            return "Partial — not all modules were tested";
        }

        return StatusDisplay.Name(TestStatus.Passed);
    }

    private static string FormatLocal(DateTime value, TimeZoneInfo tz)
    {
        var local = value.Kind switch
        {
            DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(value, tz),
            DateTimeKind.Local => value,
            _ => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), tz),
        };

        return local.ToString("yyyy-MM-dd HH:mm:ss");
    }
}