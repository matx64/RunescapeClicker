using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WinUI windows are released by the framework and dispose their owned services in the Closed handler.")]
public sealed partial class MainWindow : Window
{
    private readonly Phase3HarnessComposition _composition;
    private readonly StringBuilder _logBuilder = new();
    private readonly CancellationTokenSource _windowLifetimeCancellationTokenSource = new();
    private CancellationTokenSource? _runCancellationTokenSource;
    private ScreenPoint? _pickedCoordinate;
    private bool _pickerActive;
    private bool _isClosed;

    public MainWindow()
    {
        _composition = Phase3HarnessComposition.CreateDefault();
        InitializeComponent();
        Title = "Runescape Clicker";
        SummaryText.Text = AppEnvironment.Summary;
        Closed += OnClosed;
        _composition.AutomationServices.HotkeyService.HotkeyPressed += OnHotkeyPressed;
        _ = InitializeHarnessAsync(_windowLifetimeCancellationTokenSource.Token);
        UpdateUiState();
    }

    private async Task InitializeHarnessAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _composition.AutomationServices.HotkeyService.EnsureRegisteredAsync(cancellationToken);
            if (_isClosed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (result.Succeeded)
            {
                HotkeyStatusText.Text = "Global hotkeys registered: F1 captures the current cursor and F2 stops the active run.";
                ShowStatus("Global hotkeys are active.", InfoBarSeverity.Success);
                AppendLog("Hotkeys registered successfully.");
            }
            else
            {
                HotkeyStatusText.Text = $"Global hotkeys failed to register: {result.FailureMessage}";
                ShowStatus(result.FailureMessage ?? "Failed to register global hotkeys.", InfoBarSeverity.Warning);
                AppendLog($"Hotkey registration failed: {result.FailureMessage}");
            }

            LastHotkeyText.Text = "Last hotkey event: none";
            RunStatusText.Text = "Run status: idle";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async void RetryHotkeysButton_Click(object sender, RoutedEventArgs e)
        => await InitializeHarnessAsync(_windowLifetimeCancellationTokenSource.Token);

    private async void CaptureCursorButton_Click(object sender, RoutedEventArgs e)
        => await CaptureCurrentCursorAsync("Captured the current cursor position from the harness.");

    private async void PickOnScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerActive)
        {
            ShowStatus("The coordinate picker is already running.", InfoBarSeverity.Informational);
            return;
        }

        _pickerActive = true;
        UpdateUiState();
        ShowStatus("Click anywhere on screen to capture coordinates, or press Esc to cancel.", InfoBarSeverity.Informational);
        AppendLog("Coordinate picker opened.");

        try
        {
            var result = await _composition.AutomationServices.CoordinatePickerService.PickCoordinateAsync(_windowLifetimeCancellationTokenSource.Token);
            if (_isClosed || _windowLifetimeCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            switch (result.Outcome)
            {
                case CoordinatePickerOutcome.Captured when result.Position is not null:
                    _pickedCoordinate = result.Position.Value;
                    PickedCoordinateText.Text = $"Picked coordinate: ({_pickedCoordinate.Value.X}, {_pickedCoordinate.Value.Y})";
                    ShowStatus($"Picked coordinate ({_pickedCoordinate.Value.X}, {_pickedCoordinate.Value.Y}).", InfoBarSeverity.Success);
                    AppendLog($"Overlay picker captured ({_pickedCoordinate.Value.X}, {_pickedCoordinate.Value.Y}).");
                    break;
                case CoordinatePickerOutcome.Cancelled:
                    ShowStatus(result.Message ?? "Mouse position capture cancelled.", InfoBarSeverity.Warning);
                    AppendLog(result.Message ?? "Mouse position capture cancelled.");
                    break;
                case CoordinatePickerOutcome.Busy:
                    ShowStatus(result.Message ?? "The coordinate picker is already busy.", InfoBarSeverity.Warning);
                    AppendLog(result.Message ?? "The coordinate picker is already busy.");
                    break;
            }
        }
        finally
        {
            _pickerActive = false;
            UpdateUiState();
        }
    }

    private async void StartSafeSmokeButton_Click(object sender, RoutedEventArgs e)
        => await StartRunAsync(SmokeRunFactory.CreateSafeKeyboardRun(), "Safe F24 smoke run started.");

