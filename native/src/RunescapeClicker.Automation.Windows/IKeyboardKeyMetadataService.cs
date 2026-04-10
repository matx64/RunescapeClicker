using Windows.System;

namespace RunescapeClicker.Automation.Windows;

public interface IKeyboardKeyMetadataService
{
    bool TryCreate(VirtualKey key, out NormalizedKeyboardKey metadata);
}

public sealed record NormalizedKeyboardKey(
    string DisplayLabel,
    ushort VirtualKey,
    ushort ScanCode,
    bool IsExtendedKey);
