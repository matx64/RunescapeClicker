using RunescapeClicker.Automation.Windows;
using RunescapeClicker.Core;

namespace RunescapeClicker.App;

internal static class AppMessageFormatter
{
    public static string FormatHotkeyStatus(HotkeyRegistrationResult result)
    {
        if (result.Succeeded)
        {
            return "Global hotkeys registered: F1 captures the current cursor and F2 stops the active run.";
        }

        return result switch
        {
            { FailureKind: HotkeyRegistrationFailureKind.Collision, Hotkey: AutomationHotkey.CaptureCursor }
                => "Global hotkeys unavailable: F1 is already in use by another app. Close the conflicting app, then use Retry Hotkeys.",
            { FailureKind: HotkeyRegistrationFailureKind.Collision, Hotkey: AutomationHotkey.StopRun }
                => "Global hotkeys unavailable: F2 is already in use by another app. Close the conflicting app, then use Retry Hotkeys.",
            { FailureKind: HotkeyRegistrationFailureKind.Collision }
                => "Global hotkeys unavailable because another app is already using one of them. Close the conflicting app, then use Retry Hotkeys.",
            _ => "Global hotkeys could not be registered. Check for other keyboard tools, then use Retry Hotkeys.",
        };
    }

    public static string FormatHotkeyInfoBar(HotkeyRegistrationResult result)
        => result switch
        {
            { FailureKind: HotkeyRegistrationFailureKind.Collision, Hotkey: AutomationHotkey.CaptureCursor }
                => "F1 is already reserved by another app. Close the conflicting app, then try Retry Hotkeys.",
            { FailureKind: HotkeyRegistrationFailureKind.Collision, Hotkey: AutomationHotkey.StopRun }
                => "F2 is already reserved by another app. Close the conflicting app, then try Retry Hotkeys.",
            { FailureKind: HotkeyRegistrationFailureKind.Collision }
                => "Another app is already using one of the global hotkeys. Close it, then try Retry Hotkeys.",
            _ => "Runescape Clicker could not register its global hotkeys. Check Windows input tools, then try Retry Hotkeys.",
        };

    public static string FormatCursorCaptureFailure(CursorLocationResult result)
        => result.FailureKind switch
        {
            InputFailureKind.CursorReadUnavailable
                => "Windows could not read the current cursor position. Try again, or use Pick On Screen instead.",
            _ => "Runescape Clicker could not capture the current cursor position. Try again, or use Pick On Screen instead.",
        };

    public static string FormatCoordinatePickerCancellation(bool hadExistingCoordinate)
        => hadExistingCoordinate
            ? "Coordinate pick was cancelled. The previous point is still selected."
            : "Coordinate pick was cancelled. No coordinate was changed.";

    public static string FormatRunFault(EngineError error)
        => error.FailureKind switch
        {
            InputFailureKind.ElevatedTarget
                => "Windows blocked automated input to a higher-privilege window. Run both apps at the same privilege level and try again.",
            InputFailureKind.BlockedByWindows
                => "Windows blocked the automated input. If the target app is elevated, run both apps at the same privilege level and try again.",
            InputFailureKind.PartialInjection
                => "Windows only delivered part of the automated input. Stop the run, refocus the target window, and try again.",
            _ when error.Code == EngineErrorCode.InputAdapterUnavailable
                => "Runescape Clicker could not start its Windows input backend. Restart the app and try again.",
            _ when error.Code == EngineErrorCode.MouseMoveFailed
                => "Runescape Clicker could not move the mouse. Check the target window and try again.",
            _ when error.Code == EngineErrorCode.MouseClickFailed
                => "Runescape Clicker could not complete the mouse click. Check the target window and try again.",
            _ when error.Code == EngineErrorCode.KeyPressFailed
                => "Runescape Clicker could not send the key press. Check the target window and try again.",
            _ => "Runescape Clicker hit an input error. Check the live log for details and try again.",
        };
}
