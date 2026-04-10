using RunescapeClicker.Core;

namespace RunescapeClicker.Automation.Windows;

public sealed record CoordinatePickerResult(
    CoordinatePickerOutcome Outcome,
    ScreenPoint? Position = null,
    string? Message = null)
{
    public static CoordinatePickerResult Captured(ScreenPoint position)
        => new(CoordinatePickerOutcome.Captured, position);

    public static CoordinatePickerResult Cancelled(string? message = null)
        => new(CoordinatePickerOutcome.Cancelled, null, message ?? "Mouse position capture cancelled.");

    public static CoordinatePickerResult Busy(string? message = null)
        => new(CoordinatePickerOutcome.Busy, null, message ?? "A coordinate picker session is already active.");
}

public enum CoordinatePickerOutcome
{
    Captured = 0,
    Cancelled = 1,
    Busy = 2,
}
