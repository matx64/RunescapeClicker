namespace RunescapeClicker.Automation.Windows;

public enum HotkeyRegistrationFailureKind
{
    None = 0,
    Collision = 1,
    Unknown = 2,
}

public sealed record HotkeyRegistrationResult(
    bool Succeeded,
    string? FailureMessage = null,
    HotkeyRegistrationFailureKind FailureKind = HotkeyRegistrationFailureKind.None,
    AutomationHotkey? Hotkey = null)
{
    public static HotkeyRegistrationResult Success() => new(true);

    public static HotkeyRegistrationResult Failure(string message)
        => new(false, message, HotkeyRegistrationFailureKind.Unknown);

    public static HotkeyRegistrationResult Collision(AutomationHotkey hotkey, string message)
        => new(false, message, HotkeyRegistrationFailureKind.Collision, hotkey);
}
