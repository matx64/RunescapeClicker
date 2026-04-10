using System.Diagnostics;

namespace RunescapeClicker.Core;

public sealed class ClickerEngine : IClickerEngine
{
    private readonly IInputAdapter _inputAdapter;
    private readonly IExecutionRuntime _runtime;

    public ClickerEngine(IInputAdapter inputAdapter)
        : this(inputAdapter, new SystemExecutionRuntime())
    {
    }

    internal ClickerEngine(IInputAdapter inputAdapter, IExecutionRuntime runtime)
    {
        _inputAdapter = inputAdapter ?? throw new ArgumentNullException(nameof(inputAdapter));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public async Task<RunResult> ExecuteAsync(
        RunRequest request,
        IProgress<RunEvent>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _runtime.Reset();

        progress?.Report(new RunStartedEvent(
            request.Actions,
            request.StopCondition,
            request.ExecutionProfile,
            _runtime.Elapsed));

        if (request.Actions.Count == 0)
        {
            return ReportAndReturn(progress, RunOutcome.EmptyRequest, 0, 0);
        }

        var connectResult = await _inputAdapter.ConnectAsync(cancellationToken);
        if (!connectResult.Succeeded)
        {
            return ReportAndReturn(
                progress,
                RunOutcome.Faulted,
                0,
                0,
                new EngineError(
                    EngineErrorCode.InputAdapterUnavailable,
                    $"Failed to start the input backend: {connectResult.FailureMessage ?? "Unknown input adapter error."}"));
        }

        var iterationsStarted = 0;
        var actionsCompleted = 0;

        while (true)
        {
            if (TryGetStopOutcome(request.StopCondition, cancellationToken, out var stopOutcome))
            {
                return ReportAndReturn(progress, stopOutcome, iterationsStarted, actionsCompleted);
            }

            iterationsStarted++;
            progress?.Report(new IterationStartedEvent(iterationsStarted, _runtime.Elapsed));

            for (var actionIndex = 0; actionIndex < request.Actions.Count; actionIndex++)
            {
                if (TryGetStopOutcome(request.StopCondition, cancellationToken, out stopOutcome))
                {
                    return ReportAndReturn(progress, stopOutcome, iterationsStarted, actionsCompleted);
                }

                var action = request.Actions[actionIndex];
                progress?.Report(new ActionStartedEvent(iterationsStarted, actionIndex, action, _runtime.Elapsed));

                var actionResult = action switch
                {
                    MouseClickAction mouseClickAction => await ExecuteMouseClickAsync(
                        mouseClickAction,
                        actionIndex,
                        request.ExecutionProfile,
                        request.StopCondition,
                        cancellationToken),
                    KeyPressAction keyPressAction => await ExecuteKeyPressAsync(
                        keyPressAction,
                        actionIndex,
                        request.ExecutionProfile,
                        request.StopCondition,
                        cancellationToken),
                    DelayAction delayAction => await ExecuteDelayAsync(
                        delayAction,
                        request.ExecutionProfile,
                        request.StopCondition,
                        cancellationToken),
                    _ => throw new InvalidOperationException($"Unsupported action type '{action.GetType().Name}'."),
                };

                if (actionResult.ShouldStop)
                {
                    return ReportAndReturn(
                        progress,
                        actionResult.Outcome ?? ResolveStopOutcome(request.StopCondition, cancellationToken),
                        iterationsStarted,
                        actionsCompleted,
                        actionResult.Error);
                }

                actionsCompleted++;
                progress?.Report(new ActionCompletedEvent(iterationsStarted, actionIndex, action, _runtime.Elapsed));
            }
        }
    }

    private async ValueTask<ActionExecutionResult> ExecuteMouseClickAsync(
        MouseClickAction action,
        int actionIndex,
        ExecutionProfile profile,
        StopCondition stopCondition,
        CancellationToken cancellationToken)
    {
        if (!await SleepAsync(_runtime.NextAntiDetectDelay(profile), profile, stopCondition, cancellationToken))
        {
            return ActionExecutionResult.Stop();
        }

        var moveResult = await MoveMouseHumanLikeAsync(action, actionIndex, profile, stopCondition, cancellationToken);
        if (moveResult.ShouldStop)
        {
            return moveResult;
        }

        var clickResult = await _inputAdapter.ClickMouseAsync(action.Button, cancellationToken);
        if (!clickResult.Succeeded)
        {
            return ActionExecutionResult.Fault(new EngineError(
                EngineErrorCode.MouseClickFailed,
                $"Failed to click the mouse: {clickResult.FailureMessage ?? "Unknown mouse click failure."}",
                actionIndex));
        }

        return ActionExecutionResult.Continue();
    }

    private async ValueTask<ActionExecutionResult> ExecuteKeyPressAsync(
        KeyPressAction action,
        int actionIndex,
        ExecutionProfile profile,
        StopCondition stopCondition,
        CancellationToken cancellationToken)
    {
        if (!await SleepAsync(_runtime.NextAntiDetectDelay(profile), profile, stopCondition, cancellationToken))
        {
            return ActionExecutionResult.Stop();
        }

        var keyResult = await _inputAdapter.PressKeyAsync(action, cancellationToken);
        if (!keyResult.Succeeded)
        {
            return ActionExecutionResult.Fault(new EngineError(
                EngineErrorCode.KeyPressFailed,
                $"Failed to press the key '{action.DisplayLabel}': {keyResult.FailureMessage ?? "Unknown key press failure."}",
                actionIndex));
        }

        return ActionExecutionResult.Continue();
    }

    private async ValueTask<ActionExecutionResult> ExecuteDelayAsync(
        DelayAction action,
        ExecutionProfile profile,
        StopCondition stopCondition,
        CancellationToken cancellationToken)
    {
        var totalDelay = action.Duration + _runtime.NextDelayJitter(profile);
        if (!await SleepAsync(totalDelay, profile, stopCondition, cancellationToken))
        {
            return ActionExecutionResult.Stop();
        }

        return ActionExecutionResult.Continue();
    }

    private async ValueTask<ActionExecutionResult> MoveMouseHumanLikeAsync(
        MouseClickAction action,
        int actionIndex,
        ExecutionProfile profile,
        StopCondition stopCondition,
        CancellationToken cancellationToken)
    {
        var locationResult = await _inputAdapter.GetCursorPositionAsync(cancellationToken);
        if (!locationResult.Succeeded)
        {
            var directMoveResult = await _inputAdapter.MoveMouseAsync(action.Position, cancellationToken);
            if (!directMoveResult.Succeeded)
            {
                return ActionExecutionResult.Fault(new EngineError(
                    EngineErrorCode.MouseMoveFailed,
                    $"Failed to move the mouse: {directMoveResult.FailureMessage ?? "Unknown mouse movement failure."}",
                    actionIndex));
            }

            if (!await SleepAsync(_runtime.NextPostMoveClickDelay(profile), profile, stopCondition, cancellationToken))
            {
                return ActionExecutionResult.Stop();
            }

            return ActionExecutionResult.Continue();
        }

        var start = locationResult.Position;
        var target = action.Position;
        var dx = (double)(target.X - start.X);
        var dy = (double)(target.Y - start.Y);
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance == 0)
        {
            if (!await SleepAsync(_runtime.NextPostMoveClickDelay(profile), profile, stopCondition, cancellationToken))
            {
                return ActionExecutionResult.Stop();
            }

            return ActionExecutionResult.Continue();
        }

        var totalDuration = CalculateMovementDuration(profile, distance);
        var stepCount = CalculateMovementSteps(profile, distance);
        var driftCap = Math.Min(profile.HumanMoveMaximumDriftPixels, distance * profile.HumanMoveDriftRatio);
        var driftMagnitude = driftCap * _runtime.NextMovementCurveFactor(profile);
        var normalX = -dy / distance;
        var normalY = dx / distance;
        var minX = (double)Math.Min(start.X, target.X);
        var maxX = (double)Math.Max(start.X, target.X);
        var minY = (double)Math.Min(start.Y, target.Y);
        var maxY = (double)Math.Max(start.Y, target.Y);
        var previous = start;

        for (var step = 1; step <= stepCount; step++)
        {
            ScreenPoint next;
            if (step == stepCount)
            {
                next = target;
            }
            else
            {
                var t = step / (double)stepCount;
                var easedT = EaseInOut(t);
                var taper = Math.Sin(Math.PI * t);
                var drift = driftMagnitude * taper;
                var rawX = start.X + (dx * easedT) + (normalX * drift);
                var rawY = start.Y + (dy * easedT) + (normalY * drift);
                next = new ScreenPoint(
                    (int)Math.Round(Math.Clamp(rawX, minX, maxX)),
                    (int)Math.Round(Math.Clamp(rawY, minY, maxY)));
            }

            if (next != previous)
            {
                var moveResult = await _inputAdapter.MoveMouseAsync(next, cancellationToken);
                if (!moveResult.Succeeded)
                {
                    return ActionExecutionResult.Fault(new EngineError(
                        EngineErrorCode.MouseMoveFailed,
                        $"Failed to move the mouse: {moveResult.FailureMessage ?? "Unknown mouse movement failure."}",
                        actionIndex));
                }

                previous = next;
            }

            if (step < stepCount)
            {
                var stepDelay = CalculateStepDelay(totalDuration, step, stepCount);
                if (!await SleepAsync(stepDelay, profile, stopCondition, cancellationToken))
                {
                    return ActionExecutionResult.Stop();
                }
            }
        }

        if (!await SleepAsync(_runtime.NextPostMoveClickDelay(profile), profile, stopCondition, cancellationToken))
        {
            return ActionExecutionResult.Stop();
        }

        return ActionExecutionResult.Continue();
    }

