using Microsoft.Data.Sqlite;
using TraceForge.Application.Models;
using TraceForge.Data;
using Xunit;

namespace TraceForge.Application.Tests;

public sealed class SessionRepositoryTests
{
    [Fact]
    public async Task InitializeAndSaveSnapshot_PersistsCurrentSchema()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"traceforge-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionRepository(databasePath, TimeSpan.FromDays(7));
            await repository.InitializeAsync();
            var sessionId = await repository.StartSessionAsync(DateTimeOffset.UtcNow);
            var process = new ProcessInfo(
                10,
                4,
                "sample.exe",
                "C:\\sample.exe",
                12.5,
                4096,
                2,
                true,
                0,
                true,
                0,
                true,
                0,
                true,
                0);

            await repository.SaveSnapshotAsync(
                sessionId,
                new ProcessSnapshot(DateTimeOffset.UtcNow, [process]),
                []);
            await repository.EndSessionAsync(sessionId, DateTimeOffset.UtcNow);

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();

            var version = connection.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(4L, Convert.ToInt64(await version.ExecuteScalarAsync()));

            var sample = connection.CreateCommand();
            sample.CommandText = "SELECT parent_pid, path_available, cpu_available, memory_available FROM process_samples LIMIT 1;";
            await using var reader = await sample.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(4L, reader.GetInt64(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(1L, reader.GetInt64(3));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            if (File.Exists(databasePath + "-wal"))
            {
                File.Delete(databasePath + "-wal");
            }

            if (File.Exists(databasePath + "-shm"))
            {
                File.Delete(databasePath + "-shm");
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacyProcessSamplesTable()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"traceforge-legacy-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var legacySchema = connection.CreateCommand();
                legacySchema.CommandText = """
                    CREATE TABLE sessions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        started_utc TEXT NOT NULL,
                        ended_utc TEXT NULL
                    );
                    CREATE TABLE snapshots (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        session_id INTEGER NOT NULL,
                        timestamp_utc TEXT NOT NULL,
                        process_count INTEGER NOT NULL
                    );
                    CREATE TABLE process_samples (
                        snapshot_id INTEGER NOT NULL,
                        pid INTEGER NOT NULL,
                        name TEXT NOT NULL,
                        path TEXT NOT NULL,
                        cpu_percent REAL NOT NULL,
                        working_set_bytes INTEGER NOT NULL,
                        thread_count INTEGER NOT NULL,
                        accessible INTEGER NOT NULL,
                        access_error INTEGER NOT NULL
                    );
                    """;
                await legacySchema.ExecuteNonQueryAsync();
            }

            var repository = new SessionRepository(databasePath);
            await repository.InitializeAsync();

            await using var migrated = new SqliteConnection($"Data Source={databasePath}");
            await migrated.OpenAsync();
            var columns = migrated.CreateCommand();
            columns.CommandText = "PRAGMA table_info(process_samples);";
            var names = new List<string>();
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(1));
            }

            Assert.Contains("parent_pid", names);
            Assert.Contains("path_available", names);
            Assert.Contains("cpu_available", names);
            Assert.Contains("memory_available", names);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public async Task RecentHistory_ReturnsPersistedSessionsAndIncidents()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"traceforge-history-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SessionRepository(databasePath);
            await repository.InitializeAsync();
            var started = DateTimeOffset.UtcNow;
            var sessionId = await repository.StartSessionAsync(started);
            var snapshot = new ProcessSnapshot(started, []);
            await repository.SaveSnapshotAsync(sessionId, snapshot, []);
            await repository.SaveIncidentAsync(
                sessionId,
                new DiagnosticIncident("sample.exe", new ProcessExit(42, 1, started), [snapshot]));

            var sessions = await repository.GetRecentSessionsAsync();
            var incidents = await repository.GetRecentIncidentsAsync();

            var session = Assert.Single(sessions);
            Assert.Equal(sessionId, session.Id);
            Assert.Equal(1, session.SnapshotCount);
            Assert.Equal(1, session.IncidentCount);
            var incident = Assert.Single(incidents);
            Assert.Equal("sample.exe", incident.ProcessName);
            Assert.Equal((uint)42, incident.Pid);
            Assert.Equal((uint)1, incident.ExitCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