    private async void RunMouseSmokeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pickedCoordinate is null)
        {
            ShowStatus("Pick a coordinate before running the mouse smoke action.", InfoBarSeverity.Warning);
            return;
        }

        var confirmationDialog = new ContentDialog
        {
            Title = "Run mouse smoke?",
            Content = "This opt-in smoke action performs a real right click at the picked coordinate after a short countdown. Continue only if that target is safe.",
            PrimaryButtonText = "Run Mouse Smoke",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        if (await confirmationDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var lifetimeToken = _windowLifetimeCancellationTokenSource.Token;

        try
        {
            for (var seconds = 3; seconds >= 1; seconds--)
            {
                lifetimeToken.ThrowIfCancellationRequested();
                ShowStatus($"Mouse smoke starts in {seconds}...", InfoBarSeverity.Warning);
                AppendLog($"Mouse smoke countdown: {seconds}");
                await Task.Delay(TimeSpan.FromSeconds(1), lifetimeToken);
            }

            await StartRunAsync(
                SmokeRunFactory.CreateMouseSmokeRun(_pickedCoordinate.Value),
                $"Opt-in mouse smoke started at ({_pickedCoordinate.Value.X}, {_pickedCoordinate.Value.Y}).",
                lifetimeToken);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            AppendLog("Mouse smoke countdown cancelled.");
        }
    }

    private void StopRunButton_Click(object sender, RoutedEventArgs e)
        => RequestStop("Stop requested from the harness.");

    private async Task CaptureCurrentCursorAsync(string successMessage)
    {
        if (_pickerActive)
        {
            ShowStatus("Ignoring direct cursor capture while the overlay picker is active.", InfoBarSeverity.Informational);
            return;
        }

        var cursor = await _composition.AutomationServices.InputAdapter.GetCursorPositionAsync(CancellationToken.None);
        if (!cursor.Succeeded)
        {
            ShowStatus(cursor.FailureMessage ?? "Failed to capture the current cursor position.", InfoBarSeverity.Error);
            AppendLog(cursor.FailureMessage ?? "Failed to capture the current cursor position.");
            return;
        }

        _pickedCoordinate = cursor.Position;
        PickedCoordinateText.Text = $"Picked coordinate: ({cursor.Position.X}, {cursor.Position.Y})";
        ShowStatus(successMessage, InfoBarSeverity.Success);
        AppendLog($"Captured current cursor position at ({cursor.Position.X}, {cursor.Position.Y}).");
        UpdateUiState();
    }

    private async Task StartRunAsync(RunRequest request, string statusMessage, CancellationToken cancellationToken = default)
    {
        if (_runCancellationTokenSource is not null)
        {
            ShowStatus("A smoke run is already active.", InfoBarSeverity.Warning);
            return;
        }

        if (_isClosed || _windowLifetimeCancellationTokenSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _runCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            _windowLifetimeCancellationTokenSource.Token,
            cancellationToken);

        if (!_isClosed)
        {
            RunProgressRing.IsActive = true;
            RunStatusText.Text = "Run status: running";
        }

        AppendLog(statusMessage);
        ShowStatus(statusMessage, InfoBarSeverity.Informational);
        UpdateUiState();

        try
        {
            var progress = new Progress<RunEvent>(AppendRunEvent);
            var result = await _composition.ClickerEngine.ExecuteAsync(
                request,
                progress,
                _runCancellationTokenSource.Token);

            if (!_isClosed)
            {
                RunStatusText.Text = $"Run status: {result.Outcome} after {result.ActionsCompleted} actions.";
            }

            if (result.Error is null)
            {
                ShowStatus($"Run finished with outcome {result.Outcome}.", InfoBarSeverity.Success);
                AppendLog($"Run completed: {result.Outcome} after {result.ActionsCompleted} actions.");
            }
            else
            {
                ShowStatus(result.Error.Message, InfoBarSeverity.Error);
                AppendLog($"Run faulted: {result.Error.Message}");
            }
        }
        catch (OperationCanceledException) when (_windowLifetimeCancellationTokenSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_isClosed)
            {
                RunStatusText.Text = "Run status: faulted";
            }

            ShowStatus(ex.Message, InfoBarSeverity.Error);
            AppendLog($"Unexpected harness exception: {ex.Message}");
        }
        finally
        {
            _runCancellationTokenSource?.Dispose();
            _runCancellationTokenSource = null;

            if (!_isClosed)
            {
                RunProgressRing.IsActive = false;
            }

            UpdateUiState();
        }
    }

    private void AppendRunEvent(RunEvent runEvent)
    {
        var summary = runEvent switch
        {
            RunStartedEvent started => $"Run started with {started.Actions.Count} actions.",
            IterationStartedEvent iteration => $"Iteration {iteration.IterationNumber} started.",
            ActionStartedEvent started => $"Action {started.ActionIndex} started: {started.Action.GetType().Name}.",
            ActionCompletedEvent completed => $"Action {completed.ActionIndex} completed: {completed.Action.GetType().Name}.",
            RunEndedEvent ended => $"Run ended with {ended.Result.Outcome}.",
            _ => runEvent.GetType().Name,
        };

        AppendLog(summary);
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LastHotkeyText.Text = $"Last hotkey event: {e.Hotkey} at {e.OccurredAt:HH:mm:ss}";
            AppendLog($"Hotkey pressed: {e.Hotkey}");

            switch (e.Hotkey)
            {
                case AutomationHotkey.CaptureCursor:
                    _ = CaptureCurrentCursorAsync("Captured the current cursor position from F1.");
                    break;
                case AutomationHotkey.StopRun:
                    RequestStop("Stop requested from F2.");
                    break;
            }
        });
    }

    private void RequestStop(string reason)
    {
        if (_runCancellationTokenSource is null)
        {
            ShowStatus(reason, InfoBarSeverity.Informational);
            AppendLog(reason);
            return;
        }

        _runCancellationTokenSource.Cancel();
        ShowStatus(reason, InfoBarSeverity.Warning);
        AppendLog(reason);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        if (_isClosed)
        {
            return;
        }

        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void AppendLog(string message)
    {
        if (_isClosed)
        {
            return;
        }

        if (_logBuilder.Length > 0)
        {
            _logBuilder.AppendLine();
        }

        _logBuilder.Append(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:HH:mm:ss}  {message}"));
        LogTextBox.Text = _logBuilder.ToString();
    }

    private void UpdateUiState()
    {
        if (_isClosed)
        {
            return;
        }

        var runActive = _runCancellationTokenSource is not null;
        CaptureCursorButton.IsEnabled = !_pickerActive;
        PickOnScreenButton.IsEnabled = !_pickerActive;
        StartSafeSmokeButton.IsEnabled = !runActive;
        RunMouseSmokeButton.IsEnabled = !runActive && _pickedCoordinate is not null;
        StopRunButton.IsEnabled = runActive;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        _windowLifetimeCancellationTokenSource.Cancel();
        _runCancellationTokenSource?.Cancel();
        _composition.AutomationServices.HotkeyService.HotkeyPressed -= OnHotkeyPressed;
        _composition.Dispose();
    }
}
