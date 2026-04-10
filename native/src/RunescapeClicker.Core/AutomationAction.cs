namespace RunescapeClicker.Core;

public abstract record AutomationAction;

public sealed record MouseClickAction : AutomationAction
{
    public MouseClickAction(MouseButtonKind button, ScreenPoint position)
    {
        Button = button;
        Position = position;
    }

    public MouseButtonKind Button { get; }

    public ScreenPoint Position { get; }
}

public sealed record KeyPressAction : AutomationAction
{
    public KeyPressAction(ushort virtualKey, ushort scanCode, bool isExtendedKey, string displayLabel)
    {
        if (virtualKey == 0 && scanCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey), "A key press must include a virtual key or scan code.");
        }

        if (string.IsNullOrWhiteSpace(displayLabel))
        {
            throw new ArgumentException("A key press display label is required.", nameof(displayLabel));
        }

        VirtualKey = virtualKey;
        ScanCode = scanCode;
        IsExtendedKey = isExtendedKey;
        DisplayLabel = displayLabel.Trim();
    }

    public ushort VirtualKey { get; }

    public ushort ScanCode { get; }

    public bool IsExtendedKey { get; }

    public string DisplayLabel { get; }
}

public sealed record DelayAction : AutomationAction
{
    public DelayAction(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Delay duration cannot be negative.");
        }

        Duration = duration;
    }

    public TimeSpan Duration { get; }
}
