using FluentAssertions;
using RunescapeClicker.Core;

namespace RunescapeClicker.Core.Tests;

public sealed class ClickerEngineTests
{
    [Fact]
    public async Task SuccessfulSequenceRecordsExpectedOperationsDelaysAndEvents()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(30)),
            DelayJitters = CreateQueue(TimeSpan.Zero),
            MovementCurveFactors = CreateQueue(0.35),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(32)),
        };
        var adapter = new FakeInputAdapter();
        var engine = new ClickerEngine(adapter, runtime);
        var progress = new RecordingProgress();

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [
                    new MouseClickAction(MouseButtonKind.Left, new ScreenPoint(10, 20)),
                    CreateKeyAction("space"),
                    new DelayAction(TimeSpan.FromMilliseconds(900)),
                ],
                StopCondition.Timer(TimeSpan.FromSeconds(1)),
                ExecutionProfile.Default),
            progress,
            CancellationToken.None);

        adapter.Operations.Should().HaveCountGreaterThan(3);
        adapter.Operations[^1].Should().Be("key:space");
        adapter.Operations[^2].Should().Be("click:left");
        adapter.Operations[^3].Should().Be("move:10:20");
        adapter.Operations[..^2].Should().OnlyContain(operation => operation.StartsWith("move:", StringComparison.Ordinal));

        runtime.SleepLog.Should().HaveCountGreaterThan(4);
        runtime.SleepLog[0].Should().Be(TimeSpan.FromMilliseconds(25));
        runtime.SleepLog.Should().Contain(TimeSpan.FromMilliseconds(32));
        runtime.SleepLog[^1].Should().Be(TimeSpan.FromMilliseconds(900));

        result.Outcome.Should().Be(RunOutcome.TimerElapsed);
        result.Error.Should().BeNull();
        progress.Events.OfType<RunStartedEvent>().Should().ContainSingle();
        progress.Events.OfType<RunEndedEvent>().Should().ContainSingle(ended => ended.Result.Outcome == RunOutcome.TimerElapsed);
    }

    [Fact]
    public async Task BackendConnectFailureReportsStructuredError()
    {
        var runtime = new FakeExecutionRuntime();
        var adapter = new FakeInputAdapter
        {
            ConnectResult = InputAdapterResult.Failure(InputFailureKind.Unknown, "backend unavailable"),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [new DelayAction(TimeSpan.FromMilliseconds(50))],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        result.Outcome.Should().Be(RunOutcome.Faulted);
        result.Error.Should().BeEquivalentTo(new EngineError(
            EngineErrorCode.InputAdapterUnavailable,
            "Failed to start the input backend: backend unavailable",
            FailureKind: InputFailureKind.Unknown));
        adapter.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task MouseMoveFailureIsReported()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(100, 100),
            MoveResult = InputAdapterResult.Failure(InputFailureKind.BlockedByWindows, "cursor locked"),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [new MouseClickAction(MouseButtonKind.Right, new ScreenPoint(12, 18))],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        result.Outcome.Should().Be(RunOutcome.Faulted);
        result.Error.Should().BeEquivalentTo(new EngineError(
            EngineErrorCode.MouseMoveFailed,
            "Failed to move the mouse: cursor locked",
            0,
            InputFailureKind.BlockedByWindows));
        adapter.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task MouseClickFailureIsReportedAfterMove()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
            MovementCurveFactors = CreateQueue(0.0),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(22)),
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(90, 120),
            ClickResult = InputAdapterResult.Failure(InputFailureKind.PartialInjection, "button jammed"),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [new MouseClickAction(MouseButtonKind.Left, new ScreenPoint(3, 7))],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        result.Outcome.Should().Be(RunOutcome.Faulted);
        result.Error.Should().BeEquivalentTo(new EngineError(
            EngineErrorCode.MouseClickFailed,
            "Failed to click the mouse: button jammed",
            0,
            InputFailureKind.PartialInjection));
        adapter.Operations.Should().HaveCountGreaterThan(1);
        adapter.Operations[^1].Should().Be("move:3:7");
        adapter.Operations.Should().OnlyContain(operation => operation.StartsWith("move:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeyPressFailureIsReported()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(5)),
        };
        var adapter = new FakeInputAdapter
        {
            KeyResult = InputAdapterResult.Failure(InputFailureKind.ElevatedTarget, "key blocked"),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [CreateKeyAction("enter")],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        result.Outcome.Should().Be(RunOutcome.Faulted);
        result.Error.Should().BeEquivalentTo(new EngineError(
            EngineErrorCode.KeyPressFailed,
            "Failed to press the key 'enter': key blocked",
            0,
            InputFailureKind.ElevatedTarget));
        adapter.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task StopBetweenActionsPreventsTheNextActionFromRunning()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10)),
            StopOnSleepCall = 2,
        };
        var adapter = new FakeInputAdapter();
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [CreateKeyAction("space"), CreateKeyAction("enter")],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().Equal("key:space");
        result.Outcome.Should().Be(RunOutcome.Stopped);
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task TimerStopEndsDuringDelayBeforeLaterActions()
    {
        var runtime = new FakeExecutionRuntime
        {
            DelayJitters = CreateQueue(TimeSpan.Zero),
        };
        var adapter = new FakeInputAdapter();
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [new DelayAction(TimeSpan.FromMilliseconds(1200)), CreateKeyAction("space")],
                StopCondition.Timer(TimeSpan.FromSeconds(1)),
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().BeEmpty();
        runtime.SleepLog.Should().Equal(TimeSpan.FromMilliseconds(1200));
        result.Outcome.Should().Be(RunOutcome.TimerElapsed);
    }

    [Fact]
    public async Task EmptyActionListExitsImmediately()
    {
        var runtime = new FakeExecutionRuntime();
        var adapter = new FakeInputAdapter();
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest([], StopCondition.HotkeyOnly, ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().BeEmpty();
        runtime.SleepLog.Should().BeEmpty();
        result.Outcome.Should().Be(RunOutcome.EmptyRequest);
    }

    [Fact]
    public async Task ExecuteAsyncResetsRuntimeElapsedBeforeEachRun()
    {
        var runtime = new FakeExecutionRuntime
        {
            Elapsed = TimeSpan.FromSeconds(5),
        };
        var adapter = new FakeInputAdapter();
        var engine = new ClickerEngine(adapter, runtime);
        var progress = new RecordingProgress();

        var result = await engine.ExecuteAsync(
            new RunRequest([], StopCondition.HotkeyOnly, ExecutionProfile.Default),
            progress,
            CancellationToken.None);

        progress.Events.OfType<RunStartedEvent>().Should().ContainSingle(started => started.Elapsed == TimeSpan.Zero);
        result.Elapsed.Should().Be(TimeSpan.Zero);
        runtime.ResetCalls.Should().Be(1);
    }

    [Fact]
    public async Task MouseClickUsesInterpolatedMovementBeforeClick()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(15)),
            DelayJitters = CreateQueue(TimeSpan.Zero),
            MovementCurveFactors = CreateQueue(0.25),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(30)),
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(0, 0),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [
                    new MouseClickAction(MouseButtonKind.Left, new ScreenPoint(120, 80)),
                    new DelayAction(TimeSpan.FromMilliseconds(900)),
                ],
                StopCondition.Timer(TimeSpan.FromSeconds(1)),
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().HaveCountGreaterThan(2);
        adapter.Operations[^1].Should().Be("click:left");
        adapter.Operations[^2].Should().Be("move:120:80");
        adapter.Operations[..^1].Should().OnlyContain(operation => operation.StartsWith("move:", StringComparison.Ordinal));

        runtime.SleepLog.Should().HaveCountGreaterThan(3);
        runtime.SleepLog[0].Should().Be(TimeSpan.FromMilliseconds(15));
        runtime.SleepLog.Should().Contain(TimeSpan.FromMilliseconds(30));
        runtime.SleepLog[^1].Should().Be(TimeSpan.FromMilliseconds(900));
        result.Outcome.Should().Be(RunOutcome.TimerElapsed);
    }

    [Fact]
    public async Task InterpolatedMouseMovementStaysWithinStartTargetBounds()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
            MovementCurveFactors = CreateQueue(1.0),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(25)),
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(3, 200),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [
                    new MouseClickAction(MouseButtonKind.Left, new ScreenPoint(3, 500)),
                    new DelayAction(TimeSpan.FromMilliseconds(1000)),
                ],
                StopCondition.Timer(TimeSpan.FromSeconds(1)),
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().HaveCountGreaterThan(1);
        adapter.Operations[^1].Should().Be("click:left");
        adapter.Operations[..^1].Should().OnlyContain(operation => operation.StartsWith("move:3:", StringComparison.Ordinal));
        result.Outcome.Should().Be(RunOutcome.TimerElapsed);
    }

    [Fact]
    public async Task StopDuringMouseMovementAbortsBeforeClick()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
            MovementCurveFactors = CreateQueue(0.0),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(28)),
            StopOnSleepCall = 2,
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(0, 0),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [new MouseClickAction(MouseButtonKind.Right, new ScreenPoint(200, 50))],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().NotBeEmpty();
        adapter.Operations.Should().OnlyContain(operation => operation.StartsWith("move:", StringComparison.Ordinal));
        result.Outcome.Should().Be(RunOutcome.Stopped);
    }

    [Fact]
    public async Task MouseLocationFailureFallsBackToSingleMoveAndClick()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
            DelayJitters = CreateQueue(TimeSpan.Zero),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(27)),
        };
        var adapter = new FakeInputAdapter
        {
            CursorLocationResult = CursorLocationResult.Failure(InputFailureKind.CursorReadUnavailable, "unavailable"),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [
                    new MouseClickAction(MouseButtonKind.Right, new ScreenPoint(50, 60)),
                    new DelayAction(TimeSpan.FromMilliseconds(1000)),
                ],
                StopCondition.Timer(TimeSpan.FromSeconds(1)),
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().Equal("move:50:60", "click:right");
        runtime.SleepLog.Should().Equal(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(27),
            TimeSpan.FromMilliseconds(1000));
        result.Outcome.Should().Be(RunOutcome.TimerElapsed);
    }

    [Fact]
    public async Task ClickingSamePositionWaitsBeforeClick()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
            DelayJitters = CreateQueue(TimeSpan.Zero),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(31)),
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(50, 60),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [
                    new MouseClickAction(MouseButtonKind.Left, new ScreenPoint(50, 60)),
                    new DelayAction(TimeSpan.FromMilliseconds(1000)),
                ],
                StopCondition.Timer(TimeSpan.FromSeconds(1)),
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().Equal("click:left");
        runtime.SleepLog.Should().Equal(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(31),
            TimeSpan.FromMilliseconds(1000));
        result.Outcome.Should().Be(RunOutcome.TimerElapsed);
    }

    [Fact]
    public async Task StopDuringPostMoveClickDelayAbortsBeforeClick()
    {
        var runtime = new FakeExecutionRuntime
        {
            AntiDetectDelays = CreateQueue(TimeSpan.FromMilliseconds(10)),
            DelayJitters = CreateQueue(TimeSpan.Zero),
            PostMoveClickDelays = CreateQueue(TimeSpan.FromMilliseconds(35)),
            StopOnSleepCall = 2,
        };
        var adapter = new FakeInputAdapter
        {
            CurrentLocation = new ScreenPoint(50, 60),
        };
        var engine = new ClickerEngine(adapter, runtime);

        var result = await engine.ExecuteAsync(
            new RunRequest(
                [new MouseClickAction(MouseButtonKind.Left, new ScreenPoint(50, 60))],
                StopCondition.HotkeyOnly,
                ExecutionProfile.Default),
            progress: null,
            CancellationToken.None);

        adapter.Operations.Should().BeEmpty();
        runtime.SleepLog.Should().Equal(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(35));
        result.Outcome.Should().Be(RunOutcome.Stopped);
    }

    private static Queue<TimeSpan> CreateQueue(params TimeSpan[] values) => new(values);

    private static Queue<double> CreateQueue(params double[] values) => new(values);

    private static KeyPressAction CreateKeyAction(string label)
        => new(virtualKey: 1, scanCode: 1, isExtendedKey: false, displayLabel: label);

    private sealed class FakeInputAdapter : IInputAdapter
    {
        public InputAdapterResult ConnectResult { get; init; } = InputAdapterResult.Success();

        public CursorLocationResult CursorLocationResult { get; init; } = CursorLocationResult.Success(default);

        public InputAdapterResult MoveResult { get; init; } = InputAdapterResult.Success();

        public InputAdapterResult ClickResult { get; init; } = InputAdapterResult.Success();

        public InputAdapterResult KeyResult { get; init; } = InputAdapterResult.Success();

        public List<string> Operations { get; } = [];

        public ScreenPoint CurrentLocation { get; set; }

        public ValueTask<InputAdapterResult> ConnectAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(ConnectResult);

        public ValueTask<CursorLocationResult> GetCursorPositionAsync(CancellationToken cancellationToken)
        {
            if (CursorLocationResult.Succeeded)
            {
                return ValueTask.FromResult(CursorLocationResult.Success(CurrentLocation));
            }

            return ValueTask.FromResult(CursorLocationResult);
        }

        public ValueTask<InputAdapterResult> MoveMouseAsync(ScreenPoint position, CancellationToken cancellationToken)
        {
            if (!MoveResult.Succeeded)
            {
                return ValueTask.FromResult(MoveResult);
            }

            CurrentLocation = position;
            Operations.Add($"move:{position.X}:{position.Y}");
            return ValueTask.FromResult(MoveResult);
        }

        public ValueTask<InputAdapterResult> ClickMouseAsync(MouseButtonKind button, CancellationToken cancellationToken)
        {
            if (!ClickResult.Succeeded)
            {
                return ValueTask.FromResult(ClickResult);
            }

            Operations.Add($"click:{button.ToString().ToLowerInvariant()}");
            return ValueTask.FromResult(ClickResult);
        }

        public ValueTask<InputAdapterResult> PressKeyAsync(KeyPressAction action, CancellationToken cancellationToken)
        {
            if (!KeyResult.Succeeded)
            {
                return ValueTask.FromResult(KeyResult);
            }

            Operations.Add($"key:{action.DisplayLabel}");
            return ValueTask.FromResult(KeyResult);
        }
    }

    private sealed class RecordingProgress : IProgress<RunEvent>
    {
        public List<RunEvent> Events { get; } = [];

        public void Report(RunEvent value) => Events.Add(value);
    }

    private sealed class FakeExecutionRuntime : ClickerEngine.IExecutionRuntime
    {
        public Queue<TimeSpan> AntiDetectDelays { get; init; } = new();

        public Queue<TimeSpan> DelayJitters { get; init; } = new();

        public Queue<double> MovementCurveFactors { get; init; } = new();

        public Queue<TimeSpan> PostMoveClickDelays { get; init; } = new();

        public List<TimeSpan> SleepLog { get; } = [];

        public int? StopOnSleepCall { get; init; }

        public int SleepCalls { get; private set; }

        public int ResetCalls { get; private set; }

        public TimeSpan Elapsed { get; set; }

        public void Reset()
        {
            ResetCalls++;
            Elapsed = TimeSpan.Zero;
            SleepCalls = 0;
            SleepLog.Clear();
        }

        public TimeSpan NextAntiDetectDelay(ExecutionProfile profile)
            => AntiDetectDelays.Count > 0 ? AntiDetectDelays.Dequeue() : TimeSpan.Zero;

        public TimeSpan NextDelayJitter(ExecutionProfile profile)
            => DelayJitters.Count > 0 ? DelayJitters.Dequeue() : TimeSpan.Zero;

        public double NextMovementCurveFactor(ExecutionProfile profile)
            => MovementCurveFactors.Count > 0 ? MovementCurveFactors.Dequeue() : 0.0;

        public TimeSpan NextPostMoveClickDelay(ExecutionProfile profile)
            => PostMoveClickDelays.Count > 0 ? PostMoveClickDelays.Dequeue() : TimeSpan.Zero;

        public ValueTask<bool> SleepAsync(
            TimeSpan duration,
            TimeSpan pollInterval,
            Func<bool> shouldStop,
            CancellationToken cancellationToken)
        {
            if (shouldStop())
            {
                return ValueTask.FromResult(false);
            }

            SleepCalls++;
            SleepLog.Add(duration);

            if (StopOnSleepCall == SleepCalls)
            {
                return ValueTask.FromResult(false);
            }

            Elapsed += duration;
            return ValueTask.FromResult(!shouldStop());
        }
    }
}