    private RunResult ReportAndReturn(
        IProgress<RunEvent>? progress,
        RunOutcome outcome,
        int iterationsStarted,
        int actionsCompleted,
        EngineError? error = null)
    {
        var result = new RunResult(outcome, iterationsStarted, actionsCompleted, _runtime.Elapsed, error);
        progress?.Report(new RunEndedEvent(result, result.Elapsed));
        return result;
    }

    private bool TryGetStopOutcome(
        StopCondition stopCondition,
        CancellationToken cancellationToken,
        out RunOutcome outcome)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            outcome = RunOutcome.Stopped;
            return true;
        }

        if (stopCondition is TimerStopCondition timerStopCondition && _runtime.Elapsed >= timerStopCondition.Duration)
        {
            outcome = RunOutcome.TimerElapsed;
            return true;
        }

        outcome = default;
        return false;
    }

    private RunOutcome ResolveStopOutcome(StopCondition stopCondition, CancellationToken cancellationToken)
        => stopCondition is TimerStopCondition timerStopCondition && _runtime.Elapsed >= timerStopCondition.Duration && !cancellationToken.IsCancellationRequested
            ? RunOutcome.TimerElapsed
            : RunOutcome.Stopped;

    private async ValueTask<bool> SleepAsync(
        TimeSpan duration,
        ExecutionProfile profile,
        StopCondition stopCondition,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            return !TryGetStopOutcome(stopCondition, cancellationToken, out _);
        }

        return await _runtime.SleepAsync(
            duration,
            profile.SleepPollInterval,
            () => TryGetStopOutcome(stopCondition, cancellationToken, out _),
            cancellationToken);
    }

    private static double EaseInOut(double t) => 0.5 - (0.5 * Math.Cos(Math.PI * t));

    private static TimeSpan CalculateMovementDuration(ExecutionProfile profile, double distance)
    {
        var unclampedMilliseconds = profile.HumanMoveMinimumDuration.TotalMilliseconds + (distance * profile.HumanMoveMillisecondsPerPixel);
        var clampedMilliseconds = Math.Clamp(
            Math.Round(unclampedMilliseconds),
            profile.HumanMoveMinimumDuration.TotalMilliseconds,
            profile.HumanMoveMaximumDuration.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(clampedMilliseconds);
    }

    private static int CalculateMovementSteps(ExecutionProfile profile, double distance)
    {
        var unclampedSteps = profile.HumanMoveMinimumSteps + (distance / profile.HumanMovePixelsPerAdditionalStep);
        return (int)Math.Clamp(
            Math.Round(unclampedSteps),
            profile.HumanMoveMinimumSteps,
            profile.HumanMoveMaximumSteps);
    }

    private static TimeSpan CalculateStepDelay(TimeSpan totalDuration, int stepIndex, int stepCount)
    {
        if (stepCount <= 1)
        {
            return TimeSpan.Zero;
        }

        var sleepCount = stepCount - 1L;
        var baseTicks = totalDuration.Ticks / sleepCount;
        var remainder = totalDuration.Ticks % sleepCount;
        var extraTicks = stepIndex - 1 < remainder ? 1L : 0L;
        return TimeSpan.FromTicks(baseTicks + extraTicks);
    }

    internal readonly record struct ActionExecutionResult(bool ShouldStop, RunOutcome? Outcome = null, EngineError? Error = null)
    {
        public static ActionExecutionResult Continue() => new(false);

        public static ActionExecutionResult Stop() => new(true);

        public static ActionExecutionResult Fault(EngineError error) => new(true, RunOutcome.Faulted, error);
    }

    internal interface IExecutionRuntime
    {
        void Reset();

        TimeSpan Elapsed { get; }

        TimeSpan NextAntiDetectDelay(ExecutionProfile profile);

        TimeSpan NextDelayJitter(ExecutionProfile profile);

        double NextMovementCurveFactor(ExecutionProfile profile);

        TimeSpan NextPostMoveClickDelay(ExecutionProfile profile);

        ValueTask<bool> SleepAsync(
            TimeSpan duration,
            TimeSpan pollInterval,
            Func<bool> shouldStop,
            CancellationToken cancellationToken);
    }

    internal sealed class SystemExecutionRuntime : IExecutionRuntime
    {
        private readonly Stopwatch _stopwatch = new();
        private readonly Random _random = Random.Shared;

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public void Reset()
        {
            _stopwatch.Restart();
        }

        public TimeSpan NextAntiDetectDelay(ExecutionProfile profile)
            => NextDuration(profile.AntiDetectMinimumDelay, profile.AntiDetectMaximumDelay);

        public TimeSpan NextDelayJitter(ExecutionProfile profile)
            => NextDuration(TimeSpan.Zero, profile.DelayJitterMaximum);

        public double NextMovementCurveFactor(ExecutionProfile profile)
            => NextDouble(profile.MovementCurveFactorMinimum, profile.MovementCurveFactorMaximum);

        public TimeSpan NextPostMoveClickDelay(ExecutionProfile profile)
            => NextDuration(profile.PostMoveClickMinimumDelay, profile.PostMoveClickMaximumDelay);

        public async ValueTask<bool> SleepAsync(
            TimeSpan duration,
            TimeSpan pollInterval,
            Func<bool> shouldStop,
            CancellationToken cancellationToken)
        {
            var deadline = _stopwatch.Elapsed + duration;

            while (_stopwatch.Elapsed < deadline)
            {
                if (shouldStop())
                {
                    return false;
                }

                var remaining = deadline - _stopwatch.Elapsed;
                var nextDelay = remaining < pollInterval ? remaining : pollInterval;

                try
                {
                    await Task.Delay(nextDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return !shouldStop();
        }

        private TimeSpan NextDuration(TimeSpan minimum, TimeSpan maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            var minimumMilliseconds = (long)Math.Round(minimum.TotalMilliseconds);
            var maximumMilliseconds = (long)Math.Round(maximum.TotalMilliseconds);
            var value = _random.NextInt64(minimumMilliseconds, maximumMilliseconds + 1);
            return TimeSpan.FromMilliseconds(value);
        }

        private double NextDouble(double minimum, double maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            return minimum + (_random.NextDouble() * (maximum - minimum));
        }
    }
}
