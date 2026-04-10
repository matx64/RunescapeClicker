using RunescapeClicker.Core;

namespace RunescapeClicker.Automation.Windows;

public sealed class WindowsAutomationServices : IDisposable
{
    private readonly AutomationWindowHost _windowHost;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly CoordinatePickerService _coordinatePickerService;
    private bool _disposed;

    private WindowsAutomationServices(
        AutomationWindowHost windowHost,
        WindowsInputAdapter inputAdapter,
        GlobalHotkeyService hotkeyService,
        CoordinatePickerService coordinatePickerService)
    {
        _windowHost = windowHost;
        InputAdapter = inputAdapter;
        _hotkeyService = hotkeyService;
        _coordinatePickerService = coordinatePickerService;
    }

    public IInputAdapter InputAdapter { get; }

    public IHotkeyService HotkeyService => _hotkeyService;

    public ICoordinatePickerService CoordinatePickerService => _coordinatePickerService;

    public static WindowsAutomationServices CreateDefault()
    {
        var windowHost = new AutomationWindowHost();
        var interop = new Win32Interop();
        return new WindowsAutomationServices(
            windowHost,
            new WindowsInputAdapter(interop),
            new GlobalHotkeyService(windowHost, interop),
            new CoordinatePickerService(windowHost));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeyService.Dispose();
        _coordinatePickerService.Dispose();
        _windowHost.Dispose();
    }
}
