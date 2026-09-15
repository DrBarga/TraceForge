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
    private readonly List<ProcessInfo> _allProcesses = [];

    private string _searchText = string.Empty;
    private ProcessRowViewModel? _selectedProcess;
    private string _status = "Starting";
    private string _lastUpdated = "Never";
    private IReadOnlyList<DiagnosticAnomaly> _anomalies = [];
    private ProcessSnapshot? _currentSnapshot;

    public DashboardViewModel(
        AgentClient agentClient,
        ISessionRepository sessionRepository,
        DiagnosticAnalyzer analyzer,
        BlackBoxBuffer blackBox,
        DiagnosticReportBuilder reportBuilder)
    {
        _agentClient = agentClient;
        _sessionRepository = sessionRepository;
        _analyzer = analyzer;
        _blackBox = blackBox;
        _reportBuilder = reportBuilder;
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
            OnPropertyChanged(nameof(SelectedPath));
            OnPropertyChanged(nameof(SelectedCpu));
            OnPropertyChanged(nameof(SelectedMemory));
            OnPropertyChanged(nameof(SelectedThreads));
            OnPropertyChanged(nameof(SelectedAccess));
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

    public int ProcessCount => _currentSnapshot?.Processes.Count ?? 0;
    public int AnomalyCount => _anomalies.Count;
    public string SelectedName => SelectedProcess?.Name ?? "No process selected";
    public string SelectedPid => SelectedProcess?.Pid ?? "-";
    public string SelectedPath => string.IsNullOrWhiteSpace(SelectedProcess?.Path) ? "Unavailable" : SelectedProcess.Path;
    public string SelectedCpu => SelectedProcess?.Cpu ?? "-";
    public string SelectedMemory => SelectedProcess?.Memory ?? "-";
    public string SelectedThreads => SelectedProcess?.Threads ?? "-";
    public string SelectedAccess => SelectedProcess?.Access ?? "-";

    public async Task RefreshAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        Status = "Refreshing";
        var snapshot = await _agentClient.GetSnapshotAsync(cancellationToken);
        var anomalies = _analyzer.Analyze(snapshot);

        _currentSnapshot = snapshot;
        _anomalies = anomalies;
        _blackBox.Add(snapshot);

        _allProcesses.Clear();
        _allProcesses.AddRange(snapshot.Processes);
        ApplyFilter();

        await _sessionRepository.SaveSnapshotAsync(sessionId, snapshot, anomalies, cancellationToken);

        LastUpdated = snapshot.TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        Status = "Connected";
        OnPropertyChanged(nameof(ProcessCount));
        OnPropertyChanged(nameof(AnomalyCount));
    }

    public void Select(ProcessRowViewModel? process)
    {
        SelectedProcess = process;
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
            version);
    }

    private void ApplyFilter()
    {
        var selectedPid = SelectedProcess?.Process.Pid;
        var query = _searchText.Trim();

        var filtered = _allProcesses
            .Where(process => string.IsNullOrEmpty(query) ||
                              process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              process.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              process.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(process => process.CpuPercent)
            .ThenByDescending(process => process.WorkingSetBytes)
            .Select(process => new ProcessRowViewModel(process))
            .ToArray();

        Processes.Clear();
        foreach (var process in filtered)
        {
            Processes.Add(process);
        }

        SelectedProcess = selectedPid is null
            ? SelectedProcess
            : Processes.FirstOrDefault(process => process.Process.Pid == selectedPid.Value);
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
