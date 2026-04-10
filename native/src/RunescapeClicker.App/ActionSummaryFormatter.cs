using System.Globalization;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

internal static class ActionSummaryFormatter
{
    public static string Format(AutomationAction action)
        => action switch
        {
            MouseClickAction mouseClickAction => string.Create(
                CultureInfo.InvariantCulture,
                $"{mouseClickAction.Button} Click on ({mouseClickAction.Position.X}, {mouseClickAction.Position.Y})"),
            KeyPressAction keyPressAction => $"Press {keyPressAction.DisplayLabel}",
            DelayAction delayAction => FormatDelay(delayAction.Duration),
            _ => action.GetType().Name,
        };

    private static string FormatDelay(TimeSpan duration)
    {
        var milliseconds = (long)Math.Round(duration.TotalMilliseconds);
        if (milliseconds >= 1000 && milliseconds % 1000 == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000}s Delay");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{milliseconds}ms Delay");
    }
}
