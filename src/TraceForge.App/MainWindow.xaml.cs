using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TraceForge.App.Services;
using TraceForge.App.ViewModels;
using TraceForge.Application.Diagnostics;
using TraceForge.Application.Ipc;
using TraceForge.Application.Reporting;
using TraceForge.Data;

namespace TraceForge.App;

public sealed partial class MainWindow : Window
{
    private const string Version = "1.0.0-rc1";

    private readonly AgentClient _agentClient = new();
    private readonly AgentProcessHost _agentHost = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SessionRepository _sessionRepository;

    private long _sessionId;
    private bool _refreshInProgress;
    private bool _initialized;

    public MainWindow()
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraceForge");

        Directory.CreateDirectory(dataRoot);
        _sessionRepository = new SessionRepository(Path.Combine(dataRoot, "traceforge.db"));

        ViewModel = new DashboardViewModel(
            _agentClient,
            _sessionRepository,
            new DiagnosticAnalyzer(),
            new BlackBoxBuffer(TimeSpan.FromSeconds(60)),
            new DiagnosticReportBuilder());

        InitializeComponent();
        _timer.Tick += Timer_Tick;
        Closed += MainWindow_Closed;
    }

    public DashboardViewModel ViewModel { get; }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            await _sessionRepository.InitializeAsync();
            _sessionId = await _sessionRepository.StartSessionAsync(DateTimeOffset.UtcNow);
            _agentHost.Start();
            await _agentClient.ConnectAsync(TimeSpan.FromSeconds(5));
            await _agentClient.PingAsync();
            await RefreshAsync();
            _timer.Start();
        }
        catch (Exception exception)
        {
            ShowError("TraceForge startup failed", exception.Message);
        }
    }

    private async void Timer_Tick(object? sender, object e)
    {
        await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void LiveToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (LiveToggle.IsOn)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchText = SearchBox.Text;
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.Select(ProcessList.SelectedItem as ProcessRowViewModel);
    }

    private async void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = ViewModel.BuildReport(Version);
            var reportDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraceForge",
                "Reports");
            Directory.CreateDirectory(reportDirectory);

            var reportPath = Path.Combine(
                reportDirectory,
                $"TraceForge-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");

            await File.WriteAllTextAsync(reportPath, report);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "Diagnostic report exported";
            StatusInfoBar.Message = reportPath;
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            ShowError("Report export failed", exception.Message);
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress || _sessionId == 0)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            await ViewModel.RefreshAsync(_sessionId);
        }
        catch (Exception exception)
        {
            _timer.Stop();
            ShowError("Agent communication failed", exception.Message);
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _timer.Stop();

        try
        {
            if (_sessionId != 0)
            {
                await _sessionRepository.EndSessionAsync(_sessionId, DateTimeOffset.UtcNow);
            }

            await _agentClient.ShutdownAsync();
            await _agentClient.DisposeAsync();
        }
        catch
        {
        }
        finally
        {
            _agentHost.Dispose();
        }
    }

    private void ShowError(string title, string message)
    {
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }
}
