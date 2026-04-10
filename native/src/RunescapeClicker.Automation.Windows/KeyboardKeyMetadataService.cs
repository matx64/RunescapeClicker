using System.Globalization;
using Windows.System;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RunescapeClicker.Automation.Windows;

public sealed class KeyboardKeyMetadataService : IKeyboardKeyMetadataService
{
    public bool TryCreate(VirtualKey key, out NormalizedKeyboardKey metadata)
    {
        var virtualKey = (ushort)key;
        if (virtualKey == 0)
        {
            metadata = null!;
            return false;
        }

        var scanCodeEx = PInvoke.MapVirtualKey(virtualKey, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC_EX);
        if (scanCodeEx == 0)
        {
            metadata = null!;
            return false;
        }

        var isExtendedKey = (scanCodeEx & 0xFF00u) is 0xE000u or 0xE100u;
        var scanCode = (ushort)(scanCodeEx & 0xFFu);
        if (scanCode == 0)
        {
            metadata = null!;
            return false;
        }

        metadata = new NormalizedKeyboardKey(
            GetDisplayLabel(key, scanCode, isExtendedKey),
            virtualKey,
            scanCode,
            isExtendedKey);
        return true;
    }

    private static string GetDisplayLabel(VirtualKey key, ushort scanCode, bool isExtendedKey)
    {
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(char)('A' + ((int)key - (int)VirtualKey.A))}");
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{(char)('0' + ((int)key - (int)VirtualKey.Number0))}");
        }

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Esc",
            VirtualKey.Left => "Left Arrow",
            VirtualKey.Right => "Right Arrow",
            VirtualKey.Up => "Up Arrow",
            VirtualKey.Down => "Down Arrow",
            >= VirtualKey.F1 and <= VirtualKey.F24
                => string.Create(CultureInfo.InvariantCulture, $"F{((int)key - (int)VirtualKey.F1) + 1}"),
            _ => key.ToString(),
        };
    }
}
