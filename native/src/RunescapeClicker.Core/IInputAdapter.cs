namespace RunescapeClicker.Core;

public interface IInputAdapter
{
    ValueTask<InputAdapterResult> ConnectAsync(CancellationToken cancellationToken);

    ValueTask<CursorLocationResult> GetCursorPositionAsync(CancellationToken cancellationToken);

    ValueTask<InputAdapterResult> MoveMouseAsync(ScreenPoint position, CancellationToken cancellationToken);

    ValueTask<InputAdapterResult> ClickMouseAsync(MouseButtonKind button, CancellationToken cancellationToken);

    ValueTask<InputAdapterResult> PressKeyAsync(KeyPressAction action, CancellationToken cancellationToken);
}

public enum InputFailureKind
{
    None = 0,
    BlockedByWindows = 1,
    ElevatedTarget = 2,
    CursorReadUnavailable = 3,
    PartialInjection = 4,
    Unknown = 5,
}

public readonly record struct InputAdapterResult(
    bool Succeeded,
    string? FailureMessage = null,
    InputFailureKind FailureKind = InputFailureKind.None)
{
    public static InputAdapterResult Success() => new(true);

    public static InputAdapterResult Failure(string message)
        => new(false, message, InputFailureKind.Unknown);

    public static InputAdapterResult Failure(InputFailureKind failureKind, string message)
        => new(false, message, failureKind);
}

public readonly record struct CursorLocationResult(
    bool Succeeded,
    ScreenPoint Position,
    string? FailureMessage = null,
    InputFailureKind FailureKind = InputFailureKind.None)
{
    public static CursorLocationResult Success(ScreenPoint position) => new(true, position);

    public static CursorLocationResult Failure(string message)
        => new(false, default, message, InputFailureKind.CursorReadUnavailable);

    public static CursorLocationResult Failure(InputFailureKind failureKind, string message)
        => new(false, default, message, failureKind);
}
