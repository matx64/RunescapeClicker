namespace RunescapeClicker.Core;

public interface IClickerEngine
{
    Task<RunResult> ExecuteAsync(
        RunRequest request,
        IProgress<RunEvent>? progress,
        CancellationToken cancellationToken);
}

