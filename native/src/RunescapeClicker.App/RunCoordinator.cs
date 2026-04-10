using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public sealed class RunCoordinator : IDisposable
{
    private readonly AppSessionStore _store;
    private readonly IClickerEngine _clickerEngine;
    private readonly IInputAdapter _inputAdapter;
    private readonly IHotkeyService _hotkeyService;
    private readonly ICoordinatePickerService _coordinatePickerService;
    private readonly IUiDispatcher _dispatcher;
    private readonly IMouseSmokePrompt _mouseSmokePrompt;
    private readonly IAsyncDelayScheduler _delayScheduler;
    private CancellationTokenSource? _activeRunCancellationTokenSource;
    private bool _disposed;

    public RunCoordinator(
        AppSessionStore store,
        IClickerEngine clickerEngine,
        IInputAdapter inputAdapter,
        IHotkeyService hotkeyService,
        ICoordinatePickerService coordinatePickerService,
        IUiDispatcher dispatcher,
        IMouseSmokePrompt mouseSmokePrompt,
        IAsyncDelayScheduler delayScheduler)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clickerEngine = clickerEngine ?? throw new ArgumentNullException(nameof(clickerEngine));
        _inputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
        _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
        _coordinatePickerService = coordinatePickerService ?? throw new ArgumentNullException(nameof(coordinatePickerService));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _mouseSmokePrompt = mouseSmokePrompt ?? throw new ArgumentNullException(nameof(mouseSmokePrompt));
        _delayScheduler = delayScheduler ?? throw new ArgumentNullException(nameof(delayScheduler));
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var result = await _hotkeyService.EnsureRegisteredAsync(cancellationToken);
            await _dispatcher.InvokeAsync(() =>
            {
                if (result.Succeeded)
                {
                    _store.HotkeysRegistered = true;
                    _store.HotkeyStatusText = "Global hotkeys registered: F1 captures the current cursor and F2 stops the active run.";
                    _store.SetStatus("Global hotkeys are active.", InfoBarSeverity.Success);
                    _store.AppendLog("Hotkeys registered successfully.");
                }
                else
                {
                    var failureMessage = result.FailureMessage ?? "Failed to register global hotkeys.";
                    _store.HotkeysRegistered = false;
                    _store.HotkeyStatusText = $"Global hotkeys failed to register: {failureMessage}";
                    _store.SetStatus(failureMessage, InfoBarSeverity.Warning);
                    _store.AppendLog($"Hotkey registration failed: {failureMessage}");
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task CaptureCurrentCursorAsync(bool triggeredByHotkey, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!CanCaptureCurrentCursor())
        {
            if (!triggeredByHotkey)
            {
                await _dispatcher.InvokeAsync(() =>
                    _store.SetStatus("Start or edit a mouse click action before capturing coordinates.", InfoBarSeverity.Informational));
            }

            return;
        }

        var cursor = await _inputAdapter.GetCursorPositionAsync(cancellationToken);
        await _dispatcher.InvokeAsync(() =>
        {
            if (!cursor.Succeeded)
            {
                var message = cursor.FailureMessage ?? "Failed to capture the current cursor position.";
                _store.SetStatus(message, InfoBarSeverity.Error);
                _store.AppendLog(message);
                return;
            }

            _store.SelectedCoordinate = cursor.Position;
            var statusMessage = triggeredByHotkey
                ? "Captured the current cursor position from F1."
                : "Captured the current cursor position from the harness.";

            _store.SetStatus(statusMessage, InfoBarSeverity.Success);
            _store.AppendLog($"Captured current cursor position at ({cursor.Position.X}, {cursor.Position.Y}).");
        });
    }

    public async Task PickCoordinateAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!CanOpenCoordinatePicker())
        {
            await _dispatcher.InvokeAsync(() =>
                _store.SetStatus("Start or edit a mouse click action before opening the picker.", InfoBarSeverity.Informational));
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            _store.PickerActive = true;
            _store.SetStatus("Click anywhere on screen to capture coordinates, or press Esc to cancel.", InfoBarSeverity.Informational);
            _store.AppendLog("Coordinate picker opened.");
        });

        try
        {
            var result = await _coordinatePickerService.PickCoordinateAsync(cancellationToken);
            await _dispatcher.InvokeAsync(() =>
            {
                switch (result.Outcome)
                {
                    case CoordinatePickerOutcome.Captured when result.Position is not null:
                        _store.SelectedCoordinate = result.Position.Value;
                        _store.SetStatus(
                            $"Picked coordinate ({result.Position.Value.X}, {result.Position.Value.Y}).",
                            InfoBarSeverity.Success);
                        _store.AppendLog(
                            $"Overlay picker captured ({result.Position.Value.X}, {result.Position.Value.Y}).");
                        break;
                    case CoordinatePickerOutcome.Cancelled:
                        _store.SetStatus(result.Message ?? "Mouse position capture cancelled.", InfoBarSeverity.Warning);
                        _store.AppendLog(result.Message ?? "Mouse position capture cancelled.");
                        break;
                    case CoordinatePickerOutcome.Busy:
                        _store.SetStatus(result.Message ?? "The coordinate picker is already busy.", InfoBarSeverity.Warning);
                        _store.AppendLog(result.Message ?? "The coordinate picker is already busy.");
                        break;
                }
            });
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => _store.PickerActive = false);
        }
    }

    public Task StartRunFromSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_store.HasActions)
        {
            _store.SetStatus("Add at least one action before starting a run.", InfoBarSeverity.Warning);
            return Task.CompletedTask;
        }

        var request = new RunRequest(
            _store.Actions.ToArray(),
            _store.CurrentStopCondition,
            ExecutionProfile.Default);

        return StartRequestAsync(request, "Session run started.", cancellationToken);
    }

    public Task StartSafeSmokeAsync(CancellationToken cancellationToken = default)
        => StartRequestAsync(SmokeRunFactory.CreateSafeKeyboardRun(), "Safe F24 smoke run started.", cancellationToken);

    public async Task StartMouseSmokeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_store.SelectedCoordinate is null)
        {
            _store.SetStatus("Pick a coordinate before running the mouse smoke action.", InfoBarSeverity.Warning);
            return;
        }

        var point = _store.SelectedCoordinate.Value;
        if (!await _mouseSmokePrompt.ConfirmAsync(point, cancellationToken))
        {
            return;
        }

        await CountDownMouseSmokeAsync(cancellationToken);
        await StartRequestAsync(
            SmokeRunFactory.CreateMouseSmokeRun(point),
            $"Opt-in mouse smoke started at ({point.X}, {point.Y}).",
            cancellationToken);
    }

    public void RequestStop(string reason)
    {
        ThrowIfDisposed();

        if (_activeRunCancellationTokenSource is null)
        {
            _store.SetStatus(reason, InfoBarSeverity.Informational);
            _store.AppendLog(reason);
            return;
        }

        if (_store.StopRequested)
        {
            return;
        }

        _store.StopRequested = true;
        _store.SetStatus(reason, InfoBarSeverity.Warning);
        _store.RunStatusText = "Run status: stopping";
        _store.AppendLog(reason);
        _activeRunCancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeRunCancellationTokenSource?.Cancel();
        _activeRunCancellationTokenSource?.Dispose();
        _activeRunCancellationTokenSource = null;
        _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
    }

    private async Task StartRequestAsync(
        RunRequest request,
        string startMessage,
        CancellationToken cancellationToken)
    {
        if (_activeRunCancellationTokenSource is not null)
        {
            _store.SetStatus("A run is already active.", InfoBarSeverity.Warning);
            return;
        }

        _store.LastFault = null;
        _store.RunInProgress = true;
        _store.StopRequested = false;
        _store.ActiveRunRequest = request;
        _store.LastRequestedRun = request;
        _store.RunStatusText = "Run status: running";
        _store.SetStatus(startMessage, InfoBarSeverity.Informational);
        _store.AppendLog(startMessage);

        _activeRunCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var progress = new Progress<RunEvent>(runEvent =>
            {
                _ = _dispatcher.InvokeAsync(() => AppendRunEvent(runEvent));
            });

            var result = await _clickerEngine.ExecuteAsync(
                request,
                progress,
                _activeRunCancellationTokenSource.Token);

            await _dispatcher.InvokeAsync(() => ApplyRunResult(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _store.LastFault = new EngineError(EngineErrorCode.InputAdapterUnavailable, ex.Message);
                _store.SetStatus(ex.Message, InfoBarSeverity.Error);
                _store.RunStatusText = "Run status: faulted";
                _store.AppendLog($"Unexpected harness exception: {ex.Message}");
            });
        }
        finally
        {
            _activeRunCancellationTokenSource?.Dispose();
            _activeRunCancellationTokenSource = null;

            await _dispatcher.InvokeAsync(() =>
            {
                _store.RunInProgress = false;
                _store.StopRequested = false;
                _store.ActiveRunRequest = null;
            });
        }
    }

    private async void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _store.LastHotkeyText = $"Last hotkey event: {e.Hotkey} at {e.OccurredAt:HH:mm:ss}";
                _store.AppendLog($"Hotkey pressed: {e.Hotkey}");
            });

            switch (e.Hotkey)
            {
                case AutomationHotkey.CaptureCursor:
                    await CaptureCurrentCursorAsync(triggeredByHotkey: true);
                    break;
                case AutomationHotkey.StopRun:
                    await _dispatcher.InvokeAsync(() => RequestStop("Stop requested from F2."));
                    break;
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ApplyRunResult(RunResult result)
    {
        _store.LastRunResult = result;
        _store.LastFault = result.Error;
        _store.RunStatusText = $"Run status: {result.Outcome} after {result.ActionsCompleted} actions.";

        if (result.Error is not null)
        {
            _store.SetStatus(result.Error.Message, InfoBarSeverity.Error);
            _store.AppendLog($"Run faulted: {result.Error.Message}");
            return;
        }

        var severity = result.Outcome switch
        {
            RunOutcome.Stopped => InfoBarSeverity.Warning,
            RunOutcome.EmptyRequest => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Success,
        };

        _store.SetStatus($"Run finished with outcome {result.Outcome}.", severity);
        _store.AppendLog($"Run completed: {result.Outcome} after {result.ActionsCompleted} actions.");
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

        _store.AppendLog(summary);
    }

    private async Task CountDownMouseSmokeAsync(CancellationToken cancellationToken)
    {
        for (var seconds = 3; seconds >= 1; seconds--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentSecond = seconds;
            await _dispatcher.InvokeAsync(() =>
            {
                _store.SetStatus($"Mouse smoke starts in {currentSecond}...", InfoBarSeverity.Warning);
                _store.AppendLog($"Mouse smoke countdown: {currentSecond}");
            });
            await _delayScheduler.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private bool CanCaptureCurrentCursor()
        => _store.ComposerMode == ComposerMode.MouseClick
            && !_store.PickerActive
            && !_store.RunInProgress
            && !_store.StopRequested;

    private bool CanOpenCoordinatePicker()
        => _store.ComposerMode == ComposerMode.MouseClick
            && !_store.PickerActive
            && !_store.RunInProgress
            && !_store.StopRequested;

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
