namespace RunescapeClicker.Core;

public sealed record ExecutionProfile
{
    public static ExecutionProfile Default { get; } = new(
        antiDetectMinimumDelay: TimeSpan.FromMilliseconds(20),
        antiDetectMaximumDelay: TimeSpan.FromMilliseconds(80),
        delayJitterMaximum: TimeSpan.FromMilliseconds(50),
        sleepPollInterval: TimeSpan.FromMilliseconds(10),
        humanMoveMinimumDuration: TimeSpan.FromMilliseconds(120),
        humanMoveMaximumDuration: TimeSpan.FromMilliseconds(340),
        humanMoveMinimumSteps: 14,
        humanMoveMaximumSteps: 36,
        humanMoveMillisecondsPerPixel: 0.22,
        humanMovePixelsPerAdditionalStep: 25.0,
        humanMoveMaximumDriftPixels: 8.0,
        humanMoveDriftRatio: 0.015,
        movementCurveFactorMinimum: -1.0,
        movementCurveFactorMaximum: 1.0,
        postMoveClickMinimumDelay: TimeSpan.FromMilliseconds(22),
        postMoveClickMaximumDelay: TimeSpan.FromMilliseconds(38));

    public ExecutionProfile(
        TimeSpan antiDetectMinimumDelay,
        TimeSpan antiDetectMaximumDelay,
        TimeSpan delayJitterMaximum,
        TimeSpan sleepPollInterval,
        TimeSpan humanMoveMinimumDuration,
        TimeSpan humanMoveMaximumDuration,
        int humanMoveMinimumSteps,
        int humanMoveMaximumSteps,
        double humanMoveMillisecondsPerPixel,
        double humanMovePixelsPerAdditionalStep,
        double humanMoveMaximumDriftPixels,
        double humanMoveDriftRatio,
        double movementCurveFactorMinimum,
        double movementCurveFactorMaximum,
        TimeSpan postMoveClickMinimumDelay,
        TimeSpan postMoveClickMaximumDelay)
    {
        ValidateNonNegative(antiDetectMinimumDelay, nameof(antiDetectMinimumDelay));
        ValidateRange(antiDetectMinimumDelay, antiDetectMaximumDelay, nameof(antiDetectMaximumDelay));
        ValidateNonNegative(delayJitterMaximum, nameof(delayJitterMaximum));
        ValidatePositive(sleepPollInterval, nameof(sleepPollInterval));
        ValidatePositive(humanMoveMinimumDuration, nameof(humanMoveMinimumDuration));
        ValidateRange(humanMoveMinimumDuration, humanMoveMaximumDuration, nameof(humanMoveMaximumDuration));

        if (humanMoveMinimumSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(humanMoveMinimumSteps), "Minimum steps must be positive.");
        }

        if (humanMoveMaximumSteps < humanMoveMinimumSteps)
        {
            throw new ArgumentOutOfRangeException(nameof(humanMoveMaximumSteps), "Maximum steps must be greater than or equal to minimum steps.");
        }

        ValidateNonNegative(humanMoveMillisecondsPerPixel, nameof(humanMoveMillisecondsPerPixel));

        if (humanMovePixelsPerAdditionalStep <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(humanMovePixelsPerAdditionalStep), "Pixels per additional step must be positive.");
        }

        ValidateNonNegative(humanMoveMaximumDriftPixels, nameof(humanMoveMaximumDriftPixels));
        ValidateNonNegative(humanMoveDriftRatio, nameof(humanMoveDriftRatio));

        if (movementCurveFactorMaximum < movementCurveFactorMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(movementCurveFactorMaximum), "Movement curve factor maximum must be greater than or equal to minimum.");
        }

        ValidateNonNegative(postMoveClickMinimumDelay, nameof(postMoveClickMinimumDelay));
        ValidateRange(postMoveClickMinimumDelay, postMoveClickMaximumDelay, nameof(postMoveClickMaximumDelay));

        AntiDetectMinimumDelay = antiDetectMinimumDelay;
        AntiDetectMaximumDelay = antiDetectMaximumDelay;
        DelayJitterMaximum = delayJitterMaximum;
        SleepPollInterval = sleepPollInterval;
        HumanMoveMinimumDuration = humanMoveMinimumDuration;
        HumanMoveMaximumDuration = humanMoveMaximumDuration;
        HumanMoveMinimumSteps = humanMoveMinimumSteps;
        HumanMoveMaximumSteps = humanMoveMaximumSteps;
        HumanMoveMillisecondsPerPixel = humanMoveMillisecondsPerPixel;
        HumanMovePixelsPerAdditionalStep = humanMovePixelsPerAdditionalStep;
        HumanMoveMaximumDriftPixels = humanMoveMaximumDriftPixels;
        HumanMoveDriftRatio = humanMoveDriftRatio;
        MovementCurveFactorMinimum = movementCurveFactorMinimum;
        MovementCurveFactorMaximum = movementCurveFactorMaximum;
        PostMoveClickMinimumDelay = postMoveClickMinimumDelay;
        PostMoveClickMaximumDelay = postMoveClickMaximumDelay;
    }

    public TimeSpan AntiDetectMinimumDelay { get; }

    public TimeSpan AntiDetectMaximumDelay { get; }

    public TimeSpan DelayJitterMaximum { get; }

    public TimeSpan SleepPollInterval { get; }

    public TimeSpan HumanMoveMinimumDuration { get; }

    public TimeSpan HumanMoveMaximumDuration { get; }

    public int HumanMoveMinimumSteps { get; }

    public int HumanMoveMaximumSteps { get; }

    public double HumanMoveMillisecondsPerPixel { get; }

    public double HumanMovePixelsPerAdditionalStep { get; }

    public double HumanMoveMaximumDriftPixels { get; }

    public double HumanMoveDriftRatio { get; }

    public double MovementCurveFactorMinimum { get; }

    public double MovementCurveFactorMaximum { get; }

    public TimeSpan PostMoveClickMinimumDelay { get; }

    public TimeSpan PostMoveClickMaximumDelay { get; }

    private static void ValidateNonNegative(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Duration cannot be negative.");
        }
    }

    private static void ValidatePositive(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Duration must be positive.");
        }
    }

    private static void ValidateRange(TimeSpan minimum, TimeSpan maximum, string parameterName)
    {
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Maximum must be greater than or equal to minimum.");
        }
    }

    private static void ValidateNonNegative(double value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }
    }
}
