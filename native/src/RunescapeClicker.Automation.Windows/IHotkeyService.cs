namespace RunescapeClicker.Automation.Windows;

public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    bool IsRegistered { get; }

    Task<HotkeyRegistrationResult> EnsureRegisteredAsync(CancellationToken cancellationToken);
}
