using RunescapeClicker.Core;

namespace RunescapeClicker.Automation.Windows;

public sealed class WindowsInputAdapter : IInputAdapter
{
    private readonly IWin32Interop _interop;

    public WindowsInputAdapter()
        : this(new Win32Interop())
    {
    }

    internal WindowsInputAdapter(IWin32Interop interop)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
    }

    public ValueTask<InputAdapterResult> ConnectAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(InputAdapterResult.Success());

    public ValueTask<CursorLocationResult> GetCursorPositionAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_interop.GetCursorPosition());

    public ValueTask<InputAdapterResult> MoveMouseAsync(ScreenPoint position, CancellationToken cancellationToken)
        => ValueTask.FromResult(_interop.SendMouseMove(position));

    public ValueTask<InputAdapterResult> ClickMouseAsync(MouseButtonKind button, CancellationToken cancellationToken)
        => ValueTask.FromResult(_interop.SendMouseClick(button));

    public ValueTask<InputAdapterResult> PressKeyAsync(KeyPressAction action, CancellationToken cancellationToken)
        => ValueTask.FromResult(_interop.SendKeyPress(action));
}
