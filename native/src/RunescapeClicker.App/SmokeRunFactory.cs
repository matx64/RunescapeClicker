using RunescapeClicker.Core;

namespace RunescapeClicker.App;

public static class SmokeRunFactory
{
    public static RunRequest CreateSafeKeyboardRun()
        => new(
            [
                new DelayAction(TimeSpan.FromMilliseconds(900)),
                new KeyPressAction(virtualKey: 0x87, scanCode: 0x76, isExtendedKey: false, displayLabel: "F24"),
                new DelayAction(TimeSpan.FromMilliseconds(700)),
            ],
            StopCondition.HotkeyOnly,
            ExecutionProfile.Default);

    public static RunRequest CreateMouseSmokeRun(ScreenPoint position)
        => new(
            [
                new MouseClickAction(MouseButtonKind.Right, position),
                new DelayAction(TimeSpan.FromMilliseconds(1500)),
            ],
            StopCondition.Timer(TimeSpan.FromMilliseconds(900)),
            ExecutionProfile.Default);
}
