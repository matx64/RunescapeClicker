namespace RunescapeClicker.App;

public sealed class SystemAsyncDelayScheduler : IAsyncDelayScheduler
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
