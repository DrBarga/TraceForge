using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Reflection;
using TraceForge.App.Services;
using TraceForge.App.ViewModels;
using TraceForge.Application.Diagnostics;
using TraceForge.Application.Ipc;
using TraceForge.Application.Reporting;
using TraceForge.Data;

namespace TraceForge.App;

public sealed partial class MainWindow : Window
{
    private static readonly string Version = GetVersion();

    private readonly AgentProcessHost _agentHost = new();
    private readonly AgentClient _agentClient;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SessionRepository _sessionRepository;

    private long _sessionId;
    private bool _refreshInProgress;
    private bool _initialized;
    private bool _closing;
    private readonly string _applicationLogPath;

    public MainWindow()
    {
        _agentClient = new AgentClient(TimeSpan.FromSeconds(5), _agentHost.PipeName);
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraceForge");

        Directory.CreateDirectory(dataRoot);
        var logDirectory = Path.Combine(dataRoot, "Logs");
        Directory.CreateDirectory(logDirectory);
        _applicationLogPath = Path.Combine(logDirectory, "app.log");
        _sessionRepository = new SessionRepository(Path.Combine(dataRoot, "traceforge.db"));

        ViewModel = new DashboardViewModel(
            _agentClient,
            _sessionRepository,
            new DiagnosticAnalyzer(),
            new BlackBoxBuffer(TimeSpan.FromSeconds(60)),
            new DiagnosticReportBuilder());

        InitializeComponent();
        var workArea = Microsoft.UI.Windowing.DisplayArea
            .GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary)
            .WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            Math.Min(1500, workArea.Width),
            Math.Min(900, workArea.Height)));
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
            await ConnectAgentAsync(_lifetimeCancellation.Token);
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
            var (reportPath, htmlReportPath) = await SaveReportAsync("TraceForge");
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "Diagnostic report exported";
            StatusInfoBar.Message = $"JSON: {reportPath} | HTML: {htmlReportPath}";
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            ShowError("Report export failed", exception.Message);
        }
    }

    private async void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sessions = await _sessionRepository.GetRecentSessionsAsync(cancellationToken: _lifetimeCancellation.Token);
            var incidents = await _sessionRepository.GetRecentIncidentsAsync(cancellationToken: _lifetimeCancellation.Token);
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = "Recent sessions", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var session in sessions)
            {
                var started = session.StartedUtc.ToLocalTime();
                var ended = session.EndedUtc?.ToLocalTime().ToString("g") ?? "running";
                content.Children.Add(new TextBlock
                {
                    Text = $"{started:g} – {ended} · {session.SnapshotCount} samples · {session.IncidentCount} exits",
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (sessions.Count == 0)
            {
                content.Children.Add(new TextBlock { Text = "No recorded sessions yet." });
            }

            content.Children.Add(new TextBlock
            {
                Text = "Recent process exits",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 0)
            });
            foreach (var incident in incidents)
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"{incident.ObservedUtc.ToLocalTime():g} · {incident.ProcessName} (PID {incident.Pid}) · 0x{incident.ExitCode:X8}",
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (incidents.Count == 0)
            {
                content.Children.Add(new TextBlock { Text = "No watched process exits yet." });
            }

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Diagnostic history",
                Content = new ScrollViewer { Content = content, MaxHeight = 500, Width = 600 },
                CloseButtonText = "Close"
            };
            await dialog.ShowAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteApplicationLog("Opening diagnostic history failed", exception);
            ShowError("History unavailable", exception.Message);
        }
    }

    private async void WatchProcessButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.WatchSelectedAsync(_lifetimeCancellation.Token);
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Title = "Process watch started";
            StatusInfoBar.Message = ViewModel.WatchedProcessStatus;
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            WriteApplicationLog("Process watch failed", exception);
            ShowError("Process watch failed", exception.Message);
        }
    }

    private async void InspectProcessButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InspectSelectedAsync(_lifetimeCancellation.Token);
        }
        catch (Exception exception)
        {
            WriteApplicationLog("Process inspection failed", exception);
            ShowError("Process inspection failed", exception.Message);
        }
    }

    private async void LaunchTargetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(this));
            var executable = await picker.PickSingleFileAsync();
            if (executable is null)
            {
                return;
            }

            var argumentsBox = new TextBox
            {
                Header = "Command line arguments (optional)",
                PlaceholderText = "Arguments passed directly to the application"
            };
            var launchDialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Launch and watch",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = executable.Path, TextWrapping = TextWrapping.Wrap },
                        argumentsBox
                    }
                },
                PrimaryButtonText = "Launch",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await launchDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable.Path,
                Arguments = argumentsBox.Text,
                WorkingDirectory = Path.GetDirectoryName(executable.Path)!,
                UseShellExecute = false
            });
            if (process is null)
            {
                throw new InvalidOperationException("The selected application could not be started.");
            }

            await ViewModel.WatchProcessAsync(
                checked((uint)process.Id),
                Path.GetFileName(executable.Path),
                new DateTimeOffset(process.StartTime).ToUnixTimeMilliseconds(),
                _lifetimeCancellation.Token);
            SearchBox.Text = process.Id.ToString();
            await RefreshAsync();
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "Application launched";
            StatusInfoBar.Message = ViewModel.WatchedProcessStatus;
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            WriteApplicationLog("Launch and watch failed", exception);
            ShowError("Launch and watch failed", exception.Message);
        }
    }

    private async void StopWatchingButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.StopWatchingAsync(_lifetimeCancellation.Token);
            StatusInfoBar.IsOpen = false;
        }
        catch (Exception exception)
        {
            WriteApplicationLog("Stopping process watch failed", exception);
            ShowError("Stopping process watch failed", exception.Message);
        }
    }

    private async void CaptureDumpButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Capture process memory dump?",
            Content = "Memory dumps can contain credentials, personal data, and other sensitive application state. Keep the file private and share it only with trusted recipients.",
            PrimaryButtonText = "Capture",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var dumpPath = await ViewModel.CaptureSelectedDumpAsync(_lifetimeCancellation.Token);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Title = "MiniDump captured";
            StatusInfoBar.Message = dumpPath;
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            WriteApplicationLog("MiniDump capture failed", exception);
            ShowError("MiniDump capture failed", exception.Message);
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshInProgress || _sessionId == 0 || _closing)
        {
            return;
        }

        _refreshInProgress = true;
        try
        {
            var previousIncident = ViewModel.LastIncident;
            await ViewModel.RefreshAsync(_sessionId, _lifetimeCancellation.Token);
            ShowStorageWarning();
            if (!ReferenceEquals(previousIncident, ViewModel.LastIncident))
            {
                await SaveIncidentReportAsync();
            }
        }
        catch (OperationCanceledException) when (_closing)
        {
        }
        catch (Exception exception) when (!_closing)
        {
            _timer.Stop();
            WriteApplicationLog("Agent communication failed", exception);

            try
            {
                await RecoverAgentAsync(_lifetimeCancellation.Token);
                var previousIncident = ViewModel.LastIncident;
                await ViewModel.RefreshAsync(_sessionId, _lifetimeCancellation.Token);
                ShowStorageWarning();
                if (!ReferenceEquals(previousIncident, ViewModel.LastIncident))
                {
                    await SaveIncidentReportAsync();
                }

                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Title = "Agent connection recovered";
                StatusInfoBar.Message = "TraceForge restarted the diagnostics agent after a communication failure.";
                StatusInfoBar.IsOpen = true;

                if (LiveToggle.IsOn)
                {
                    _timer.Start();
                }
            }
            catch (Exception recoveryException) when (!_closing)
            {
                WriteApplicationLog("Agent recovery failed", recoveryException);
                ShowError("Agent communication failed", recoveryException.Message);
            }
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _timer.Stop();
        _lifetimeCancellation.Cancel();

        try
        {
            if (_sessionId != 0)
            {
                await _sessionRepository.EndSessionAsync(_sessionId, DateTimeOffset.UtcNow);
            }

            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _agentClient.ShutdownAsync(shutdownTimeout.Token);
            await _agentClient.DisposeAsync();
        }
        catch
        {
        }
        finally
        {
            _agentHost.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }

    private void ShowError(string title, string message)
    {
        StatusInfoBar.Severity = InfoBarSeverity.Error;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private void ShowStorageWarning()
    {
        if (string.IsNullOrWhiteSpace(ViewModel.StorageWarning))
        {
            return;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Title = "Local history unavailable";
        StatusInfoBar.Message = ViewModel.StorageWarning;
        StatusInfoBar.IsOpen = true;
    }

    private async Task ConnectAgentAsync(CancellationToken cancellationToken)
    {
        _agentHost.Start();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _agentClient.ConnectAsync(TimeSpan.FromSeconds(2), cancellationToken);
                await _agentClient.PingAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidDataException)
            {
                lastError = exception;
                await _agentClient.DisconnectAsync();

                if (!_agentHost.IsRunning)
                {
                    _agentHost.Start();
                }

                if (attempt < 4)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException("TraceForge could not establish a compatible Agent connection.", lastError);
    }

    private async Task RecoverAgentAsync(CancellationToken cancellationToken)
    {
        await _agentClient.DisconnectAsync();
        _agentHost.Restart();
        await ConnectAgentAsync(cancellationToken);
        await ViewModel.RestoreWatchAsync(cancellationToken);
    }

    private async Task<(string JsonPath, string HtmlPath)> SaveReportAsync(string prefix)
    {
        var reportDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TraceForge",
            "Reports");
        Directory.CreateDirectory(reportDirectory);

        var basePath = Path.Combine(reportDirectory, $"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}");
        var jsonPath = basePath + ".json";
        var htmlPath = basePath + ".html";

        await File.WriteAllTextAsync(jsonPath, ViewModel.BuildReport(Version));
        await File.WriteAllTextAsync(htmlPath, ViewModel.BuildHtmlReport(Version));
        return (jsonPath, htmlPath);
    }

    private async Task SaveIncidentReportAsync()
    {
        try
        {
            var (jsonPath, htmlPath) = await SaveReportAsync("TraceForge-Incident");
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Title = "Watched process exited";
            StatusInfoBar.Message = $"{ViewModel.LastIncidentSummary}. Reports: {jsonPath} | {htmlPath}";
            StatusInfoBar.IsOpen = true;
        }
        catch (Exception exception)
        {
            WriteApplicationLog("Incident report export failed", exception);
            ShowError("Incident report export failed", exception.Message);
        }
    }

    private void WriteApplicationLog(string message, Exception exception)
    {
        try
        {
            File.AppendAllText(
                _applicationLogPath,
                $"{DateTimeOffset.UtcNow:O} {message}: {exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetVersion()
    {
        var value = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Split('+', 2)[0];
    }
}
