namespace RunescapeClicker.Automation.Windows;

public sealed class GlobalHotkeyService : IHotkeyService
{
    private const int CaptureCursorHotkeyId = 0x1001;
    private const int StopRunHotkeyId = 0x1002;
    private const int WindowMessageHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyF1 = 0x70;
    private const uint VirtualKeyF2 = 0x71;

    private readonly IAutomationWindowHost _windowHost;
    private readonly IWin32Interop _interop;
    private bool _disposed;

    public GlobalHotkeyService()
        : this(new AutomationWindowHost(), new Win32Interop())
    {
    }

    internal GlobalHotkeyService(IAutomationWindowHost windowHost, IWin32Interop interop)
    {
        _windowHost = windowHost ?? throw new ArgumentNullException(nameof(windowHost));
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
        _windowHost.WindowMessageReceived += OnWindowMessageReceived;
    }

    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public bool IsRegistered { get; private set; }

    public async Task<HotkeyRegistrationResult> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (IsRegistered)
        {
            return HotkeyRegistrationResult.Success();
        }

        var handle = await _windowHost.EnsureWindowHandleAsync(cancellationToken);
        if (!_interop.RegisterHotKey(handle, CaptureCursorHotkeyId, ModNoRepeat, VirtualKeyF1))
        {
            return HotkeyRegistrationResult.Collision(
                AutomationHotkey.CaptureCursor,
                "Failed to register F1. Another application may already be using that hotkey.");
        }

        if (!_interop.RegisterHotKey(handle, StopRunHotkeyId, ModNoRepeat, VirtualKeyF2))
        {
            _interop.UnregisterHotKey(handle, CaptureCursorHotkeyId);
            return HotkeyRegistrationResult.Collision(
                AutomationHotkey.StopRun,
                "Failed to register F2. Another application may already be using that hotkey.");
        }

        IsRegistered = true;
        return HotkeyRegistrationResult.Success();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _windowHost.WindowMessageReceived -= OnWindowMessageReceived;

        if (IsRegistered)
        {
            try
            {
                var handle = _windowHost.EnsureWindowHandleAsync(CancellationToken.None).GetAwaiter().GetResult();
                _interop.UnregisterHotKey(handle, CaptureCursorHotkeyId);
                _interop.UnregisterHotKey(handle, StopRunHotkeyId);
            }
            finally
            {
                IsRegistered = false;
            }
        }
    }

    private void OnWindowMessageReceived(object? sender, WindowMessage message)
    {
        if (message.MessageId != WindowMessageHotkey)
        {
            return;
        }

        var hotkey = message.WParam switch
        {
            CaptureCursorHotkeyId => AutomationHotkey.CaptureCursor,
            StopRunHotkeyId => AutomationHotkey.StopRun,
            _ => (AutomationHotkey?)null,
        };

        if (hotkey is null)
        {
            return;
        }

        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(hotkey.Value, DateTimeOffset.UtcNow));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
