namespace RunescapeClicker.Core;

public abstract record StopCondition
{
    public static StopCondition HotkeyOnly { get; } = new HotkeyOnlyStopCondition();

    public static StopCondition Timer(TimeSpan duration) => new TimerStopCondition(duration);
}

public sealed record HotkeyOnlyStopCondition : StopCondition;

public sealed record TimerStopCondition : StopCondition
{
    public TimerStopCondition(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Timer duration cannot be negative.");
        }

        Duration = duration;
    }

    public TimeSpan Duration { get; }
}
