using System.ComponentModel;
using System.Runtime.InteropServices;
using RunescapeClicker.Core;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RunescapeClicker.Automation.Windows;

internal interface IWin32Interop
{
    CursorLocationResult GetCursorPosition();

    InputAdapterResult SendMouseMove(ScreenPoint position);

    InputAdapterResult SendMouseClick(MouseButtonKind button);

    InputAdapterResult SendKeyPress(KeyPressAction action);

    bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    bool UnregisterHotKey(nint windowHandle, int id);
}

internal sealed class Win32Interop : IWin32Interop
{
    private const string SendInputFailurePrefix =
        "Windows blocked the injected input. Input injection may be blocked by UIPI, or the target window may be running elevated.";

    public CursorLocationResult GetCursorPosition()
    {
        if (!PInvoke.GetCursorPos(out var point))
        {
            return CursorLocationResult.Failure(
                InputFailureKind.CursorReadUnavailable,
                CreateWin32Message("Failed to read the current cursor position."));
        }

        return CursorLocationResult.Success(new ScreenPoint(point.X, point.Y));
    }

    public InputAdapterResult SendMouseMove(ScreenPoint position)
    {
        var virtualWidth = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        var virtualHeight = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            return InputAdapterResult.Failure(InputFailureKind.Unknown, "Windows reported an invalid virtual desktop size.");
        }

        var virtualLeft = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var virtualTop = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    dx = NormalizeAbsoluteCoordinate(position.X, virtualLeft, virtualWidth),
                    dy = NormalizeAbsoluteCoordinate(position.Y, virtualTop, virtualHeight),
                    dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE
                        | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE
                        | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK,
                },
            },
        };

        return SendInputs([input], "move the mouse");
    }

    public InputAdapterResult SendMouseClick(MouseButtonKind button)
    {
        var (down, up) = button switch
        {
            MouseButtonKind.Left => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP),
            MouseButtonKind.Right => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button."),
        };

        return SendInputs(
            [
                CreateMouseButtonInput(down),
                CreateMouseButtonInput(up),
            ],
            "click the mouse");
    }

    public InputAdapterResult SendKeyPress(KeyPressAction action)
    {
        var keyDownFlags = action.ScanCode != 0 ? KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE : (KEYBD_EVENT_FLAGS)0;
        if (action.IsExtendedKey)
        {
            keyDownFlags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        var keyUpFlags = keyDownFlags | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        return SendInputs(
            [
                CreateKeyboardInput(action, keyDownFlags),
                CreateKeyboardInput(action, keyUpFlags),
            ],
            $"press the key '{action.DisplayLabel}'");
    }

    public bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey)
        => PInvoke.RegisterHotKey(new(windowHandle), id, (HOT_KEY_MODIFIERS)modifiers, virtualKey);

    public bool UnregisterHotKey(nint windowHandle, int id)
        => PInvoke.UnregisterHotKey(new(windowHandle), id);

    private static INPUT CreateMouseButtonInput(MOUSE_EVENT_FLAGS flags)
        => new()
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                },
            },
        };

    private static INPUT CreateKeyboardInput(KeyPressAction action, KEYBD_EVENT_FLAGS flags)
        => new()
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)action.VirtualKey,
                    wScan = action.ScanCode,
                    dwFlags = flags,
                },
            },
        };

    private static int NormalizeAbsoluteCoordinate(int position, int origin, int length)
    {
        if (length <= 1)
        {
            return 0;
        }

        var clampedOffset = Math.Clamp(position - origin, 0, length - 1);
        return (int)Math.Round(clampedOffset * 65535d / (length - 1d));
    }

    private static InputAdapterResult SendInputs(INPUT[] inputs, string actionDescription)
    {
        var sent = PInvoke.SendInput(inputs, Marshal.SizeOf<INPUT>());
        if (sent == 0)
        {
            return CreateSendInputFailure(actionDescription);
        }

        if (sent != inputs.Length)
        {
            return InputAdapterResult.Failure(
                InputFailureKind.PartialInjection,
                $"Windows injected only {sent} of {inputs.Length} input events while trying to {actionDescription}. {SendInputFailurePrefix}");
        }

        return InputAdapterResult.Success();
    }

    private static InputAdapterResult CreateSendInputFailure(string actionDescription)
    {
        var lastError = Marshal.GetLastWin32Error();
        var message = lastError == 0
            ? $"Failed to {actionDescription}. {SendInputFailurePrefix}"
            : $"Failed to {actionDescription}. {SendInputFailurePrefix} Win32 error {lastError}: {new Win32Exception(lastError).Message}";
        var failureKind = lastError == 5
            ? InputFailureKind.ElevatedTarget
            : InputFailureKind.BlockedByWindows;
        return InputAdapterResult.Failure(failureKind, message);
    }

    private static string CreateWin32Message(string prefix)
    {
        var lastError = Marshal.GetLastWin32Error();
        return lastError == 0
            ? prefix
            : $"{prefix} Win32 error {lastError}: {new Win32Exception(lastError).Message}";
    }
}
