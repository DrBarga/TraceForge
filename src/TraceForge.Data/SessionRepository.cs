using Microsoft.Data.Sqlite;
using TraceForge.Application.Models;
using TraceForge.Application.Persistence;

namespace TraceForge.Data;

public sealed class SessionRepository : ISessionRepository
{
    private readonly string _connectionString;

    public SessionRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;

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
                name TEXT NOT NULL,
                path TEXT NOT NULL,
                cpu_percent REAL NOT NULL,
                working_set_bytes INTEGER NOT NULL,
                thread_count INTEGER NOT NULL,
                accessible INTEGER NOT NULL,
                access_error INTEGER NOT NULL,
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

            CREATE INDEX IF NOT EXISTS ix_snapshots_session_timestamp
                ON snapshots(session_id, timestamp_utc);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> StartSessionAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var snapshotCommand = connection.CreateCommand();
        snapshotCommand.Transaction = transaction;
        snapshotCommand.CommandText = "INSERT INTO snapshots(session_id, timestamp_utc, process_count) VALUES ($session, $timestamp, $count); SELECT last_insert_rowid();";
        snapshotCommand.Parameters.AddWithValue("$session", sessionId);
        snapshotCommand.Parameters.AddWithValue("$timestamp", snapshot.TimestampUtc.ToString("O"));
        snapshotCommand.Parameters.AddWithValue("$count", snapshot.Processes.Count);

        var snapshotId = Convert.ToInt64(await snapshotCommand.ExecuteScalarAsync(cancellationToken));

        foreach (var process in snapshot.Processes)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO process_samples(
                    snapshot_id, pid, name, path, cpu_percent,
                    working_set_bytes, thread_count, accessible, access_error)
                VALUES(
                    $snapshot, $pid, $name, $path, $cpu,
                    $memory, $threads, $accessible, $error);
                """;
            command.Parameters.AddWithValue("$snapshot", snapshotId);
            command.Parameters.AddWithValue("$pid", process.Pid);
            command.Parameters.AddWithValue("$name", process.Name);
            command.Parameters.AddWithValue("$path", process.Path);
            command.Parameters.AddWithValue("$cpu", process.CpuPercent);
            command.Parameters.AddWithValue("$memory", checked((long)process.WorkingSetBytes));
            command.Parameters.AddWithValue("$threads", process.ThreadCount);
            command.Parameters.AddWithValue("$accessible", process.Accessible ? 1 : 0);
            command.Parameters.AddWithValue("$error", process.AccessError);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var anomaly in anomalies)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO anomalies(snapshot_id, code, severity, pid, process_name, message)
                VALUES($snapshot, $code, $severity, $pid, $name, $message);
                """;
            command.Parameters.AddWithValue("$snapshot", snapshotId);
            command.Parameters.AddWithValue("$code", anomaly.Code);
            command.Parameters.AddWithValue("$severity", anomaly.Severity);
            command.Parameters.AddWithValue("$pid", anomaly.Pid);
            command.Parameters.AddWithValue("$name", anomaly.ProcessName);
            command.Parameters.AddWithValue("$message", anomaly.Message);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EndSessionAsync(long sessionId, DateTimeOffset endedUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET ended_utc = $ended WHERE id = $id;";
        command.Parameters.AddWithValue("$ended", endedUtc.ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
