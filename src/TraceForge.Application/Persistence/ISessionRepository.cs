using TraceForge.Application.Models;

namespace TraceForge.Application.Persistence;

public interface ISessionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> StartSessionAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken = default);
    Task SaveSnapshotAsync(long sessionId, ProcessSnapshot snapshot, IReadOnlyList<DiagnosticAnomaly> anomalies, CancellationToken cancellationToken = default);
    Task SaveIncidentAsync(long sessionId, DiagnosticIncident incident, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(int limit = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncidentSummary>> GetRecentIncidentsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task EndSessionAsync(long sessionId, DateTimeOffset endedUtc, CancellationToken cancellationToken = default);
}
