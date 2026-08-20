using System.IO;
using System.Text.Json;
using HardwareAuditToolkit.Core;
using Xunit;

namespace HardwareAuditToolkit.Tests;

/// <summary>
/// Phase 7 crash-persistence: <see cref="SessionCheckpointStore"/> must serialize the
/// in-memory <see cref="AuditSession"/> to a deterministic, durable JSON file so a forced
/// termination cannot lose findings collected before an explicit §9.6 export.
/// </summary>
public class SessionCheckpointTests
{
    [Fact]
    public void Save_RoundTripsSessionToDurableJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "HATK_checkpoint_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionCheckpointStore(dir);
            var session = new AuditSession
            {
                SessionId = "abc123",
                Hostname = "ROUNDTRIP",
                StartedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc),
                OverallStatus = TestStatus.Passed,
            };
            session.Modules.Add(new ModuleResult { ModuleId = "keyboard", Status = TestStatus.Passed });

            store.Save(session);

            string file = Path.Combine(dir, "audit-abc123.hat.json");
            Assert.True(File.Exists(file));

            var reloaded = JsonSerializer.Deserialize<AuditSession>(File.ReadAllText(file));
            Assert.NotNull(reloaded);
            Assert.Equal("abc123", reloaded!.SessionId);
            Assert.Equal("ROUNDTRIP", reloaded.Hostname);
            Assert.Single(reloaded.Modules);
            Assert.Equal(TestStatus.Passed, reloaded.OverallStatus);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
