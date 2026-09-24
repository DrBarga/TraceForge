using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TraceForge.Application.Diagnostics;
using TraceForge.Application.Ipc;
using TraceForge.Application.Models;
using TraceForge.Application.Persistence;
using TraceForge.Application.Reporting;

namespace TraceForge.App.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly AgentClient _agentClient;
    private readonly ISessionRepository _sessionRepository;
    private readonly DiagnosticAnalyzer _analyzer;
    private readonly BlackBoxBuffer _blackBox;
    private readonly DiagnosticReportBuilder _reportBuilder;
    private readonly TimeSpan _persistenceInterval;
    private readonly List<ProcessInfo> _allProcesses = [];
    private readonly Dictionary<(uint Pid, long Started), ProcessRowViewModel> _rowCache = [];

    private string _searchText = string.Empty;
    private ProcessRowViewModel? _selectedProcess;
    private string _status = "Starting";
    private string _lastUpdated = "Never";
    private IReadOnlyList<DiagnosticAnomaly> _anomalies = [];
    private ProcessSnapshot? _currentSnapshot;
    private DateTimeOffset? _lastPersistedUtc;
    private uint? _watchedPid;
    private long _watchedStartTimeUnixMs;
    private string _watchedName = string.Empty;
    private DiagnosticIncident? _lastIncident;
    private ProcessInspection? _inspection;
    private bool _updatingProcessList;
    private string _storageWarning = string.Empty;
    private readonly Queue<DiagnosticIncident> _pendingIncidents = new();

    public DashboardViewModel(
        AgentClient agentClient,
        ISessionRepository sessionRepository,
        DiagnosticAnalyzer analyzer,
        BlackBoxBuffer blackBox,
        DiagnosticReportBuilder reportBuilder,
        TimeSpan? persistenceInterval = null)
    {
        _agentClient = agentClient;
        _sessionRepository = sessionRepository;
        _analyzer = analyzer;
        _blackBox = blackBox;
        _reportBuilder = reportBuilder;
        _persistenceInterval = persistenceInterval ?? TimeSpan.FromSeconds(30);
        if (_persistenceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(persistenceInterval));
        }
    }

    public ObservableCollection<ProcessRowViewModel> Processes { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value;
            ApplyFilter();
            OnPropertyChanged();
        }
    }

    public ProcessRowViewModel? SelectedProcess
    {
        get => _selectedProcess;
        private set
        {
            _selectedProcess = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedPid));
            OnPropertyChanged(nameof(SelectedParentPid));
            OnPropertyChanged(nameof(SelectedStartTime));
            OnPropertyChanged(nameof(SelectedPath));
            OnPropertyChanged(nameof(SelectedCpu));
            OnPropertyChanged(nameof(SelectedMemory));
            OnPropertyChanged(nameof(SelectedThreads));
            OnPropertyChanged(nameof(SelectedAccess));
            OnPropertyChanged(nameof(SelectedChildCount));
            OnPropertyChanged(nameof(HasSelectedProcess));
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public string StorageWarning
    {
        get => _storageWarning;
        private set => SetField(ref _storageWarning, value);
    }

    public int ProcessCount => _currentSnapshot?.Processes.Count ?? 0;
    public int LimitedProcessCount => _currentSnapshot?.Processes.Count(process =>
        !process.Accessible || !process.PathAvailable || !process.MemoryAvailable) ?? 0;
    public int AnomalyCount => _anomalies.Count;
    public string SelectedName => SelectedProcess?.Name ?? "No process selected";
    public string SelectedPid => SelectedProcess?.Pid ?? "-";
    public string SelectedParentPid => SelectedProcess?.ParentPid ?? "-";
    public string SelectedStartTime => SelectedProcess?.Process.StartTimeUnixMs is > 0 and var started
        ? DateTimeOffset.FromUnixTimeMilliseconds(started).ToLocalTime().ToString("g")
        : "Unavailable";
    public string SelectedPath => string.IsNullOrWhiteSpace(SelectedProcess?.Path) ? "Unavailable" : SelectedProcess.Path;
    public string SelectedCpu => SelectedProcess?.Cpu ?? "-";
    public string SelectedMemory => SelectedProcess?.Memory ?? "-";
    public string SelectedThreads => SelectedProcess?.Threads ?? "-";
    public string SelectedAccess => SelectedProcess?.Access ?? "-";
    public bool HasSelectedProcess => SelectedProcess is not null;
    public int SelectedChildCount => SelectedProcess is null
        ? 0
        : _allProcesses.Count(process => process.ParentPid == SelectedProcess.Process.Pid);
    public ObservableCollection<string> ThreadRows { get; } = [];
    public ObservableCollection<string> ModuleRows { get; } = [];
    public ObservableCollection<string> ConnectionRows { get; } = [];
    public string InspectionSummary => _inspection is null
        ? "Select a process and run Inspect"
        : $"{_inspection.Threads.Count} threads · {_inspection.Modules.Count} modules · {_inspection.Connections.Count} endpoints";
    public string InspectionWarnings => _inspection is null
        ? string.Empty
        : string.Join(" · ", new[]
        {
            _inspection.ThreadError == 0 ? null : $"Threads: Win32 {_inspection.ThreadError}",
            _inspection.ModuleError == 0 ? null : $"Modules: Win32 {_inspection.ModuleError}",
            _inspection.NetworkError == 0 ? null : $"Network: Win32 {_inspection.NetworkError}",
            _inspection.IdentityError == 0 ? null : $"Identity: Win32 {_inspection.IdentityError}"
        }.Where(value => value is not null));
    public bool IsWatching => _watchedPid.HasValue;
    public string WatchedProcessStatus => _watchedPid.HasValue
        ? $"Watching {_watchedName} (PID {_watchedPid.Value})"
        : "No process is being watched";
    public DiagnosticIncident? LastIncident => _lastIncident;
    public string LastIncidentSummary => _lastIncident is null
        ? "No process exit has been recorded"
        : $"{_lastIncident.ProcessName} (PID {_lastIncident.Exit.Pid}) exited with 0x{_lastIncident.Exit.ExitCode:X8} at {_lastIncident.Exit.TimestampUtc.ToLocalTime():HH:mm:ss}";

    public async Task RefreshAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        Status = "Refreshing";
        var snapshot = await _agentClient.GetSnapshotAsync(cancellationToken);
        var anomalies = _analyzer.Analyze(snapshot);

        _currentSnapshot = snapshot;
        _anomalies = anomalies;
        _blackBox.Add(snapshot);

        if (snapshot.ProcessExit is { } processExit && processExit.Pid == _watchedPid)
        {
            _lastIncident = new DiagnosticIncident(
                _watchedName,
                processExit,
                _blackBox.Snapshot(),
                _watchedStartTimeUnixMs);
            _pendingIncidents.Enqueue(_lastIncident);
            _watchedPid = null;
            _watchedStartTimeUnixMs = 0;
            _watchedName = string.Empty;
            OnPropertyChanged(nameof(LastIncident));
            OnPropertyChanged(nameof(LastIncidentSummary));
            OnPropertyChanged(nameof(IsWatching));
            OnPropertyChanged(nameof(WatchedProcessStatus));
        }

        _allProcesses.Clear();
        _allProcesses.AddRange(snapshot.Processes);
        ApplyFilter();

        if (_lastPersistedUtc is null ||
            snapshot.TimestampUtc - _lastPersistedUtc.Value >= _persistenceInterval)
        {
            try
            {
                await _sessionRepository.SaveSnapshotAsync(sessionId, snapshot, anomalies, cancellationToken);
                _lastPersistedUtc = snapshot.TimestampUtc;
                if (_pendingIncidents.Count == 0)
                {
                    StorageWarning = string.Empty;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                StorageWarning = $"Session history could not be saved: {exception.Message}";
            }
        }

        while (_pendingIncidents.TryPeek(out var pendingIncident))
        {
            try
            {
                await _sessionRepository.SaveIncidentAsync(sessionId, pendingIncident, cancellationToken);
                _pendingIncidents.Dequeue();
                if (_pendingIncidents.Count == 0)
                {
                    StorageWarning = string.Empty;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                StorageWarning = $"Incident history could not be saved: {exception.Message}";
                break;
            }
        }

        LastUpdated = snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        Status = "Connected";
        OnPropertyChanged(nameof(ProcessCount));
        OnPropertyChanged(nameof(LimitedProcessCount));
        OnPropertyChanged(nameof(AnomalyCount));
    }

    public void Select(ProcessRowViewModel? process)
    {
        if (_updatingProcessList)
        {
            return;
        }

        if (SelectedProcess?.Process.Pid != process?.Process.Pid ||
            SelectedProcess?.Process.StartTimeUnixMs != process?.Process.StartTimeUnixMs)
        {
            ClearInspection();
        }

        SelectedProcess = process;
    }

    public async Task InspectSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedProcess?.Process
            ?? throw new InvalidOperationException("Select a process before inspecting it.");
        var inspection = await _agentClient.InspectProcessAsync(selected.Pid, cancellationToken);
        if (selected.StartTimeUnixMs != 0 && inspection.StartTimeUnixMs != 0 &&
            selected.StartTimeUnixMs != inspection.StartTimeUnixMs)
        {
            throw new InvalidDataException("The selected PID now belongs to a different process. Refresh and select it again.");
        }

        if (SelectedProcess?.Process.Pid != selected.Pid ||
            SelectedProcess.Process.StartTimeUnixMs != selected.StartTimeUnixMs)
        {
            return;
        }

        _inspection = inspection;
        ReplaceRows(ThreadRows, inspection.Threads.Select(thread =>
            $"TID {thread.Id} · base priority {thread.BasePriority}"));
        ReplaceRows(ModuleRows, inspection.Modules.Select(module =>
            $"{module.Name} · {module.SizeBytes / 1024d:F0} KiB\n{module.Path}"));
        ReplaceRows(ConnectionRows, inspection.Connections.Select(connection =>
            $"{connection.Protocol}  {connection.LocalAddress}:{connection.LocalPort}  →  " +
            (string.IsNullOrEmpty(connection.RemoteAddress)
                ? connection.State
                : $"{connection.RemoteAddress}:{connection.RemotePort}  {connection.State}")));
        OnPropertyChanged(nameof(InspectionSummary));
        OnPropertyChanged(nameof(InspectionWarnings));
    }

    public async Task WatchSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = SelectedProcess?.Process
            ?? throw new InvalidOperationException("Select a process before starting a watch.");

        await WatchProcessAsync(selected.Pid, selected.Name, selected.StartTimeUnixMs, cancellationToken);
    }

    public async Task WatchProcessAsync(
        uint processId,
        string processName,
        long startTimeUnixMs = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        await _agentClient.WatchProcessAsync(processId, cancellationToken);
        _watchedPid = processId;
        _watchedStartTimeUnixMs = startTimeUnixMs;
        _watchedName = processName;
        OnPropertyChanged(nameof(IsWatching));
        OnPropertyChanged(nameof(WatchedProcessStatus));
    }

    public async Task StopWatchingAsync(CancellationToken cancellationToken = default)
    {
        await _agentClient.StopWatchingAsync(cancellationToken);
        ClearWatch();
    }

    public async Task RestoreWatchAsync(CancellationToken cancellationToken = default)
    {
        if (_watchedPid is { } processId)
        {
            try
            {
                var snapshot = await _agentClient.GetSnapshotAsync(cancellationToken);
                if (_watchedStartTimeUnixMs == 0 ||
                    !snapshot.Processes.Any(process =>
                        process.Pid == processId &&
                        process.StartTimeUnixMs == _watchedStartTimeUnixMs))
                {
                    ClearWatch();
                    return;
                }

                await _agentClient.WatchProcessAsync(processId, cancellationToken);
            }
            catch
            {
                ClearWatch();
                throw;
            }
        }
    }

    private void ClearWatch()
    {
        _watchedPid = null;
        _watchedStartTimeUnixMs = 0;
        _watchedName = string.Empty;
        OnPropertyChanged(nameof(IsWatching));
        OnPropertyChanged(nameof(WatchedProcessStatus));
    }

    public string BuildReport(string version)
    {
        if (_currentSnapshot is null)
        {
            throw new InvalidOperationException("No process snapshot is available yet.");
        }

        return _reportBuilder.Build(
            _currentSnapshot,
            _blackBox.Snapshot(),
            _anomalies,
            version,
            _lastIncident,
            _inspection);
    }

    public string BuildHtmlReport(string version)
    {
        if (_currentSnapshot is null)
        {
            throw new InvalidOperationException("No process snapshot is available yet.");
        }

        return _reportBuilder.BuildHtml(
            _currentSnapshot,
            _blackBox.Snapshot(),
            _anomalies,
            version,
            _lastIncident,
            _inspection);
    }

    public Task<string> CaptureSelectedDumpAsync(CancellationToken cancellationToken = default)
    {
        var processId = SelectedProcess?.Process.Pid
            ?? throw new InvalidOperationException("Select a process before capturing a dump.");
        return _agentClient.CaptureDumpAsync(processId, cancellationToken);
    }

    private void ApplyFilter()
    {
        var selectedPid = SelectedProcess?.Process.Pid;
        var selectedStartTime = SelectedProcess?.Process.StartTimeUnixMs;
        var query = _searchText.Trim();

        var activeKeys = _allProcesses
            .Select(process => (process.Pid, process.StartTimeUnixMs))
            .ToHashSet();
        foreach (var key in _rowCache.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
        {
            _rowCache.Remove(key);
        }

        var filtered = _allProcesses
            .Where(process => string.IsNullOrEmpty(query) ||
                              process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              process.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              process.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(process => process.Pid)
            .Select(process =>
            {
                var key = (process.Pid, process.StartTimeUnixMs);
                if (!_rowCache.TryGetValue(key, out var row))
                {
                    row = new ProcessRowViewModel(process);
                    _rowCache.Add(key, row);
                }
                else
                {
                    row.Update(process);
                }

                return row;
            })
            .ToArray();

        _updatingProcessList = true;
        try
        {
            for (var index = 0; index < filtered.Length; index++)
            {
                if (index < Processes.Count && ReferenceEquals(Processes[index], filtered[index]))
                {
                    continue;
                }

                var existingIndex = Processes.IndexOf(filtered[index]);
                if (existingIndex >= 0)
                {
                    Processes.Move(existingIndex, index);
                }
                else
                {
                    Processes.Insert(index, filtered[index]);
                }
            }

            while (Processes.Count > filtered.Length)
            {
                Processes.RemoveAt(Processes.Count - 1);
            }
        }
        finally
        {
            _updatingProcessList = false;
        }

        SelectedProcess = selectedPid is null
            ? SelectedProcess
            : Processes.FirstOrDefault(process =>
                process.Process.Pid == selectedPid.Value &&
                process.Process.StartTimeUnixMs == selectedStartTime);
        OnPropertyChanged(nameof(SelectedChildCount));
    }

    private static void ReplaceRows(ObservableCollection<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (var row in source)
        {
            target.Add(row);
        }
    }

    private void ClearInspection()
    {
        _inspection = null;
        ThreadRows.Clear();
        ModuleRows.Clear();
        ConnectionRows.Clear();
        OnPropertyChanged(nameof(InspectionSummary));
        OnPropertyChanged(nameof(InspectionWarnings));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
