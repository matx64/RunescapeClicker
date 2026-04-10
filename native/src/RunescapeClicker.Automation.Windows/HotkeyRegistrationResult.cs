namespace RunescapeClicker.Automation.Windows;

public sealed record HotkeyRegistrationResult(bool Succeeded, string? FailureMessage = null)
{
    public static HotkeyRegistrationResult Success() => new(true);

    public static HotkeyRegistrationResult Failure(string message) => new(false, message);
}
