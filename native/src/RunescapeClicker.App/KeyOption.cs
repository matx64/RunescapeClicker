namespace RunescapeClicker.App;

public sealed record KeyOption(
    string DisplayLabel,
    ushort VirtualKey,
    ushort ScanCode,
    bool IsExtendedKey);
