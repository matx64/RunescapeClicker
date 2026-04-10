namespace RunescapeClicker.Automation.Windows;

public sealed class CoordinatePickerService : ICoordinatePickerService
{
    private readonly IAutomationWindowHost _windowHost;
    private int _activeSession;
    private bool _disposed;

    public CoordinatePickerService()
        : this(new AutomationWindowHost())
    {
    }

    internal CoordinatePickerService(IAutomationWindowHost windowHost)
    {
        _windowHost = windowHost ?? throw new ArgumentNullException(nameof(windowHost));
    }

    public async Task<CoordinatePickerResult> PickCoordinateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (Interlocked.Exchange(ref _activeSession, 1) == 1)
        {
            return CoordinatePickerResult.Busy();
        }

        try
        {
            await _windowHost.EnsureWindowHandleAsync(cancellationToken);
            return await _windowHost.ShowCoordinatePickerAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _activeSession, 0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
