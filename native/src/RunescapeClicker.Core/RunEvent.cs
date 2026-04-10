namespace RunescapeClicker.Core;

public abstract record RunEvent(TimeSpan Elapsed);

public sealed record RunStartedEvent(
    IReadOnlyList<AutomationAction> Actions,
    StopCondition StopCondition,
    ExecutionProfile Profile,
    TimeSpan Elapsed) : RunEvent(Elapsed);

public sealed record IterationStartedEvent(
    int IterationNumber,
    TimeSpan Elapsed) : RunEvent(Elapsed);

public sealed record ActionStartedEvent(
    int IterationNumber,
    int ActionIndex,
    AutomationAction Action,
    TimeSpan Elapsed) : RunEvent(Elapsed);

public sealed record ActionCompletedEvent(
    int IterationNumber,
    int ActionIndex,
    AutomationAction Action,
    TimeSpan Elapsed) : RunEvent(Elapsed);

public sealed record RunEndedEvent(RunResult Result, TimeSpan Elapsed) : RunEvent(Elapsed);
