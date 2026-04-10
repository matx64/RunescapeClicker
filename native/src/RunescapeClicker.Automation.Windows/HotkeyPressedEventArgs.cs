namespace RunescapeClicker.Automation.Windows;

public sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(AutomationHotkey hotkey, DateTimeOffset occurredAt)
    {
        Hotkey = hotkey;
        OccurredAt = occurredAt;
    }

    public AutomationHotkey Hotkey { get; }

    public DateTimeOffset OccurredAt { get; }
}
