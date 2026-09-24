using Microsoft.Data.Sqlite;
using TraceForge.Application.Models;
using TraceForge.Application.Persistence;

namespace TraceForge.Data;

public sealed class SessionRepository : ISessionRepository
{
    private const int SchemaVersion = 4;

    private readonly string _connectionString;
    private readonly TimeSpan _retention;

    public SessionRepository(string databasePath, TimeSpan? retention = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _retention = retention ?? TimeSpan.FromDays(7);
        if (_retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_utc TEXT NOT NULL,
                ended_utc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                timestamp_utc TEXT NOT NULL,
                process_count INTEGER NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS process_samples (
                snapshot_id INTEGER NOT NULL,
                pid INTEGER NOT NULL,
                parent_pid INTEGER NOT NULL DEFAULT 0,
                start_time_unix_ms INTEGER NOT NULL DEFAULT 0,
                name TEXT NOT NULL,
                path TEXT NOT NULL,
                cpu_percent REAL NOT NULL,
                working_set_bytes INTEGER NOT NULL,
                thread_count INTEGER NOT NULL,
                accessible INTEGER NOT NULL,
                access_error INTEGER NOT NULL,
                path_available INTEGER NOT NULL DEFAULT 0,
                path_error INTEGER NOT NULL DEFAULT 0,
                cpu_available INTEGER NOT NULL DEFAULT 0,
                cpu_error INTEGER NOT NULL DEFAULT 0,
                memory_available INTEGER NOT NULL DEFAULT 0,
                memory_error INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(snapshot_id) REFERENCES snapshots(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS anomalies (
                snapshot_id INTEGER NOT NULL,
                code TEXT NOT NULL,
                severity TEXT NOT NULL,
                pid INTEGER NOT NULL,
                process_name TEXT NOT NULL,
                message TEXT NOT NULL,
                FOREIGN KEY(snapshot_id) REFERENCES snapshots(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS incidents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                pid INTEGER NOT NULL,
                process_name TEXT NOT NULL,
                exit_code INTEGER NOT NULL,
                observed_utc TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_snapshots_session_timestamp
                ON snapshots(session_id, timestamp_utc);

            CREATE INDEX IF NOT EXISTS ix_process_samples_snapshot
                ON process_samples(snapshot_id);

            CREATE INDEX IF NOT EXISTS ix_anomalies_snapshot
                ON anomalies(snapshot_id);

            CREATE INDEX IF NOT EXISTS ix_incidents_session_observed
                ON incidents(session_id, observed_utc);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "process_samples", "parent_pid", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "start_time_unix_ms", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "path_available", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "path_error", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "cpu_available", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "cpu_error", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "memory_available", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "process_samples", "memory_error", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = $"PRAGMA user_version = {SchemaVersion};";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken);

        await DeleteExpiredSessionsAsync(connection, DateTimeOffset.UtcNow - _retention, cancellationToken);
    }

    public async Task<long> StartSessionAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sessions(started_utc) VALUES ($started); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$started", startedUtc.ToString("O"));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result);
    }

    public async Task SaveSnapshotAsync(
        long sessionId,
        ProcessSnapshot snapshot,
        IReadOnlyList<DiagnosticAnomaly> anomalies,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var snapshotCommand = connection.CreateCommand();
        snapshotCommand.Transaction = transaction;
        snapshotCommand.CommandText = "INSERT INTO snapshots(session_id, timestamp_utc, process_count) VALUES ($session, $timestamp, $count); SELECT last_insert_rowid();";
        snapshotCommand.Parameters.AddWithValue("$session", sessionId);
        snapshotCommand.Parameters.AddWithValue("$timestamp", snapshot.TimestampUtc.ToString("O"));
        snapshotCommand.Parameters.AddWithValue("$count", snapshot.Processes.Count);

        var snapshotId = Convert.ToInt64(await snapshotCommand.ExecuteScalarAsync(cancellationToken));

        var processCommand = connection.CreateCommand();
        processCommand.Transaction = transaction;
        processCommand.CommandText = """
            INSERT INTO process_samples(
                snapshot_id, pid, parent_pid, start_time_unix_ms, name, path, cpu_percent,
                working_set_bytes, thread_count, accessible, access_error,
                path_available, path_error, cpu_available, cpu_error,
                memory_available, memory_error)
            VALUES(
                $snapshot, $pid, $parentPid, $startTime, $name, $path, $cpu,
                $memory, $threads, $accessible, $accessError,
                $pathAvailable, $pathError, $cpuAvailable, $cpuError,
                $memoryAvailable, $memoryError);
            """;
        processCommand.Parameters.Add("$snapshot", SqliteType.Integer);
        processCommand.Parameters.Add("$pid", SqliteType.Integer);
        processCommand.Parameters.Add("$parentPid", SqliteType.Integer);
        processCommand.Parameters.Add("$startTime", SqliteType.Integer);
        processCommand.Parameters.Add("$name", SqliteType.Text);
        processCommand.Parameters.Add("$path", SqliteType.Text);
        processCommand.Parameters.Add("$cpu", SqliteType.Real);
        processCommand.Parameters.Add("$memory", SqliteType.Integer);
        processCommand.Parameters.Add("$threads", SqliteType.Integer);
        processCommand.Parameters.Add("$accessible", SqliteType.Integer);
        processCommand.Parameters.Add("$accessError", SqliteType.Integer);
        processCommand.Parameters.Add("$pathAvailable", SqliteType.Integer);
        processCommand.Parameters.Add("$pathError", SqliteType.Integer);
        processCommand.Parameters.Add("$cpuAvailable", SqliteType.Integer);
        processCommand.Parameters.Add("$cpuError", SqliteType.Integer);
        processCommand.Parameters.Add("$memoryAvailable", SqliteType.Integer);
        processCommand.Parameters.Add("$memoryError", SqliteType.Integer);

        foreach (var process in snapshot.Processes)
        {
            processCommand.Parameters["$snapshot"].Value = snapshotId;
            processCommand.Parameters["$pid"].Value = process.Pid;
            processCommand.Parameters["$parentPid"].Value = process.ParentPid;
            processCommand.Parameters["$startTime"].Value = process.StartTimeUnixMs;
            processCommand.Parameters["$name"].Value = process.Name;
            processCommand.Parameters["$path"].Value = process.Path;
            processCommand.Parameters["$cpu"].Value = process.CpuPercent;
            processCommand.Parameters["$memory"].Value = checked((long)process.WorkingSetBytes);
            processCommand.Parameters["$threads"].Value = process.ThreadCount;
            processCommand.Parameters["$accessible"].Value = process.Accessible ? 1 : 0;
            processCommand.Parameters["$accessError"].Value = process.AccessError;
            processCommand.Parameters["$pathAvailable"].Value = process.PathAvailable ? 1 : 0;
            processCommand.Parameters["$pathError"].Value = process.PathError;
            processCommand.Parameters["$cpuAvailable"].Value = process.CpuAvailable ? 1 : 0;
            processCommand.Parameters["$cpuError"].Value = process.CpuError;
            processCommand.Parameters["$memoryAvailable"].Value = process.MemoryAvailable ? 1 : 0;
            processCommand.Parameters["$memoryError"].Value = process.MemoryError;
            await processCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var anomalyCommand = connection.CreateCommand();
        anomalyCommand.Transaction = transaction;
        anomalyCommand.CommandText = """
            INSERT INTO anomalies(snapshot_id, code, severity, pid, process_name, message)
            VALUES($snapshot, $code, $severity, $pid, $name, $message);
            """;
        anomalyCommand.Parameters.Add("$snapshot", SqliteType.Integer);
        anomalyCommand.Parameters.Add("$code", SqliteType.Text);
        anomalyCommand.Parameters.Add("$severity", SqliteType.Text);
        anomalyCommand.Parameters.Add("$pid", SqliteType.Integer);
        anomalyCommand.Parameters.Add("$name", SqliteType.Text);
        anomalyCommand.Parameters.Add("$message", SqliteType.Text);

        foreach (var anomaly in anomalies)
        {
            anomalyCommand.Parameters["$snapshot"].Value = snapshotId;
            anomalyCommand.Parameters["$code"].Value = anomaly.Code;
            anomalyCommand.Parameters["$severity"].Value = anomaly.Severity;
            anomalyCommand.Parameters["$pid"].Value = anomaly.Pid;
            anomalyCommand.Parameters["$name"].Value = anomaly.ProcessName;
            anomalyCommand.Parameters["$message"].Value = anomaly.Message;
            await anomalyCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EndSessionAsync(long sessionId, DateTimeOffset endedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET ended_utc = $ended WHERE id = $id;";
        command.Parameters.AddWithValue("$ended", endedUtc.ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveIncidentAsync(
        long sessionId,
        DiagnosticIncident incident,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO incidents(session_id, pid, process_name, exit_code, observed_utc)
            VALUES($session, $pid, $name, $exitCode, $observed);
            """;
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$pid", incident.Exit.Pid);
        command.Parameters.AddWithValue("$name", incident.ProcessName);
        command.Parameters.AddWithValue("$exitCode", incident.Exit.ExitCode);
        command.Parameters.AddWithValue("$observed", incident.Exit.TimestampUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.started_utc, s.ended_utc,
                   (SELECT COUNT(*) FROM snapshots WHERE session_id = s.id),
                   (SELECT COUNT(*) FROM incidents WHERE session_id = s.id)
            FROM sessions AS s
            ORDER BY s.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var sessions = new List<SessionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new SessionSummary(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return sessions;
    }

    public async Task<IReadOnlyList<IncidentSummary>> GetRecentIncidentsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_id, pid, process_name, exit_code, observed_utc
            FROM incidents
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var incidents = new List<IncidentSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            incidents.Add(new IncidentSummary(
                reader.GetInt64(0),
                checked((uint)reader.GetInt64(1)),
                reader.GetString(2),
                checked((uint)reader.GetInt64(3)),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return incidents;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var query = connection.CreateCommand();
        query.CommandText = $"PRAGMA table_info({table});";

        var exists = false;
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        var migration = connection.CreateCommand();
        migration.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await migration.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteExpiredSessionsAsync(
        SqliteConnection connection,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var delete = connection.CreateCommand();
        delete.CommandText = """
            DELETE FROM sessions
            WHERE COALESCE(ended_utc, started_utc) < $cutoff;
            """;
        delete.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
        await delete.ExecuteNonQueryAsync(cancellationToken);

        var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        await checkpoint.ExecuteNonQueryAsync(cancellationToken);
    }
}
