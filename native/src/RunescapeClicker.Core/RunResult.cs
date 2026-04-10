namespace RunescapeClicker.Core;

public sealed record RunResult(
    RunOutcome Outcome,
    int IterationsStarted,
    int ActionsCompleted,
    TimeSpan Elapsed,
    EngineError? Error = null);

public enum RunOutcome
{
    Stopped = 0,
    TimerElapsed = 1,
    Faulted = 2,
    EmptyRequest = 3,
}

