using RunescapeClicker.Core;

namespace RunescapeClicker.Automation.Windows;

internal sealed class CoordinatePickerSession
{
    private bool _waitingForRelease = true;
    private bool _completed;

    public bool WaitingForRelease => _waitingForRelease;

    public bool TryArm(bool primaryButtonDown)
    {
        if (_completed || primaryButtonDown)
        {
            return false;
        }

        _waitingForRelease = false;
        return true;
    }

    public CoordinatePickerResult? TryCapture(ScreenPoint position)
    {
        if (_completed || _waitingForRelease)
        {
            return null;
        }

        _completed = true;
        return CoordinatePickerResult.Captured(position);
    }

    public CoordinatePickerResult Cancel()
    {
        _completed = true;
        return CoordinatePickerResult.Cancelled();
    }
}
