namespace RunescapeClicker.App;

public interface IAsyncDelayScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
