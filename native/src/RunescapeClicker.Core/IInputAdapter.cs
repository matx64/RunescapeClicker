namespace RunescapeClicker.Core;

public interface IInputAdapter
{
    ValueTask<InputAdapterResult> ConnectAsync(CancellationToken cancellationToken);

    ValueTask<CursorLocationResult> GetCursorPositionAsync(CancellationToken cancellationToken);

    ValueTask<InputAdapterResult> MoveMouseAsync(ScreenPoint position, CancellationToken cancellationToken);

    ValueTask<InputAdapterResult> ClickMouseAsync(MouseButtonKind button, CancellationToken cancellationToken);

    ValueTask<InputAdapterResult> PressKeyAsync(KeyPressAction action, CancellationToken cancellationToken);
}

public readonly record struct InputAdapterResult(bool Succeeded, string? FailureMessage = null)
{
    public static InputAdapterResult Success() => new(true);

    public static InputAdapterResult Failure(string message) => new(false, message);
}

public readonly record struct CursorLocationResult(bool Succeeded, ScreenPoint Position, string? FailureMessage = null)
{
    public static CursorLocationResult Success(ScreenPoint position) => new(true, position);

    public static CursorLocationResult Failure(string message) => new(false, default, message);
}
