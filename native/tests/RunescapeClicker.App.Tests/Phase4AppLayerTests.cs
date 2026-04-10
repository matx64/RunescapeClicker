using FluentAssertions;
using Microsoft.UI.Xaml.Controls;
using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;
using Windows.System;

namespace RunescapeClicker.App.Tests;

public sealed class Phase4AppLayerTests
{
    [Fact]
    public void BeginningMouseDraftClearsSavedCoordinateAndRequiresANewCapture()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.SelectedCoordinate = new ScreenPoint(12, 34);

        viewModel.ActionComposer.BeginAddMouseClickCommand.Execute(null);

        viewModel.ActionComposer.IsMouseDraftActive.Should().BeTrue();
        store.SelectedCoordinate.Should().BeNull();
        viewModel.ActionComposer.ConfirmDraftCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void NonMouseDraftsDoNotClearTheSavedCoordinate()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        var point = new ScreenPoint(25, 40);
        store.SelectedCoordinate = point;

        viewModel.ActionComposer.BeginAddKeyPressCommand.Execute(null);
        store.SelectedCoordinate.Should().Be(point);

        viewModel.ActionComposer.BeginAddDelayCommand.Execute(null);
        store.SelectedCoordinate.Should().Be(point);
    }

    [Fact]
    public void InvalidKeyAndDelayDraftsStayOpenAndDoNotMutateTheSequence()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);

        viewModel.ActionComposer.BeginAddKeyPressCommand.Execute(null);
        viewModel.ActionComposer.ConfirmDraftCommand.Execute(null);

        viewModel.ActionComposer.IsKeyDraftActive.Should().BeTrue();
        store.Actions.Should().BeEmpty();

        viewModel.ActionComposer.BeginAddDelayCommand.Execute(null);
        viewModel.ActionComposer.DelayMillisecondsText = "1.5";
        viewModel.ActionComposer.ConfirmDraftCommand.Execute(null);

        viewModel.ActionComposer.IsDelayDraftActive.Should().BeTrue();
        store.Actions.Should().BeEmpty();
    }

    [Fact]
    public void BeginningKeyDraftArmsRealKeyboardCapture()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.SelectedKeyOption = new KeyOption("Space", 0x20, 0x39, false);

        viewModel.ActionComposer.BeginAddKeyPressCommand.Execute(null);

        viewModel.ActionComposer.IsKeyDraftActive.Should().BeTrue();
        viewModel.ActionComposer.IsAwaitingKeyCapture.Should().BeTrue();
        store.SelectedKeyOption.Should().BeNull();
        viewModel.ActionComposer.ConfirmDraftCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CapturedKeyMetadataIsSavedIntoTheSequence()
    {
        var store = CreateStore();
        var keyMetadata = new FakeKeyboardKeyMetadataService
        {
            Result = new NormalizedKeyboardKey("A", 0x41, 0x1E, false),
        };
        var viewModel = CreateMainViewModel(store, keyMetadataService: keyMetadata);

        viewModel.ActionComposer.BeginAddKeyPressCommand.Execute(null);
        viewModel.ActionComposer.TryCaptureKey(VirtualKey.A).Should().BeTrue();
        viewModel.ActionComposer.ConfirmDraftCommand.Execute(null);

        keyMetadata.RequestedKeys.Should().ContainSingle().Which.Should().Be(VirtualKey.A);
        store.Actions.Should().ContainSingle();
        store.Actions[0].Should().Be(new KeyPressAction(0x41, 0x1E, false, "A"));
    }

    [Fact]
    public void EditingAKeyActionDoesNotCaptureUntilExplicitlyArmed()
    {
        var store = CreateStore();
        store.Actions.Add(new KeyPressAction(0x20, 0x39, false, "Space"));
        var keyMetadata = new FakeKeyboardKeyMetadataService
        {
            Result = new NormalizedKeyboardKey("A", 0x41, 0x1E, false),
        };
        var viewModel = CreateMainViewModel(store, keyMetadataService: keyMetadata);

        viewModel.ActionList.EditAction(0);

        viewModel.ActionComposer.TryCaptureKey(VirtualKey.A).Should().BeFalse();
        viewModel.ActionComposer.SelectedKeyOption.Should().Be(new KeyOption("Space", 0x20, 0x39, false));

        viewModel.ActionComposer.BeginKeyCaptureCommand.Execute(null);
        viewModel.ActionComposer.TryCaptureKey(VirtualKey.A).Should().BeTrue();
        viewModel.ActionComposer.SelectedKeyOption.Should().Be(new KeyOption("A", 0x41, 0x1E, false));
    }

    [Fact]
    public void EditingAnActionReplacesItInPlaceOnlyAfterConfirm()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(1500)));

        viewModel.ActionList.EditAction(0);
        viewModel.ActionComposer.DelayMillisecondsText = "2500";

        store.Actions[0].Should().BeOfType<DelayAction>()
            .Which.Duration.Should().Be(TimeSpan.FromMilliseconds(1500));

        viewModel.ActionComposer.ConfirmDraftCommand.Execute(null);

        store.Actions.Should().ContainSingle();
        store.Actions[0].Should().Be(new DelayAction(TimeSpan.FromMilliseconds(2500)));
    }

    [Fact]
    public void ClearActionsPreservesOtherEditorAndStopState()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(100)));
        store.Actions.Add(new KeyPressAction(1, 1, false, "space"));
        store.SelectedCoordinate = new ScreenPoint(12, 34);
        store.SetStopRuleMode(StopRuleMode.Timer);
        store.TryUpdateTimerSeconds("25");
        viewModel.ActionComposer.BeginAddKeyPressCommand.Execute(null);
        viewModel.ActionComposer.SelectedKeyOption = new KeyOption("Space", 0x20, 0x39, false);
        store.SetStatus("ready", InfoBarSeverity.Informational);

        viewModel.ActionList.ClearActionsCommand.Execute(null);

        store.Actions.Should().BeEmpty();
        viewModel.ActionComposer.IsKeyDraftActive.Should().BeTrue();
        store.SelectedCoordinate.Should().Be(new ScreenPoint(12, 34));
        store.CurrentStopCondition.Should().BeOfType<TimerStopCondition>()
            .Which.Duration.Should().Be(TimeSpan.FromSeconds(25));
        store.StatusMessage.Should().Be("Sequence cleared.");
    }

    [Fact]
    public void DeletingTheEditedActionClosesTheDraft()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(100)));
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(200)));

        viewModel.ActionList.EditAction(1);
        viewModel.ActionList.DeleteAction(1);

        viewModel.ActionComposer.IsDraftOpen.Should().BeFalse();
        store.EditingIndex.Should().BeNull();
    }

    [Fact]
    public void ReorderingKeepsTheDraftBoundToTheSameAction()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(100)));
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(200)));

        viewModel.ActionList.EditAction(1);
        viewModel.ActionList.MoveAction(0, 1);
        viewModel.ActionComposer.DelayMillisecondsText = "450";
        viewModel.ActionComposer.ConfirmDraftCommand.Execute(null);

        store.Actions[0].Should().Be(new DelayAction(TimeSpan.FromMilliseconds(450)));
        store.Actions[1].Should().Be(new DelayAction(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void DragDropCommitUpdatesTheSequenceAndKeepsTheEditedAction()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(100)));
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(200)));

        viewModel.ActionList.EditAction(1);
        viewModel.ActionList.Items.Move(1, 0);
        viewModel.ActionList.CommitCurrentItemOrder();
        viewModel.ActionComposer.DelayMillisecondsText = "450";
        viewModel.ActionComposer.ConfirmDraftCommand.Execute(null);

        store.Actions[0].Should().Be(new DelayAction(TimeSpan.FromMilliseconds(450)));
        store.Actions[1].Should().Be(new DelayAction(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void RejectedDragDropRestoresTheVisibleSequenceOrder()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(100)));
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(200)));

        viewModel.ActionList.Items.Move(1, 0);
        store.RunInProgress = true;
        viewModel.ActionList.CommitCurrentItemOrder();

        viewModel.ActionList.Items.Select(item => item.Action).Should().Equal(store.Actions);
    }

    [Fact]
    public void InvalidTimerInputKeepsTheLastValidStopCondition()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);
        viewModel.RunPanel.SelectedStopMode = StopRuleMode.Timer;
        viewModel.RunPanel.TimerSecondsText = "25";

        store.CurrentStopCondition.Should().BeOfType<TimerStopCondition>()
            .Which.Duration.Should().Be(TimeSpan.FromSeconds(25));

        viewModel.RunPanel.TimerSecondsText = "two minutes";

        store.CurrentStopCondition.Should().BeOfType<TimerStopCondition>()
            .Which.Duration.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public async Task DirectCaptureOnlyAppliesDuringMouseDraftWhenPickerIsInactive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var input = new FakeInputAdapter
        {
            CursorLocation = CursorLocationResult.Success(new ScreenPoint(320, 640)),
        };
        var store = CreateStore();
        using var coordinator = CreateCoordinator(store, inputAdapter: input);
        var viewModel = new MainViewModel(store, coordinator);

        await coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: true, cancellationToken);
        store.SelectedCoordinate.Should().BeNull();

        viewModel.ActionComposer.BeginAddKeyPressCommand.Execute(null);
        await coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: true, cancellationToken);
        store.SelectedCoordinate.Should().BeNull();

        viewModel.ActionComposer.BeginAddMouseClickCommand.Execute(null);
        store.PickerActive = true;
        await coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: true, cancellationToken);
        store.SelectedCoordinate.Should().BeNull();

        store.PickerActive = false;
        await coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: true, cancellationToken);
        store.SelectedCoordinate.Should().Be(new ScreenPoint(320, 640));
        store.StatusMessage.Should().Be("Captured the current cursor position from F1.");
    }

    [Fact]
    public async Task HotkeyRegistrationFailureShowsAFriendlyMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hotkeys = new FakeHotkeyService
        {
            RegistrationResult = HotkeyRegistrationResult.Collision(
                AutomationHotkey.StopRun,
                "F2 is already registered by another app."),
        };
        var store = CreateStore();
        using var coordinator = CreateCoordinator(store, hotkeyService: hotkeys);

        await coordinator.InitializeAsync(cancellationToken);

        store.HotkeysRegistered.Should().BeFalse();
        store.HotkeyStatusText.Should().Contain("F2");
        store.StatusMessage.Should().Contain("Retry Hotkeys");
        store.StatusSeverity.Should().Be(InfoBarSeverity.Warning);
    }

    [Fact]
    public async Task RetryHotkeysCanRecoverAfterAnInitialCollision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hotkeys = new FakeHotkeyService
        {
            RegistrationResult = HotkeyRegistrationResult.Collision(
                AutomationHotkey.CaptureCursor,
                "F1 is already registered by another app."),
        };
        var store = CreateStore();
        using var coordinator = CreateCoordinator(store, hotkeyService: hotkeys);

        await coordinator.InitializeAsync(cancellationToken);
        store.HotkeysRegistered.Should().BeFalse();

        hotkeys.RegistrationResult = HotkeyRegistrationResult.Success();
        await coordinator.InitializeAsync(cancellationToken);

        hotkeys.EnsureRegisteredCalls.Should().Be(2);
        store.HotkeysRegistered.Should().BeTrue();
        store.StatusMessage.Should().Be("Global hotkeys are active.");
    }

    [Fact]
    public void StartRunIsDisabledWhenNoActionsExist()
    {
        var store = CreateStore();
        var viewModel = CreateMainViewModel(store);

        viewModel.RunPanel.StartRunCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task StartRunSnapshotsTheCurrentSessionBeforeLaterEdits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = new RecordingEngine();
        var store = CreateStore();
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(900)));
        using var coordinator = CreateCoordinator(store, clickerEngine: engine);

        var runTask = coordinator.StartRunFromSessionAsync(cancellationToken);
        await Task.Yield();

        store.Actions[0] = new DelayAction(TimeSpan.FromMilliseconds(2500));

        engine.Requests.Should().ContainSingle();
        engine.Requests[0].Actions.Should().ContainSingle()
            .Which.Should().Be(new DelayAction(TimeSpan.FromMilliseconds(900)));

        engine.Complete(new RunResult(RunOutcome.Stopped, 1, 0, TimeSpan.FromMilliseconds(10)));
        await runTask;
    }

    [Fact]
    public async Task StopCommandAndF2BothDriveTheStoppingState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = new RecordingEngine();
        var hotkeys = new FakeHotkeyService();
        var store = CreateStore();
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(500)));
        using var coordinator = CreateCoordinator(store, clickerEngine: engine, hotkeyService: hotkeys);
        var viewModel = new MainViewModel(store, coordinator);

        var firstRun = coordinator.StartRunFromSessionAsync(cancellationToken);
        await Task.Yield();

        viewModel.CurrentState.Should().Be(AppState.Running);
        viewModel.RunPanel.StopRunCommand.Execute(null);
        viewModel.CurrentState.Should().Be(AppState.Stopping);

        engine.Complete(new RunResult(RunOutcome.Stopped, 1, 0, TimeSpan.FromMilliseconds(25)));
        await firstRun;

        var secondRun = coordinator.StartRunFromSessionAsync(cancellationToken);
        await Task.Yield();

        hotkeys.Raise(AutomationHotkey.StopRun);
        viewModel.CurrentState.Should().Be(AppState.Stopping);

        engine.Complete(new RunResult(RunOutcome.Stopped, 1, 0, TimeSpan.FromMilliseconds(25)));
        await secondRun;
    }

    [Fact]
    public async Task EngineFaultTransitionsTheMainViewModelToFaulted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var engine = new RecordingEngine();
        var store = CreateStore();
        store.Actions.Add(new DelayAction(TimeSpan.FromMilliseconds(500)));
        using var coordinator = CreateCoordinator(store, clickerEngine: engine);
        var viewModel = new MainViewModel(store, coordinator);

        var runTask = coordinator.StartRunFromSessionAsync(cancellationToken);
        await Task.Yield();
        engine.Complete(new RunResult(
            RunOutcome.Faulted,
            1,
            0,
            TimeSpan.FromMilliseconds(10),
            new EngineError(
                EngineErrorCode.KeyPressFailed,
                "Key blocked",
                0,
                InputFailureKind.ElevatedTarget)));
        await runTask;

        viewModel.CurrentState.Should().Be(AppState.Faulted);
        store.StatusSeverity.Should().Be(InfoBarSeverity.Error);
        store.StatusMessage.Should().Contain("same privilege level");
        store.LogText.Should().Contain("Key blocked");
    }

    [Fact]
    public async Task LatestStatusMessageWinsAfterCoordinatorUpdates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var input = new FakeInputAdapter
        {
            CursorLocation = CursorLocationResult.Success(new ScreenPoint(50, 60)),
        };
        var hotkeys = new FakeHotkeyService
        {
            RegistrationResult = HotkeyRegistrationResult.Collision(
                AutomationHotkey.CaptureCursor,
                "F1 collision"),
        };
        var store = CreateStore();
        using var coordinator = CreateCoordinator(store, inputAdapter: input, hotkeyService: hotkeys);
        var viewModel = new MainViewModel(store, coordinator);

        await coordinator.InitializeAsync(cancellationToken);
        viewModel.ActionComposer.BeginAddMouseClickCommand.Execute(null);
        await coordinator.CaptureCurrentCursorAsync(triggeredByHotkey: false, cancellationToken);

        store.StatusMessage.Should().Be("Captured the current cursor position from the harness.");
        store.StatusSeverity.Should().Be(InfoBarSeverity.Success);
    }

    [Fact]
    public async Task MouseSmokeRequiresConfirmationBeforeStarting()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = CreateStore();
        store.SelectedCoordinate = new ScreenPoint(90, 120);
        var engine = new RecordingEngine
        {
            AutoCompleteResult = new RunResult(RunOutcome.TimerElapsed, 1, 2, TimeSpan.FromMilliseconds(10)),
        };
        var prompt = new FakeMouseSmokePrompt { ConfirmResult = false };
        var delays = new RecordingDelayScheduler();
        using var coordinator = CreateCoordinator(store, clickerEngine: engine, mouseSmokePrompt: prompt, delayScheduler: delays);

        await coordinator.StartMouseSmokeAsync(cancellationToken);

        prompt.Points.Should().ContainSingle().Which.Should().Be(new ScreenPoint(90, 120));
        delays.Delays.Should().BeEmpty();
        engine.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task MouseSmokeCountdownRunsBeforeTheRealClick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = CreateStore();
        store.SelectedCoordinate = new ScreenPoint(90, 120);
        var engine = new RecordingEngine
        {
            AutoCompleteResult = new RunResult(RunOutcome.TimerElapsed, 1, 2, TimeSpan.FromMilliseconds(10)),
        };
        var prompt = new FakeMouseSmokePrompt { ConfirmResult = true };
        var delays = new RecordingDelayScheduler();
        using var coordinator = CreateCoordinator(store, clickerEngine: engine, mouseSmokePrompt: prompt, delayScheduler: delays);

        await coordinator.StartMouseSmokeAsync(cancellationToken);

        delays.Delays.Should().HaveCount(3);
        delays.Delays.Should().OnlyContain(delay => delay == TimeSpan.FromSeconds(1));
        store.LogText.Should().Contain("Mouse smoke countdown: 3");
        store.LogText.Should().Contain("Mouse smoke countdown: 1");
        engine.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task PickerCancellationKeepsThePreviousCoordinateAndShowsAFriendlyMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = CreateStore();
        store.SelectedCoordinate = new ScreenPoint(90, 120);
        var picker = new FakeCoordinatePickerService
        {
            Result = CoordinatePickerResult.Cancelled(),
        };
        using var coordinator = CreateCoordinator(store, coordinatePickerService: picker);
        var viewModel = new MainViewModel(store, coordinator);
        viewModel.ActionComposer.BeginAddMouseClickCommand.Execute(null);
        store.SelectedCoordinate = new ScreenPoint(90, 120);

        await coordinator.PickCoordinateAsync(cancellationToken);

        store.SelectedCoordinate.Should().Be(new ScreenPoint(90, 120));
        store.StatusMessage.Should().Be("Coordinate pick was cancelled. The previous point is still selected.");
    }

    [Fact]
    public void AppEnvironmentSummaryMentionsThePhase6Shell()
    {
        AppEnvironment.Summary.Should().Contain("Phase 6");
        AppEnvironment.Summary.Should().Contain("RunescapeClicker.Core");
        AppEnvironment.Summary.Should().Contain("RunescapeClicker.Automation.Windows");
        AppEnvironment.Summary.Should().Contain("friendly Windows failure guidance");
    }

    private static AppSessionStore CreateStore() => new();

    private static MainViewModel CreateMainViewModel(
        AppSessionStore? store = null,
        IKeyboardKeyMetadataService? keyMetadataService = null)
    {
        var sessionStore = store ?? CreateStore();
        return new MainViewModel(sessionStore, CreateCoordinator(sessionStore), keyMetadataService);
    }

    private static RunCoordinator CreateCoordinator(
        AppSessionStore? store = null,
        IClickerEngine? clickerEngine = null,
        IInputAdapter? inputAdapter = null,
        IHotkeyService? hotkeyService = null,
        ICoordinatePickerService? coordinatePickerService = null,
        IUiDispatcher? dispatcher = null,
        IMouseSmokePrompt? mouseSmokePrompt = null,
        IAsyncDelayScheduler? delayScheduler = null)
    {
        var sessionStore = store ?? CreateStore();
        var adapter = inputAdapter ?? new FakeInputAdapter();
        return new RunCoordinator(
            sessionStore,
            clickerEngine ?? new RecordingEngine(),
            adapter,
            hotkeyService ?? new FakeHotkeyService(),
            coordinatePickerService ?? new FakeCoordinatePickerService(),
            dispatcher ?? new ImmediateDispatcher(),
            mouseSmokePrompt ?? new FakeMouseSmokePrompt(),
            delayScheduler ?? new RecordingDelayScheduler());
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInputAdapter : IInputAdapter
    {
        public CursorLocationResult CursorLocation { get; init; } = CursorLocationResult.Success(default);

        public ValueTask<InputAdapterResult> ConnectAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(InputAdapterResult.Success());

        public ValueTask<CursorLocationResult> GetCursorPositionAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(CursorLocation);

        public ValueTask<InputAdapterResult> MoveMouseAsync(ScreenPoint position, CancellationToken cancellationToken)
            => ValueTask.FromResult(InputAdapterResult.Success());

        public ValueTask<InputAdapterResult> ClickMouseAsync(MouseButtonKind button, CancellationToken cancellationToken)
            => ValueTask.FromResult(InputAdapterResult.Success());

        public ValueTask<InputAdapterResult> PressKeyAsync(KeyPressAction action, CancellationToken cancellationToken)
            => ValueTask.FromResult(InputAdapterResult.Success());
    }

    private sealed class FakeHotkeyService : IHotkeyService
    {
        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

        public bool IsRegistered => RegistrationResult.Succeeded;

        public int EnsureRegisteredCalls { get; private set; }

        public HotkeyRegistrationResult RegistrationResult { get; set; } = HotkeyRegistrationResult.Success();

        public Task<HotkeyRegistrationResult> EnsureRegisteredAsync(CancellationToken cancellationToken)
        {
            EnsureRegisteredCalls++;
            return Task.FromResult(RegistrationResult);
        }

        public void Raise(AutomationHotkey hotkey)
            => HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(hotkey, DateTimeOffset.UtcNow));

        public void Dispose()
        {
        }
    }

    private sealed class FakeCoordinatePickerService : ICoordinatePickerService
    {
        public CoordinatePickerResult Result { get; set; } = CoordinatePickerResult.Captured(new ScreenPoint(1, 2));

        public Task<CoordinatePickerResult> PickCoordinateAsync(CancellationToken cancellationToken)
            => Task.FromResult(Result);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingEngine : IClickerEngine
    {
        private TaskCompletionSource<RunResult>? _completion;

        public List<RunRequest> Requests { get; } = [];

        public RunResult? AutoCompleteResult { get; init; }

        public Task<RunResult> ExecuteAsync(RunRequest request, IProgress<RunEvent>? progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (AutoCompleteResult is RunResult result)
            {
                return Task.FromResult(result);
            }

            _completion = new TaskCompletionSource<RunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _completion.Task;
        }

        public void Complete(RunResult result)
            => _completion?.TrySetResult(result);
    }

    private sealed class FakeMouseSmokePrompt : IMouseSmokePrompt
    {
        public bool ConfirmResult { get; init; } = true;

        public List<ScreenPoint> Points { get; } = [];

        public Task<bool> ConfirmAsync(ScreenPoint point, CancellationToken cancellationToken)
        {
            Points.Add(point);
            return Task.FromResult(ConfirmResult);
        }
    }

    private sealed class RecordingDelayScheduler : IAsyncDelayScheduler
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKeyboardKeyMetadataService : IKeyboardKeyMetadataService
    {
        public bool ShouldSucceed { get; init; } = true;

        public NormalizedKeyboardKey Result { get; init; } = new("Space", 0x20, 0x39, false);

        public List<VirtualKey> RequestedKeys { get; } = [];

        public bool TryCreate(VirtualKey key, out NormalizedKeyboardKey metadata)
        {
            RequestedKeys.Add(key);
            metadata = Result;
            return ShouldSucceed;
        }
    }
}
