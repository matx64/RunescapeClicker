namespace RunescapeClicker.Core;

public sealed record RunRequest
{
    public RunRequest(IEnumerable<AutomationAction> actions, StopCondition stopCondition, ExecutionProfile executionProfile)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(stopCondition);
        ArgumentNullException.ThrowIfNull(executionProfile);

        var materializedActions = actions.ToArray();
        if (materializedActions.Any(action => action is null))
        {
            throw new ArgumentException("Actions cannot contain null entries.", nameof(actions));
        }

        Actions = materializedActions;
        StopCondition = stopCondition;
        ExecutionProfile = executionProfile;
    }

    public IReadOnlyList<AutomationAction> Actions { get; }

    public StopCondition StopCondition { get; }

    public ExecutionProfile ExecutionProfile { get; }
}
